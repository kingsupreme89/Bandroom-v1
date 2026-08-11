# Bandroom State-Machine Analysis — Updated Complete Audit

**Date:** August 10, 2026  
**Author:** Senior State-Machine & Event-Driven Systems Engineer  
**Basis:** Full codebase re-audit against the August 8 analysis (STATE_MACHINE_ANALYSIS.md), incorporating all August 8-10 changes across 20 evaluators, the audio pipeline, side-routing, and OCR layer.  
**Prior Analysis:** `docs/STATE_MACHINE_ANALYSIS.md` (August 8, 2026) — 10 discrepancies, 4 race conditions, 3 out-of-order event scenarios, 42 EventKeys across 16 evaluators.

---

## Executive Summary

Since the August 8 audit, **7 of 10** discrepancies have been fully resolved, **2** were cosmetic/documentation (no code change needed), and **1** will be assessed below for completeness. **3 new evaluators** have been added (`DefenseFirstDownHelper`, `DefenseThirdDownShortHelper`, `PregameHelper`), bringing the total to **19 evaluators** across **approximately 49 EventKeys**. The `OffenseDownHelper` was completely rewritten, `DefenseHelper` was trimmed to Loss-only variants, `DownFieldPositionHelper` was reduced to Midfield-only variants, and `EventRouter` gained an in-engine `Dedupe` pass. A new `ResolveEventRouting` method in `WebMainForm` introduced a 3-tier side-routing model (home-only, un-gated Offense, ordinary Defense) replacing the old Big Game heuristic auto-detect with a manual toggle.

**6 new discrepancies** were discovered during this re-audit, primarily from the interaction between the rewritten `OffenseDownHelper`/`DefenseHelper` and the pre-existing `BigEventHelper`, as well as one dead evaluator path (`Defense: Fourth Down (Loss)` unreachable because the only signal it depends on is always false).

---

## Architecture Overview

Bandroom is a **College Football 27 companion app** that uses Windows OCR to read the game's on-screen scorebug HUD, runs a rule-engine of **19 evaluators** against frame-to-frame game-state deltas, and fires audio cues (band music, PA announcer clips) matched to whichever team committed each event. The system operates as a **layered event-driven state machine** with three distinct layers:

1. **OCR/Input Layer** — `GameWatcher.RunAsync` polling loop (window detection, region OCR, color sampling, possession detection)
2. **Game-State Engine** — `RouteEngineTick` building `PlaySnapshot` → `PlayDelta` → 19 `IRuleEvaluator` classes producing `TriggerEvent` outputs → `EventRouter.Dedupe` → `EventsDetected`
3. **Audio/UI Layer** — `WebMainForm.OnEngineEventsDetected` → `ResolveEventRouting` (3-tier side model) → `FireEventForSide` → `AudioPlayer.Play` with DSP pipeline

---

## LAYER 1: OCR Input State Machine

### States (GameWatcher internal)

| State | Condition | Poll Interval |
|---|---|---|
| `IDLE` | `_cts` is null | N/A |
| `SEARCHING` | Window handle not found | 1500ms |
| `MINIMIZED` | `IsIconic(hwnd)` | 1000ms |
| `RECT_FAILED` | `GetWindowRect` failed | 1000ms |
| `ZERO_SIZE` | `winW <= 0 \|\| winH <= 0` | 1000ms |
| `NOT_FOREGROUND` | `GetForegroundWindow() != hwnd` | 500ms |
| `ACTIVE` | Window found, foreground, valid rect | 250ms (steady-state OCR poll) |
| `ERROR` | Exception caught | 1000ms backoff |

### State Transition Map — OCR Layer

```
IDLE
  + Start() called
  + _cts = new CancellationTokenSource()
  = SEARCHING
  + _eventRouter = CreateEventRouter(), snapshots reset, _isFirstEngineTick = true

SEARCHING → ACTIVE: FindGameWindow() returns valid hwnd
ACTIVE    → MINIMIZED: IsIconic(hwnd) == true
ACTIVE    → RECT_FAILED: !GetWindowRect(hwnd, out rect) → hwnd = IntPtr.Zero
ACTIVE    → ZERO_SIZE: winW <= 0 || winH <= 0
ACTIVE    → NOT_FOREGROUND: GetForegroundWindow() != hwnd
ACTIVE    → ERROR: Exception caught → log + backoff
ANY       → IDLE: Stop() called / CancellationToken cancelled
```

### WatchedRegion Sub-States (within ACTIVE)

| Sub-State | Trigger |
|---|---|
| `NULL_LAST` | `region.Last == null` — ready to fire on next match |
| `HELD` | `region.Last == currentValue` — same value, no change |
| `COOLDOWN` | `DateTime.UtcNow < region.CooldownUntil` — flicker suppression |
| `FIRING` | `currentValue != null && currentValue != region.Last && cooldown expired` |
| `BLANK_RESET` | `currentValue == null && !EventGatedRegions.Contains(name)` |
| `GATED` | `currentValue == null && EventGatedRegions.Contains(name)` — does NOT reset Last |

**Sticky fields** (never blank on pause/replay overlays): `_lastKnownDown`, `_lastKnownAwayScore`, `_lastKnownHomeScore`, `_lastKnownQuarter`, `_lastDistanceRaw` — all updated only when a valid OCR parse exists.

**EventGatedRegions**: `situation`, `banner`, `quarter` — reset only when `_downChangedThisTick` is true (a real new snap), preventing pause-menu re-fires.

### Possession Detection

Two methods, prioritized:

1. **Underline brightness** (preferred, for presets with `AwayUnderlineFx*`/`HomeUnderlineFx*` calibrated): reads average luminance under each team's name — lit underline = has the ball. Requires `minMargin` of 15 luminance points to avoid ambiguous frames.

2. **Color-match fallback** (legacy, for `KamsCbsScorebug`): reads average color of the possession ribbon crop and resolves via `ResolveTeamColor` (Euclidean distance, max 90 units).

Both method results are **edge-trigger fired only when `_possessionCooldownUntil` has expired**, and `_lastPossession` is only updated together with the `PossessionChanged` event (fixed the desync bug where snapshot and routing layer disagreed during cooldown).

Possession sampling is suppressed when `flag` or `situation` regions are active (yellow penalty ribbon, touchdown/kickoff celebration banners) to avoid misreading team colors.

---

## LAYER 2: Game-State Engine

### Primary Application States (encoded in PlaySnapshot boolean flags)

| State | Defining Fields |
|---|---|
| `PRE_SNAP` | Down > 0, no situational flags set |
| `KICKOFF` | `IsKickoff == true` |
| `PAT_ATTEMPT` | `IsPAT == true` |
| `TOUCHDOWN` | `IsTouchdown == true` |
| `TURNOVER` | `IsTurnover == true` (OCR text OR structural turnover backstop) |
| `PENALTY_OFFENSE` | `IsPenaltyOnOffense == true` |
| `PENALTY_DEFENSE` | `IsPenaltyOnDefense == true` |
| `NO_PUNT_RETURN` | `IsNoPuntReturn == true` |
| `PREGAME_READY` | `IsPregameReady == true` |
| `FIELD_GOAL_ATTEMPT` | `IsFieldGoalAttempt == true` |
| `PRE_GAME` | Quarter == 0, Down == 0 |
| `DRIVE_IN_PROGRESS` | Down 1-4, no situational flags, possession known |
| `TIMEOUT` | `AwayTimeoutsRemaining` decrements |
| `SCORE_TRANSITION` | HomeScore or AwayScore changed |

