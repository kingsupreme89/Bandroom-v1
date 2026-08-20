# Bandroom Handoff — August 19, 2026 — Session 97

Direct continuation of Session 96, same live-game session, same night (4th session in a row).
Session 96 ended mid-diagnosis on a live cluster of RAM-data-quality symptoms with an explicit
"don't chase further tonight" flag. This session picked that back up live, in-game, and shipped
six releases (v1.1.23 through v1.1.28) fixing it incrementally as new symptoms surfaced in real
time. **Read "Still Open" before doing anything else next session.**

## Root cause #1 (Session 96) — now fixed, two layers deep

Session 96 diagnosed but never confirmed that RAM's down/distance/possession fields could resolve
correctly once, then go silently stuck or noisy for the rest of the game while `RamReaderStatus`
still read `Connected`. This session confirmed it live, then found it was two separate bugs
stacked on top of each other:

- **Down/distance flapping (v1.1.24).** RAM's raw down/distance weren't simply frozen -- they were
  genuinely noisy tick to tick (distance observed ticking 9 -> 7 -> 9 in the live log). The
  existing stale-RAM-field fallback (`GameWatcher.RouteEngineTick`, added 2026-08-14) only
  overrides to OCR once RAM has held one wrong value steady for 5s -- a value that never holds
  still never triggers it, so the fallback kept engaging/disengaging every ~250ms tick, and the
  FINAL down/yardsToGo fed into `PlaySnapshot` flapped between RAM's and OCR's values in lockstep.
  Down-edge evaluators (`DefenseThirdDownHelper`, etc.) fire on any `Current.Down != Previous.Down`,
  so every flap re-fired the same card (owner report, live: "Defense: 3rd & Long" firing 3x in 5s).
  **Fix:** added `ConfirmFinalValue`/`_lastConfirmedFinalDown`/`_lastConfirmedFinalYardsToGo` --
  a 2-consecutive-agreeing-tick debounce applied to the FINAL committed down/yardsToGo, after the
  RAM-vs-OCR fallback has already picked a value for the tick, regardless of which source produced
  it. A value that flaps every tick never accumulates 2 agreeing ticks, so it can't reach the
  evaluators until it's genuinely settled.
- **Possession flapping (v1.1.28, found late in the session, live).** Same bug, one layer up.
  `ReaderNumericSnapshot.HavePossession` itself started flickering true/false mid-game (RAM's
  possession locator intermittently failing to resolve at all, not just resolving wrong). Both
  `readerPossessionAway` (RAM's own confirmed value) and `_lastPossession` (OCR's own confirmed
  value, via `ConfirmPossessionFlip`) were already individually debounced -- but the line that
  picks between them, `readerPossessionAway ?? (_lastPossession == "away")`, switched which
  already-smoothed SOURCE it read from every single tick `HavePossession` flickered, with no
  debounce on that switch itself. Owner report, live: "After Punt (Home)"/"1st Down (Away)" firing
  repeatedly every ~20s with no real punt in between -- each source-switch looked like a fresh
  turnover to the structural-turnover/first-down helpers. **Fix:** `ConfirmFinalPossession`/
  `_lastConfirmedFinalPossessionAway`, same 2-tick debounce pattern, applied to the combined final
  bool right before it's written into `PlaySnapshot`.
- **Diagnostic visibility (v1.1.24):** `LogRamOcrCrosscheck`'s possession mismatch line now
  includes `HavePossession={ram.HavePossession}` directly -- Session 96 flagged this as invisible
  ("no log line for it currently"); it's what made the possession-flapping bug traceable live
  tonight once it started.

## Root cause #2 (Session 96) — partial fix shipped, still real risk

Session 96 diagnosed but explicitly scoped out: every `Start()` (including a bare app/process
restart, not just a deliberate Stop-then-Start) rebuilds `GameStateEventHelper` fresh, re-arming
its one-shot flags (`_didFirePregame`, `_didFireStart2ndQuarter`, `_didFireStart4thQuarter`).
Restarting mid-game makes the next kickoff-shaped moment look exactly like a fresh pregame/
quarter-start transition. Confirmed live this session: an app auto-update mid-game (see below)
caused exactly this -- a phantom "Pregame Take the Field"/"Opening Kickoff" pair fired right after
relaunch.

**Fix shipped (v1.1.24):** `GameStateEventHelper.SuppressOneShotsAlreadyPassed(quarter, down)`,
called once per `Start()` the first tick Quarter/Down both resolve to real values. A genuinely
fresh pregame's first live tick is always Quarter==1/Down==1 (a drive can't start already on 2nd
down) -- anything else observed on that first tick is unambiguous proof the game was already in
progress, so the already-passed one-shot flags get pre-armed. Leaves a real fresh pregame
completely untouched.

