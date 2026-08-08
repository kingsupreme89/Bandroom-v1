namespace Bandroom.Core.Helpers;

/// <summary>Fires penalty events when a penalty flag is detected.
/// Differentiates offense vs defense penalties based on the penalty flag data.</summary>
public sealed class PenaltyHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // Offense penalty: fire for the defense side
        if (state.Current.IsPenaltyOnOffense && !state.Previous.IsPenaltyOnOffense)
        {
            return new TriggerEvent
            {
                EventKey = "Penalty: Offense",
                Volume = 70,
                IsEarnedBigEvent = false
            };
        }

        // Defense penalty: fire for the offense side
        if (state.Current.IsPenaltyOnDefense && !state.Previous.IsPenaltyOnDefense)
        {
            return new TriggerEvent
            {
                EventKey = "Penalty: Defense",
                Volume = 70,
                IsEarnedBigEvent = false
            };
        }

        return null;
    }
}