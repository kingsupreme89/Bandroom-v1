namespace Bandroom.Core;

public sealed class PlaySnapshot
{
    public int Down { get; init; }
    public int YardsToGo { get; init; }
    public int YardLine { get; init; }
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public int Quarter { get; init; }
    public int TimeRemainingSeconds { get; init; }
    public int AwayTimeoutsRemaining { get; init; }
    public bool BigGame { get; init; }
    public bool PossessionAway { get; init; }
    public bool IsKickoff { get; init; }
    public bool IsPAT { get; init; }
    public bool IsPenaltyOnOffense { get; init; }
    public bool IsPenaltyOnDefense { get; init; }
    public bool IsTouchdown { get; init; }
    public bool IsTurnover { get; init; }
    public bool IsNoPuntReturn { get; init; }
}
