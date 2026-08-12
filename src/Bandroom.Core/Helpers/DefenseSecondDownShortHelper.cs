namespace Bandroom.Core.Helpers;

/// <summary>Fires "Defense: Second Down Short" on the SAME condition OffenseDownHelper's 2nd-down
/// short branch fires "Offense: Second Down Short" -- a separate evaluator, not a branch inside
/// that one, so both fire on the same tick (same pattern as DefenseThirdDownShortHelper/
/// OffenseDownHelper's 3rd-down pairing). One routes to the driving offense (their own hype
/// facing a manageable 2nd down), this one routes to the opposing defense.
///
/// Owner rule 2026-08-11 (live game): 2nd & short is the INVERSE balance of 3rd & short -- the
/// offense is the bigger moment here (whoever has the ball plays at full 100), so this Defense
/// counterpart is the ducked side at 60, not the loud one. Mirrors OffenseDownHelper's own volume
/// comment for the exact same rule.
///
/// Buffered edge-detection, same fix as DefenseThirdDownShortHelper/OffenseDownHelper (STATE_
/// MACHINE_ANALYSIS Discrepancy #12): "down" and "yards to go" are independent OCR reads that
/// don't always land on the same tick. Mirrors OffenseDownHelper's buffer exactly so both keep
/// firing on the SAME tick as each other.</summary>
public sealed class DefenseSecondDownShortHelper : IRuleEvaluator
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

        // Same Loss deferral as OffenseDownHelper -- a down that got LONGER (tackle for loss)
        // reads as long here too, DefenseHelper's "(Loss)" branch already owns that specific cue.
        if (state.Current.YardsToGo > baselineYardsToGo)
            return null;

        // Must stay in sync with OffenseDownHelper.isShort's own threshold since both fire on the
        // same tick for the same snap (see this file's header comment).
        if (state.Current.YardsToGo > 5)
            return null; // long -- "Defense: Second Down" (via OffenseDownHelper) already covers it

        return new TriggerEvent
        {
            EventKey = "Defense: Second Down Short",
            Volume = 60,
            IsEarnedBigEvent = false
        };
    }
}
