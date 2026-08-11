# Bandroom Punch List — Session 41 (2026-08-11)

Raw owner brain-dump from this session, organized by area. Not yet triaged, not yet reproduced/fixed.
Capturing verbatim intent before starting work.

## Navigation / flow
- "Away offense needs 1st down event" — away-team offense is missing (or not firing?) a 1st down
  event. Needs clarification: missing assignment vs. not firing vs. missing from event list.
- **Locking in** (assignment lock?) should navigate to the Band Room view with the Assignments and
  Sound Booth modal embedded together, not separate.
- Band Room view should also switch/change to "all teams" if needed.
- The team-switch arrows are "barely visible" — need a pulsing, **team-themed glow** effect (not
  just a static "selected" state) so they're visible regardless of which team's colors are active.

## Down & distance event firing (bugs — real game observed behavior)
- **2nd and long — didn't fire.**
- **3rd and long — fired** (correct).
- **4th and long — fired twice** (double-fire bug).
- **2nd and short on offense — triggered Safety** (wrong event fired entirely).
- **3rd and short — fired twice**, and **then also fired Safety.**
- **4th and 3 on offense — triggered** [cut off / unclear what it triggered — needs clarification].

This pattern (2nd/3rd/4th & long/short double-firing, wrong-event misfires, Safety false-triggers)
looks related to the `EventGatedRegions` / down-distance parsing logic touched in Session35
(`PenaltyHelper`/`TurnoverHelper` double-fire fixes) — likely a similar OCR-flicker or region-gating
gap specific to down+distance combos. Needs its own audit pass, not yet investigated this session.

## Sound Booth / Clipper
- Clipper: after trimming and saving a clip, it goes back to [the clip picker / previous screen —
  unclear which] instead of [expected destination — unclear].
- Editing track info should save the **actual track name** across platform if there isn't already a
  local one (i.e., don't leave it blank/generic when a real title is available).
- The universal lead-in whistle should **not** play in the song list preview — only when playing
  from event cards.

## Event card UX
- Event cards need a button to **copy/load an assignment from another already-assigned event** on
  the same team — e.g., a already-assigned "2nd and short" should be suggested as a source when
  setting up any other "2nd down" event.
- Add a **configurable delay on start of sound** (a few seconds before playback begins after trigger).

## Reference: play clock state semantics (for whoever debugs the down/distance firing bugs above)
Owner's explanation of how to read the play clock box to determine play state:
- Play clock resets to a **blank box** → the play has just started (in progress).
- Play clock box **populates back to "40"** → the play has ended.
- If it's still **"1st & 10"** while the box is blank → that was a **first down** result (i.e., no
  down increment happened, treat as a fresh set of downs, not a "2nd and X" continuation).

This is likely the missing piece for correctly gating the down-distance event logic above — parsing
should probably key off blank→40 transitions rather than (or in addition to) down/distance OCR text
alone.

## Status

**Fixed (build-clean, relaunched, NOT yet live-verified against a real game):**
- Down/distance misfire cluster (2nd-long-no-fire, 4th/3rd-long-and-short double-fires, wrong
  short/long classification). Root cause: `OffenseDownHelper` (`src/Bandroom.Core/Helpers/
  OffenseDownHelper.cs`) compared `YardsToGo` on the same tick the down changed, but down and
  yards-to-go are independent OCR reads that don't always land on the same tick — its neighbor
  `DefenseHelper` already had a buffered wait for exactly this reason, `OffenseDownHelper` never
  did. Gave it the same buffered-pending pattern (waits up to 3 ticks / ~750ms for YardsToGo to
  move off its pre-transition baseline before classifying short vs. long, falls back to whatever's
  read on timeout so it still fires something rather than going silent).
- Safety false-trigger (`2nd and short on offense triggered Safety`, `3rd and short ... then fired
  Safety`). Root cause: `GameWatcher.cs`'s `_lastKnownAwayScore`/`_lastKnownHomeScore` committed on
  a single OCR frame with zero debounce (every OTHER region has a `Cooldown`/sticky-value guard;
  scores didn't). A single misread digit for one 250ms tick could produce a phantom +2 delta and
  fire `SafetyHelper`. Added `CommitScoreIfConfirmed` — now requires the same score value on two
  consecutive ticks before committing, matching the debounce pattern already used elsewhere.
- **owner's play-clock semantics note (blank box = play in progress, box back to "40" = play over,
  still "1st & 10" while blank = that was a first down)** was NOT yet incorporated into the OCR
  parsing — flagging as still open in case blank/40 clock-state tracking would sharpen this further
  (e.g. as an additional confirmation signal alongside the down/distance buffering above). Worth a
  follow-up pass if the buffered fix above doesn't fully resolve the misfires live.

