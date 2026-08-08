namespace Bandroom.Core.Helpers;

public sealed class DefenseHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // Defense = user's team does NOT have the ball
        if (state.UserHasPossession)
            return null;

        if (state.Current.Down == 3 && state.Delta.LostYards)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        if (state.Current.Down == 2)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Second Down",
                Volume = state.Current.BigGame ? 100 : 70,
                IsEarnedBigEvent = false
            };
        }

        return null;
    }
}
