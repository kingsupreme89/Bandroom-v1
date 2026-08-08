namespace Bandroom.Core.Helpers;

/// <summary>Fires when a safety is scored: defense gets 2 points.
/// Detected when the non-possession side gains exactly 2 points.</summary>
public sealed class SafetyHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        int homeDelta = state.Current.HomeScore - state.Previous.HomeScore;
        int awayDelta = state.Current.AwayScore - state.Previous.AwayScore;

        // Safety = defense scores 2 points on the team that had the ball
        // If Away had possession (+2 to Home = safety against Away)
        if (state.Previous.PossessionAway && homeDelta == 2)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Safety",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }

        // If Home had possession (+2 to Away = safety against Home)
        if (!state.Previous.PossessionAway && awayDelta == 2)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Safety",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }

        return null;
    }
}