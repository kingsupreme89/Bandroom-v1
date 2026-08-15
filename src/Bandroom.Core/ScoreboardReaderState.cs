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
    /// <summary>Reader's own per-field "when did this actually last change" data (added in the
    /// reader's v1.4.9, `ram.freshness` in the raw JSON) -- ground truth from directly re-checking
    /// live memory on every publish, not an outside guess. Null when the connected reader predates
    /// v1.4.9 and never published this block; callers must treat that the same as "no freshness
    /// data available" (fall back to trusting the field, same as before this existed) rather than
    /// treating an absent block as "everything is stale."</summary>
    public ScoreboardReaderFreshness? Freshness { get; init; }
}

/// <summary>One field's freshness entry from `ram.freshness.&lt;field&gt;` -- ChangedAtUtc is the ISO
/// timestamp the reader gave for when the PUBLISHED value last actually changed (a null-to-value
/// or value-to-null transition counts as a change too, per the reader's own DATA-API.md). Prefer
/// this over SecondsSinceChange when possible -- the reader's own number is relative to ITS clock,
/// which can drift from ours; recomputing from ChangedAtUtc against our own utcNow keeps both
/// numbers on the same clock.</summary>
public readonly record struct ScoreboardReaderFreshnessEntry(DateTime? ChangedAtUtc, double? SecondsSinceChange);

/// <summary>Mirrors the reader's documented `ram.freshness` field set exactly (DATA-API.md,
/// v1.4.9) -- one entry per field sharing the core memory block (game clock/play clock are the
/// canary: see GameWatcher's staleness check, which trusts the whole block live whenever either
/// clock shows recent change) plus the self-guarding fields (rank/record/possession/names) that
/// the reader already nulls out instead of going stale.</summary>
public sealed class ScoreboardReaderFreshness
{
    public ScoreboardReaderFreshnessEntry? Quarter { get; init; }
    public ScoreboardReaderFreshnessEntry? GameClockSeconds { get; init; }
    public ScoreboardReaderFreshnessEntry? PlayClock { get; init; }
    public ScoreboardReaderFreshnessEntry? AwayScore { get; init; }
    public ScoreboardReaderFreshnessEntry? HomeScore { get; init; }
    public ScoreboardReaderFreshnessEntry? PossessionAwayIsOne { get; init; }
    public ScoreboardReaderFreshnessEntry? Down { get; init; }
    public ScoreboardReaderFreshnessEntry? Distance { get; init; }
    public ScoreboardReaderFreshnessEntry? AwayTimeouts { get; init; }
    public ScoreboardReaderFreshnessEntry? HomeTimeouts { get; init; }
    public ScoreboardReaderFreshnessEntry? AwayRank { get; init; }
    public ScoreboardReaderFreshnessEntry? HomeRank { get; init; }
    public ScoreboardReaderFreshnessEntry? AwayRecord { get; init; }
    public ScoreboardReaderFreshnessEntry? HomeRecord { get; init; }
    public ScoreboardReaderFreshnessEntry? AwayTeamName { get; init; }
    public ScoreboardReaderFreshnessEntry? HomeTeamName { get; init; }

    /// <summary>True whenever EITHER clock (game clock or play clock) shows a change within the
    /// last <paramref name="window"/> -- the reader's own documented canary for "the whole core
    /// memory block (quarter/clocks/scores/down/distance/timeouts) is provably live right now."
    /// Both entries missing (pre-v1.4.9 reader) returns false -- callers must NOT treat that as
    /// "block is stale," only as "no freshness data to lean on," same as ScoreboardReaderState.Freshness
    /// being null entirely.</summary>
    public bool CoreBlockRecentlyChanged(DateTime utcNow, TimeSpan window) =>
        IsRecent(GameClockSeconds, utcNow, window) || IsRecent(PlayClock, utcNow, window);

    static bool IsRecent(ScoreboardReaderFreshnessEntry? entry, DateTime utcNow, TimeSpan window) =>
        entry is { ChangedAtUtc: { } changedAt } && (utcNow - changedAt) <= window;
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