**Still open / not started:**
- Away offense missing 1st down event (needs clarification: missing assignment vs. not firing vs.
  missing from event list)
- Locking-in → Band Room view with Assignments + Sound Booth modal embedded
- Band Room view "switch to all teams" behavior
- Pulsing team-themed glow on team-switch arrows
- Clipper post-trim-save destination
- Track info editing — save actual cross-platform track name
- Universal lead-in whistle playing in song list preview (should be event-cards-only)
- Copy-assignment-from-another-event button on event cards
- Configurable start-of-sound delay

**Next step**: get this in front of a real/recorded game to confirm the down/distance and Safety
fixes actually hold, then move to the quick UI fixes (arrow glow, whistle-in-preview, delay
setting) or the ones needing clarification.

## Full 4-way parallel audit (same session, after owner request to "dig 50 levels deep")

Found that `docs/STATE_MACHINE_ANALYSIS_FINAL_2026-08-11.md` (dated today, claims "all
discrepancies fixed") is **not reliable** — it asserted `OffenseDownHelper` already had the
buffered-tick fix above, which did not exist until this session. Its other claims were
independently re-verified rather than trusted; most held up, but the audit still found 4 more real
bugs and 2 items needing an owner decision.

**Additional fixes made this session (build-clean, relaunched):**
- `DefenseThirdDownShortHelper.cs` had the exact same stale-tick bug `OffenseDownHelper` just got
  fixed for — and worse, it's explicitly designed to fire in lockstep with `OffenseDownHelper` on
  the same tick (same-tick pairing is load-bearing per its own header comment). Fixing one without
  the other would have desynced them. Gave it the identical buffered-pending pattern.
- The score-debounce fix (`CommitScoreIfConfirmed`, now renamed `CommitValueIfConfirmed`) could
  silently drop a real fast second score (e.g. touchdown immediately followed by a 2-point
  conversion) — the unconfirmed reading was being discarded instead of committed once. Fixed: the
  outgoing pending value now commits once before starting a new confirmation cycle.
- Quarter had zero debounce (same single-bad-frame risk as the Safety bug, just not yet reported
  live) — given the same two-tick confirmation.
- The touchdown-celebration possession-color guard checked `flag`/`situation` region activity but
  not `banner` (the full-screen TOUCHDOWN/FIELD GOAL/SAFETY ribbon) — added.
- Cleaned up a stale `ConfigStore.cs` comment claiming `OffenseDownHelper` emits `"Offense: Fourth
  Down"` (it never has — 4th down is always keyed `"Defense: Fourth Down"` by design). Harmless,
  just misleading.

**Owner decisions made and implemented (build-clean, relaunched):**
1. `TflHelper` — owner chose "fix it to actually fire," as a generic cue alongside (not
   instead of) the more specific down-by-down Loss cues. Rewrote it to fire on any 2nd/3rd/4th
   down advance with increased yardage, with the same OCR-race buffering as its siblings. Both
   "Defense: Tackle for Loss" and e.g. "Defense: Third Down (Loss)" can now legitimately fire on
   the same snap — this is intentional layering, not a bug (WebMainForm's per-tick audio layering
   already supports this, same pattern as DefenseThirdDownShortHelper/OffenseDownHelper firing
   together).
2. `"Offense/Defense: Drive Starter"` — confirmed with owner it fires on any fresh drive that
   isn't a kickoff or turnover (in practice, almost always the first snap after a punt), and
   confirmed there's no existing "1st down after kickoff" card duplicating it. Restored both keys
   to `AllEngineEventKeys` so they're assignable again.

**Also surfaced, not yet independently confirmed against a real broadcast:**
- A prior handoff (Session27) flagged a pick-six/fumble-return touchdown routing to the wrong team.
  Current `TouchdownHelper.cs` code reads correct — either it was already fixed elsewhere and the
  old handoff doc just never got updated, or it never actually reproduced. Worth a live check on a
  defensive/return touchdown specifically.
- No sanity bound exists on the buffered-pending baseline (`OffenseDownHelper`/`DefenseHelper`/
  `BigEventHelper`/`DefenseThirdDownShortHelper` all trust whatever `YardsToGo` reads on the exact
  down-change tick as ground truth for their ~750ms wait window) — a bad OCR read at that specific
  instant could poison the whole window. Not fixed; flagged as a real but unconfirmed risk.
