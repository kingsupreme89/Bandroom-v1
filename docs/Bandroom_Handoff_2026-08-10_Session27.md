# Bandroom Handoff — Session 27 (2026-08-10) — RELEASED as v1.0.73

Picks up from the root `HANDOFF_2026-08-10.md` (Claude, bug-list pass, same day, earlier) --
that doc's two open items ("assigning a song doesn't save" for legacy Down cards, and two
un-deployed C# fixes) are both resolved and shipped by this session. Treat that file as
superseded by this one; it's left in place for the diff history but nothing in it is still
actionable.

## Starting point

Owner pasted an unstructured list of ~12 live bug reports spanning the clipper UI, the game-event
engine, and audio. Investigated and fixed in priority order (gameplay logic first, per owner
choice), then did a live UI sweep, then chased two "still broken after the fix" reports down to
their actual root causes using a technique worth keeping in the toolbox (see below).

## Fixes (all shipped in v1.0.73)

### Game-event engine
1. **Pick-six/fumble-return TD routed to the wrong team.** `TouchdownHelper` emits `"Defense:
   Touchdown Scored"` only when `Delta.NewPossession` is true -- i.e. GameState's possession has
   *already* flipped to the scoring team by the time the event fires. But
   `WebMainForm.OnEngineEventsDetected` flips every `"Defense:*"` key to the opposite side
   unconditionally (correct for every other Defense event, wrong for this one alone). Fixed by
   exempting that one key from the flip. `WebMainForm.cs`.
2. **1st/2nd/3rd/4th Down song assignments silently blanked on every relaunch.** Root cause was
   `ConfigStore.MigrateLegacyDownEvents`, called from `EnsureAllEvents` on *every* load despite its
   own doc comment claiming "one-time." Each run copied a legacy `down:1st`-style assignment into
   the canonical slot, then blanked the legacy card -- so assigning a song to the visible "1st
   Down" card looked like it never saved. `WebMainForm.FireEventForSide` already falls back to the
   legacy Trigger via `LegacyDownEventAlias` when the canonical slot is empty, so firing never
   depended on migration running at all. Removed the migration outright. `ConfigStore.cs`.
3. **"Away got 1st down" also fired a false "opponent drive started" cue.** `NewPossession` comes
   from a separate OCR color sample than `Down`; on the exact tick a first down is earned, the "1ST
   DOWN" banner can cover the possession-indicator region for one frame and misread `NewPossession`
   as true. Added a guard: `DriveStarterHelper` now skips when `Delta.WasFirstDown` is also true --
   the two should never both be true on a real tick. `src/Bandroom.Core/Helpers/DriveStarterHelper.cs`.
