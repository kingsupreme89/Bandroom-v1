namespace Bandroom.Core.Helpers;

/// <summary>Fires "Offense: Second Down" on 2nd & LONG -- the missing offense-side counterpart to
/// "Defense: Second Down" (still fired by OffenseDownHelper's own 2nd-down-long branch, unchanged).
/// Added 2026-08-19 (owner report: no Offense card existed at all for 2nd & long, unlike 3rd down
/// which already dual-fires "Offense: Third Down"/"Defense: Third Down" together).
///
/// A separate evaluator, not a branch inside OffenseDownHelper, so both fire on the same tick --
/// same pattern as DefenseSecondDownShortHelper/OffenseDownHelper's 2nd-short pairing and
/// DefenseThirdDownHelper/OffenseDownHelper's 3rd-down pairing.
///
/// Volume convention matches 3rd down's long case (Defense full at whatever OffenseDownHelper
/// already computes, Offense ducked to 60) -- long yardage "hands it to the defense" per
/// OffenseDownHelper's own original design comment, so the offense side is the quieter one here,
/// same as 3rd & long's "Offense: Third Down" @ 60.
///
/// Buffered edge-detection, mirrors OffenseDownHelper/DefenseSecondDownShortHelper exactly (STATE_
/// MACHINE_ANALYSIS Discrepancy #12) so this fires on the SAME tick as "Defense: Second Down".</summary>
public sealed class OffenseSecondDownHelper : IRuleEvaluator
{
    readonly DownDistanceBuffer _buffer = new();

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.Down != state.Previous.Down)
        {
            if (state.Delta.NewPossession)
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

        int down = _buffer.PendingDown!.Value;
        int baselineYardsToGo = _buffer.BaselineYardsToGo;
        _buffer.Clear();

        if (down != 2)
            return null;

        // Same Loss deferral as OffenseDownHelper/DefenseSecondDownShortHelper -- a down that got
        // LONGER (tackle for loss) reads as long here too, DefenseHelper's "(Loss)" branch already
        // owns that specific cue, unless it's a Big Game (see OffenseDownHelper's own comment for
        // why Big Game still wants the hype cue on top of the loss cue).
        if (state.Current.YardsToGo > baselineYardsToGo && !state.Current.BigGame)
            return null;

        // Must stay in sync with OffenseDownHelper.isShort's own threshold -- short is
        // "Offense: Second Down Short"'s territory (via OffenseDownHelper), not this one.
        if (state.Current.YardsToGo <= 5)
            return null;

        return new TriggerEvent
        {
            EventKey = "Offense: Second Down",
            Volume = 60,
            IsEarnedBigEvent = false
        };
    }
}
