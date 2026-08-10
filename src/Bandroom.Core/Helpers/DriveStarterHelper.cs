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

        // Skip if this tick also looks like a mid-drive first-down conversion
        // (Delta.WasFirstDown: Current.Down==1 && Previous.Down>1). A real change of
        // possession and an earned first down should never both be true on the same
        // tick, but NewPossession comes from a separate OCR color sample than Down and
        // can misread true for one tick right as the "1ST DOWN" banner covers the
        // possession-indicator region (STATE_MACHINE_ANALYSIS.md Race #1). Reported live
        // as "away team got 1st down is also linking to [Drive Starter]" -- the earned
        // first down cue was correct, this evaluator's false-positive was the second one
        // riding along on the same noisy tick. WasFirstDown is the more specific signal
        // (it also requires a real prior down > 1), so it wins the tie.
        if (state.Delta.WasFirstDown)
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