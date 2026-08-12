namespace Bandroom.Core.Helpers;

public sealed class DefenseHelper : IRuleEvaluator
{
    // Cheap early-out: this evaluator only ever fires when the user's team is on defense.
    public bool CanFire(GameState state) => !state.UserHasPossession;

    // Buffered edge-detection -- STATE_MACHINE_ANALYSIS Discrepancy #12: the down region and the
    // yards-to-go region are two independent OCR reads that don't always land on the same tick.
    // A same-tick-only check (down just changed AND yards-to-go already higher on that exact
    // Previous->Current pair) missed splits where down updates on tick N but yards-to-go doesn't
    // catch up until tick N+1: at tick N the yards field is still stale (condition fails), and at
    // tick N+1 Current.Down == Previous.Down so the old "down just changed" guard excluded it too
    // -- the loss never fired. Fix: remember the down and the yards-to-go baseline from the tick
    // right before the transition, and keep comparing against that baseline for a short window
    // instead of requiring both fields to move on the exact same tick. Shared bookkeeping
    // extracted into DownDistanceBuffer (2026-08-11 audit).
    readonly DownDistanceBuffer _buffer = new();

    public TriggerEvent? Evaluate(GameState state)
    {
        // Defense = user's team does NOT have the ball
        if (state.UserHasPossession)
        {
            _buffer.Clear();
            return null;
        }

        // A 3rd-down loss that ALSO flips possession (e.g. a sack-fumble the defense recovers)
        // is a turnover/stop, not just a loss -- BigEventHelper already covers that as
        // "Defense: Third Down". Without this guard both fired together: two different real
        // cues stacked on one snap, each cutting the other off via interruptPrevious.
        // STATE_MACHINE_ANALYSIS.md Discrepancy #4.
        if (state.Delta.NewPossession)
        {
            _buffer.Clear();
            return null;
        }

        if (state.Current.Down != state.Previous.Down)
        {
            _buffer.Start(state.Current.Down, state.Previous.YardsToGo);
        }
        else if (_buffer.IsPending)
        {
            int pendingDown = _buffer.PendingDown!.Value;
            if (_buffer.Advance())
            {
                // Timed out with the down never matching Current.Down again, or matching but
                // YardsToGo never actually increasing (item #5: "almost fired" ghost log). Only
                // downs 2/3 are this evaluator's territory (see the down==2/3 branches below).
                if (pendingDown is 2 or 3)
                    state.NearMisses.Add($"DefenseHelper: buffered wait for a down-{pendingDown} Loss timed out, no YardsToGo increase detected");
                _buffer.Clear();
            }
        }

        if (_buffer.PendingDown == null || _buffer.PendingDown != state.Current.Down)
            return null;

        if (state.Current.YardsToGo <= _buffer.BaselineYardsToGo)
            return null;

        // Consume the pending window so this fires once per down transition, not once per tick
        // it happens to still be true.
        int down = _buffer.PendingDown.Value;
        _buffer.Clear();

        // REMOVED 2026-08-11 (owner audit call): "Defense: Third Down (Loss)" retired as its own
        // card -- TflHelper's generic "Defense: Tackle for Loss" already fires on this exact same
        // snap (see that file's own comment: it's intentionally NOT down-specific and fires
        // alongside whatever the down-specific cue is), so having a separate 3rd-down-only Loss
        // key was redundant. Down==3 losses now just get the one generic Tackle for Loss cue.
        // (Was: "Defense: Third Down (Loss)", 2026-08-07 through 2026-08-11 -- moved to
        // ConfigStore.RetiredEventKeys so an already-assigned song under the old key doesn't error,
        // it just stops firing.)

        // REMOVED 2026-08-10: see the Down == 3 comment above -- "Defense: Second Down" (plain,
        // distance-blind) is now OffenseDownHelper's job too, fired only for 2nd & long.
        if (down == 2)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Second Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        return null;
    }
}
