using System.Reflection;
using Bandroom.Core;
using Xunit;

namespace Bandroom.Core.Tests;

/// <summary>State-machine audit finding #4 (2026-08-16): IRuleEvaluator.CanFire is a cheap
/// pre-check run before Evaluate on every tick -- when it returns false, Evaluate is skipped
/// entirely (see IRuleEvaluator's own doc comment). Nothing previously enforced that every
/// evaluator's CanFire stays a true superset of the conditions its own Evaluate checks; this
/// exact class of bug already broke TurnoverHelper once (2026-08-11: its doc comment said
/// "NewPossession" but the code didn't check it, so a real fix to Evaluate would have silently
/// never run had CanFire not happened to already cover it). Rather than trust each evaluator's own
/// doc comment to stay accurate, this asserts the invariant directly, generically, across every
/// concrete IRuleEvaluator in Bandroom.Core.Helpers: for a representative spread of GameStates,
/// CanFire(state) == false must imply Evaluate(state) == null.
///
/// This is NOT a substitute for each evaluator's own positive-case tests in EvaluatorTests.cs --
/// it only catches "CanFire silently disagrees with Evaluate," not "Evaluate is wrong."</summary>
public class EvaluatorInvariantTests
{
    static GameState State(PlaySnapshot previous, PlaySnapshot current, bool userIsHome = true) =>
        new() { Previous = previous, Current = current, UserIsHome = userIsHome };

    /// <summary>Every sealed, parameterless-constructible IRuleEvaluator in Bandroom.Core.Helpers
    /// -- found via reflection instead of a hand-maintained list, so a newly added evaluator is
    /// automatically covered instead of silently exempt until someone remembers to add it here
    /// (the same "hand-maintained list drifts" failure mode this codebase's own comments warn
    /// about repeatedly, e.g. EventRouter's fixed construction-order list).</summary>
    public static IEnumerable<object[]> AllEvaluators() =>
        typeof(IRuleEvaluator).Assembly.GetTypes()
            .Where(t => typeof(IRuleEvaluator).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => new object[] { (IRuleEvaluator)Activator.CreateInstance(t)! });

    /// <summary>A curated (not exhaustive) spread of PlaySnapshot pairs covering the dimensions
    /// every evaluator in this codebase actually branches on: down transitions 1-4, possession
    /// flips both directions, real/no turnover, penalties on offense/defense, kickoff/PAT/
    /// touchdown/timeout flags, pregame signals, and the late-game/winning-side combination
    /// TurnoverHelper's Iced-Game branch needs. Not a full cross product (that's combinatorially
    /// enormous and mostly redundant for this invariant) -- enough spread that a CanFire/Evaluate
    /// disagreement introduced anywhere plausible would be caught.</summary>
    public static IEnumerable<object[]> RepresentativeStates()
    {
        PlaySnapshot Base(int down = 1, int yardsToGo = 10, bool possessionAway = false,
            bool isTurnover = false, bool isPenaltyOnOffense = false, bool isPenaltyOnDefense = false,
            bool isKickoff = false, bool isPat = false, bool isTouchdown = false, bool isTimeout = false,
            bool isFieldGoalAttempt = false, bool isPregameReady = false, bool isTeamRunOut = false,
            int quarter = 1, int timeRemainingSeconds = 900, int homeScore = 0, int awayScore = 0) => new()
        {
            Down = down,
            YardsToGo = yardsToGo,
            YardLine = 50,
            PossessionAway = possessionAway,
            IsTurnover = isTurnover,
            IsPenaltyOnOffense = isPenaltyOnOffense,
            IsPenaltyOnDefense = isPenaltyOnDefense,
            IsKickoff = isKickoff,
            IsPAT = isPat,
            IsTouchdown = isTouchdown,
            IsTimeout = isTimeout,
            IsFieldGoalAttempt = isFieldGoalAttempt,
            IsPregameReady = isPregameReady,
            IsTeamRunOut = isTeamRunOut,
            Quarter = quarter,
            TimeRemainingSeconds = timeRemainingSeconds,
            HomeScore = homeScore,
            AwayScore = awayScore,
            AwayTimeoutsRemaining = 3,
            HomeTimeoutsRemaining = 3,
        };

        var snapshots = new List<PlaySnapshot>
        {
            Base(),
            Base(down: 0, quarter: 0), // pregame / never-resolved
            Base(down: 1, possessionAway: true),
            Base(down: 2, yardsToGo: 8),
            Base(down: 3, yardsToGo: 2),
            Base(down: 3, yardsToGo: 10),
            Base(down: 4),
            Base(down: 4, isFieldGoalAttempt: true),
            Base(isTurnover: true),
            Base(isPenaltyOnOffense: true),
            Base(isPenaltyOnDefense: true),
            Base(isKickoff: true),
            Base(isPat: true),
            Base(isTouchdown: true),
            Base(isTimeout: true),
            Base(isPregameReady: true),
            Base(isTeamRunOut: true),
            // Late-game, iced-game-shaped state (TurnoverHelper's broadest CanFire condition).
            Base(down: 4, quarter: 4, timeRemainingSeconds: 90, homeScore: 10, awayScore: 17, possessionAway: true),
        };

        // Pair every snapshot with itself (no delta) and with Base() as Previous (a delta from a
        // neutral starting point) -- covers both "nothing changed" (CanFire should mostly be
        // false) and "something changed" (CanFire should mostly be true) without a full N^2 sweep.
        foreach (var snap in snapshots)
        {
            yield return new object[] { State(snap, snap) };
            yield return new object[] { State(Base(), snap) };
            yield return new object[] { State(snap, Base()) };
        }
    }

    [Theory]
    [MemberData(nameof(AllEvaluators))]
    public void CanFire_False_Implies_Evaluate_Null_AllHelpers(IRuleEvaluator evaluator)
    {
        foreach (var stateArgs in RepresentativeStates())
        {
            var state = (GameState)stateArgs[0];
            if (!evaluator.CanFire(state))
            {
                var result = evaluator.Evaluate(state);
                Assert.True(result == null,
                    $"{evaluator.GetType().Name}.CanFire returned false but Evaluate still returned " +
                    $"'{result?.EventKey}' -- CanFire must be a superset of every condition Evaluate checks, " +
                    "otherwise a future Evaluate-only fix silently never runs (see this evaluator's CanFire " +
                    "doc comment, or IRuleEvaluator.cs's own warning).");
            }
        }
    }
}
