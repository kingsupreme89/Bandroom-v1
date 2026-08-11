namespace Bandroom.Core.Helpers;

public sealed class TflHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Previous.Down == 0 || state.Current.Down == 0)
            return null;

        // TFL means the current down required more yards than the previous down.
        // FIXED: was `state.Delta.LostYards`, which can never be true -- PlayDelta.Calculate
        // derives it from PlaySnapshot.YardLine, which GameWatcher.cs hardcodes to 0 on every
        // snapshot (no OCR region reads yard line at all), so previousLine - currentLine is
        // always 0-0=0, never negative. This evaluator could never fire. Down actually advancing
        // (rather than resetting to 1 via conversion/turnover) plus YardsToGo increasing is the
        // real, reliably-OCR'd signal for "this down needed more yards than the last one."
        //
        // FIXED 2026-08-11 (STATE_MACHINE_ANALYSIS Discrepancy #11): that same signal is exactly
        // what DefenseHelper (2nd/3rd down) and BigEventHelper (4th down) now use for their own
        // "(Loss)" cues, so every TFL that advances the down to 2, 3, or 4 fired BOTH this
        // evaluator's "Defense: Tackle for Loss" AND one of theirs at once -- two different real
        // cues stacked on the same snap. Those three downs are this evaluator's entire practical
        // domain (a down can only ever advance to 2, 3, or 4 in normal play), so this fires
        // exclusively where they don't: entirely excluding the ones already owned elsewhere.
        if (state.Current.Down > state.Previous.Down
            && state.Current.YardsToGo > state.Previous.YardsToGo
            && state.Current.Down != 2 && state.Current.Down != 3 && state.Current.Down != 4)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Tackle for Loss",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        return null;
    }
}
