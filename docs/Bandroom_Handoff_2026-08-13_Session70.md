# Bandroom Handoff — August 13, 2026 — Session 70

Same idea as always: what happened, explained plain.

## Rebuilt (again): Scorebug Overlay Transparency — Root Cause Finally Found, New Approach Shipped

Session 69 shipped a WebView2 "visual hosting" rewrite (CoreWebView2CompositionController +
Windows.UI.Composition DirectComposition tree via WS_EX_NOREDIRECTIONBITMAP) that looked correct
on paper and matched Microsoft's own sample pattern, but the handoff flagged it as "not yet
confirmed working live." First live test this session showed it was still solid opaque green,
identical to before. Chased it in stages:

1. **First real bug found**: `ScorebugOverlayForm` is reused across watch sessions
   (`RefreshForCurrentSkin` re-runs on every watch-start), but the old code called the
   composition setup (`CreateDesktopWindowTarget`) unconditionally on every call. A given HWND
   can only have that called on it ONCE, ever -- the second call threw
   `DCOMPOSITION_ERROR_WINDOW_ALREADY_COMPOSED`, which silently killed the WebView2 browser
   process (`crash.log` showed `BrowserProcessExited` immediately after), leaving the window
   rendering in a broken fallback state. Fixed by splitting one-time composition setup from
   per-refresh navigation -- but this alone didn't fix the green screen, just a *different*
   green screen (a fresh, never-before-composed window was still green).
2. **Went deep with live diagnostics** (temporary `CrashLog.Write` calls at each stage, since
   there's no way to attach a live debugger to this): confirmed via `getComputedStyle` that the
   page's own CSS genuinely *was* transparent (`rgba(0,0,0,0)` on html/body) and that
   `DefaultBackgroundColor`'s value genuinely stuck (`readback: A=0`). Despite both being
   provably correct, the screen still showed solid green. Conclusion: the DirectComposition/
   `Windows.UI.Composition`-hosted WebView2 surface itself never carries a real alpha channel in
   this composition mode on this machine -- a known rough edge of that specific hosting path,
   not fixable from the CSS/JS side no matter how correct the page is.
3. **Owner decision**: rather than keep fighting DWM compositing, rewrite around it entirely.
4. **New architecture (this session, replaces the DirectComposition approach completely)**:
   WebView2 now renders normally (fully opaque, standard windowed control) into an off-screen
   helper window positioned at (-32000,-32000) -- never visible, but a real HWND so the browser
   paints normally. Every ~130ms: push live data, force-reassert transparency (see below), then
   capture the page as a PNG via `CoreWebView2.CapturePreviewAsync` (which *does* reliably
   preserve real per-pixel alpha -- a completely different, more reliable code path from live
   DWM compositing), and paint that bitmap onto the actual on-screen window via classic GDI
   `UpdateLayeredWindow` (`WS_EX_LAYERED`), which has supported real per-pixel alpha since
   Windows 2000. New file: `NativeLayeredWindow.Paint` (the small P/Invoke wrapper around
   `CreateDIBSection`/`SelectObject`/`UpdateLayeredWindow`). Trade-off: ~8fps capture-cadence
   redraws instead of live-synced compositing -- imperceptible for a mostly-static scorebug.
5. **Three more real bugs found and fixed while building this out**, each confirmed via the same
   live-diagnostic-then-fix loop (no exceptions anywhere in the pipeline for any of these --
   every one was a silent logic bug, not a crash):
   - **Alpha corruption**: `CompositingMode.SourceCopy` was set (for "efficiency") when drawing
     the captured frame onto the premultiplied target bitmap. That skips GDI+'s
     straight-to-premultiplied alpha conversion entirely, silently producing a technically
     error-free pipeline that still painted solid opaque. Fixed by using the default
     `SourceOver` instead.
   - **Theme-specific stuck layer (FOX 2025 only, still unresolved -- see Known Gaps)**: some
     themes are "bundler-exported" (a loading placeholder shows first, real content swaps in
     async). That swap can reset the page's own inline style and defeat a one-time
     transparency override. Fixed for most themes with a per-tick reinforcement script instead
     of a one-time injection, plus a forced-opacity-toggle trick (a known Chromium technique for
     un-sticking a cached opaque compositing layer -- toggling `opacity` off out of `1` always
     allocates a fresh layer). **This fixed NBC 2024 / NBC 2024 Monochrome but did NOT fix FOX
     2025** -- see Known Gaps.
   - **First-navigation race**: even with the per-tick fix, a theme's *first* navigation after a
     fresh page load sometimes still lost the timing race against Chromium's layer promotion,
     while the exact same theme's *second* navigation (a plain skin refresh) came up perfectly
     transparent every time observed. Fixed by automatically firing one silent re-navigation
     ~900ms after the first navigation completes, every time a skin loads (`_pendingWarmupRenavigate`
     in `ScorebugOverlayForm.cs`) -- a fresh page load gets a fresh first-paint, and the
     per-tick script gets a full new attempt before anything gets captured.
   - **White edge fringe**: the captured PNG (straight, non-premultiplied alpha) was being
     scaled down with bilinear interpolation *before* premultiplying. Transparent pixels in a
     straight-alpha PNG still carry arbitrary RGB (often white, a PNG-encoder default) --
     interpolating that in produces a visible white halo at every edge, weighted purely by
     spatial distance with no awareness of near-zero alpha. Fixed by premultiplying at native
     size FIRST (no scaling, so garbage RGB in transparent regions gets zeroed out), THEN
     scaling the already-premultiplied bitmap down -- interpolating premultiplied data is
     exactly what premultiplied alpha exists to make safe.

**Status: confirmed working live for NBC 2024** (real transparency, real live data, positioned
correctly, no green, no white border after the last fix) -- owner-confirmed via live screenshots
this session, including one showing genuinely correct real-time data (`UNLV 2-0` vs
`Notre Dame 0-1`, real logo, real 0:00 pregame clock) with a clean transparent background.
**FOX 2025 is still broken** -- see Known Gaps, this is the #1 priority for whoever picks this up
next. The white-border fix (the very last change made this session) has NOT yet been confirmed
live -- next session should open with a live check on NBC 2024/Monochrome before anything else.

Diagnostic logging note: several `CrashLog.Write(..., new Exception("diagnostic"))` calls were
added this session purely to see what was happening live (no debugger access) -- these are still
in the code (`InitAsync started`, `NavigationCompleted fired`, first render tick stats, first 5
live-data pushes). Harmless (low volume, capped counters) but worth stripping once FOX 2025 is
also confirmed fixed and this class of bug is done being actively chased.

## Fixed: RAM Reader Orphan/Timing Issues

Two related bugs found while testing the above (repeatedly killing/rebuilding BANDroom during
the session kept producing a confusing "RAM mode isn't reading anything" symptom that turned out
to be process-lifecycle bugs, not a RAM-reading bug):

1. **RAM reader only ever launched from Start Watching/GAMETIME**, meaning every fresh app
   launch needed an explicit click before live data worked at all -- easy to misdiagnose as
   broken when it just hadn't started yet. Now auto-launches on app startup (`WebMainForm.cs`
   `Load` handler) instead, still gated behind the same opt-in RAM-mode + anti-cheat-safety
   flags as before -- this only moved *when* it launches, not the underlying risk decision.
