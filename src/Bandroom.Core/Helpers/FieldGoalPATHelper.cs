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

        // 2-point conversion: exactly 2 points added (detected via IsPAT + score delta)
        if (scoreDiff == 2)
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