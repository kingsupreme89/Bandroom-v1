# Trigger Audit Checklist (2026-08-11)

All 45 events currently in `ConfigStore.AllEngineEventKeys`, in plain English. Checkbox = ready
to confirm/assign a song; leave unchecked if something reads wrong and needs discussion.

Routing notes that apply broadly (not repeated per-row):
- **Defense:*** events auto-flip to the side OPPOSITE whoever has the ball (so "Defense: Third
  Down" plays for whichever team is trying to stop the offense).
- **Big Game gating**: ordinary `Defense:*` cues play at full volume for home always; for away
  they only play at 25% unless it's a marked "earned/big" moment, and only play at all if Big
  Game mode is on (unless earned). `Offense:*` cues always play full volume for whoever's driving.
- **Home-only-always** (2 events, marked below): never plays for the away team, Big Game or not.

## Offense

- [x] **Offense: 1st Down After Punt** *(renamed from "Offense: Drive Starter")* — a brand new
  possession starts that ISN'T a kickoff and ISN'T off a turnover (in practice: almost always
  your first snap after a punt). Old key kept as a fallback so any existing song assignment
  still plays (`WebMainForm.RenamedEventKeyAliases`).
- [x] **Offense: Second Down Short** — it's 2nd down with 5 yards or less to go. *(corrected
  2026-08-11: was 3, now 5 — matches the Earned First Down Short threshold.)*
- [x] **Offense: Third Down Short** — it's 3rd down with 5 yards or less to go. *(same
  correction.)*
