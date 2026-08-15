# Bandroom Handoff — August 14, 2026 — Session 83

Same idea as always: what happened, explained plain.

## Investigated & Confirmed: CFB27 Scorebug Crop Coordinates Are Correct

Session 82 ended with the scorebug-not-triggering mystery narrowed to two suspects: crop
coordinates being off, or the game window losing OS-foreground focus during capture. This session
started by chasing the crop-coordinate half with a fresh batch of 16 live screenshots the owner
sent (Georgia @ Fresno State pregame walkout/huddle, Clemson @ Florida State kickoff through a full
scoring drive, plus a penalty-decision overlay sent separately).

Measured every one of `ScorebugPreset.CollegeFootball27`'s crop boxes against these screenshots:

- **Band/score/clock**: all still land correctly (`BandFxY=0.870/BandFxH=0.075`, away/home score
  boxes, clock box) -- no drift from the last calibration.
- **TOUCHDOWN banner** (`BannerFx*`): confirmed correct against 3 separate TOUCHDOWN screenshots.
- **PenaltyAgainstFx\***: this one had NEVER actually been verified against a real CFB27 screenshot
  before -- it was a CBS-cloned placeholder. The owner's penalty screenshot (#733, Clemson @
  Florida State, "Neutral Zone Infraction... Against Florida State") confirmed the placeholder
  values already happen to land correctly (~x 0.68-0.96, y 0.54-0.80, inside the existing
  0.65-0.99/0.50-0.84 box). No coordinate changes made anywhere in this file -- just added
  confirmation comments recording that these are now verified, not guessed.

**Conclusion: crop coordinates were never the bug.** Both this session's investigation and Session
82's "not focused" log spam pointed elsewhere.

## Fixed: HBCU Mode's Opening Kickoff Needlessly Gated on a Second OCR Read

The owner clarified the actual definition needed: "Opening Kickoff is the first time kickoff
appears on the screen and no other time" -- full stop, no other condition.

`KickoffHelper.cs` used to also require `state.Current.Quarter == 1` before firing the
"Other: Opening Kickoff" cue. That's logically redundant (the first kickoff of a game IS quarter 1
by definition) but practically dangerous: it made Opening Kickoff depend on TWO independent OCR
reads (the `situation` region reading "KICKOFF" AND the `quarter` region reading "1st") landing
correctly on the exact same tick. A blank/stale quarter read on that one tick silently demoted the
event to the generic "Other: Kickoff" instead -- no error, no log flag, just the wrong (HBCU-
unfiltered) event key. This is the likely real cause of HBCU mode's kickoff never showing up despite
the on-screen text being clearly legible in every screenshot checked.

Fix: dropped the `Quarter == 1` check entirely. `_openingKickoffFired` (already existing, resets
per-game) is sufficient on its own. Existing test `KickoffHelper_Fires_OpeningKickoff_OnceOnly`
still passes unchanged (it happened to pass `quarter: 1` anyway).

## Fixed: HBCU Mode Was Still Using Big Game Routing/Volume Logic

Owner request: remove the Big Game feature from HBCU mode entirely -- it has its own continuous
pot-shuffle playback (`HbcuPlaybackService`) with its own touchdown/kickoff handling, and Big Game's
"away team plays quiet unless it's an earned event" logic was actively fighting that system.

Added `EffectiveBigGame` property to `WebMainForm.cs`:
```csharp
bool EffectiveBigGame => _watcher.IsBigGame && _hbcuPlayback == null;
```
Swapped every direct `_watcher.IsBigGame` read in this file over to it: the " BG" activity-log tag,
`FireEvent`'s `BigGameAudioFile` alternate-track selection, `ResolveKickoffSongDuration`'s same
alternate-file check, `ResolveEventRouting`'s away-team-quiet/earned-event gating, and
`FieldPositionVolumeMultiplier`. The underlying `ConfigStore.BigGameSettings` toggle itself is
untouched -- still works normally for a non-HBCU game, just forced off whenever
`_hbcuPlayback != null`.

## Fixed: HBCU Kickoff/Pregame Events Played the Wrong Side's Song

Owner report, live: Opening Kickoff fired this time (after the two fixes above) but played the AWAY
band's song instead of home's.

Root cause: `OnEngineEventsDetected`'s side-agnostic "Other:*" events (Pregame Ready, Pregame Take
the Field, Kickoff, Opening Kickoff) only got fair home/away treatment when `_possession` hadn't
been read yet for the game (a dedicated branch fires them for BOTH sides in that case, home first).
Once `_possession` had already been set to something -- which can easily happen before Opening
Kickoff's own event fires -- these events fell through to the same `ResolveEventRouting` every
possession-tied event uses, which routes strictly by `routedSide = possessionSide`. If possession
happened to read "away" at that moment (an arbitrary/early read, nothing to do with who's actually
kicking), the "side-agnostic" cue silently became away-only with zero home preference.

Fix, scoped entirely inside the existing `if (_hbcuPlayback != null)` block in
`OnEngineEventsDetected`: these four event keys are now resolved and fired directly there --
home-first, falling back to away only if home has nothing assigned -- via a plain
`ResolveEntryForEvent("home", ...) != null` check, completely independent of `_possession`. They're
then stripped out of the `events` list (`events.Except(hbcuSideAgnostic)`) so the possession-routed
loop further down can't also fire them a second time. Touchdown routing (which legitimately needs
the real possession side) is untouched -- only these 4 side-agnostic keys were pulled out.

## Build & Test Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, twice this session
  (once for the KickoffHelper fix, once for the HBCU Big Game/routing fix). Had to kill a locked
  `Bandroom.exe` (PID 24432, left running from Session 82) before the first build would succeed.
- `dotnet test src/Bandroom.Core.Tests` -- 104/104 passing, twice this session, no regressions.
- Bandroom.exe relaunched twice (PID 21072, then PID 15860 after the HBCU fix) so the owner can
  live-test. NOT yet confirmed live by the owner as of this handoff -- the owner's last message was
  "it triggered this time but it was away song bg", which is the exact bug the final fix in this
  session addresses; that fix has not yet been re-tested live.

## Git

Not committed. Same already-uncommitted working tree as Session 82, now with this session's
changes layered on top: `ScorebugPreset.cs` (confirmation comments only, no coordinate changes),
`src/Bandroom.Core/Helpers/KickoffHelper.cs` (dropped Quarter==1 gate), `WebMainForm.cs`
(`EffectiveBigGame` property + 5 call-site swaps, HBCU side-agnostic home-priority routing). No
release triggered this session (no "ppup").

## Options Discussed, Not Started

- **Waiting on**: owner live-testing the HBCU home-priority + Big-Game-off fix (Bandroom.exe PID
  15860 is running and ready) -- specifically confirming Opening Kickoff now plays HOME's song and
  the " BG" tag no longer appears in HBCU mode's activity log.
- Session 82's original "nothing on the scorebug is triggering" report (CollegeFB27.exe live-game,
  non-HBCU) is still not independently re-confirmed fixed -- the crop-coordinate half is now ruled
  out for real, but no fresh `ocr_debug.log` from a live Start-Watching session has been pulled
  since Session 82's stale-log finding. Still the next step if that specific report resurfaces.
- `ChevronMarkerFx*` recalibration (only ever confirmed from one Rose Bowl screenshot) -- still
  untouched, still on the list from Session 82.
- Session 81's still-open items (Coffee scorebug overlay work, RAM reader address-locking
  unreliability) -- untouched again this session.
