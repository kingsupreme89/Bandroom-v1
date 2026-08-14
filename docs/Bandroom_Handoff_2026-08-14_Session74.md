# Bandroom Handoff — August 14, 2026 — Session 74

Same idea as always: what happened, explained plain.

## Real Bug Found and Fixed: FOX 2025 Green Screen Was a Capture/Paint Race

Session 73 added the magenta chroma-key compositing approach (render the theme on a solid
key-color canvas, chroma-key the captured frame afterward) and specifically called out FOX 2025 as
a theme that gets stuck on a cached opaque-green GPU compositing layer from its own
loading-placeholder first paint. That session's fix was an opacity-toggle trick
(`ForceTransparentScript`) meant to force Chromium to allocate a fresh compositing layer every
render tick.

**Owner reported it was still green after that fix.** Root cause: `RenderTickAsync`
(`ScorebugOverlayForm.cs`) called `CapturePreviewAsync` immediately after `await
_core.ExecuteScriptAsync(ForceTransparentScript)` -- but `ExecuteScriptAsync` only waits for the
synchronous JS to finish *running*, not for the browser to actually *paint* the resulting frame.
The opacity toggle only *schedules* a repaint; on FOX 2025 specifically, the capture kept winning
that race often enough to still grab the stale opaque-green frame.

**Fixed:** `ForceTransparentScript` is now an explicit `Promise` that only resolves after two
nested `requestAnimationFrame` callbacks -- the standard "wait for a real committed paint" pattern
(first rAF fires before the frame with the opacity change is produced, second fires only after it's
actually composited). WebView2's `ExecuteScriptAsync` awaits a returned promise before resolving, so
`RenderTickAsync`'s capture now genuinely waits for a fresh painted frame. This isn't FOX-specific --
every theme goes through the same tick, so any other theme with a subtler version of the same race
is covered too.

**Still needs live confirmation:** launch against FOX 2025 live and visually confirm the fringe/green
is gone -- verified at the build level only this session.

## Reverted: Coffee's Corner UI (Owner Request — Wait For Coffee's App To Mature)

Session 73 imported a chunk of UI modeled on Coffee's own CFB27 Scoreboard Overlay app: a "Coffee's
Corner" sidebar nav item (scorebug skin gallery + reader connection status), a GAMETIME "Choose Your
Scorebug" skin-picker prompt, and a pill+arrows "Scorebug Skin" switcher in the matchup header
cycling ESPN/FOX/NBC/etc themes. The owner decided that was too much too soon -- wants Coffee's own
app to finish maturing before Bandroom builds further on top of it.

**Removed** (`wwwroot/index.html`, `app.js`, `style.css`):
- `#btn-coffees-corner` nav button and the `#coffees-corner-overlay` gallery/status panel.
- `#scorebug-skin-prompt-overlay` (the GAMETIME skin-picker prompt).
- `.scorebug-skin-switcher` (the ESPN/FOX/NBC pill+arrows + thumbnail switcher) and all its JS
  (`loadScorebugSkinSwitcher`, `renderScorebugSkinSwitcher`, `cycleScorebugSkin`, etc).

**Restored:** the original pill+arrows scorebug-LAYOUT switcher in the matchup header (adapted from
commit `fe20695`, the last commit before Session 73's absorption), wired to the untouched
`GetScorebugPresetsFromWeb`/`SetScorebugPresetFromWeb` bridge calls.

**Explicitly NOT touched:** the RAM/OCR score-and-possession merge-bug fix
(`GameStateNormalizer.cs`/`GameWatcher.cs`), the chroma-key rendering pipeline in
`ScorebugOverlayForm.cs` (including this session's paint-race fix above), and the backend bridge
methods `GetScorebugThemeGalleryFromWeb`/`ResolveActiveScorebugThemeFile`/`ConfigStore.
SaveScorebugSkinChoice` -- left in place since `ScorebugOverlayForm.cs`'s chroma-key renderer still
depends on `ResolveActiveScorebugThemeFile` internally, even with the web-facing gallery UI gone.

## Fixed: ESPN 2013 (Compact) Was Still Selectable Despite Being Non-Functional

Session 73's own handoff already flagged ESPN 2013 as "visual only, no live data" -- it renders but
never updates with a real score. Despite that, `ScorebugPreset.AllPresets` (`ScorebugPreset.cs:350`)
still included it, so the just-restored CFB27/CBS-v3 switcher was cycling through it as a fourth,
broken option. Owner caught this by eye ("i dont think that even works right").

**Fixed:** removed `Espn2013` from `AllPresets`. The preset definition itself (real OCR crop
calibration work) is left in the file, just unreferenced, in case a genuinely live ESPN broadcast
overlay shows up later to calibrate against instead. Switcher is back to exactly three working
options: Kam's CBS v3, College Football 27, College Football 26 Console.

## Also Discussed: What Remote Play Needs

No code change, just clarified for the owner: Remote Play mode (the "playing on console or
streaming/remote-play" toggle) skips the RAM reader entirely (it can only attach to a local PC game
process) and falls back to OCR against the Kam's CBS v3 layout specifically, since that's one of the
only two presets with calibrated crop coordinates. Requires: the toggle on, the video actually visible
in a capturable window (capture card / streaming client), showing the CBS-style scorebug, with that
window focused/foreground (OCR silently skips every tick otherwise -- confirmed live in
`ocr_debug.log` this session).

## Build & Run Status

- `dotnet build BandAudioHook.csproj -c Debug` and `-c Release` -- both **0 errors, 0 warnings**
  after every change this session.
- App was closed, rebuilt, and relaunched twice this session (once after the paint-race fix + UI
  revert, once again after the ESPN preset removal) so both sets of changes are live in the running
  process, not just compiled.
- No live game launched by any agent this session -- FOX fringe fix is code/build verified only.

## Git

Committed as `e2f29e9` ("Fix FOX chroma-key paint race, revert Coffee's Corner UI, drop non-live ESPN
preset"), 5 files changed (+81/-442, net shrink from removing the Coffee's Corner UI). Not yet
pushed to `origin/master` -- ask before pushing.

Three untracked script files (`scripts/measure_appdata.ps1`, `measure_clean_targets.ps1`,
`measure_disk_hogs.ps1`) were left alone -- unrelated to this session's work, not committed.

## Options Discussed, Not Started

- Live confirmation of the FOX fringe fix -- needs an actual game launch.
- Nothing else new; see Session 73's own "Options Discussed, Not Started" section for the
  longer-standing items (OCR confidence gating, manual-override fallback UI, live clock correction
  offset, NFL/other-ESPN theme sourcing) -- none of those were touched this session.
