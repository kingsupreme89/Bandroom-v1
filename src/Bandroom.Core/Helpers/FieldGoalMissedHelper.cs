namespace Bandroom.Core.Helpers;

/// <summary>Fires when a field goal attempt fails: the "banner" region's IsFieldGoalAttempt flag
/// (see PlaySnapshot.cs) is set and possession flips with no score change.
/// STATE_MACHINE_ANALYSIS.md Discrepancy #6: this used to gate on IsPAT going true->false, but
/// IsPAT is only ever set from the "situation" OCR region's "PAT GOOD" success text -- a missed
/// kick never produces that text, so the old condition could never fire. IsFieldGoalAttempt comes
/// from the "banner" region's "FIELD GOAL" text instead, which appears for both made and missed
/// attempts (the banner text alone doesn't distinguish them), so a made kick is excluded here by
/// requiring no score change -- FieldGoalPATHelper already claims the scoreDiff==3 case.
/// NewPossession is itself edge-triggered (only true on the tick possession actually flips), so
/// this doesn't need its own true->false edge on IsFieldGoalAttempt (which, being a gated/sticky
/// region like "situation", may not clear until the next scoring banner appears).</summary>
public sealed class FieldGoalMissedHelper : IRuleEvaluator
{
    // Cheap early-out: Evaluate's first condition, checked before touching Delta.
    public bool CanFire(GameState state) => state.Current.IsFieldGoalAttempt;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsFieldGoalAttempt || !state.Delta.NewPossession)
            return null;

        int homeDelta = state.Current.HomeScore - state.Previous.HomeScore;
        int awayDelta = state.Current.AwayScore - state.Previous.AwayScore;
        if (homeDelta != 0 || awayDelta != 0)
            return null;

        return new TriggerEvent
        {
            EventKey = "Defense: Field Goal Missed by Opponent",
            Volume = 85,
            IsEarnedBigEvent = true
        };
    }
}
