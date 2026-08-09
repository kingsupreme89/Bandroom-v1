# Bandroom State-Machine Analysis — Complete Audit

**Date:** August 8, 2026  
**Author:** Senior State-Machine & Event-Driven Systems Engineer  
**Methodology:** Full codebase review against intended application state machine, event → transition → action mapping, discrepancy analysis, and regression test specification.

---

## Architecture Overview

Bandroom is a **College Football 27 companion app** that uses Windows OCR to read the game's on-screen scorebug HUD, runs a rule-engine of 16 evaluators against frame-to-frame game-state deltas, and fires audio cues (band music, PA announcer clips) matched to whichever team committed each event. The system operates as a **layered event-driven state machine** with three distinct layers:

1. **OCR/Input Layer** — `GameWatcher.RunAsync` polling loop (window detection, region OCR, color sampling)
2. **Game-State Engine** — `RouteEngineTick` building `PlaySnapshot` → `PlayDelta` → 16 `IRuleEvaluator` classes producing `TriggerEvent` outputs
3. **Audio/UI Layer** — `WebMainForm` consuming events, resolving home/away routing, firing `AudioPlayer`

---

## LAYER 1: OCR Input State Machine

### States (GameWatcher internal)

| State | Description |
|---|---|
| `IDLE` | Watcher not started (`_cts` is null) |
| `SEARCHING` | Window handle not found — polling `FindGameWindow()` at 1500ms |
| `MINIMIZED` | Window found but `IsIconic` — pausing at 1000ms |
| `RECT_FAILED` | `GetWindowRect` returned false — stale handle, re-search |
| `ZERO_SIZE` | Window rect has non-positive dimensions |
| `NOT_FOREGROUND` | Game is not the foreground window — skip OCR to avoid reading Bandroom's own UI |
| `ACTIVE` | Window found, foreground, valid rect — full OCR pipeline running at 250ms interval |
| `ERROR` | Exception caught in outer try/catch — backoff 1000ms |

### State Transition Map — OCR Layer

```
IDLE
  + Start() called
  + _cts != null
  = SEARCHING
  + CreateEventRouter()

SEARCHING
  + FindGameWindow() returns valid hwnd
  + hwnd != IntPtr.Zero && IsWindowVisible
  = ACTIVE
  + WindowFoundChanged?.Invoke(true), Log window found

ACTIVE
  + IsIconic(hwnd) == true
  + (any tick)
  = MINIMIZED
  + Log "window minimized"

ACTIVE
  + !GetWindowRect(hwnd, out rect)
  + (any tick)
  = RECT_FAILED
  + hwnd = IntPtr.Zero, WindowFoundChanged(false)

ACTIVE
  + winW <= 0 || winH <= 0
  + (any tick)
  = ZERO_SIZE
  + (delays 1000ms, then cycles back)

ACTIVE
  + GetForegroundWindow() != hwnd
  + (any tick)
  = NOT_FOREGROUND
  + Log "game window isn't focused", delay 500ms, cycle back

ACTIVE
  + Exception thrown (anywhere in loop)
  + (any tick)
  = ERROR
  + Log + CrashLog, delay 1000ms, cycle back to SEARCHING

ANY_STATE
  + Stop() called / CancellationToken cancelled
  = IDLE
  + _cts.Cancel()
```

### Region-Specific Sub-States (within ACTIVE)

Each `WatchedRegion` has its own mini-state-machine:

| Region State | Trigger |
|---|---|
| `NULL_LAST` | `region.Last == null` — ready to fire on next match |
| `HELD` | `region.Last == currentValue` — same value, no change |
| `COOLDOWN` | `DateTime.UtcNow < region.CooldownUntil` — same value re-appeared, suppressed |
| `FIRING` | `currentValue != null && currentValue != region.Last && cooldown expired` — fires `RegionChanged` |
| `BLANK_RESET` | `currentValue == null && !EventGatedRegions.Contains(name)` — `region.Last = null` |
| `GATED` | `currentValue == null && EventGatedRegions.Contains(name)` — does NOT reset `Last` (pause-protection) |

---

## LAYER 2: Game-State Engine (The Core State Machine)

### Primary Application States (Derived from OCR)

These are NOT explicitly modeled as an enum but are encoded in `PlaySnapshot` boolean flags:

| State | Defining Snapshot Fields |
|---|---|
| `PRE_SNAP` | Down > 0, no situational flags set |
| `KICKOFF` | `IsKickoff == true` |
| `PAT_ATTEMPT` | `IsPAT == true` |
| `TOUCHDOWN` | `IsTouchdown == true` |
| `TURNOVER` | `IsTurnover == true` |
| `PENALTY` | `IsPenaltyOnOffense \|\| IsPenaltyOnDefense` |
| `NO_PUNT_RETURN` | `IsNoPuntReturn == true` |
| `PRE_GAME` | Quarter == 0, Down == 0 |
| `DRIVE_IN_PROGRESS` | Down 1-4, no situational flags, possession known |
| `TIMEOUT` | `AwayTimeoutsRemaining` decrements (inferred via brightness) |
| `SCORE_TRANSITION` | Score delta detected (HomeScore or AwayScore changed) |

