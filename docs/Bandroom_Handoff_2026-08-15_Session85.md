# Bandroom Handoff — August 15, 2026 — Session 85

Same idea as always: what happened, explained plain.

## Applied: The 4 Real Fixes From Session 84's State-Machine Audit

Session 84 ended with a full state-machine analysis of the game-watching/HBCU-playback system
(state tables, design-level problems, 8 concern-by-concern discrepancy writeups). Of those, 4 were
confirmed real bugs (not just theoretical) and got fixed this session; the other 4 (Opening Kickoff
triple-hardcode, `Quarter==0` guard safety, NAudio duration-read blocking risk, Generic Pack
lookup-vs-display separation) were investigated in depth and verified safe -- no code change.

1. **`DistancePattern` widened to match the down-region "&" fix.** Session 84 widened the
   down-region ordinal lookahead from literal `&` to `[&a8]` (CFB26's stylized "&" OCRs as "a") but
   missed that `DistancePattern` -- the regex that actually extracts the yardage number, not just
   detects the ordinal -- was a separate pattern still requiring literal `&`. On the exact misread
   this was meant to fix, `Down` would update but `YardsToGo` could silently fail to parse. Widened
   to `[&a8]` too (`GameWatcher.cs:318`).

2. **Foreground-window re-target debounced.** `GameWatcher.FindGameWindow`'s foreground-preference
   re-targeting (added Session 84) retargeted `hwnd` the instant a different candidate process took
   focus, every single tick, no dwell time. Two windows both valid candidates (e.g. a background
   CollegeFB27 process and a RemotePlay window both open under the unscoped default preset) fighting
   for OS focus could thrash `hwnd` every tick. Now requires the same alternate candidate to be
   foreground for 2 consecutive ticks before switching (`_pendingForegroundCandidate`,
   `GameWatcher.cs`).

3. **`HbcuPlaybackService.Stop()` now resets `_paused`.** It reset `_running` but left `_paused`
   stale -- latent only (no current call path exercised it, since `Start()`/`Restart()` both route
   through `PlayNext()` directly, not `Resume()`), but a cheap fix to close the trap before anything
   ever depends on it.

4. **New `_tdSequenceActive` guard on the HBCU touchdown sequence.** The TD sequence (fade → TD cue
   → bonus song → resume) only had `_paused` protecting it, and `_paused` is also the flag every
   ordinary Runout/Ready/Kickoff interrupt uses. Concretely: WebMainForm's 8-second post-Runout/
   Kickoff `Resume()` call could fire *while a TD's bonus-song timer was still pending*, since both
   states looked identical (`_running && _paused`) -- that would start a normal rotation track
   underneath the still-queued TD bonus, so when the bonus finally fired it played overlapping
   instead of cleanly after. New `_tdSequenceActive` flag (set for the whole `OnTouchdown` →
   `PlayTouchdownBonus` sequence, cleared only at the sequence's own end or by `Stop()`) makes
   `Resume()` a no-op for the duration, so external interrupt-resumes can't sneak in
   (`HbcuPlaybackService.cs`).

Verified: `dotnet build` had zero `error CS*` (the only failure was the pre-existing file lock from
the Bandroom instance that was running at the time -- unrelated to code correctness). `dotnet test
src/Bandroom.Core.Tests` -- 105/105 passing, unchanged (IntakeEngine/HbcuPlaybackService aren't
covered by that test project; no C# tests target them directly).

## Added: HBCU Schools In The Song Importer

Owner report: importing a FAMU or Bethune-Cookman (BCU/BCC) song wasn't getting picked up by the
"song bank" (the filename-based auto-indexing used by both `ImportLocalSongFromWeb`'s naming dialog
and `DefaultSongPackService`'s bulk folder importer). Root cause: both of those read team names from
`IntakeEngine.ResolveTeam`, which is backed entirely by `scripts/team_registry.json` -- and that
file had **zero** HBCU entries, despite `TeamColors.HbcuTeams` having had all 19 HBCU schools since
Session 82. Every HBCU import silently fell through to `"Unknown"`.

Fixed: added all 19 HBCU schools (SWAC + MEAC) to `scripts/team_registry.json` with real
abbreviations, band names, mascots, and name variants -- same shape as every other entry in the
file, so they flow through the identical `exact → abbreviation → variant → fuzzy` resolution chain.
`FAMU` and both `BCU`/`BCC` (Bethune-Cookman's old and current short names) are explicit aliases per
the owner's ask. Avoided collisions with existing non-HBCU abbreviations (Morgan State can't use
`MSU` -- already Michigan State; Southern University can't use `SU` -- already Syracuse; used
`MORGAN`/`SUBR` instead). Also added `SWAC`/`MEAC` to `conference_map` and to `IntakeEngine.cs`'s
`ConferenceHintTokens` (the filename-based conference-disambiguation list already used for b1g/sec/
acc/etc.) for future-proofing.

Since `scripts/intake_engine.py` (the Python port) reads the exact same JSON, no separate Python
change was needed -- one source of truth, both engines pick it up automatically.

Full list of what got added, grouped by conference:

- **SWAC**: Alabama A&M (`AAMU`), Alabama State (`ALST`/`ALSTATE`), Alcorn State (`ALCORN`/
  `ALCORNST`), Arkansas-Pine Bluff (`UAPB`/`PINEBLUFF`), Bethune-Cookman (`BCU`/`BCC`), Florida A&M
  (`FAMU`), Grambling State (`GSU`/`GRAM`), Jackson State (`JSU`), Mississippi Valley State
  (`MVSU`), Prairie View A&M (`PVAMU`), Southern University (`SUBR`), Texas Southern (`TSU`)
- **MEAC**: Delaware State (`DSU`/`DEST`), Howard (`HU`), Morgan State (`MORGAN`), Norfolk State
  (`NSU`), North Carolina A&T (`NCAT`), North Carolina Central (`NCCU`), South Carolina State
  (`SCSU`)

## Fixed: Team Color-Edit Popover Getting Stuck Open

Owner report, live: "this window wont close" (screenshot showed the "Edit Colors -- Alabama A&M"
popover with Save/Cancel visible but unresponsive). Root cause: the Save button only closed the
popover *after* `await bridge.SetTeamColors(...)` and `await bridge.GetTeams()` both succeeded --
any exception in that async chain left `closeTeamColorEditor()` unreached, so the dialog was stuck
with no way out (Cancel used a separate, unconditional listener and should have kept working
independently, but Save alone had this trap).

Fixed (`wwwroot/app.js`): Save now closes the popover immediately on click -- the color values are
already captured by then -- and does the actual `SetTeamColors`/`GetTeams`/re-render work
afterward in its own try/catch, so a save failure now surfaces as a toast ("Couldn't save team
colors -- try again.") instead of freezing the dialog. Also added click-outside-to-close and
Escape-to-close as a backstop, since previously the two in-dialog buttons were the only way out.

Not independently root-caused *why* the original save call was throwing for this owner in this
moment (the fix makes the dialog resilient to it either way) -- worth a look if it recurs with the
browser devtools console open to catch the actual exception.

## Released: v1.1.9 ("ppup")

Full release run via `release.ps1`:
- Commit `6d9ca25` on `master` (31 files changed -- this session's 4 fixes above, plus everything
  still-uncommitted from Session 84: `HbcuPlaybackService.cs`, the touchdown rework, pot-hub fixes,
  Remote Play OCR fixes, Generic Pack picker, and 6 backlogged handoff docs Sessions 79-84) -- pushed
  to origin.
- Tagged and released as `v1.1.9` (was `v1.1.8`).
- `BandroomSetup.exe` (46.5 MB) + `Bandroom-1.1.9-full.nupkg` (46.3 MB) + `RELEASES` (BOM-stripped)
  uploaded to https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.9 -- live, not a draft.
- Existing installs get the delta update automatically on next launch; new installs run Setup.exe.

Release notes bundle Session 84's CFB26 Remote Play/HBCU touchdown/HBCU pot work together with this
session's 4 audit fixes, the HBCU importer support, and the color-editor fix -- all one release
since none of it had shipped yet.

## Note On Tooling

`git`/`gh` weren't on PATH inside the PowerShell tool this session (only inside the Bash/Git-Bash
tool) -- `release.ps1` failed on its very first `git status --porcelain` call until `C:\Program
Files\Git\cmd` was prepended to `$env:PATH` for that invocation. Not a repo issue, just this
session's shell environment; worth prepending automatically if `ppup` keeps needing PowerShell.

## Verification

- `dotnet build BandAudioHook.csproj -c Debug` -- zero `error CS*` after all 4 audit fixes (file-copy
  step failed only because Bandroom.exe was running at the time and had the DLL locked).
- `dotnet test src/Bandroom.Core.Tests` -- 105/105 passing, both before and after this session's
  changes.
- `node --check wwwroot/app.js` -- clean syntax after the color-editor fix.
- `release.ps1`'s own Release-config `dotnet publish` succeeded clean (this is the build that
  actually shipped) -- separate output dir from the locked Debug one, unaffected by the lock above.
- Squirrel pack succeeded, `RELEASES` BOM-strip step ran (no BOM detected this time, so it was a
  no-op check, not a real strip).
- NOT independently live-tested this session: none of the 4 audit fixes (TD-sequence race, foreground
  debounce, distance-regex widening, `Stop()` reset) were exercised against a real CFB27 session --
  same "log/build/unit-test only" caveat Session 84 already flagged for the underlying features these
  fixes sit on top of. HBCU importer additions and the color-editor fix also weren't clicked live in
  the running app (code-reviewed + syntax/build-verified only).

## Options Discussed, Not Started

- Live-verify this session's 4 audit fixes during an actual game, especially the TD-sequence guard
  (needs a real touchdown within ~8s of another interrupting cue, or two touchdowns in quick
  succession, to actually exercise the new code path) and the foreground-window debounce (needs two
  simultaneously-valid candidate windows actually fighting for OS focus, which needs the unscoped
  default preset with both a PC window and Remote Play window open at once).
- Root-cause why `SetTeamColors`/`GetTeams` threw for the owner in the reported "won't close"
  moment -- the fix makes the symptom go away either way, but the underlying trigger wasn't found.
- Everything still carried over from Session 84's own "not started" list: Mac's HBCU Team Pot bridge
  support (`MacWebBridge.cs` -- confirmed zero HBCU methods at all, not partial), the "Use Generic
  Pack" checkbox not yet clicked live, Session 82's original CollegeFB27.exe live-game report not
  independently re-confirmed on that specific path, `ChevronMarkerFx*` recalibration, Session 81's
  Coffee scorebug overlay work and RAM reader address-locking unreliability.
