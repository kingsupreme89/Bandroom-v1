# Bandroom Integration Plan — Complete Context for Next Agent

> **Status**: Analysis complete. Ready for implementation.
> **Last Updated**: 2026-08-07
> **Project Root**: `/Users/user/CODING/PROJECTS/BANDROOM/`

---

## 1. Executive Summary

**Goal**: Extend `GameWatcher.cs` to populate all `PlaySnapshot` fields from OCR, maintain `GameState` (current/previous snapshots), integrate `EventRouter` with rule evaluators, and route `TriggerEvent` → `TriggerEntry` → `AudioPlayer` for side-aware audio playback.

**Current State**: All core files read and understood. No code changes made yet.

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           DATA FLOW                                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  GameWatcher (OCR Loop)                                                     │
│       │                                                                      │
│       ▼                                                                      │
│  WatchedRegions → OCR → Parsed Fields                                       │
│       │                                                                      │
│       ▼                                                                      │
│  PlaySnapshot (18 fields) ──────► GameState (Current + Previous)            │
│       │                                                                      │
│       ▼                                                                      │
│  PlayDelta (calculated from snapshots)                                      │
│       │                                                                      │
│       ▼                                                                      │
│  EventRouter (IEnumerable<IRuleEvaluator>)                                  │
│       │                                                                      │
│       ▼                                                                      │
│  List<TriggerEvent> { EventKey, Volume, IsEarnedBigEvent }                 │
│       │                                                                      │
│       ▼                                                                      │
│  TriggerEntry lookup (Trigger + Event → AudioFile)                          │
│       │                                                                      │
│       ▼                                                                      │
│  AudioPlayer.Play() + WebView2 dispatch 'bandroom:triggerfired'             │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Core Files — Complete Understanding

### 3.1 Bandroom.Core Library (`/src/Bandroom.Core/`)

| File | Purpose | Key Details |
|------|---------|-------------|
| **GameState.cs** | Holds current/previous snapshots | `Current`, `Previous` (PlaySnapshot), `Delta` (PlayDelta) |
| **PlaySnapshot.cs** | Immutable game state snapshot | **18 fields** (see §4.1) |
| **PlayDelta.cs** | Computes changes between snapshots | `YardsGained`, `LostYards`, `NewPossession`, `WasFirstDown`, `WasThirdDownStop`, `WasFourthDownStop` |
| **EventRouter.cs** | Routes GameState through rules | `Route(GameState) → IReadOnlyList<TriggerEvent>` |
| **TriggerEvent.cs** | Rule evaluation output | `EventKey` (string), `Volume` (int, default 100), `IsEarnedBigEvent` (bool) |
| **IRuleEvaluator.cs** | Rule interface | `TriggerEvent? Evaluate(GameState state)` |

### 3.2 Main Project Files

| File | Path | Status |
|------|------|--------|
| **GameWatcher.cs** | `/GameWatcher.cs` | ✅ Fully read (459 lines) |
| **TriggerEntry.cs** | `/TriggerEntry.cs` | ✅ Fully read |
| **WebMainForm.cs** | `/WebMainForm.cs` | ✅ Fully read (~1200 lines) |

---

## 4. Data Models — Exact Specifications

### 4.1 PlaySnapshot — 18 Required Fields

```csharp
public class PlaySnapshot {
    // Down & Distance
    public int Down { get; set; }                    // 1-4
    public int YardsToGo { get; set; }               // yards to first down
    
    // Field Position
    public int YardLine { get; set; }                // 1-100 (1 = own goal line)
    
    // Score
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    
    // Game Clock
    public int Quarter { get; set; }                 // 1-4, 5=OT
    public int TimeRemainingSeconds { get; set; }    // seconds in quarter
    
    // Timeouts
    public int AwayTimeoutsRemaining { get; set; }   // 0-3
    
    // Game State Flags
    public bool BigGame { get; set; }                // rivalry/playoff
    public bool PossessionAway { get; set; }         // true = away has ball
    public bool IsKickoff { get; set; }
    public bool IsPAT { get; set; }
    public bool IsPenaltyOnOffense { get; set; }
    public bool IsPenaltyOnDefense { get; set; }
    public bool IsTouchdown { get; set; }
    public bool IsTurnover { get; set; }
}
```

