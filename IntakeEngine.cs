using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SupremeStadiumSoundSelector;

/// <summary>C# port of scripts/intake_engine.py's title-cleaning, team-resolution, and
/// trigger-mapping stages, so filename-based auto-indexing works at runtime without shelling
/// out to Python (confirmed nowhere else in this app does that -- would be a net-new runtime
/// dependency this app doesn't otherwise have). Reads the same team_registry.json /
/// trigger_event_map.json the Python script uses (bundled as Content in BandAudioHook.csproj)
/// so both stay in sync off one source of truth instead of duplicating the team/keyword lists.
/// Deliberately narrower than the full Python engine (no safe-filename generation, no clip-
/// duration recommendation) -- this only covers what ImportLocalSongFromWeb actually needs:
/// suggest a clean title + team + trigger so the user isn't starting from a raw filename.</summary>
public static class IntakeEngine
{
    public sealed record IntakeResult(
        string CleanedTitle,
        string Team,
        string TeamMatchType,
        string PrimaryTrigger,
        string TriggerSource,
        string[] SuggestedEventKeys,
        double Confidence,
        string[] ReviewFlags);

    private static Dictionary<string, TeamData>? _teams;
    // alias -> every team that claims it. Was a single string (first/only claimant silently won,
    // e.g. "UM" -> Miami even for a Big Ten pack where it means Michigan) -- now a list so
    // ResolveTeam can disambiguate using a conference hint parsed from the filename (see
    // ConferenceHintFor) instead of one candidate always shadowing every other.
    private static Dictionary<string, List<string>>? _aliasIndex;
    private static Dictionary<string, string[]>? _triggerToEventKeys;
    private static HashSet<string>? _validTriggers;

    private sealed record TeamData(string Name, string Conference, string[] Abbreviations, string[] Variants);

