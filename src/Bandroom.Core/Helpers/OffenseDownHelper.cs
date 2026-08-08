namespace Bandroom.Core.Helpers;

public sealed class OffenseDownHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.UserHasPossession)
            return null;

        // 1st down is handled by FirstDownHelper (with Big Gain / Midfield variants).
        // This block was a duplicate of FirstDownHelper's base event — removed 2026-08-07.

        // 2nd/3rd down: fire when down changes
        if (state.Current.Down != state.Previous.Down)
        {
            string eventKey = state.Current.Down switch
            {
                2 => "Offense: Second Down",
                3 => "Offense: Third Down",
                // Added 2026-08-08 (STATE_MACHINE_ANALYSIS.md Discrepancy #10): no evaluator
                // produced this at all, so any user who'd assigned a song to the legacy down:4th
                // trigger went silent once the engine took over -- going for it on 4th produced
                // no offensive audio cue.
                4 => "Offense: Fourth Down",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(eventKey))
            {
                return new TriggerEvent
                {
                    EventKey = eventKey,
                    Volume = state.Current.BigGame ? 100 : 70,
                    IsEarnedBigEvent = false
                };
            }
        }

        return null;
    }
}