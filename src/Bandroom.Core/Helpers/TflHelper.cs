namespace Bandroom.Core.Helpers;

/// <summary>Fires a generic "Defense: Tackle for Loss" cue when a down advances to 3rd with
/// YardsToGo having increased -- 3rd down has no down-specific Loss cue of its own (see the
/// REMOVED 2026-08-11 comment below), so this generic cue is the only one that plays for it.
///
/// FIXED 2026-08-11 (audit finding): a prior "fix" excluded downs 2/3/4 entirely to stop this
/// evaluator from double-firing alongside DefenseHelper/BigEventHelper -- but since a down can
/// only ever advance INTO 2, 3, or 4 in normal play, that exclusion covered this evaluator's
/// entire reachable domain and made it permanently dead code (its EventKey stayed listed as an
/// assignable song card that could never actually play). Restored to fire on 3rd only (2nd/4th
/// stay excluded, see those branches below) -- EventRouter's same-tick dedupe only blocks
/// identical EventKeys, not different ones, so an overlap here would otherwise double-play,
/// not get deduped for free.
///
/// UN-RESTORED for Down==2 on 2026-08-19 (owner report, live game log: "Second Down (Loss)" and
/// "Tackle for Loss" both played back-to-back for the same snap): unlike Down==3 above, Down==2
/// never had its specific "Defense: Second Down (Loss)" cue retired -- DefenseHelper's own
/// down==2 branch fires on this exact same down-advance/YardsToGo-increase detection, so the two
/// were guaranteed to co-fire on every single 2nd-down loss, not an occasional overlap worth
/// living with. See the Down==2 guard below.
///
/// Buffered edge-detection -- same OCR split-tick fix as DefenseHelper/BigEventHelper/
/// OffenseDownHelper/DefenseThirdDownShortHelper (STATE_MACHINE_ANALYSIS Discrepancy #12):
/// "down" and "yards to go" are independent OCR reads that don't always land on the same tick,
/// so classifying a loss off Current.YardsToGo on the exact down-change tick can read a stale
/// value.
///
/// DOUBLES AS THE FUMBLE CUE (owner call 2026-08-11, live big game): a fumble the offense
/// recovers itself is just a down that advanced with a yardage loss -- exactly this evaluator's
/// existing detection, no separate "Fumble" event/card needed. TurnoverHelper now only fires
/// "Turnover Forced" when the fumble/interception ACTUALLY flips possession (see that file's own
/// 2026-08-11 fix); a same-team-recovered fumble falls through to here instead. Display label
/// updated to "Tackle for Loss / Fumble" (wwwroot/app.js) to reflect that -- EventKey string
/// itself ("Defense: Tackle for Loss") is unchanged so existing song assignments / default song
/// pack mappings keep working.</summary>
public sealed class TflHelper : IRuleEvaluator
{
    readonly DownDistanceBuffer _buffer = new();

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Previous.Down == 0 || state.Current.Down == 0)
        {
            _buffer.Clear();
            return null;
        }

        if (state.Current.Down != state.Previous.Down)
        {
            // A turnover also changes Down for the new offense -- that's TurnoverHelper's
            // moment, not a loss cue. A down RESETTING to 1 (conversion/turnover) isn't an
            // advance either. Same NewPossession guard DefenseHelper/BigEventHelper use.
            if (state.Delta.NewPossession || state.Current.Down <= state.Previous.Down)
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

        int baselineYardsToGo = _buffer.BaselineYardsToGo;
        _buffer.Clear();

        if (state.Current.YardsToGo <= baselineYardsToGo)
        {
            if (timedOut)
                state.NearMisses.Add("TflHelper: buffered wait timed out, no YardsToGo increase detected (not a Loss)");
            return null; // not actually a loss
        }

        // Owner call 2026-08-11: 4th down always overrides the generic Tackle for Loss cue -- a
        // loss that pushes the offense to 4th down is about to be followed by BigEventHelper's
        // "Defense: Fourth Down" cue when that 4th-down snap resolves, and that's the bigger,
        // more specific moment. Suppressing here rather than in BigEventHelper since the two
        // don't fire on the same tick (this fires immediately off the loss; "Defense: Fourth
        // Down" only fires once the 4th-down play itself ends the drive) -- the loss cue would
        // otherwise play first and get stepped on moments later by the more important one anyway.
        if (state.Current.Down == 4)
            return null;

        // Added 2026-08-19 (owner report, live game log: "Second Down (Loss)" and "Tackle for
        // Loss" both played back-to-back for the same snap): DefenseHelper fires its own
        // "Defense: Second Down (Loss)" off this exact same down==2/YardsToGo-increased
        // detection (see that file's down==2 branch) -- unlike down==3, which had its own
        // specific Loss cue retired in favor of this generic one (see this class's own doc
        // comment), down==2 never lost its specific cue, so the two have always been guaranteed
        // to co-fire on every 2nd-down loss. Same deferral shape as the Down==4 guard above:
        // the more specific cue wins, this generic one steps aside rather than stacking with it.
        if (state.Current.Down == 2)
            return null;

        return new TriggerEvent
        {
            EventKey = "Defense: Tackle for Loss",
            Volume = state.Current.BigGame ? 100 : 75,
            IsEarnedBigEvent = true
        };
    }
}
