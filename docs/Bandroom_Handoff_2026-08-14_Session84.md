# Bandroom Handoff — August 14, 2026 — Session 84

Same idea as always: what happened, explained plain.

## Fixed: CFB26 Remote Play Wasn't Reading At All

Two separate bugs, both confirmed live against real ocr_debug.log output:

1. **Window focus lock-in.** `GameWatcher.FindGameWindow()` locks onto the first matching process
   it finds and never re-checks. With both a background `CollegeFB27` PC process AND `RemotePlay`
   running at once, it always grabbed CollegeFB27's window — even though RemotePlay was the one
   actually on screen — so the foreground check failed every single tick with no explanation.
   Fixed: `FindGameWindow` now prefers whatever's actually in the foreground, and the main loop
   re-targets `hwnd` mid-session if a different candidate process takes focus (`GameWatcher.cs`).
2. **Process matching is now scoped per scorebug preset**, not "search everything." Added
   `ScorebugPreset.GameProcessNames` — "College Football 27" only looks for `CollegeFB27`;
   "College Football 26/27 Console" (renamed from "College Football 26 Console" -- owner plays
   both years through Remote Play) only looks for `RemotePlay`. Fixes the same root problem more
   permanently: a leftover process from the other game can't get matched by accident anymore.
3. **Down/distance OCR silently never matched.** CFB26's stylized "&" glyph gets read by Windows
   OCR as the letter "a" ("2nd a 8" instead of "2nd & 8"). The `down`/`quarter` regexes required a
   literal "&" — widened both to accept `[&a8]` (`GameWatcher.cs`).

`ScorebugPreset.CollegeFootball26Console`'s Band/Score/Clock/Penalty/Banner crops were also
calibrated for real against live CFB26 Remote Play screenshots (previously an unverified clone) —
confirmed matching CFB27's geometry exactly.

## Fixed: Opening Kickoff Still Occasionally Played Away's Song

Three separate routing paths could all misroute Opening Kickoff depending on game state
(`_hbcuPlayback` null/not-null, `_possession` read yet or not) -- all three now hardcode Opening
Kickoff to `home`, never falling back to away even if home has no song assigned:
`WebMainForm.cs`'s HBCU home-priority branch, the no-possession-yet both-sides fallback, and
`ResolveEventRouting`'s main non-HBCU path.

## Fixed: "Pregame Ready" Firing Mid-Game

