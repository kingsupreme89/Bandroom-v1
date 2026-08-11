namespace Bandroom.Core.Helpers;

/// <summary>Fires pregame take-the-field, quarter-start, iced-game-by-first-down,
/// and victory-in-hand events. These are the "Hype" category game-state transition events
/// that detect quarter changes and late-game clinching scenarios.</summary>
public sealed class GameStateEventHelper : IRuleEvaluator
{
    // Victory in Hand's condition (4th quarter, clock <= 30s, 9+ point lead) is level-triggered,
    // not edge-triggered like everything else this class fires -- with 30 seconds of game clock
    // and a tick roughly every 250ms, that's up to ~120 evaluations where the condition holds, all
    // returning the same TriggerEvent. FireCooldown masks some of the resulting duplicates but not
    // all. Same self-tracked-flag fix as KickoffHelper's _didFire for the same class of bug.
    bool _didFireVictoryInHand;

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

            // NOTE: quarter 3 (halftime -> second half) is deliberately NOT handled here --
            // KickoffHelper already fires "Other: Second-Half Kickoff" for that exact moment,
            // gated on the actual kickoff situation-text edge (more precise than a bare quarter
            // transition) and registered in ConfigStore.AllEngineEventKeys. Adding a second
            // event here would just double-fire on the same real-world moment.

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
        // Excludes turnover-driven down resets (e.g. a defensive INT resets Down to 1) -- those
        // are TurnoverHelper's "Iced Game by Turnover" cue, not an earned offensive conversion.
        if (state.Delta.WasFirstDown
            && !state.Delta.NewPossession
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
        bool victoryInHandNow = false;
        if (state.Current.Quarter >= 4 && state.Current.TimeRemainingSeconds <= 30)
        {
            int homeLead = state.Current.HomeScore - state.Current.AwayScore;
            int awayLead = state.Current.AwayScore - state.Current.HomeScore;
            victoryInHandNow = homeLead >= 9 || awayLead >= 9;
        }

        if (victoryInHandNow)
        {
            if (_didFireVictoryInHand) return null;
            _didFireVictoryInHand = true;
            return new TriggerEvent
            {
                EventKey = "Offense: Victory in Hand",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }
        _didFireVictoryInHand = false;

        return null;
    }
}