    private static void AddAlias(string alias, string team)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;
        if (!_aliasIndex!.TryGetValue(alias, out var list))
            _aliasIndex[alias] = list = new List<string>();
        if (!list.Contains(team, StringComparer.Ordinal)) list.Add(team);
    }

    private static void EnsureLoaded()
    {
        if (_teams != null) return;

        var registryPath = Path.Combine(AppContext.BaseDirectory, "scripts", "team_registry.json");
        var triggerMapPath = Path.Combine(AppContext.BaseDirectory, "scripts", "trigger_event_map.json");

        // BUG FIX: was StringComparer.Ordinal -- a default-pack folder named "uga" (any casing
        // that didn't byte-for-byte match team_registry.json's own casing, e.g. "UGA") missed
        // this exact-match lookup entirely and fell through to the much weaker fuzzy Contains()
        // match below, which DefaultSongPackService explicitly excludes from the bulk
        // CopyTeamFolder import path -- the whole team's folder got silently routed through
        // slower per-file filename classification instead, purely because of folder-name casing.
        _teams = new Dictionary<string, TeamData>(StringComparer.OrdinalIgnoreCase);
        _aliasIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _triggerToEventKeys = new Dictionary<string, string[]>(StringComparer.Ordinal);
        _validTriggers = new HashSet<string>(StringComparer.Ordinal);

        if (File.Exists(registryPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(registryPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("teams", out var teamsEl))
            {
                foreach (var t in teamsEl.EnumerateObject())
                {
                    var conf = t.Value.TryGetProperty("conference", out var c) ? c.GetString() ?? "General" : "General";
                    var abbrevs = t.Value.TryGetProperty("abbreviations", out var a)
                        ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
                    var variants = t.Value.TryGetProperty("variants", out var v)
                        ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
                    _teams[t.Name] = new TeamData(t.Name, conf, abbrevs, variants);
                }
            }
            if (root.TryGetProperty("alias_index", out var aliasEl))
            {
                foreach (var a in aliasEl.EnumerateObject())
                    AddAlias(a.Name, a.Value.GetString() ?? "");
            }
            // Backfill from each team's own `abbreviations` array -- confirmed real gap: several
            // teams (e.g. Maryland's "MRLD"/"UMD") declare abbreviations there that were never
            // copied into the shared alias_index map above, so IntakeEngine could never actually
            // match them despite the data existing right next to it.
            foreach (var (teamName, data) in _teams)
                foreach (var abbr in data.Abbreviations)
                    AddAlias(abbr, teamName);
        }

        if (File.Exists(triggerMapPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(triggerMapPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("trigger_to_event_keys", out var mapEl))
            {
                foreach (var m in mapEl.EnumerateObject())
                    _triggerToEventKeys[m.Name] = m.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            }
            if (root.TryGetProperty("valid_triggers", out var validEl))
            {
                foreach (var v in validEl.EnumerateArray())
                    _validTriggers.Add(v.GetString() ?? "");
            }
        }
    }

    private static readonly Regex[] RemovePatterns =
    {
        new(@"\b\d{2,4}\s*kbps\b", RegexOptions.IgnoreCase),
        new(@"\b\d{2,4}k\b", RegexOptions.IgnoreCase),
        new(@"\bFLAC\b|\bWAV\b|\bAIFF\b", RegexOptions.IgnoreCase),
        new(@"\[RIP\]|\[YT\]|\[SC\]|\[BC\]|\(YT\)|\(SC\)|\(BC\)", RegexOptions.IgnoreCase),
        new(@"\(Official Audio\)|\(Official Video\)|\(Lyrics?\)|\(Audio\)", RegexOptions.IgnoreCase),
        new(@"\b(final|master|copy|demo|rough|v\d+)\b", RegexOptions.IgnoreCase),
        new(@"\b(remaster(ed)?)\b", RegexOptions.IgnoreCase),
        new(@"\(\d{4}\)|\[\d{4}\]|\b(19|20)\d{2}\b", RegexOptions.IgnoreCase),
        new(@"\bHD\b|\bHQ\b", RegexOptions.IgnoreCase),
        new(@"^[\s\-_.,;]+|[\s\-_.,;]+$", RegexOptions.None),
    };

    private static readonly (Regex pattern, string replacement)[] CleanupPairs =
    {
        (new Regex(@"[\s_]+"), " "),
        (new Regex(@"--+"), "-"),
        (new Regex(@"\s*-\s*"), " - "),
        (new Regex(@"[\(\[\{].*?[\)\]\}]"), ""),
    };

    private static readonly string[] KnownExtensions = { ".mp3", ".wav", ".m4a", ".flac", ".aiff", ".ogg", ".wma" };

    public static string CleanTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown";
        var s = raw.Trim();

        foreach (var ext in KnownExtensions)
        {
            if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^ext.Length];
                break;
            }
        }

        s = s.Replace('_', ' ');

        foreach (var pattern in RemovePatterns)
            s = pattern.Replace(s, "");

        foreach (var (pattern, replacement) in CleanupPairs)
            s = pattern.Replace(s, replacement);

        s = Regex.Replace(s, @"\s{2,}", " ").Trim().Trim('-', '_', ' ');

        if (string.IsNullOrWhiteSpace(s)) return "Unknown";

        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = words.Select(w =>
            w.Length <= 4 && w == w.ToUpperInvariant() && w.Any(char.IsLetter)
                ? w
                : char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..].ToLowerInvariant() : ""));
        return string.Join(' ', result);
    }

    /// <summary>conferenceHint (see GuessConferenceHint) disambiguates an alias multiple teams
    /// claim -- e.g. "UM" is Michigan in a Big Ten pack but Miami elsewhere, "OSU" is Ohio State,
    /// Oklahoma State, or Oregon State depending which. Without a hint (or no candidate matches
    /// it), falls back to the first candidate, same as the old single-value behavior.</summary>
    public static (string Team, string Conference, string MatchType) ResolveTeam(string? name, string? conferenceHint = null)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(name)) return ("Unknown", "General", "unknown");
        var nameClean = name.Trim();

        if (_teams!.TryGetValue(nameClean, out var exact))
            // Return the registry's own canonical casing (exact.Name), not the input's -- a
            // case-insensitive dictionary hit for "uga" must still resolve to whatever casing
            // team_registry.json actually uses ("UGA"/"Georgia"/etc), or every downstream
            // consumer keying off this string (ConfigStore folder lookups, the saved-profile
            // filename, GetDefaultPackTeams' index.json) ends up with an inconsistent, possibly
            // duplicate, per-import casing for the same real team.
            return (exact.Name, exact.Conference, "exact");

        if (_aliasIndex!.TryGetValue(nameClean.ToUpperInvariant(), out var candidates) && candidates.Count > 0)
        {
            string chosen = candidates[0];
            if (conferenceHint != null)
            {
                var confMatch = candidates.FirstOrDefault(c =>
                    _teams.TryGetValue(c, out var d) && string.Equals(d.Conference, conferenceHint, StringComparison.OrdinalIgnoreCase));
                if (confMatch != null) chosen = confMatch;
            }
            if (_teams.TryGetValue(chosen, out var aliasTeam))
                return (chosen, aliasTeam.Conference, "abbreviation");
        }

        foreach (var (teamName, data) in _teams)
        {
            foreach (var variant in data.Variants)
            {
                if (string.Equals(variant, nameClean, StringComparison.OrdinalIgnoreCase))
                    return (teamName, data.Conference, "variant");
            }
        }

        var lower = nameClean.ToLowerInvariant();
        foreach (var (teamName, data) in _teams)
        {
            var teamLower = teamName.ToLowerInvariant();
            if (lower.Contains(teamLower) || teamLower.Contains(lower))
                return (teamName, data.Conference, "fuzzy");
        }

        return ("Unknown", "General", "unknown");
    }

    private static readonly (string trigger, string[] keywords)[] KeywordMap =
    {
        ("TD_HOME", new[] { "touchdown", " td ", "td scored", "td home" }),
        ("TD_AWAY", new[] { "away td", "opponent td", "defensive td" }),
        ("PAT", new[] { "pat", "extra point", "point after" }),
        ("FIELD_GOAL", new[] { "field goal", "fg ", "fg made" }),
        ("OFF_1ST_DOWN", new[] { "first down", "1st down", "1st dn" }),
        ("OFF_3RD_DOWN", new[] { "third down", "3rd down", "3rd dn", "off 3rd" }),
        ("DEF_3RD_DOWN", new[] { "def third", "def 3rd", "defense third" }),
        ("DEF_STOP_3RD_4TH", new[] { "fourth down", "4th down", "tackle for loss", "tfl", "stop" }),
        ("TURNOVER", new[] { "turnover", "fumble", "interception", "int ", "takeaway" }),
        ("INTERCEPTION", new[] { "interception", "pick", "picked off" }),
        ("FUMBLE_RECOVERY", new[] { "fumble", "fumble recovery" }),
        ("OPENING_KICKOFF", new[] { "kickoff", "opening kick", "ko " }),
        ("KICKOFF_RETURN", new[] { "kick return", "kickoff return", "return" }),
        ("TIMEOUT", new[] { "timeout", "time out" }),
        ("PENALTY", new[] { "penalty", "flag" }),
        ("BIG_PLAY", new[] { "big play", "big gain", "explosive" }),
        ("RUNOUT", new[] { "runout", "take the field", "pregame", "entrance" }),
        ("HALFTIME", new[] { "halftime", "half time", "half-time" }),
        ("POSTGAME", new[] { "postgame", "victory", "post-game", "after game" }),
        ("GENERAL_HYPE", new[] { "hype", "crowd", "stadium", "cheer", "chant", "fight song", "pep" }),
        ("PRE_SNAP", new[] { "pre snap", "pre-snap", "presnap" }),
    };

    public static (string Primary, string Source) MapTrigger(string rawFilename)
    {
        var text = (rawFilename ?? "").ToLowerInvariant();
        string? best = null;
        int bestScore = 0;
        foreach (var (trigger, keywords) in KeywordMap)
        {
            var score = keywords.Count(kw => text.Contains(kw));
            if (score > bestScore)
            {
                bestScore = score;
                best = trigger;
            }
        }
        return best != null ? (best, "filename_keyword") : ("GENERAL_HYPE", "fallback");
    }

    public static string[] EventKeysFor(string trigger)
    {
        EnsureLoaded();
        return _triggerToEventKeys!.TryGetValue(trigger, out var keys) ? keys : Array.Empty<string>();
    }

    public static double ComputeConfidence(string cleanedTitle, string teamMatchType, string triggerSource)
    {
        double score = 0.0;

        if (cleanedTitle != "Unknown")
        {
            var noiseCount = Regex.Matches(cleanedTitle, @"[0-9]{3,}|\(|\)|\[|\]").Count;
            score += noiseCount == 0 ? 0.4 : noiseCount <= 2 ? 0.25 : 0.1;
        }

        score += teamMatchType switch
        {
            "exact" => 0.3,
            "abbreviation" => 0.28,
            "variant" => 0.25,
            "fuzzy" => 0.15,
            _ => 0.0,
        };

        score += triggerSource switch
        {
            "filename_keyword" => 0.3,
            _ => 0.05,
        };

        return Math.Round(Math.Min(score, 1.0), 2);
    }

    /// <summary>Suggest a cleaned title, team, and trigger for a locally-imported audio file,
    /// purely from its original filename -- used to prefill the naming dialog in
    /// WebMainForm.ImportLocalSongFromWeb instead of leaving the user to type a raw filename in
    /// from scratch. Never auto-assigns a trigger by itself; only ever a suggestion the user
    /// sees and can accept or overwrite.</summary>
    public static IntakeResult Process(string originalFilename)
    {
        EnsureLoaded();
        var cleaned = CleanTitle(originalFilename);
        var (team, _, matchType) = ResolveTeam(GuessTeamToken(originalFilename), GuessConferenceHint(originalFilename));
        var (trigger, source) = MapTrigger(originalFilename);
        var confidence = ComputeConfidence(cleaned, matchType, source);

        var flags = new List<string>();
        if (cleaned == "Unknown") flags.Add("title_unknown");
        if (team == "Unknown") flags.Add("team_unknown");
        if (matchType is "fuzzy" or "unknown") flags.Add("team_match_low_confidence");
        if (source == "fallback") flags.Add("trigger_fallback_assigned");

        return new IntakeResult(cleaned, team, matchType, trigger, source, EventKeysFor(trigger), confidence, flags.ToArray());
    }

    /// <summary>Populates an AudioTrackMetadata suggestion for a newly-imported file: reuses
    /// Process() for title/team/trigger (no re-implementation of the filename-parsing logic) and
    /// AudioTrackMetadataStore.AnalyzeAudioFile for the computed duration/loudness fields. Energy
    /// level is guessed from the matched trigger's usual intensity -- a coarse heuristic, not a
    /// real audio-analysis classifier -- so it's just a starting point the user confirms/edits in
    /// the Track Info drawer, same spirit as every other suggestion this engine makes.</summary>
    public static AudioTrackMetadata AnalyzeAndSuggest(string filePath)
    {
        var result = Process(Path.GetFileName(filePath));
        float duration = 0f, lufsApprox = 0f;
        float? realLufs = null, truePeakDbtp = null;
        try { (duration, realLufs, truePeakDbtp, lufsApprox) = AudioTrackMetadataStore.AnalyzeAudioFile(filePath); }
        catch { /* unreadable/corrupt audio file -- leave computed fields null, title/team/trigger suggestions still stand */ }

        return new AudioTrackMetadata
        {
            StandardTitle = result.CleanedTitle == "Unknown" ? null : result.CleanedTitle,
            SchoolAbbreviation = result.Team == "Unknown" ? null : result.Team,
            EnergyLevel = GuessEnergyLevel(result.PrimaryTrigger),
            PrimaryGameTriggerEvent = result.SuggestedEventKeys.Length > 0 ? result.SuggestedEventKeys[0] : null,
            DurationSeconds = duration > 0 ? duration : null,
            IntegratedLufs = duration > 0 ? realLufs : null,
            TruePeakDbtp = duration > 0 ? truePeakDbtp : null,
            IntegratedLufsApprox = duration > 0 ? lufsApprox : null,
        };
    }

    private static readonly HashSet<string> HighEnergyTriggers = new(StringComparer.Ordinal)
        { "TD_HOME", "BIG_PLAY", "TURNOVER", "INTERCEPTION", "FUMBLE_RECOVERY", "GENERAL_HYPE" };
    private static readonly HashSet<string> LowEnergyTriggers = new(StringComparer.Ordinal)
        { "TIMEOUT", "PRE_SNAP", "PENALTY" };

    private static string GuessEnergyLevel(string trigger) =>
        HighEnergyTriggers.Contains(trigger) ? "High" : LowEnergyTriggers.Contains(trigger) ? "Low" : "Mid";

    /// <summary>True if `alias` appears in `lowerText` without being embedded in a longer run of
    /// letters on either side -- e.g. "nw" matches "b1g nw '21" (space-delimited) but not inside
    /// "answer". Digits/punctuation/apostrophes are fine neighbors (matches "clem4thdown"'s
    /// glued-to-a-digit style on purpose, see GuessTeamToken's original design note) -- this is
    /// deliberately looser than a strict regex \b word boundary (where letter-to-digit isn't a
    /// boundary at all) so that still works, while finally making short 2-letter aliases like
    /// "NW"/"UM"/"UW" safe to check at all (they were blanket-excluded below length 3 before,
    /// which is why Northwestern silently never matched despite being in the alias table).</summary>
    private static bool ContainsAliasToken(string lowerText, string aliasLower)
    {
        int idx = 0;
        while ((idx = lowerText.IndexOf(aliasLower, idx, StringComparison.Ordinal)) >= 0)
        {
            bool leftOk = idx == 0 || !char.IsLetter(lowerText[idx - 1]);
            int endIdx = idx + aliasLower.Length;
            bool rightOk = endIdx >= lowerText.Length || !char.IsLetter(lowerText[endIdx]);
            if (leftOk && rightOk) return true;
            idx++;
        }
        return false;
    }

    /// <summary>Recognizes a conference name baked into the filename (e.g. pack files named
    /// "b1g ala '21 ...") so ResolveTeam can pick the right team when an alias is genuinely
    /// ambiguous across conferences (see ResolveTeam's conferenceHint doc). Best-effort only --
    /// null just means no hint was found, ResolveTeam falls back to its default candidate.</summary>
    private static readonly (string token, string conference)[] ConferenceHintTokens =
    {
        ("b1g", "Big Ten"), ("bigten", "Big Ten"),
        ("sec", "SEC"),
        ("acc", "ACC"),
        ("big12", "Big 12"),
        ("pac12", "Pac-12"), ("pac", "Pac-12"),
    };

    private static string? GuessConferenceHint(string filename)
    {
        var lower = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
        foreach (var (token, conference) in ConferenceHintTokens)
            if (ContainsAliasToken(lower, token)) return conference;
        return null;
    }

    /// <summary>Best-effort scan of a raw filename for a team name/abbreviation token, so
    /// ResolveTeam has something to try even though ImportLocalSongFromWeb doesn't collect an
    /// explicit team field today (see ConfigStore.cs -- team is implied by profile file, not a
    /// TriggerEntry field). Checks the whole registry rather than word-splitting, since team
    /// names/abbreviations can appear glued to other text (e.g. "clem4thdown"). Returns the raw
    /// alias/name token found, NOT a pre-resolved team -- ResolveTeam owns actual resolution
    /// (including conference-hint disambiguation for an alias multiple teams claim), so this
    /// can't shortcut past that by resolving the alias itself.</summary>
    private static string GuessTeamToken(string filename)
    {
        EnsureLoaded();
        var lower = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();

        foreach (var teamName in _teams!.Keys)
        {
            if (lower.Contains(teamName.ToLowerInvariant()))
                return teamName;
        }
        foreach (var alias in _aliasIndex!.Keys)
        {
            if (ContainsAliasToken(lower, alias.ToLowerInvariant()))
                return alias;
        }
        return "";
    }
}
