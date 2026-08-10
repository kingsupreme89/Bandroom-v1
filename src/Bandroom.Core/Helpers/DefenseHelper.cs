namespace Bandroom.Core.Helpers;

public sealed class DefenseHelper : IRuleEvaluator
{
    // Cheap early-out: this evaluator only ever fires when the user's team is on defense.
    public bool CanFire(GameState state) => !state.UserHasPossession;

    public TriggerEvent? Evaluate(GameState state)
    {
        // Defense = user's team does NOT have the ball
        if (state.UserHasPossession)
            return null;

        // Edge-trigger on the down actually changing -- unlike OffenseDownHelper (which already
        // did this), this fired on every single tick a defended 2nd/3rd down stayed on screen,
        // not just once on the transition. Harmless while unassigned (nothing to spam), but would
        // have hammered AudioPlayer.Play on every ~0.5s tick the instant a song was assigned.
        if (state.Current.Down == state.Previous.Down)
            return null;

        // A 3rd-down loss that ALSO flips possession (e.g. a sack-fumble the defense recovers)
        // is a turnover/stop, not just a loss -- BigEventHelper already covers that as
        // "Defense: Third Down". Without this guard both fired together: two different real
        // cues stacked on one snap, each cutting the other off via interruptPrevious.
        // STATE_MACHINE_ANALYSIS.md Discrepancy #4.
        if (state.Delta.NewPossession)
            return null;

        // FIXED: was `state.Delta.LostYards`, which is always false -- see TflHelper.cs for the
        // root cause (PlaySnapshot.YardLine is hardcoded to 0, so LostYards can never be true).
        // Down having just changed (guaranteed by the guards above: unchanged-down and
        // new-possession are both already excluded) to 3 plus YardsToGo increasing is the same
        // real signal TflHelper now uses.
        if (state.Current.Down == 3 && state.Current.YardsToGo > state.Previous.YardsToGo)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        // REMOVED 2026-08-10 (the "gameplan" rewrite): this used to also fire a plain, distance-
        // blind "Defense: Third Down" here for every 3rd-down stop. That's now OffenseDownHelper's
        // job -- it fires "Defense: Third Down" (this same, pre-existing key, so default song
        // packs keep working unchanged) specifically for 3rd & LONG, and a new "Offense: Third
        // Down Short" for 3rd & short instead of always crediting the defense. Kept the (Loss)
        // branch here since a stuffed-for-a-loss down is always "long" too and needs to stay a
        // distinct, more specific cue rather than colliding with the plain long-yardage one --
        // OffenseDownHelper defers to this branch by skipping entirely whenever YardsToGo went up.

        // FIXED: same dead-signal bug as the Down == 3 branch above -- see its comment.
        if (state.Current.Down == 2 && state.Current.YardsToGo > state.Previous.YardsToGo)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Second Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        // REMOVED 2026-08-10: see the Down == 3 comment above -- "Defense: Second Down" (plain,
        // distance-blind) is now OffenseDownHelper's job too, fired only for 2nd & long.

        return null;
    }
}
