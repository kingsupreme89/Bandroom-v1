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

        // FIXED: was `state.Delta.LostYards`, always false -- see TflHelper.cs for the root
        // cause (PlaySnapshot.YardLine hardcoded to 0). Down == 4 plus YardsToGo increasing from
        // the previous down is the same real, OCR'd signal used there.
        if (state.Current.Down == 4 && state.Current.YardsToGo > state.Previous.YardsToGo)
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