### 4.2 PlayDelta — Computed Properties

```csharp
public class PlayDelta {
    public int YardsGained { get; }           // previous.YardLine - current.YardLine
    public bool LostYards { get; }            // YardsGained < 0
    public bool NewPossession { get; }        // possession changed
    public bool WasFirstDown { get; }         // current.Down==1 && previous.Down>1
    public bool WasThirdDownStop { get; }     // prev.Down==3 && curr.Down==1 && newPossession
    public bool WasFourthDownStop { get; }    // prev.Down==4 && curr.Down==1 && newPossession
    
    public static PlayDelta Calculate(PlaySnapshot previous, PlaySnapshot current)
}
```

### 4.3 TriggerEntry — Audio Mapping (Composite Key: Trigger + Event)

```csharp
public class TriggerEntry {
    public string Trigger { get; set; } = "";   // e.g., "down:1", "situation:touchdown"
    public string Event { get; set; } = "";     // e.g., "Offense: Touchdown Scored"
    public string AudioFile { get; set; } = ""; // path to .mp3/.wav
}
```

### 4.4 TriggerEvent — Rule Output

```csharp
public class TriggerEvent {
    public string EventKey { get; set; }        // matches TriggerEntry.Trigger OR TriggerEntry.Event
    public int Volume { get; set; } = 100;      // 0-100
    public bool IsEarnedBigEvent { get; set; }  // for "earned" big play bonuses
}
```

---

## 5. GameWatcher — Current Implementation (Fully Understood)

### 5.1 WatchedRegions (5 Defined)

| Region Name | FxX | FxY | FxW | FxH | Pattern | Purpose |
|-------------|-----|-----|-----|-----|---------|---------|
| `down` | 0.0 | 0.83 | 1.0 | 0.14 | `^\s*(\d)(?:st|nd|rd|th)\s*&\s*(\d+)` | Down & distance |
| `flag` | 0.0 | 0.83 | 1.0 | 0.14 | penalty flag detection | Penalties |
| `situation` | 0.0 | 0.83 | 1.0 | 0.14 | situation text | Red zone, goal line, etc. |
| `quarter` | 0.0 | 0.83 | 1.0 | 0.14 | `Q(\d)` | Quarter number |
| `banner` | 0.0 | 0.10 | 1.0 | 0.15 | banner text | Touchdown, turnover, etc. |

**All regions share bottom band (FxY=0.83, FxH=0.14) except banner (top)**

### 5.2 Key Logic in RunAsync (250ms Polling)

```csharp
// 1. Find game window
// 2. For each calibrated region: capture → OCR → regex match → NormalizeMatch
// 3. Cooldown check (2 seconds per region)
// 4. Fire RegionChanged / DownChanged events
// 5. SamplePossession() — separate tight crop of ribbon color
// 6. CheckForLossOfLoss() — parses "& -N" from down text
```

### 5.3 Events Exposed

```csharp
public event Action<string> DownChanged;           // down value "1", "2", "3", "4"
public event Action<string, string> RegionChanged; // regionName, value
public event Action<bool> PossessionChanged;       // true = away has ball
public event Action TackleForLossDetected;         // no args
public event Action<bool> WindowFoundChanged;      // window found/lost
```

### 5.4 Critical Behaviors

| Behavior | Implementation |
|----------|----------------|
| **Event Gating** | `EventGatedRegions = {"situation","banner","quarter"}` — reset on down change, NOT on blank OCR |
| **Possession Sampling** | Separate tight crop → average color → `ResolveTeamColor` delegate (distance-based, MaxMatchDistance=90) |
| **Cooldown** | 2 seconds per region prevents flicker re-fires |
| **Tackle for Loss** | Detects `& -N` pattern in down text (negative yards) |

---

## 6. WebMainForm — Current Trigger Flow (Fully Understood)

### 6.1 Matchup Workflow

