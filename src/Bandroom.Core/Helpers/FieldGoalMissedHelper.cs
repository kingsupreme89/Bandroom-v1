namespace Bandroom.Core.Helpers;

/// <summary>Fires when a field goal attempt fails: the PAT flag was set (FG attempts
/// share the same game state flag since both are special-teams kicks) but no points
/// were scored and possession flipped (missed FG gives ball to defense at spot).
/// Also detects via the scenario: possession flipped with no score change.</summary>
public sealed class FieldGoalMissedHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // Field goal missed: PAT flag was set, then cleared, and no points scored
        if (state.Previous.IsPAT && !state.Current.IsPAT
            && state.Delta.NewPossession)
        {
            int homeDelta = state.Current.HomeScore - state.Previous.HomeScore;
            int awayDelta = state.Current.AwayScore - state.Previous.AwayScore;
            if (homeDelta == 0 && awayDelta == 0)
            {
                return new TriggerEvent
                {
                    EventKey = "Defense: Field Goal Missed by Opponent",
                    Volume = 85,
                    IsEarnedBigEvent = true
                };
            }
        }

        return null;
    }
}