**Known gap, not attempted:** this only covers the two down/quarter-anchored pregame signals.
`KickoffHelper`'s `IsKickoff`-edge fallback signal (the 3rd of pregame's three OR'd triggers) has
no equivalent guard -- a restart landing exactly during a live kickoff situation, before down/
quarter resolve, can still misfire pregame via that path. Narrow window, not hit tonight, not
fixed. The deeper "is this a restart of the same game vs. a genuinely new game" detector Session
96 called the real fix remains unbuilt, scoped out three sessions running now.

## Live-release side effect discovered this session (important, operational)

Running `ppup` (release.ps1) while the app is live in-game triggers the INSTALLED Squirrel app's
own auto-update check, which silently kills and relaunches the running process to apply the
update -- confirmed live: ~85 second dead window (17:18:17 -> 17:19:42) where OCR/RAM capture
stopped entirely ("game window isn't focused") while Squirrel swapped `app-1.1.22` for
`app-1.1.25`. A real field goal happened in that exact gap and was never captured -- not a
`FieldGoalPATHelper` bug, just nothing was watching.

**Also discovered:** the `D:\Bandroom\bin\Debug\...\Bandroom.exe` build (rebuilt/relaunched
repeatedly early this session while testing fixes) is a COMPLETELY SEPARATE process from the
installed production app at `C:\Users\Fresh\AppData\Local\Bandroom\app-<version>\Bandroom.exe`
that was actually driving the owner's live game audio all night. Early-session debug rebuilds
never reached the live game -- only the `ppup` releases (which trigger the installed app's own
auto-update) actually updated what the owner was hearing. Worth remembering next session: testing
against the Debug build proves the CODE works, not that the LIVE session is running it.

**Not fixed, flagged for next session if it keeps happening:** no guard exists against a `ppup`
mid-live-game causing another auto-update capture gap. Options not yet explored: defer the
installed app's update check while `_ramModeEnabled`/watching is actively live, or warn before
running `ppup` if `GameWatcher` is currently watching.

## Other fixes shipped this session

- **v1.1.25** — "No songs assigned yet" GAMETIME prompt only ever explained what Starter Profile
  does; Generic Profile had zero explanation, so the two buttons looked identical in purpose
  (owner report, live: "people don't know the difference"). Added explicit copy for both:
  Starter pulls from the team's own Default Song Pack, Generic pulls from a shared non-team-
  specific fallback pack.
- **v1.1.26 -> v1.1.27** — Owner request to trim the Coffee's Corner scorebug SKIN overlay
  gallery (separate feature from the OCR-reading `ScorebugPreset` picker, which was already
  correctly limited to exactly 3: Kam's CBSv3, College Football 27, College Football 26/27
  Console). v1.1.26 first trimmed ESPN/NBC out, keeping FOX/CW; owner clarified they wanted
  everything gone, so v1.1.27 emptied `theme-library/library.json`'s `themes` array entirely --
  no skin auto-selects or shows on GAMETIME now unless one is added back. Fixed the follow-on
  bug this exposed: `WebMainForm.GetSavedScorebugSkinFromWeb` now validates a previously-saved
  skin name against the CURRENT gallery and returns "" if it's gone (was: kept returning a dead
  name forever, switcher pill showed a name that could never actually resolve to a file).

## Still Open

1. **Confirm the possession-flap fix (v1.1.28) holds for a full game.** Only diagnosed and fixed
   live in the final stretch of tonight's session off one game's symptoms -- not yet confirmed
   clean over a complete game with no watchdog possession-mismatch log spam.
2. **`KickoffHelper`'s pregame fallback signal has no restart-guard** (see Root cause #2 above) --
   narrow gap, not hit tonight, not fixed.
3. **`ppup` mid-live-game causes an ~85s capture blackout** via the installed app's own
   auto-update -- no mitigation built. Will recur on every future mid-game release until addressed.
4. **The real per-field RAM staleness detector Session 96 called the actual fix is still
   unbuilt.** Tonight's two debounce fixes (down/distance, possession) treat the SYMPTOM
   (flapping final values re-triggering evaluators) -- they don't detect or surface when a field
   has gone stuck/noisy at the SOURCE. If RAM keeps degrading mid-game on this setup, that's still
   the real fix, not another debounce.
5. **RAM auto-restart watchdog (Session 96, `RestartRamReader`) still not live-tested** -- no
   confirmed sighting of the `[ScoreboardReaderHost watchdog]` restart log line this session either.
