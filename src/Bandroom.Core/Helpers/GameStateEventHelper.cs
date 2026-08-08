namespace Bandroom.Core.Helpers;

/// <summary>Fires pregame take-the-field, quarter-start, iced-game-by-first-down,
/// and victory-in-hand events. These are the "Hype" category game-state transition events
/// that detect quarter changes and late-game clinching scenarios.</summary>
public sealed class GameStateEventHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // --- Quarter transitions ---
        if (state.Previous.Quarter != state.Current.Quarter && state.Previous.Quarter > 0)
        {
            if (state.Current.Quarter == 2)
            {
                return new TriggerEvent
                {
                    EventKey = "Other: Start of 2nd Quarter",
                    Volume = 70,
                    IsEarnedBigEvent = false
                };
            }

            if (state.Current.Quarter == 4)
            {
                return new TriggerEvent
                {
                    EventKey = "Other: Start of 4th Quarter",
                    Volume = 80,
                    IsEarnedBigEvent = true
                };
            }
        }

        // --- Pregame Take the Field: first detection, quarter 1, down unknown (0) ---
        if (state.Previous.Quarter == 0 && state.Current.Quarter == 1
            && state.Previous.Down == 0 && state.Current.Down > 0)
        {
            return new TriggerEvent
            {
                EventKey = "Other: Pregame Take the Field",
                Volume = 85,
                IsEarnedBigEvent = true
            };
        }

        // --- Iced Game by First Down: 4th quarter, under 2 min, offense earned a 1st down ---
        if (state.Delta.WasFirstDown
            && state.Current.Quarter >= 4
            && state.Current.TimeRemainingSeconds <= 120)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Iced Game by First Down",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }

        // --- Victory in Hand: 4th quarter under 30 seconds, leading by 9+ ---
        if (state.Current.Quarter >= 4 && state.Current.TimeRemainingSeconds <= 30)
        {
            int homeLead = state.Current.HomeScore - state.Current.AwayScore;
            int awayLead = state.Current.AwayScore - state.Current.HomeScore;

            if (homeLead >= 9 || awayLead >= 9)
            {
                return new TriggerEvent
                {
                    EventKey = "Offense: Victory in Hand",
                    Volume = 100,
                    IsEarnedBigEvent = true
                };
            }
        }

        return null;
    }
}