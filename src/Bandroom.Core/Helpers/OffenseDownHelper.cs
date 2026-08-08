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