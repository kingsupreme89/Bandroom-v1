namespace Bandroom.Core.Helpers;

public sealed class TflHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Previous.Down == 0 || state.Current.Down == 0)
            return null;

        // TFL means the current down required more yards than the previous down.
        if (state.Current.YardsToGo > state.Previous.YardsToGo && state.Delta.LostYards)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Tackle for Loss",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        return null;
    }
}