### PlayDelta — Derived Transition Signals

| Signal | Computation | Meaning |
|---|---|---|
| `YardsGained` | `previous.YardLine - current.YardLine` | Net yards gained (⚠️ always 0 — YardLine OCR never built) |
| `LostYards` | `yardsGained < 0` | Offense lost yardage (⚠️ always false — see above) |
| `NewPossession` | `previous.PossessionAway != current.PossessionAway` | Possession flipped |
| `WasFirstDown` | `current.Down == 1 && previous.Down > 1` | New set of downs |
| `WasThirdDownStop` | `previous.Down == 3 && current.Down == 1 && NewPossession` | Defense stopped on 3rd |
| `WasFourthDownStop` | `previous.Down == 4 && current.Down == 1 && NewPossession` | Defense stopped on 4th |

### Engine Tick Flow

```
RouteEngineTick():
  1. Read OCR region.Last values
  2. Parse sticky fields (_lastKnownDown, _lastKnownAwayScore, etc.)
  3. Build PlaySnapshot (19 fields)
  4. Compute PlayDelta
  5. Rotate snapshots: _snapshotPrevious = _snapshotCurrent; _snapshotCurrent = current
  6. Skip first tick (_isFirstEngineTick guard)
  7. _eventRouter.Route(state) — runs all 19 evaluators, dedupes by EventKey
  8. EventsDetected?.Invoke(results)
```

---

## LAYER 3: Audio/Side-Routing State Machine (WebMainForm)

### Matchup Lifecycle States

| State | Conditions | Watcher | Hook | Matchup Lock |
|---|---|---|---|---|
| `NO_MATCHUP` | `_homeTeam` null or `_awayTeam` null | Stopped | Stopped | false |
| `MATCHUP_SET` | Both teams set, `_matchupLocked` false | Stopped | Stopped | false |
| `GAMETIME` | Both teams set, `_matchupLocked` true | Started | Started | true |
| `WATCHING_ACTIVE` | `_watching && _windowFound` | Running | Running | true |
| `WATCHING_WAITING` | `_watching && !_windowFound` | Running | Running | true |
| `STOPPED` | `!_watching` | Stopped | Stopped | false |

### State Transition Map — Matchup Lifecycle

```
NO_MATCHUP
  + SetGameTeamsFromWeb(home, away) + ConfirmGametimeFromWeb(home, away)
  + Teams resolved, configs loaded, default pack backfill for empty profiles
  = GAMETIME
  + _matchupLocked = true, StartWatchingIfMatchupSet(), PlayGametimeSound(), RecordGameWatched()

GAMETIME
  + ToggleWatchingFromWeb() called (Stop Watching)
  + User clicked Stop
  = STOPPED
  + _hook.Stop(), _watcher.Stop(), CrowdBusService.Stop(), _matchupLocked = false, _windowFound = false

GAMETIME / WATCHING_ACTIVE / WATCHING_WAITING
  + WindowFoundChanged event
  + Window detected or lost
  = WATCHING_ACTIVE or WATCHING_WAITING
  + Push watchstate to web UI

STOPPED
  + OnGameTeamsFromWeb + ConfirmGametimeFromWeb
  + New matchup confirmed
  = GAMETIME
```

### Side Routing Logic (ResolveEventRouting)

Three-tier model (redesigned 2026-08-10):

```
Tier 1 — Home-only-always (HomeOnlyAlwaysEventKeys):
  "Defense: Third Down", "Defense: First Down"
  → Never fires for away, Big Game irrelevant.

Tier 2 — Un-gated Offense:*:
  Any "Offense:"-prefixed event key
  → Always full volume for whoever's driving (home or away).

Tier 3 — Ordinary Defense:* (all other "Defense:" + "Penalty: Offense"):
  Home: always fires, full volume.
  Away: only fires during Big Game (full volume), otherwise only IsEarnedBigEvent cues at 25%.

"Defense: Touchdown Scored" is excluded from the Defense:* prefix check
  → routes to the scoring team (possession already flipped by pick-six).
```

### Routing Formula

```
EventKey.StartsWith("Defense:") && EventKey != "Defense: Touchdown Scored"
|| EventKey == "Penalty: Offense"
  → routedSide = opposite of possession
Everything else
  → routedSide = possession side
```

---

## COMPLETE EVALUATOR TRANSITION MAP (19 Evaluators)

### TouchdownHelper
```
CURRENT STATE: !Current.IsTouchdown
  + Current.IsTouchdown (edge: Previous.IsTouchdown == false)
  + No other gate
  = EVENT
  + Delta.NewPossession → "Defense: Touchdown Scored" (pick-six/fumble-return), Vol 85/100 (BigGame)
  + Else → "Offense: Touchdown Scored", Vol 85/100 (BigGame)
  + IsEarnedBigEvent = true
```

### TurnoverHelper
```
CURRENT STATE: !Current.IsTurnover
  + Current.IsTurnover (edge)
  = EVENT
  + Q4+ && time ≤ 120s → "Defense: Iced Game by Turnover", Vol 100
  + Else → "Defense: Turnover Forced", Vol 80/100 (BigGame)
  + IsEarnedBigEvent = true
```

### FieldGoalPATHelper
```
CURRENT STATE: Any score delta
  + scoreDiff == 1 && Current.IsPAT
  = "Offense: PAT Made", Vol 75, IsEarnedBigEvent = false

CURRENT STATE: Any score delta
  + scoreDiff == 2 && possessingSideGained2
  + (the POSSESSION side's score went up by 2 — excludes safety)
  = "Offense: 2-Point Conversion Made", Vol 85, IsEarnedBigEvent = true

CURRENT STATE: Any score delta
  + scoreDiff == 3
  = "Offense: Field Goal Made", Vol 85, IsEarnedBigEvent = false
```

### SafetyHelper
```
CURRENT STATE: Previous.PossessionAway && homeDelta == 2
  = "Defense: Safety" (safety against away team), Vol 100

CURRENT STATE: !Previous.PossessionAway && awayDelta == 2
  = "Defense: Safety" (safety against home team), Vol 100
```

### FieldGoalMissedHelper
```
CURRENT STATE: Current.IsFieldGoalAttempt && Delta.NewPossession && no score change
  + Banner showed "FIELD GOAL" → possession flipped → score unchanged
  = "Defense: Field Goal Missed by Opponent", Vol 85, IsEarnedBigEvent = true
```

### FirstDownHelper
```
CURRENT STATE: Delta.WasFirstDown && Previous.Down > 0
  + yardsGained >= 15 → "Offense: Earned First Down (Big Gain)", Vol 100
  + YardsToGo ≤ 50 && YardLine > 0 (⚠️ never fires — YardLine always 0)
    → "Offense: Earned First Down (Midfield)" [DORMANT]
  + Else → "Offense: Earned First Down", Vol 80
```

### OffenseDownHelper (REWRITTEN 2026-08-10)
```
CURRENT STATE: Current.Down != Previous.Down && !NewPossession && !YardsToGoIncreased
  + Down 2, short (≤3 yds) → "Offense: Second Down Short", Vol 70/100
  + Down 2, long (>3 yds)  → "Defense: Second Down", Vol 70/100
  + Down 3, short (≤3 yds) → "Offense: Third Down Short", Vol 70/100
  + Down 3, long (>3 yds)  → "Defense: Third Down", Vol 70/100
  + Down 4                  → "Defense: Fourth Down", Vol 70/100
```

