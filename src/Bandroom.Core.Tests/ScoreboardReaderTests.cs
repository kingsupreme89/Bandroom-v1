using System.Text;
using Bandroom.Core;
using Xunit;

namespace Bandroom.Core.Tests;

/// <summary>Coverage for the scoreboard reader integration's pure-additive data layer
/// (GameStateNormalizer's sticky-cache discipline + YardLine/possession mapping,
/// ScoreboardJsonReader's atomic-read race handling) -- see A6 of the integration plan.</summary>
public class ScoreboardReaderTests
{
    static ScoreboardReaderState State(
        int? awayScore = null, int? homeScore = null,
        int? down = null, string? distance = null, int? ballOn = null,
        string? quarter = null, string? clock = null,
        int? awayTimeouts = null, int? homeTimeouts = null,
        string? possession = null, bool? visible = true) => new()
    {
        Away = new ScoreboardReaderTeamState { Score = awayScore, Timeouts = awayTimeouts },
        Home = new ScoreboardReaderTeamState { Score = homeScore, Timeouts = homeTimeouts },
        Game = new ScoreboardReaderGameState
        {
            Down = down,
            Distance = distance,
            BallOn = ballOn,
            Quarter = quarter,
            Clock = clock,
            Possession = possession,
        },
        Meta = new ScoreboardReaderMeta { Visible = visible },
    };

    // ---------- GameStateNormalizer ----------

    [Fact]
    public void Normalize_MapsBallOnToYardLine_PreviouslyDeadField()
    {
        var normalizer = new GameStateNormalizer();
        var result = normalizer.Normalize(State(ballOn: 35, down: 2, distance: "7"));
        Assert.NotNull(result);
        Assert.Equal(35, result!.Value.YardLine);
    }

    [Fact]
    public void Normalize_NullState_ReturnsNull()
    {
        var normalizer = new GameStateNormalizer();
        Assert.Null(normalizer.Normalize(null));
    }

    [Fact]
    public void Normalize_MetaVisibleFalse_ReturnsNull_EvenAfterPriorGoodRead()
    {
        var normalizer = new GameStateNormalizer();
        normalizer.Normalize(State(awayScore: 7, homeScore: 0, ballOn: 20));
        var result = normalizer.Normalize(State(visible: false));
        Assert.Null(result);
    }

    [Fact]
    public void Normalize_BlankSubsequentRead_HoldsStickyScore_DoesNotFabricateDrop()
    {
        var normalizer = new GameStateNormalizer();
        var first = normalizer.Normalize(State(awayScore: 14, homeScore: 7, ballOn: 40));
        Assert.Equal(14, first!.Value.AwayScore);

        // A read where the team score fields themselves come back null (e.g. a transient partial
        // write) must never read as the score dropping to 0 -- same discipline as GameWatcher's
        // own _lastKnownAwayScore.
        var second = normalizer.Normalize(new ScoreboardReaderState
        {
            Away = new ScoreboardReaderTeamState { Score = null },
            Home = new ScoreboardReaderTeamState { Score = null },
            Game = new ScoreboardReaderGameState(),
            Meta = new ScoreboardReaderMeta { Visible = true },
        });
        Assert.Equal(14, second!.Value.AwayScore);
        Assert.Equal(7, second.Value.HomeScore);
    }

    [Fact]
    public void Normalize_Possession_StickyAcrossNoneRead()
    {
        var normalizer = new GameStateNormalizer();
        var first = normalizer.Normalize(State(possession: "away"));
        Assert.True(first!.Value.PossessionAway);

        var second = normalizer.Normalize(State(possession: "none"));
        Assert.True(second!.Value.PossessionAway); // "none" must not fabricate a flip
    }

    [Fact]
    public void Normalize_Possession_HomeFlip_Commits()
    {
        var normalizer = new GameStateNormalizer();
        normalizer.Normalize(State(possession: "away"));
        var flipped = normalizer.Normalize(State(possession: "home"));
        Assert.False(flipped!.Value.PossessionAway);
    }

    [Fact]
    public void Normalize_GoalToGoDistance_NormalizesToZero()
    {
        var normalizer = new GameStateNormalizer();
        var result = normalizer.Normalize(State(distance: "Goal", ballOn: 3));
        Assert.Equal(0, result!.Value.YardsToGo);
    }

    [Fact]
    public void Reset_ClearsAllStickyFields()
    {
        var normalizer = new GameStateNormalizer();
        normalizer.Normalize(State(awayScore: 21, homeScore: 14, ballOn: 50, possession: "away"));
        normalizer.Reset();
        var afterReset = normalizer.Normalize(State(visible: true)); // no fields set this read
        // RELIABILITY FIX 2026-08-14: score/yard-line now sentinel to -1 ("never resolved this
        // game") rather than 0, since 0 is a real, common value (0-0 at kickoff, own goal line)
        // indistinguishable from "reader confirms zero" -- see GameStateNormalizer's field
        // comments and ReaderNumericSnapshot's doc comment for the GameWatcher bug this fixes
        // (a real score/yardage getting silently stomped to 0 by an unresolved reader field).
        Assert.Equal(-1, afterReset!.Value.AwayScore);
        Assert.Equal(-1, afterReset.Value.HomeScore);
        Assert.Equal(-1, afterReset.Value.YardLine);
        Assert.False(afterReset.Value.PossessionAway);
        Assert.False(afterReset.Value.HavePossession);
    }

