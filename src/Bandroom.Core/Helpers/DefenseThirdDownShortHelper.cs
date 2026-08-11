namespace Bandroom.Core.Helpers;

/// <summary>Fires "Defense: Third Down Short" on the SAME condition OffenseDownHelper's short
/// branch fires "Offense: Third Down Short" -- a separate evaluator, not a branch inside that
/// one, so both fire on the same tick (confirmed safe: EventRouter.Route runs every evaluator
/// every tick and only dedupes exact same-EventKey collisions, see EventRouter.cs). One routes
/// to the driving offense (their own hype facing a manageable 3rd down), this one routes to the
/// opposing defense (the crowd anticipating a stop) -- home-always/away-only-if-Big-Game gating
/// (WebMainForm.ResolveEventRouting tier 2).
///
/// Owner's own framing: this fires on FACING the down, not on the stop outcome. A successful
/// stop naturally continues into the existing "Defense: Fourth Down" cue on the next down
/// change; a failed stop (they convert) naturally continues into a normal
/// "Offense: Earned First Down" for the opponent -- neither needs new logic here.
///
/// NOTE: firing alongside Offense: Third Down Short on the same tick means WebMainForm's
/// per-tick fire loop must not let the second event's interruptPrevious cut the first one off
/// -- see ResolveEventRouting/OnEngineEventsDetected's same-tick layering fix.
///
/// UPDATED 2026-08-11: OffenseDownHelper picked up a buffered-pending window (STATE_MACHINE_
/// ANALYSIS Discrepancy #12's fix, applied here too the same session after an audit caught this
/// file hadn't gotten it) because "down" and "yards to go" are independent OCR reads that don't
/// always land on the same tick -- classifying short-vs-long off Current.YardsToGo on the exact
/// down-change tick could read a stale value, causing this evaluator to misfire or never fire.
/// Mirrors OffenseDownHelper's buffer exactly (down/tick-count/baseline fields) so the two keep
/// firing on the SAME tick as each other, preserving the same-tick pairing this file's own
/// header comment depends on.</summary>
public sealed class DefenseThirdDownShortHelper : IRuleEvaluator
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

        if (down != 3)
            return null;

        // Same Loss deferral as OffenseDownHelper -- a down that got LONGER (tackle for loss)
        // reads as long here too, DefenseHelper's "(Loss)" branch already owns that specific cue.
        if (state.Current.YardsToGo > baselineYardsToGo)
            return null;

        if (state.Current.YardsToGo > 3)
            return null; // long -- Defense: Third Down (via OffenseDownHelper) already covers it

        return new TriggerEvent
        {
            EventKey = "Defense: Third Down Short",
            Volume = state.Current.BigGame ? 100 : 70,
            IsEarnedBigEvent = false
        };
    }
}