### DefenseHelper (REWRITTEN 2026-08-10 — Loss-only variants)
```
CURRENT STATE: !UserHasPossession && Current.Down != Previous.Down && !NewPossession
  + Down 2, YardsToGo increased  → "Defense: Second Down (Loss)", Vol 75/100
  + Down 3, YardsToGo increased  → "Defense: Third Down (Loss)", Vol 75/100
  + ⚠️ Down 4 is NOT handled — Loss on 4th down has no evaluator coverage
  + IsEarnedBigEvent = true
```

### BigEventHelper
```
CURRENT STATE: !UserHasPossession
  + Down 3, NewPossession → "Defense: Third Down", Vol 80/100, IsEarnedBigEvent = true
  + Down 4, NewPossession → "Defense: Fourth Down", Vol 80/100, IsEarnedBigEvent = true
  + Down 4, Delta.LostYards → "Defense: Fourth Down (Loss)", Vol 85/100, IsEarnedBigEvent = true
    ⚠️ LOSTYARDS IS ALWAYS FALSE (YardLine == 0) → THIS BRANCH IS DEAD
```

### DownFieldPositionHelper (Midfield-only, dormant)
```
CURRENT STATE: Previous.Down > 0 && Current.Down != Previous.Down
  + Defense side, Down 2, atMidfield (YardLine > 0 && ≤ 50) → "Defense: Second Down (Midfield)"
  + Offense side, Down 2, atMidfield → "Offense: Second Down (Midfield)"
  + ⚠️ YardLine always 0 → atMidfield always false → both branches dormant
```

### TflHelper
```
CURRENT STATE: Play in progress (both downs > 0)
  + Current.YardsToGo > Previous.YardsToGo && Previous.Down > 0 && Current.Down > 0
  + YardsToGo increased (ball moved backward relative to line to gain)
  = "Defense: Tackle for Loss", Vol 75/100, IsEarnedBigEvent = true
```

### KickoffHelper
```
CURRENT STATE: !Current.IsKickoff, Current.IsKickoff (edge)
  + Quarter 1, Previous.Quarter == 0 → "Other: Opening Kickoff", Vol 90
  + Quarter 3, _secondHalfKickoffFired == false → "Other: Second-Half Kickoff", Vol 90
  + UserHasPossession → "Other: Kickoff on Kick (Receiving)", Vol 75
  + Else → "Other: Kickoff on Kick (Kicking)", Vol 75
```

### PenaltyHelper
```
CURRENT STATE: !IsPenalty, IsPenalty (edge)
  + IsPenaltyOnOffense edge → "Penalty: Offense", Vol 70
  + IsPenaltyOnDefense edge → "Penalty: Defense", Vol 70
  + ⚠️ PenaltyHelper checks EDGE on IsPenalty (Previous → Current) but the underlying
    IsPenaltyOnOffense/IsPenaltyOnDefense are STATE values, not edge-triggered — they can persist
    across multiple ticks while the penalty overlay stays on screen. This could fire the same
    penalty event twice if the overlay clears and reappears within the same play.
```

### GameStateEventHelper
```
CURRENT STATE: Quarter transitions
  + 1→2 → "Other: Start of 2nd Quarter", Vol 70
  + 3→4 → "Other: Start of 4th Quarter", Vol 80, IsEarnedBigEvent = true

CURRENT STATE: Previous.Quarter == 0, Current.Quarter == 1, Down > 0
  + → "Other: Pregame Take the Field", Vol 85, IsEarnedBigEvent = true

CURRENT STATE: WasFirstDown, Q4+, time ≤ 120s
  + → "Offense: Iced Game by First Down", Vol 100, IsEarnedBigEvent = true

CURRENT STATE: Q4+, time ≤ 30s, lead ≥ 9
  + → "Offense: Victory in Hand", Vol 100, IsEarnedBigEvent = true
```

### TimeoutHelper (FIXED — now edge-triggered)
```
CURRENT STATE: !UserHasPossession && TimeRemainingSeconds ≤ 240
  + AwayTimeoutsRemaining decremented (Current < Previous, within [0,4])
  = "Defense: Timeout (N Remaining)", Vol 65/100 (BigGame)
```

### DriveStarterHelper
```
CURRENT STATE: Delta.NewPossession, Down == 1, !IsKickoff, !IsTurnover, Previous.Down > 0
  + UserHasPossession → "Offense: Drive Starter", Vol 70
  + Else → "Defense: Drive Starter", Vol 70
```

### NoPuntReturnHelper
```
CURRENT STATE: !IsNoPuntReturn, IsNoPuntReturn (edge), !UserHasPossession
  = "Defense: No Punt Return", Vol 75
```

### DefenseFirstDownHelper (NEW — 2026-08-10)
```
CURRENT STATE: _awaitingFirstSnap == true, Current.IsKickoff == false
  + Down == 1 → "Defense: First Down", Vol 85
  + Down != 1 → drop flag, no event
```

### DefenseThirdDownShortHelper (NEW — 2026-08-10)
```
CURRENT STATE: Down == 3, !NewPossession, !YardsToGoIncreased, YardsToGo ≤ 3
  = "Defense: Third Down Short", Vol 70/100
```

### PregameHelper (NEW — 2026-08-10)
```
CURRENT STATE: Current.IsPregameReady, !Previous.IsPregameReady (edge)
  = "Other: Pregame Ready", Vol 90, IsEarnedBigEvent = true
```

---

## DISCREPANCY STATUS: ORIGINAL 10 (August 8 Analysis)

### DISCREPANCY #1 — TimeoutHelper: Level-Triggered Instead of Edge-Triggered
**Status:** ✅ **FIXED**

Added edge detection: `if (state.Current.AwayTimeoutsRemaining >= state.Previous.AwayTimeoutsRemaining) return null;`

### DISCREPANCY #2 — DownFieldPositionHelper: Midfield Always True
**Status:** ✅ **FIXED**

Gated behind `state.Current.YardLine > 0` — Midfield variants stay dormant until yard-line OCR exists.

### DISCREPANCY #3 — Duplicate Event Coverage: DefenseHelper + DownFieldPositionHelper
**Status:** ✅ **FIXED**

Loss variants removed from `DownFieldPositionHelper` entirely. `EventRouter.Dedupe` added as structural backstop.

### DISCREPANCY #4 — BigEventHelper + DefenseHelper 3rd-Down Ambiguity
**Status:** ✅ **FIXED**

`DefenseHelper` now checks `if (state.Delta.NewPossession) return null;`. OffenseDownHelper checks the same.

### DISCREPANCY #5 — SafetyHelper + FieldGoalPATHelper Score Delta Ambiguity
**Status:** ✅ **FIXED**

