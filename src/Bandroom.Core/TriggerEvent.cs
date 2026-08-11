namespace Bandroom.Core;

public sealed class TriggerEvent
{
    public string EventKey { get; init; } = string.Empty;
    public int Volume { get; init; } = 100;
    public bool IsEarnedBigEvent { get; init; }

    /// <summary>Name of the evaluator that produced this event, e.g. "DefenseHelper". Set by
    /// EventRouter.Route right after Evaluate returns (not by the evaluators themselves, so no
    /// evaluator needs to know its own type name) -- used purely for provenance in same-tick
    /// dedupe logging (EventRouter.Dedupe / GameWatcher's onDuplicateDropped wiring) so a log
    /// entry can say which evaluator's event was kept vs dropped, not just "a duplicate was
    /// dropped". Never affects routing/dedupe logic itself.</summary>
    public string SourceEvaluator { get; internal set; } = string.Empty;
}
