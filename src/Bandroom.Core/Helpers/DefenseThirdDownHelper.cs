namespace Bandroom.Core.Helpers;

/// <summary>Fires "Defense: Third Down" (long) or "Defense: Third Down Short" for the offense
/// facing 3rd down at ANY distance -- paired with OffenseDownHelper's own "Offense: Third Down"/
/// "Offense: Third Down Short" on the same tick (60%), same balance as before.
///
/// MERGED 2026-08-15 (live owner report: double-fire): this used to be two separate evaluators
/// (DefenseThirdDownHelper + DefenseThirdDownShortHelper), each running its OWN independent
/// DownDistanceBuffer instance tracking the SAME down transition. Under flickering OCR/RAM
/// distance reads, the two buffers could resolve on DIFFERENT ticks with DIFFERENT
/// Current.YardsToGo snapshots -- each looking individually valid at its own resolution moment --
/// so a single physical 3rd down could get classified as "long" by one buffer and "short" by the
/// other, firing BOTH EventKeys for one play (different keys, so EventRouter's dedupe never
/// caught it; only the first event in a tick's batch stops previous audio, so both songs played
/// simultaneously). A same-instant exclusion guard (added earlier this session) only prevented
/// the double-fire when both resolved on the SAME tick -- it didn't stop two independent buffers
/// from disagreeing across ticks. One evaluator, one buffer, one resolution moment, one EventKey
/// -- structurally impossible to double-fire now, matching how OffenseDownHelper already avoids
/// this exact problem on the offense side via a single switch.</summary>
public sealed class DefenseThirdDownHelper : IRuleEvaluator
{
    readonly DownDistanceBuffer _buffer = new();

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.Down != state.Previous.Down)
        {
            if (state.Delta.NewPossession)
            {
                _buffer.Clear();
                return null;
            }

            _buffer.Start(state.Current.Down, state.Previous.YardsToGo);
            return null; // wait for the yards-to-go OCR read to catch up before classifying
        }

        if (!_buffer.IsPending)
            return null;

        bool timedOut = _buffer.Advance();
        if (!timedOut && state.Current.YardsToGo == _buffer.BaselineYardsToGo)
            return null; // yards-to-go hasn't updated yet -- keep waiting

        int down = _buffer.PendingDown!.Value;
        int baselineYardsToGo = _buffer.BaselineYardsToGo;
        _buffer.Clear();

        if (down != 3)
            return null;

        // A loss (yards-to-go went UP) is DefenseHelper's/TflHelper's territory, not this cue.
        if (state.Current.YardsToGo > baselineYardsToGo)
            return null;

        bool isShort = state.Current.YardsToGo <= 5;

        return new TriggerEvent
        {
            EventKey = isShort ? "Defense: Third Down Short" : "Defense: Third Down",
            Volume = 100,
            IsEarnedBigEvent = isShort ? false : state.Current.BigGame
        };
    }
}
