namespace Bandroom.Core;

public interface IRuleEvaluator
{
    TriggerEvent? Evaluate(GameState state);

    /// <summary>Cheap pre-check run before Evaluate on every tick (~4x/second across ~18
    /// evaluators). Default true (always evaluate) so existing evaluators need no changes.
    /// Override only when there's a genuinely cheap guard AND Evaluate has no side effects
    /// (state resets, etc.) that must run even when the evaluator won't fire this tick --
    /// skipping Evaluate entirely means skipping those too.</summary>
    bool CanFire(GameState state) => true;
}
