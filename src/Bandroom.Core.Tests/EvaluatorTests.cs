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
    public void BigEventHelper_FourthDownLoss_FiresWithinBufferWindow()
    {
        var evaluator = new BigEventHelper();
        // Tick 1: down changes 3 -> 4, YardsToGo baseline latched from Previous (5).
        var t1 = State(Snap.With(down: 3, yardsToGo: 5), Snap.With(down: 4, yardsToGo: 5));
        Assert.Null(evaluator.Evaluate(t1));

        // Tick 2: down unchanged, but YardsToGo OCR catches up to a higher value (a loss).
        var t2 = State(Snap.With(down: 4, yardsToGo: 5), Snap.With(down: 4, yardsToGo: 12));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Fourth Down (Loss)", result!.EventKey);
    }

    [Fact]
    public void BigEventHelper_FourthDownLoss_TimesOutWithoutQualifyingChange_NoFireAndNearMissLogged()
    {
        var evaluator = new BigEventHelper();
        var down = Snap.With(down: 3, yardsToGo: 5);
        var t1 = State(down, Snap.With(down: 4, yardsToGo: 5));
        evaluator.Evaluate(t1);

        // YardsToGo never increases across the whole confirmation window -- MaxPendingTicks
        // default is 3, so 4 more same-down ticks exhausts it.
        GameState? last = null;
        for (int i = 0; i < 4; i++)
        {
            last = State(Snap.With(down: 4, yardsToGo: 5), Snap.With(down: 4, yardsToGo: 5));
            var result = evaluator.Evaluate(last);
            Assert.Null(result);
        }
        Assert.Contains(last!.NearMisses, m => m.Contains("BigEventHelper") && m.Contains("4th-down"));
    }

    // ---------- DefenseHelper ----------

    [Fact]
    public void DefenseHelper_ThirdDownLoss_FiresWhenNotUserPossession()
    {
        var evaluator = new DefenseHelper();
        var prev = Snap.With(down: 2, yardsToGo: 5, possessionAway: true);
        var cur = Snap.With(down: 3, yardsToGo: 5, possessionAway: true);
        Assert.Null(evaluator.Evaluate(State(prev, cur, userIsHome: true))); // buffering, waiting

        var next = State(Snap.With(down: 3, yardsToGo: 5, possessionAway: true),
                          Snap.With(down: 3, yardsToGo: 9, possessionAway: true), userIsHome: true);
        var result = evaluator.Evaluate(next);
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down (Loss)", result!.EventKey);
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
    public void TflHelper_TimesOut_NotALoss_NearMissLogged_NoFire()
    {
        var evaluator = new TflHelper();
        var t1 = State(Snap.With(down: 1, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 10));
        evaluator.Evaluate(t1);

        GameState? last = null;
        for (int i = 0; i < 4; i++)
        {
            last = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 2, yardsToGo: 10));
            Assert.Null(evaluator.Evaluate(last));
        }
        Assert.Contains(last!.NearMisses, m => m.Contains("TflHelper"));
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
    public void OffenseDownHelper_LongThirdDown_FiresDefenseKey()
    {
        var evaluator = new OffenseDownHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 8));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down", result!.EventKey);
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

    // ---------- DefenseThirdDownShortHelper ----------

    [Fact]
    public void DefenseThirdDownShortHelper_FiresAlongsideOffenseVariant()
    {
        var evaluator = new DefenseThirdDownShortHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 2));
        var result = evaluator.Evaluate(t2);
        Assert.NotNull(result);
        Assert.Equal("Defense: Third Down Short", result!.EventKey);
    }

    [Fact]
    public void DefenseThirdDownShortHelper_DoesNotFire_OnLongThirdDown()
    {
        var evaluator = new DefenseThirdDownShortHelper();
        var t1 = State(Snap.With(down: 2, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 10));
        Assert.Null(evaluator.Evaluate(t1));

        var t2 = State(Snap.With(down: 3, yardsToGo: 10), Snap.With(down: 3, yardsToGo: 8));
        Assert.Null(evaluator.Evaluate(t2));
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
        var state = State(Snap.With(down: 3, yardsToGo: 2), Snap.With(down: 1, yardsToGo: 10));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Offense: Earned First Down", result!.EventKey);
    }

    [Fact]
    public void FirstDownHelper_DoesNotFire_OnTurnoverDrivenDownReset()
    {
        var evaluator = new FirstDownHelper();
        var state = State(Snap.With(down: 3, yardsToGo: 2, possessionAway: false),
                           Snap.With(down: 1, yardsToGo: 10, possessionAway: true));
        Assert.Null(evaluator.Evaluate(state));
    }

    // ---------- TouchdownHelper ----------

    [Fact]
    public void TouchdownHelper_Fires_OffenseTouchdown()
    {
        var evaluator = new TouchdownHelper();
        var state = State(Snap.With(isTouchdown: false), Snap.With(isTouchdown: true));
        var result = evaluator.Evaluate(state);
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

    // ---------- TurnoverHelper ----------

    [Fact]
    public void TurnoverHelper_Fires_TurnoverForced()
    {
        var evaluator = new TurnoverHelper();
        var state = State(Snap.With(isTurnover: false), Snap.With(isTurnover: true, quarter: 2, timeRemainingSeconds: 600));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Turnover Forced", result!.EventKey);
    }

    [Fact]
    public void TurnoverHelper_Fires_IcedGameVariant_LateFourthQuarter()
    {
        var evaluator = new TurnoverHelper();
        var state = State(Snap.With(isTurnover: false), Snap.With(isTurnover: true, quarter: 4, timeRemainingSeconds: 60));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: Iced Game by Turnover", result!.EventKey);
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
                           Snap.With(possessionAway: true, timeRemainingSeconds: 195, awayTimeoutsRemaining: 2));
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
            Current = Snap.With(possessionAway: true, timeRemainingSeconds: 195, awayTimeoutsRemaining: 2),
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
        var state = State(Snap.With(isPregameReady: false), Snap.With(isPregameReady: true));
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Other: Pregame Ready", result!.EventKey);
    }

    [Fact]
    public void PregameHelper_DoesNotRefire_WhileStillReady()
    {
        var evaluator = new PregameHelper();
        var state = State(Snap.With(isPregameReady: true), Snap.With(isPregameReady: true));
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
        Assert.Equal("Defense: Drive Starter", result!.EventKey);
    }

    [Fact]
    public void DriveStarterHelper_DoesNotFire_OnKickoff()
    {
        var evaluator = new DriveStarterHelper();
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

    // ---------- NoPuntReturnHelper ----------

    [Fact]
    public void NoPuntReturnHelper_Fires_ForDefenseOnEdge()
    {
        var evaluator = new NoPuntReturnHelper();
        var state = State(Snap.With(isNoPuntReturn: false, possessionAway: true),
                           Snap.With(isNoPuntReturn: true, possessionAway: true), userIsHome: true);
        var result = evaluator.Evaluate(state);
        Assert.NotNull(result);
        Assert.Equal("Defense: No Punt Return", result!.EventKey);
    }

    [Fact]
    public void NoPuntReturnHelper_DoesNotFire_WhenUserHasPossession()
    {
        var evaluator = new NoPuntReturnHelper();
        var state = State(Snap.With(isNoPuntReturn: false, possessionAway: false),
                           Snap.With(isNoPuntReturn: true, possessionAway: false), userIsHome: true);
        Assert.Null(evaluator.Evaluate(state));
    }

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
        Assert.Equal("Defense: First Down", result!.EventKey);
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
