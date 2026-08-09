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
        if (state.Current.Down > state.Previous.Down && state.Current.YardsToGo > state.Previous.YardsToGo)
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
