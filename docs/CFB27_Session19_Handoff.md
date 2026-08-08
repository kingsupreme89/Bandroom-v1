# Supreme's Stadium Sound Selector — Session 19 Handoff

## Where this picks up from
Session 17 built the working GUI prototype (down detection + trigger grid +
keyboard hook), confirmed live against the game but with the crop region
mis-calibrated (calibrated against a pause-menu screenshot, not live
gameplay). **This session (19) fixed that and added a large batch of
features on top.** Project location, unchanged:
`D:\Claude\Projects\tools\BandAudioHook\`

A separate design-focused handoff was also written this session for whoever
picks up the *visual* polish pass: [`CFB27_Session18_DesignHandoff.md`](CFB27_Session18_DesignHandoff.md)
in the same folder. Read that one too if the next session is about UI/UX,
not features.

---

## What's confirmed working (tested live against the actual running game this session)
- **Down detection** — crop region recalibrated against a REAL live-gameplay
  screenshot (`GameWatcher.cs`, fractions `0.65/0.85/0.14/0.09`). User
  confirmed this fixed the original bug (worked on pause menu, not in-game).
- **Double-fire / flicker bug** — fixed via a 2-second cooldown per region
  (`GameWatcher.Cooldown`, now user-tunable). User confirmed this resolved
  the "playing double sound after a few seconds" issue.
- **Fade-out** — tuned live per user feedback: currently starts at 9s, ramps
  over 4.5s (both now tunable in Settings, see below).
- **Reverb** — three presets (Stadium/Dome/Night Game), custom Freeverb-style
  DSP implementation (`ReverbProvider.cs`) since NAudio has no built-in
  reverb. Built and wired this session; **user has not yet confirmed how it
  actually sounds** — that's the top thing to check in on next session if
  they haven't already reported back.
- **Profile save/load** (`ConfigStore.cs` `SaveProfile`/`LoadProfile`/etc.)
  — team-specific config save slots, replacing an earlier raw file-export
  approach at the user's explicit request ("just theme the team page UIs...
  can we just have a save feature... save their own configurations for a
  specific team").
- **Trimmer** (`TrimmerForm.cs`) — per-row "Trim..." button, preview +
  save-as-new-clip via `OffsetSampleProvider` + `WaveFileWriter.CreateWaveFile16`.
- **Stop Playback** — global stop button, `AudioPlayer.StopAll()` tracks
  active `WaveOutEvent`s in a static list.

## Built but NOT yet confirmed by the user (build succeeded, process launches
and stays responsive, but no click-through confirmation happened this
session for these specific items)
- Settings dialog (pre-roll/fade-in/fade-out/cooldown all now live-tunable,
  `SettingsForm.cs`)
- Search/filter box, assigned-song indicator coloring, "Clear All" button
- Compact Mode toggle
- System tray icon + minimize-to-tray (uses `SystemIcons.Application` as a
  placeholder — no custom app icon exists yet)
- Crash logging to `crash.log` next to the exe (`CrashLog.cs`, wired into
  `Program.cs` unhandled-exception handlers + `GameWatcher`/`AudioPlayer`
  catch blocks)
- Fade-in on start (previously only fade-out existed)

**Next session should have the user click through all of the above and
report back — this list is the actual to-do for "verify it works."**

---

## Explicitly deferred / needs something this session couldn't produce
From the "30 more suggestions" round, these are real gaps, not overlooked:
- **App icon art** — still using default .NET icon everywhere including the
  tray icon
- **Installer / self-contained publish** — app still requires `dotnet build`
  to run; not distributable as-is despite user's stated ambition ("were
  gonna be on cnn seriously")
- **Song pack manifest/import system** — user said this session "im gonna
  also get a base little pack of sample situation songs we can name etc for
  people to use" — **waiting on the user to actually produce/hand over that
  pack** before a manifest format can be designed around real files
- **In-app scoreboard calibration UI** (drag-to-select regions, save as
  named profile) — real sub-project, not started
- **Team background images** — user has 1920x1080 art inside a Frosty mod
  (`Spyda's Backdrops_2.0.fbmod`, path:
  `C:\Games\Mod Folder\CFB Mods\Mods\Spyda's Backdrops_2.0.fbmod`). This is
  a packed Frosty binary format (`FROSTY` magic header), NOT a zip —
  attempting to parse it directly was explicitly avoided this session
  (previous sessions burned 6 sessions on exactly this kind of Frosty
  binary-format rabbit hole). **The user needs to export the images
  THROUGH Frosty itself** (open the mod, browse to the texture asset,
  right-click → Export) — see Session 18's handoff or just re-explain this
  if asked again. Nothing is blocked on my end here; it's blocked on the
  user doing that export.
- **Flag/penalty region calibration** — `GameWatcher.cs` has a `"flag"`
  `WatchedRegion` scaffolded but with `FxW=0/FxH=0` (uncalibrated, so
  skipped). Needs a live screenshot of an actual penalty-flag banner during
  a real game to calibrate, same process as was done for the down region
  this session (see below).

---

## How the down-region calibration was actually done this session
(Useful precedent if the flag region needs the same treatment next time.)
1. Got `request_access` to `Collegefb27.exe` (exact bundle id resolved as
   `c:\games\ea sports college football 27\collegefb27.exe` — computer-use
   screenshot tooling CAN see the actual game, unlike the custom-built
   BandAudioHook exe).
2. Took a live screenshot DURING actual gameplay (not the pause menu — that
   was the original bug).