2. **Orphaned RAM reader processes**: killing BANDroom uncleanly (force-quit, debugger stop, a
   crash) could leave `CollegeFB27RamReader.exe` running as an orphan, writing
   `"message":"RAM service: parent scorebug app closed"` into its own status file instead of
   live data -- looks identical to "RAM mode is broken" from GameWatcher's side. Fixed in
   `ScoreboardReaderHost.TryStartRamReader` -- now clears out any existing process with that
   exact name before launching a fresh one (safe since exactly one should ever exist at a time,
   always launched by us).

Owner also explicitly opted IN to RAM mode this session (previously off by default for
anti-cheat-safety reasons, per Session 68/69's own decision) -- confirmed and accepted the risk
directly when asked. `scoreboard_reader_ram_enabled.txt` is now `true`.

## Note: Cline Is Also Active On This Codebase

Confirmed via a live screenshot this session -- the owner runs Cline (a separate AI agent) in
the same VS Code window, working the existing TASK_BOARD.md admin-task backlog concurrently.
Cline has read the two most recent handoffs and is deliberately staying out of anything this
session touched (`ScorebugOverlayForm.cs`, `WebMainForm.cs`, `ScoreboardReaderHost.cs`). No
conflicts observed this session, but worth knowing going in -- check `git status` for
concurrent changes before assuming a clean starting state next session.

## Known Gaps Carried Forward

- **FOX 2025 still shows solid green, unresolved.** Unlike NBC 2024, its capture came back
  byte-for-byte IDENTICAL across every single tick this session, meaning the per-tick
  reinforcement script + opacity-toggle trick that fixed NBC 2024 never unstuck it even once --
  not a timing race, something more persistent. Next step probably needs to either find FOX
  2025's own bundler "ready" signal and wait for it before doing anything, or intercept/patch
  the bundled script's own paint calls directly rather than fighting them after the fact from
  outside. Owner's stated plan: use NBC 2024 in the meantime (confirmed working), come back to
  FOX 2025 separately.
- **White-border fix unconfirmed live** -- see above, first thing to check next session.
- **Diagnostic CrashLog calls still in ScorebugOverlayForm.cs** -- harmless but should be
  stripped once both open items above are closed out.
- FOX 2021 still never shows live data (Coffee's own file has no live-data hook at all) --
  unchanged from Session 68/69, not fixable from BANDroom's side.
- UCF (and most of the roster) still has no logo file -- owner-supplied asset, not a code task
  (unchanged from Session 69).
- The empty `AAC\`/`C-USA\`/`MAC\`/`Mountain_West\`/`Sun_Belt\`/`Independents\`/`PAC12\`
  subfolders under `TeamBackgrounds\` are still there (unchanged from Session 68/69) -- harmless.
