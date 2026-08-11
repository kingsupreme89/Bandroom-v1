namespace Bandroom.Core.Helpers;

/// <summary>Fires a generic "Defense: Tackle for Loss" cue whenever a down advances (2nd, 3rd, or
/// 4th) with YardsToGo having increased -- alongside, not instead of, the more specific
/// "Defense: Second/Third Down (Loss)"/"Defense: Fourth Down (Loss)" cues DefenseHelper/
/// BigEventHelper already fire for the same snap. Owner's explicit call (2026-08-11 audit): this
/// generic cue should still play in addition to those, not be replaced by them.
///
/// FIXED 2026-08-11 (audit finding): a prior "fix" excluded downs 2/3/4 entirely to stop this
/// evaluator from double-firing alongside DefenseHelper/BigEventHelper -- but since a down can
/// only ever advance INTO 2, 3, or 4 in normal play, that exclusion covered this evaluator's
/// entire reachable domain and made it permanently dead code (its EventKey stayed listed as an
/// assignable song card that could never actually play). Restored to fire on all three, now
/// intentionally overlapping with the down-specific cues (EventRouter's same-tick dedupe only
/// blocks identical EventKeys, not different ones -- WebMainForm's per-tick layering already
/// supports exactly this, see DefenseThirdDownShortHelper/OffenseDownHelper's identical
/// same-tick-pairing pattern).
///
/// Buffered edge-detection -- same OCR split-tick fix as DefenseHelper/BigEventHelper/
/// OffenseDownHelper/DefenseThirdDownShortHelper (STATE_MACHINE_ANALYSIS Discrepancy #12):
/// "down" and "yards to go" are independent OCR reads that don't always land on the same tick,
/// so classifying a loss off Current.YardsToGo on the exact down-change tick can read a stale
/// value.</summary>
public sealed class TflHelper : IRuleEvaluator
{
    readonly DownDistanceBuffer _buffer = new();

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Previous.Down == 0 || state.Current.Down == 0)
        {
            _buffer.Clear();
            return null;
        }

        if (state.Current.Down != state.Previous.Down)
        {
            // A turnover also changes Down for the new offense -- that's TurnoverHelper's
            // moment, not a loss cue. A down RESETTING to 1 (conversion/turnover) isn't an
            // advance either. Same NewPossession guard DefenseHelper/BigEventHelper use.
            if (state.Delta.NewPossession || state.Current.Down <= state.Previous.Down)
            {
                _buffer.Clear();
                return null;
            }

            _buffer.Start(state.Current.Down, state.Previous.YardsToGo);
            return null; // wait for the yards-to-go OCR read to catch up before classifying
        }

        if (!_buffer.IsPending)
            return null;

        bool timedOut = _buffer.Advance();
        if (!timedOut && state.Current.YardsToGo == _buffer.BaselineYardsToGo)
            return null; // yards-to-go hasn't updated yet -- keep waiting

        int baselineYardsToGo = _buffer.BaselineYardsToGo;
        _buffer.Clear();

        if (state.Current.YardsToGo <= baselineYardsToGo)
        {
            if (timedOut)
                state.NearMisses.Add("TflHelper: buffered wait timed out, no YardsToGo increase detected (not a Loss)");
            return null; // not actually a loss
        }

        return new TriggerEvent
        {
            EventKey = "Defense: Tackle for Loss",
            Volume = state.Current.BigGame ? 100 : 75,
            IsEarnedBigEvent = true
        };
    }
}
