namespace Bandroom.Core.Helpers;

/// <summary>Fires earned-first-down events. The base event fires any time a new 1st down
/// is earned (not the opening kickoff's 1st & 10). Variants fire based on yardage gained
/// or field position to let users assign different sounds per situation.</summary>
public sealed class FirstDownHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // Must be a fresh first down (not the opening snap)
        if (!state.Delta.WasFirstDown || state.Previous.Down == 0)
            return null;

        int yardsGained = state.Delta.YardsGained;

        // Big Gain: 15+ yard pickup
        if (yardsGained >= 15)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Earned First Down (Big Gain)",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }

        // Midfield: inside opponent territory (yard line <= 50). DISABLED 2026-08-07 --
        // YardLine is hardcoded to 0 everywhere (OCR for it was never built, see TASK_BOARD.md),
        // so "<= 50" was always true, meaning this branch fired on literally every first down
        // and the base event below could never be reached. Re-enable once YardLine reads a real
        // value; until then this must stay off rather than silently misfire on every down.
        // if (state.Current.YardLine <= 50)
        // {
        //     return new TriggerEvent
        //     {
        //         EventKey = "Offense: Earned First Down (Midfield)",
        //         Volume = 85,
        //         IsEarnedBigEvent = true
        //     };
        // }

        // Base first down
        return new TriggerEvent
        {
            EventKey = "Offense: Earned First Down",
            Volume = 80,
            IsEarnedBigEvent = false
        };
    }
}