The `pregameready` OCR region scans a wide, deliberately team-neutral band for the literal word
"READY" -- with zero game-phase guard. That word can legitimately appear elsewhere mid-game (a
post-play prompt, etc), and since the region is deliberately left out of `EventGatedRegions` (so
Back-and-re-ready on the team-select screen can re-fire it), nothing stopped a stray mid-game
sighting from re-arming the edge trigger. Reported live: fired during a PAT with no READY screen
up, played `Offense_PAT_Made_norm_Song.wav`. Fixed with a `Quarter == 0` guard in
`PregameHelper.cs` (this codebase's established "still pregame" signal) -- added a regression
test (`PregameHelper_DoesNotFire_OnMidGameReadySighting`).

## Reworked: HBCU Touchdown Handling

Owner request. Previously only the OPPONENT's shuffle track faded on a TD; the scoring side's own
track kept playing underneath. Now (`HbcuPlaybackService.OnTouchdown`):
1. Whatever's currently playing on EITHER side fades out (owner call: "fade whoever's playing").
2. The TD cue plays clean with nothing underneath it.
3. Once the TD cue's own duration elapses, the scoring side gets ONE bonus song from their pot.
4. Normal alternating rotation resumes exactly where `_nextTurn` already was -- the bonus song is
   a treat on top of the existing turn order, not a replacement for that side's next real turn
   (owner call: "bonus, doesn't affect turn order").

## Fixed: HBCU Pot Dashboard Pills Causing Overlapping/Stuck Playback

Two bugs in `HbcuPlaybackService`, both live-reported ("make all the songs play and don't really
stop and start properly"):
1. `Stop()` only ever reset internal scheduling state -- it never actually silenced whatever was
   CURRENTLY sounding. Stop-then-Start (Restart calls Stop first) left the old song audibly
   playing while a new one started on top of it. Now calls `AudioPlayer.FadeOutChannel` on both
   channels for real.
2. `Resume()` had no guard against being called when not actually paused. A rapid double-click of
   the dashboard's Resume pill called `PlayNext()` twice in a row -- since PlayNext alternates
   sides every call, the second call started the OTHER side's song on top of the first with
   nothing to stop it (different channel). Added a `_paused` guard so a redundant Resume is a
   clean no-op; every legitimate internal caller already only resumes while genuinely paused.

## Fixed: Dashboard Showing "200 sec" Until Next Track

`DefaultAssumedDuration` (3 minutes) + `AdvanceGap` (20s) = 200s exactly -- this fallback fired
whenever a pot song had no cached `.meta.json` duration, which is common since Team Pot songs are
usually short hype clips added straight from the Clipper without ever going through the
preview/analyze flow that writes that sidecar. Blindly assuming every uncached song is a full
3-minute track badly overshot the real (~10-30s) length. Added `ResolveSongDuration` -- reads the
real file duration via NAudio (same fallback `WebMainForm.ResolveEventSongDuration` already uses
for event cues), only falling back to the 3-minute guess if the file is genuinely unreadable.

Also fixed (owner: "keep them shuffling no matter if they've played or not"): a team with a
genuinely empty pot/pack used to sit fully idle for the whole 3-minute `DefaultAssumedDuration`
before retrying. Now retries after just `AdvanceGap` (20s, same cadence as everything else) so a
pot that gets songs added mid-game picks back up quickly instead of freezing that side's rotation
for minutes.

## Added: "Use Generic Pack" Picker for HBCU Opponents

Owner request -- a team with no HBCU pot/pack of its own (typically the FBS/non-HBCU opponent in
a matchup) used to just sit silent all game once their empty queue was reached. Explicit picker
(owner wanted a forced override, not just an automatic empty-pot fallback), not automatic:
- `ConfigStore.GetHbcuUseGenericPack`/`SetHbcuUseGenericPack` -- new per-team boolean flag,
  persisted to `hbcu_generic_pack_teams.json`.
- New checkbox in the Team Pot panel header ("Use Generic Pack") -- `wwwroot/index.html`/`app.js`.
- `HbcuPlaybackService` takes two new optional constructor flags; when set, `Refill` looks up the
  pot/pack under the sentinel team name `"Generic"` instead of the real team name. Display fields
  (`_homeTeam`/`_awayTeam`, used by the dashboard) are left untouched -- only the lookup key
  changes, so the dashboard still shows the real matchup.
- "Generic" itself needed zero schema changes -- `GetHbcuPot("Generic")`/
  `GetPackFilesForSchool("Generic")`/`AddToHbcuPot("Generic", ...)` all already worked unmodified
  since neither store validates the team name against a real roster.
- **Known gap, not a regression**: `src/Bandroom.Mac/MacWebBridge.cs` doesn't implement ANY HBCU
  pot bridge methods (not even the pre-existing `GetHbcuPot`/`AddToHbcuPot`) -- HBCU Team Pot mode
  has always been Windows-only. The new checkbox degrades gracefully there (try/catch swallows the
  missing-bridge-method error, checkbox just stays unchecked) but doesn't do anything on Mac yet.

## Other Owner Requests, Done

- **Pregame runout delay**: was 15s (owner thought 20), bumped to 35s. Single shared setting --
  applies to both FBS and HBCU modes automatically. Still adjustable 15-45s via Profile → Settings
  → Timing.
- **Removed duplicate scorebug preset picker**: Profile → Settings had a second dropdown
  (`#settings-scorebug`) that wrote the exact same `ConfigStore` value as the matchup-screen pill
  switcher -- confirmed both were reading/writing the identical stored preset, just two UI views
  on it. Deleted the Settings-tab one, kept the matchup-screen pill as the sole picker.
- **Profile pills moved above Team grid** in the left sidebar (owner: "so people can see how to
  share profiles") -- pure DOM reorder in `index.html`, no JS/backend changes needed.
- **Volume/whistle "bug"** -- investigated, found no code bug in the save/load path
  (`SetHomeVolumeFromWeb`/`PersistAudioSettingsDebounced` in `WebMainForm.cs`). Turned out to be
  the Windows per-app volume mixer, confirmed by the owner directly. No fix needed.

## Verification

- `dotnet test src/Bandroom.Core.Tests` -- 105/105 passing (added 1 new regression test for the
  PregameHelper mid-game guard).
- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, multiple times this
  session as changes landed.
- Live-verified via `ocr_debug.log`: window-focus fix confirmed actually reading OCR text
  end-to-end (down/situation/quarter/clock all populating) during a real Remote Play session,
  correctly suspending on the pause menu ("frame appears frozen").
- Visually confirmed via screenshot: Profile pills render above Team grid; Home/Away volume
  sliders are live-adjustable, not stuck.
- Self-audit pass (5-check deep-audit skill) on this round's `HbcuPlaybackService.cs` changes:
  read every changed function against its callers, checked `Resume()`/`Pause()`/`Stop()`/
  `OnTouchdown()`/`PlayTouchdownBonus()` for guard consistency, confirmed `DefaultAssumedDuration`
  has no other now-stale references, confirmed the one `new HbcuPlaybackService(...)` call site
  (`WebMainForm.cs:949`) was the only one needing the new constructor params. No regressions found
  beyond the pre-existing Mac gap noted above.
- HBCU Pot panel's new "Use Generic Pack" checkbox itself was NOT live-clicked/visually confirmed
  this session (only code-reviewed) -- worth a quick look next time the Pot panel is open.

## Git

Not committed. Large uncommitted working tree, this session's changes layered on top of several
prior uncommitted sessions: `AudioPlayer.cs` (FadeOutChannel, prior session), `ConfigStore.cs`
(Generic Pack flag storage), `GameWatcher.cs` (window-focus fix, down/quarter OCR regex,
preset-scoped process matching), `HbcuPlaybackService.cs` (untracked -- new file, touchdown
rework + pot-hub fixes), `Native.cs` (GetWindowThreadProcessId), `ScorebugPreset.cs` (CFB26/27
console rename + calibration + GameProcessNames), `WebBridge.cs`/`WebMainForm.cs` (Generic Pack
bridge, ResolveEventSongDuration rename, kickoff/routing fixes), `wwwroot/*` (Generic Pack
checkbox, profile pills reorder, removed duplicate scorebug dropdown),
`src/Bandroom.Core/Helpers/PregameHelper.cs` (Quarter==0 guard),
`src/Bandroom.Core.Tests/EvaluatorTests.cs` (new regression test). No release triggered this
session (no "ppup").

## Options Discussed, Not Started

- **Waiting on**: owner live-testing the Remote Play window-focus fix, the down/distance "&"
  OCR fix, and the reworked HBCU touchdown sequence during an actual live game (all confirmed via
  log/build/unit-test only, not a full live playthrough yet).
- Mac's HBCU Team Pot bridge support (`MacWebBridge.cs`) -- pre-existing gap, not touched this
  session, would need `GetHbcuPot`/`AddToHbcuPot`/etc. ported over before Generic Pack (or Team
  Pot at all) works on Mac.
- HBCU Pot panel's "Use Generic Pack" checkbox -- code-reviewed only, not clicked live yet.
- Session 82's original "nothing on the scorebug is triggering" CollegeFB27.exe live-game report
  -- still not independently re-confirmed fixed on that specific path (this session's window-focus
  fix targets the Remote Play/multi-process scenario specifically; worth checking if it also
  explains the Session 82 report).
- `ChevronMarkerFx*` recalibration -- still on the list from Session 82, untouched again.
- Session 81's still-open items (Coffee scorebug overlay work, RAM reader address-locking
  unreliability) -- untouched again this session.