```csharp
// 1. SetGameTeamsFromWeb(homeTeam, awayTeam) → loads _homeConfig, _awayConfig (List<TriggerEntry>)
// 2. ConfirmGametimeFromWeb() → _matchupLocked = true (prevents team changes)
// 3. ToggleWatchingFromWeb(true) → starts watcher, REQUIRES _matchupLocked
// 4. ToggleWatchingFromWeb(false) → stops watcher, _matchupLocked = false
```

### 6.2 Trigger Lookup Methods

```csharp
// FireEventForSide: looks up by TriggerEntry.Event property in side-specific config
FireEventForSide(side, eventName, volumeOverride)

// FireTriggerForSide: looks up by TriggerEntry.Trigger property (case-insensitive) in side-specific config
FireTriggerForSide(side, triggerName, volumeOverride)
```

### 6.3 Region → Trigger Key Mapping (OnRegionChanged)

```csharp
// ValueKeyedRegions = {"situation", "banner", "quarter"}
// For these: triggerKey = "{region}:{value}"  (e.g., "situation:touchdown")

// SideAwareEvents mapping:
"touchdown"        → "Offense: Touchdown Scored"
"turnover"         → "Defense: Turnover Forced"
"pat_good"         → "Offense: PAT Made"
"kickoff"          → "Other: Opening Kickoff"

// For SideAwareEvents: fires EVENT NAME (not trigger key) for possession side
// Respects HomeOnlyEventsForNow = true (temp: only home team events fire, TFL exempt)

// Other regions: triggerKey = "{region}:on"  (e.g., "flag:on")
```

### 6.4 Down Change Handling (OnDownChanged)

```csharp
trigger = "down:{down}"  // e.g., "down:1"
FireTriggerForSide(possessionSide, trigger)
// If HomeOnlyEventsForNow: only fires for home side
```

### 6.5 Tackle for Loss (OnTackleForLoss)

```csharp
// Fires "Defense: Tackle for Loss" for DEFENSE side based on _possession
// EXEMPT from HomeOnlyEventsForNow
```

### 6.6 Volume Control

```csharp
SetHomeVolumeFromWeb(volume)  // 0-100, side-specific
SetAwayVolumeFromWeb(volume)  // 0-100, side-specific
// Applied in FireEvent via volumeOverride parameter
```

---

## 7. Integration Gaps — What Must Be Implemented

### 7.1 Missing OCR Regions (GameWatcher)

| PlaySnapshot Field | Current Source | Needed |
|--------------------|----------------|--------|
| `YardLine` | ❌ None | New WatchedRegion (scoreboard area) |
| `HomeScore` | ❌ None | New WatchedRegion (scoreboard) |
| `AwayScore` | ❌ None | New WatchedRegion (scoreboard) |
| `TimeRemainingSeconds` | ❌ None | New WatchedRegion (clock) |
| `AwayTimeoutsRemaining` | ❌ None | New WatchedRegion (timeout indicators) |
| `BigGame` | ❌ None | Heuristic or config |
| `IsKickoff` | ❌ None | Detect from situation/banner |
| `IsPAT` | ❌ None | Detect from situation/banner |
| `IsPenaltyOnOffense` | ❌ Partial (flag region) | Parse flag region for offense/defense |
| `IsPenaltyOnDefense` | ❌ Partial (flag region) | Parse flag region for offense/defense |
| `IsTouchdown` | ✅ Banner "touchdown" | Already detected via banner |
| `IsTurnover` | ✅ Banner "turnover" | Already detected via banner |

### 7.2 GameState Maintenance (GameWatcher)

