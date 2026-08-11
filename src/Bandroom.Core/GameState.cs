namespace Bandroom.Core;

public sealed class GameState
{
    public PlaySnapshot Current { get; init; } = new();
    public PlaySnapshot Previous { get; init; } = new();

    /// <summary>True when the user's selected team is the Home team.
    /// Used by evaluators to determine offense vs defense from the user's perspective.</summary>
    public bool UserIsHome { get; init; }

    // Cached, not recomputed per access -- Current/Previous are init-only so the result can never
    // change after construction, but every evaluator that reads state.Delta (7+ on a scoring play)
    // was triggering a fresh PlayDelta.Calculate + heap allocation for identical data each time.
    PlayDelta? _delta;
    public PlayDelta Delta => _delta ??= PlayDelta.Calculate(Previous, Current);

    /// <summary>True when the USER's team currently has possession of the ball.</summary>
    public bool UserHasPossession =>
        UserIsHome ? !Current.PossessionAway : Current.PossessionAway;

    /// <summary>2026-08-11 audit item #5 ("almost fired" ghost log): buffered evaluators
    /// (DownDistanceBuffer users) append a plain-English note here when their confirmation window
    /// times out WITHOUT the change they were waiting for -- e.g. "was this a Loss?" buffering
    /// that never saw YardsToGo actually increase. GameWatcher drains this after each Route() call
    /// and writes each note to EventActivityLog as a near-miss entry, distinct from a real fire, so
    /// it's visible in the Event Log UI instead of silently doing nothing. Plain List, not a
    /// pooled/reused field like EventRouter's internals, since near-misses are rare (not 4x/sec
    /// hot path) and GameState itself is already a fresh instance per tick.</summary>
    public List<string> NearMisses { get; } = new();
}