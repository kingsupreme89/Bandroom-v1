namespace Bandroom.Core.Helpers;

public sealed class BigEventHelper : IRuleEvaluator
{
    // Buffered edge-detection for the Down == 4 (Loss) branch below -- same split-tick OCR gap
    // as DefenseHelper (STATE_MACHINE_ANALYSIS Discrepancy #12): down and yards-to-go are
    // separate OCR reads that don't always update on the same tick, so a strict same-tick
    // Previous-vs-Current comparison can miss the transition entirely. See DefenseHelper.cs for
    // the full explanation of this pattern. Shared bookkeeping extracted into DownDistanceBuffer
    // (2026-08-11 audit) -- see that file for the sanity-bound guard on the baseline too.
    readonly DownDistanceBuffer _buffer = new();

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

        if (state.Current.Down != state.Previous.Down)
        {
            _buffer.Start(state.Current.Down, state.Previous.YardsToGo);
        }
        else if (_buffer.IsPending)
        {
            if (_buffer.Advance())
            {
                // Timed out with no qualifying Loss ever seen (item #5: "almost fired" ghost log).
                if (_buffer.PendingDown == 4)
                    state.NearMisses.Add("BigEventHelper: buffered wait for a 4th-down Loss timed out, no YardsToGo increase detected");
                _buffer.Clear();
            }
        }

        if (_buffer.PendingDown == 4 && state.Current.Down == 4 && state.Current.YardsToGo > _buffer.BaselineYardsToGo)
        {
            _buffer.Clear();
            return new TriggerEvent
            {
                EventKey = "Defense: Fourth Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 85,
                IsEarnedBigEvent = true
            };
        }

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