```csharp
// Need to add to GameWatcher:
private GameState _gameState = new GameState();
private EventRouter _eventRouter;  // inject via constructor

// In RunAsync after parsing all regions:
var snapshot = new PlaySnapshot {
    Down = parsedDown,
    YardsToGo = parsedYardsToGo,
    YardLine = parsedYardLine,
    HomeScore = parsedHomeScore,
    AwayScore = parsedAwayScore,
    Quarter = parsedQuarter,
    TimeRemainingSeconds = parsedTimeRemaining,
    AwayTimeoutsRemaining = parsedAwayTimeouts,
    BigGame = parsedBigGame,
    PossessionAway = _possessionAway,  // from PossessionChanged
    IsKickoff = detectedKickoff,
    IsPAT = detectedPAT,
    IsPenaltyOnOffense = detectedPenaltyOffense,
    IsPenaltyOnDefense = detectedPenaltyDefense,
    IsTouchdown = detectedTouchdown,
    IsTurnover = detectedTurnover,
};

// Swap current → previous, set new current
_gameState.Previous = _gameState.Current;
_gameState.Current = snapshot;

// Route through EventRouter
var triggerEvents = _eventRouter.Route(_gameState);

// Map TriggerEvent → TriggerEntry → AudioPlayer
foreach (var te in triggerEvents) {
    // Lookup in _homeConfig / _awayConfig based on possession side
    // FireEvent with volume from TriggerEvent.Volume
}
```

### 7.3 Rule Evaluators (New Classes Needed)

Implement `IRuleEvaluator` for each trigger type:

| Evaluator | TriggerEvent.EventKey | Conditions |
|-----------|----------------------|------------|
| `DownChangeEvaluator` | `down:1` \| `down:2` \| `down:3` \| `down:4` | `Delta.Down != 0` |
| `SituationChangeEvaluator` | `situation:{value}` | `Delta.SituationChanged` |
| `PossessionChangeEvaluator` | `possession:home` \| `possession:away` | `Delta.NewPossession` |
| `TackleForLossEvaluator` | `defense:tackle_for_loss` | `Delta.LostYards` |
| `TouchdownEvaluator` | `offense:touchdown_scored` | `Current.IsTouchdown && !Previous.IsTouchdown` |
| `TurnoverEvaluator` | `defense:turnover_forced` | `Current.IsTurnover && !Previous.IsTurnover` |
| `PATEvaluator` | `offense:pat_made` | `Current.IsPAT && !Previous.IsPAT` |
| `KickoffEvaluator` | `other:opening_kickoff` | `Current.IsKickoff && !Previous.IsKickoff` |
| `FirstDownEvaluator` | `offense:first_down` | `Delta.WasFirstDown` |
| `ThirdDownStopEvaluator` | `defense:third_down_stop` | `Delta.WasThirdDownStop` |
| `FourthDownStopEvaluator` | `defense:fourth_down_stop` | `Delta.WasFourthDownStop` |
| `ScoreChangeEvaluator` | `offense:score` \| `defense:score` | `Delta.HomeScoreChanged` \| `Delta.AwayScoreChanged` |
| `BigGameEvaluator` | `other:big_game` | `Current.BigGame && !Previous.BigGame` |

### 7.4 TriggerEntry → AudioPlayer Mapping

Current WebMainForm uses:
- `FireEventForSide` → looks up by **Event** property
- `FireTriggerForSide` → looks up by **Trigger** property

**Decision**: EventRouter should output `TriggerEvent.EventKey` matching **TriggerEntry.Trigger** (for `FireTriggerForSide`) OR **TriggerEntry.Event** (for `FireEventForSide`). 

**Recommendation**: Standardize on `Trigger` property (more explicit). Update WebMainForm to use `FireTriggerForSide` consistently, or have EventRouter output both keys.

---

## 8. Implementation Sequence

### Phase 1: Extend GameWatcher OCR (Highest Priority)
1. Add new `WatchedRegion` definitions for:
   - Yard line (scoreboard area, fractional coords)
   - Home/Away score (scoreboard)
   - Game clock (time remaining)
   - Timeout indicators (away timeouts)
2. Implement parsing logic in `RunAsync` for each new region
3. Calibrate fractional coordinates using broadcast skin reference

### Phase 2: GameState + EventRouter Integration
1. Add `GameState` and `EventRouter` fields to `GameWatcher`
2. Construct `PlaySnapshot` from all parsed OCR fields each loop
3. Maintain previous/current swap
4. Call `_eventRouter.Route(_gameState)` after snapshot update
5. Map `TriggerEvent` → `TriggerEntry` lookup → `AudioPlayer.Play()`

