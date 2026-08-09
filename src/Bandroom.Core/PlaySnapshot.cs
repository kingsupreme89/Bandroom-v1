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

    /// <summary>True while CFB27's pregame team-intro/"READY" screen is on screen (the screen
    /// shown right before kickoff where both teams' ratings badges and a center READY prompt
    /// are displayed). Detection must be team-color-independent -- see PregameHelper.cs and the
    /// "pregameready" WatchedRegion in GameWatcher.cs for why this can never be color-matched
    /// (that screen's panel colors are per-matchup team colors, e.g. red/blue for Ohio State/
    /// Michigan vs different colors for any other pairing).</summary>
    public bool IsPregameReady { get; init; }
}