    [Fact]
    public void Normalize_ScoreNeverResolved_ReturnsSentinelNotZero()
    {
        // RELIABILITY FIX 2026-08-14 regression test: a reader connection where score has NEVER
        // resolved (e.g. RAM locked team names/clock but not the score locator yet -- the exact
        // intermittent per-session failure the Session 72 handoff doc reports for timeouts) must
        // report -1, not 0, so GameWatcher knows to keep trusting its own OCR-derived score
        // instead of stomping it to a fabricated 0-0.
        var normalizer = new GameStateNormalizer();
        var result = normalizer.Normalize(State(down: 2, distance: "7")); // score fields never set
        Assert.Equal(-1, result!.Value.AwayScore);
        Assert.Equal(-1, result.Value.HomeScore);
    }

    [Fact]
    public void Normalize_PossessionNeverResolved_HavePossessionFalse()
    {
        // RELIABILITY FIX 2026-08-14 regression test: connecting with other fields resolved but
        // possession never read must leave HavePossession false so GameWatcher falls back to its
        // own OCR-tracked possession instead of defaulting to "home."
        var normalizer = new GameStateNormalizer();
        var result = normalizer.Normalize(State(awayScore: 7, homeScore: 3)); // possession never set
        Assert.False(result!.Value.HavePossession);
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("inches", 0)] // not parseable as int and not "goal" -- distance parse fails, sticky holds (0 from fresh state)
    [InlineData(null, 0)]
    public void TryParseDistance_Cases(string? raw, int expectedWhenNoPrior)
    {
        bool parsed = GameStateNormalizer.TryParseDistance(raw, out int distance);
        if (raw == "7") { Assert.True(parsed); Assert.Equal(expectedWhenNoPrior, distance); }
        else Assert.False(parsed);
    }

    [Fact]
    public void TryParseClock_ParsesMinutesSeconds()
    {
        Assert.True(GameStateNormalizer.TryParseClock("7:23", out int seconds));
        Assert.Equal(7 * 60 + 23, seconds);
    }

    [Fact]
    public void TryParseQuarter_ExtractsDigitsFromOrdinalText()
    {
        Assert.True(GameStateNormalizer.TryParseQuarter("3rd", out int quarter));
        Assert.Equal(3, quarter);
    }

    // ---------- ScoreboardJsonReader ----------

    [Fact]
    public void Read_MissingFile_ReturnsNotFound()
    {
        var reader = new ScoreboardJsonReader();
        var (status, state) = reader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Equal(ScoreboardReaderStatus.NotFound, status);
        Assert.Null(state);
    }

    [Fact]
    public void Read_NullPath_ReturnsNotFound()
    {
        var reader = new ScoreboardJsonReader();
        var (status, _) = reader.Read(null);
        Assert.Equal(ScoreboardReaderStatus.NotFound, status);
    }

    [Fact]
    public void Read_ValidVisibleJson_ReturnsConnected()
    {
        string path = WriteTempJson("""{"away":{"score":7},"home":{"score":0},"game":{},"meta":{"visible":true}}""");
        try
        {
            var reader = new ScoreboardJsonReader();
            var (status, state) = reader.Read(path);
            Assert.Equal(ScoreboardReaderStatus.Connected, status);
            Assert.Equal(7, state!.Away!.Score);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_VisibleFalse_ReturnsWaitingForGameData()
    {
        string path = WriteTempJson("""{"away":{},"home":{},"game":{},"meta":{"visible":false}}""");
        try
        {
            var reader = new ScoreboardJsonReader();
            var (status, _) = reader.Read(path);
            Assert.Equal(ScoreboardReaderStatus.WaitingForGameData, status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_CorruptJson_FallsBackToCachedGoodReadWithinGraceWindow()
    {
        string path = WriteTempJson("""{"away":{"score":3},"home":{"score":0},"game":{},"meta":{"visible":true}}""");
        try
        {
            var reader = new ScoreboardJsonReader();
            var (firstStatus, firstState) = reader.Read(path);
            Assert.Equal(ScoreboardReaderStatus.Connected, firstStatus);

            // Simulate landing mid-rename: file momentarily holds a truncated/partial write.
            File.WriteAllText(path, "{\"away\":{\"sc");
            var (secondStatus, secondState) = reader.Read(path);
            Assert.Equal(ScoreboardReaderStatus.Connected, secondStatus); // served from grace-window cache
            Assert.Equal(firstState!.Away!.Score, secondState!.Away!.Score);
        }
        finally { File.Delete(path); }
    }

    static string WriteTempJson(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }
}
