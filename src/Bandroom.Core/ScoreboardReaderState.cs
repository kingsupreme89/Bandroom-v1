namespace Bandroom.Core;

/// <summary>Mirrors Coffee's Scorebug Overlay App's `live-scoreboard.json` shape exactly (RAM-reader
/// + OCR fallback, atomic .tmp-then-rename write). Deserialized as-is by ScoreboardJsonReader --
/// GameStateNormalizer is the only place that turns this into a PlaySnapshot, so this DTO stays a
/// pure mirror of the reader's schema rather than picking up any BANDroom-side interpretation.</summary>
public sealed class ScoreboardReaderState
{
    public ScoreboardReaderTeamState? Away { get; init; }
    public ScoreboardReaderTeamState? Home { get; init; }
    public ScoreboardReaderGameState? Game { get; init; }
    public ScoreboardReaderMeta? Meta { get; init; }
}

public sealed class ScoreboardReaderTeamState
{
    public string? Rank { get; init; }
    public string? Name { get; init; }
    public string? Nickname { get; init; }
    public string? Record { get; init; }
    public string? Color { get; init; }
    public int? Score { get; init; }
    public int? Timeouts { get; init; }
    public bool? Possession { get; init; }
}

public sealed class ScoreboardReaderGameState
{
    public int? Down { get; init; }
    /// <summary>Reader can send this as a number (10) or goal-line text ("Goal") -- kept as the
    /// raw JSON element text so GameStateNormalizer decides how to interpret it, same reasoning
    /// as GameWatcher's own NormalizeDistanceRaw for OCR'd distance text.</summary>
    public string? Distance { get; init; }
    public string? DownDistance { get; init; }
    public string? Quarter { get; init; }
    public string? Clock { get; init; }
    public int? PlayClock { get; init; }
    /// <summary>Yard line the ball is on, reader's own coordinate (not yet oriented to home/away
    /// side) -- see GameStateNormalizer for how this maps to PlaySnapshot.YardLine.</summary>
    public int? BallOn { get; init; }
    public string? Status { get; init; }
    /// <summary>"away" | "home" | "none".</summary>
    public string? Possession { get; init; }
}

public sealed class ScoreboardReaderMeta
{
    /// <summary>"ram" | "screen" -- which read path produced this snapshot.</summary>
    public string? Source { get; init; }
    public bool? Visible { get; init; }
    public double? Confidence { get; init; }
    public string? UpdatedAt { get; init; }
    public string? RamUpdatedAt { get; init; }
}

/// <summary>Reader's `score-change` event stream (separate small JSON, corroboration-only --
/// see A5 in the integration plan). Never used to fire an event flag by itself.</summary>
public sealed class ScoreboardReaderScoreChangeEvent
{
    public string? Side { get; init; }
    public int? NewScore { get; init; }
    public int? Delta { get; init; }
    /// <summary>"touchdown-candidate" | "field-goal-candidate" | "conversion-candidate".</summary>
    public string? LikelyType { get; init; }
    public string? Timestamp { get; init; }
}
