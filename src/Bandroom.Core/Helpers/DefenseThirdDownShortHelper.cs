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
/// -- see ResolveEventRouting/OnEngineEventsDetected's same-tick layering fix.</summary>
public sealed class DefenseThirdDownShortHelper : IRuleEvaluator
{
    public bool CanFire(GameState state) => state.Current.Down != state.Previous.Down;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.Down != 3)
            return null;

        if (state.Delta.NewPossession)
            return null;

        // Same Loss deferral as OffenseDownHelper -- a down that got LONGER (tackle for loss)
        // reads as long here too, DefenseHelper's "(Loss)" branch already owns that specific cue.
        if (state.Current.YardsToGo > state.Previous.YardsToGo)
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
