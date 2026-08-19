using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bandroom.Core;

/// <summary>Ports the trust boundary Coffee's own `main.js` (`ramScoreboardPayload()`,
/// ~lines 829-891 of the decompiled source) puts in front of the bundled `CollegeFB27RamReader.exe`'s
/// raw output -- the exe's own JSON is NOT safe to trust as-is, so nothing downstream (including
/// GameStateNormalizer) ever sees it unvalidated. Every rule here is a direct port, not a guess:
///   - `status` must literally be "live".
///   - `process.id` must be present, and if the caller already knows which PID is the real game
///     process, it must match (Coffee's own `runtime.game.pid` check).
///   - `updatedAt` must parse and be &lt;= 20 seconds old (Coffee's own hardcoded window -- a
///     slower background lookup must not make already-confirmed values look live forever, but
///     also must not be so tight that an idle-but-still-live document gets rejected).
///   - EVERY individual field additionally requires its own sibling `&lt;field&gt;Source === "ram"`
///     marker (provenance) AND passes its own format/range check (scores 0-255, timeouts 0-3,
///     record `W-L` or `W-L-T`, clock `M:SS`/`MM:SS`, down 1-4, distance 0-99, rank 1-25) --
///     fields that fail either check are silently dropped (not applied), never fail the whole
///     document. If NOTHING validates, the whole read is treated as unusable (matches Coffee's
///     own `if (fields.length === 0) return null`).
/// GameStateNormalizer's sticky-cache mapping runs AFTER this, unchanged -- this class only ever
/// decides "is this raw document trustworthy," never does PlaySnapshot mapping itself.</summary>
public static class RamReaderValidator
{
    const double MaxDocumentAgeMs = 20_000;

    static readonly Regex RecordPattern = new(@"^\d{1,2}-\d{1,2}(-\d{1,2})?$");
    static readonly Regex ClockPattern = new(@"^\d{1,2}:\d{2}$");
    static readonly Regex NameSourcePattern = new(@"^ram(-cached|-pending)?$", RegexOptions.IgnoreCase);

    /// <summary>Validates and maps the RAM reader's raw status document. Returns (null, empty)
    /// for anything untrustworthy -- missing/wrong status, PID mismatch, stale, unparsable, or a
    /// document where every individual field failed its own check. <paramref name="expectedGameProcessId"/>
    /// is null when the caller doesn't yet know the real game PID (accepts whatever the document
    /// claims); pass a real PID once known to enforce the match.</summary>
    public static (ScoreboardReaderState? State, IReadOnlyList<string> AppliedFields) Validate(
        string documentJson, int? expectedGameProcessId, DateTime utcNow)
    {
        try
        {
            using var doc = JsonDocument.Parse(documentJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, Array.Empty<string>());

            if (!TryGetString(root, "status", out var status) || !string.Equals(status, "live", StringComparison.OrdinalIgnoreCase))
                return (null, Array.Empty<string>());

            if (!root.TryGetProperty("process", out var processEl) || !TryGetInt(processEl, "id", out int documentPid))
                return (null, Array.Empty<string>());
            if (expectedGameProcessId.HasValue && documentPid != expectedGameProcessId.Value)
                return (null, Array.Empty<string>());

            if (!TryGetString(root, "updatedAt", out var updatedAtRaw) || updatedAtRaw == null
                || !DateTime.TryParse(updatedAtRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var updatedAt))
                return (null, Array.Empty<string>());
            if ((utcNow - updatedAt).TotalMilliseconds > MaxDocumentAgeMs)
                return (null, Array.Empty<string>());

            var fields = new List<string>();
            var away = root.TryGetProperty("away", out var awayEl) ? BuildTeamState(awayEl, fields, "away") : new ScoreboardReaderTeamState();
            var home = root.TryGetProperty("home", out var homeEl) ? BuildTeamState(homeEl, fields, "home") : new ScoreboardReaderTeamState();
            var game = root.TryGetProperty("game", out var gameEl) ? BuildGameState(gameEl, fields) : new ScoreboardReaderGameState();

            if (fields.Count == 0) return (null, Array.Empty<string>());

            // "ram.freshness" (reader v1.4.9+) -- a nested "ram" object carries it, per DATA-API.md.
            // Absent entirely on older readers; BuildFreshness returns null in that case, which
            // ScoreboardReaderFreshness.CoreBlockRecentlyChanged's own doc comment says callers must
            // treat as "no data to lean on," never as "stale."
            ScoreboardReaderFreshness? freshness = root.TryGetProperty("ram", out var ramEl)
                && ramEl.TryGetProperty("freshness", out var freshnessEl)
                ? BuildFreshness(freshnessEl) : null;

            // ram.penalty.side ("offense"|"defense"|null) -- see ScoreboardReaderState.PenaltySide's
            // doc comment. No *Source provenance marker documented for this one (unlike every other
            // field in this file) -- ramEl's own presence plus a strict allowlist on the side string
            // is the validation here.
            string? penaltySide = null;
            if (ramEl.ValueKind == JsonValueKind.Object && ramEl.TryGetProperty("penalty", out var penaltyEl)
                && penaltyEl.ValueKind == JsonValueKind.Object
                && TryGetString(penaltyEl, "side", out var sideVal)
                && (sideVal == "offense" || sideVal == "defense"))
            { penaltySide = sideVal; fields.Add("ram.penalty.side"); }

            var state = new ScoreboardReaderState
            {
                Away = away,
                Home = home,
                Game = game,
                Meta = new ScoreboardReaderMeta { Source = "ram", Visible = true, UpdatedAt = updatedAtRaw, RamUpdatedAt = updatedAtRaw },
                Freshness = freshness,
                PenaltySide = penaltySide,
            };
            return (state, fields);
        }
        catch (JsonException)
        {
            return (null, Array.Empty<string>());
        }
    }

