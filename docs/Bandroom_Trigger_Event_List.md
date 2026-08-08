# Every trigger/event in Bandroom — confirmed vs. not

Two different layers: the **OCR watcher** (what the app actually looks for on screen) and the
**33 assignable sound events** (what you assign songs to, mapped into 6 categories for the UI).

## OCR watcher regions — what's actually being scanned for (GameWatcher.cs)

| Region | What it looks for | Status |
|---|---|---|
| `down` | 1st/2nd/3rd/4th & distance (e.g. "3rd & 7") | ✅ Confirmed, calibrated, live |
| `situation` | KICKOFF, PAT GOOD, TOUCHDOWN, INTERCEPTED, FUMBLE, TURNOVER | ✅ Confirmed, calibrated, live |
| `quarter` | Quarter number (1st/2nd/3rd/4th, not followed by "&") | ✅ Confirmed, calibrated, live |
| `flag` | FLAG / PENALTY banner | ⚠️ **Not calibrated — inert.** No screen coordinates set yet, needs a live screenshot of a real penalty banner to finish |
| `banner` | Big full-screen TOUCHDOWN / FIELD GOAL / SAFETY ribbon | ⚠️ **Not calibrated — inert.** Same story — needs a live screenshot |
| Possession color sample | Which team's color fills the down/distance ribbon | ✅ Confirmed, calibrated, live |
| Tackle-for-loss detection | Negative distance-to-go (e.g. "3rd & -4") | ✅ Confirmed, reuses the "down" crop, live |

**Bottom line: `flag` and `banner` are the two dead weight items.** They exist in code but do
nothing right now — no screen coordinates ever got filled in. Either finish calibrating them
(needs one clean screenshot each, at the moment a flag/big banner appears) or rip them out if
they're not worth chasing.

## The 33 assignable sound events — what you can attach a song to

Every one of these is a real, working trigger — none of these 33 are placeholder/dead. They're
grouped into 6 categories in the UI:

**Downs**
- Offense: Earned First Down
- Offense: Earned First Down (Big Gain)
- Offense: Earned First Down (Midfield)
- Offense: Second Down
- Offense: Second Down (Midfield)
- Offense: Third Down
- Defense: Third Down
- Defense: Third Down (Loss)
- Defense: Fourth Down
- Defense: Fourth Down (Loss)
- Defense: Second Down
- Defense: Second Down (Midfield)
- Defense: Second Down (Loss)
- Defense: Tackle for Loss

**Scoring**
- Offense: Touchdown Scored
- Offense: Field Goal Made
- Offense: 2-Point Conversion Made
- Offense: PAT Made
- Defense: Touchdown Scored
- Defense: Safety

**Turnovers**
- Defense: Turnover Forced
- Defense: Iced Game by Turnover

**Special Teams**
- Defense: Field Goal Missed by Opponent
- Other: Opening Kickoff
- Other: Second-Half Kickoff
- Other: Opening Kickoff on Kick
- Other: Kickoff on Kick (Kicking)
- Other: Kickoff on Kick (Receiving)

**Penalties**
- (Fed by the `flag` region above — inert until that's calibrated, see note above)

**Hype**
- Offense: Drive Starter
- Offense: Iced Game by First Down
- Offense: Victory in Hand
- Defense: Drive Starter
- Other: Pregame Take the Field
- Other: Start of 2nd Quarter
- Other: Start of 4th Quarter

## What "slimming" could actually mean here

- **Cut `flag`/`banner` entirely** if penalty-banner and full-screen-scoring-banner detection
  aren't worth finishing — that removes dead code, not a working feature.
- **The 33 assignable events are all real and working** — nothing to slim there without actually
  losing functionality. If the goal is fewer choices in the UI (not fewer working triggers),
  that's a design/grouping question, not a bug-fix.
