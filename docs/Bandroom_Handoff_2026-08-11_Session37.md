# Bandroom Handoff — Session 37 (2026-08-11)

Picks up directly after Session 36 (nav-rack sidebar, Sound Booth redesign, the unresolved "LOCK
IN? renders as a plain native button" mystery) and Session 35's state-machine audit (the 4 new
discrepancies below). This session **resolved the Session 36 rendering mystery** (it was never a
caching bug) and fixed 2 of the 4 state-machine discrepancies from
`STATE_MACHINE_ANALYSIS_CORRECTED_2026-08-10.md` — the other 2 turned out to already be fixed in
current source, contradicting that document. Touched `wwwroot/style.css`, `wwwroot/index.html`,
`src/Bandroom.Core/Helpers/DefenseHelper.cs`, `src/Bandroom.Core/Helpers/BigEventHelper.cs`,
`src/Bandroom.Core/Helpers/TflHelper.cs`. **Nothing from this session is committed.**

## 1. THE LOCK IN? / Sound Booth mystery — SOLVED, was never a caching bug

Session 36 spent an entire session on live CDP investigation, ruling out every caching theory
(browser cache, WebView2-internal cache, stale on-disk file) and ended without a resolution. The
actual root cause was a **CSS comment that closes itself early**, sitting well upstream of both
symptoms:

`wwwroot/style.css` ~line 3329 (inside the Sound Booth Session-32 banner comment):
```css
/* ================================================================
   SOUND BOOTH PLUGIN RACK (Session 32) -- rotary knob, live IN/OUT
   meters, and a tab strip restyled to match a hardware effects-rack
   reference the owner shared, but themed entirely off the app's own
   --accent/--team-*/--glass-* vars (not the reference's pink/teal)
   ...
   ================================================================ */
```

`--team-*/--glass-*` contains a literal `*/`, which the CSS tokenizer reads as the comment's
actual closing marker — years early. Everything from that point to the next `{` (the
`.soundbooth-rack-head` selector line) became one long garbage selector prelude. Per CSS error
recovery, an invalid selector drops the *entire* qualified rule up to its matching `}` — so
`.soundbooth-rack-head`'s whole block was silently discarded by the parser on every load, byte-
identical file or not. This is exactly why Session 36's `md5sum`/cache-busting/full-cross-
navigation checks all came back clean: the file being served was correct; the file was never the
problem. **Confirmed via a whole-file brace/comment-balance scan** (155 `/*` vs 156 `*/` before
the fix — the single point where the count first went negative was this exact line; balanced
155/155 after).

Fixed: rewrote the comment to `--accent / --team- / --glass-` (no adjacent `*` `/`).

**Live-verified against the actual running app via CDP** (not just static reading):
`getComputedStyle('#btn-matchup')` now returns `appearance: "none"`, `borderRadius: "999px"`, a
team-tinted background — matching `.matchup-btn`'s source exactly, vs. Session 36's `"auto"` /
`"0px"` / plain gray. `getComputedStyle('.soundbooth-rack-head')` now returns `display: "flex"`
instead of the rule being entirely absent from the computed style.

**Why this also explains the .matchup-btn half of the mystery even though `.matchup-btn` itself
(line 3755) is well past the corrupted region**: the corrupted `.soundbooth-rack-head` rule was
one isolated drop, not a cascading failure — CSS parsing resumes cleanly at the next rule after an
invalid one. `.matchup-btn`'s own styling issue was likely the genuine WebView2-internal-cache
staleness Session 36's own comment (`wwwroot/index.html` ~line 6-14) already diagnosed and
partially addressed with the cache-busting `?v=<Date.now()>` query string — that fix was real and
is still in place; it just wasn't verifiable at the time because DevTools access was blocked and
the process kept getting replaced mid-investigation. With a stable CDP connection this session,
both are now confirmed actually applying.

## 2. Favicon 404 noise

`wwwroot/index.html` had no `<link rel="icon">`, so the browser auto-requested `/favicon.ico`
against the `appassets` virtual host every load and 404'd (`net::ERR_FILE_NOT_FOUND` in console).
Added `<link rel="icon" href="data:," />` (inert, no request) to `<head>`.

## 3. Ctrl+Shift+T test hook — confirmed genuinely working, not just correct-in-source

