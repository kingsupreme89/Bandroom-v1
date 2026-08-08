namespace Bandroom.Core.Helpers;

/// <summary>Fires when a touchdown is detected (offense scored) or
/// the possession flips after a pick-six / fumble-return TD (defense scored).
/// Only fires on the transition from not-touchdown to touchdown.</summary>
public sealed class TouchdownHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsTouchdown || state.Previous.IsTouchdown)
            return null;

        // Defense touchdown = possession flipped AND touchdown flag is set
        if (state.Delta.NewPossession)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Touchdown Scored",
                Volume = state.Current.BigGame ? 100 : 85,
                IsEarnedBigEvent = true
            };
        }

        // Offense touchdown
        return new TriggerEvent
        {
            EventKey = "Offense: Touchdown Scored",
            Volume = state.Current.BigGame ? 100 : 85,
            IsEarnedBigEvent = true
        };
    }
}