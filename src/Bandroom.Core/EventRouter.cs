namespace Bandroom.Core;

public sealed class EventRouter
{
    private readonly IReadOnlyList<IRuleEvaluator> _rules;

    public EventRouter(IEnumerable<IRuleEvaluator> rules)
    {
        _rules = rules.ToList();
    }

    public IReadOnlyList<TriggerEvent> Route(GameState state)
    {
        var results = new List<TriggerEvent>();

        foreach (var rule in _rules)
        {
            var trigger = rule.Evaluate(state);
            if (trigger != null)
            {
                results.Add(trigger);
            }
        }

        return results;
    }
}
