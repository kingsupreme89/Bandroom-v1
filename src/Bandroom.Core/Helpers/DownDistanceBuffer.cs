namespace Bandroom.Core.Helpers;

/// <summary>Shared buffered edge-detection for the down/yards-to-go OCR split-tick gap (see
/// STATE_MACHINE_ANALYSIS Discrepancy #12): "down" and "yards to go" are independent OCR reads
/// that don't always land on the same tick, so a strict same-tick Previous-vs-Current comparison
/// can miss (or misclassify) a down transition entirely. The fix is to remember the down and the
/// YardsToGo baseline from the tick right before the transition, and keep comparing against that
/// baseline for a short window instead of requiring both fields to move on the exact same tick.
///
/// Extracted 2026-08-11 audit: BigEventHelper, DefenseHelper, TflHelper, OffenseDownHelper, and
/// DefenseThirdDownShortHelper each hand-rolled this exact pattern (identical fields/constant,
/// different names). This is a pure refactor -- every caller's own control flow (when to Start,
/// when to Advance, what "timed out" means, how to classify) is unchanged; only the bookkeeping
/// fields moved here.</summary>
public sealed class DownDistanceBuffer
{
    /// <summary>Sanity bound (2026-08-11 audit item #2): a single bad OCR read at the exact
    /// down-change tick could otherwise establish a wildly implausible baseline (e.g. a garbled
    /// negative or multi-digit misread) that poisons the whole ~750ms confirmation window.
    /// YardsToGo is always parsed/normalized into this range elsewhere (see GameWatcher.cs's
    /// DistancePattern / NormalizeDistanceRaw), so a reading outside it at the moment we'd latch
    /// it as a baseline is treated as a corrupt single-tick glitch and the buffer simply doesn't
    /// start (any prior pending state is cleared instead, same as a normal Clear()).</summary>
    public const int MinPlausibleYardsToGo = 0;
    public const int MaxPlausibleYardsToGo = 99;

    readonly int _maxPendingTicks;

    public int? PendingDown { get; private set; }
    public int BaselineYardsToGo { get; private set; }
    public int TicksPending { get; private set; }

    public bool IsPending => PendingDown != null;

    // TIGHTENED 2026-08-11 (owner call: felt too slow on every buffered down cue, not just the
    // 4th-down-conversion case -- 3 ticks (~750ms) was the shared default every caller
    // (TflHelper, OffenseDownHelper, DefenseHelper, BigEventHelper, DefenseThirdDownShortHelper)
    // used. Cut to 2 (~500ms): still enough for the yards-to-go OCR read to catch up on the
    // very next tick in the common case, just less of a wait on the timeout path.
    public DownDistanceBuffer(int maxPendingTicks = 2)
    {
        _maxPendingTicks = maxPendingTicks;
    }

    /// <summary>Latch a new pending down transition with the given pre-transition YardsToGo
    /// baseline. If the baseline is outside the plausible range, treat it as a corrupt OCR read
    /// and don't start buffering (equivalent to Clear()).</summary>
    public void Start(int down, int baselineYardsToGo)
    {
        if (baselineYardsToGo < MinPlausibleYardsToGo || baselineYardsToGo > MaxPlausibleYardsToGo)
        {
            Clear();
            return;
        }

        PendingDown = down;
        BaselineYardsToGo = baselineYardsToGo;
        TicksPending = 0;
    }

    public void Clear()
    {
        PendingDown = null;
        TicksPending = 0;
    }

    /// <summary>Advance the pending window by one tick. Returns true once TicksPending has
    /// exceeded MaxPendingTicks -- callers decide what "timed out" means for them: some clear the
    /// buffer outright and give up, others classify off whatever's been read by then rather than
    /// going silent.</summary>
    public bool Advance()
    {
        TicksPending++;
        return TicksPending > _maxPendingTicks;
    }
}
