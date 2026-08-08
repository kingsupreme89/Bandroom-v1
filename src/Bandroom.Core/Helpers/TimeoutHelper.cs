namespace Bandroom.Core.Helpers;

public sealed class TimeoutHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // Fire for the team that's on defense (doesn't have possession)
        if (state.UserHasPossession || state.Current.TimeRemainingSeconds > 240)
            return null;

        if (state.Current.AwayTimeoutsRemaining < 0 || state.Current.AwayTimeoutsRemaining > 6)
            return null;

        int remaining = state.Current.AwayTimeoutsRemaining;

        return new TriggerEvent
        {
            EventKey = remaining switch
            {
                4 => "Defense: Timeout (4 Remaining)",
                3 => "Defense: Timeout (3 Remaining)",
                2 => "Defense: Timeout (2 Remaining)",
                1 => "Defense: Timeout (1 Remaining)",
                0 => "Defense: Timeout (0 Remaining)",
                _ => string.Empty
            },
            Volume = state.Current.BigGame ? 100 : 65,
            IsEarnedBigEvent = false
        };
    }
}