### PlayDelta — Derived Transition Signals

| Signal | Computation | Meaning |
|---|---|---|
| `YardsGained` | `previous.YardLine - current.YardLine` | Net yards gained on play |
| `LostYards` | `yardsGained < 0` | Offense lost yardage |
| `NewPossession` | `previous.PossessionAway != current.PossessionAway` | Possession flipped |
| `WasFirstDown` | `current.Down == 1 && previous.Down > 1` | New set of downs |
| `WasThirdDownStop` | `previous.Down == 3 && current.Down == 1 && newPossession` | Defense stopped on 3rd |
| `WasFourthDownStop` | `previous.Down == 4 && current.Down == 1 && newPossession` | Defense stopped on 4th |

### Complete Evaluator Transition Map

Below is every evaluator, its state-transition formula, conditions, and actions.

---

#### TouchdownHelper

```
CURRENT STATE: Not Touchdown
  + IsTouchdown transitions false→true
  + Previous.IsTouchdown == false, Current.IsTouchdown == true
  = TOUCHDOWN SCORED
  + If Delta.NewPossession → "Defense: Touchdown Scored" (pick-six/fumble-return TD)
  + Else → "Offense: Touchdown Scored"
  + Volume 85/100 (BigGame), IsEarnedBigEvent=true
```

#### TurnoverHelper

```
CURRENT STATE: Not Turnover
  + IsTurnover transitions false→true
  + Previous.IsTurnover == false, Current.IsTurnover == true
  = TURNOVER FORCED
  + If Q4+ && time <= 120s → "Defense: Iced Game by Turnover", Volume 100
  + Else → "Defense: Turnover Forced", Volume 80/100 (BigGame)
```

#### FieldGoalPATHelper

```
CURRENT STATE: Any
  + Total score delta == 1 AND IsPAT == true
  + scoreDiff == 1
  = PAT MADE
  + Fire "Offense: PAT Made", Volume 75, IsEarnedBigEvent=false

CURRENT STATE: Any
  + Total score delta == 2
  + scoreDiff == 2 (does NOT check IsPAT)
  = 2-POINT CONVERSION
  + Fire "Offense: 2-Point Conversion Made", Volume 85, IsEarnedBigEvent=true

CURRENT STATE: Any
  + Total score delta == 3
  + scoreDiff == 3
  = FIELD GOAL MADE
  + Fire "Offense: Field Goal Made", Volume 85, IsEarnedBigEvent=false
```

#### FieldGoalMissedHelper

```
CURRENT STATE: PAT Attempt
  + IsPAT true→false AND NewPossession AND score unchanged
  + Previous.IsPAT && !Current.IsPAT && Delta.NewPossession && homeDelta==0 && awayDelta==0
  = FIELD GOAL MISSED
  + Fire "Defense: Field Goal Missed by Opponent", Volume 85, IsEarnedBigEvent=true
```

#### SafetyHelper

```
CURRENT STATE: Any
  + Previous.PossessionAway AND homeDelta == 2
  + Score change of exactly 2 against the possessing team
  = SAFETY (against away)
  + Fire "Defense: Safety", Volume 100

CURRENT STATE: Any
  + !Previous.PossessionAway AND awayDelta == 2
  + Score change of exactly 2 against the possessing team
  = SAFETY (against home)
  + Fire "Defense: Safety", Volume 100
```

#### FirstDownHelper

```
CURRENT STATE: At least 2nd down
  + Down resets to 1 (WasFirstDown) AND Previous.Down > 0
  + Delta.WasFirstDown && state.Previous.Down > 0
  = EARNED FIRST DOWN
  + If yardsGained >= 15 → "Offense: Earned First Down (Big Gain)", Vol 100
  + If YardLine <= 50 → "Offense: Earned First Down (Midfield)" [DISABLED — YardLine always 0]
  + Else → "Offense: Earned First Down", Vol 80
```

#### DefenseHelper

```
CURRENT STATE: User does NOT have possession
  + Down changes (Current.Down != Previous.Down)
  + !UserHasPossession
  = DEFENSIVE DOWN
  + Down 3 + LostYards → "Defense: Third Down (Loss)", Vol 75/100
  + Down 2 + LostYards → "Defense: Second Down (Loss)", Vol 75/100
  + Down 2 → "Defense: Second Down", Vol 70/100
```

#### OffenseDownHelper

```
CURRENT STATE: User HAS possession
  + Down changes (Current.Down != Previous.Down)
  + UserHasPossession
  = OFFENSIVE DOWN
  + Down 2 → "Offense: Second Down", Vol 70/100
  + Down 3 → "Offense: Third Down", Vol 70/100
  + NOTE: Does not handle Down 4 — see Discrepancy #10
```

#### BigEventHelper

```
CURRENT STATE: Any
  + Down 3 AND NewPossession
  = THIRD DOWN STOP
  + Fire "Defense: Third Down", Vol 80/100

CURRENT STATE: Any
  + Down 4 AND LostYards
  = FOURTH DOWN STOP (LOSS)
  + Fire "Defense: Fourth Down (Loss)", Vol 85/100

CURRENT STATE: Any
  + Down 4 AND NewPossession
  = FOURTH DOWN STOP
  + Fire "Defense: Fourth Down", Vol 80/100
```