4. **Volume slider didn't apply live to a preview already playing.** `AudioPlayer.Play` captured
   `volume` once into a local at call time; previews now re-read `MasterVolume` every polling tick
   instead of replaying the frozen snapshot. Real game fires keep the snapshot on purpose (a cue
   shouldn't drift mid-play). `AudioPlayer.cs`.

### Clipper / assign-popup UI (`wwwroot/`)
5. Removed the "Load Conference Pack" pill and its handler (owner request -- simplifying the
   overwrite-confirm dialog).
6. Popups holding typed input (Load Profile confirm, Add-to-marketplace name dialog) no longer
   discard it on an outside click -- removed backdrop-click-to-close on both.
7. **Song list didn't visually collapse when the trim panel opened.** CSS specificity trap:
   `.clipper-assign-list { display: flex }` is an author-stylesheet rule with the same specificity
   as the browser's built-in `[hidden] { display: none }`, and author rules win ties. Added
   `.clipper-assign-list[hidden] { display: none; }` (the sibling `#clipper-assign[hidden]` rule
   already had this guard, this element didn't).
8. Category tabs (Offense/Defense/etc.) now show which one is selected -- there was no `.selected`
   state at all before.
9. Added a **Skip Event** button to the everyday Assign/Edit popup (previously only existed on the
   guided-wizard bar) -- closes without assigning, opens the next unassigned event in the category.

### Marketplace upload ("bad form data" / "check your connection")
10. **Share Profile always failed.** Root-caused by capturing the exact bytes .NET's
    `MultipartFormDataContent` sent (stood up a local echo server in place of the real worker,
    triggered the real upload via CDP -- see technique note below) and replaying that exact payload
    against the real `bandroom-marketplace.bandroom.workers.dev` worker with curl. Isolated the
    cause: .NET doesn't quote `name=`/`filename=` in `Content-Disposition` for plain alphanumeric
    field names (`name=type` instead of `name="type"`); the worker's multipart parser requires the
    RFC 7578 quoted form. curl's `-F` always quotes, which is why "the exact same field values"
    worked from curl but not the app. Added `WebBridge.AddFormPart`, forces quoted
    `Content-Disposition` on every part; used by both `ShareLocalTrackToMarketplace` and
    `ShareCurrentProfileToMarketplace`. Also added crash-log detail for the non-exception failure
    branch (HTTP status + response body), which had been silently swallowing the real reason on
    every failure before this -- that's what made this diagnosable at all on the second pass.
    `WebBridge.cs`.

## Technique note: driving the live app via CDP instead of guessing

WebView2 supports Chrome DevTools Protocol remote debugging via
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=PORT` before launching the exe. With
that set, `http://127.0.0.1:PORT/json` lists the page's `webSocketDebuggerUrl`, and a small Node
script (`ws` package) can attach, call `Runtime.enable`, and then:
- **Drive the UI headlessly** via `Runtime.evaluate` calling real app.js functions/DOM clicks
  directly (`document.getElementById(...).click()`, `openSaveProfileDialog()`, etc.) -- no mouse/
  keyboard automation needed.
- **Capture every uncaught exception and console.error** via the `Runtime.exceptionThrown` /
  `Runtime.consoleAPICalled` events, instead of asking the owner to open DevTools manually (which
  they didn't know how to do) or guessing blindly at static code.

This is how the two "still broken after your fix" follow-ups got resolved instead of another round
of blind guessing: a full CDP-driven sweep across every major UI surface (category tabs, assign
popup, Skip Event, trim controls, Auto-Assign, Save/Load/Share Profile, Sound Bank, My Downloads,
Help, command palette, whistle picker) turned up **zero JS exceptions** -- ruling out a JS crash as
the cause of the reported "Something went wrong rendering" toast (most likely explanation: it was
from a build before this session's fixes landed). The Share Profile failure specifically needed one
more step: standing up a local Node HTTP server, temporarily pointing `ShareCurrentProfileToMarketplace`'s
POST target at it for one diagnostic build, triggering the real upload via CDP, and diffing the
captured raw multipart bytes against a hand-built curl request that used identical field values.
Worth reusing this pattern (CDP probe + temporary local echo server) any time "the exact same
thing works from curl/Postman but not the app" comes up again.

## Deploy

Deployed to the installed `app-1.0.72` copy incrementally through the session (Release build +
`Bandroom.dll`/`Bandroom.Core.dll` + `wwwroot` copy, app fully closed each time) so each fix could
be verified live before moving on. Confirmed live by the owner: down-save fix, Share Profile fix.
Confirmed live by this session's own CDP probing: TD routing (via crash-log absence of new
failures), UI sweep (zero exceptions), Share Profile (byte-level replay against the real worker).

Session ended with a full `release.ps1` run: committed pending work (including previously-untracked
`guide/`, `voice_poc/`, and two earlier handoff docs -- see gitignore note below), pushed `master`,
built Release, packed with Squirrel, tagged, and published. **Live as `v1.0.73`:**
https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.73. Existing installs get the delta
update automatically on next launch.

**One fix before running the release script:** `.claude/worktrees/` (2.8GB, a separate agent
session's own nested git clone) was sitting untracked in the repo. `release.ps1`'s step 0 does
`git add -A`, which would have swept that whole thing into the commit and pushed it to GitHub.
Added `.claude/worktrees/` to `.gitignore` before running -- worth keeping there permanently, any
future worktree session would hit the same trap otherwise.

## Still open

- **Audio effects (reverb/crowd presets) not clearly audible** -- owner wants "2 real states: big
  game with deeper drums and more crowd, vs. none for other schools," reports currently only the
  "punchy drums" (transient shaper?) is audible. Traced the DSP chain in `AudioPlayer.cs` -- reads
  correctly, presets are real and wired (see `ReverbProvider.cs`/`ReverbPresets`,
  `CrowdBusService.cs`). `CrowdBusService.cs` itself notes the crowd-ambience bed is "fully wired
  but inert -- no bundled crowd-loop asset ships," which is a real, known gap: the crowd side of
  "more crowd effect" has no audio to play regardless of preset. Owner said they'd check this live
  during an actual game rather than have it chased further blind -- revisit if it's still an issue,
  starting with confirming whether a crowd-loop asset actually exists on disk.
- **`src/Bandroom.Mac/MacWebBridge.cs`** has the same `MultipartFormDataContent` pattern as the
  fixed `WebBridge.cs` and almost certainly the same unquoted-`Content-Disposition` bug. Not
  touched this session (separate, unverified build). Apply the same `AddFormPart`-style fix there
  if/when the Mac port's marketplace sharing is actually exercised.
- **Trim-preview pill / "clipper bugs after saving a trim"** -- owner confirmed the trimmer works
  now (unclear which specific fix from this session's UI batch resolved it, or whether it was
  already-deployed-but-untested code from before this session). No further action unless it
  resurfaces.
