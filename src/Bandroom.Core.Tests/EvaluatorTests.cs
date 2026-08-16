using Bandroom.Core;
using Bandroom.Core.Helpers;
using Xunit;

namespace Bandroom.Core.Tests;

/// <summary>Regression coverage for the 19 IRuleEvaluator implementations in
/// src/Bandroom.Core/Helpers -- one representative "normal fire" scenario per evaluator plus at
/// least one guard/edge case that should suppress it, per the 2026-08-11 audit's item #6. The
/// buffered-tick evaluators from item #1 (BigEventHelper, DefenseHelper, TflHelper,
/// OffenseDownHelper, DefenseThirdDownShortHelper) get the deepest coverage since they're the
/// most fragile (multi-tick state machines, not single-tick pure functions) -- including the
/// DownDistanceBuffer sanity-bound guard from item #2 and the near-miss ghost-log from item #5.
///
/// Pattern: build GameState(Previous, Current, UserIsHome) pairs directly rather than going
/// through GameWatcher.RouteEngineTick (that method also does OCR/region bookkeeping this project
/// doesn't reference) -- this is exactly what RouteEngineTick itself does at the point it
/// constructs `state` right before calling _eventRouter.Route, just without the OCR machinery
/// feeding PlaySnapshot.</summary>
public class EvaluatorTests
{
    static GameState State(PlaySnapshot previous, PlaySnapshot current, bool userIsHome = true) =>
        new() { Previous = previous, Current = current, UserIsHome = userIsHome };

    // ---------- BigEventHelper ----------

    [Fact]
    public void BigEventHelper_ThirdDown_NewPossession_Fires()
    {
        var prev = Snap.With(down: 3, possessionAway: false);
        var cur = Snap.With(down: 3, possessionAway: true);
        var evaluator = new BigEventHelper();
        var result = evaluator.Evaluate(State(prev, cur));
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down", result!.EventKey);
    }

    [Fact]
    public void BigEventHelper_ThirdDown_NewPossession_ButTurnover_DoesNotFire()
    {
        var prev = Snap.With(down: 3, possessionAway: false);
        var cur = Snap.With(down: 3, possessionAway: true, isTurnover: true);
        var evaluator = new BigEventHelper();
        var result = evaluator.Evaluate(State(prev, cur));
        Assert.Null(result);
    }

    [Fact]
    public void BigEventHelper_FourthDown_NewPossession_Fires_FourthDownStopKey()
    {
        // Added 2026-08-13 (owner report, real game log): this used to share the same
        // "Defense: Fourth Down" key OffenseDownHelper fires just for FACING a 4th down, causing
        // two evaluators to fire the identical key/song at two different real moments a few ticks
        // apart -- reported live as "4th Down (Home BG) fired twice at the same timestamp, no
        // penalty around it." Split into its own "Defense: Fourth Down Stop" key (see
        // BigEventHelper.cs's own comment on the split).
        var prev = Snap.With(down: 4, possessionAway: false);
        var cur = Snap.With(down: 4, possessionAway: true);
        var evaluator = new BigEventHelper();
        var result = evaluator.Evaluate(State(prev, cur));
        Assert.NotNull(result);
        Assert.Equal("Defense: Fourth Down Stop", result!.EventKey);
    }

    [Fact]
    public void BigEventHelper_FourthDownLoss_NoLongerFiresOwnKey_RetiredInFavorOfTflAndFourthDownStop()
    {
        // Retired 2026-08-11 (owner audit call, same reasoning as Third Down (Loss) below):
        // "Defense: Fourth Down (Loss)" merged into the generic "Defense: Tackle for Loss" cue
        // (TflHelper) plus the plain "Defense: Fourth Down" stop cue instead of having its own
        // key -- BigEventHelper's buffered down==4 Loss branch is gone entirely now.
        var evaluator = new BigEventHelper();
        var t1 = State(Snap.With(down: 3, yardsToGo: 5), Snap.With(down: 4, yardsToGo: 5));
        Assert.Null(evaluator.Evaluate(t1));
        var t2 = State(Snap.With(down: 4, yardsToGo: 5), Snap.With(down: 4, yardsToGo: 12));
        Assert.Null(evaluator.Evaluate(t2));
    }

    // ---------- DefenseHelper ----------

    [Fact]
    public void DefenseHelper_ThirdDownLoss_NoLongerFiresOwnKey_RetiredInFavorOfTflHelper()
    {
        // Retired 2026-08-11 (owner audit call): "Defense: Third Down (Loss)" merged into the
        // generic "Defense: Tackle for Loss" cue (TflHelper) instead of having its own key --
        // DefenseHelper's down==3 branch no longer returns anything for this case.
        var evaluator = new DefenseHelper();
        var prev = Snap.With(down: 2, yardsToGo: 5, possessionAway: true);
        var cur = Snap.With(down: 3, yardsToGo: 5, possessionAway: true);
        Assert.Null(evaluator.Evaluate(State(prev, cur, userIsHome: true))); // buffering, waiting

        var next = State(Snap.With(down: 3, yardsToGo: 5, possessionAway: true),
                          Snap.With(down: 3, yardsToGo: 9, possessionAway: true), userIsHome: true);
        Assert.Null(evaluator.Evaluate(next));
    }

    [Fact]
    public void DefenseHelper_CanFire_False_WhenUserHasPossession()
    {
        // UserIsHome=true, PossessionAway=false => user has the ball => defense evaluator should
        // not even run.
        var state = State(Snap.With(down: 2, possessionAway: false), Snap.With(down: 3, possessionAway: false), userIsHome: true);
        var evaluator = new DefenseHelper();
        Assert.False(evaluator.CanFire(state));
    }