#### KickoffHelper

```
CURRENT STATE: Not Kickoff
  + IsKickoff transitions false→true
  + !Previous.IsKickoff && Current.IsKickoff
  = KICKOFF EVENT
  + Quarter 1 → "Other: Opening Kickoff", Vol 90
  + Quarter 3 → "Other: Second-Half Kickoff", Vol 90
  + UserHasPossession → "Other: Kickoff on Kick (Receiving)", Vol 75
  + Else → "Other: Kickoff on Kick (Kicking)", Vol 75
```

#### PenaltyHelper

```
CURRENT STATE: No penalty
  + IsPenaltyOnOffense false→true
  = OFFENSE PENALTY
  + Fire "Penalty: Offense" (routed to DEFENSE side — celebrating opponent's mistake)
  + Vol 70, IsEarnedBigEvent=false

CURRENT STATE: No penalty
  + IsPenaltyOnDefense false→true
  = DEFENSE PENALTY
  + Fire "Penalty: Defense" (routed to OFFENSE side)
  + Vol 70, IsEarnedBigEvent=false
```

#### GameStateEventHelper

```
CURRENT STATE: Prev quarter != current quarter
  + Quarter 1→2 (Previous.Quarter > 0 required)
  = START OF 2ND QUARTER
  + Fire "Other: Start of 2nd Quarter", Vol 70

CURRENT STATE: Prev quarter != current quarter
  + Quarter 3→4 (Previous.Quarter > 0 required)
  = START OF 4TH QUARTER
  + Fire "Other: Start of 4th Quarter", Vol 80, IsEarnedBigEvent=true

CURRENT STATE: Pre-game (Q0, D0)
  + Quarter 0→1 AND Down 0→>0
  = PREGAME TAKE THE FIELD
  + Fire "Other: Pregame Take the Field", Vol 85, IsEarnedBigEvent=true

CURRENT STATE: Any drive
  + WasFirstDown AND Q4+ AND time <= 120
  + Delta.WasFirstDown && Current.Quarter >= 4 && TimeRemainingSeconds <= 120
  = ICED GAME BY FIRST DOWN
  + Fire "Offense: Iced Game by First Down", Vol 100, IsEarnedBigEvent=true

CURRENT STATE: Any
  + Q4+ AND time <= 30 AND lead >= 9
  + Current.Quarter >= 4 && TimeRemainingSeconds <= 30 && (homeLead >= 9 or awayLead >= 9)
  = VICTORY IN HAND
  + Fire "Offense: Victory in Hand", Vol 100, IsEarnedBigEvent=true
```

#### TflHelper

```
CURRENT STATE: Any drive play
  + YardsToGo increased AND LostYards AND Previous.Down > 0 && Current.Down > 0
  + Current.YardsToGo > Previous.YardsToGo && Delta.LostYards
  = TACKLE FOR LOSS
  + Fire "Defense: Tackle for Loss", Vol 75/100, IsEarnedBigEvent=true
```

#### DownFieldPositionHelper

```
CURRENT STATE: Any down transition
  + Down changes AND Previous.Down > 0
  = POSITIONAL DOWNS
  + Defense side (2nd+Loss) → "Defense: Second Down (Loss)", Vol 85
  + Defense side (2nd+Midfield) → "Defense: Second Down (Midfield)", Vol 75
  + Defense side (3rd+Loss) → "Defense: Third Down (Loss)", Vol 85
  + Defense side (4th+Loss) → "Defense: Fourth Down (Loss)", Vol 85
  + Offense side (2nd+Midfield) → "Offense: Second Down (Midfield)", Vol 75
```

#### DriveStarterHelper

```
CURRENT STATE: Any possession change
  + NewPossession AND Down==1 AND NOT kickoff AND NOT turnover AND Previous.Down > 0
  + Delta.NewPossession && Current.Down == 1 && !Current.IsKickoff && !Current.IsTurnover
  = DRIVE STARTER
  + UserHasPossession → "Offense: Drive Starter", Vol 70
  + Else → "Defense: Drive Starter", Vol 70
```

#### NoPuntReturnHelper

```
CURRENT STATE: Not no-punt-return
  + IsNoPuntReturn false→true AND User does NOT have possession
  + !Previous.IsNoPuntReturn && Current.IsNoPuntReturn && !UserHasPossession
  = NO PUNT RETURN (defense punting team coverage won)
  + Fire "Defense: No Punt Return", Vol 75
```

#### TimeoutHelper

```
CURRENT STATE: Any (defense side)
  + User on defense AND time > 240 AND valid timeout count
  + !UserHasPossession && TimeRemainingSeconds > 240 && 0 <= AwayTimeoutsRemaining <= 6
  = TIMEOUT EVENT
  + ⚠️ LEVEL-TRIGGERED: fires every tick conditions hold
  + N=4,3,2,1,0 → "Defense: Timeout (N Remaining)"
```

---

## LAYER 3: Audio/Side-Routing State Machine (WebMainForm)