    static ScoreboardReaderTeamState BuildTeamState(JsonElement sideEl, List<string> fields, string sidePrefix)
    {
        string? name = null, record = null;
        int? rank = null, score = null, timeouts = null;
        bool? possession = null;

        if (TryGetString(sideEl, "nameSource", out var nameSource) && nameSource != null && NameSourcePattern.IsMatch(nameSource)
            && TryGetString(sideEl, "name", out var nameVal) && !string.IsNullOrEmpty(nameVal))
        { name = nameVal; fields.Add($"{sidePrefix}.name"); }

        if (SourceIsRam(sideEl, "rankSource") && TryGetInt(sideEl, "rank", out var rankVal) && rankVal is >= 1 and <= 25)
        { rank = rankVal; fields.Add($"{sidePrefix}.rank"); }

        if (SourceIsRam(sideEl, "recordSource") && TryGetString(sideEl, "record", out var recordVal)
            && recordVal != null && RecordPattern.IsMatch(recordVal))
        { record = recordVal; fields.Add($"{sidePrefix}.record"); }

        if (SourceIsRam(sideEl, "scoreSource") && TryGetInt(sideEl, "score", out var scoreVal) && scoreVal is >= 0 and <= 255)
        { score = scoreVal; fields.Add($"{sidePrefix}.score"); }

        if (SourceIsRam(sideEl, "timeoutsSource") && TryGetInt(sideEl, "timeouts", out var timeoutsVal) && timeoutsVal is >= 0 and <= 3)
        { timeouts = timeoutsVal; fields.Add($"{sidePrefix}.timeouts"); }

        if (SourceIsRam(sideEl, "possessionSource") && TryGetBool(sideEl, "possession", out var possessionVal))
        { possession = possessionVal; fields.Add($"{sidePrefix}.possession"); }

        return new ScoreboardReaderTeamState
        {
            Name = name,
            Rank = rank?.ToString(CultureInfo.InvariantCulture),
            Record = record,
            Score = score,
            Timeouts = timeouts,
            Possession = possession,
        };
    }

    static ScoreboardReaderGameState BuildGameState(JsonElement gameEl, List<string> fields)
    {
        string? quarter = null, clock = null, downDistance = null, distance = null;
        int? playClock = null, down = null;

        if (SourceIsRam(gameEl, "quarterSource") && TryGetString(gameEl, "quarterText", out var quarterVal) && !string.IsNullOrEmpty(quarterVal))
        { quarter = quarterVal; fields.Add("game.quarter"); }

        if (SourceIsRam(gameEl, "clockSource") && TryGetString(gameEl, "clock", out var clockVal)
            && clockVal != null && ClockPattern.IsMatch(clockVal))
        { clock = clockVal; fields.Add("game.clock"); }

        if (SourceIsRam(gameEl, "playClockSource") && TryGetInt(gameEl, "playClock", out var playClockVal) && playClockVal is >= 0 and <= 99)
        { playClock = playClockVal; fields.Add("game.playClock"); }

        // down/distance/downDistance/downDistanceKind all share one `downDistanceSource` marker
        // in Coffee's own source -- one field's provenance covers the whole down/distance group.
        if (SourceIsRam(gameEl, "downDistanceSource"))
        {
            if (TryGetInt(gameEl, "down", out var downVal) && downVal is >= 1 and <= 4)
            { down = downVal; fields.Add("game.down"); }
            if (TryGetInt(gameEl, "distance", out var distanceVal) && distanceVal is >= 0 and <= 99)
            { distance = distanceVal.ToString(CultureInfo.InvariantCulture); fields.Add("game.distance"); }
            if (TryGetString(gameEl, "downDistance", out var downDistanceVal) && !string.IsNullOrEmpty(downDistanceVal))
            { downDistance = downDistanceVal; fields.Add("game.downDistance"); }
        }

        return new ScoreboardReaderGameState
        {
            Down = down,
            Distance = distance,
            DownDistance = downDistance,
            Quarter = quarter,
            Clock = clock,
            PlayClock = playClock,
            // The RAM document has no game-level possession string (unlike the screen-JSON
            // schema's game.possession "away"/"home"/"none") -- Coffee's own source only ever
            // reports possession per-team (away.possession/home.possession bools, handled in
            // BuildTeamState above). GameStateNormalizer already falls back to those same
            // team-level bools when game.possession is null, so no mapping is lost here.
            Possession = null,
            // Not part of the RAM document at all (not in Coffee's own ramScoreboardPayload) --
            // the RAM reader doesn't report ball-on-field position, only down/distance/clock/score.
            BallOn = null,
            Status = null,
        };
    }

