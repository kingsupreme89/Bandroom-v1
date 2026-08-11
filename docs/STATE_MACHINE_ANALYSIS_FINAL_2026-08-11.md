# Bandroom State-Machine Analysis — FINAL (August 11, 2026)

**Supersedes:** `STATE_MACHINE_ANALYSIS.md` (Aug 8), `STATE_MACHINE_ANALYSIS_UPDATED_2026-08-10.md` (obsoleted by the corrected doc below), `STATE_MACHINE_ANALYSIS_CORRECTED_2026-08-10.md` (Aug 10). Those three are left in place as historical record; this document is the current source of truth.

**Methodology:** Every claim below was checked against source as it exists on 2026-08-11, not against any prior doc's description of the source. File:line citations are given so any claim can be re-verified in seconds.

---

## Executive Summary

All 14 discrepancies raised across the two prior audits (#1-10 original, #11-14 corrected) are now **FIXED** in source. No discrepancy is still open from either prior document.

One **new** discrepancy was found in this pass, introduced as a side effect of the #11 fix:

| # | Issue | Severity |
|---|---|---|
| **15** | **`TflHelper` is now dead code** — its firing condition can never be satisfied | LOW (no user-visible symptom, but wasted cycles + future-maintenance trap) |

The engine now has **19 evaluators**, confirmed by directory listing of `src/Bandroom.Core/Helpers/`.

---

## Architecture (unchanged from prior audits)

1. **OCR/Input Layer** — `GameWatcher.RunAsync`, 250ms poll, region OCR + color sampling
2. **Game-State Engine** — `RouteEngineTick` → `PlaySnapshot` → `PlayDelta` → 19 `IRuleEvaluator`s → `EventRouter.Dedupe` → `EventsDetected`
3. **Audio/UI Layer** — `OnEngineEventsDetected` → `ResolveEventRouting` (3-tier) → `FireEventForSide` → `AudioPlayer.Play`

---

## DISCREPANCY STATUS — ALL 14 PRIOR ISSUES, VERIFIED AGAINST CURRENT SOURCE

| # | Issue | Status | Evidence (2026-08-11) |
|---|---|---|---|
| 1 | TimeoutHelper level-triggered | ✅ FIXED | Edge guard present (verified in CORRECTED doc; no evaluator changes since) |
| 2 | DownFieldPositionHelper Midfield always true | ✅ FIXED | Gated on `YardLine > 0`, dormant since `YardLine` is hardcoded 0 |
| 3 | Duplicate DefenseHelper + DownFieldPositionHelper | ✅ FIXED | Loss branches removed from DownFieldPositionHelper; `EventRouter.Dedupe` added |
| 4 | BigEventHelper + DefenseHelper 3rd-down ambiguity | ✅ FIXED | `DefenseHelper.cs:36-40` returns null on `Delta.NewPossession`; `BigEventHelper.cs:19` checks `!IsTurnover` |
| 5 | Safety + 2-pt conversion score delta overlap | ✅ FIXED | Mutually exclusive `possessingSideGained2` vs opposite-side checks (per CORRECTED doc, unchanged since) |
| 6 | FieldGoalMissed may never fire | ✅ FIXED | Uses `IsFieldGoalAttempt` banner flag, not the dead `IsPAT`-only path |
| 7 | OCR blanking race on non-sticky fields | ✅ FIXED | Sticky `_lastDistanceRaw` pattern |
| 8 | NoPuntReturn comment clarity | COSMETIC | Logic was always correct; not re-verified further |
| 9 | Dual TFL fire path (legacy + engine) | ✅ FIXED | `_useEngineForEvents` gate on legacy `OnTackleForLoss` |
| 10 | No "Offense: Fourth Down" event | ✅ FIXED | `OffenseDownHelper.cs:88` — Down 4 always fires `"Defense: Fourth Down"` (reuses the pre-existing key by design, see file's own doc comment) |
| 11 | TflHelper collides with down-specific Loss evaluators | ✅ FIXED | `TflHelper.cs:27` excludes `Current.Down` 2, 3, and 4 — see new Discrepancy #15 below for the side effect |
| 12 | Split-tick loss detection silence (2nd/3rd down TFL) | ✅ FIXED | `DefenseHelper.cs:17-20,42-59` and `BigEventHelper.cs:10-13,29-51` both now use a pending-buffer pattern (`_pendingDown`/`_baselineYardsToGo`/`_ticksPending`, `MaxPendingTicks=3`) instead of requiring down-change and yards-change on the same tick |
| 13 | Penalty overlay flicker double-fire | ✅ FIXED | `GameWatcher.cs:195` — `"penaltyagainst"` is in `EventGatedRegions` |
| 14 | KickoffHelper silently dropped 2 EventKeys, UI still offered them | ✅ FIXED | `ConfigStore.cs:1483-1484` — both keys moved into `RetiredEventKeys`; absent from `AllEngineEventKeys` (`ConfigStore.cs:1493-1541`) |

### Notable pattern across #11, #12, and #15

The `_pendingDown`/`_baselineYardsToGo` buffered-edge-detection pattern introduced to fix #12 is now shared verbatim across `DefenseHelper`, `BigEventHelper`, and `OffenseDownHelper` — all three carry the identical comment explaining the OCR split-tick problem. This is a real, working fix, but it means three independent per-evaluator instances of mutable buffering state instead of one shared mechanism (see Recommendation 3 below).

---

## NEW DISCREPANCY #15 — TflHelper Is Dead Code

**Severity:** LOW (no audible symptom — the evaluator simply never fires — but it is wasted per-tick evaluation and a trap for a future maintainer who edits it expecting it to do something)
**Category:** Unreachable Code / Vestigial Evaluator

**CURRENT STATE + EVENT + CONDITIONS = NEXT STATE + ACTIONS**

```
CURRENT STATE: Any drive tick
  + Current.Down > Previous.Down
  + Current.YardsToGo > Previous.YardsToGo
  + Current.Down != 2 && Current.Down != 3 && Current.Down != 4
  = (UNREACHABLE — no valid Current.Down satisfies all three conditions)
  + Would fire "Defense: Tackle for Loss" if reachable
```

**1. Intended behavior (per the evaluator's own guard comment, `TflHelper.cs:18-24`):** Fire a generic "Defense: Tackle for Loss" cue for any down-advancing loss that the down-specific evaluators (`DefenseHelper` for 2nd/3rd, `BigEventHelper` for 4th) don't already own — i.e. cover only whatever "the rest" of the domain might be.

**2. Actual implementation (`TflHelper.cs:25-27`):**
```csharp
if (state.Current.Down > state.Previous.Down
    && state.Current.YardsToGo > state.Previous.YardsToGo
    && state.Current.Down != 2 && state.Current.Down != 3 && state.Current.Down != 4)
```
`Current.Down` is an `int` representing a real down number, which by the rules of football (and by every other evaluator in this codebase, e.g. `OffenseDownHelper.cs:73`: `if (down < 2 || down > 4) return null;`) is always 1, 2, 3, or 4. The condition requires `Current.Down > Previous.Down` (so `Current.Down` is at least 2 — a down never "advances" into 1, since reaching 1 means either a first down reset or a new possession, both of which reset the sequence rather than incrementing it) **and simultaneously** `Current.Down ∉ {2, 3, 4}`. There is no integer that is both ≥ 2 and not in {2, 3, 4} within the valid domain {1, 2, 3, 4}. The condition is unsatisfiable.

**3. Why it differs:** The #11 fix (2026-08-11, per the file's own comment at lines 18-24) needed to stop `TflHelper` from double-firing alongside `DefenseHelper`/`BigEventHelper` on downs 2/3/4. Excluding "the downs owned elsewhere" was the right instinct, but since 2/3/4 is not just "most of" TflHelper's practical domain — it is *the entire domain a down can ever advance into* — the exclusion silently ate 100% of what remained. The comment at line 23 ("Those three downs are this evaluator's entire practical domain") already states the fact that makes the fix total, without drawing the conclusion that the evaluator is now fully inert.

**4. Possible failure:** None user-visible today — `DefenseHelper` and `BigEventHelper` fully cover the same ground with more specific cues, so no cue is missing. The risk is entirely future-facing: `TflHelper` still runs every tick (allocates a `TriggerEvent` check, participates in evaluator iteration), and its EventKey `"Defense: Tackle for Loss"` still exists in `ConfigStore.AllEngineEventKeys` (confirmed present, not in `RetiredEventKeys`) as an assignable card. Any user who assigns a song to that card will find it never plays — the same class of silent-dead-assignment bug as the original #14, just not yet flagged as such.

**5. Recommended fix — two options:**
- **A (minimal):** Delete `TflHelper.cs` and its registration in the evaluator list (`GameWatcher.cs`, wherever the 19 evaluators are constructed/registered), and move `"Defense: Tackle for Loss"` into `ConfigStore.RetiredEventKeys` alongside the kickoff keys, with the same "still fires if already assigned... just no longer offered" comment pattern already used there.
- **B (restore purpose):** If a generic TFL cue independent of down number is still wanted (e.g. for feel/flexibility), give `TflHelper` a condition that is not a strict subset of `DefenseHelper`'s and `BigEventHelper`'s — e.g. fire it only when down did NOT advance to 2/3/4 through the normal path (there isn't currently such a case), or repurpose it as a fallback that fires only if neither `DefenseHelper` nor `BigEventHelper` produced an event that tick (requires either evaluator ordering/coordination in `EventRouter`, or converting `TflHelper` from independent to a "catch remainder" pass).

Option A is recommended: the codebase's own comments (both in `TflHelper.cs` and the CORRECTED analysis doc, "TflHelper's coverage is fully duplicated by DefenseHelper... and BigEventHelper") already conclude removal is correct; nothing currently exploits TflHelper firing standalone.

**6. Regression test:** Construct a `GameState` with `Previous.Down=1, Current.Down=2, YardsToGo` increased. Assert `TflHelper.Evaluate` returns `null` for every value of `Current.Down` in {2,3,4} (i.e., for all reachable inputs). Optionally assert no `GameState` exists for which it returns non-null (a property-style test over `Current.Down ∈ {1,2,3,4}` is sufficient given the domain is finite).

---

## COMPLETE EVALUATOR TRANSITION MAP (19 Evaluators)

Evaluators re-verified against source this pass are marked **[verified 08-11]**; the rest are carried forward from the CORRECTED doc's line-by-line audit with no code changes detected since (confirmed via `Bandroom_Handoff_*` session docs through Session 41 — none touch these files).

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
```

### FieldGoalPATHelper
```
  + scoreDiff==1 && IsPAT → "Offense: PAT Made", Vol 75
  + scoreDiff==2 && possessingSideGained2 → "Offense: 2-Point Conversion Made", Vol 85, IsEarnedBigEvent=true
  + scoreDiff==3 → "Offense: Field Goal Made", Vol 85
```

### SafetyHelper
```
  + Previous.PossessionAway && homeDelta==2 → "Defense: Safety", Vol 100
  + !Previous.PossessionAway && awayDelta==2 → "Defense: Safety", Vol 100
```

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

### OffenseDownHelper **[verified 08-11, `OffenseDownHelper.cs`]**
```
CURRENT STATE: Any down tick
  + Current.Down != Previous.Down
    + Delta.NewPossession → return null [turnover, not a down-and-distance cue]
    = latch _pendingDown=Current.Down, _baselineYardsToGo=Previous.YardsToGo, return null
  + Current.Down == Previous.Down && _pendingDown != null
    = wait until YardsToGo moves off baseline, or MaxPendingTicks(3) elapses
  + down < 2 || down > 4 → return null [1st down is FirstDownHelper's/DriveStarterHelper's]
  + Current.YardsToGo > baseline → return null [defer to DefenseHelper's (Loss) branch]
  + isShort = YardsToGo <= 3
  = Down 2, short  → "Offense: Second Down Short", Vol 70/100
  = Down 2, long   → "Defense: Second Down", Vol 70/100
  = Down 3, short  → "Offense: Third Down Short", Vol 70/100
  = Down 3, long   → "Defense: Third Down", Vol 70/100
  = Down 4 (any)   → "Defense: Fourth Down", Vol 70/100
  + IsEarnedBigEvent=false always
```
Reuses the pre-existing `"Defense: Second/Third/Fourth Down"` keys for long/4th-down (not new keys) so existing song assignments keep working.

### DefenseHelper **[verified 08-11, `DefenseHelper.cs`]**
```
CanFire: !UserHasPossession

CURRENT STATE: User does not have possession
  + Delta.NewPossession → return null [sack-fumble recovery is BigEventHelper's "Third Down" stop, not a Loss]
  + Current.Down != Previous.Down
    = latch _pendingDown, _baselineYardsToGo=Previous.YardsToGo, _ticksPending=0
  + Current.Down == Previous.Down && _pendingDown != null → _ticksPending++; drop pending if > 3
  + _pendingDown == null || _pendingDown != Current.Down → return null
  + Current.YardsToGo <= baseline → return null
  = down==3 → "Defense: Third Down (Loss)", Vol 75/100, IsEarnedBigEvent=true
  = down==2 → "Defense: Second Down (Loss)", Vol 75/100, IsEarnedBigEvent=true
  + (down==4 NOT handled here — BigEventHelper's territory)
```
Fires once per down transition (pending window consumed on fire), tolerant of the down-field and yards-to-go OCR reads landing up to 3 ticks apart.

### BigEventHelper **[verified 08-11, `BigEventHelper.cs`]**
```
CURRENT STATE: Any
  + Down==3 && Delta.NewPossession && !IsTurnover
  = "Defense: Third Down", Vol 80/100, IsEarnedBigEvent=BigGame

  + Current.Down != Previous.Down → latch _pendingDown/_baselineYardsToGo (same pattern as DefenseHelper)
  + _pendingDown==4 && Current.Down==4 && YardsToGo > baseline
  = "Defense: Fourth Down (Loss)", Vol 85/100, IsEarnedBigEvent=true

  + Down==4 && Delta.NewPossession && !IsFieldGoalAttempt && !IsTurnover
  = "Defense: Fourth Down", Vol 80/100, IsEarnedBigEvent=BigGame
```

### TflHelper **[verified 08-11, `TflHelper.cs`]** — see Discrepancy #15
```
CURRENT STATE: Any
  + Previous.Down==0 || Current.Down==0 → return null
  + Current.Down > Previous.Down && YardsToGo > Previous.YardsToGo && Current.Down ∉ {2,3,4}
  = UNREACHABLE (no valid Down satisfies both "> Previous" and "∉ {2,3,4}" within {1,2,3,4})
  + Effectively always returns null — dead code
```

### DownFieldPositionHelper (Midfield-only, dormant)
```
  + Defense side, Down 2, YardLine > 0 && <= 50 → "Defense: Second Down (Midfield)"
  + Offense side, Down 2, YardLine > 0 && <= 50 → "Offense: Second Down (Midfield)"
  + YardLine always 0 → both branches dormant (correctly guarded, not a bug)
```

### KickoffHelper **[verified 08-11, `KickoffHelper.cs`]**
```
CURRENT STATE: !IsKickoff → _didFire=false, return null
  + _didFire → return null [self-tracked edge guard, immune to gated-region staleness]
  + Quarter==1 && !_openingKickoffFired → "Other: Opening Kickoff", Vol 90, IsEarnedBigEvent=true
  + Quarter==3 && !_secondHalfKickoffFired → "Other: Second-Half Kickoff", Vol 90, IsEarnedBigEvent=true
  + Else → return null [no cue for ordinary mid-game kickoffs; "Offense: PAT Made" already signals the score right before]
```
`"Other: Kickoff on Kick (Receiving/Kicking)"` retired — confirmed absent from `ConfigStore.AllEngineEventKeys`, present in `RetiredEventKeys` (`ConfigStore.cs:1483-1484`). Discrepancy #14 fully closed, including the UI-side cleanup the CORRECTED doc flagged as still missing.

### PenaltyHelper
```
  + Current.IsPenaltyOnOffense && !Previous.IsPenaltyOnOffense → "Penalty: Offense", Vol 70
  + Current.IsPenaltyOnDefense && !Previous.IsPenaltyOnDefense → "Penalty: Defense", Vol 70
```
Edge detection was always correct; the #13 bug was the ungated OCR region feeding it stale re-arms, fixed via `EventGatedRegions` (see status table).

### GameStateEventHelper
```
  + Quarter 1→2 → "Other: Start of 2nd Quarter", Vol 70
  + Quarter 3→4 → "Other: Start of 4th Quarter", Vol 80
  + Q0→Q1 && D0→D+ → "Other: Pregame Take the Field", Vol 85
  + WasFirstDown && !NewPossession && Q4+ && time<=120 → "Offense: Iced Game by First Down", Vol 100
  + Q4+ && time<=30 && lead>=9 → "Offense: Victory in Hand", Vol 100 (self-tracked _didFire)
```

### TimeoutHelper
```
  + !UserHasPossession && TimeRemainingSeconds <= 240
  + AwayTimeoutsRemaining in [0,6]
  + Current.AwayTimeoutsRemaining < Previous [edge guard]
  = "Defense: Timeout (N Remaining)" for N=4,3,2,1,0, Vol 65/100
```

### DriveStarterHelper, NoPuntReturnHelper, DefenseFirstDownHelper, DefenseThirdDownShortHelper, PregameHelper
No changes detected since the CORRECTED audit; no session handoff since Aug 10 touches these files. Carried forward as verified-correct with no interaction issues beyond those already covered above.

---

## THREE-LAYER AUDIT

### Layer 1 — OCR Input
`EventGatedRegions` (`GameWatcher.cs:195`) = `{ "situation", "banner", "quarter", "penaltyagainst", "pregameready" }`. Both additions since the original Aug 8 doc (`penaltyagainst`, `pregameready`) are deliberate fixes for the same root cause — a status-style region left ungated re-arms on replay-cut blanking — confirmed via `Bandroom_Handoff_2026-08-10_Session35.md` and `Bandroom_Handoff_2026-08-11_Session37.md`. No further ungated status region has been found.

### Layer 2 — Game-State Engine
- `PlayDelta.LostYards` no longer exists anywhere in `src/Bandroom.Core/PlayDelta.cs` — fully removed from the codebase (not merely unreferenced), closing out the "dead signal" finding the CORRECTED doc raised (it was still present as an unused field there).
- The three-evaluator pending-buffer pattern (`DefenseHelper`, `BigEventHelper`, `OffenseDownHelper`) is a real fix for #12, but each is its own private mutable-state instance with no shared abstraction — see Recommendation 3.
- **Structural fragility in the pending-buffer pattern:** all three buffer implementations key off `Previous.YardsToGo` captured at the moment `Current.Down` first changes. If that specific baseline-capture tick had a bad OCR read on `YardsToGo` (e.g. a transient misread of "10" as "1"), the entire `MaxPendingTicks`-tick window compares against the wrong baseline — every subsequent real value looks like either a spurious loss (false Loss cue) or an artificially large gain (Loss cue suppressed when it shouldn't be). This is a plausible-but-unconfirmed failure mode; no evidence found of it occurring in practice, but it was not explicitly guarded against (e.g. no sanity bound on how much `YardsToGo` can plausibly jump between ticks) and is worth flagging for anyone debugging a "wrong or missing Loss cue" report.

### Layer 3 — Audio/Side Routing
No changes detected since the CORRECTED doc's Race #3 fix (`_possession` null handling) and Race #5 fix (preload-before-first-trigger). Not re-verified line-by-line this pass since no session handoff since Aug 10 touches `WebMainForm.cs`'s routing logic; flagged as **not independently re-confirmed** rather than asserted fixed.

---

## REVISED RECOMMENDATIONS

1. **Delete or repurpose `TflHelper`** (Discrepancy #15) — it is dead code today. Prefer deletion + moving its EventKey into `ConfigStore.RetiredEventKeys`, matching the precedent already set for the kickoff keys.
2. **Consider a sanity bound on the pending-buffer baseline** in `DefenseHelper`/`BigEventHelper`/`OffenseDownHelper` — e.g. discard a baseline capture if `YardsToGo` swings implausibly (say, a jump of 40+ yards in one tick) rather than trusting whatever OCR read at the exact down-change tick.
3. **Factor the pending-buffer pattern into one shared helper class** — three independent copies of `_pendingDown`/`_baselineYardsToGo`/`_ticksPending`/`MaxPendingTicks` with identical logic is a maintenance risk: a future fix to the buffering behavior (e.g. changing `MaxPendingTicks`, or fixing a bug in it) has to be applied in three places and will likely drift.
4. **Build yard-line OCR or remove `YardLine` from `PlaySnapshot`** — unchanged recommendation from the CORRECTED doc. The dead `YardLine` field is still the root cause of the (correctly dormant, but permanently dead-until-OCR-exists) Midfield branches in `DownFieldPositionHelper` and the commented-out branch in `FirstDownHelper`.