3. Used `zoom` on the score-bug region to get precise pixel bounds.
4. Cross-referenced against the REAL window rect via a PowerShell P/Invoke
   snippet (`GetWindowRect`) to get the actual `2560x1440` resolution, since
   the computer-use screenshot image is downscaled (~1456x819) relative to
   the real screen — needed the scale factor (~1.758x) to convert screenshot
   pixel coordinates into accurate fractional crop coordinates.
5. Wrote the fractions into `GameWatcher.cs`, rebuilt, relaunched, had the
   user confirm live.

---

## Architecture additions this session (files new or meaningfully changed)
| File | What changed |
|---|---|
| `GameWatcher.cs` | Multi-region watcher (was down-only); added cooldown/debounce; added raw-OCR-read logging; `Cooldown` now mutable |
| `AudioPlayer.cs` | Added fade-in, made all timing constants mutable static fields, added `StopAll()`/active-output tracking, wired in reverb pipeline, routed exceptions to `CrashLog` |
| `ReverbProvider.cs` | New — hand-written Freeverb-style stereo reverb (`ISampleProvider`), 3 presets in `ReverbPresets` |
| `ConfigStore.cs` | Added named profile save/load/list/delete (`ProfilesFolder`) |
| `TrimmerForm.cs` | New — trim/preview/save-clip dialog |
| `PromptDialog.cs` | New — small reusable text-input modal (used for profile naming) |
| `Theme.cs` | New — centralized dark charcoal palette + `StyleGrid`/`StyleButton` helpers |
| `GlassButton.cs` | New — custom-drawn gradient/rounded button (explicitly a visual approximation, NOT real backdrop blur — WinForms can't do that) |
| `SettingsForm.cs` | New — exposes previously-hardcoded timing constants |
| `CrashLog.cs` | New — writes to `crash.log` next to the exe |
| `Program.cs` | Added `AppDomain.UnhandledException`/`Application.ThreadException` handlers; switched DPI mode to `PerMonitorV2` (was `SystemAware` — fixes a real coordinate-drift bug on scaled displays) |
| `MainForm.cs` | Extensively grown: profile UI, volume slider, reverb dropdown, search/filter, assigned-indicator, Clear All, tray icon, Compact Mode |

---

## Working-relationship notes (carried forward + new)
- [[feedback_handoff_at_375k_context]] — user asked for this handoff
  directly ("give me a handoff for another chat") — standard pattern,
  honored immediately.
- [[feedback_show_terminal_activity]] / [[feedback_act_autonomously_on_technical_steps]] —
  held throughout; every build/relaunch shown via real `dotnet build` /
  `tasklist` output; investigative steps (locating the actual project after
  an initial mix-up with an unrelated Google-AI-Studio screenshot, digging
  through prior handoffs, live screenshot calibration) taken without pausing
  for permission.
- **Important correction from earlier in this session**: at the very start,
  screenshots the user shared showed a totally different, unrelated app
  ("Dynasty Companion" / "DynastyOS", browser/Google-AI-Studio-looking UI)
  and I initially assumed that WAS the project to work on, wasting a round
  of clarifying questions. The user then pasted an actual Session 17
  handoff pointing at the REAL project (`BandAudioHook`). **If screenshots
  and session context disagree about which app is being discussed, trust
  the handoff docs / actual project files over a pasted screenshot** — the
  user may be showing something unrelated (e.g. someone else's Discord post,
  a different tool entirely) without meaning it as "this is the project."
- User is in full "vibe coding" mode this session — extremely fast,
  overlapping requests (multiple new asks arrive mid-tool-call constantly),
  high energy, comfortable with rapid feature pivots, expects forward
  motion. Pattern held all session: don't stop to over-confirm minor asks,
  just build and report what shipped.
- User explicitly confirmed two real bugs got fixed based on their own live
  testing (down detection now works in real gameplay; double-sound issue
  gone) — both are genuinely validated, not just "should work" claims.
- User's ambition is explicitly a real public release ("cnn seriously",
  wants a video/YouTube feature showcase — a feature-list PDF was generated
  this session at `D:\Claude\Projects\Supreme_Stadium_Sound_Selector_Feature_List.pdf`
  for that purpose, built via a throwaway QuestPDF .NET console project
  since **this machine has no Python installed** — worth remembering for any
  future PDF/document-generation need on this machine, don't assume
  Python/reportlab is available.
- Computer-use screenshot/click limitation still applies: **the custom
  BandAudioHook exe is invisible to computer-use screenshot tooling**
  (only apps from the fixed installed-apps catalog render) — the user is
  still the only one who can visually verify the app's own GUI. The actual
  GAME (`Collegefb27.exe`) IS visible/accessible via computer-use, which is
  how the down-region recalibration screenshot was obtained this session.

---

## File state (end of session 19)
| File/Item | Status |
|---|---|
| All `.cs` files in `BandAudioHook\` | Compile cleanly, 0 warnings/0 errors as of last build this session |
| App process | Was running and responding at end of session (relaunched multiple times through the session as features landed) |
| `crash.log` | Will appear next to the exe if anything throws — check it first if something seems broken next session |
| `Supreme_Stadium_Sound_Selector_Feature_List.pdf` | Generated and delivered to user this session, saved at `D:\Claude\Projects\` |
| `CFB27_Session18_DesignHandoff.md` | Separate handoff for a design-focused session — still fully valid, nothing in it has been invalidated by this session's feature work |
