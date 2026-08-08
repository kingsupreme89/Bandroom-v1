# Bandroom EventKey → TriggerEntry.Event Mapping
## Complete concordance for the UI's audio assignment system

Each evaluator returns a `TriggerEvent.EventKey` string. 
`FireEventForSide` matches this against `TriggerEntry.Event` to find the assigned audio file.

| Evaluator | EventKey | Category | Side |
|-----------|----------|----------|------|
| OffenseDownHelper | `Offense: Earned First Down` | Downs | Offense |
| OffenseDownHelper | `Offense: Second Down` | Downs | Offense |
| OffenseDownHelper | `Offense: Third Down` | Downs | Offense |
| DefenseHelper | `Defense: Second Down` | Downs | Defense |
| DefenseHelper | `Defense: Third Down (Loss)` | Downs | Defense |
| BigEventHelper | `Defense: Third Down` | Downs | Defense |
| BigEventHelper | `Defense: Fourth Down` | Downs | Defense |
| DownFieldPositionHelper | `Offense: Second Down (Midfield)` | Downs | Offense |
| DownFieldPositionHelper | `Defense: Second Down (Loss)` | Downs | Defense |
| DownFieldPositionHelper | `Defense: Second Down (Midfield)` | Downs | Defense |
| DownFieldPositionHelper | `Defense: Third Down (Loss)` | Downs | Defense |
| DownFieldPositionHelper | `Defense: Fourth Down (Loss)` | Downs | Defense |
| FirstDownHelper | `Offense: Earned First Down` | Downs | Offense |
| FirstDownHelper | `Offense: Earned First Down (Big Gain)` | Downs | Offense |
| FirstDownHelper | `Offense: Earned First Down (Midfield)` | Downs | Offense |
| FirstDownHelper | `Offense: Earned First Down` | Downs | Offense |
| TflHelper | `Defense: Tackle for Loss` | Downs | Defense |
| TouchdownHelper | `Offense: Touchdown Scored` | Scoring | Offense |
| TouchdownHelper | `Defense: Touchdown Scored` | Scoring | Defense |
| FieldGoalPATHelper | `Offense: Field Goal Made` | Scoring | Offense |
| FieldGoalPATHelper | `Offense: PAT Made` | Scoring | Offense |
| FieldGoalPATHelper | `Offense: 2-Point Conversion Made` | Scoring | Offense |
| FieldGoalMissedHelper | `Defense: Field Goal Missed by Opponent` | Scoring | Defense |
| SafetyHelper | `Defense: Safety` | Scoring | Defense |
| TurnoverHelper | `Defense: Turnover Forced` | Turnovers | Defense |
| TurnoverHelper | `Defense: Iced Game by Turnover` | Turnovers | Defense |
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

**Total: 42 EventKeys across 16 evaluators**

## Side Routing Logic
```
EventKey.StartsWith("Defense:") → fire for side OPPOSITE possession
Everything else                 → fire for possession side
HomeOnlyEventsForNow = true     → gate to home side only