    /// <summary>Reads every `ram.freshness.&lt;field&gt;` entry documented in DATA-API.md. Unlike
    /// every other field in this file, a freshness entry that fails to parse is simply left null
    /// (not a reason to reject the whole document) -- freshness is a trust SIGNAL, not game data,
    /// so a partially-broken freshness block should just mean "no signal for that one field,"
    /// never "distrust the document."</summary>
    static ScoreboardReaderFreshness BuildFreshness(JsonElement freshnessEl) => new()
    {
        Quarter = BuildFreshnessEntry(freshnessEl, "quarter"),
        GameClockSeconds = BuildFreshnessEntry(freshnessEl, "gameClockSeconds"),
        PlayClock = BuildFreshnessEntry(freshnessEl, "playClock"),
        AwayScore = BuildFreshnessEntry(freshnessEl, "awayScore"),
        HomeScore = BuildFreshnessEntry(freshnessEl, "homeScore"),
        PossessionAwayIsOne = BuildFreshnessEntry(freshnessEl, "possessionAwayIsOne"),
        Down = BuildFreshnessEntry(freshnessEl, "down"),
        Distance = BuildFreshnessEntry(freshnessEl, "distance"),
        AwayTimeouts = BuildFreshnessEntry(freshnessEl, "awayTimeouts"),
        HomeTimeouts = BuildFreshnessEntry(freshnessEl, "homeTimeouts"),
        AwayRank = BuildFreshnessEntry(freshnessEl, "awayRank"),
        HomeRank = BuildFreshnessEntry(freshnessEl, "homeRank"),
        AwayRecord = BuildFreshnessEntry(freshnessEl, "awayRecord"),
        HomeRecord = BuildFreshnessEntry(freshnessEl, "homeRecord"),
        AwayTeamName = BuildFreshnessEntry(freshnessEl, "awayTeamName"),
        HomeTeamName = BuildFreshnessEntry(freshnessEl, "homeTeamName"),
    };

    static ScoreboardReaderFreshnessEntry? BuildFreshnessEntry(JsonElement freshnessEl, string prop)
    {
        if (freshnessEl.ValueKind != JsonValueKind.Object || !freshnessEl.TryGetProperty(prop, out var entryEl)
            || entryEl.ValueKind != JsonValueKind.Object) return null;

        DateTime? changedAt = TryGetString(entryEl, "changedAt", out var changedAtRaw) && changedAtRaw != null
            && DateTime.TryParse(changedAtRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : null;

        double? secondsSinceChange = entryEl.TryGetProperty("secondsSinceChange", out var secEl)
            && secEl.ValueKind == JsonValueKind.Number && secEl.TryGetDouble(out var secVal)
            ? secVal : null;

        return changedAt == null && secondsSinceChange == null ? null
            : new ScoreboardReaderFreshnessEntry(changedAt, secondsSinceChange);
    }

    static bool SourceIsRam(JsonElement obj, string sourceProp) =>
        TryGetString(obj, sourceProp, out var s) && string.Equals(s, "ram", StringComparison.OrdinalIgnoreCase);

    static bool TryGetString(JsonElement obj, string prop, out string? value)
    {
        value = null;
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var el)) return false;
        if (el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString();
        return true;
    }

    static bool TryGetInt(JsonElement obj, string prop, out int value)
    {
        value = 0;
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value)) return true;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value)) return true;
        return false;
    }

    static bool TryGetBool(JsonElement obj, string prop, out bool value)
    {
        value = false;
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var el)) return false;
        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False) { value = el.GetBoolean(); return true; }
        return false;
    }
}
