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

        // REMOVED 2026-08-10 (the "gameplan" rewrite): this used to also fire a plain, distance-
        // blind "Defense: Third Down" here for every 3rd-down stop. That's now OffenseDownHelper's
        // job -- it fires "Defense: Third Down" (this same, pre-existing key, so default song
        // packs keep working unchanged) specifically for 3rd & LONG, and a new "Offense: Third
        // Down Short" for 3rd & short instead of always crediting the defense. Kept the (Loss)
        // branch here since a stuffed-for-a-loss down is always "long" too and needs to stay a
        // distinct, more specific cue rather than colliding with the plain long-yardage one --
        // OffenseDownHelper defers to this branch by skipping entirely whenever YardsToGo went up.
        if (down == 3)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

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