### Matchup Lifecycle States

| State | Conditions |
|---|---|
| `NO_MATCHUP` | `_homeTeam` == null OR `_awayTeam` == null |
| `MATCHUP_SET` | Both teams set, `_matchupLocked` == false |
| `GAMETIME` | Both teams set, `_matchupLocked` == true, watcher started |
| `WATCHING_ACTIVE` | `_watching` && `_windowFound` |
| `WATCHING_WAITING` | `_watching` && `!_windowFound` |
| `STOPPED` | `!_watching`, matchup unlocked |

### Side Routing Logic

```
EventKey.StartsWith("Defense:") || EventKey == "Penalty: Offense"
  → fire for side OPPOSITE possession
Everything else
  → fire for possession side
HomeOnlyEventsForNow (currently false)
  → filter to home only (defunct)
```

### Legitimate Event Transitions (Game Consumption)

```
GAMETIME
  + Stop Watching pressed
  = STOPPED
  + _hook.Stop(), _watcher.Stop(), _matchupLocked = false

NO_MATCHUP
  + SetGameTeamsFromWeb(home, away) + ConfirmGametimeFromWeb
  = GAMETIME
  + _homeConfig/_awayConfig loaded, _possession reset, watcher started
```

---

## DISCREPANCIES FOUND: INTENDED VS ACTUAL IMPLEMENTATION

---

### DISCREPANCY #1 — TimeoutHelper: Level-Triggered Instead of Edge-Triggered

**Severity:** HIGH  
**Category:** Missing Transition Guard

**1. Intended behavior:** Fire once per timeout taken — one audio cue each time a team calls timeout.

**2. Actual implementation:** `TimeoutHelper.Evaluate` returns a `TriggerEvent` on **every single tick** where `!UserHasPossession && TimeRemainingSeconds > 240 && AwayTimeoutsRemaining` is in [0,6]. There is no edge detection — no previous-state comparison and no flag set after firing. It evaluates as a **level trigger** rather than an **edge trigger**.

**3. Why they differ:** The evaluator checks only the current state, not a transition. Every other evaluator either uses `PlayDelta` or compares `Previous.X != Current.X`. TimeoutHelper has no such guard.

**4. Possible failure:** During a live game, whenever the user's team is on defense and there are >4 minutes left, this fires `"Defense: Timeout (N Remaining)"` on **every 250ms OCR tick** — roughly 4 times per second — continuously until either possession changes or the clock drops below 4 minutes. Even with `AudioPlayer.FireCooldown` (20s per path), this means the same timeout cue fires every ~20 seconds rather than only when a timeout is actually called.

**5. Recommended fix:** Add edge detection — track previous `AwayTimeoutsRemaining` and only fire on a decrement:
```csharp
if (state.Current.AwayTimeoutsRemaining < state.Previous.AwayTimeoutsRemaining)
```

**6. Regression test:** Simulate a game tick where `AwayTimeoutsRemaining` drops from 3→2. Confirm exactly 1 event fires. Simulate staying on the same timeout count across multiple ticks — confirm 0 events fire.

---

### DISCREPANCY #2 — DownFieldPositionHelper: Midfield Always True

**Severity:** MEDIUM  
**Category:** Logic Error (Always-True Condition)

**1. Intended behavior:** `"Defense: Second Down (Midfield)"` fires when the ball is at or inside the opponent's 50-yard line.

**2. Actual implementation:** `YardLine` is always 0 because OCR for yard line was never built. `atMidfield = state.Current.YardLine <= 50` is **always true**. The Midfield variant was disabled in `FirstDownHelper` (commented out with a note) but is **NOT** disabled in `DownFieldPositionHelper`. Line 26: `2 when atMidfield => Make("Defense: Second Down (Midfield)", 75, false)` fires on **every** 2nd down when the defense is on the field.

**3. Why they differ:** `DownFieldPositionHelper` was added after `FirstDownHelper`'s Midfield fix. The same bug was reintroduced without the same guard.