Session 36 explicitly flagged this as unconfirmed ("do not assume this is fixed either"). This
session verified it end-to-end against the live app using three escalating techniques, each closer
to a real user's actual input path than the last:
1. `document.dispatchEvent(new KeyboardEvent(...))` via CDP `Runtime.evaluate` — opened the panel.
2. CDP `Input.dispatchKeyEvent` (WebView2's real input pipeline, not a synthetic DOM event) —
   opened the panel, populated all 37 event options from `bridge.GetAllEventKeys()`.
3. **A real OS-level keypress**: brought the actual Bandroom window to the foreground via
   `SetForegroundWindow`/`ShowWindow` P/Invoke and sent literal `Ctrl+Shift+T` via
   `System.Windows.Forms.SendKeys.SendWait("^+t")` — panel opened, 37 options populated. This is
   the closest possible proxy to the owner physically pressing the keys.

No code changes were needed here — `openTestHook()`'s try/catch (Session 33/36) and the
`AreBrowserAcceleratorKeysEnabled = false` setting (`WebMainForm.cs`) are both correct and both
confirmed live. If the owner still can't trigger it by hand, the break is not in this app's
code — check OS-level: another app's global hotkey grabbing Ctrl+Shift+T, or the Bandroom window
not actually having focus at the moment of the keypress.

## 4. State-machine discrepancies (`STATE_MACHINE_ANALYSIS_CORRECTED_2026-08-10.md`) — 2 fixed, 2 already-fixed/stale

That document claimed 4 new discrepancies. Re-verified each against current source before touching
anything:

**#11 (TflHelper fires alongside DefenseHelper/BigEventHelper on the same TFL) — REAL, FIXED.**
`TflHelper` fired `"Defense: Tackle for Loss"` on any down-increase + yards-to-go-increase, with
no possession or down-value guard, while `DefenseHelper` (down 2/3) and `BigEventHelper` (down 4)
fire their own `"(Loss)"` cues on the identical signal. Since a down can only ever advance to 2, 3,
or 4 in normal play, `TflHelper`'s entire practical domain was already owned by the other two —
every real TFL fired two different cues at once, uncaught by `EventRouter.Dedupe` since the
EventKeys differ. Fixed: `TflHelper` now explicitly excludes downs 2/3/4, leaving it to fire only
outside the domain those two already cover (`src/Bandroom.Core/Helpers/TflHelper.cs`).

**#12 (split-tick loss detection gap) — REAL, FIXED.** `DefenseHelper` and `BigEventHelper` both
required the down region AND the yards-to-go region to update on the exact same OCR tick
(`Previous` vs `Current`, one tick apart). When the scorebug updates those two regions on separate
render frames — down changes on tick N, yards-to-go catches up on tick N+1 — neither evaluator's
condition was ever simultaneously true: tick N has stale yards-to-go, tick N+1 has an already-
equal down (the "did the down just change" guard excludes it). The loss silently never fired.
Fixed: both evaluators now remember the down and the yards-to-go baseline from the tick right
before a transition and keep comparing against that baseline for up to 3 subsequent ticks, instead
of requiring both fields to move together. Applies only to `DefenseHelper`'s two `(Loss)` branches
and `BigEventHelper`'s `Down == 4 (Loss)` branch — their other branches (turnover/NewPossession-
gated) were untouched.

**#13 (`penaltyagainst` missing from `EventGatedRegions`) — DOC IS STALE, no fix needed.**
`GameWatcher.cs:181` already has 5 entries including `penaltyagainst` (`situation`, `banner`,
`quarter`, `penaltyagainst`, `pregameready`), not the 3 the analysis doc claims. Whatever produced
that document's "598 vs 653 rules"-style count was wrong, or this was already fixed after that doc
was generated. No change made.

**#14 (dead kickoff EventKeys still in `AllEngineEventKeys`) — DOC IS STALE, no fix needed.**
`ConfigStore.cs`'s `AllEngineEventKeys` array does **not** contain `"Other: Kickoff on Kick
(Receiving)"` / `"(Kicking)"` — they only appear in `RetiredEventKeys` (the pruning set that
removes empty already-persisted rows without touching existing assignments), under a comment
already dated 2026-08-10. This is exactly the correct fix the doc says is missing; it just already
landed. No change made.

**Verification method** (not just re-reading source): wrote a throwaway console harness
referencing `Bandroom.Core.dll` directly and ran 12 scenarios against the real evaluator classes —
same-tick TFL into 2nd down (only `DefenseHelper` fires, not `TflHelper`), same-tick TFL into 4th
down (only `BigEventHelper` fires), the actual split-tick sequence (silent tick 1 → fires tick 2 →
does not re-fire tick 3), a normal 1st-down conversion (must stay silent), the user's own offense
taking a loss (must stay silent, `DefenseHelper` is defense-only), and the pending window expiring
after `MaxPendingTicks` so a stale read several ticks later doesn't fire a false positive. All 12
passed. Scratch project deleted after the run — it was never part of the shipped code.

## Verified this session
- `dotnet build BandAudioHook.csproj -c Debug` clean (0 warnings/errors) after every change.
- `.matchup-btn` and `.soundbooth-rack-head` computed styles confirmed live via CDP against the
  actual running app (see item 1) — not just "should work now," actually observed applying.
- Ctrl+Shift+T confirmed via real OS-level keypress against the actual running app (see item 3).
- The 12-scenario evaluator harness (see item 4) — all passing against the real `Bandroom.Core.dll`
  classes, not a re-read of the logic.

## Not yet confirmed — real next steps
1. **Live game verification** — everything above was verified via CDP/synthetic snapshots, not a
   real captured game. The state-machine fixes (#11/#12) and the CSS fix should be watched against
   an actual game/replay before considering them fully done. Session 36's item 6 (Sound Booth
   meters/Preview against a real game event) is still outstanding from before this session too.
2. **Volume persistence (Session 36 item 5)** — still not round-tripped (close app, reopen, check
   sliders kept their values). Untouched this session.
3. **Clipper song-list fixes + Add Songs button (Session 36 item 6)** — still not live-verified.
   Untouched this session.
4. Given items 1-3 above plus everything already shipped and building clean, this is a reasonable
   point to consider a rebuild+relaunch for the owner to eyeball the Sound Booth rack and LOCK IN?
   pill directly, now that the actual root cause is fixed rather than worked around.

## Carried forward from Session 36/35/34/33 (untouched this session)
1. Volume persistence round-trip — not yet confirmed (Session 36 item 5).
2. Clipper song-list fixes + Add Songs button — not yet live-verified (Session 36 item 6).
3. Sound Booth meters/Preview against a real game event — not yet live-verified (Session 33/34).
4. Conflict-prompt-before-autosave feature — not scoped, needs owner alignment (Session 34).
5. `voice_poc/.env` — still untracked, uncommitted, not gitignored; likely holds a secret.
6. **Not released** — commits sit on `master` past `v1.0.73` with no version bump/tag/Squirrel pack.
7. 3 deleted `guide/` files (Session 32) — still unexplained, still not touched.
8. Coverflow edge-fade mask + `.team-swatch-reflection` DOM wiring — CSS in place, JS side never
   wired up.
