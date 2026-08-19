# Bandroom Handoff — August 19, 2026 — Session 96

Owner was live in a real game again this session (3rd in a row: Session 94, 95, now 96). Two
threads: an event-naming cleanup pass (owner-driven, UI clarity), and a live cluster of
RAM-reader-data-quality bugs discovered mid-game that were only partially resolved by the end of
the session. **Read the "Not Resolved" section before doing anything else next session** — several
live symptoms were diagnosed but deliberately not chased further tonight.

## Event Naming/Assignment Cleanup (all shipped, built, confirmed compiling)

Root complaint that kicked this off: owner assigned a custom song to the "3rd & Long" card under
the **Defense** tab, but on a snap where their team was on offense, the identically-named "3rd &
Long" card under the **Offense** tab fired instead (unassigned, played the default). Both cards
displayed the exact same string with no indication they're different cards for different sides.

Audited every EventKey's friendly-name mapping across `wwwroot/app.js` (`EVENT_FRIENDLY_NAMES`,
the assign-screen/card source of truth) and `EventActivityLog.cs` (`FriendlyNameOverrides`, the
Event Log's independent label source) -- these two maps have drifted out of sync repeatedly across
past sessions (see that file's own 2026-08-12 FIXED comment) and drifted again this session before
being caught. Changes, all applied to **both** files to keep them in sync:

- **Disambiguated every Offense/Defense pair that shared an identical display name** by prefixing
  with the side, matching the existing "Defense: After Punt" style: 2nd & Long, 2nd & Short,
  3rd & Long, 3rd & Short, and Touchdown now read "Offense: 2nd & Long" / "Defense: 2nd & Long"
  etc. in both the card list and the Event Log.
- **`Defense: Fourth Down`** relabeled "4th Down" -> **"3rd Down Stop"** (owner call: this key
  actually fires when the defense just stopped the 3rd-down attempt, forcing the 4th down --
  "4th Down" was a misleading name for that moment). `Defense: Fourth Down Stop` ("4th Down Stop",
  unchanged) is the actual turnover-on-downs/"switch back to offense" cue the owner was originally
  asking to add -- it already existed from 2026-08-13, just needed confirming, not building.
- **Removed `Offense: Earned First Down Short`** ("1st & Short") as an assignable card entirely --
  pulled from `ConfigStore.AllEngineEventKeys`, added to `RetiredEventKeys` so an empty row prunes
  on load. `FirstDownHelper` still emits the EventKey internally (unchanged, same "removing the UI
  card doesn't touch firing" pattern used for every other retirement in this codebase) -- it just
  has no card to assign a song to anymore, per owner request.
- **`Offense: First Down on First Down`** relabeled "First Down" -> **"First Down on First Down"**
  (was colliding in meaning with the plain "1st Down" card for `Offense: Earned First Down`).
- **Reverted `Penalty: Offense`/`Penalty: Defense`** in `app.js` from "Penalty - Your Team"/
  "Penalty - Opponent" back to **"Penalty - Offense"/"Penalty - Defense"**, matching what
  `EventActivityLog.cs` already had (owner explicitly wants the neutral wording kept -- the two
  files had silently diverged on this pair at some point before this session).
- **New event added**: `Defense: Earned First Down` / `Defense: Earned First Down Short`
  ("Defense: 1st Down Allowed" / "(Short)") -- owner request: a defense-side cue for "the opponent's
  offense just converted a first down against us," the missing Defense counterpart to
  `FirstDownHelper`'s existing Offense-only cue. New standalone evaluator
  `DefenseFirstDownAllowedHelper.cs` mirrors `FirstDownHelper`'s exact logic (including the
  buffered 4th-down conversion-vs-punt disambiguation) rather than sharing code, same
  one-evaluator-per-side pattern as every other Offense/Defense pair in this codebase. Registered
  in `GameWatcher.CreateEventRouter` and `ConfigStore.AllEngineEventKeys`.
- **`TflHelper` no longer double-fires with `DefenseHelper` on a 2nd-down loss** (owner report,
  live log: "Second Down (Loss)" and "Tackle for Loss" both played back-to-back for the same snap).
  Down==3 already had its own specific Loss cue retired in favor of the generic Tackle for Loss
  cue (2026-08-11), but Down==2 never did -- `DefenseHelper`'s down==2 branch and `TflHelper` fire
  off the identical down-advance/YardsToGo-increase detection, so they were guaranteed to co-fire
  on literally every 2nd-down loss. Added a `Down == 2` guard to `TflHelper`, same deferral shape
  as the pre-existing `Down == 4` guard (defers to `BigEventHelper`). Down==3 is now the only case
  where `TflHelper`'s generic cue is the sole cue for a loss.

All of the above built clean (`dotnet build BandAudioHook.csproj`, 0 errors) and the app was
relaunched for the owner to visually confirm the new/renamed cards mid-session.

## RAM Reader Auto-Restart Watchdog (built, NOT yet live-tested)

Owner request: "we need the ram reader permanently on and programmed to reset if it loses
[connection]." Added:

- `GameWatcher.RestartRamReader` (public `Action?` property) -- set by `WebMainForm` right after
  construction to `_scoreboardReaderHost.RestartRamReader()`.
- A watchdog inside `RouteEngineTick` (runs every ~250ms, same cadence as everything else there):
  if `_ramModeEnabled` and `RamReaderStatus` has been anything other than `Connected` for 40
  consecutive ticks (~10s), it logs
  `[ScoreboardReaderHost watchdog] RAM reader not connected for...ms+ -- restarting it` and invokes
  the callback. A 35s floor between restart attempts (`RamRestartCooldown`) stops it from
  restart-looping if the reader keeps failing to re-attach (Coffee's own reader retries self-attach
  for up to ~30s on a cold start, so the cooldown has to clear that window).
- New `ScoreboardReaderHost.RestartRamReader()` method -- unconditional `Stop()` then
  `TryStartRamReader()` (not just `TryStartRamReader()` alone, since a wedged-but-still-alive
  process might not show up as `HasExited` for `IsRunning` to catch on its own).

**This only covers the reader's child process dying or its status file going stale/disconnected.**
It does NOT address the deeper, still-unconfirmed suspicion below that the reader can report
`Connected` while individual fields (specifically `HavePossession`) are quietly stuck unresolved --
that's a different failure mode this watchdog can't see, since status still reads "Connected."

## NOT Resolved -- Diagnosed, Deliberately Not Chased Further Tonight

The owner hit a long, cascading cluster of live symptoms this session, all of which trace back to
what looks like **two root causes**, not independent bugs -- see the last message of the session
for the full reasoning. Flagging explicitly here since none of this was fixed, only diagnosed:

1. **RAM's Down/YardsToGo/possession fields may not be resolving reliably this specific session**,
   even though `CollegeFB27RamReader.exe` was confirmed byte-identical (same MD5) to the owner's
   current `D:\CFB27-Scoreboard-Overlay-v1.4.60` install -- ruling out the Session-95-style
   "stale exe" explanation. The watchdog log's `possession RAM=home OCR=away` lines are misleading:
   that's RAM's **raw** bit, logged regardless of whether `rs.HavePossession` has ever actually
   resolved (`GameWatcher.cs` ~line 2301). If `HavePossession` is stuck `false`, the engine's
   actual routing falls back to OCR's `_lastPossession` unconditionally, even though RAM's raw
   value (visible in the log) was already correct. **Never confirmed live** whether
   `HavePossession` is really the stuck flag -- there's no log line for it currently. Next step if
   this recurs: add a log line exposing `rs.HavePossession`'s raw value directly, since right now
   it's invisible.
2. **Every "Start Watching" click is a deliberate full state reset** (`GameWatcher.Start()` calls
   `CreateEventRouter()` fresh every time, by original 2026-08-1x design -- "Stop Watching then
   start a new game" is supposed to mean a clean slate). If Watching gets restarted mid-game for
   any reason, one-shot flags like `GameStateEventHelper._didFirePregame` reset too, and the next
   kickoff-shaped moment (including the kickoff after an opponent's TD -- confirmed this is exactly
   what happened tonight, "Pregame Take the Field" fired on an Away TD when nothing should have
   played) looks like a fresh pregame/opening-kickoff. This exact side effect was already flagged
   as a known, accepted tradeoff in Session 95's handoff ("would be a separate, larger feature if
   wanted, not attempted this session") -- still true, still not attempted. Building real
   "is this a restart of the same game vs. a genuinely new game" detection remains the actual fix,
   scoped out both sessions now.

Symptoms this session that are believed to be downstream of #1 (not independently investigated
further once the pattern was recognized): wrong-tab Offense/Defense routing on a correctly-detected
3rd down, missing "Defense: After Punt," a rapid-fire cluster of 1st Down/Turnover Forced/After
Punt/1st Down firing within ~7 seconds of each other, and a phantom "Tackle for Loss" on what the
owner confirmed was just an ordinary defensive stop.

## First Thing Next Session

Confirm RAM reader status stays `Connected` for a full game before trusting any further live
symptom reports as "real bugs" rather than "root cause #1 again." If it drops, confirm the new
auto-restart watchdog actually fires (look for the `[ScoreboardReaderHost watchdog]` log line) and
successfully reconnects. If RAM holds solid the whole game and the cascading-misfire cluster still
reproduces, that rules out #1 and it's worth a real, non-live investigation. If it doesn't
reproduce, #1 was very likely the whole story and #2 (restart re-arm) is the only piece still
worth fixing on its own.
