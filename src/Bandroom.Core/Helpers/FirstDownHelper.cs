namespace Bandroom.Core.Helpers;

/// <summary>Fires earned-first-down events. The base event fires any time a new 1st down
/// is earned (not the opening kickoff's 1st & 10). REWRITTEN 2026-08-10 (owner's "gameplan
/// simplification" pass, same session as the 2nd/3rd down short/long split): used to split by
/// yards gained to earn the down (Big Gain 15+, else base). Replaced with a pure YardsToGo check
/// on the resulting first down, matching the 2nd/3rd down pattern -- 1st & 5-or-less credits a
/// new "Short" key, else plain "1st & 10". The old Big Gain branch is dropped entirely (owner's
/// explicit, accepted tradeoff): whatever used to trigger it now just falls through to plain
/// "1st & 10" -- there's no reliable signal left to keep it distinct from the new split.
///
/// FIXED 2026-08-11 (live bug: "Offense: Earned First Down" fired after a PUNT, not a real
/// conversion): Delta.NewPossession and Down are independently-timed OCR-derived signals -- same
/// root cause as GameWatcher's structural-turnover/_awaitingPostKickoffSnap fix and
/// DriveStarterHelper's kickoff-return fix, just never caught here because a punt isn't a
/// "kickoff." On the tick Down first resets to 1 after a 4th-down punt, Delta.NewPossession can
/// still read false (possession-side confirmation is now 2-tick-debounced, see GameWatcher.
/// ConfirmPossessionFlip, which widened this exact timing gap), so the plain !NewPossession guard
/// below passed when it shouldn't have. A genuine 4th-down CONVERSION (same team keeps the ball)
/// is a real earned first down and must still fire -- only Previous.Down==4 is ambiguous (could be
/// a conversion OR a punt/turnover-on-downs), so only that case buffers briefly for NewPossession
/// to resolve before deciding; every other down transition (2nd/3rd -> 1st) can't be a possession
/// change and still fires immediately. Mirrors DownDistanceBuffer's short-timeout-then-decide
/// shape used elsewhere in this file's sibling evaluators.</summary>
public sealed class FirstDownHelper : IRuleEvaluator
{
    bool _awaitingFourthDownPossessionConfirm;
    int _pendingTicksRemaining;
    // Captured the moment buffering starts (see the Previous.Down==4 branch below) -- the side
    // that had the ball facing that 4th down. Used as a same-side sanity check at timeout time,
    // see the FIXED 2026-08-13 comment on the timeout branch for why this exists.
    bool _possessionAwayAtBufferStart;
    // FIXED 2026-08-11 (live bug recurred: "Earned First Down" still fired after a punt): this
    // was 3 ticks (~750ms), way too short. GameWatcher's own possession commit isn't instant --
    // ConfirmPossessionFlip alone needs 2 consecutive matching ticks (~500ms), AND a flip is
    // dropped entirely if it lands inside GameWatcher.Cooldown's post-commit lockout window from
    // the prior possession event, which a punt's flip routinely does. So NewPossession was still
    // reading false when this buffer gave up and fired -- same false-positive, just delayed.
    // REBALANCED same day (owner call: 3s felt too slow) alongside trimming GameWatcher.Cooldown
    // itself from 2.0s to 1.2s -- MaxPendingTicks must stay in sync with THAT value's worst case
    // (~0.5s confirm + 1.2s cooldown + margin), not an independent number. 7 ticks (~1750ms)
    // covers it with the same safety margin as before, just shorter because the cooldown it's
    // covering for is shorter now.
    const int MaxPendingTicks = 7; // ~1750ms at the 250ms OCR poll interval

