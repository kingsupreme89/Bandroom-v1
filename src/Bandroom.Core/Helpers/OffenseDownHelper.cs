namespace Bandroom.Core.Helpers;

public sealed class OffenseDownHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.UserHasPossession)
            return null;

        // 1st down: only fire if it's a fresh EARNED first down (not opening snap)
        if (state.Current.Down == 1 && state.Delta.WasFirstDown)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Earned First Down",
                Volume = state.Current.BigGame ? 100 : 70,
                IsEarnedBigEvent = false
            };
        }

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
