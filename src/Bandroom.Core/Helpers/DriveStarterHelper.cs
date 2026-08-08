namespace Bandroom.Core.Helpers;

/// <summary>Fires "Drive Starter" when a fresh possession begins (not from a kickoff,
/// not from a turnover — those have their own events). Detected as down 1 with
/// a new possession that wasn't flagged as turnover or kickoff.</summary>
public sealed class DriveStarterHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // Must be a change of possession to a new 1st down
        if (!state.Delta.NewPossession || state.Current.Down != 1)
            return null;

        // Skip if it's a kickoff or turnover — those fire their own events
        if (state.Current.IsKickoff || state.Current.IsTurnover)
            return null;

        // Skipping opening snap (Previous.Down == 0 means no prior state)
        if (state.Previous.Down == 0)
            return null;

        // User's team got the ball → Offense Drive Starter
        if (state.UserHasPossession)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Drive Starter",
                Volume = 70,
                IsEarnedBigEvent = false
            };
        }

        return new TriggerEvent
        {
            EventKey = "Defense: Drive Starter",
            Volume = 70,
            IsEarnedBigEvent = false
        };
    }
}