- [ ] **Offense: Earned First Down Short** — you just converted a new 1st down (from a down that
  ISN'T 1st — i.e. you were on 2nd/3rd/4th and picked up the conversion), and the fresh set
  starts with 5 yards or less to go (usually near the goal line).
- [ ] **Offense: Earned First Down** — you just converted a new 1st down (from a down that ISN'T
  1st), the normal case with more than 5 yards to go on the fresh set.
- [x] **Offense: 3rd Down Conversion** *(new)* — you converted specifically FROM 3rd down into a
  fresh 1st down. Fires alongside the two events above on the same snap (separate, more specific
  cue for a clutch 3rd-down conversion — doesn't replace them).
- [ ] **Offense: PAT Made** — extra point (1 point) good.
- [ ] **Offense: 2-Point Conversion Made** — 2-point try succeeds (must be YOUR score going up by
  2, not a safety against you — those are told apart).
- [ ] **Offense: Field Goal Made** — field goal (3 points) good.
- [ ] **Offense: Touchdown Scored** — you scored a touchdown.
- [ ] **Offense: Iced Game by First Down** — 4th quarter, under 2:00 left, you just converted a
  1st down (running out the clock).
- [ ] **Offense: Victory in Hand** — 4th quarter, under 30 seconds left, you're up by 9+ points.
  Fires once per game.

## Defense

- [x] **Defense: After Punt** *(renamed from "Defense: Drive Starter", now home-only-always)* —
  the opponent just started a fresh possession (not a kickoff, not off a turnover). Never plays
  for away, Big Game or not.
- [x] **Defense: After Opening Kick** *(renamed from "Defense: First Down")* — the very first
  snap of the game right after a kickoff return (the receiving team's fresh 1st & 10).
  **Home-only-always.**
- [x] **Defense: Third Down Short** — opponent facing 3rd down with 5 yards or less to go (fires
  alongside "Offense: Third Down Short" on the opponent's side, same snap). *(corrected 2026-08-11:
  was 3, now 5.)*
- [ ] **Defense: Third Down** — opponent facing 3rd down with MORE than 3 yards to go (3rd &
  long). **Home-only-always.**
- [x] **Defense: Third Down** *(corrected 2026-08-11)* — offense facing 3rd down, ANY distance
  (was long-only; now fires on every 3rd down via a new dedicated evaluator,
  `DefenseThirdDownHelper`, alongside "Offense: Third Down Short"/"Defense: Third Down Short" on
  short ones). **Home-only-always.**
- [ ] **Defense: Fourth Down** — opponent facing 4th down, any distance (always Defense regardless
  of distance — it's a pressure/decision down).
- [x] **Defense: Third Down (Loss)** *(retired 2026-08-11)* — merged into the generic "Defense:
  Tackle for Loss" cue below, which already fires on this exact same snap. No longer a separate
  assignable card.
- [x] **Defense: Second Down** *(confirmed 2026-08-11)* — opponent facing 2nd down with MORE than
  5 yards to go. Always plays for home defense; for away defense only plays during a Big Game
  (full volume then), otherwise silent for away outside a Big Game. Verified this already matches
  `OffenseDownHelper`'s `IsEarnedBigEvent = false` + `ResolveEventRouting`'s ordinary-Defense tier
  — no code change needed, just confirming intent.
- [ ] **Defense: Second Down (Loss)** — opponent got stuffed for a loss on 2nd down.
- [x] **Defense: Fourth Down (Loss)** *(retired 2026-08-11)* — merged into the generic "Defense:
  Tackle for Loss" cue plus the plain "Defense: Fourth Down" stop cue, both of which already fire
  on this exact same snap. No longer a separate assignable card (`BigEventHelper`'s buffered
  down==4 Loss branch removed entirely).
- [x] **Defense: Field Goal Missed by Opponent** *(confirmed 2026-08-11)* — opponent's field goal
  try misses (possession flips to you, no points scored). Walked through `FieldGoalMissedHelper.cs`
  with the owner: detected by elimination, not a direct "missed" OCR signal — the banner region's
  "FIELD GOAL" text appears for BOTH makes and misses, so this fires when that text is up AND
  possession just flipped AND neither team's score changed that tick (a make is claimed separately
  by `FieldGoalPATHelper`'s `scoreDiff==3` case). No code change, confirming existing logic.
- [ ] **Defense: Turnover Forced** — you just forced a fumble or interception.
- [x] **Defense: Iced Game by Turnover** *(corrected 2026-08-11)* — 4th quarter, under 2:00 left,
  possession flips to WHICHEVER SIDE IS ACTUALLY WINNING right now (owner correction: old logic
  fired on any real turnover in the window regardless of who was ahead, so a still-trailing team
  intercepting a pass got the same "game sealed" cue as a genuine game-ending takeaway). Now fires
  on the broader condition of "the trailing team's possession flips to the leading team" — a real
  turnover (INT/fumble) OR a punt/turnover-on-downs by the trailing team both qualify, as long as
  the new possessor is ahead on the scoreboard (`TurnoverHelper.cs`).
- [ ] **Defense: Safety** — you tackled the opponent in their own end zone (2 points for you).
- [ ] **Defense: Tackle for Loss** — generic "opponent lost yards" cue, fires ALONGSIDE the more
  specific Loss cues above (not instead of) any time a down advances with yards-to-go increasing.
- [x] **Defense: Touchdown Scored** *(corrected 2026-08-11)* — you scored on defense (pick-six /
  fumble return TD). Owner flagged: the "TOUCHDOWN" banner OCR doesn't stay on screen long enough
  for a defensive score specifically (goes straight into the ensuing kickoff, unlike an offensive
  TD's longer PAT/2pt follow-up that gives OCR more ticks to catch it), so the old banner-only
  detection could miss it. Now detected purely from the scoreboard instead (same technique
  `SafetyHelper`/`FieldGoalPATHelper` already use): whichever side's score jumps by exactly 6 while
  that side did NOT have the ball the previous tick is a defensive score, full stop — no banner
  needed. A dedupe guard stops a late-arriving banner from double-firing the offense cue for the
  same points (`TouchdownHelper.cs`).
- [x] **Defense: No Punt Return** *(retired 2026-08-11)* — removed entirely, no replacement cue
  requested. `NoPuntReturnHelper.cs` deleted, unregistered from all three evaluator lists (Windows/
  Mac/Avalonia), key moved to `RetiredEventKeys`.
- [ ] **Defense: Timeout (4 Remaining)** through **Defense: Timeout (0 Remaining)** (5 separate
  events) — opponent just called a timeout, fires once per timeout showing exactly how many
  they have left after using it. Only fires under 4:00 game clock.

## Penalty

- [ ] **Penalty: Offense** — a flag is thrown on the offense (fires for the defense's side).
- [ ] **Penalty: Defense** — a flag is thrown on the defense (fires for the offense's side).
  *(Doesn't yet know which specific penalty — just "a flag happened.")*

## Other / Situations

- [ ] **Other: Pregame Ready** — the pregame team-intro "READY" screen appears (before kickoff,
  fires once per game).
- [ ] **Other: Pregame Take the Field** — the very first snap of the game becomes readable
  (backup signal to Pregame Ready, in case that screen gets missed).
- [ ] **Other: Opening Kickoff** — the game's very first kickoff.
- [ ] **Other: Second-Half Kickoff** — the kickoff that starts the 3rd quarter.
- [ ] **Other: Start of 2nd Quarter** — clock rolls into Q2.
- [ ] **Other: Start of 4th Quarter** — clock rolls into Q4.

---

## Known gaps / things NOT wired yet (worth deciding on before calling this final)

- **No per-penalty-type detection** — "Penalty: Offense/Defense" is just "a flag happened,"
  not holding/false start/PI/etc individually.
- **No live-ball big-play/explosive-play cue** — nothing fires on a long run/pass by yardage
  (YardLine OCR was never built, so anything yardage-based is disabled — see the commented-out
  "Midfield"/"Big Gain" branches in the code).
- **Missed/blocked PAT** has no dedicated cue (only made PAT and missed/made FG are covered).
- **Fumble recovered by own team** (no turnover) has no cue — only a fumble that changes
  possession counts as "Defense: Turnover Forced."
- **Overtime** isn't specifically handled — quarter-based logic (Iced Game, Victory in Hand,
  quarter-start cues) assumes a normal 4-quarter clock.
- **Mid-game injuries/reviews/challenges** have no cues.
