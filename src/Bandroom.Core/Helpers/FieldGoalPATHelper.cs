namespace Bandroom.Core.Helpers;

/// <summary>Fires field-goal-made, field-goal-missed, and PAT-made events.
/// Detects PAT when the PAT flag transitions true AND a score changed by 1.
/// Detects field goal when score changes by 3.
/// Field goal missed: no score change but IsPAT went true→false (detected externally, not here).</summary>
public sealed class FieldGoalPATHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        int scoreDiff = state.Current.HomeScore + state.Current.AwayScore
                      - (state.Previous.HomeScore + state.Previous.AwayScore);
        if (scoreDiff == 0)
            return null;

        // PAT: exactly 1 point added
        if (scoreDiff == 1 && state.Current.IsPAT)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: PAT Made",
                Volume = 75,
                IsEarnedBigEvent = false
            };
        }

        // A safety ALSO nets a total-score delta of +2 (defense scores against the possessing
        // team), and used to be indistinguishable from a real 2-point conversion here since this
        // only checked the combined total, not which side actually gained the points --
        // SafetyHelper would correctly fire "Defense: Safety" at the same time this incorrectly
        // fired "Offense: 2-Point Conversion Made" for a play that never happened.
        // STATE_MACHINE_ANALYSIS.md Discrepancy #5: a real 2-point conversion is the POSSESSING
        // side's own score going up by 2; a safety is the OTHER side's score going up by 2.
        int homeDelta = state.Current.HomeScore - state.Previous.HomeScore;
        int awayDelta = state.Current.AwayScore - state.Previous.AwayScore;
        bool possessingSideGained2 = state.Previous.PossessionAway ? awayDelta == 2 : homeDelta == 2;

        // 2-point conversion: exactly 2 points added, and it's the offense's own score that moved
        if (scoreDiff == 2 && possessingSideGained2)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: 2-Point Conversion Made",
                Volume = 85,
                IsEarnedBigEvent = true
            };
        }

        // Field goal: exactly 3 points added
        if (scoreDiff == 3)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Field Goal Made",
                Volume = 85,
                IsEarnedBigEvent = false
            };
        }

        return null;
    }
}