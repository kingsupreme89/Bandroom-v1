namespace Bandroom.Core;

public interface IRuleEvaluator
{
    TriggerEvent? Evaluate(GameState state);
}