### Phase 3: Rule Evaluators
1. Create concrete `IRuleEvaluator` classes (one per trigger type)
2. Register all evaluators in `EventRouter` constructor
3. Test each evaluator against `PlayDelta` logic

### Phase 4: WebMainForm Integration
1. Inject `EventRouter` into `GameWatcher` from `WebMainForm`
2. Replace/augment current event handlers (`OnDownChanged`, `OnRegionChanged`, etc.) with EventRouter-driven flow
3. Ensure side-aware volume (`_homeVolume`, `_awayVolume`) applied from `TriggerEvent.Volume`
4. Remove `HomeOnlyEventsForNow` once side-aware routing works

---

## 9. Critical Decisions Needed

| Decision | Options | Recommendation |
|----------|---------|----------------|
| **TriggerEvent.EventKey format** | Match `TriggerEntry.Trigger` OR `TriggerEntry.Event` | Match `Trigger` (more explicit, used by `FireTriggerForSide`) |
| **Side-aware routing** | EventRouter knows possession side OR WebMainForm maps after | EventRouter outputs side-neutral events; WebMainForm maps to side config |
| **Volume source** | TriggerEvent.Volume OR WebMainForm side volume | TriggerEvent.Volume as base, multiplied by side volume (0-1) |
| **HomeOnlyEventsForNow** | Keep as config flag OR remove | Remove once side-aware routing verified |
| **OCR calibration** | Hardcode fractional coords OR external config | External JSON config for broadcast-skin adaptability |

---

## 10. File Paths Reference

```
/Users/user/CODING/PROJECTS/BANDROOM/
├── GameWatcher.cs              # Main OCR loop — EXTEND HERE
├── TriggerEntry.cs             # Audio mapping — READ ONLY
├── WebMainForm.cs              # UI + trigger handling — INTEGRATE HERE
├── src/
│   ├── Bandroom.Core/
│   │   ├── GameState.cs        # ✅ Understood
│   │   ├── PlaySnapshot.cs     # ✅ Understood (18 fields)
│   │   ├── PlayDelta.cs        # ✅ Understood
│   │   ├── EventRouter.cs      # ✅ Understood
│   │   ├── TriggerEvent.cs     # ✅ Understood
│   │   └── IRuleEvaluator.cs   # ✅ Understood
│   └── Bandroom.Mac/           # macOS port (ignore for now)
```

---

## 11. Quick-Start for Next Agent

```bash
# 1. Open project
cd /Users/user/CODING/PROJECTS/BANDROOM/

# 2. Read GameWatcher.cs first (entry point)
# 3. Add new WatchedRegion definitions for missing fields
# 4. Implement parsing in RunAsync
# 5. Add GameState + EventRouter integration
# 6. Create rule evaluators in Bandroom.Core or new folder
# 7. Wire up in WebMainForm constructor
```

---

## 12. Known Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| OCR accuracy on new regions | Calibrate fractional coords per broadcast skin; add confidence thresholds |
| PlaySnapshot field completeness | Start with core fields (down, distance, yardline, score, clock); add flags incrementally |
| EventRouter performance | 250ms loop → keep evaluators lightweight; cache TriggerEntry lookups |
| Side-aware volume math | `finalVolume = triggerEvent.Volume * sideVolume / 100.0f` |
| Matchup locking race | Ensure `_matchupLocked` checked before EventRouter routes |

---

## 13. Testing Checklist

- [ ] All 18 PlaySnapshot fields populated from OCR
- [ ] PlayDelta correctly calculates YardsGained, NewPossession, WasFirstDown, etc.
- [ ] Each IRuleEvaluator fires correct TriggerEvent for its condition
- [ ] TriggerEvent.EventKey matches TriggerEntry.Trigger in config
- [ ] Side-aware audio plays for home/away correctly
- [ ] Volume respects both TriggerEvent.Volume and side volume slider
- [ ] No duplicate triggers (cooldown respected)
- [ ] Matchup lock prevents team change during game
- [ ] Stop watching unlocks matchup

---

*End of Integration Plan — Ready for Implementation*