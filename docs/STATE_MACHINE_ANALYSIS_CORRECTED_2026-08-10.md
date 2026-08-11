# Bandroom State-Machine Analysis — Corrected Audit (August 10, 2026)

**Date:** August 10, 2026  
**Author:** Senior State-Machine & Event-Driven Systems Engineer  
**Prior Analysis:** `docs/STATE_MACHINE_ANALYSIS.md` (August 8, 2026) — 10 discrepancies.  
**This document obsoletes:** `STATE_MACHINE_ANALYSIS_UPDATED_2026-08-10.md` — contained factual errors in Discrepancies #11, #13, #14.  
**Methodology:** Every evaluator re-read from source, every interaction traced tick-by-tick against actual `GameState` conditions. No extrapolation from comments alone.

---

## Executive Summary

**All 10 original discrepancies from the August 8 audit are FIXED.** Seven required code changes, two were cosmetic/documentation, one (Offense: Fourth Down) was addressed in the August 8-10 OffenseDownHelper rewrite. Three new evaluators were added (DefenseFirstDownHelper, DefenseThirdDownShortHelper, PregameHelper), bringing the total to 19 evaluators. EventRouter gained an in-engine Dedupe pass. The side-routing model was redesigned into a 3-tier system (home-only, un-gated Offense, ordinary Defense).

**Four new discrepancies** were discovered in this re-audit:

| # | Issue | Severity |
|---|---|---|
| **11** | **TflHelper collides with down-specific Loss evaluators** — simultaneous double-cue on every TFL | **HIGH** |
| **12** | **Split-tick loss detection: silence on 2nd/3rd down TFL** — neither evaluator fires when down/yds change on separate ticks | **HIGH** |
| **13** | **Penalty overlay flicker double-fire** — `penaltyagainst` region not gated against replay flicker | **MEDIUM** |
| **14** | **KickoffHelper silently dropped 2 EventKeys** — "Kickoff on Kick (Receiving/Kicking)" no longer fire, cards still user-assignable | **MEDIUM** |

---

## Architecture Overview

Three-layered event-driven state machine:

1. **OCR/Input Layer** — `GameWatcher.RunAsync` polling loop (window detection, region OCR at 250ms, color sampling, possession detection)
2. **Game-State Engine** — `RouteEngineTick` → `PlaySnapshot` (19 fields) → `PlayDelta` → 19 `IRuleEvaluator` classes → `EventRouter.Dedupe` → `EventsDetected`
3. **Audio/UI Layer** — `OnEngineEventsDetected` → `ResolveEventRouting` (3-tier) → `FireEventForSide` → `AudioPlayer.Play`

---

## DISCREPANCY STATUS: ORIGINAL 10 (August 8)

| # | Issue | Status |
|---|---|---|
| 1 | TimeoutHelper level-triggered | ✅ FIXED — edge detection: `Current < Previous` |
| 2 | DownFieldPositionHelper Midfield always true | ✅ FIXED — gated on `YardLine > 0` |
| 3 | Duplicate DefenseHelper + DownFieldPositionHelper | ✅ FIXED — Loss variants removed from DFP; Dedupe added |
| 4 | BigEventHelper + DefenseHelper 3rd-down ambiguity | ✅ FIXED — NewPossession guard in both |
| 5 | Safety + 2-pt conversion overlap | ✅ FIXED — `possessingSideGained2` check |
| 6 | FieldGoalMissed may never fire | ✅ FIXED — uses `IsFieldGoalAttempt` from banner region |
| 7 | OCR blanking race on non-sticky fields | ✅ FIXED — sticky `_lastDistanceRaw` |
| 8 | NoPuntReturn comment clarity | COSMETIC — logic was correct |
| 9 | Dual TFL fire path (legacy + engine) | ✅ FIXED — `_useEngineForEvents` gate |
| 10 | No Offense: Fourth Down | ✅ FIXED — OffenseDownHelper fires `Defense: Fourth Down` |

---

## COMPLETE EVALUATOR TRANSITION MAP (19 Evaluators, Verified Against Source)

### TouchdownHelper
```
CURRENT STATE: !Current.IsTouchdown
  + Current.IsTouchdown && !Previous.IsTouchdown (edge)
  = Delta.NewPossession → "Defense: Touchdown Scored", Vol 85/100
  = Else → "Offense: Touchdown Scored", Vol 85/100
  + IsEarnedBigEvent=true
```

