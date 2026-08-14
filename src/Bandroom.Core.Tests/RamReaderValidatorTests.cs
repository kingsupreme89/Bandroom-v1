using Bandroom.Core;
using Xunit;

namespace Bandroom.Core.Tests;

/// <summary>Coverage for RamReaderValidator's port of Coffee's `ramScoreboardPayload()` trust
/// boundary (status/PID/freshness/per-field provenance+format) -- see that class's own doc
/// comment for the exact rules being ported.</summary>
public class RamReaderValidatorTests
{
    static string LiveDoc(string extraAwayFields = "", string extraGameFields = "", string? updatedAt = null, int pid = 4242) => $$"""
        {
          "status": "live",
          "process": { "id": {{pid}} },
          "updatedAt": "{{updatedAt ?? DateTime.UtcNow.ToString("O")}}",
          "away": {
            "score": 7, "scoreSource": "ram",
            "timeouts": 2, "timeoutsSource": "ram"
            {{extraAwayFields}}
          },
          "home": { "score": 3, "scoreSource": "ram" },
          "game": {
            "down": 2, "distance": 7, "downDistanceSource": "ram",
            "clock": "7:23", "clockSource": "ram"
            {{extraGameFields}}
          }
        }
        """;

    [Fact]
    public void Validate_WellFormedLiveDocument_ReturnsState()
    {
        var (state, fields) = RamReaderValidator.Validate(LiveDoc(), expectedGameProcessId: 4242, DateTime.UtcNow);
        Assert.NotNull(state);
        Assert.Equal(7, state!.Away!.Score);
        Assert.Equal(3, state.Home!.Score);
        Assert.Equal(2, state.Game!.Down);
        Assert.NotEmpty(fields);
    }

    [Fact]
    public void Validate_StatusNotLive_RejectsWholeDocument()
    {
        string doc = LiveDoc().Replace("\"live\"", "\"paused\"");
        var (state, _) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.Null(state);
    }

    [Fact]
    public void Validate_PidMismatch_Rejects()
    {
        var (state, _) = RamReaderValidator.Validate(LiveDoc(pid: 111), expectedGameProcessId: 999, DateTime.UtcNow);
        Assert.Null(state);
    }

    [Fact]
    public void Validate_PidUnknownExpected_AcceptsWhateverDocumentClaims()
    {
        var (state, _) = RamReaderValidator.Validate(LiveDoc(pid: 111), expectedGameProcessId: null, DateTime.UtcNow);
        Assert.NotNull(state);
    }

    [Fact]
    public void Validate_StaleDocument_Rejects()
    {
        string staleTimestamp = DateTime.UtcNow.AddSeconds(-25).ToString("O");
        var (state, _) = RamReaderValidator.Validate(LiveDoc(updatedAt: staleTimestamp), null, DateTime.UtcNow);
        Assert.Null(state);
    }

    [Fact]
    public void Validate_JustUnderStaleWindow_Accepts()
    {
        string freshTimestamp = DateTime.UtcNow.AddSeconds(-15).ToString("O");
        var (state, _) = RamReaderValidator.Validate(LiveDoc(updatedAt: freshTimestamp), null, DateTime.UtcNow);
        Assert.NotNull(state);
    }

    [Fact]
    public void Validate_FieldMissingSourceMarker_DropsOnlyThatField_DoesNotRejectDocument()
    {
        // home.score has no "scoreSource":"ram" sibling in LiveDoc()'s home object by default --
        // wait, it does; construct one explicitly without it instead.
        string doc = """
            {
              "status": "live",
              "process": { "id": 1 },
              "updatedAt": "REPLACED",
              "away": { "score": 7, "scoreSource": "ram" },
              "home": { "score": 99 },
              "game": {}
            }
            """.Replace("REPLACED", DateTime.UtcNow.ToString("O"));
        var (state, fields) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.NotNull(state);
        Assert.Equal(7, state!.Away!.Score);
        Assert.Null(state.Home!.Score); // dropped: no provenance marker
        Assert.Contains("away.score", fields);
        Assert.DoesNotContain("home.score", fields);
    }

    [Fact]
    public void Validate_ScoreOutOfRange_DropsField()
    {
        string doc = """
            {
              "status": "live",
              "process": { "id": 1 },
              "updatedAt": "REPLACED",
              "away": { "score": 999, "scoreSource": "ram" },
              "home": { "score": 3, "scoreSource": "ram" },
              "game": {}
            }
            """.Replace("REPLACED", DateTime.UtcNow.ToString("O"));
        var (state, fields) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.NotNull(state); // home.score still validates -- document isn't rejected wholesale
        Assert.Null(state!.Away!.Score);
        Assert.Equal(3, state.Home!.Score);
    }

    [Fact]
    public void Validate_NoFieldsValidateAtAll_ReturnsNull()
    {
        string doc = """
            {
              "status": "live",
              "process": { "id": 1 },
              "updatedAt": "REPLACED",
              "away": {},
              "home": {},
              "game": {}
            }
            """.Replace("REPLACED", DateTime.UtcNow.ToString("O"));
        var (state, fields) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.Null(state);
        Assert.Empty(fields);
    }

    [Fact]
    public void Validate_MalformedRecord_DropsField()
    {
        string doc = """
            {
              "status": "live",
              "process": { "id": 1 },
              "updatedAt": "REPLACED",
              "away": { "record": "not-a-record", "recordSource": "ram" },
              "home": {},
              "game": { "down": 2, "distance": 7, "downDistanceSource": "ram" }
            }
            """.Replace("REPLACED", DateTime.UtcNow.ToString("O"));
        var (state, fields) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.NotNull(state);
        Assert.Null(state!.Away!.Record);
        Assert.DoesNotContain("away.record", fields);
    }

    [Fact]
    public void Validate_ValidRecord_Accepted()
    {
        string doc = """
            {
              "status": "live",
              "process": { "id": 1 },
              "updatedAt": "REPLACED",
              "away": { "record": "5-2", "recordSource": "ram" },
              "home": {},
              "game": {}
            }
            """.Replace("REPLACED", DateTime.UtcNow.ToString("O"));
        var (state, fields) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.NotNull(state);
        Assert.Equal("5-2", state!.Away!.Record);
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsNull()
    {
        var (state, fields) = RamReaderValidator.Validate("{not json", null, DateTime.UtcNow);
        Assert.Null(state);
        Assert.Empty(fields);
    }

    [Fact]
    public void Validate_MissingProcessId_ReturnsNull()
    {
        string doc = """{"status":"live","updatedAt":"REPLACED","away":{},"home":{},"game":{}}"""
            .Replace("REPLACED", DateTime.UtcNow.ToString("O"));
        var (state, _) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.Null(state);
    }

    [Fact]
    public void Validate_GamePossessionAbsent_LeavesGamePossessionNull_TeamLevelStillCarried()
    {
        string doc = """
            {
              "status": "live",
              "process": { "id": 1 },
              "updatedAt": "REPLACED",
              "away": { "possession": true, "possessionSource": "ram" },
              "home": {},
              "game": {}
            }
            """.Replace("REPLACED", DateTime.UtcNow.ToString("O"));
        var (state, _) = RamReaderValidator.Validate(doc, null, DateTime.UtcNow);
        Assert.NotNull(state);
        Assert.Null(state!.Game!.Possession);
        Assert.True(state.Away!.Possession);
    }
}