**4. Possible failure:** `"Defense: Second Down (Midfield)"` fires on literally every 2nd down the defense plays, regardless of field position — it silently duplicates `"Defense: Second Down"` with a different volume (75 vs DefenseHelper's 70).

**5. Recommended fix:** Gate all Midfield variants behind a `YardLine > 0` check:
```csharp
bool atMidfield = state.Current.YardLine > 0 && state.Current.YardLine <= 50;
```

**6. Regression test:** With YardLine=0, verify `DownFieldPositionHelper` never returns Midfield variants. With YardLine=45, verify it returns Midfield. With YardLine=55, verify it does not.

---

### DISCREPANCY #3 — Duplicate Event Coverage: DefenseHelper + DownFieldPositionHelper

**Severity:** HIGH  
**Category:** Duplicate Transitions

**1. Intended behavior:** Each down-transition event fires exactly once from a single evaluator.

**2. Actual implementation:** `DefenseHelper` and `DownFieldPositionHelper` **both** fire for the same down transitions:

| Down | Condition | DefenseHelper fires | DownFieldPositionHelper fires |
|---|---|---|---|
| 2nd | LostYards | `"Defense: Second Down (Loss)"` Vol 75 | `"Defense: Second Down (Loss)"` Vol 85 |
| 3rd | LostYards | `"Defense: Third Down (Loss)"` Vol 75 | `"Defense: Third Down (Loss)"` Vol 85 |
| 4th | LostYards | (not handled) | `"Defense: Fourth Down (Loss)"` Vol 85 |

Both evaluators return a TriggerEvent on the same tick with the **same EventKey** but **different volumes**. `EventRouter.Route` collects both into its results list, and `OnEngineEventsDetected` fires `FireEventForSide` twice for the **exact same EventKey**.

**3. Why they differ:** `DownFieldPositionHelper` was designed to supplement `DefenseHelper` with positional variants, but it also duplicates the "Loss" variants without any deduplication in `EventRouter`.

**4. Possible failure:** `"Defense: Second Down (Loss)"` fires **twice** per event. `AudioPlayer.Play` is called with `interruptPrevious: true`, so the second call stops the first and starts over — creating an audible start-stop-restart glitch on every loss-of-yards 2nd/3rd down.

**5. Recommended fix:** Consolidate the "Loss" variants into only one evaluator. Either:
- Remove the `lostYards` cases from `DownFieldPositionHelper` (keeping only the Midfield variants), or
- Remove the `lostYards` cases from `DefenseHelper`, or
- Add a deduplication pass in `EventRouter` by EventKey.

**6. Regression test:** Trigger a 2nd-down loss. Assert exactly 1 `TriggerEvent` with EventKey `"Defense: Second Down (Loss)"` appears in the EventsDetected list.

---

### DISCREPANCY #4 — BigEventHelper + DefenseHelper 3rd-Down Ambiguity

**Severity:** MEDIUM  
**Category:** Conflicting Transition Priorities

**1. Intended behavior:** Clear separation: `BigEventHelper` handles 3rd-down stops (possession changes), `DefenseHelper` handles 3rd-down losses.

**2. Actual implementation:** If a 3rd-down play results in both `LostYards` AND `NewPossession` (e.g., a sack-fumble where the defense recovers), all three fire:
- `BigEventHelper` → `"Defense: Third Down"` (NewPossession on down 3)
- `DefenseHelper` → `"Defense: Third Down (Loss)"` (LostYards on down 3)
- `DownFieldPositionHelper` → `"Defense: Third Down (Loss)"` (LostYards on down 3)
= **3 events** from one snap.

**3. Why they differ:** No mutual exclusion or priority system between evaluators. Each fires independently if its conditions are met.

**4. Possible failure:** Three different audio cues try to play simultaneously/sequentially with `interruptPrevious: true`. The user hears the first cue start, get cut off, second cue starts, gets cut off, third cue plays — an audible mess.

**5. Recommended fix:** Add an explicit priority/resolution strategy. A sack-fumble on 3rd down that results in a turnover should fire ONLY the turnover/big-play event:
```csharp
// In DefenseHelper/DownFieldPositionHelper — skip loss events when possession already flipped
if (state.Delta.NewPossession) return null;
```

**6. Regression test:** Simulate a 3rd-down play with both LostYards=true AND NewPossession=true. Verify exactly 1 event fires.

---

### DISCREPANCY #5 — SafetyHelper + FieldGoalPATHelper Score Delta Ambiguity

**Severity:** HIGH  
**Category:** Incorrect Event on Valid Transition

**1. Intended behavior:** A safety (+2 points for defense) is distinct from a 2-point conversion (+2 points for offense).

**2. Actual implementation:** `FieldGoalPATHelper` fires `"Offense: 2-Point Conversion Made"` on **any** score delta of 2, regardless of which side's score changed. `SafetyHelper` fires `"Defense: Safety"` when the defense side gains exactly 2 points. On a safety, **both** evaluators fire — `FieldGoalPATHelper` uses `totalScoreDiff` which also reaches 2 for a safety.

**3. Why they differ:** `FieldGoalPATHelper` uses `totalScoreDiff = (H+A) - (prevH+prevA)` without checking which individual score changed.

**4. Possible failure:** On a safety, both `"Offense: 2-Point Conversion Made"` and `"Defense: Safety"` fire. The user hears a celebration for an offensive 2-point conversion that never happened.

**5. Recommended fix:** Check possession-side score delta:
```csharp
int offenseScoreDelta = state.Previous.PossessionAway 
    ? state.Current.AwayScore - state.Previous.AwayScore
    : state.Current.HomeScore - state.Previous.HomeScore;
// 2-point conversion: offense gains +2
// Safety: defense (non-possession side) gains +2 — skip here
if (scoreDiff == 2 && offenseScoreDelta == 2) { ... }
```

**6. Regression test:** Simulate a safety (defense +2, total score +2). Verify only `"Defense: Safety"` fires. Simulate a 2-point conversion. Verify only `"Offense: 2-Point Conversion Made"` fires.

---

### DISCREPANCY #6 — FieldGoalMissedHelper May Never Fire

**Severity:** MEDIUM  
**Category:** Impossible Transition (Missing OCR Data)

**1. Intended behavior:** Field goal missed detection via `Previous.IsPAT && !Current.IsPAT && NewPossession && no score change`.

**2. Actual implementation:** The `IsPAT` flag is set when `situation == "pat_good"` via `NormalizeMatch`. A **missed** field goal never produces "PAT GOOD" text. The "situation" OCR region maps only success text: `"PAT GOOD" → "pat_good"`, `"TOUCHDOWN" → "touchdown"`. There is no OCR region that captures an attempted-but-missed field goal.

**3. Why they differ:** The code comment says "FG attempts share the same game state flag since both are special-teams kicks" — but the OCR never sets that flag before a missed attempt.

**4. Possible failure:** `FieldGoalMissedHelper` likely **never fires** in practice. A missed FG produces no audio cue.

**5. Recommended fix:** Either:
- Add OCR for "Field Goal Attempt" or "FG" text that appears pre-kick, or
- Detect missed FG heuristically: possession flipped + down was 4 + no score change + time decreased by ~play duration

**6. Regression test:** Mock OCR sequence: situation→fieldgoal text, then possession flips with no score change. Verify `FieldGoalMissedHelper` fires.

---

### DISCREPANCY #7 — Race Condition: OCR Blanking During Pause Menus

**Severity:** LOW  
**Category:** Race Condition

**1. Intended behavior:** Score/quarter/down values remain stable across pause menus and replay overlays.

**2. Actual implementation:** The sticky `_lastKnownX` fields (score/quarter/down) preserve the last real reading, but `_lastDistanceRaw` is NOT sticky — it's updated from fresh OCR on every tick and can go null during pauses. On resume, `yardsToGo` may be 0 temporarily.

**3. Why they differ:** The sticky pattern was applied to score/quarter (which caused the "random song on pause" bug) but not exhaustively to all fields.

**4. Possible failure:** After unpausing, `TflHelper` could see `Current.YardsToGo (0) > Previous.YardsToGo (7)` as false (0 < 7, safe in this direction), but other evaluators reading yards-to-go during the resume tick get incorrect data.

**5. Recommended fix:** Apply the sticky pattern to `_lastDistanceRaw` — only update it when a valid distance is parsed:
```csharp
if (distanceRaw != null) _lastDistanceRaw = distanceRaw;
```
Then read `_lastDistanceRaw` in `RouteEngineTick` instead of the volatile `_lastDistanceRaw`.

**6. Regression test:** Pause game during a 3rd & 7. Resume. Verify no spurious TFL or first-down events fire from the post-pause snapshot.

---

### DISCREPANCY #8 — NoPuntReturnHelper: Confusing Comment (No Bug)

**Severity:** COSMETIC  
**Category:** Documentation

**1. Intended behavior:** `"Defense: No Punt Return"` fires when the punting team's coverage unit forces a fair catch / no return.

**2. Actual implementation:** The logic is actually **correct**: `if (state.UserHasPossession) return null` — after a punt, possession has flipped to the receiving team. If the user's team punted, they DON'T have possession → event fires correctly. If the user's team received the punt and called fair catch, they DO have possession → event skipped correctly.

**3. Why they differ:** The comment's reasoning about who "has the ball" during a punt sequence is stated confusingly, making it look like a logic error when it isn't one.

**4. Possible failure:** None — logic is correct.

**5. Recommended fix:** Clarify the comment.

**6. Regression test:** No code change needed. Verify existing behavior is correct: user's team punts → fair catch → event fires. User's team receives punt → fair catch → event does NOT fire.

---

### DISCREPANCY #9 — TackleForLoss: Dual Fire Path (Legacy + Engine)

**Severity:** LOW  
**Category:** Duplicate Transition (Masked)

**1. Intended behavior:** TFL detected once, fires one audio cue.

**2. Actual implementation:** The legacy `GameWatcher.TackleForLossDetected` event AND the engine's `TflHelper` evaluator BOTH fire for the same tackle-for-loss. `OnTackleForLoss` fires `FireEventForSide(defenseSide, "Defense: Tackle for Loss")` while the engine fires the same EventKey through `OnEngineEventsDetected`.

**3. Why they differ:** Previously flagged as DEFERRED (AUDIT_REPORT Bug #3). The legacy path was deliberately left un-gated per owner request.

**4. Possible failure:** Same EventKey fires twice per TFL. Currently masked by `AudioPlayer.FireCooldown` (20s per path) — the second call is suppressed.

**5. Recommended fix:** Gate `OnTackleForLoss` with `if (_useEngineForEvents) return;`.

**6. Regression test:** Verify TFL event fires exactly once per TFL play.

---

### DISCREPANCY #10 — No "Offense: Fourth Down" Event Exists

**Severity:** MEDIUM  
**Category:** Missing Transition

**1. Intended behavior:** Complete coverage for all downs, including 4th down from the offense perspective.

**2. Actual implementation:** `OffenseDownHelper` only handles downs 2 and 3. No evaluator produces `"Offense: Fourth Down"`. The old `down:4th` Trigger entry has no equivalent in the new engine (documented in `WebMainForm.cs` line 361-363).

**3. Why they differ:** Product decision was deferred — "that one needs a product decision, not a guess."

**4. Possible failure:** If a user had assigned audio to the legacy `down:4th` trigger, it's now silent. Going for it on 4th down produces no offensive audio cue.

**5. Recommended fix:** Add `"Offense: Fourth Down"` to `OffenseDownHelper`'s switch statement:
```csharp
4 => "Offense: Fourth Down",
```

**6. Regression test:** User's team faces 4th down. Verify `"Offense: Fourth Down"` fires.

---

## INVALID / IMPOSSIBLE STATES

### Invalid State: IsTouchdown + IsTurnover Simultaneously

Both are set from the same OCR "situation" region via `NormalizeMatch`. A single OCR read can only match ONE regex group, so both can't be set simultaneously. `RouteEngineTick` sets both from the same `situation` string — mutually exclusive by definition. **Safe.**

### Impossible State: Previous.Down == 0, Current.Down > 0, WasFirstDown == false

`PlayDelta.Calculate`: `wasFirstDown = current.Down == 1 && previous.Down > 1`. If opening snap has Down=1 and Previous.Down=0, WasFirstDown is false because `0 > 1` is false. This is intentional — the opening kickoff's 1st & 10 shouldn't fire "Earned First Down." The guard in `FirstDownHelper` (`state.Previous.Down == 0`) and the first-tick skip in `RouteEngineTick` both reinforce this. **Consistent.**

### Invalid State: NewPossession Without Down Change

`PlayDelta.Calculate` sets `newPossession` purely from possession comparison. It's possible for possession to change without a down change if OCR flickers or the color sampling misreads one frame. Evaluators that check `NewPossession` (BigEventHelper, DriveStarterHelper, etc.) also check `Down == 1` or `Down == 3/4` as secondary conditions, which partially mitigates this. However, `TouchdownHelper` checks `Delta.NewPossession` to distinguish offense/defense TDs without a down guard — a possession flipper during a touchdown banner display could misroute the event. **Partially mitigated.**

---

## RACE CONDITION ANALYSIS

### Race #1: Possession Sampling vs OCR Tick Ordering

`SamplePossessionFromWindow` runs inside the "down" region processing loop (GameWatcher line 400), BEFORE the "flag" region is re-read on the same tick. The fix that skips possession sampling when a flag is active uses the PREVIOUS tick's flag state: `_regions.FirstOrDefault(r => r.Name == "flag")?.Last`. If a flag appears and disappears between ticks faster than the 250ms polling interval, possession could still be mis-sampled during the flag display.

### Race #2: Multiple Evaluators Firing Simultaneously

EventRouter processes all 16 evaluators sequentially and collects all non-null results. There is no mutual exclusion. `OnEngineEventsDetected` iterates the list and calls `FireEventForSide` for each. `FireEventForSide` calls `AudioPlayer.Play(entry.AudioFile, interruptPrevious: true)` — which calls `StopAll()` before playing. So event B's play call stops event A's audio that started milliseconds earlier. This is a **simultaneous-event collision** pattern.

### Race #3: _possession Could Be Null During Event Routing

In `OnEngineEventsDetected` (line 992): `string side = _possession ?? "home"`. When `_possession` is null (possession not yet detected right after GAMETIME), all events default to "home". A defensive event for the away team would fire for the home team. The `penaltyagainst` routing in `RouteEngineTick` correctly uses nullable `possessionIsHomeNow` — but the event consumer does NOT mirror this safety.

### Race #4: OCR Thread vs UI Thread

`RouteEngineTick` runs on the OCR polling thread. It calls `EventsDetected?.Invoke(results)`, which triggers `OnEngineEventsDetected`. The handler wraps in `RunOnUi`, so audio plays on the UI thread. However, snapshot rotation (`_snapshotPrevious = _snapshotCurrent`) happens BEFORE the UI thread processes events. If the next OCR poll starts before the UI thread finishes, `_snapshotPrevious` is already overwritten. In practice, the 250ms poll interval makes this unlikely but theoretically possible under heavy CPU load.

---

## EVENTS THAT COULD ARRIVE OUT OF ORDER

1. **Down change before situational banner:** The "down" region may update to a new down BEFORE the "situation" region shows "TOUCHDOWN." `RouteEngineTick` builds a snapshot with the new Down but old Situation, missing the TD detection for one tick. The next tick catches it correctly.

2. **Score update before PAT banner:** The score regions may show the new score before the "situation" region shows "PAT GOOD." `FieldGoalPATHelper` checks `scoreDiff == 1 && IsPAT` — if score arrives first, `IsPAT` is false and the event is missed for one tick.

3. **Possession color change before down change:** After a turnover, the possession underline color may flip BEFORE the "down" region shows 1st down. Creates a one-tick window where possession reflects the new team but down hasn't updated.

---

## SUMMARY: CRITICALITY MATRIX

| # | Discrepancy | Severity | User-Visible Symptom | Currently Masked By |
|---|---|---|---|---|
| 1 | TimeoutHelper level-triggered | **HIGH** | Repeating timeout cue every ~20s during defense | FireCooldown |
| 2 | DownFieldPositionHelper Midfield always true | **MEDIUM** | Duplicate 2nd-down event on every play | — |
| 3 | Duplicate DefenseHelper + DownFieldPositionHelper | **HIGH** | Audible start-stop glitch on loss plays | — |
| 4 | BigEventHelper + DefenseHelper 3rd-down ambiguity | **MEDIUM** | Multiple conflicting cues on sack-fumble | FireCooldown + interruptPrevious |
| 5 | Safety + 2-pt conversion score delta overlap | **HIGH** | Wrong cue on safety ("2-Point Conversion"!) | — |
| 6 | FieldGoalMissed may never fire | **MEDIUM** | Silent missed FGs | — |
| 7 | OCR blanking race on non-sticky fields | **LOW** | Spurious TFL after pause (edge case) | — |
| 8 | NoPuntReturn comment/logic clarity | **COSMETIC** | None | — |
| 9 | Dual TFL fire path | **LOW** | None (cooldown-masked) | FireCooldown |
| 10 | No Offense: Fourth Down | **MEDIUM** | Silent 4th down plays for offense | — |

---

## 42 EventKeys Across 16 Evaluators

| Evaluator | EventKey | Category | Side |
|---|---|---|---|
| TouchdownHelper | `Offense: Touchdown Scored` | Scoring | Offense |
| TouchdownHelper | `Defense: Touchdown Scored` | Scoring | Defense |
| TurnoverHelper | `Defense: Turnover Forced` | Turnovers | Defense |
| TurnoverHelper | `Defense: Iced Game by Turnover` | Turnovers | Defense |
| FieldGoalPATHelper | `Offense: Field Goal Made` | Scoring | Offense |
| FieldGoalPATHelper | `Offense: PAT Made` | Scoring | Offense |
| FieldGoalPATHelper | `Offense: 2-Point Conversion Made` | Scoring | Offense |
| FieldGoalMissedHelper | `Defense: Field Goal Missed by Opponent` | Scoring | Defense |
| SafetyHelper | `Defense: Safety` | Scoring | Defense |
| FirstDownHelper | `Offense: Earned First Down` | Downs | Offense |
| FirstDownHelper | `Offense: Earned First Down (Big Gain)` | Downs | Offense |
| FirstDownHelper | `Offense: Earned First Down (Midfield)` | Downs | Offense |
| DefenseHelper | `Defense: Second Down` | Downs | Defense |
| DefenseHelper | `Defense: Second Down (Loss)` | Downs | Defense |
| DefenseHelper | `Defense: Third Down (Loss)` | Downs | Defense |
| OffenseDownHelper | `Offense: Second Down` | Downs | Offense |
| OffenseDownHelper | `Offense: Third Down` | Downs | Offense |
| BigEventHelper | `Defense: Third Down` | Downs | Defense |
| BigEventHelper | `Defense: Fourth Down` | Downs | Defense |
| BigEventHelper | `Defense: Fourth Down (Loss)` | Downs | Defense |
| DownFieldPositionHelper | `Offense: Second Down (Midfield)` | Downs | Offense |
| DownFieldPositionHelper | `Defense: Second Down (Loss)` | Downs | Defense |
| DownFieldPositionHelper | `Defense: Second Down (Midfield)` | Downs | Defense |
| DownFieldPositionHelper | `Defense: Third Down (Loss)` | Downs | Defense |
| DownFieldPositionHelper | `Defense: Fourth Down (Loss)` | Downs | Defense |
| TflHelper | `Defense: Tackle for Loss` | Downs | Defense |
| KickoffHelper | `Other: Opening Kickoff` | Special Teams | Other |
| KickoffHelper | `Other: Second-Half Kickoff` | Special Teams | Other |
| KickoffHelper | `Other: Kickoff on Kick (Receiving)` | Special Teams | Other |
| KickoffHelper | `Other: Kickoff on Kick (Kicking)` | Special Teams | Other |
| PenaltyHelper | `Penalty: Offense` | Penalties | Defense |
| PenaltyHelper | `Penalty: Defense` | Penalties | Offense |
| GameStateEventHelper | `Other: Start of 2nd Quarter` | Hype | Other |
| GameStateEventHelper | `Other: Start of 4th Quarter` | Hype | Other |
| GameStateEventHelper | `Other: Pregame Take the Field` | Hype | Other |
| GameStateEventHelper | `Offense: Iced Game by First Down` | Hype | Offense |
| GameStateEventHelper | `Offense: Victory in Hand` | Hype | Offense |
| TimeoutHelper | `Defense: Timeout (4 Remaining)` | Hype | Defense |
| TimeoutHelper | `Defense: Timeout (3 Remaining)` | Hype | Defense |
| TimeoutHelper | `Defense: Timeout (2 Remaining)` | Hype | Defense |
| TimeoutHelper | `Defense: Timeout (1 Remaining)` | Hype | Defense |
| TimeoutHelper | `Defense: Timeout (0 Remaining)` | Hype | Defense |
| DriveStarterHelper | `Offense: Drive Starter` | Hype | Offense |
| DriveStarterHelper | `Defense: Drive Starter` | Hype | Defense |
| NoPuntReturnHelper | `Defense: No Punt Return` | Special Teams | Defense |

---