    // ---------- TflHelper ----------

    [Fact]
    public void TflHelper_TackleForLoss_FiresOnDownAdvanceWithYardsIncrease()
    {
        var evaluator = new TflHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 14));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Tackle for Loss", result!.EventKey);
    }

    [Fact]
    public void TflHelper_FiresTackleForLoss_OwnerCall_SameTeamRecoveredFumbleWithLoss()
    {
        // Owner report, live big game: away fumbled, recovered by their OWN offense, 1st & 10 ->
        // 2nd & 23 (no possession change). TurnoverHelper no longer fires "Turnover Forced" for
        // this (see its own 2026-08-11 fix, requires an actual possession switch) -- this generic
        // loss-detection cue is what should fire instead, doubling as the fumble cue so no
        // separate "Fumble" card is needed.
        var evaluator = new TflHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 10, possessionAway: true),
                        Snap.With(down: 2, yardsToGo: 10, possessionAway: true));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 2, yardsToGo: 10, possessionAway: true),
                        Snap.With(down: 2, yardsToGo: 23, possessionAway: true));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Tackle for Loss", result!.EventKey);
    }

    [Fact]
    public void TflHelper_TimesOut_NotALoss_NearMissLogged_NoFire()
    {
        var evaluator = new TflHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 10));
        evaluator.Evaluate(t1);

        GameState? last = null;
        for (int i = 0; i < 3; i++)
        {
            last = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 10));
            Assert.Null(evaluator.Evaluate(last));
        }
        Assert.Contains(last!.NearMisses, m => m.Contains("TflHelper"));
    }

    [Fact]
    public void TflHelper_SuppressedOnFourthDown_OwnerCall_FourthDownCueOverridesIt()
    {
        // Owner call 2026-08-11: a 3rd-down loss that pushes the offense to 4th down should NOT
        // also play the generic Tackle for Loss cue -- BigEventHelper's "Defense: Fourth Down"
        // cue is about to cover the more important moment when that 4th-down snap resolves.
        var evaluator = new TflHelper();
        var t1 = State(Snap.With(down: 3, yardsToGo: 6), Snap.With(down: 4, yardsToGo: 6));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 4, yardsToGo: 6), Snap.With(down: 4, yardsToGo: 12));
        Assert.Null(evaluator.Evaluate(t2));
    }

    [Fact]
    public void TflHelper_DoesNotStartBuffer_OnDownReset_OrTurnover()
    {
        var evaluator = new TflHelper();
        // Down reset to 1 (e.g. conversion) should not be treated as an advance.
        var t1 = State(Snap.With(down: 3, yardsToGo: 6), Snap.With(down: 1, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));
        // Follow-up tick with no further down change should still be a no-op (never started).
        var t2 = State(Snap.With(down: 1, yardsToGo: 10), Snap.With(down: 1, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t2));
    }

    // ---------- OffenseDownHelper ----------

    [Fact]
    public void OffenseDownHelper_ShortSecondDown_FiresOffenseKey()
    {
        var evaluator = new OffenseDownHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 3));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Offense: Second Down Short", result!.EventKey);
    }

    [Fact]
    public void OffenseDownHelper_LongThirdDown_FiresOffenseThirdDownAt60()
    {
        // Updated 2026-08-11 (owner, live game): 3rd & long now gets the same balanced dual-fire
        // treatment as 3rd & short -- DefenseThirdDownHelper fires "Defense: Third Down" at 100
        // (see that class's own tests below), and this helper now ALSO fires "Offense: Third
        // Down" at 60 on the same long snap, instead of staying silent.
        var evaluator = new OffenseDownHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 8));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Offense: Third Down", result!.EventKey);
        Assert.Equal(60, result.Volume);
    }

    // ---------- DefenseThirdDownHelper ----------

    [Fact]
    public void DefenseThirdDownHelper_FiresOnLongThirdDown()
    {
        var evaluator = new DefenseThirdDownHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 8));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down", result!.EventKey);
    }

    [Fact]
    public void DefenseThirdDownHelper_FiresShortVariant_OnShortThirdDown()
    {
        // 2026-08-15: this used to assert "Defense: Third Down" fires on EVERY distance including
        // short -- that was the exact double-fire bug (this key AND the separate ...Short helper's
        // key both firing for one short 3rd down). Now merged into one evaluator/one decision: a
        // short 3rd down emits ONLY "Defense: Third Down Short", never the long key too.
        var evaluator = new DefenseThirdDownHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 3));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 3), Snap.With(down: 3, yardsToGo: 2));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down Short", result!.EventKey);
    }

    [Fact]
    public void DefenseThirdDownHelper_DefersToLossBranch_WhenYardsToGoIncreased()
    {
        var evaluator = new DefenseThirdDownHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 15));
        Assert.Null(evaluator.Evaluate(t2));
    }

    [Fact]
    public void OffenseDownHelper_DefersToLossBranch_WhenYardsToGoIncreased()
    {
        var evaluator = new OffenseDownHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 15));
        Assert.Null(evaluator.Evaluate(t2)); // a loss -- DefenseHelper's Loss branch owns this, not this evaluator
    }

    [Fact]
    public void OffenseDownHelper_BigGame_DoesNotDeferOnLoss_FiresLongCueAlongsideTfl()
    {
        // Owner call 2026-08-11 (live big game): unlike the ordinary-game case above, a big-game
        // 2nd/3rd & long still deserves the long-yardage hype cue even when the long distance came
        // from a loss -- fires alongside (not instead of) TflHelper's "Tackle for Loss" cue.
        var evaluator = new OffenseDownHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 10, bigGame: true), Snap.With(down: 2, yardsToGo: 10, bigGame: true));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 2, yardsToGo: 10, bigGame: true), Snap.With(down: 2, yardsToGo: 15, bigGame: true));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Second Down", result!.EventKey);
    }

    [Fact]
    public void OffenseDownHelper_BigGame_ShortYardageAfterLoss_StillFiresOffenseCue()
    {
        // Owner call 2026-08-11: a small loss that still nets short yardage (e.g. 2nd & 5) keeps
        // firing the Offense cue regardless of BigGame -- only the long-yardage classification
        // changes between BigGame and not.
        var evaluator = new OffenseDownHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 2, bigGame: true), Snap.With(down: 2, yardsToGo: 2, bigGame: true));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 2, yardsToGo: 2, bigGame: true), Snap.With(down: 2, yardsToGo: 5, bigGame: true));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Offense: Second Down Short", result!.EventKey);
    }

    [Fact]
    public void OffenseDownHelper_NewPossession_ClearsPending_DoesNotFire()
    {
        var evaluator = new OffenseDownHelper();
        var t1 = State(Snap.With(down: 3, yardsToGo: 10, possessionAway: false),
                        Snap.With(down: 1, yardsToGo: 10, possessionAway: true));
        Assert.Null(evaluator.Evaluate(t1));
        var t2 = State(Snap.With(down: 1, yardsToGo: 10, possessionAway: true),
                        Snap.With(down: 1, yardsToGo: 10, possessionAway: true));
        Assert.Null(evaluator.Evaluate(t2));
    }

    // ---------- DefenseThirdDownHelper (merged with the former DefenseThirdDownShortHelper
    // 2026-08-15 -- see that class's own doc comment: two separate buffer instances tracking the
    // same down transition could resolve on different ticks with different YardsToGo readings and
    // double-fire for one physical play, so short/long are now one evaluator's single decision) ----------

    [Fact]
    public void DefenseThirdDownHelper_FiresShort_AlongsideOffenseVariant()
    {
        var evaluator = new DefenseThirdDownHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 2));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down Short", result!.EventKey);
    }

    [Fact]
    public void DefenseThirdDownHelper_FiresLong_NotShort_OnLongThirdDown()
    {
        var evaluator = new DefenseThirdDownHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 8));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down", result!.EventKey);
    }

    // ---------- DownDistanceBuffer sanity bound (item #2) ----------

    [Fact]
    public void DownDistanceBuffer_RejectsImplausibleBaseline_TreatsAsNotPending()
    {
        var buffer = new DownDistanceBuffer();
        buffer.Start(down: 3, baselineYardsToGo: -500); // corrupt single-tick OCR misread
        Assert.False(buffer.IsPending);
    }

    [Fact]
    public void DownDistanceBuffer_AcceptsPlausibleBaseline()
    {
        var buffer = new DownDistanceBuffer();
        buffer.Start(down: 3, baselineYardsToGo: 7);
        Assert.True(buffer.IsPending);
        Assert.Equal(7, buffer.BaselineYardsToGo);
    }

    // ---------- FirstDownHelper ----------

    [Fact]
    public void FirstDownHelper_Fires_OnFreshFirstDown()
    {
        var evaluator = new FirstDownHelper();
        var state = State(Snap.With(down: 2, yardsToGo: 2), Snap.With(down: 1, yardsToGo: 10));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Offense: Earned First Down", result!.EventKey);
    }

    [Fact]
    public void FirstDownHelper_DoesNotFire_OnThirdDownConversion()
    {
        // Owner rule 2026-08-11: converting 3rd down specifically is ThirdDownConversionHelper's
        // own moment ("Offense: 3rd Down Conversion") -- this generic cue no longer stacks
        // alongside it on the same tick.
        var evaluator = new FirstDownHelper();
        var state = State(Snap.With(down: 3, yardsToGo: 2), Snap.With(down: 1, yardsToGo: 10));
        var result = evaluator.Evaluate(state);
        Assert.Null(result);
    }

    [Fact]
    public void FirstDownHelper_DoesNotFire_OnTurnoverDrivenDownReset()
    {
        var evaluator = new FirstDownHelper();
        var state = State(Snap.With(down: 3, yardsToGo: 2, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: true));
        Assert.Null(evaluator.Evaluate(state));
    }

    [Fact]
    public void FirstDownHelper_FourthDownBuffer_TimesOut_SameSideStillPossesses_Fires()
    {
        // A genuine 4th-down conversion: Down stays ambiguous long enough that the buffer times
        // out, but the SAME side still has the ball the whole time (PossessionAway never flips) --
        // should fire as a real earned first down once the buffer gives up waiting for a
        // possession change that was never coming.
        var evaluator = new FirstDownHelper();
        var start = State(Snap.With(down: 4, yardsToGo: 2, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: false));
        Assert.Null(evaluator.Evaluate(start)); // buffering

        TriggerEvent? result = null;
        for (int i = 0; i < FirstDownHelperMaxPendingTicks; i++)
        {
            var tick = State(Snap.With(down: 1, yardsToGo: 10, possessionAway: false),
                              Snap.With(down: 1, yardsToGo: 10, possessionAway: false));
            result = evaluator.Evaluate(tick);
        }
        Assert.NotNull(result);
        Assert.Equal("Offense: Earned First Down", result!.EventKey);
    }

    [Fact]
    public void FirstDownHelper_FourthDownBuffer_TimesOut_ButPossessionAlreadyFlipped_DoesNotFire()
    {
        // FIXED 2026-08-13 (live bug, real game log): a punt/turnover-on-downs whose single-tick
        // NewPossession edge got missed by OCR (a real return can easily outlast the buffer
        // window) used to fall through the timeout and fire a false "Offense: Earned First Down"
        // for the team that actually just lost the ball -- reported live as two "Earned First
        // Down" events, opposite sides, ~4 seconds apart, for what was really one punt. Now checks
        // Current.PossessionAway against the side captured when buffering started; if it no longer
        // matches (even without the edge itself ever being observed), abandon instead of guessing.
        var evaluator = new FirstDownHelper();
        var start = State(Snap.With(down: 4, yardsToGo: 2, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: false));
        Assert.Null(evaluator.Evaluate(start)); // buffering, side starts as "home has the ball"

        for (int i = 0; i < FirstDownHelperMaxPendingTicks; i++)
        {
            // Possession quietly reads as flipped every tick (no edge on any single tick since it
            // was already "away" the very next read) -- Delta.NewPossession never fires, but
            // Current.PossessionAway no longer matches what it was at buffer-start.
            var tick = State(Snap.With(down: 1, yardsToGo: 10, possessionAway: true),
                              Snap.With(down: 1, yardsToGo: 10, possessionAway: true));
            Assert.Null(evaluator.Evaluate(tick)); // includes the final timed-out tick -- still null
        }
    }

    const int FirstDownHelperMaxPendingTicks = 7;

    // ---------- TouchdownHelper ----------

    [Fact]
    public void TouchdownHelper_Fires_OffenseTouchdown()
    {
        // FIXED 2026-08-12 (stale test, went red after the defense-TD race-condition rewrite in
        // TouchdownHelper.cs the same day): that rewrite made an offense touchdown wait for the
        // scoreboard to move before firing, specifically to rule out a defensive score whose banner
        // arrived early (see TouchdownHelper's class comment) -- a banner-only tick with no score
        // delta now buffers instead of firing immediately. This test never modeled a score change,
        // so it always hit the buffered path and got null on the first Evaluate call. Fixed by
        // giving the possessing side's score its own +7 on the same tick, matching the normal case
        // where the banner and scoreboard update together, so this still verifies the immediate-fire
        // path (see TouchdownHelper_Fires_OffenseTouchdown_AfterBufferedScoreConfirm below for the
        // delayed-scoreboard path this rewrite added).
        var evaluator = new TouchdownHelper();
        var state = State(
            Snap.With(isTouchdown: false, possessionAway: false, homeScore: 0, awayScore: 0),
            Snap.With(isTouchdown: true, possessionAway: false, homeScore: 7, awayScore: 0));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Offense: Touchdown Scored", result!.EventKey);
    }

    [Fact]
    public void TouchdownHelper_Fires_OffenseTouchdown_AfterBufferedScoreConfirm()
    {
        // Covers the buffered path directly: banner appears with no score movement yet (OCR gap),
        // then the scoreboard catches up a few ticks later -- see TouchdownHelper's class comment.
        var evaluator = new TouchdownHelper();
        var bannerState = State(
            Snap.With(isTouchdown: false, possessionAway: false, homeScore: 0, awayScore: 0),
            Snap.With(isTouchdown: true, possessionAway: false, homeScore: 0, awayScore: 0));
        Assert.Null(evaluator.Evaluate(bannerState));

        var scoreState = State(
            Snap.With(isTouchdown: true, possessionAway: false, homeScore: 0, awayScore: 0),
            Snap.With(isTouchdown: true, possessionAway: false, homeScore: 7, awayScore: 0));
        var result = evaluator.Evaluate(scoreState);
        Assert.NotNull(result);
        Assert.Equal("Offense: Touchdown Scored", result!.EventKey);
    }

    [Fact]
    public void TouchdownHelper_DoesNotRefire_WhileStillTouchdown()
    {
        var evaluator = new TouchdownHelper();
        var state = State(Snap.With(isTouchdown: true), Snap.With(isTouchdown: true));
        Assert.Null(evaluator.Evaluate(state));
    }

    [Fact]
    public void TouchdownHelper_Fires_DefenseTouchdown_FromScoreDeltaAlone_NoBannerNeeded()
    {
        // CORRECTED 2026-08-11 (owner audit call): pick-six/fumble-return TD detected purely from
        // the scoreboard (away had the ball, home's score jumps by 6) -- IsTouchdown stays false
        // the whole time here, simulating the banner never getting caught by OCR.
        var evaluator = new TouchdownHelper();
        var state = State(
            Snap.With(isTouchdown: false, possessionAway: true, homeScore: 7, awayScore: 7),
            Snap.With(isTouchdown: false, possessionAway: true, homeScore: 13, awayScore: 7));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Touchdown Scored", result!.EventKey);
    }

    [Fact]
    public void TouchdownHelper_LateBanner_DoesNotDoubleFire_AfterScoreDeltaAlreadyAttributedIt()
    {
        var evaluator = new TouchdownHelper();
        var t1 = State(
            Snap.With(isTouchdown: false, possessionAway: true, homeScore: 7, awayScore: 7),
            Snap.With(isTouchdown: false, possessionAway: true, homeScore: 13, awayScore: 7));
        var r1 = evaluator.Evaluate(t1);
        Assert.Equal("Defense: Touchdown Scored", r1!.EventKey);

        // Banner finally catches up a tick later, same scoreboard total -- should NOT also fire
        // "Offense: Touchdown Scored" for the same points.
        var t2 = State(
            Snap.With(isTouchdown: false, possessionAway: true, homeScore: 13, awayScore: 7),
            Snap.With(isTouchdown: true, possessionAway: true, homeScore: 13, awayScore: 7));
        Assert.Null(evaluator.Evaluate(t2));
    }

    // ---------- TurnoverHelper ----------

    [Fact]
    public void TurnoverHelper_Fires_TurnoverForced()
    {
        var evaluator = new TurnoverHelper();
        var state = State(
            Snap.With(isTurnover: false, possessionAway: true),
            Snap.With(isTurnover: true, possessionAway: false, quarter: 2, timeRemainingSeconds: 600));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Turnover Forced", result!.EventKey);
    }

    [Fact]
    public void TurnoverHelper_DoesNotFire_WhenFumbleRecoveredBySameTeam_OwnerCall_NoPossessionSwitch()
    {
        // Owner report, live big game: away fumbled, recovered by their OWN offense (1st & 10 ->
        // 2nd & 23, no possession change) -- but this used to fire "Turnover Forced" anyway just
        // because the HUD briefly showed FUMBLE text, covering up the TFL cue that should've
        // played instead. IsTurnover is set from that on-screen text alone; possessionAway is
        // unchanged here (still Away both snapshots) so NewPossession is false.
        var evaluator = new TurnoverHelper();
        var state = State(
            Snap.With(isTurnover: false, possessionAway: true, down: 1, yardsToGo: 10),
            Snap.With(isTurnover: true, possessionAway: true, down: 2, yardsToGo: 23));
        var result = evaluator.Evaluate(state);
        Assert.Null(result);
    }

    [Fact]
    public void TurnoverHelper_Fires_IcedGameVariant_LateFourthQuarter_WhenNewPossessorIsWinning()
    {
        // CORRECTED 2026-08-11 (owner audit call): iced-game now requires the team that just took
        // over to actually be ahead on the scoreboard -- home leads 14-7 and just intercepted it
        // back (possession flips away -> home) with 60 seconds left in Q4.
        var evaluator = new TurnoverHelper();
        var state = State(
            Snap.With(isTurnover: false, possessionAway: true, homeScore: 14, awayScore: 7),
            Snap.With(isTurnover: true, possessionAway: false, homeScore: 14, awayScore: 7, quarter: 4, timeRemainingSeconds: 60));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Iced Game by Turnover", result!.EventKey);
    }

    [Fact]
    public void TurnoverHelper_Fires_TurnoverForced_NotIced_WhenNewPossessorIsStillLosing()
    {
        // Same late-4th-quarter turnover, but the team that just took over is STILL behind (7-14)
        // -- they forced a takeaway, but nothing is "sealed" yet since they still need to score.
        // Should fall through to the plain "Defense: Turnover Forced" cue instead.
        var evaluator = new TurnoverHelper();
        var state = State(
            Snap.With(isTurnover: false, possessionAway: false, homeScore: 14, awayScore: 7),
            Snap.With(isTurnover: true, possessionAway: true, homeScore: 14, awayScore: 7, quarter: 4, timeRemainingSeconds: 60));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Turnover Forced", result!.EventKey);
    }

    // ---------- SafetyHelper ----------

    [Fact]
    public void SafetyHelper_Fires_WhenPossessingTeamConcedesTwo()
    {
        var evaluator = new SafetyHelper();
        var state = State(Snap.With(possessionAway: false, homeScore: 0, awayScore: 0),
                           Snap.With(possessionAway: false, homeScore: 0, awayScore: 2));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Safety", result!.EventKey);
    }

    [Fact]
    public void SafetyHelper_DoesNotFire_OnUnrelatedTwoPointSwing()
    {
        // Home has the ball; away's score moving by 2 with home possessing is a safety (covered
        // above) -- here home itself gains 2, which isn't a real scoring value (should not fire).
        var evaluator = new SafetyHelper();
        var state = State(Snap.With(possessionAway: false, homeScore: 0, awayScore: 0),
                           Snap.With(possessionAway: false, homeScore: 2, awayScore: 0));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- KickoffHelper ----------

    [Fact]
    public void KickoffHelper_Fires_OpeningKickoff_OnceOnly()
    {
        var evaluator = new KickoffHelper();
        var t1 = State(Snap.With(isKickoff: false, quarter: 1), Snap.With(isKickoff: true, quarter: 1));
        var result = evaluator.Evaluate(t1);
        Assert.NotNull(result);
        Assert.Equal("Other: Opening Kickoff", result!.EventKey);

        // Same kickoff still showing on the next tick -- must not refire.
        var t2 = State(Snap.With(isKickoff: true, quarter: 1), Snap.With(isKickoff: true, quarter: 1));
        Assert.Null(evaluator.Evaluate(t2));
    }

    // ---------- TimeoutHelper ----------

    [Fact]
    public void TimeoutHelper_Fires_OnActualDecrement_WhenUserOnDefense()
    {
        var evaluator = new TimeoutHelper();
        var state = State(Snap.With(possessionAway: true, timeRemainingSeconds: 200, awayTimeoutsRemaining: 3),
                           Snap.With(possessionAway: true, timeRemainingSeconds: 195, awayTimeoutsRemaining: 2, isTimeout: true));
        // UserIsHome=true, PossessionAway=true => user does not have the ball => UserHasPossession=false.
        var result = evaluator.Evaluate(new GameState { Previous = state.Previous, Current = state.Current, UserIsHome = true });
        Assert.NotNull(result);
        Assert.Equal("Defense: Timeout (2 Remaining)", result!.EventKey);
    }

    [Fact]
    public void TimeoutHelper_DoesNotFire_WithoutADecrement()
    {
        var evaluator = new TimeoutHelper();
        var state = new GameState
        {
            Previous = Snap.With(possessionAway: true, timeRemainingSeconds: 200, awayTimeoutsRemaining: 2),
            Current = Snap.With(possessionAway: true, timeRemainingSeconds: 195, awayTimeoutsRemaining: 2, isTimeout: true),
            UserIsHome = true,
        };
        Assert.Null(evaluator.Evaluate(state));
    }

    [Fact]
    public void TimeoutHelper_Fires_OnHomeDecrement_WhenHomeHasBall()
    {
        // FIXED 2026-08-11 (owner report): a Home timeout used to never fire anything at all --
        // only AwayTimeoutsRemaining was ever tracked. Mirror of the Away-side test above:
        // UserIsHome=true, PossessionAway=false => UserHasPossession=true => Home branch checked.
        var evaluator = new TimeoutHelper();
        var state = new GameState
        {
            Previous = Snap.With(possessionAway: false, timeRemainingSeconds: 200, homeTimeoutsRemaining: 3),
            Current = Snap.With(possessionAway: false, timeRemainingSeconds: 195, homeTimeoutsRemaining: 2, isTimeout: true),
            UserIsHome = true,
        };
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Timeout (2 Remaining)", result!.EventKey);
    }

    [Fact]
    public void TimeoutHelper_DoesNotFire_OnHomeDecrement_WhenAwayHasBall()
    {
        // Home's count is only meaningful to react to while Home has the ball -- same convention
        // the pre-existing Away-side guard already used, just mirrored. A Home decrement noticed
        // while Away has the ball should not fire (matches the original design's restriction,
        // not a new one introduced by adding Home tracking).
        var evaluator = new TimeoutHelper();
        var state = new GameState
        {
            Previous = Snap.With(possessionAway: true, timeRemainingSeconds: 200, homeTimeoutsRemaining: 3, awayTimeoutsRemaining: 3),
            Current = Snap.With(possessionAway: true, timeRemainingSeconds: 195, homeTimeoutsRemaining: 2, awayTimeoutsRemaining: 3),
            UserIsHome = true,
        };
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- PenaltyHelper ----------

    [Fact]
    public void PenaltyHelper_Fires_OnOffensePenaltyEdge()
    {
        var evaluator = new PenaltyHelper();
        var state = State(Snap.With(isPenaltyOnOffense: false), Snap.With(isPenaltyOnOffense: true));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Penalty: Offense", result!.EventKey);
    }

    [Fact]
    public void PenaltyHelper_DoesNotRefire_WhileFlagStillSet()
    {
        var evaluator = new PenaltyHelper();
        var state = State(Snap.With(isPenaltyOnOffense: true), Snap.With(isPenaltyOnOffense: true));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- PregameHelper ----------

    [Fact]
    public void PregameHelper_Fires_OnReadyScreenEdge()
    {
        var evaluator = new PregameHelper();
        // quarter: 0 -- real pregame state (before kickoff); see PregameHelper's 2026-08-14 guard
        // against firing on a stray mid-game "READY" OCR sighting once the game clock has started.
        var state = State(Snap.With(isPregameReady: false, quarter: 0), Snap.With(isPregameReady: true, quarter: 0));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Other: Pregame Ready", result!.EventKey);
    }

    [Fact]
    public void PregameHelper_DoesNotRefire_WhileStillReady()
    {
        var evaluator = new PregameHelper();
        var state = State(Snap.With(isPregameReady: true, quarter: 0), Snap.With(isPregameReady: true, quarter: 0));
        Assert.Null(evaluator.Evaluate(state));
    }

    [Fact]
    public void PregameHelper_DoesNotFire_OnMidGameReadySighting()
    {
        // FIXED 2026-08-14 live bug: "Pregame Ready" fired during a live game (around a PAT) with
        // no actual READY screen up -- a stray "READY" OCR match mid-game flipped the edge trigger.
        var evaluator = new PregameHelper();
        var state = State(Snap.With(isPregameReady: false, quarter: 2), Snap.With(isPregameReady: true, quarter: 2));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- RunOutHelper ----------

    [Fact]
    public void RunOutHelper_Fires_OnFlagCardEdge()
    {
        var evaluator = new RunOutHelper();
        var state = State(Snap.With(isTeamRunOut: false), Snap.With(isTeamRunOut: true));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Other: Pregame Tunnel", result!.EventKey);
    }

    [Fact]
    public void RunOutHelper_DoesNotRefire_WhileStillShown()
    {
        var evaluator = new RunOutHelper();
        var state = State(Snap.With(isTeamRunOut: true), Snap.With(isTeamRunOut: true));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- DriveStarterHelper ----------

    [Fact]
    public void DriveStarterHelper_Fires_OnFreshPossession_NotKickoffOrTurnover()
    {
        var evaluator = new DriveStarterHelper();
        // Previous.Down must be <= 1 here (not >1) or Delta.WasFirstDown's own tie-break guard
        // (see DriveStarterHelper's own comment) intentionally defers to the earned-first-down
        // cue instead -- e.g. a punt: the receiving team's previous snap was their own 1st down,
        // then possession flips to the new team's fresh 1st & 10.
        var state = State(Snap.With(down: 1, yardsToGo: 5, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: true, isKickoff: false, isTurnover: false),
                           userIsHome: true);
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: After Punt", result!.EventKey);
    }

    [Fact]
    public void DriveStarterHelper_DoesNotFire_OnKickoff()
    {
        var evaluator = new DriveStarterHelper();
        var state = State(Snap.With(down: 0, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: true, isKickoff: true));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- OffenseAfterPuntHelper ----------

    [Fact]
    public void OffenseAfterPuntHelper_Fires_ForAwayTeamsOwnDriveStart()
    {
        // Added 2026-08-13 (owner report, real game log: "needed an event that was the away bg
        // off after punt"). DriveStarterHelper's own "Offense: Earned First Down" branch only
        // ever fires for the user's/home's own drive start -- this is the missing counterpart for
        // when it's the AWAY team's fresh drive (e.g. after receiving a punt), same tick as
        // DriveStarterHelper's own "Defense: After Punt" fire for the same real moment.
        var evaluator = new OffenseAfterPuntHelper();
        var state = State(Snap.With(down: 1, yardsToGo: 5, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: true, isKickoff: false, isTurnover: false),
                           userIsHome: true);
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Offense: Earned First Down", result!.EventKey);
    }

    [Fact]
    public void OffenseAfterPuntHelper_DoesNotFire_ForUsersOwnDriveStart()
    {
        // DriveStarterHelper's existing Offense branch already owns this moment -- avoids a
        // same-tick duplicate "Offense: Earned First Down" fire for the user's own side.
        var evaluator = new OffenseAfterPuntHelper();
        var state = State(Snap.With(down: 1, yardsToGo: 5, possessionAway: true),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: false, isKickoff: false, isTurnover: false),
                           userIsHome: true);
        Assert.Null(evaluator.Evaluate(state));
    }

    [Fact]
    public void OffenseAfterPuntHelper_DoesNotFire_OnKickoff()
    {
        var evaluator = new OffenseAfterPuntHelper();
        var state = State(Snap.With(down: 0, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: true, isKickoff: true));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- DownFieldPositionHelper ----------

    [Fact]
    public void DownFieldPositionHelper_Fires_SecondDownMidfield_Offense()
    {
        var evaluator = new DownFieldPositionHelper();
        var state = State(Snap.With(down: 1, yardLine: 45, possessionAway: false),
                           Snap.With(down: 2, yardLine: 45, possessionAway: false), userIsHome: true);
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Offense: Second Down (Midfield)", result!.EventKey);
    }

    [Fact]
    public void DownFieldPositionHelper_DoesNotFire_WhenYardLineUnavailable()
    {
        // YardLine == 0 means "not OCR'd yet" -- must stay dormant rather than treating 0 <= 50 as true.
        var evaluator = new DownFieldPositionHelper();
        var state = State(Snap.With(down: 1, yardLine: 0, possessionAway: false),
                           Snap.With(down: 2, yardLine: 0, possessionAway: false));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- FieldGoalMissedHelper ----------

    [Fact]
    public void FieldGoalMissedHelper_Fires_OnPossessionFlipWithNoScoreChange()
    {
        var evaluator = new FieldGoalMissedHelper();
        var state = State(Snap.With(isFieldGoalAttempt: true, possessionAway: false, homeScore: 0, awayScore: 0),
                           Snap.With(isFieldGoalAttempt: true, possessionAway: true, homeScore: 0, awayScore: 0));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Field Goal Missed by Opponent", result!.EventKey);
    }

    [Fact]
    public void FieldGoalMissedHelper_DoesNotFire_WhenKickWasGood()
    {
        var evaluator = new FieldGoalMissedHelper();
        var state = State(Snap.With(isFieldGoalAttempt: true, possessionAway: false, homeScore: 0, awayScore: 0),
                           Snap.With(isFieldGoalAttempt: true, possessionAway: true, homeScore: 3, awayScore: 0));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- FieldGoalPATHelper ----------

    [Fact]
    public void FieldGoalPATHelper_Fires_FieldGoalMade()
    {
        var evaluator = new FieldGoalPATHelper();
        var state = State(Snap.With(homeScore: 0, awayScore: 0), Snap.With(homeScore: 3, awayScore: 0));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Offense: Field Goal Made", result!.EventKey);
    }

    [Fact]
    public void FieldGoalPATHelper_DistinguishesSafetyFromTwoPointConversion()
    {
        // Away possesses; HOME gains 2 => that's a safety (the non-possessing side scored), not a
        // 2-point conversion by the possessing team -- must not fire here (SafetyHelper's job).
        var evaluator = new FieldGoalPATHelper();
        var state = State(Snap.With(possessionAway: true, homeScore: 0, awayScore: 0),
                           Snap.With(possessionAway: true, homeScore: 2, awayScore: 0));
        Assert.Null(evaluator.Evaluate(state));
    }

    // NoPuntReturnHelper retired 2026-08-11 (owner audit call) -- "Defense: No Punt Return" removed
    // entirely, no replacement cue requested.

    // ---------- GameStateEventHelper ----------

    [Fact]
    public void GameStateEventHelper_Fires_StartOfSecondQuarter()
    {
        var evaluator = new GameStateEventHelper();
        var state = State(Snap.With(quarter: 1, down: 2), Snap.With(quarter: 2, down: 2));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Other: Start of 2nd Quarter", result!.EventKey);
    }

    [Fact]
    public void GameStateEventHelper_DoesNotFireQuarterEvent_OnVeryFirstTick()
    {
        // Previous.Quarter == 0 means no real prior state -- must not treat as a "transition".
        var evaluator = new GameStateEventHelper();
        var state = State(Snap.With(quarter: 0, down: 0), Snap.With(quarter: 1, down: 1));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result); // this IS the pregame-take-the-field branch
        Assert.Equal("Other: Pregame Take the Field", result!.EventKey);
    }

    [Fact]
    public void GameStateEventHelper_PregameTakeTheField_OnlyFiresOnce_OwnerCall_BigGameOcrBlip()
    {
        // Owner report, live big game: "Pregame Take the Field" fired mid-game off an Away first
        // down. A big game's extra overlays can blank the scorebug for a tick, misreading
        // Quarter/Down back down to 0 -- the next tick where they resolve again then looks
        // identical to the one real pregame transition. Reuse the same evaluator instance (its
        // one-shot flag is per-instance, matching how one GameStateEventHelper lives for a whole
        // GAMETIME session) across both ticks.
        var evaluator = new GameStateEventHelper();
        var real = State(Snap.With(quarter: 0, down: 0), Snap.With(quarter: 1, down: 1));
        var result = evaluator.Evaluate(real);
        Assert.NotNull(result);
        Assert.Equal("Other: Pregame Take the Field", result!.EventKey);

        // Later in the game: an OCR blip reads Quarter/Down back down to 0 for one tick, then back
        // up -- structurally identical to the real pregame transition, must NOT refire.
        var blip = State(Snap.With(quarter: 0, down: 0, awayScore: 14), Snap.With(quarter: 1, down: 1, awayScore: 14));
        Assert.Null(evaluator.Evaluate(blip));
    }

    // ---------- DefenseFirstDownHelper ----------

    [Fact]
    public void DefenseFirstDownHelper_Fires_OnFirstSnapAfterKickoff()
    {
        var evaluator = new DefenseFirstDownHelper();
        var t1 = State(Snap.With(isKickoff: false, down: 0), Snap.With(isKickoff: true, down: 0));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(isKickoff: true, down: 0), Snap.With(isKickoff: false, down: 1));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: After Opening Kick", result!.EventKey);
        Assert.Equal(60, result.Volume); // ducked side of the 2026-08-12 dual-fire pairing
    }

    [Fact]
    public void DefenseFirstDownHelper_DoesNotFire_WhenSnapMissed()
    {
        var evaluator = new DefenseFirstDownHelper();
        var t1 = State(Snap.With(isKickoff: false, down: 0), Snap.With(isKickoff: true, down: 0));
        evaluator.Evaluate(t1);

        // Next non-kickoff tick shows down 2, not a fresh 1st -- the exact snap was missed.
        var t2 = State(Snap.With(isKickoff: true, down: 0), Snap.With(isKickoff: false, down: 2));
        Assert.Null(evaluator.Evaluate(t2));
    }

    // ---------- OffenseAfterOpeningKickHelper ----------

    [Fact]
    public void OffenseAfterOpeningKickHelper_Fires_OnFirstSnapAfterKickoff()
    {
        var evaluator = new OffenseAfterOpeningKickHelper();
        var t1 = State(Snap.With(isKickoff: false, down: 0), Snap.With(isKickoff: true, down: 0));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(isKickoff: true, down: 0), Snap.With(isKickoff: false, down: 1));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Offense: After Opening Kick", result!.EventKey);
        Assert.Equal(100, result.Volume); // loud side -- receiving team has the ball
    }

    [Fact]
    public void OffenseAfterOpeningKickHelper_DoesNotFire_WhenSnapMissed()
    {
        var evaluator = new OffenseAfterOpeningKickHelper();
        var t1 = State(Snap.With(isKickoff: false, down: 0), Snap.With(isKickoff: true, down: 0));
        evaluator.Evaluate(t1);

        var t2 = State(Snap.With(isKickoff: true, down: 0), Snap.With(isKickoff: false, down: 2));
        Assert.Null(evaluator.Evaluate(t2));
    }

    // ---------- EventRouter dedupe provenance (item #3) ----------

    [Fact]
    public void EventRouter_Dedupe_ReportsKeptAndDroppedEvaluatorNames()
    {
        var rules = new IRuleEvaluator[] { new AlwaysFireA(), new AlwaysFireB() };
        var router = new EventRouter(rules);
        var state = State(Snap.With(), Snap.With());

        string? droppedKey = null, droppedBy = null, keptBy = null;
        var results = router.Route(state, (dropped, droppedEvaluator, keptEvaluator) =>
        {
            droppedKey = dropped.EventKey;
            droppedBy = droppedEvaluator;
            keptBy = keptEvaluator;
        });

        Assert.Single(results);
        Assert.Equal("Other: Duplicate", results[0].EventKey);
        Assert.Equal("AlwaysFireA", results[0].SourceEvaluator);
        Assert.Equal("Other: Duplicate", droppedKey);
        Assert.Equal("AlwaysFireB", droppedBy);
        Assert.Equal("AlwaysFireA", keptBy);
    }

    sealed class AlwaysFireA : IRuleEvaluator
    {
        public TriggerEvent? Evaluate(GameState state) => new() { EventKey = "Other: Duplicate" };
    }

    sealed class AlwaysFireB : IRuleEvaluator
    {
        public TriggerEvent? Evaluate(GameState state) => new() { EventKey = "Other: Duplicate" };
    }
}