### TurnoverHelper
```
CURRENT STATE: !Previous.IsTurnover
  + Current.IsTurnover && !Previous.IsTurnover (edge)
  = Q4+ && time ≤ 120 → "Defense: Iced Game by Turnover", Vol 100
  = Else → "Defense: Turnover Forced", Vol 80/100
  + IsEarnedBigEvent=true
```

### TouchdownHelper / TurnoverHelper — No Interaction Issue
Both check different OCR flags (`IsTouchdown` vs `IsTurnover`) from the same region but mutually exclusive by `NormalizeMatch`. **No collision possible.**

### FieldGoalPATHelper
```
  + scoreDiff==1 && IsPAT → "Offense: PAT Made", Vol 75
  + scoreDiff==2 && possessingSideGained2 → "Offense: 2-Point Conversion Made", Vol 85, IsEarnedBigEvent=true
  + scoreDiff==3 → "Offense: Field Goal Made", Vol 85, IsEarnedBigEvent=false
```

### SafetyHelper
```
  + Previous.PossessionAway && homeDelta==2 → "Defense: Safety", Vol 100
  + !Previous.PossessionAway && awayDelta==2 → "Defense: Safety", Vol 100
```

### FieldGoalPATHelper / SafetyHelper — No Collision
`FieldGoalPATHelper` checks `possessingSideGained2` for scoreDiff==2 (offense's own score must move by 2). SafetyHelper checks the OPPOSITE: the non-possession side gains 2. These conditions are mutually exclusive. **Fixed — no collision.**

### FieldGoalMissedHelper
```
  + Current.IsFieldGoalAttempt && Delta.NewPossession && homeDelta==0 && awayDelta==0
  = "Defense: Field Goal Missed by Opponent", Vol 85, IsEarnedBigEvent=true
```

### FirstDownHelper
```
  + Delta.WasFirstDown && Previous.Down > 0 && !Delta.NewPossession
  = Current.YardsToGo <= 5 → "Offense: Earned First Down Short", Vol 90
  = Else → "Offense: Earned First Down", Vol 80
  + (Midfield variant commented out — YardLine always 0)
```

### OffenseDownHelper (REWRITTEN 2026-08-10)
```
  + Current.Down != Previous.Down [guard]
  + Delta.NewPossession → return null [skip — turnover territory]
  + Current.YardsToGo > Previous.YardsToGo → return null [DEFER to DefenseHelper]
  + Down 2, short (≤3 yds) → "Offense: Second Down Short", Vol 70/100
  + Down 2, long (>3 yds)  → "Defense: Second Down", Vol 70/100
  + Down 3, short (≤3 yds) → "Offense: Third Down Short", Vol 70/100
  + Down 3, long (>3 yds)  → "Defense: Third Down", Vol 70/100
  + Down 4                  → "Defense: Fourth Down", Vol 70/100
  + IsEarnedBigEvent=false
```

### DefenseHelper (REWRITTEN 2026-08-10 — Loss-only)
```
  + !UserHasPossession [guard]
  + Current.Down == Previous.Down → return null [edge guard]
  + Delta.NewPossession → return null [skip]
  + Down 3, YardsToGo > Previous → "Defense: Third Down (Loss)", Vol 75/100, IsEarnedBigEvent=true
  + Down 2, YardsToGo > Previous → "Defense: Second Down (Loss)", Vol 75/100, IsEarnedBigEvent=true
  + (Down 4 NOT handled here)
```

### BigEventHelper
```
  + Down 3, Delta.NewPossession, !IsTurnover → "Defense: Third Down", Vol 80/100, IsEarnedBigEvent=BigGame
  + Down 4, YardsToGo > Previous → "Defense: Fourth Down (Loss)", Vol 85/100, IsEarnedBigEvent=true
  + Down 4, Delta.NewPossession, !IsFieldGoalAttempt, !IsTurnover → "Defense: Fourth Down", Vol 80/100, IsEarnedBigEvent=BigGame
  + ⚠️ Note: line 22 uses YardsToGo comparison (NOT LostYards) — already fixed
```

### TflHelper
```
  + Previous.Down == 0 || Current.Down == 0 → return null
  + Current.Down > Previous.Down && Current.YardsToGo > Previous.YardsToGo
  = "Defense: Tackle for Loss", Vol 75/100, IsEarnedBigEvent=true
  + ⚠️ Requires BOTH down increase AND yards increase on SAME tick
```

### DownFieldPositionHelper (Midfield-only, dormant)
```
  + Previous.Down > 0 && Current.Down != Previous.Down [guard]
  + Defense side, Down 2, YardLine > 0 && YardLine ≤ 50 → "Defense: Second Down (Midfield)"
  + Offense side, Down 2, YardLine > 0 && YardLine ≤ 50 → "Offense: Second Down (Midfield)"
  + ⚠️ YardLine always 0 → atMidfield always false → both branches dormant (correctly)
```

### KickoffHelper
```
  + !Current.IsKickoff → _didFire=false; return null
  + _didFire → return null [edge guard]
  + Quarter 1, !_openingKickoffFired → "Other: Opening Kickoff", Vol 90
  + Quarter 3, !_secondHalfKickoffFired → "Other: Second-Half Kickoff", Vol 90
  + ⚠️ "Other: Kickoff on Kick (Receiving/Kicking)" REMOVED 2026-08-10 — no evaluator fires them
```

### PenaltyHelper
```
  + Current.IsPenaltyOnOffense && !Previous.IsPenaltyOnOffense → "Penalty: Offense", Vol 70
  + Current.IsPenaltyOnDefense && !Previous.IsPenaltyOnDefense → "Penalty: Defense", Vol 70
  + ⚠️ Edge detection IS present (lines 13/24). But underlying OCR region not gated.
```

### GameStateEventHelper
```
  + Quarter 1→2 → "Other: Start of 2nd Quarter", Vol 70
  + Quarter 3→4 → "Other: Start of 4th Quarter", Vol 80
  + Q0→Q1 && D0→D+ → "Other: Pregame Take the Field", Vol 85
  + WasFirstDown && !NewPossession && Q4+ && time≤120 → "Offense: Iced Game by First Down", Vol 100
  + Q4+ && time≤30 && lead≥9 → "Offense: Victory in Hand", Vol 100 (self-tracked _didFire)
```

### TimeoutHelper
```
  + !UserHasPossession && TimeRemainingSeconds ≤ 240 [guard]
  + AwayTimeoutsRemaining < 0 || > 6 → return null
  + Current.AwayTimeoutsRemaining >= Previous → return null [edge guard — FIXED]
  + Fires "Defense: Timeout (N Remaining)" for N=4,3,2,1,0, Vol 65/100
```

### DriveStarterHelper, NoPuntReturnHelper, DefenseFirstDownHelper, DefenseThirdDownShortHelper, PregameHelper
(Verified against source — all correct, no interaction issues found beyond those noted in Discrepancy #11/#15 below.)

---

## NEW DISCREPANCIES (August 10 Re-Audit, Verified Against Source)

---

### NEW DISCREPANCY #11 — TflHelper Collides with Down-Specific Loss Evaluators (Simultaneous Double-Cue)

**Severity:** HIGH  
**Category:** Duplicate Transition (Different EventKeys, Same Event)

**1. Intended behavior:** One tackle-for-loss produces one audio cue. Either the down-specific Loss variant or the generic TFL cue — not both simultaneously.

**2. Actual implementation:** On a same-tick loss (down changes AND yards-to-go increases on the same OCR pass), BOTH evaluators fire with different EventKeys that Dedupe does not catch:

**2nd down loss:**
- `DefenseHelper` (line 54): `Down == 2 && YardsToGo > Previous` → `"Defense: Second Down (Loss)"`, Vol 75/100
- `TflHelper` (line 17): `Down > Previous && YardsToGo > Previous` → `"Defense: Tackle for Loss"`, Vol 75/100

**3rd down loss:**
- `DefenseHelper` (line 34): `Down == 3 && YardsToGo > Previous` → `"Defense: Third Down (Loss)"`, Vol 75/100
- `TflHelper` (line 17): `Down > Previous && YardsToGo > Previous` → `"Defense: Tackle for Loss"`, Vol 75/100

**4th down loss:**
- `BigEventHelper` (line 22): `Down == 4 && YardsToGo > Previous` → `"Defense: Fourth Down (Loss)"`, Vol 85/100
- `TflHelper` (line 17): `Down > Previous && YardsToGo > Previous` → `"Defense: Tackle for Loss"`, Vol 75/100

`EventRouter.Dedupe` does NOT catch these because the EventKeys differ. The same-tick multi-fire layering fix in `OnEngineEventsDetected` passes `interruptPrevious: false` for the second event, so both cues play simultaneously — producing an audible clash.

**3. Why it differs:** `DefenseHelper` and `BigEventHelper` were rewritten to use `YardsToGo` comparison for their Loss branches. `TflHelper` was also rewritten to use `YardsToGo > Previous && Down > Previous`. All three independently evaluate the same game-state change without mutual exclusion.

**4. Possible failure:** On any tackle-for-loss play, the user hears TWO audio cues playing at the same time: the down-specific Loss cue AND the generic "Tackle for Loss" cue. Volume is the same for both (75 or 100 for BigGame), so neither is quieter — they compete for audibility.

**5. Recommended fix:** Add a guard to `TflHelper` that suppresses firing when a down-specific Loss evaluator already covered the same event:

```csharp
// In TflHelper.Evaluate, after existing guards:
// DefenseHelper already covers 2nd/3rd down loss with a more specific cue;
// BigEventHelper covers 4th down loss.
// Don't fire the generic TFL alongside a down-specific one.
if (state.Current.Down == 2 || state.Current.Down == 3)
    return null; // DefenseHelper owns 2nd/3rd down losses
if (state.Current.Down == 4)
    return null; // BigEventHelper owns 4th down losses
```

Or alternatively, remove TflHelper entirely and fold its remaining non-overlapping cases into the down-specific evaluators. TflHelper currently only fires when `Down > Previous && YardsToGo > Previous`. DefenseHelper fires when `Down == 2/3 && YardsToGo > Previous`. BigEventHelper fires when `Down == 4 && YardsToGo > Previous`. The conditions are strict subsets — TflHelper's coverage is fully duplicated by the other two evaluators.

**6. Regression test:** Simulate a 2nd-down TFL (Down 1→2, YardsToGo 10→12). Verify exactly one event fires: `"Defense: Second Down (Loss)"`. Verify `"Defense: Tackle for Loss"` does NOT fire. Repeat for 3rd-down and 4th-down TFLs.

---

### NEW DISCREPANCY #12 — Split-Tick Loss Detection: Silence on 2nd/3rd Down TFL

**Severity:** HIGH  
**Category:** Missing Transition (Tick-Ordering Race)

**1. Intended behavior:** A tackle-for-loss always produces some audio cue — the down-specific Loss variant if possible, the generic TFL as fallback.

**2. Actual implementation:** When the OCR scorebug updates the down and the yards-to-go on different capture ticks:

**Tick A:** Down changes (e.g., 1st→2nd), yards-to-go still shows old pre-loss value (e.g., 10):
- `OffenseDownHelper`: Down 2, yards not increased, not short (10 > 3) → fires `"Defense: Second Down"` (the **normal** long-yardage variant, not a Loss cue)
- `DefenseHelper`: Down changed, yards NOT increased → return null
- `TflHelper`: Down increased (2>1), yards NOT increased → return null

**Tick B (~250ms later):** Down unchanged (still 2nd), yards-to-go FINALLY updates (10→13, reflecting the loss):
- `OffenseDownHelper`: `Current.Down == Previous.Down` → return null
- `DefenseHelper`: `Current.Down == Previous.Down` → return null (edge guard at line 18)
- `TflHelper`: `Current.Down (2) > Previous.Down (2)` → **FALSE** → return null

**RESULT:** Only the normal `"Defense: Second Down"` fires on Tick A. NOTHING fires on Tick B. The user never hears any Loss cue for this play.

For 4th down, `BigEventHelper` does NOT have a `Current.Down == Previous.Down` guard — it only checks `Current.Down == 4 && YardsToGo > Previous` — so it WOULD fire correctly on Tick B. But for 2nd and 3rd down losses, the gap is real.

**3. Why it differs:** Both `DefenseHelper` and `TflHelper` require the down change AND the yards-to-go change to arrive on the SAME OCR tick. The scorebug HUD's down indicator and distance text can update on different physical frames (different render passes of the game's UI), and OCR captures each independently at 250ms intervals. A single loss play that produces a split across two OCR ticks is invisible to both evaluators.

**4. Possible failure:** On any 2nd or 3rd down tackle-for-loss where the scorebug updates down and distance on different frames (likely on the first play of a drive, or any play where the HUD redraws the down indicator slightly ahead of the distance text), the user hears a normal down cue followed by silence — the most exciting defensive outcome produces the least exciting audio response.

**5. Recommended fix:** Remove the `Current.Down == Previous.Down` edge guard from `DefenseHelper`'s Loss branches. The non-Loss branches no longer exist in DefenseHelper (they were removed in the 2026-08-10 rewrite), so the guard only gates the two Loss branches. Let them fire on ANY tick where `Down == 2/3 && YardsToGo > Previous`:

```csharp
// In DefenseHelper.Evaluate, after !UserHasPossession and Delta.NewPossession checks:
// Remove: if (state.Current.Down == state.Previous.Down) return null;

// Down 3 Loss — fire on any tick where 3rd down got longer
if (state.Current.Down == 3 && state.Current.YardsToGo > state.Previous.YardsToGo)
    return new TriggerEvent { EventKey = "Defense: Third Down (Loss)", ... };

// Down 2 Loss — same pattern
if (state.Current.Down == 2 && state.Current.YardsToGo > state.Previous.YardsToGo)
    return new TriggerEvent { EventKey = "Defense: Second Down (Loss)", ... };
```

This would be safe because:
- The `Delta.NewPossession` guard still prevents firing during a turnover
- A second tick with the same loss (yards already reflected) would have `YardsToGo == Previous` and return null (no re-fire)
- OffenseDownHelper already defers to DefenseHelper when yards increase

**6. Regression test:** Simulate Tick A: Down 2→3, YardsToGo unchanged (7). Tick B: Down unchanged (3), YardsToGo 7→9. Verify Tick A fires normal `"Defense: Third Down"` (from OffenseDownHelper). Verify Tick B fires `"Defense: Third Down (Loss)"` (from DefenseHelper). Verify no duplicate fires on subsequent ticks with same values.

---

### NEW DISCREPANCY #13 — Penalty Overlay Flicker Double-Fire

**Severity:** MEDIUM  
**Category:** Potential Double-Fire (Region Re-Arming)

**1. Intended behavior:** Each penalty produces exactly one audio cue.

**2. Actual implementation:** The `penaltyagainst` WatchedRegion is NOT in `EventGatedRegions` (only `situation`, `banner`, and `quarter` are). This means when the penalty decision overlay text clears (camera cut, replay), `region.Last` resets to `null`. If the overlay reappears on a replay angle, `region.Last` goes from `null` to the penalty text again, OCR re-matches, and `IsPenaltyOnOffense`/`IsPenaltyOnDefense` toggles `false → true` a second time.

`PenaltyHelper.Evaluate` (lines 13, 24) correctly uses edge detection (`!Previous.IsPenaltyOn*`), so it would fire both times — once on the original overlay appearance, and again on the replay re-appearance.

**3. Why it differs:** `penaltyagainst` was not added to `EventGatedRegions` when the region was created. Every other region that maps to a situational flag (`situation`, `banner`, `quarter`) IS gated. The penalty detection was an oversight.

**4. Possible failure:** A penalty replay that shows the "Against Team X" text, cuts to a replay angle that clears it, then cuts back to the live feed showing the penalty text again fires `"Penalty: Offense"` or `"Penalty: Defense"` twice for one real flag. Currently partially masked by `AudioPlayer.FireCooldown` (20s per file path), but two rapid penalty replays within 20 seconds could still produce an audible double-fire, or legitimately distinct back-to-back penalties could be suppressed by the same cooldown.

**5. Recommended fix:** Add `penaltyagainst` to `EventGatedRegions`:
```csharp
static readonly HashSet<string> EventGatedRegions = new(StringComparer.OrdinalIgnoreCase)
    { "situation", "banner", "quarter", "penaltyagainst" };
```
This makes the penaltyagainst region only reset on a down change (same as situation/banner/quarter), so replay flicker cannot re-arm it.

**6. Regression test:** Simulate penalty overlay ON → OFF → ON on the same down. Verify exactly 1 penalty event fires. Simulate two genuinely separate penalties on different downs — verify both fire independently.

---

### NEW DISCREPANCY #14 — KickoffHelper Silently Dropped 2 User-Assignable EventKeys

**Severity:** MEDIUM  
**Category:** Missing Transition (Silent Dropped Events)

**1. Intended behavior:** All EventKeys that have user-assignable cards continue to fire when their conditions are met. If an event is deliberately removed, its card should be hidden from the UI and existing user assignments migrated or warned about.

**2. Actual implementation:** `KickoffHelper` was rewritten on 2026-08-10 to remove the `"Other: Kickoff on Kick (Receiving)"` and `"Other: Kickoff on Kick (Kicking)"` events. The comment (lines 67-73) explains the product decision: these collided with PAT GOOD detection and weren't worth the noise. The evaluator now only fires `"Other: Opening Kickoff"` and `"Other: Second-Half Kickoff"`.

However:
- These two EventKeys still exist in `ConfigStore.AllEngineEventKeys` (the list the web UI reads to render assignable cards)
- Users who assigned songs to `"Kickoff on Kick (Receiving)"` or `"Kickoff on Kick (Kicking)"` will hear **permanent silence** for those assignments with no warning, error, or migration
- The generic profile's default pack may also reference these keys

**3. Why it differs:** The evaluator was changed but `ConfigStore.AllEngineEventKeys` was not updated. There is no migration step that warns the user or moves their assignments. This is the same class of bug as the original Discrepancy #10 (Offense: Fourth Down had no evaluator), but for an intentional removal rather than an omission.

**4. Possible failure:** A user who assigned custom songs to either kickoff variant hears nothing when kickoffs happen after scores during a game. They may assume the app is broken or that their song was deleted, when in reality the evaluator simply stopped firing those keys.

**5. Recommended fix:** Remove the two keys from `ConfigStore.AllEngineEventKeys` so new users never see assignable cards that can't fire. For existing users, add a migration step that logs a warning (to `EventActivityLog`) when loading a profile that has assignments on either key:

```csharp
// In ConfigStore.LoadProfile or equivalent:
if (entry.Trigger.Contains("Kickoff on Kick") && !string.IsNullOrWhiteSpace(entry.AudioFile))
{
    EventActivityLog.Record(entry.Event, "n/a", 
        $"{entry.Event} was retired — kickoff sounds now only fire for opening and second-half kickoffs. " +
        $"Your song '{Path.GetFileName(entry.AudioFile)}' is still in your library but won't play here anymore.");
}
```

**6. Regression test:** Verify `ConfigStore.AllEngineEventKeys` no longer includes "Other: Kickoff on Kick (Receiving)" or "Other: Kickoff on Kick (Kicking)". Verify a profile with songs assigned to those keys shows a warning on load. Verify opening and second-half kickoffs still fire correctly.

---

## INVALID / IMPOSSIBLE STATES

### Dead Signal: PlayDelta.YardsGained and LostYards
`YardLine` is hardcoded to 0 in `RouteEngineTick` line 974. No OCR region reads yard line. `YardsGained` is always `0 - 0 = 0`. `LostYards` is always `0 < 0 = false`.

**All evaluators that used `LostYards` have been fixed**:
- `DefenseHelper`: now uses `YardsToGo > Previous.YardsToGo` ✅
- `BigEventHelper`: now uses `YardsToGo > Previous.YardsToGo` ✅ (line 22)
- `TflHelper`: now uses `Down > Previous.Down && YardsToGo > Previous.YardsToGo` ✅
- `DownFieldPositionHelper`: Midfield variants gated on `YardLine > 0` (correctly dormant) ✅
- `FirstDownHelper`: Midfield variant commented out ✅
- **No remaining consumers of `LostYards` or `YardsGained`.**

### Dead State: atMidfield == true
Both `DownFieldPositionHelper` and `FirstDownHelper` correctly gate Midfield variants behind `YardLine > 0`, which is always false. These evaluator branches are dormant but correctly guarded — they will activate automatically when yard-line OCR is built.

### Invalid State: IsTouchdown + IsTurnover Simultaneously
Both set from the same `NormalizeMatch` result on the "situation" region. Mutually exclusive by definition. **Safe.**

---

## RACE CONDITION ANALYSIS (Updated)

### Race #1: Possession Sampling vs OCR Tick Ordering
`SamplePossessionFromWindow` runs during "down" region processing, BEFORE "flag"/"situation" re-read on same tick. Suppression gates use PREVIOUS tick values. A flag appearing and vanishing within one 250ms cycle could still mis-sample. **Still present, mitigated by cooldown.**

### Race #2: Multiple Evaluators — Same-Tick Collision
`EventRouter.Dedupe` eliminates same-EventKey duplicates. The layering fix (`interruptPrevious: !firedYet`) prevents distinct events from cutting each other off. However, `TflHelper` + down-specific Loss evaluators still collide on same-tick TFLs (Discrepancy #11). **Partially fixed, #11 remains.**

### Race #3: _possession Null During Routing
Fixed: `OnEngineEventsDetected` returns early for side-specific events when `_possession` is null, logs them as skipped. Only side-agnostic "Other:*" events route to "home" as fallback. ✅

### Race #4: OCR Thread vs UI Thread
Snapshots rotate before UI thread processes events. Theoretically possible under extreme load. 250ms poll interval makes it practically impossible.

### Race #5: Preload vs First Trigger
Fixed: `StartWatchingIfMatchupSet` blocks on `AudioCache.Preload()` before starting watcher. ✅

---

## EVENTS THAT COULD ARRIVE OUT OF ORDER

1. **Down change before situational banner** — scorebug can update down indicator before situation text (TOUCHDOWN/KICKOFF). One-tick detection gap.

2. **Score update before PAT/field-goal banner** — score digits may OCR before the banner text. `FieldGoalPATHelper` checks both score AND `IsPAT` — if score arrives first, event is missed for one tick.

3. **Possession underline before down change** — after a turnover, the underline color can flip before the down resets to 1st. The structural turnover backstop in `RouteEngineTick` partially closes this.

4. **Down change before yards-to-go update** — the scorebug can update the down indicator on a different render frame than the distance text. Causes Discrepancy #12 (silent loss on split ticks).

5. **Field goal banner before possession flip** — `FieldGoalMissedHelper` requires `IsFieldGoalAttempt && NewPossession`. If the banner clears before possession flips, the event is missed.

---

## STATE SYNCHRONIZATION PROBLEMS

### Problem 1: _lastPossession / _possession / PossessionAway
Three variables representing the same fact, updated via different mechanisms:
- `_lastPossession`: set by sampling with cooldown
- `_possession`: set by `PossessionChanged` event
- `PossessionAway`: read from `_lastPossession` every tick

Fixed in Aug 8-10: `_lastPossession` now only updates TOGETHER with `PossessionChanged` event. But the three-variable architecture remains fragile.

### Problem 2: _homeConfig / _awayConfig In-Memory vs Disk
Loaded at matchup time, refreshed via `RefreshHomeAwayConfigIfNeeded` on save. No auto-reload during a live game. A cloud sync or external import touching the same profile on disk would leave in-memory copies stale.

---

## ARCHITECTURAL RECOMMENDATIONS

1. **Remove TflHelper or add mutual exclusion**: TflHelper's coverage is fully duplicated by DefenseHelper (2nd/3rd down) and BigEventHelper (4th down). Removing it and folding any remaining edge case into the down-specific evaluators would eliminate Discrepancy #11 completely.

2. **Relax DefenseHelper's down-change guard for Loss branches**: The `Current.Down == Previous.Down` edge guard prevents split-tick loss detection (Discrepancy #12). Removing it for the Loss branches (while keeping `Delta.NewPossession` and `!UserHasPossession` guards) fixes the gap safely.

3. **Gate `penaltyagainst` region**: Add to `EventGatedRegions` to prevent replay-flicker double-fires (Discrepancy #13).

4. **Add migration warnings for retired EventKeys**: When an evaluator stops firing a key that still exists in `ConfigStore.AllEngineEventKeys`, log a plain-English warning for any user who has a song assigned to it (Discrepancy #14).

5. **Build yard-line OCR or remove `YardLine` from PlaySnapshot**: The dead yard-line signal has been the root cause of every evaluator bug this audit found (original #2, #3, #7; TflHelper was dead; DefenseHelper was dead). Every evaluator that depended on it has been manually fixed, but the dead data remains in the shared data model, waiting for the next evaluator to accidentally depend on it.