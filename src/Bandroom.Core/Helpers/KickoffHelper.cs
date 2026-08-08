namespace Bandroom.Core.Helpers;

/// <summary>Fires kickoff events. Which variant fires depends on the quarter
/// (opening vs second-half) and possession side (kicking vs receiving).
/// All kickoff events fire once on the transition from not-kickoff to kickoff.</summary>
public sealed class KickoffHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsKickoff || state.Previous.IsKickoff)
            return null;

        // Opening kickoff: 1st quarter, first time kickoff is seen
        if (state.Current.Quarter == 1)
        {
            return new TriggerEvent
            {
                EventKey = "Other: Opening Kickoff",
                Volume = 90,
                IsEarnedBigEvent = true
            };
        }

        // Second-half kickoff: 3rd quarter
        if (state.Current.Quarter == 3)
        {
            return new TriggerEvent
            {
                EventKey = "Other: Second-Half Kickoff",
                Volume = 90,
                IsEarnedBigEvent = true
            };
        }

        // Other kickoffs: user's team receiving = user has possession
        if (state.UserHasPossession)
        {
            return new TriggerEvent
            {
                EventKey = "Other: Kickoff on Kick (Receiving)",
                Volume = 75,
                IsEarnedBigEvent = false
            };
        }

        return new TriggerEvent
        {
            EventKey = "Other: Kickoff on Kick (Kicking)",
            Volume = 75,
            IsEarnedBigEvent = false
        };
    }
}