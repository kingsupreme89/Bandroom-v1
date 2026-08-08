namespace Bandroom.Core.Helpers;

public sealed class BigEventHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.Down == 3 && state.Delta.NewPossession)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down",
                Volume = state.Current.BigGame ? 100 : 80,
                IsEarnedBigEvent = state.Current.BigGame
            };
        }

        if (state.Current.Down == 4 && state.Delta.LostYards)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Fourth Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 85,
                IsEarnedBigEvent = true
            };
        }

        if (state.Current.Down == 4 && state.Delta.NewPossession)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Fourth Down",
                Volume = state.Current.BigGame ? 100 : 80,
                IsEarnedBigEvent = state.Current.BigGame
            };
        }

        return null;
    }
}
