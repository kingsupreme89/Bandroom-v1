# Bandroom Handoff — August 13, 2026 — Session 69

Same idea as always: what happened, explained plain.

## Rebuilt: Live Scorebug Overlay's Transparency, Now Actually Working (DirectComposition)

Session 68 shipped the live scorebug overlay (`ScorebugOverlayForm.cs`) using a WinForms
`TransparencyKey` color-key trick to make the window see-through everywhere except the bug
graphic. First live test this session showed it wasn't working at all — the whole window painted
as a solid opaque green box, positioned at the top of the screen instead of bottom-center like the
reference screenshots (`fox 21.webp`/`fox 25.webp`).

Chased this in stages, each one uncovering the next real cause:

1. **Position** — moved from top-anchored to bottom-anchored (`Top = screen.Bottom - Height -
   margin`), matching the reference shots.
2. **First transparency attempt (color-key against the theme's own green)** — didn't survive a
   zoom-factor scale (pixel resampling shifted the green off the exact key color, so nothing
   matched and it stayed solid).
3. **Root cause, finally confirmed**: `WinForms.TransparencyKey` color-keying **does not work
   against WebView2 content at all**, in either windowed-control form. WebView2 renders through
   its own hardware compositor straight to the DWM surface, bypassing the parent window's
   GDI-based layered-window color-key mechanism entirely. No amount of CSS/zoom tweaking was ever
   going to fix that — confirmed by owner decision to do the real fix rather than keep patching
   the wrong mechanism.
4. **Real fix**: rewrote `ScorebugOverlayForm` to host WebView2 via **visual hosting**
   (`CoreWebView2CompositionController` + a `Windows.UI.Composition` DirectComposition visual
   tree), composited onto the window through `WS_EX_NOREDIRECTIONBITMAP` instead of a GDI
   redirection surface — the officially-supported path for real per-pixel-alpha WebView2
   transparency. This required three additional pieces the CsWinRT projection doesn't hand you for
   free, each one a separate crash-log round-trip to nail down:
   - `Windows.UI.Composition.Compositor`'s constructor throws (`the caller must initialize
     DispatcherQueue on this thread`) unless a `Windows.System.DispatcherQueue` already exists on
     the calling thread — a plain WinForms thread doesn't have one. Added
     `DispatcherQueueHelper.EnsureOnCurrentThread()`, a small P/Invoke into
     `CoreMessaging.dll`'s `CreateDispatcherQueueController`, run once per process.
   - `Compositor.CreateDesktopWindowTarget` isn't a real method on the projected type — it's
     COM-interop-only via `ICompositorDesktopInterop`, and CsWinRT-projected objects don't support
     a plain C# cast to an arbitrary `[ComImport]` interface (that's a CLR type check, not a
     QueryInterface). Added the small `ICompositorDesktopInterop` shim + `.As<T>()` (CsWinRT's own
     extension method that does a real QueryInterface).
   - The interop method's return value (a `DesktopWindowTarget`) also can't be returned directly —
     legacy interop's `InterfaceMarshaler` doesn't know how to wrap a raw returned interface
     pointer into a CsWinRT-projected class. Declared it to return `IntPtr` instead and wrapped it
     manually with `WinRT.MarshalInterface<DesktopWindowTarget>.FromAbi`.
5. **Still green after all that** — because the real problem wasn't the hosting mechanism at all.
   Coffee's theme HTML deliberately marks its chroma-key CSS `!important`, with its own comment
   admitting exactly why: *"keyable; !important so a host stylesheet can't black it out"* — built
   so OBS-style consumers can't accidentally lose the key color, which also means it was actively
   designed to resist exactly what we were trying to do. Our injected override CSS was also
   `!important`, but landed via `AddScriptToExecuteOnDocumentCreatedAsync`, which runs *before* the
   theme's own `<style>` blocks parse — and when two `!important` rules of equal specificity
   target the same property, CSS breaks the tie by whichever is *later* in the document, so ours
   was always losing. Fixed by moving the injection to run from `NavigationCompleted` instead,
   guaranteeing it's the last `<style>` appended. Also added `#backdrop` to the override selector
   list — a separate 200vw×200vh fixed green div in the theme, independent of the `html`/`body`
   background, which explains why the green box was so much bigger than the bug graphic itself.
6. **Scrollbar** — dropping the WinForms `WebView2` control also dropped `ZoomFactor`, which is
   how the previous approach shrank the page to fit the window. `CoreWebView2CompositionController`
   has no equivalent. Now `Bounds` is kept at the theme's native authored canvas size (so WebView2's
   own page layout never overflows), and the DirectComposition root visual is scaled down
   afterward via its own `Scale` property to fit the actual (smaller) on-screen window — scaling
   the rendered visual instead of asking the page to lay out smaller.

**Status: not yet confirmed working live.** The last build (with all six fixes above) was running
when this handoff was written, but no live-game confirmation had come back yet — next session
should open with a live GAMETIME test before touching anything else here. `crash.log` in the debug
build output folder is the fastest way to check what actually happened if it's still not right —
every dead end this session showed up there first (WebView2-env conflicts, DispatcherQueue
denial, the two marshaling exceptions), well before any visual symptom made the cause obvious.

## Fixed: ESPN 2020 / NBC 2024 Thumbnail Images Were Swapped (Again)

Session 68's own note claimed this exact pair was already fixed ("matched by actual visual design
this time"), but a fresh look at Coffee's Corner's gallery this session showed `espn2020.png`
still showing NBC's peacock-logo design and `nbc2024.png` showing the plain scoreblock design —
backwards. Confirmed by opening both PNGs directly rather than trusting the file names, then
swapped the two files' contents in `Assets/ScoreboardReader/theme-library/thumbs/`. The underlying
theme HTML files themselves were always correctly labeled in `library.json` (confirmed by reading
each file's actual internal branding text/version string) — only the thumbnail *images* were ever
wrong.

## Investigated: Missing UCF Logo (Not a Bug)

Owner noticed UCF's crest wasn't showing in the live scorebug (Arizona showed fine, UCF fell back
to plain text). Checked `TeamLogo.FindImagePath`'s convention (`<TeamName>.png` dropped into
`TeamLogos/`) and confirmed there's simply no `UCF.png` in the live `UserData/TeamLogos/` folder
(128 logos present, no UCF) or the repo's shipped copy (89 logos, no UCF either). Also checked
whether Coffee's theme packages bundle any logos of their own that could be borrowed — they don't;
every theme HTML file only has blank `<img data-cfb27-bind="away.logo">`/`home.logo` slots, no
baked-in images at all. Nothing to fix in code — just a missing asset, same as most of the
~148-team roster not having a logo yet by design (see `TeamLogo.cs`'s own doc comment). Owner
still needs to supply a UCF crest file for `TeamLogos/UCF.png` (live + repo) whenever convenient.

## Known Gaps Carried Forward

- **Scorebug overlay's transparency/positioning/scrollbar fixes are unverified live** — see above,
  this is the top priority for whoever picks this up next.
- FOX 2021 still never shows live data (Coffee's own file has no live-data hook at all) —
  unchanged from Session 68, not fixable from BANDroom's side.
- UCF (and most of the roster) still has no logo file — owner-supplied asset, not a code task.
- The empty `AAC\`/`C-USA\`/`MAC\`/`Mountain_West\`/`Sun_Belt\`/`Independents\`/`PAC12\`
  subfolders under `TeamBackgrounds\` are still there (permission denied on cleanup back in
  Session 68) — harmless, safe to delete by hand whenever convenient.
