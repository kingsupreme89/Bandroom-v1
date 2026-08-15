# Bandroom Handoff — August 15, 2026 — Session 88

Same idea as always: what happened, explained plain.

## Fixed: FBS Mode Team Editing Was Leaking Into HBCU Mode (and vice versa)

Owner report: FBS mode shouldn't need a pot, and should go back to the exact pre-HBCU-toggle
workflow/design. HBCU Mode should lock every team picker to HBCU schools only, and FBS teams
should only be reachable for editing when the toggle is off.

Root cause: `renderTeamGridInto()` (`wwwroot/app.js`) is the one shared team-picker grid behind
every "which team is this for" flow -- Share to Marketplace, Move Upload, Edit Upload's school
picker, Import target -- and it always pulled from the full unfiltered `state.teams` list,
regardless of HBCU Mode. Only the main left-side team grid was actually respecting the toggle.

Fixed: `renderTeamGridInto` now sources from `hbcuFilteredTeams()`, the same filter the main grid
already used. HBCU Mode on -> every picker locks to HBCU-only teams. HBCU Mode off -> unfiltered,
exactly the pre-toggle FBS behavior. The Team Pot panel itself was already correctly gated off in
FBS mode (`app.js:969-970`) -- that part didn't need a change.

## Fixed: Admin Edit Silently Failing (Again)

Owner report (screenshot): "Admin edit failed -- try again" toast, no other detail.

Two changes:
- Re-pushed the `ADMIN_TOKEN` secret to the `bandroom-marketplace` Cloudflare worker from
  `admin_token.local.txt`, in case it had drifted from what a prior session pushed.
- Made this diagnosable if it recurs: `WebBridge.cs`'s `AdminEditMarketplaceItem` now logs the
  worker's actual HTTP status + response body to `crash.log` on a non-success response, and the
  toast (`app.js`) now shows that detail instead of a generic "try again."

## Fixed: HBCU Mode Fading Out Songs It Shouldn't

Owner report: HBCU Mode should have no fade-outs at all except the touchdown interrupt rule
already in place.

Root cause: Team Pot songs already defaulted to `NoFade = true` (verified against the live
`hbcu_pots.json` -- every entry already had it). But Kickoff/Runout/Ready/Touchdown event cards
still route through the normal `TriggerEntry`/`FireEvent` path even in HBCU Mode, and
`TriggerEntry.NoFade` defaults to `false` -- so those specific cards were still fading out on the
global Audio Timing schedule.

Fixed (`WebMainForm.cs FireEvent`): forces `noFade = true` for every cue whenever
`ConfigStore.LoadPlaybackMode() == PlaybackMode.Hbcu`, regardless of that card's own setting. The
touchdown interrupt fade (`HbcuPlaybackService.OnTouchdown`'s `AudioPlayer.FadeOutChannel` call on
whatever's currently playing) is a separate hard-cut mechanic and is untouched.

## Fixed: Post-Touchdown Kickoff Routed to the Wrong Team (and Went Silent)

Owner report (event log screenshot): Away scored a touchdown, then "Kickoff (Home) -- no song
assigned, nothing played." Real football: the team that just scored kicks off, not Home by default.

Root cause: the post-TD "Other: Kickoff" event was sharing the same home-priority fallback rule
as Opening Kickoff and Pregame Ready/Take the Field -- which is correct for those (they're genuinely
side-agnostic), but Kickoff-after-a-score isn't; it has a real answer (whoever scored).

Fixed (`WebMainForm.cs` / `HbcuPlaybackService.cs`):
- Added `_lastHbcuTouchdownSide`, set whenever a touchdown fires in HBCU Mode.
- Post-TD `Other: Kickoff` now routes to whichever team just scored instead of guessing Home.
- If that team has no Kickoff song assigned, it no longer sits silent -- new
  `HbcuPlaybackService.PlayKickoffFallback()` grabs a song from that team's Team Pot and plays it,
  then the normal 20s `AdvanceGap` carries the alternating rotation on from there, same as any
  other track transition. A real assigned Kickoff cue still plays normally, unchanged.

## Investigated: RAM/OCR Watchdog "Disagrees" Log Line

Owner flagged the `(RAM/OCR watchdog) RAM is primary but disagrees with OCR -- away score RAM=7
OCR=0` log entries as unwanted, "2nd time it's happened."

Checked `GameWatcher.cs LogRamOcrCrosscheck`: this is purely diagnostic (EventActivityLog only),
never overrides or blocks any actual playback decision -- RAM stays authoritative regardless. The
specific mismatch shown is the on-screen OCR reader briefly lagging the RAM reader for a tick or
two right after a score updates (the scoreboard digits are still mid-animation), and it already
self-clears once OCR catches up (dedup logic only logs once per distinct mismatch). Not a bug,
left as-is -- flagged to the owner as expected noise around every touchdown, with an option
offered (not taken up this session) to suppress score-only mismatches for a couple seconds after
a scoring play if the log clutter itself is unwanted.

## Investigated: Windows Mixer Volume Turning Bandroom Down

Owner report: Bandroom's own slider in the Windows volume mixer drops on its own.

Checked `AudioPlayer.cs` / `AudioEngine.cs` for anything writing to the Windows session/endpoint
volume -- confirmed nothing in the app ever does; the one CoreAudioApi touch point
(`AudioEngine.cs:745-747`) is read-only diagnostics. Root cause was Windows' own "Communications"
ducking feature (`HKCU:\Software\Microsoft\Multimedia\Audio\UserDuckingPreference`), which wasn't
explicitly set and was defaulting to "Reduce by 80%" -- Windows auto-ducks every other app's mixer
level whenever it detects communications-type audio activity (Discord, Zoom, calls, etc.). Set
that registry value to `3` ("Do nothing") directly, same effect as Control Panel -> Sound ->
Communications tab -> "Do nothing." Takes effect immediately, no reboot.

## Fixed: Update-Available Chime Sometimes Not Playing

Owner report: no draft chime when an update is detected, even though the app-open chime works fine.

Root cause: `AudioPlayer.Play`'s 20s same-file `FireCooldown` (built to stop rapid OCR
re-triggers mid-game) was silently swallowing the update-detected chime whenever an update was
already available right at launch -- the app-open chime and the update-detected chime share the
exact same file (`nfl-draft-chime.mp3`), and `InitAutoUpdater`'s first check runs almost
immediately with no startup delay, landing inside that 20s window.

