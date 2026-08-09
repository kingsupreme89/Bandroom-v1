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
}