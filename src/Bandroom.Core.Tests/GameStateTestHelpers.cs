using Bandroom.Core;

namespace Bandroom.Core.Tests;

/// <summary>Builds a chain of GameState ticks the same way GameWatcher.RouteEngineTick does:
/// each tick's Previous is the prior tick's Current, and Current is a fresh PlaySnapshot. Tests
/// call Next(snapshot) to advance one OCR tick and get back the GameState an evaluator would see.</summary>
sealed class GameStateSequence
{
    PlaySnapshot _current;
    public bool UserIsHome { get; }

    public GameStateSequence(PlaySnapshot initial, bool userIsHome = true)
    {
        _current = initial;
        UserIsHome = userIsHome;
    }

    public GameState Next(PlaySnapshot next)
    {
        var state = new GameState
        {
            Previous = _current,
            Current = next,
            UserIsHome = UserIsHome,
        };
        _current = next;
        return state;
    }
}

static class Snap
{
    public static PlaySnapshot With(
        int down = 1,
        int yardsToGo = 10,
        int yardLine = 50,
        bool possessionAway = false,
        bool bigGame = false,
        bool isTurnover = false,
        bool isPenaltyOnOffense = false,
        bool isPenaltyOnDefense = false,
        int homeScore = 0,
        int awayScore = 0,
        bool isFieldGoalAttempt = false,
        int quarter = 1,
        int timeRemainingSeconds = 900,
        int awayTimeoutsRemaining = 3,
        int homeTimeoutsRemaining = 3,
        bool isKickoff = false,
        bool isPat = false,
        bool isTouchdown = false,
        bool isNoPuntReturn = false,
        bool isTimeout = false,
        bool isPregameReady = false,
        bool isTeamRunOut = false) => new PlaySnapshot
        {
            Down = down,
            YardsToGo = yardsToGo,
            YardLine = yardLine,
            PossessionAway = possessionAway,
            BigGame = bigGame,
            IsTurnover = isTurnover,
            IsPenaltyOnOffense = isPenaltyOnOffense,
            IsPenaltyOnDefense = isPenaltyOnDefense,
            HomeScore = homeScore,
            AwayScore = awayScore,
            IsFieldGoalAttempt = isFieldGoalAttempt,
            Quarter = quarter,
            TimeRemainingSeconds = timeRemainingSeconds,
            AwayTimeoutsRemaining = awayTimeoutsRemaining,
            HomeTimeoutsRemaining = homeTimeoutsRemaining,
            IsKickoff = isKickoff,
            IsPAT = isPat,
            IsTouchdown = isTouchdown,
            IsNoPuntReturn = isNoPuntReturn,
            IsTimeout = isTimeout,
            IsPregameReady = isPregameReady,
            IsTeamRunOut = isTeamRunOut,
        };
}
