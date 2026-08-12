namespace Bandroom.Core.Helpers;

public sealed class BigEventHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // A turnover (interception/fumble) is also a possession flip -- TurnoverHelper already
        // covers that with its own cue, so skip here to avoid both firing together.
        if (state.Current.Down == 3 && state.Delta.NewPossession && !state.Current.IsTurnover)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down",
                Volume = state.Current.BigGame ? 100 : 80,
                IsEarnedBigEvent = state.Current.BigGame
            };
        }

        // REMOVED 2026-08-11 (owner audit call): "Defense: Fourth Down (Loss)" retired as its own
        // card, same reasoning as "Defense: Third Down (Loss)"'s retirement earlier this session
        // (see DefenseHelper.cs) -- a 4th-down loss is already covered by the generic "Defense:
        // Tackle for Loss" cue (TflHelper, fires on the same snap) PLUS the plain "Defense: Fourth
        // Down" stop cue right below (a 4th-down loss essentially always ends the drive, so
        // Delta.NewPossession fires right alongside it). A separate 4th-down-specific Loss key was
        // redundant with both. The buffered edge-detection this branch used (DownDistanceBuffer)
        // is gone too -- nothing else in this class needed it.

        // A missed field goal is also a 4th-down possession flip -- FieldGoalMissedHelper already
        // covers that specific case with its own cue, so skip here to avoid both firing together.
        if (state.Current.Down == 4 && state.Delta.NewPossession && !state.Current.IsFieldGoalAttempt && !state.Current.IsTurnover)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Fourth Down",
                Volume = state.Current.BigGame ? 100 : 80,
                IsEarnedBigEvent = state.Current.BigGame
            };
        }

        return null;
    }
}
