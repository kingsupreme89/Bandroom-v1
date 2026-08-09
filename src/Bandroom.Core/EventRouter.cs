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

        return Dedupe(results);
    }

    /// <summary>Backstop against two evaluators firing the same EventKey on one tick.
    /// STATE_MACHINE_ANALYSIS.md's Discrepancy #3/#4/#5/#12 findings were all instances of exactly
    /// this -- two evaluators independently matching overlapping game states and both emitting a
    /// (sometimes identical, sometimes just conflicting) EventKey, which WebMainForm's
    /// FireEventForSide then fired twice with interruptPrevious:true, audibly cutting the first
    /// cue off mid-play. Each of those was root-caused and fixed at the evaluator level (adding
    /// NewPossession/score-delta guards so only one evaluator's conditions can be true at a time),
    /// but that discipline lives in N separate files with no compiler enforcement -- a future
    /// evaluator added without the same care would silently reintroduce the exact bug class this
    /// audit spent most of its time on. This dedupe is the single choke point that makes a
    /// same-tick duplicate EventKey structurally impossible regardless of what any individual
    /// evaluator does, on top of (not instead of) the per-evaluator guards. Keeps the first match
    /// per EventKey, since _rules order is fixed construction order and callers don't currently
    /// rely on which duplicate "wins" -- the fix is not firing twice, not picking a winner.</summary>
    static IReadOnlyList<TriggerEvent> Dedupe(List<TriggerEvent> results)
    {
        if (results.Count < 2) return results;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<TriggerEvent>(results.Count);
        foreach (var trigger in results)
        {
            if (seen.Add(trigger.EventKey))
                deduped.Add(trigger);
        }
        return deduped;
    }
}