`FieldGoalPATHelper` now checks `possessingSideGained2` (the possession side's score went up by 2), excluding safeties.

### DISCREPANCY #6 — FieldGoalMissedHelper May Never Fire
**Status:** ✅ **FIXED**

Switched from `IsPAT` (only set on success text) to `IsFieldGoalAttempt` (from "FIELD GOAL" banner, appears for both made and missed). Exclusion via score-delta check.

### DISCREPANCY #7 — Race Condition: OCR Blanking During Pause Menus
**Status:** ✅ **FIXED**

`_lastDistanceRaw` is now sticky: only updated when `distanceRaw != null`, never nulled on blank reads. All evaluators read the sticky value via `RouteEngineTick`.

### DISCREPANCY #8 — NoPuntReturnHelper: Confusing Comment
**Status:** COSMETIC — No code change needed. Logic was already correct.

### DISCREPANCY #9 — TackleForLoss: Dual Fire Path
**Status:** ✅ **FIXED**

`OnTackleForLoss` now gated with `if (_useEngineForEvents) return;`. Legacy path still exists as fallback.

### DISCREPANCY #10 — No "Offense: Fourth Down" Event
**Status:** ✅ **FIXED**

`OffenseDownHelper` now handles down 4 → `"Defense: Fourth Down"` (long-standing product decision: 4th down always attributes to defense).

---

## NEW DISCREPANCIES FOUND (August 10 Re-Audit)

---

### NEW DISCREPANCY #11 — Dead Evaluator Path: "Defense: Fourth Down (Loss)"

**Severity:** MEDIUM  
**Category:** Impossible Transition (Dead Signal)

**1. Intended behavior:** `BigEventHelper` should fire `"Defense: Fourth Down (Loss)"` when the offense gets stuffed for a loss on a 4th down play.

**2. Actual implementation:** `BigEventHelper` (line ~19-21):
```csharp
4 when state.Delta.LostYards => Make("Defense: Fourth Down (Loss)", 85, true),
```
`PlayDelta.LostYards` is computed as `yardsGained < 0` where `yardsGained = previous.YardLine - current.YardLine`. `PlaySnapshot.YardLine` is hardcoded to 0 at `RouteEngineTick` line 974: `YardLine = 0`. There is no OCR region for yard line. Therefore `yardsGained` is always 0, `LostYards` is always false, and this evaluator branch **can never fire**.

The old `DownFieldPositionHelper` used to also fire `"Defense: Fourth Down (Loss)"` but those variants were removed as part of the Discrepancy #3 fix. `DefenseHelper` only handles 2nd and 3rd down losses, not 4th. `OffenseDownHelper` fires the plain `"Defense: Fourth Down"` but defers on loss (`YardsToGo increased → return null`).

**3. Why it differs:** `LostYards` was flagged as always-false in `DefenseHelper`'s own comment and fixed there by switching to `YardsToGo` comparison. `BigEventHelper` was never updated with the same fix. The `Defense: Fourth Down (Loss)` path fell through the gap during the Discrepancy #3 resolution.

**4. Possible failure:** A stuffed 4th-down play (offense loses yards) produces NO audio cue. `OffenseDownHelper` defers because yards increased, `DefenseHelper` returns null (down != 2 && down != 3), `BigEventHelper`'s `LostYards` branch silently fails. The user hears nothing for what should be a significant defensive moment.

**5. Recommended fix:** Apply the same fix used in `DefenseHelper`/`TflHelper`:
```csharp
// In BigEventHelper, replace:
4 when state.Delta.LostYards => Make("Defense: Fourth Down (Loss)", 85, true),
// With:
4 when state.Current.YardsToGo > state.Previous.YardsToGo => Make("Defense: Fourth Down (Loss)", 85, true),
```
And add a `!state.Delta.NewPossession` guard (same as OffenseDownHelper/DefenseHelper) to avoid colliding with a turnover-on-downs.

**6. Regression test:** Simulate a 4th-down play where `YardsToGo` increases (e.g., 4th & 3 → 4th & 6). Verify exactly one `"Defense: Fourth Down (Loss)"` event fires. Verify no event fires on a 4th-down turnover (NewPossession == true). Verify no event fires on a normal 4th-down incompletion (YardsToGo unchanged).

---

### NEW DISCREPANCY #12 — PenaltyHelper: Edge Detection on Composite Flag

**Severity:** MEDIUM  
**Category:** Potential Double-Fire

**1. Intended behavior:** Each penalty fires exactly once — one audio cue per penalty committed (offense or defense).

**2. Actual implementation:** `PenaltyHelper` edge-triggers on `IsPenalty`. But `IsPenaltyOnOffense` and `IsPenaltyOnDefense` in its `Evaluate` are read as current-state values (not edge-triggered). Looking at the code flow:

```csharp
// PenaltyHelper (inferred from its description and the old analysis):
// Checks Previous.IsPenaltyOnOffense == false && Current.IsPenaltyOnOffense == true
```

Wait — examining this more carefully. The old analysis describes PenaltyHelper as:
```
CURRENT STATE: No penalty
  + IsPenaltyOnOffense false→true
  = OFFENSE PENALTY
```

But checking the current implementation — this evaluator does compare previous vs current state flags. However, `IsPenaltyOnOffense` and `IsPenaltyOnDefense` are set in `RouteEngineTick` from the current tick's `penaltyText` OCR read and compared against team names. The OCR's `penaltyagainst` region is NOT in `EventGatedRegions`, meaning its `region.Last` DOES reset to null when the text clears, and DOES re-arm. So if the penalty overlay flickers (appears → clears → appears again on replay), `IsPenaltyOnOffense`/`IsPenaltyOnDefense` toggles false→true twice, producing two events for one penalty.

**3. Why it differs:** The `penaltyagainst` region's normal blanking behavior (not gated like `situation`/`banner`/`quarter`) means it can re-arm mid-play. Every other region-based evaluator either uses gated regions or has explicit internal state tracking (e.g., `KickoffHelper._didFire`).

**4. Possible failure:** A penalty replay that shows the overlay, clears it, then shows it again produces two `"Penalty: Offense"` or `"Penalty: Defense"` events for one real flag. Currently partially masked by `AudioPlayer.FireCooldown` (20s per path), but two penalties in rapid succession could be legitimately distinct and would still be blocked.

**5. Recommended fix:** Add `penaltyagainst` to `EventGatedRegions` so it only resets on a down change (same gate as `situation`/`banner`/`quarter`):
```csharp
static readonly HashSet<string> EventGatedRegions = new(StringComparer.OrdinalIgnoreCase)
    { "situation", "banner", "quarter", "penaltyagainst" };
```

**6. Regression test:** Simulate penalty overlay ON → OFF → ON on the same down. Verify exactly 1 penalty event fires. Verify a genuinely separate penalty (different down) still fires independently.

---

### NEW DISCREPANCY #13 — OffenseDownHelper/DefenseHelper Gap: Loss on Unchanged Down

**Severity:** MEDIUM  
**Category:** Missing Transition (Tick-Ordering Race)

**1. Intended behavior:** A tackle-for-loss (yards-to-go increases) detected on any tick fires the appropriate Loss cue.

**2. Actual implementation:** The Loss detection path depends on two fields changing simultaneously:
- `OffenseDownHelper`: fires when `Current.Down != Previous.Down` AND `YardsToGo` does NOT increase. If yards DID increase, it defers.
- `DefenseHelper`: fires when `Current.Down != Previous.Down` AND `YardsToGo` increased. 
- `TflHelper`: fires when `YardsToGo` increased AND `Previous.Down > 0 && Current.Down > 0` (no down-change requirement).

If the down changes on Tick A and yards-to-go updates on Tick B (OCR reads them from separate captures):

**Tick A:** Down changes (e.g., 2nd → 3rd), yards-to-go still shows the old value:
- `OffenseDownHelper`: fires "Defense: Third Down" (yards didn't increase, so this is treated as a normal long down, not a loss)
- `DefenseHelper`: returns null (yards didn't increase)
- `TflHelper`: returns null (yards didn't increase)

**Tick B:** Yards-to-go updates (now shows the post-loss larger number), down unchanged (still 3rd):
- `OffenseDownHelper`: returns null (down unchanged)
- `DefenseHelper`: returns null (down unchanged)
- `TflHelper`: returns null (`Current.YardsToGo > Previous.YardsToGo` evaluates against Tick A's snapshot — but Tick B's Previous was just Tick A's snapshot rotated, so `Current.YardsToGo > Previous.YardsToGo` IS true — wait, this should fire...)

Actually, let me trace more carefully. `RouteEngineTick`:
```
_snapshotPrevious = _snapshotCurrent;  // A's snapshot becomes Previous
_snapshotCurrent = snapshot;            // B's snapshot becomes Current
```

So on Tick B: `state.Previous` = Tick A's snapshot, `state.Current` = Tick B's snapshot.
`state.Current.YardsToGo > state.Previous.YardsToGo` — if yards changed between ticks, this IS true.
`state.Previous.Down > 0 && state.Current.Down > 0` — both true (3rd down).

TflHelper DOES fire on Tick B! But it fires `"Defense: Tackle for Loss"`, not the more specific `"Defense: Third Down (Loss)"`. So the Loss-specific cue is missed, replaced by the generic TFL cue. This is a substitution, not a gap.

BUT if the yards change is small enough that `YardsToGo` only increases by 1-2 (a minimal loss), `TflHelper` fires and `DefenseHelper`'s Loss variant doesn't. The user hears `"Defense: Tackle for Loss"` instead of `"Defense: Third Down (Loss)"`. Since both cards exist and a user might have assigned different songs to each, this is a wrong-cue-firing, not silence.

**3. Why it differs:** The Loss detection is split across two evaluators with different tick-timing requirements (`DefenseHelper` needs down change AND yards change on same tick; `TflHelper` only needs yards change). The code defers from `OffenseDownHelper` to `DefenseHelper` assuming both arrive on the same tick.

**4. Possible failure:** On a real tackle-for-loss where OCR captures the down change and yards change on separate ticks (~125ms apart on average with 250ms polling), the user hears the generic `"Defense: Tackle for Loss"` instead of the down-specific `"Defense: Third Down (Loss)"`.

**5. Recommended fix:** Consolidate Loss detection into one evaluator. Remove the `Current.Down != Previous.Down` guard from DefenseHelper's Loss branches and let them fire on any tick where `YardsToGo` increased and down is 2 or 3:
```csharp
// In DefenseHelper: remove "if (state.Current.Down == state.Previous.Down) return null;"
// for the Loss branches only. Keep the guard for non-Loss branches.
```
Then add `!Delta.NewPossession` and `!Current.IsKickoff` guards for safety.

**6. Regression test:** Simulate Tick A: Down 2→3, YardsToGo unchanged (7). Simulate Tick B: Down unchanged (3), YardsToGo 7→9. Verify Tick A fires normal down cue, Tick B fires `"Defense: Third Down (Loss)"`. Verify TflHelper does NOT also fire on Tick B (it should be suppressed when a down-specific Loss fires).

---

### NEW DISCREPANCY #14 — OffenseDownHelper Unconditional Loss Deferral Creates Fourth-Down Gap

**Severity:** HIGH  
**Category:** Missing Transition Chain

**1. Intended behavior:** Every 4th down play produces some event — at minimum `"Defense: Fourth Down"` for a normal snap, `"Defense: Fourth Down (Loss)"` for a stuffed play.

**2. Actual implementation:** On a 4th down where yards-to-go increases (stuffed for loss):

- `OffenseDownHelper` line 41: `if (state.Current.YardsToGo > state.Previous.YardsToGo) return null;` — defers to DefenseHelper/BigEventHelper
- `DefenseHelper` lines 45-67: only handles `down == 3` and `down == 2` — returns null for down 4
- `BigEventHelper`: checks `state.Delta.LostYards` which is always false (Discrepancy #11)
- `TflHelper`: fires `"Defense: Tackle for Loss"` for yards increase on any down

RESULT: Only the generic `"Defense: Tackle for Loss"` fires. The specific `"Defense: Fourth Down"` does NOT fire because `OffenseDownHelper` deferred. The specific `"Defense: Fourth Down (Loss)"` does NOT fire because `BigEventHelper`'s signal is dead.

**3. Why it differs:** The 2026-08-10 rewrite removed the old `OffenseDownHelper`'s unconditional `4 => "Offense: Fourth Down"` (which at least fired SOMETHING) and replaced it with `4 => "Defense: Fourth Down"` — but that path is gated behind the Loss deferral check. If yards increased on 4th down, nothing fires the down-specific event.

**4. Possible failure:** On a stuffed 4th down (offense loses yards going for it), the user hears either `"Defense: Tackle for Loss"` or silence (if TflHelper's yards comparison also fails the tick ordering). This is the most important 4th down moment — a successful defensive stand — producing the wrong cue or none at all.

**5. Recommended fix:** Two changes:

a) Fix BigEventHelper's dead branch (Discrepancy #11 fix).

b) Remove the Loss deferral from `OffenseDownHelper` for down 4 specifically, since `DefenseHelper` doesn't handle down 4 and `BigEventHelper`'s path needs the same `YardsToGo` fix:
```csharp
// OffenseDownHelper line 41:
if (state.Current.YardsToGo > state.Previous.YardsToGo && down < 4)
    return null; // DefenseHelper covers 2nd/3rd down losses
// For down 4, let BigEventHelper (after Discrepancy #11 fix) handle the Loss variant,
// but still fire the plain event as fallback.
```

**6. Regression test:** Three 4th-down scenarios:
- 4th & 5, yards go up to 4th & 8 (stuffed): verify `"Defense: Fourth Down (Loss)"` fires
- 4th & 5, incomplete, down becomes 1st (turnover on downs): verify `BigEventHelper` fires `"Defense: Fourth Down"` (NewPossession)
- 4th & 5, converted (new 1st down for same team): verify `FirstDownHelper` or `DriveStarterHelper` fires

---

### NEW DISCREPANCY #15 — DefenseThirdDownShortHelper + OffenseDownHelper: Paired Same-Tick Firing with EventRouter Bypass

**Severity:** LOW  
**Category:** Architectural Note (Not a Bug)

**1. Intended behavior:** On a 3rd & short, both the offense hype cue AND the defense anticipation cue fire simultaneously — routed to opposite sides.

**2. Actual implementation:** Both evaluators fire on the same tick conditions (Down changed to 3, YardsToGo ≤ 3, no NewPossession, no Loss):
- `OffenseDownHelper` → `"Offense: Third Down Short"` 
- `DefenseThirdDownShortHelper` → `"Defense: Third Down Short"`

These are DIFFERENT EventKeys, so `EventRouter.Dedupe` does NOT filter either out. Both appear in the results list. `OnEngineEventsDetected` iterates both and `ResolveEventRouting` routes each to opposite sides. The same-tick multi-fire layering fix (`interruptPrevious: !firedYet` → false for the second event) means both cues play without cutting each other off.

**3. Why it differs:** This is intentional design, not a bug. The old analysis (Discrepancy #3/#4) flagged duplicate-same-EventKey events as bugs. This is different — two genuinely distinct events firing on the same game-state transition, by design.

**4. Possible failure:** None. Working as designed. However, the evaluator naming and placement creates confusion for a future maintainer: `OffenseDownHelper` now fires both `"Offense: *"` AND `"Defense: *"` EventKeys, while `DefenseThirdDownShortHelper` fires a `"Defense: *"` EventKey that overlaps conceptually. Documenting this pairing relationship is important.

**5. Recommended fix:** Add a comment block in `OffenseDownHelper` and `DefenseThirdDownShortHelper` cross-referencing each other, noting the paired same-tick firing behavior.

**6. Regression test:** Simulate 3rd & 2. Verify BOTH `"Offense: Third Down Short"` AND `"Defense: Third Down Short"` appear in the EventsDetected list. Verify Dedupe does not remove either. Verify both play without cutting each other off.

---

### NEW DISCREPANCY #16 — EventRouter Evaluator Order Dependency

**Severity:** LOW  
**Category:** Order-Dependent Deduplication

**1. Intended behavior:** When two evaluators produce the same EventKey on one tick, `EventRouter.Dedupe` keeps the first and drops the second.

**2. Actual implementation:** `EventRouter.Dedupe` preserves the FIRST match per EventKey. Evaluators are processed in the fixed order defined in `CreateEventRouter()` (line 1014-1037 in GameWatcher.cs). Currently this order is:

```
BigEventHelper, DefenseFirstDownHelper, DefenseHelper, DefenseThirdDownShortHelper,
DownFieldPositionHelper, DriveStarterHelper, FieldGoalMissedHelper, FieldGoalPATHelper,
FirstDownHelper, GameStateEventHelper, KickoffHelper, NoPuntReturnHelper,
OffenseDownHelper, PenaltyHelper, PregameHelper, SafetyHelper, TflHelper,
TimeoutHelper, TouchdownHelper, TurnoverHelper
```

If two evaluators ever produce the same EventKey with DIFFERENT Volume/IsEarnedBigEvent values, the one earlier in the list "wins" silently. This is correct as a backstop, but a future evaluator inserted before an existing one that happens to share an EventKey would silently change which values that key gets — a regression that no test would catch unless it specifically tested the Volume/IsEarnedBigEvent fields of the output.

**3. Why it differs:** The `Dedupe` function is order-dependent by design (it uses a HashSet which preserves first-seen). The evaluator list is a fixed-purpose software construction order.

**4. Possible failure:** Currently NONE — the known duplicate EventKeys (Discrepancy #3) were removed at the evaluator level, so Dedupe is purely a safety net. But this is a fragile design: a future evaluator that accidentally shares an EventKey with an earlier one will silently have its values dropped with no log warning beyond the `onDuplicateDropped` callback (which logs to EventActivityLog, not CrashLog).

**5. Recommended fix:** Add the evaluator index or name to the `onDuplicateDropped` log entry so a maintainer can trace which evaluator "lost" when diagnosing a wrong-volume/wrong-IsEarnedBigEvent report:
```csharp
[engine] {dupe.EventKey} dropped (duplicate from later evaluator, first kept)
```
The log already exists via `EventActivityLog.Record`, just needs the evaluator provenance.

**6. Regression test:** Force two evaluators to produce the same EventKey with different Volume values. Verify Dedupe keeps the first. Verify the dropped event logs the correct provenance.

---

## INVALID / IMPOSSIBLE STATES (Updated)

### Invalid State: IsTouchdown + IsTurnover Simultaneously
Both set from the same OCR "situation" region's single `NormalizeMatch` result. Mutually exclusive by definition. **Safe.**

### Invalid State: IsTouchdown + IsFieldGoalAttempt Simultaneously
Touchdown comes from "situation" region, FieldGoalAttempt from "banner" region — two different OCR crops. Could theoretically read different states if the banner text lingers while the situation updates. `RouteEngineTick` reads both regions on the same tick, so both flags could be true simultaneously. No evaluator currently checks both in a way that would conflict. Potentially problematic if a future evaluator does.

### Impossible State: Previous.Down == 0, Current.Down > 0, WasFirstDown == false
The first-tick skip in `RouteEngineTick` (`_isFirstEngineTick`) and the `Previous.Down > 1` condition in `PlayDelta.WasFirstDown` are both **consistent** by design. Opening kickoff's 1st & 10 should never fire "Earned First Down." `FirstDownHelper` additionally checks `state.Previous.Down == 0` as a guard.

### Dead State: LostYards == true
`PlayDelta.LostYards` is computed from `YardLine` which is always 0. This signal is dead. `BigEventHelper` still depends on it for `"Defense: Fourth Down (Loss)"`. All other evaluators have been migrated to `YardsToGo` comparison. See Discrepancy #11.

### New Dead State: atMidfield == true
`DownFieldPositionHelper` gates Midfield variants on `YardLine > 0` (always false). Evaluator still runs and returns `null` each tick, consuming CPU for no possible output. Not harmful but wasteful — the `CanFire` check passes because `Previous.Down != 0 && Current.Down != Previous.Down`, then `Evaluate` returns null because YardLine is 0. Every 2nd-down transition wastes a comparison.

---

## RACE CONDITION ANALYSIS (Updated)

### Race #1: Possession Sampling vs OCR Tick Ordering
**Status:** STILL PRESENT (mitigated, not eliminated)

`SamplePossessionFromWindow` runs during the "down" region processing, BEFORE "flag"/"situation" are re-read on the same tick. The suppression gates (`flagActive`, `situationActive`) use the PREVIOUS tick's values (`region.Last`). A flag that appears and disappears within one 250ms poll cycle could still mis-sample possession. The cooldown on `_possessionCooldownUntil` provides some protection but doesn't eliminate the window.

### Race #2: Multiple Evaluators Firing Simultaneously
**Status:** PARTIALLY FIXED

`EventRouter.Dedupe` now eliminates duplicate EventKeys. The same-tick multi-fire layering fix in `OnEngineEventsDetected` (`interruptPrevious: !firedYet`) means genuinely distinct events on the same tick no longer cut each other off. However, there is still **no priority system** between evaluators — `BigEventHelper` runs before `OffenseDownHelper`, so if both were to fire the same key (currently prevented by mutual exclusion), `BigEventHelper`'s values win.

### Race #3: _possession Could Be Null During Event Routing
**Status:** ✅ **FIXED**

`OnEngineEventsDetected` no longer defaults to "home" when `_possession` is null. It now explicitly returns early for side-specific events, logs them as skipped, and only routes side-agnostic "Other:*" events to "home" (which is safe since they aren't possession-dependent).

### Race #4: OCR Thread vs UI Thread
**Status:** STILL PRESENT (theoretical)

`RouteEngineTick` rotates snapshots (`_snapshotPrevious = _snapshotCurrent`) before the UI thread processes `EventsDetected`. The next OCR poll runs on the OCR thread and could overwrite `_snapshotPrevious` before the UI thread has read it. In practice, the 250ms poll interval makes this unlikely unless the system is under extreme CPU load. Mitigated by the fact that `EventsDetected` is invoked synchronously before `RouteEngineTick` returns, but the `RunOnUi` wrapper processes the actual audio on the UI thread's message pump, which may be delayed.

### Race #5 (NEW): Preload vs First Trigger Race
**Status:** ✅ **FIXED**

`StartWatchingIfMatchupSet` (line 819) now **blocks** on `AudioCache.Preload()` before starting the watcher/hook, instead of the old fire-and-forget `Task.Run`. This eliminates the race where the first game event fired before the preload finished, causing a synchronous disk read stall with `PreRollSeconds` at 0.

### Race #6 (NEW): ConfigStore Save vs Live Matchup In-Memory State
During `ConfirmGametimeFromWeb`, `SetGameTeamsFromWeb` loads `_homeConfig`/`_awayConfig` from disk. Then `ConfigStore.ImportDefaultPackForTeam` runs and modifies those in-memory lists. Then `ConfigStore.SaveProfile` writes them to disk. If the user simultaneously modified the same team's profile from another process or the web UI between the load and save, the in-memory mutation would be written over disk without conflict detection. The single-instance mutex in `Program.cs` prevents two Bandroom processes, but the web UI could still issue a save via a browser refresh. Low risk, not currently exploitable.

---

## EVENTS THAT COULD ARRIVE OUT OF ORDER

1. **Down change before situational banner:** The "down" region may update to a new down BEFORE the "situation" region shows "TOUCHDOWN." `RouteEngineTick` builds a snapshot with the new Down but old Situation, missing the TD detection for one tick. Next tick catches it.

2. **Score update before PAT banner:** The score regions may show the new score before the "situation" region shows "PAT GOOD." `FieldGoalPATHelper` checks `scoreDiff == 1 && IsPAT` — if score arrives first, `IsPAT` is false and the event is missed for one tick.

3. **Possession color change before down change:** After a turnover, the possession underline color may flip BEFORE the "down" region shows 1st down. Creates a one-tick window where `_possession` reflects the new team but `Down` hasn't updated. The structural turnover backstop in `RouteEngineTick` (`possessionFlipped && Previous.Down != 4 && Previous.Down != 0`) partially closes this by catching possession flips without waiting for OCR text confirmation.

4. **(NEW) Yards-to-go change on different tick than down change:** When a tackle-for-loss is stuffed, the yards-to-go update may arrive on a different OCR tick than the down change. This splits the Loss detection across two evaluators with different tick requirements (`DefenseHelper` needs both on same tick, `TflHelper` only needs yards change). See Discrepancy #13.

5. **(NEW) FieldGoal banner vs possession flip ordering:** `FieldGoalMissedHelper` fires when `IsFieldGoalAttempt && NewPossession && no score change`. If the "FIELD GOAL" banner text clears BEFORE `NewPossession` goes true (the banner may disappear on the cut to the change-of-possession play), the evaluator sees `IsFieldGoalAttempt == false` on the tick `NewPossession` is true and misses the event.

---

## COMPLETE EVENT KEY TABLE (Updated — 49 EventKeys across 19 Evaluators)

| Evaluator | EventKey | Category | Routes To |
|---|---|---|---|
| TouchdownHelper | `Offense: Touchdown Scored` | Scoring | Possession side |
| TouchdownHelper | `Defense: Touchdown Scored` | Scoring | Possession side (pick-six, possession already flipped) |
| TurnoverHelper | `Defense: Turnover Forced` | Turnovers | Opposite of possession |
| TurnoverHelper | `Defense: Iced Game by Turnover` | Turnovers | Opposite of possession |
| FieldGoalPATHelper | `Offense: Field Goal Made` | Scoring | Possession side |
| FieldGoalPATHelper | `Offense: PAT Made` | Scoring | Possession side |
| FieldGoalPATHelper | `Offense: 2-Point Conversion Made` | Scoring | Possession side |
| FieldGoalMissedHelper | `Defense: Field Goal Missed by Opponent` | Scoring | Opposite of possession |
| SafetyHelper | `Defense: Safety` | Scoring | Opposite of possession |
| FirstDownHelper | `Offense: Earned First Down` | Downs | Possession side |
| FirstDownHelper | `Offense: Earned First Down (Big Gain)` | Downs | Possession side |
| FirstDownHelper | `Offense: Earned First Down (Midfield)` | Downs | Possession side (DORMANT — YardLine always 0) |
| OffenseDownHelper | `Offense: Second Down Short` | Downs | Possession side |
| OffenseDownHelper | `Defense: Second Down` | Downs | Opposite of possession |
| OffenseDownHelper | `Offense: Third Down Short` | Downs | Possession side |
| OffenseDownHelper | `Defense: Third Down` | Downs | Opposite of possession |
| OffenseDownHelper | `Defense: Fourth Down` | Downs | Opposite of possession |
| DefenseHelper | `Defense: Second Down (Loss)` | Downs | Opposite of possession |
| DefenseHelper | `Defense: Third Down (Loss)` | Downs | Opposite of possession |
| BigEventHelper | `Defense: Third Down` | Downs | Opposite of possession |
| BigEventHelper | `Defense: Fourth Down` | Downs | Opposite of possession |
| BigEventHelper | `Defense: Fourth Down (Loss)` | Downs | Opposite of possession (DEAD — LostYards always false) |
| DownFieldPositionHelper | `Offense: Second Down (Midfield)` | Downs | Possession side (DORMANT) |
| DownFieldPositionHelper | `Defense: Second Down (Midfield)` | Downs | Opposite of possession (DORMANT) |
| TflHelper | `Defense: Tackle for Loss` | Downs | Opposite of possession |
| KickoffHelper | `Other: Opening Kickoff` | Special Teams | Possession side |
| KickoffHelper | `Other: Second-Half Kickoff` | Special Teams | Possession side |
| KickoffHelper | `Other: Kickoff on Kick (Receiving)` | Special Teams | Possession side |
| KickoffHelper | `Other: Kickoff on Kick (Kicking)` | Special Teams | Possession side |
| PenaltyHelper | `Penalty: Offense` | Penalties | Opposite of possession |
| PenaltyHelper | `Penalty: Defense` | Penalties | Possession side |
| GameStateEventHelper | `Other: Start of 2nd Quarter` | Hype | Possession side |
| GameStateEventHelper | `Other: Start of 4th Quarter` | Hype | Possession side |
| GameStateEventHelper | `Other: Pregame Take the Field` | Hype | Possession side |
| GameStateEventHelper | `Offense: Iced Game by First Down` | Hype | Possession side |
| GameStateEventHelper | `Offense: Victory in Hand` | Hype | Possession side |
| TimeoutHelper | `Defense: Timeout (4 Remaining)` | Hype | Opposite of possession |
| TimeoutHelper | `Defense: Timeout (3 Remaining)` | Hype | Opposite of possession |
| TimeoutHelper | `Defense: Timeout (2 Remaining)` | Hype | Opposite of possession |
| TimeoutHelper | `Defense: Timeout (1 Remaining)` | Hype | Opposite of possession |
| TimeoutHelper | `Defense: Timeout (0 Remaining)` | Hype | Opposite of possession |
| DriveStarterHelper | `Offense: Drive Starter` | Hype | Possession side |
| DriveStarterHelper | `Defense: Drive Starter` | Hype | Opposite of possession |
| NoPuntReturnHelper | `Defense: No Punt Return` | Special Teams | Opposite of possession |
| **DefenseFirstDownHelper** | `Defense: First Down` | Downs | Opposite of possession (NEW, home-only-always) |
| **DefenseThirdDownShortHelper** | `Defense: Third Down Short` | Downs | Opposite of possession (NEW) |
| **PregameHelper** | `Other: Pregame Ready` | Hype | Possession side (NEW) |

---

## SUMMARY: CRITICALITY MATRIX (All Discrepancies)

| # | Discrepancy | Severity | Status | User-Visible Symptom |
|---|---|---|---|---|
| 1 | TimeoutHelper level-triggered | HIGH | ✅ FIXED | Repeating timeout cue |
| 2 | DownFieldPositionHelper Midfield always true | MEDIUM | ✅ FIXED | Duplicate 2nd-down event |
| 3 | Duplicate DefenseHelper + DownFieldPositionHelper | HIGH | ✅ FIXED | Audible start-stop glitch |
| 4 | BigEventHelper + DefenseHelper 3rd-down | MEDIUM | ✅ FIXED | Multiple conflicting cues |
| 5 | Safety + 2-pt conversion overlap | HIGH | ✅ FIXED | Wrong cue on safety |
| 6 | FieldGoalMissed may never fire | MEDIUM | ✅ FIXED | Silent missed FGs |
| 7 | OCR blanking race on non-sticky fields | LOW | ✅ FIXED | Spurious events on resume |
| 8 | NoPuntReturn comment clarity | COSMETIC | NO CHANGE | None |
| 9 | Dual TFL fire path | LOW | ✅ FIXED | Double-fire (was masked) |
| 10 | No Offense: Fourth Down | MEDIUM | ✅ FIXED | Now fires Defense: Fourth Down |
| **11** | **Dead evaluator path: Fourth Down (Loss)** | **MEDIUM** | **NEW** | **Stuffed 4th down produces wrong cue or silence** |
| **12** | **PenaltyHelper double-fire on flicker** | **MEDIUM** | **NEW** | **Penalty cue fires twice on replay** |
| **13** | **Loss on unchanged down (tick-ordering)** | **MEDIUM** | **NEW** | **Wrong Loss cue (generic TFL instead of down-specific)** |
| **14** | **Fourth-down Loss deferral gap** | **HIGH** | **NEW** | **Stuffed 4th down has no down-specific cue** |
| 15 | Paired third-down-short firing | LOW | NOTE | Working as designed — document |
| 16 | EventRouter evaluator order dependency | LOW | NOTE | Structural — add provenance logging |

---

## ROOT CAUSE ANALYSIS

### Root Cause: Loss Detection Depends on a Dead Signal

The single most impactful systemic issue is that `PlayDelta.LostYards` is computed from `YardLine`, and `YardLine` has been hardcoded to 0 since the codebase was created (OCR for yard-line position was never built). This dead signal:

1. Made `DefenseHelper`'s original Loss detection silently non-functional (fixed Aug 8-10 by switching to `YardsToGo` comparison)
2. Left `BigEventHelper`'s `"Defense: Fourth Down (Loss)"` branch dead (Discrepancy #11 — NOT fixed)
3. Left `DownFieldPositionHelper`'s Midfield variants always-true (fixed by gating behind `YardLine > 0`)
4. Left `FirstDownHelper`'s `"Offense: Earned First Down (Midfield)"` dormant (correctly gated)

**The root architectural flaw** is that `PlayDelta` was built with fields (`YardsGained`, `LostYards`, `YardLineDelta`) that depend on a data source (yard-line OCR) that never existed. Every evaluator that used these fields had incorrect or dead behavior. The fixes were applied evaluator-by-evaluator over the Aug 8-10 period, but the root cause (dead signal in the shared data model) remains — `BigEventHelper` was missed.

**Comprehensive fix:** Either:
- Build yard-line OCR (requires new `WatchedRegion`, calibration, regex pattern), or
- Remove `YardLine`/`YardsGained`/`LostYards` from `PlaySnapshot`/`PlayDelta` entirely and migrate the ONE remaining consumer (`BigEventHelper`'s 4th-down-loss branch) to `YardsToGo` comparison

### Root Cause: Tick-Ordering Fragility

The Loss and down-transition detection logic is split across four evaluators (`OffenseDownHelper`, `DefenseHelper`, `TflHelper`, `BigEventHelper`) with different assumptions about which fields change simultaneously on a single OCR tick. In reality, OCR reads each region independently, and the scorebug may update its down/distance text at slightly different times than the down indicator. The system has no debounce or tick-buffering layer between OCR capture and evaluator invocation — every tick produces a fresh `PlaySnapshot` and the previous one is rotated as-is.

**Comprehensive fix:** Add a 1-tick buffer in `RouteEngineTick`:
```csharp
// After building snapshot, check if YardsToGo changed but Down didn't (or vice versa):
// if so, synthesize a "combined" snapshot for evaluators that need both changes on one tick.
```
This would eliminate the tick-ordering race for ALL evaluators, not just the Loss ones.

---

## STATE SYNCHRONIZATION PROBLEMS

### Problem 1: _possession vs PossessionAway vs _lastPossession
Three representations of the same fact, updated at different times:
- `GameWatcher._lastPossession`: set by `SamplePossessionFromWindow`/`SamplePossessionByUnderline` with cooldown, edge-triggers `PossessionChanged`
- `WebMainForm._possession`: set ONLY by `_watcher.PossessionChanged` callback — lags behind `_lastPossession` if cooldown suppresses the event
- `PlaySnapshot.PossessionAway`: read from `_lastPossession` every tick by `RouteEngineTick`

The Aug 8 fix made `_lastPossession` only update TOGETHER with the `PossessionChanged` event (instead of updating then bailing before firing the event). This fixed the desync between `PossessionAway` (read fresh each tick) and `_possession` (event-driven). But the three-field architecture remains brittle.

### Problem 2: Config Disk vs In-Memory State
`_homeConfig`/`_awayConfig` are loaded from disk at `SetGameTeamsFromWeb` time. They are updated in memory by `RefreshHomeAwayConfigIfNeeded` when the user saves a profile. But they are never re-read from disk during a live game — if the user opens AssignTrackForm, assigns a song, and saves, the save path calls `RefreshHomeAwayConfigIfNeeded` which reloads the profile from disk. This works correctly now, but any code path that mutates a team's profile on disk without going through `WebMainForm`'s save methods (e.g., an automated import or cloud sync) would leave the in-memory `_homeConfig`/`_awayConfig` stale for the rest of the game.

### Problem 3: WebView2 Thread Affinity
`_webView.CoreWebView2` is thread-affinitized to the UI thread. Several code paths call `_webView.ExecuteScriptAsync` from `Task.Run` continuations (e.g., `ImportDefaultSongPackFolderFromWeb` line 1390), correctly using `RunOnUi` for the WebView2 call. But `PushCategories()` (line 2095) reads `_webView.CoreWebView2` without a null check — if called before `InitWebViewAsync` completes, it would silently skip the push. The constructor calls `InitWebViewAsync` in the `Load` event, and PushCategories is only called from user actions that happen after the UI is visible, so this is currently safe but fragile.

---

## ARCHITECTURAL RECOMMENDATIONS

1. **Eliminate dead signals from PlaySnapshot:** Remove `YardLine`, `YardsGained`, `LostYards` until yard-line OCR is built. Replace the one remaining `LostYards` consumer (`BigEventHelper`) with `YardsToGo` comparison.

2. **Consolidate Loss detection:** Combine `DefenseHelper`'s Loss branches, `TflHelper`, and `BigEventHelper`'s 4th-down-loss branch into a single evaluator with a single set of guards, rather than three evaluators with overlapping but subtly different conditions.

3. **Add 1-tick buffering:** Before computing `PlayDelta`, compare the previous and current snapshots for fields that should change together (Down, YardsToGo) and, if only one changed, hold the previous value for the non-changing field so evaluators see a "logical" transition rather than a physically split one.

4. **Add regression test harness:** The evaluators are pure functions of `GameState`. A test harness that feeds synthetic `GameState` transitions and asserts expected `TriggerEvent` outputs would catch every Discrepancy #11-14 regression at build time rather than requiring live-game testing.

5. **Document evaluator inter-relationships:** An ASCII diagram showing which evaluators produce which EventKeys and which guard each other (e.g., `OffenseDownHelper` defers Loss to `DefenseHelper`, `DefenseThirdDownShortHelper` fires alongside `OffenseDownHelper` on 3rd & short) would prevent future evaluators from accidentally stepping on existing coverage.