namespace Bandroom.Core;

public sealed class EventRouter
{
    private readonly IReadOnlyList<IRuleEvaluator> _rules;

    // Pooled instance fields, reused every Route() call (4x/second) instead of reallocated.
    // Route()'s return value (the deduped list) is only ever read by the caller before the
    // next tick's Route() call, so reusing the backing storage across calls is safe.
    private readonly List<TriggerEvent> _results = new();
    private readonly List<string> _resultEvaluatorNames = new();
    private readonly Dictionary<string, string> _keptBy = new(StringComparer.Ordinal);
    private readonly List<TriggerEvent> _deduped = new();

    public EventRouter(IEnumerable<IRuleEvaluator> rules)
    {
        _rules = rules.ToList();
    }

    /// <summary>onDuplicateDropped reports (droppedTrigger, droppedEvaluatorName,
    /// keptEvaluatorName) for each same-tick EventKey collision -- see Dedupe's own comment for
    /// why _rules' fixed construction order is what decides the winner, and why that's
    /// intentional rather than arbitrary.</summary>
    public IReadOnlyList<TriggerEvent> Route(GameState state, Action<TriggerEvent, string, string>? onDuplicateDropped = null)
    {
        _results.Clear();
        _resultEvaluatorNames.Clear();

        foreach (var rule in _rules)
        {
            if (!rule.CanFire(state))
                continue;

            var trigger = rule.Evaluate(state);
            if (trigger != null)
            {
                trigger.SourceEvaluator = rule.GetType().Name;
                _results.Add(trigger);
                _resultEvaluatorNames.Add(trigger.SourceEvaluator);
            }
        }

        return Dedupe(_results, _resultEvaluatorNames, _keptBy, _deduped, onDuplicateDropped);
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
    /// per EventKey, since _rules order is fixed construction order (deliberately: evaluators
    /// listed earlier in CreateEventRouter's array are meant to take priority over more generic
    /// ones added later, e.g. BigEventHelper/DefenseHelper's specific "(Loss)" cues ahead of
    /// TflHelper's generic one) -- not firing twice is the fix, picking a winner is just a
    /// side effect of that fixed order, now made traceable via evaluator-name provenance
    /// (2026-08-11 audit item #3) instead of only "a duplicate was dropped".</summary>
    static IReadOnlyList<TriggerEvent> Dedupe(List<TriggerEvent> results, List<string> evaluatorNames, Dictionary<string, string> keptBy, List<TriggerEvent> deduped, Action<TriggerEvent, string, string>? onDuplicateDropped)
    {
        if (results.Count < 2) return results;

        keptBy.Clear();
        deduped.Clear();
        for (int i = 0; i < results.Count; i++)
        {
            var trigger = results[i];
            var evaluatorName = evaluatorNames[i];
            if (keptBy.TryAdd(trigger.EventKey, evaluatorName))
            {
                deduped.Add(trigger);
            }
            else
            {
                onDuplicateDropped?.Invoke(trigger, evaluatorName, keptBy[trigger.EventKey]);
            }
        }
        return deduped;
    }
}
