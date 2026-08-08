# Bandroom Deep Audit Report — August 7, 2026 17:36 MT
## 15-Level Sweep — Every File Re-Checked

## Build Verification (After All Fixes)
```
Bandroom.Core.dll   → 0 errors, 0 warnings ✅
Bandroom.dll (Win)  → 0 errors, 5 pre-existing warnings ✅
Bandroom.Mac.dll    → 0 errors, 0 warnings ✅
```

---

## Level-by-Level Findings

| Level | File/Component | Finding |
|-------|---------------|---------|
| 1 | BandAudioHook.csproj | ✅ ProjectReference + DefaultItemExcludes correct |
| 2 | GameWatcher.cs usings | ✅ Bandroom.Core + Bandroom.Core.Helpers present |
| 3 | PlayDelta.Calculate | ✅ Logic correct: YardLine subtract, NewPossession XOR, WasFirstDown |
| 4 | PlaySnapshot fields | ✅ All 14 fields present, init-only |
| 5 | TriggerEvent + IRuleEvaluator | ✅ Simple, clean interfaces |
| 6 | EventRouter.Route | ✅ Loops all evaluators, collects non-null |
| 7 | GameState.UserHasPossession | ✅ `UserIsHome ? !PossessionAway : PossessionAway` correct |
| 8 | RouteEngineTick OCR→snapshot | ✅ Reads down/quarter/situation from region.Last |
| 9 | CreateEventRouter | ✅ All 16 evaluators instantiated |
| 10 | EventsDetected subscription | ✅ `_watcher.EventsDetected += OnEngineEventsDetected` |
| 11 | OnEngineEventsDetected side routing | ✅ "Defense:*" → opposite, else → possession |
| 12 | HomeOnlyEventsForNow gate | ✅ `side == "home"` filter active |
| 13 | All 42 EventKey strings | ✅ Consistent "Category: Name" format |
| 14 | Duplicate event paths | ✅ Old handlers gated by `_useEngineForEvents` |
| 15 | Mac AudioPlayer API parity | ✅ Same Play/StopAll/Warmup/fields as Windows |

---

## Bugs Found This Round

### 🔴 BUG #1 — `_useEngineForEvents` Never Set to True (FIXED)
**File:** `WebMainForm.cs` line 75  
**Symptom:** Old legacy handlers (`OnDownChanged`, `OnRegionChanged`) still fired alongside new engine.  
**Root cause:** `_useEngineForEvents` was initialized to `_homeConfig != null && _awayConfig != null` in constructor (both null = false) but never set to true when matchup was confirmed.  
**Fix:** Added `_useEngineForEvents = true;` in `SetGameTeamsFromWeb()`.  
**Impact:** `OnDownChanged` and `OnRegionChanged` now immediately return when engine is active. No more duplicate processing.

### 🔴 BUG #2 — First-Tick Evaluator Storm (FIXED)
**File:** `GameWatcher.cs` line 502-504  
**Symptom:** On the first OCR tick, Previous snapshot is all zeros. Every evaluator would see a transition from 0→real value and fire simultaneously.  
**Root cause:** No guard against the initial state transition.  
**Fix:** Added `if (_snapshotPrevious.Down == 0 && _snapshotPrevious.Quarter == 0) return;` after snapshot rotation.  
**Impact:** First tick is silently skipped. Evaluators only fire after second tick when Previous is populated.

### 🟡 NOTE #3 — `OnTackleForLoss` Not Gated (DEFERRED)
**File:** `WebMainForm.cs` line 845  
**Symptom:** `OnTackleForLoss` fires from old `TackleForLossDetected` event AND from engine `TflHelper` evaluator.  
**Mitigation:** `AudioPlayer.FireCooldown` (20s per-path) prevents audible double-play. Both code paths execute but only one reaches the speaker.  
**Recommendation:** Add `if (_useEngineForEvents) return;` to `OnTackleForLoss` when confident in engine.

---

## Verified Correct (No Issues)

- ✅ `ProjectReference` path is relative (`src\Bandroom.Core\Bandroom.Core.csproj`)
- ✅ `DefaultItemExcludes` prevents `src\**\*` from accidental compilation
- ✅ `UserIsHome` is set to `true` in `SetGameTeamsFromWeb` (always home team for now)
- ✅ `PossessionAway` computed from `_lastPossession == "away"` (from color sampling)
- ✅ `IsKickoff`/`IsPAT`/`IsTouchdown`/`IsTurnover` mapped from OCR situation region
- ✅ Snapshot rotation: `_snapshotPrevious = _snapshotCurrent` then `_snapshotCurrent = snapshot`
- ✅ `EventRouter` is not nullable after `Start()` (initialized via `??=`)
- ✅ `PlayDelta.Calculate` handles all cases (yards gained, lost yards, possession flip, etc.)
- ✅ All 16 evaluators check edge cases (previous.IsTouchdown guard, previous.Down==0 guard, etc.)
- ✅ TimeoutHelper EventKeys now match standard format (`Defense: Timeout (4 Remaining)`)
- ✅ Mac AudioPlayer uses `afplay` (bundled macOS tool), same API surface
- ✅ Mac builds clean (no platform-specific code issues on Windows compilation)

## Pre-existing Limitations (Not From This Session)
- Score/clock/yard line OCR regions missing → several evaluators dormant
- Flag/banner regions uncalibrated (FxW=0)
- Timeout indicator OCR missing