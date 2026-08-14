# Bandroom Handoff — August 14, 2026 — Session 75

Same idea as always: what happened, explained plain.

## Released v1.1.6

Ran the full `release.ps1` pipeline (commit + push, patch bump, build, Squirrel pack, tag, publish)
to ship the FOX chroma-key paint-race fix, the Coffee's Corner UI revert, and the ESPN 2013 preset
removal from Session 74. `git` wasn't on PATH in the PowerShell session used to invoke the script
(`C:\Program Files\Git\cmd` / `mingw64\bin` had to be added manually) -- worth fixing in the
PowerShell profile if this keeps coming up.

Result: **v1.1.6 is live** at
https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.6. Existing installs auto-update on
next launch; new users get `BandroomSetup.exe`. Also swept up 4 previously-untracked
`scripts/measure_*.ps1` files into the commit (owner's own disk-usage scripts, unrelated to app
code, just hadn't been committed yet).

## Mac App: Assessed Current State, Verified It Still Builds

Owner's stated mission is really the Mac app now, with remote play (OCR-based, no RAM reader) as
the shared foundation both platforms depend on. Investigated `src/Bandroom.Mac`:

- It's an **Avalonia (.NET) app**, not a native Swift/Xcode rewrite -- cross-compiles ~19 shared
  C# files from the Windows root (`ConfigStore.cs`, `TeamColors.cs`, `EventHistoryFeed.cs`,
  `CloudDatabaseService.cs`, `GoogleAuthService.cs`, etc.) plus references `Bandroom.Core` (the
  platform-agnostic rule engine: `GameState`, `EventRouter`, `PlaySnapshot`, ~20 rule
  `Helpers/*.cs`).
- **Confirmed builds clean** against today's Session 74 changes: `dotnet build
  src/Bandroom.Mac/Bandroom.Mac.csproj -c Debug` -- 0 warnings, 0 errors.
- **Confirmed `Bandroom.Core.Tests` passes**: 104/104 tests green.
- `MacGameWatcher` (in `GameWatcher.Mac.cs`) mirrors Windows `GameWatcher.cs`'s full 12-region OCR
  set + sticky/two-tick-commit discipline, driven by a bundled Python OCR bridge
  (`bandroom_ocr_bridge.py`, uses `screencapture` + Vision OCR) instead of the Windows RAM-reader
  `.exe`. This script is preset-agnostic (regions come in as JSON), so it's unaffected by today's
  ESPN 2013 preset removal.
- **Known, intentionally-deferred gap:** possession side (home/away) has no color/underline
  pixel-sampling equivalent on Mac (Windows samples scorebug pixel colors; Mac only has a
  best-effort "HOME"/"AWAY" text OCR match that rarely fires since scorebugs don't render that
  literal text). Documented in-code as a known limitation, not a bug -- penalty-side attribution
  logic already gates safely on `null` possession rather than guessing wrong. Left untouched this
  session (not worth touching blind without a Mac to verify against).
- **Real blocker, not fixable in software:** the actual RAM reader (`CollegeFB27RamReader.exe`) is
  a closed-source Windows-only `.exe`. Mac can never use it -- OCR is the *only* path for Mac,
  which is exactly why today's remote-play test matters double: it's validating the same code path
  Mac is permanently stuck using.
- Other known stubs (audio engine / CrowdBus / controller rumble / Sparkle auto-update) are
  unchanged from prior sessions -- see `PlatformStubs.Mac.cs`, all still no-ops pending
  macOS-native reimplementations.

## Handed Owner Step-by-Step Mac Test Instructions

Wrote plain-language (non-technical) steps for testing on the MacBook Air:
1. Install .NET 10 SDK (ARM64 build, since Air is Apple Silicon).
2. Copy `C:\Bandroom` over to `~/Bandroom` on the Mac (zip + AirDrop/Drive suggested).
3. `dotnet build src/Bandroom.Mac/Bandroom.Mac.csproj` -- confirm `Build succeeded.`
4. `dotnet run --project src/Bandroom.Mac/Bandroom.Mac.csproj` -- confirm window + browser tab open.
5. Get the game/stream on screen with a CBS-style scorebug visible and that window focused
   (remote-play OCR only works with the window in focus and the CBS layout specifically).
6. Toggle Remote Play on in the app, confirm score/down/quarter update live (or check
   `ocr_debug.log` if unsure).

## Build & Run Status

- `dotnet build` on both `BandAudioHook.csproj` (Windows) and `Bandroom.Mac.csproj` -- clean.
- `Bandroom.Core.Tests` -- 104/104 passing.
- No live game tested by any agent this session on either platform -- owner was running the
  remote-play/BG-events test independently in parallel; results not yet reported back.

## Git

`13e75e9` committed and pushed to `origin/master` as part of the release (includes the 4 untracked
measure scripts). Tagged and released as `v1.1.6`, live on GitHub.

## Options Discussed, Not Started

- **Waiting on:** owner's live remote-play + BG-events test results on Windows -- this is the
  actual validation for both platforms' OCR path and should drive next steps.
- **Waiting on:** owner's Mac build/run test on the MacBook Air using the steps above.
- Mac audio engine (AVAudioEngine-based CrowdBus/rumble replacements), Sparkle auto-update wiring,
  and the possession color-sampling gap remain open but intentionally not started -- all need
  either live Mac hardware to iterate against or an owner decision on priority.
- One earlier background research agent (spawned to investigate Mac app state) got stuck in a loop
  claiming it hadn't received its own results and never produced real output after 3 follow-up
  pings -- had to abandon it and do the investigation directly instead. Worth noting if that
  pattern recurs; no root cause identified.