Fixed: `PlayDraftChime()` (`WebMainForm.cs`) now calls `AudioPlayer.ClearCooldown()` on that file
before playing -- it's a deliberate, infrequent notification, not the game-trigger spam the
cooldown exists for.

## Fixed: Team Pot Kept Playing After "Stop Watching"

Owner request: the Team Pot auto-play needs to stop when Stop Watching is pressed.

Root cause: `_hbcuPlayback` was tied to the confirmed matchup (built at GAMETIME), not to the
watch state -- `ToggleWatchingFromWeb`'s stop branch never told it the game ended.

Fixed: stop branch now calls `_hbcuPlayback?.Stop()`. Next GAMETIME press still builds a fresh
`HbcuPlaybackService` and auto-starts it after that new game's own Opening Kickoff song, same as
always -- this only silences the instance from the game that just ended.

## Added: Team Pot Falls Back to the Generic Pack After a Full Cycle

Owner request: once every song in a team's own Team Pot has played through, the rotation should
pull from the shared Generic pack instead of immediately re-shuffling the same handful of songs.

Fixed (`HbcuPlaybackService.cs Refill`): each side now alternates own-pot / Generic every time its
queue empties -- own pot cycle, then a Generic cycle, then back to own pot, and so on -- as long as
Generic actually has songs to alternate with (falls back to just looping the team's own pot
otherwise). Doesn't touch the explicit "Use Generic Pack" toggle (still forces Generic always) or
the existing "no pot/pack at all -> borrow other side -> Generic" fallback for a side with nothing
assigned. Resets to own-pot-first on every `Stop()`/new game.

## Shipped

`ppup` -- committed, pushed, tagged, built, packaged with Squirrel, and published as **v1.1.12**:
https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.12

## Verification

- `dotnet build BandAudioHook.csproj -c Debug` -- 0 warnings, 0 errors, after every C# change this
  session (checked incrementally, not just once at the end).
- `node --check wwwroot/app.js` -- clean syntax after every JS change.
- Read the live `hbcu_pots.json` directly (`%LOCALAPPDATA%\Bandroom\UserData\hbcu_pots.json`) to
  confirm `NoFade: true` was already present on every existing pot entry before ruling that out as
  the fade-out source.
- Confirmed via `wrangler deployments list` / `wrangler secret list` that the marketplace worker's
  `ADMIN_TOKEN` secret deploy actually landed.
- NOT independently live-tested this session: every HBCU Mode behavior change (kickoff fallback,
  fade removal, pot-stops-on-Stop-Watching, Generic pack alternation, team-picker locking) was
  verified by code/build only -- none were clicked through or played through in a running game.
  Same "log/build only" caveat prior sessions have flagged for gameplay-timing-dependent changes.

## Options Discussed, Not Started

- Suppressing RAM/OCR watchdog score-only mismatches for a couple seconds right after a scoring
  play, if the log clutter (not any actual playback effect) is still unwanted -- offered, not
  requested.
- Live-fire verification of this session's HBCU Mode changes in an actual running game -- the
  kickoff-fallback and Generic-pack-alternation logic in particular have real timing dependencies
  (song durations, the 20s `AdvanceGap`) that are hard to fully trust without hearing them.
- Everything still carried over from prior sessions' "not started" lists: Mac's HBCU Team Pot
  bridge support, Session 82's original CollegeFB27.exe live-game report, `ChevronMarkerFx*`
  recalibration, Session 81's Coffee scorebug overlay work / RAM reader address-locking
  unreliability, the two Session 87 internal-only audit items (duplicate helpers cleanup,
  title-based team search fallback).