    // Always true while a buffered decision might still be pending -- Evaluate needs to keep
    // running on ticks where WasFirstDown is no longer true (Down hasn't changed further).
    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (_awaitingFourthDownPossessionConfirm)
        {
            if (state.Delta.NewPossession)
            {
                // Confirmed a real possession change -- this is DriveStarterHelper's moment
                // (it fires correctly once NewPossession resolves, regardless of WasFirstDown),
                // not an earned first down. Abandon the pending fire permanently.
                _awaitingFourthDownPossessionConfirm = false;
                return null;
            }
            if (--_pendingTicksRemaining > 0)
                return null; // keep waiting

            _awaitingFourthDownPossessionConfirm = false;

            // FIXED 2026-08-13 (live bug, real game log: "Earned First Down" fired for the WRONG
            // side ~4 seconds before DriveStarterHelper's own correct fire for the team that
            // actually took over -- two "Earned First Down" events, opposite sides, seconds apart,
            // for what was really one punt). Root cause: Delta.NewPossession is a single-tick edge
            // (Previous.PossessionAway != Current.PossessionAway on the SAME tick) -- if the OCR
            // possession-color read never lands on the exact tick the flip happens (a real punt
            // return can easily run past MaxPendingTicks' ~1.75-2.75s window, especially with
            // ConfirmPossessionFlip's own 2-tick debounce and GameWatcher.Cooldown's lockout
            // stacked on top), the edge is silently missed and this timeout assumed "no flip ever
            // happened" -- a genuine 4th-down conversion -- when the ball had actually changed
            // hands. Belt-and-suspenders fix: at timeout, also directly compare Current.
            // PossessionAway against the side captured when buffering started. If it still matches,
            // nobody's disputing this was a real conversion (fire as before). If it doesn't --
            // Current possession already reads as the OTHER side even though the single-tick edge
            // was never caught -- this was a punt/turnover-on-downs after all; abandon silently and
            // let DriveStarterHelper (which reads Current.PossessionAway directly, not an edge) own
            // that moment whenever it resolves, however many ticks that takes.
            if (state.Current.PossessionAway != _possessionAwayAtBufferStart)
                return null;

            return MakeFirstDownEvent(state);
        }

        // Must be a fresh first down (not the opening snap)
        if (!state.Delta.WasFirstDown || state.Previous.Down == 0)
            return null;

        // A turnover also resets Down to 1 for the new offense -- that's the start of a new
        // drive by possession change, not a conversion the offense "earned" mid-drive. Without
        // this guard, the tick after TurnoverHelper fires "Defense: Turnover Forced", the down
        // updating to 1st fires "Offense: Earned First Down" and (via interruptPrevious) cuts off
        // the turnover cue that was already playing -- reported live as "plays the 1st down sound
        // instead of the turnover forced sound." Same NewPossession guard DefenseHelper already
        // uses for its own analogous false-positive (STATE_MACHINE_ANALYSIS.md Discrepancy #4).
        if (state.Delta.NewPossession)
            return null;

        // Owner rule 2026-08-11: converting 3RD down specifically is ThirdDownConversionHelper's
        // own, more specific moment ("Offense: 3rd Down Conversion") -- this generic "Earned First
        // Down" cue used to fire ALONGSIDE it on the same tick (deliberately, per that file's own
        // doc comment), but the owner wants only the more specific cue to play here, not both
        // stacked together. 2nd-down conversions still fall through to the base event below
        // unchanged -- only Previous.Down == 3 defers.
        if (state.Previous.Down == 3)
            return null;

        // Ambiguous case: Down just reset to 1 immediately after a 4th down, but NewPossession
        // hasn't been confirmed yet THIS tick. Could be a genuine 4th-down conversion (fire) or a
        // punt/turnover-on-downs whose possession flip just hasn't caught up in the OCR pipeline
        // yet (defer -- DriveStarterHelper owns that moment once NewPossession resolves). Buffer
        // briefly instead of trusting this tick's possibly-stale NewPossession=false blindly.
        if (state.Previous.Down == 4)
        {
            _awaitingFourthDownPossessionConfirm = true;
            _pendingTicksRemaining = MaxPendingTicks;
            _possessionAwayAtBufferStart = state.Current.PossessionAway;
            return null;
        }

        return MakeFirstDownEvent(state);
    }

    static TriggerEvent MakeFirstDownEvent(GameState state)
    {
        // Short: 5 yards or less to go on the new set of downs (only really happens near the
        // goal line or after certain penalties -- most 1st downs are a plain 1st & 10).
        if (state.Current.YardsToGo <= 5)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Earned First Down Short",
                Volume = 90,
                IsEarnedBigEvent = false
            };
        }

        // Midfield: inside opponent territory (yard line <= 50). DISABLED 2026-08-07 --
        // YardLine is hardcoded to 0 everywhere (OCR for it was never built, see TASK_BOARD.md),
        // so "<= 50" was always true, meaning this branch fired on literally every first down
        // and the base event below could never be reached. Re-enable once YardLine reads a real
        // value; until then this must stay off rather than silently misfire on every down.
        // if (state.Current.YardLine <= 50)
        // {
        //     return new TriggerEvent
        //     {
        //         EventKey = "Offense: Earned First Down (Midfield)",
        //         Volume = 85,
        //         IsEarnedBigEvent = true
        //     };
        // }

        // Base first down
        return new TriggerEvent
        {
            EventKey = "Offense: Earned First Down",
            Volume = 80,
            IsEarnedBigEvent = false
        };
    }
}