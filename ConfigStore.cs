using System.Collections.Concurrent;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

internal static class ConfigStore
{
    // Everything the user actually owns -- songs, profiles, team backgrounds, trigger config --
    // lives in ONE folder next to (not inside) the versioned app-X.X.X install folder. Squirrel
    // deletes app-X.X.X wholesale on every update, so anything stored under AppContext.BaseDirectory
    // (the old behavior) got silently wiped on every single update. This folder is the parent of
    // that versioned folder and Squirrel never touches it.
    public static readonly string UserDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bandroom", "UserData");

    public static readonly string ConfigPath = Path.Combine(UserDataRoot, "triggers.json");
    public static readonly string SongsFolder = Path.Combine(UserDataRoot, "Songs");
    /// <summary>Single-slot scratch folder the embedded web trimmer copies whatever's being
    /// trimmed into (see WebMainForm.PrepareTrimFromWeb) -- exists so the WebView2 page has a
    /// virtual-host-mappable URL to fetch/decode for its waveform, since there's no mapping for
    /// arbitrary local paths (SongsFolder/SongsTrimmedFolder/etc aren't safe to expose wholesale).
    /// Cleared and re-filled on every trim-panel open; never holds more than one file.</summary>
    public static readonly string TrimSourceFolder = Path.Combine(UserDataRoot, "TrimSource");
    /// <summary>Trimmed clips (from TrimmerForm's "Save & Name") land here, not loose in
    /// SongsFolder -- keeps user-trimmed cues visually separate from raw uploaded files.</summary>
    public static readonly string SongsTrimmedFolder = Path.Combine(SongsFolder, "trimmed");
    /// <summary>Raw files a user browses/imports (untrimmed) land here. Only created when they
    /// actually import a new file -- picking an existing library track never copies anything,
    /// so storage isn't duplicated on every load.</summary>
    public static readonly string SongsUploadedFolder = Path.Combine(SongsFolder, "uploaded");
    /// <summary>End-user "import my own song" pipeline (item 21) lands trimmed+normalized clips
    /// here -- separate from SongsTrimmedFolder (trims of an already-assigned/marketplace track)
    /// so these have their own virtual host mapping (see WebMainForm's "localtracks") and their
    /// own manifest (local_tracks.json below) for the My Downloads tab's "Share to Marketplace"
    /// button, which only ever applies to tracks that came through THIS pipeline.</summary>
    public static readonly string LocalTracksFolder = Path.Combine(SongsFolder, "local");
    /// <summary>Single global lead-in whistle clip (TrimmerForm's "Set as Lead-In Whistle"
    /// writes here, always the same filename -- there's only ever one active whistle at a time,
    /// same "single global setting" model as AudioPlayer.CurrentReverb/PreRollSeconds).</summary>
    public static readonly string LeadInWhistlePath = Path.Combine(SongsFolder, "leadin_whistle.wav");
    public static readonly string ProfilesFolder = Path.Combine(UserDataRoot, "Profiles");
    /// <summary>One-time marker for the library-wide loudness-normalization sweep (owner request:
    /// "all songs the same volume", applied retroactively to everything assigned before
    /// LoudnessNormalizationService existed/was wired into the assign flow -- see
    /// WebMainForm.NormalizeExistingLibraryOnce). Presence alone means "already ran"; content is
    /// unused. New assignments going forward are already normalized on the spot via
    /// NormalizeAssignmentInBackground, so this only ever needs to run once per install.</summary>
    public static readonly string LibraryNormalizedMarkerPath = Path.Combine(UserDataRoot, "library_normalized_v1.marker");
    public static readonly string TeamBackgroundsFolder = Path.Combine(UserDataRoot, "TeamBackgrounds");
    public static readonly string TeamLogosFolder = Path.Combine(UserDataRoot, "TeamLogos");
    /// <summary>Optional icon-only variant of a team's logo (no name-banner text) -- used only by
    /// the small tile spots (matchup side-grid, main team-select grid, events side-bar) where the
    /// baked-in text reads too small to matter. Falls back to TeamLogosFolder's full logo when
    /// absent, so this is purely additive and never required. Local-only for now, not part of the
    /// CustomTeamLogos cross-device/public sync triangle.</summary>
    public static readonly string TeamIconsFolder = Path.Combine(TeamLogosFolder, "Icons");
    /// <summary>Images downloaded from the marketplace via the "My Downloads" tab land here --
    /// separate from TeamBackgroundsFolder, which holds only the ONE currently-active background
    /// per team. A downloaded image doesn't become a team's live background until the user
    /// explicitly picks it (still via TeamBackgroundDownloadService's existing flow), so mixing
    /// the two folders would make FindImagePath's single-active-file convention ambiguous.</summary>
    public static readonly string DownloadedImagesFolder = Path.Combine(UserDataRoot, "DownloadedImages");
    static readonly string MarketplaceDownloadsManifestPath = Path.Combine(UserDataRoot, "marketplace_downloads.json");
    static readonly string LocalTracksManifestPath = Path.Combine(UserDataRoot, "local_tracks.json");
    static readonly string AuthSessionPath = Path.Combine(UserDataRoot, "auth_session.json");
    static readonly string UserProfilePath = Path.Combine(UserDataRoot, "user_profile.json");
    static readonly string FirstRunFlagPath = Path.Combine(UserDataRoot, ".firstrun_done");
    static readonly string ScorebugPresetPath = Path.Combine(UserDataRoot, "scorebug_preset.txt");
    /// <summary>The single scorebug-size choice asked once at GAMETIME instead of exposing any of
    /// the reader's own settings UI -- see LoadSavedScorebugSize's doc comment.</summary>
    static readonly string ScorebugSizeChoicePath = Path.Combine(UserDataRoot, "scorebug_size_choice.txt");
    /// <summary>One-time opt-in for the reader's RAM-read mode (default off -- see
    /// ScoreboardReaderHost's doc comment: RAM mode reads CFB27's own process memory, which the
    /// reader's own instructions restrict to "offline/modded play only"). Presence of "true" means
    /// the user has already seen and accepted the one-time warning dialog.</summary>
    static readonly string ScoreboardReaderRamModeEnabledPath = Path.Combine(UserDataRoot, "scoreboard_reader_ram_enabled.txt");
    /// <summary>Owner request 2026-08-13: console/remote-play streamers have no local CFB27
    /// process to read memory from, so RAM mode is moot for them -- this is a standing "never even
    /// try" preference, separate from ScoreboardReaderRamModeEnabledPath's own opt-in accuracy
    /// toggle, so flipping this off never has the side effect of opting a user INTO RAM mode.
    /// False by default (most players are on PC/local).</summary>
    static readonly string RemotePlayModePath = Path.Combine(UserDataRoot, "remote_play_mode.txt");
    /// <summary>User-created "TeamBuilder" schools (custom/team-builder "add school" v1) --
    /// name + colors only, never mapped to in-game OCR/matching. Kept in their own manifest
    /// rather than folded into triggers.json/user_profile.json so TeamColors can load them at
    /// static-init time without depending on the rest of ConfigStore's per-user state.</summary>
    static readonly string CustomTeamsPath = Path.Combine(UserDataRoot, "custom_teams.json");
    /// <summary>Supabase project URL + anon key for CloudDatabaseService (BANDROOM_STREAMER_MASTER_PROMPT.md
    /// System 1). The anon key is NOT a secret by Supabase's own design (it's meant to be embedded
    /// in client apps; row-level security is what actually gates access), same "not a secret,
    /// still not hardcoded" treatment ADMIN_TOKEN-style values get elsewhere in this file.</summary>
    static readonly string SupabaseSettingsPath = Path.Combine(UserDataRoot, "supabase_settings.json");
    static readonly object CustomTeamsLock = new();

    public sealed record CustomTeamEntry
    {
        public string Name { get; init; } = "";
        public string PrimaryHex { get; init; } = "#22d3ee";
        public string SecondaryHex { get; init; } = "#22d3ee";
        public string Mascot { get; init; } = "";
    }

    public static List<CustomTeamEntry> LoadCustomTeams()
    {
        lock (CustomTeamsLock)
        {
            if (!File.Exists(CustomTeamsPath)) return new List<CustomTeamEntry>();
            try
            {
                string json = File.ReadAllText(CustomTeamsPath);
                return JsonSerializer.Deserialize<List<CustomTeamEntry>>(json, JsonOptions) ?? new List<CustomTeamEntry>();
            }
            catch { return new List<CustomTeamEntry>(); } // corrupt manifest shouldn't crash team loading
        }
    }

    /// <summary>Adds (or replaces, by case-insensitive name) one custom team and persists the
    /// whole manifest. Caller (TeamColors.AddCustomTeam) is responsible for keeping its in-memory
    /// list in sync -- this only owns the on-disk copy.</summary>
    public static void SaveCustomTeam(string name, string primaryHex, string secondaryHex, string mascot = "")
    {
        lock (CustomTeamsLock)
        {
            List<CustomTeamEntry> entries;
            if (File.Exists(CustomTeamsPath))
            {
                try { entries = JsonSerializer.Deserialize<List<CustomTeamEntry>>(File.ReadAllText(CustomTeamsPath), JsonOptions) ?? new(); }
                catch { entries = new List<CustomTeamEntry>(); }
            }
            else entries = new List<CustomTeamEntry>();

            entries.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            entries.Add(new CustomTeamEntry { Name = name, PrimaryHex = primaryHex, SecondaryHex = secondaryHex, Mascot = mascot });

            Directory.CreateDirectory(UserDataRoot);
            AtomicWriteAllText(CustomTeamsPath, JsonSerializer.Serialize(entries, JsonOptions));
        }
    }
    static readonly string LeadInWhistleEnabledPath = Path.Combine(UserDataRoot, "leadin_whistle_enabled.txt");

    /// <summary>REDEFINED 2026-08-10: this used to be an auto-detect rule ("quarter 4 and score
    /// within 8 points" boosted cue volume 80-&gt;100). Replaced entirely at the owner's request --
    /// "BigGame" now means something structurally different: a MANUAL per-game toggle for "both
    /// teams' full bands are physically present" (e.g. Bama @ LSU), as opposed to a normal game
    /// where the away team only sends a small travel pep band. The owner is a real band member and
    /// wants this to gate whether the away side plays situational cues at all, not just how loud --
    /// see WebMainForm.OnEngineEventsDetected's away-side routing. `Enabled` is now literally
    /// "is this currently a Big Game" (the user flips it on before kickoff of a real one, off
    /// otherwise) -- QuarterThreshold/ScoreMargin are DEAD FIELDS kept only so old saved
    /// big_game_settings.json files still deserialize without a migration step; nothing reads them
    /// anymore (GameWatcher.cs's isBigGame computation no longer references either). Default
    /// changed true-&gt;false: the old default was harmless (auto-boost, always safe to leave on),
    /// but defaulting the new manual toggle to true would silently play every away-side event at
    /// full volume for every ordinary game, which is exactly the "small pep band" case this flag
    /// is supposed to exclude.</summary>
    static readonly string BigGameSettingsPath = Path.Combine(UserDataRoot, "big_game_settings.json");
    static readonly string PlaybackModePath = Path.Combine(UserDataRoot, "playback_mode.json");

    public record BigGameSettings(bool Enabled, int QuarterThreshold, int ScoreMargin);

    // Cached in memory -- GameWatcher's OCR loop re-checks this every frame during a live game,
    // and re-reading a JSON file off disk that often is wasteful for a value that only ever
    // changes when the user hits Save in the Big Game Rules panel.
    static BigGameSettings? _bigGameSettingsCache;

    public static BigGameSettings LoadBigGameSettings()
    {
        if (_bigGameSettingsCache != null) return _bigGameSettingsCache;
        if (!File.Exists(BigGameSettingsPath)) return _bigGameSettingsCache = new BigGameSettings(false, 4, 8);
        try
        {
            var loaded = JsonSerializer.Deserialize<BigGameSettings>(File.ReadAllText(BigGameSettingsPath), JsonOptions);
            return _bigGameSettingsCache = loaded ?? new BigGameSettings(false, 4, 8);
        }
        catch
        {
            return _bigGameSettingsCache = new BigGameSettings(false, 4, 8);
        }
    }

    public static void SaveBigGameSettings(BigGameSettings settings)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(BigGameSettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        _bigGameSettingsCache = settings;
    }

    // FCS mode (default): normal FBS/FCS roster + the per-event trigger system, same as always.
    // HBCU mode narrows the team chooser/favorite picker to the SWAC/MEAC roster (TeamColors.
    // HbcuTeamNames; matchup screens still show every team, since an HBCU band can play any
    // opponent) AND switches playback to HbcuPlaybackService's continuous pool shuffle -- see
    // HbcuPool below for the per-team song list that drives it, and Touchdown/Kickoff/Runout for
    // the only three events still routed through the normal per-event trigger system in this mode.
    public enum PlaybackMode { Fcs, Hbcu }

    public record PlaybackModeSettings(PlaybackMode Mode);

    static PlaybackModeSettings? _playbackModeCache;

    public static PlaybackMode LoadPlaybackMode()
    {
        if (_playbackModeCache != null) return _playbackModeCache.Mode;
        if (!File.Exists(PlaybackModePath)) return (_playbackModeCache = new PlaybackModeSettings(PlaybackMode.Fcs)).Mode;
        try
        {
            var loaded = JsonSerializer.Deserialize<PlaybackModeSettings>(File.ReadAllText(PlaybackModePath), JsonOptions);
            return (_playbackModeCache = loaded ?? new PlaybackModeSettings(PlaybackMode.Fcs)).Mode;
        }
        catch
        {
            return (_playbackModeCache = new PlaybackModeSettings(PlaybackMode.Fcs)).Mode;
        }
    }

    public static void SavePlaybackMode(PlaybackMode mode)
    {
        Directory.CreateDirectory(UserDataRoot);
        var settings = new PlaybackModeSettings(mode);
        AtomicWriteAllText(PlaybackModePath, JsonSerializer.Serialize(settings, JsonOptions));
        _playbackModeCache = settings;
    }

    // Per-team color OVERRIDE -- none of TeamColors.cs's hardcoded hex values are real CFB27
    // roster colors (owner confirmed every team here, HBCU or otherwise, is a custom in-game
    // roster/uniform they built themselves), so WebMainForm.ResolveTeamColor's scoreboard OCR
    // match can only ever work once the owner enters the colors they ACTUALLY set in-game. This
    // store lets any team's Primary/Secondary be overridden without touching the hardcoded arrays
    // -- TeamColors.BuildAll applies these on top of Base/FcsTeams/HbcuTeams/custom teams.
    static readonly string TeamColorOverridesPath = Path.Combine(UserDataRoot, "team_color_overrides.json");
    public record TeamColorOverride(string PrimaryHex, string SecondaryHex);
    public record TeamColorOverrides(Dictionary<string, TeamColorOverride> ByTeam);
    static TeamColorOverrides? _teamColorOverridesCache;

    public static TeamColorOverrides LoadTeamColorOverrides()
    {
        if (_teamColorOverridesCache != null) return _teamColorOverridesCache;
        if (!File.Exists(TeamColorOverridesPath)) return _teamColorOverridesCache = new TeamColorOverrides(new());
        try
        {
            var loaded = JsonSerializer.Deserialize<TeamColorOverrides>(File.ReadAllText(TeamColorOverridesPath), JsonOptions);
            return _teamColorOverridesCache = loaded ?? new TeamColorOverrides(new());
        }
        catch
        {
            return _teamColorOverridesCache = new TeamColorOverrides(new());
        }
    }

    /// <summary>Saves the override and invalidates TeamColors' cached roster so the next
    /// TeamColors.All read reflects it immediately -- no restart needed.</summary>
    public static void SaveTeamColorOverride(string teamName, string primaryHex, string secondaryHex)
    {
        var overrides = LoadTeamColorOverrides();
        overrides.ByTeam[teamName] = new TeamColorOverride(primaryHex, secondaryHex);
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(TeamColorOverridesPath, JsonSerializer.Serialize(overrides, JsonOptions));
        _teamColorOverridesCache = overrides;
        TeamColors.InvalidateRoster();
    }

    // HBCU mode's "Team Pot" -- an unlimited, freely add/remove list of songs per team that
    // HbcuPlaybackService shuffles through all game, distinct from every other TriggerEntry slot
    // (which holds exactly one file). Falls back to GetPackFilesForSchool only if a team's pot is
    // still empty (nothing added yet). Each entry carries the SAME per-song playback settings a
    // TriggerEntry does (whistle/speed/PA effect/fade/no-fade/volume) -- owner request: "the pot
    // needs the same settings as the FBS event cards" -- kept as its own small record rather than
    // reusing TriggerEntry itself, since a pot entry has no Trigger/Event/PaAudioFile/
    // BigGameAudioFile (those only make sense for a single fixed game-event slot).
    public class HbcuPotSong
    {
        public string FilePath { get; set; } = "";
        public int Volume { get; set; } = 100;
        public bool PlayLeadInWhistle { get; set; } = true;
        public double WhistleSpeed { get; set; } = 1.0;
        public bool PlaybackSpeed2x { get; set; } = false;
        public bool PaSpeakerEffect { get; set; } = false;
        public double? FadeStartSecondsOverride { get; set; } = null;
        public double? FadeOutDurationOverride { get; set; } = null;
        public bool NoFade { get; set; } = true; // pot songs default to full-song playback, matching HBCU mode's "no clipped fade-outs" behavior
    }

    static readonly string HbcuPotsPath = Path.Combine(UserDataRoot, "hbcu_pots.json");
    public record HbcuPots(Dictionary<string, List<HbcuPotSong>> ByTeam);
    static HbcuPots? _hbcuPotsCache;

    static HbcuPots LoadHbcuPots()
    {
        if (_hbcuPotsCache != null) return _hbcuPotsCache;
        if (!File.Exists(HbcuPotsPath)) return _hbcuPotsCache = new HbcuPots(new());
        try
        {
            var loaded = JsonSerializer.Deserialize<HbcuPots>(File.ReadAllText(HbcuPotsPath), JsonOptions);
            return _hbcuPotsCache = loaded ?? new HbcuPots(new());
        }
        catch
        {
            return _hbcuPotsCache = new HbcuPots(new());
        }
    }

    static void SaveHbcuPots(HbcuPots pots)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(HbcuPotsPath, JsonSerializer.Serialize(pots, JsonOptions));
        _hbcuPotsCache = pots;
    }

    public static List<HbcuPotSong> GetHbcuPot(string teamName)
    {
        var pots = LoadHbcuPots();
        return pots.ByTeam.TryGetValue(teamName, out var songs) ? songs.Where(s => File.Exists(s.FilePath)).ToList() : new List<HbcuPotSong>();
    }

    /// <summary>Owner request 2026-08-14: a song added to a Team Pot from the Clipper's "+ Add
    /// Song" modal should also show up in My Downloads, same as any other song brought into the
    /// app -- lets it get reused (assigned to a normal event card, shared to marketplace) without
    /// having to re-import it. Skipped for a file that's already a marketplace download or already
    /// has its own My Downloads entry (RecordLocalTrack's own path-based dedupe would just
    /// silently overwrite that entry's Shared/Type/CreatedAt otherwise -- see its doc comment on
    /// never mixing the two sources).</summary>
    public static void AddToHbcuPot(string teamName, string filePath)
    {
        var pots = LoadHbcuPots();
        if (!pots.ByTeam.TryGetValue(teamName, out var songs)) pots.ByTeam[teamName] = songs = new List<HbcuPotSong>();
        if (!songs.Any(s => string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            songs.Add(new HbcuPotSong { FilePath = filePath });
        SaveHbcuPots(pots);

        bool alreadyTracked = LoadMarketplaceDownloads().Any(d => string.Equals(d.Path, filePath, StringComparison.OrdinalIgnoreCase))
            || LoadLocalTracks().Any(t => string.Equals(t.Path, filePath, StringComparison.OrdinalIgnoreCase));
        if (!alreadyTracked && File.Exists(filePath))
            RecordLocalTrack(Path.GetFileNameWithoutExtension(filePath), filePath);
    }

    public static void RemoveFromHbcuPot(string teamName, string filePath)
    {
        var pots = LoadHbcuPots();
        if (pots.ByTeam.TryGetValue(teamName, out var songs) && songs.RemoveAll(s => string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) > 0)
            SaveHbcuPots(pots);
    }

    /// <summary>Overwrites one pot entry's settings in place (whistle/speed/PA effect/fade/no-
    /// fade/volume) -- same "Event Settings" popover the FCS event cards use, just targeting a
    /// pot song by file path instead of a TriggerEntry. No-ops if the file isn't in this team's
    /// pot (e.g. a stale client-side call after it was already removed).</summary>
    public static void UpdateHbcuPotSongSettings(string teamName, HbcuPotSong updated)
    {
        var pots = LoadHbcuPots();
        if (!pots.ByTeam.TryGetValue(teamName, out var songs)) return;
        int idx = songs.FindIndex(s => string.Equals(s.FilePath, updated.FilePath, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        songs[idx] = updated;
        SaveHbcuPots(pots);
    }

    /// <summary>Trim/rename support for pot songs (mirrors WebMainForm.RenameAssignedTrackFromWeb/
    /// MoveAssignedTrackToHbcuFolderFromWeb's "retarget the stored path" pattern): swaps a pot
    /// entry's FilePath from oldPath to newPath in place, e.g. after TrimmerForm writes a trimmed
    /// copy. No-ops if oldPath isn't in this team's pot.</summary>
    public static void RetargetHbcuPotSong(string teamName, string oldPath, string newPath)
    {
        var pots = LoadHbcuPots();
        if (!pots.ByTeam.TryGetValue(teamName, out var songs)) return;
        var song = songs.FirstOrDefault(s => string.Equals(s.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));
        if (song == null) return;
        song.FilePath = newPath;
        SaveHbcuPots(pots);
    }

    // Added 2026-08-14 (owner request): explicit per-team toggle so a team with no HBCU pot/pack
    // of its own (typically the FBS/non-HBCU opponent in a matchup) can be pointed at a shared
    // "Generic" pool instead of sitting silent all game. Deliberately an EXPLICIT picker, not an
    // automatic empty-pot fallback -- owner wants to be able to force Generic even for a team that
    // already has some songs, not just as a last resort. "Generic" itself is just a sentinel team-
    // name string -- GetHbcuPot("Generic")/GetPackFilesForSchool("Generic")/AddToHbcuPot("Generic",
    // ...) all already work unmodified (no schema special-casing needed, same as any real team).
    static readonly string HbcuGenericPackTeamsPath = Path.Combine(UserDataRoot, "hbcu_generic_pack_teams.json");
    static HashSet<string>? _hbcuGenericPackTeamsCache;

    static HashSet<string> LoadHbcuGenericPackTeams()
    {
        if (_hbcuGenericPackTeamsCache != null) return _hbcuGenericPackTeamsCache;
        if (!File.Exists(HbcuGenericPackTeamsPath))
            return _hbcuGenericPackTeamsCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var loaded = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(HbcuGenericPackTeamsPath), JsonOptions);
            return _hbcuGenericPackTeamsCache = loaded != null
                ? new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return _hbcuGenericPackTeamsCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool GetHbcuUseGenericPack(string teamName) => LoadHbcuGenericPackTeams().Contains(teamName);

    public static void SetHbcuUseGenericPack(string teamName, bool useGeneric)
    {
        var teams = LoadHbcuGenericPackTeams();
        bool changed = useGeneric ? teams.Add(teamName) : teams.Remove(teamName);
        if (!changed) return;
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(HbcuGenericPackTeamsPath, JsonSerializer.Serialize(teams, JsonOptions));
        _hbcuGenericPackTeamsCache = teams;
    }

    /// <summary>SongsFolder/HBCU/{school}/ -- one subfolder per HBCU roster school (see
    /// TeamColors.HbcuTeamNames), so a user uploading a track and marking it "for an HBCU" has a
    /// real place on disk for that school's songs to land, browsable outside the app too.</summary>
    public static string HbcuSchoolFolder(string schoolName) =>
        Path.Combine(SongsFolder, "HBCU", SanitizeFileNamePart(schoolName));

    static string SanitizeFileNamePart(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>Creates SongsFolder/HBCU/{school}/ for every school in TeamColors.HbcuTeamNames
    /// that doesn't already have one. Safe to call repeatedly (CreateDirectory is a no-op if it
    /// already exists) -- called once at app startup so the folders are there immediately, before
    /// any upload happens.</summary>
    public static void EnsureHbcuSchoolFolders()
    {
        foreach (var school in TeamColors.HbcuTeamNames)
            Directory.CreateDirectory(HbcuSchoolFolder(school));
    }

    /// <summary>HbcuPlaybackService's fallback pool when a team has no songs assigned to any
    /// trigger slot yet: every audio file in that school's HbcuSchoolFolder, plus (for songs
    /// uploaded before that folder convention existed) anything elsewhere under SongsFolder/the
    /// downloaded default pack whose .meta.json sidecar tags it to this school via
    /// AudioTrackMetadata.SchoolAbbreviation.</summary>
    public static List<string> GetPackFilesForSchool(string schoolName)
    {
        var result = new List<string>();
        string schoolFolder = HbcuSchoolFolder(schoolName);
        if (Directory.Exists(schoolFolder))
            result.AddRange(Directory.EnumerateFiles(schoolFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)));

        foreach (var root in new[] { SongsFolder, DownloadedDefaultSongsFolder })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.StartsWith(schoolFolder, StringComparison.OrdinalIgnoreCase)) continue; // already added above
                var meta = AudioTrackMetadataStore.Load(file);
                if (meta?.SchoolAbbreviation != null && meta.SchoolAbbreviation.Equals(schoolName, StringComparison.OrdinalIgnoreCase))
                    result.Add(file);
            }
        }
        return result;
    }

    /// <summary>The most recently confirmed GAMETIME matchup (home/away team names), so the
    /// matchup dialog can offer a one-click "Last: Away @ Home" pill instead of re-picking both
    /// teams from scratch every time -- same "static class, simple lock, JSON file under
    /// UserDataRoot" shape as BigGameSettings above. Null until the first GAMETIME of all time.</summary>
    static readonly string LastMatchupPath = Path.Combine(UserDataRoot, "last_matchup.json");

    public record LastMatchup(string HomeName, string AwayName);

    static LastMatchup? _lastMatchupCache;

    public static LastMatchup? LoadLastMatchup()
    {
        if (_lastMatchupCache != null) return _lastMatchupCache;
        if (!File.Exists(LastMatchupPath)) return null;
        try
        {
            return _lastMatchupCache = JsonSerializer.Deserialize<LastMatchup>(File.ReadAllText(LastMatchupPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLastMatchup(string homeName, string awayName)
    {
        var settings = new LastMatchup(homeName, awayName);
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(LastMatchupPath, JsonSerializer.Serialize(settings, JsonOptions));
        _lastMatchupCache = settings;
    }

    static readonly string BandDirectorDashboardSettingsPath = Path.Combine(UserDataRoot, "band_director_dashboard_settings.json");

    /// <summary>Phase 1 of the Band Director streamer dashboard (see
    /// BANDROOM_STREAMER_MASTER_PROMPT.md SYSTEM 2). QuickTriggerMap keys are the 8 quick-trigger
    /// slot ids ("1".."8") mapped to an engine EventKey (e.g. "Offense: Touchdown Scored"); empty
    /// string = unassigned. Everything else in the dashboard (Twitch/YouTube connection, chat
    /// commands, polls, guest DJ) is mock-only in this phase and has no persisted config yet.</summary>
    public record BandDirectorDashboardSettings(Dictionary<string, string> QuickTriggerMap)
    {
        public static readonly BandDirectorDashboardSettings Default = new(new Dictionary<string, string> {
            ["1"] = "", ["2"] = "", ["3"] = "", ["4"] = "", ["5"] = "", ["6"] = "", ["7"] = "", ["8"] = "",
        });
    }

    static BandDirectorDashboardSettings? _bandDirectorDashboardSettingsCache;

    public static BandDirectorDashboardSettings LoadBandDirectorDashboardSettings()
    {
        if (_bandDirectorDashboardSettingsCache != null) return _bandDirectorDashboardSettingsCache;
        if (!File.Exists(BandDirectorDashboardSettingsPath)) return _bandDirectorDashboardSettingsCache = BandDirectorDashboardSettings.Default;
        try
        {
            var loaded = JsonSerializer.Deserialize<BandDirectorDashboardSettings>(File.ReadAllText(BandDirectorDashboardSettingsPath), JsonOptions);
            return _bandDirectorDashboardSettingsCache = loaded ?? BandDirectorDashboardSettings.Default;
        }
        catch
        {
            return _bandDirectorDashboardSettingsCache = BandDirectorDashboardSettings.Default;
        }
    }

    public static void SaveBandDirectorDashboardSettings(BandDirectorDashboardSettings settings)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(BandDirectorDashboardSettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        _bandDirectorDashboardSettingsCache = settings;
    }

    static readonly string PlaybackTimingSettingsPath = Path.Combine(UserDataRoot, "playback_timing_settings.json");

    /// <summary>Settings-modal migration (native SettingsForm.cs -> themed Profile overlay): these
    /// 4 fields used to only live in AudioPlayer/GameWatcher's in-memory statics with no disk
    /// persistence at all, so every relaunch silently reset them to defaults even after the owner
    /// used "Apply Timing" in the old dialog. Defaults here match those statics' own defaults
    /// (AudioPlayer.cs: PreRollSeconds=0.0, FadeStartSeconds=10.0, FadeOutDuration=4.5;
    /// GameWatcher.cs: Cooldown=2.0s).</summary>
    public record PlaybackTimingSettings(double PreRollSeconds, double FadeStartSeconds, double FadeOutDuration, double CooldownSeconds, double PregameRunoutDelaySeconds = 15.0)
    {
        public static readonly PlaybackTimingSettings Default = new(0.0, 10.0, 4.5, 2.0, 15.0);
    }

    static PlaybackTimingSettings? _playbackTimingSettingsCache;

    public static PlaybackTimingSettings LoadPlaybackTimingSettings()
    {
        if (_playbackTimingSettingsCache != null) return _playbackTimingSettingsCache;
        if (!File.Exists(PlaybackTimingSettingsPath)) return _playbackTimingSettingsCache = PlaybackTimingSettings.Default;
        try
        {
            var loaded = JsonSerializer.Deserialize<PlaybackTimingSettings>(File.ReadAllText(PlaybackTimingSettingsPath), JsonOptions);
            return _playbackTimingSettingsCache = loaded ?? PlaybackTimingSettings.Default;
        }
        catch
        {
            return _playbackTimingSettingsCache = PlaybackTimingSettings.Default;
        }
    }

    public static void SavePlaybackTimingSettings(PlaybackTimingSettings settings)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(PlaybackTimingSettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        _playbackTimingSettingsCache = settings;
    }

    static readonly string AppWindowSettingsPath = Path.Combine(UserDataRoot, "app_window_settings.json");

    /// <summary>AlwaysOnTop used to be in-memory only (WebMainForm.TopMost, set once from the old
    /// native SettingsForm's checkbox, never persisted).</summary>
    public record AppWindowSettings(bool AlwaysOnTop)
    {
        public static readonly AppWindowSettings Default = new(false);
    }

    static AppWindowSettings? _appWindowSettingsCache;

    public static AppWindowSettings LoadAppWindowSettings()
    {
        if (_appWindowSettingsCache != null) return _appWindowSettingsCache;
        if (!File.Exists(AppWindowSettingsPath)) return _appWindowSettingsCache = AppWindowSettings.Default;
        try
        {
            var loaded = JsonSerializer.Deserialize<AppWindowSettings>(File.ReadAllText(AppWindowSettingsPath), JsonOptions);
            return _appWindowSettingsCache = loaded ?? AppWindowSettings.Default;
        }
        catch
        {
            return _appWindowSettingsCache = AppWindowSettings.Default;
        }
    }

    public static void SaveAppWindowSettings(AppWindowSettings settings)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(AppWindowSettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        _appWindowSettingsCache = settings;
    }

    /// <summary>Owner report: volume sliders (Master/Home/Away/PA/Whistle) reset to 100% every
    /// launch -- AudioPlayer.cs's entire volume surface is plain in-memory static state with zero
    /// disk persistence (confirmed via Session 34's investigation: grepped ConfigStore.cs for
    /// "Volume", zero hits before this). Same JSON-file-in-UserDataRoot pattern as
    /// BigGameSettings above. All five ship as percentages (0-100 ints, matching the *FromWeb
    /// bridge methods' own units) rather than the 0.0-1.0 floats AudioPlayer uses internally, so
    /// the JSON stays human-readable and matches what the UI sliders actually show.</summary>
    static readonly string AudioSettingsPath = Path.Combine(UserDataRoot, "audio_settings.json");

    // REMOVED 2026-08-11: the "Sound Start Delay" feature (ms, 0-5000, between an event firing
    // and its assigned sound actually starting) was dropped entirely -- owner's explicit call.
    // No migration needed: System.Text.Json ignores unknown JSON properties by default, so an
    // existing settings file on disk with a leftover "SoundStartDelayMs" key just gets ignored on
    // next load rather than erroring.
    public record AudioSettings(int MasterVolume, int HomeVolume, int AwayVolume, int PaVolume, int WhistleVolume)
    {
        public static readonly AudioSettings Default = new(72, 100, 100, 100, 100);
    }

    static AudioSettings? _audioSettingsCache;

    public static AudioSettings LoadAudioSettings()
    {
        if (_audioSettingsCache != null) return _audioSettingsCache;
        if (!File.Exists(AudioSettingsPath)) return _audioSettingsCache = AudioSettings.Default;
        try
        {
            var loaded = JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(AudioSettingsPath), JsonOptions);
            return _audioSettingsCache = loaded ?? AudioSettings.Default;
        }
        catch
        {
            return _audioSettingsCache = AudioSettings.Default;
        }
    }

    public static void SaveAudioSettings(AudioSettings settings)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(AudioSettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        _audioSettingsCache = settings;
    }

    /// <summary>Where the installer would have bundled the default song pack, if this build
    /// includes it (dev builds and any future full build still do -- see BundleDefaultSongs in
    /// BandAudioHook.csproj). Public releases from v1.0.48 on don't, so this is empty there.</summary>
    static readonly string BundledDefaultSongsFolder = Path.Combine(AppContext.BaseDirectory, "Songs", "Default");
    /// <summary>Default location DefaultSongPackService extracts the pack into after the user
    /// opts into the one-time download (see cloudflare-defaultsongs). Lives under UserDataRoot
    /// so, like everything else there, Squirrel updates never touch or wipe it.</summary>
    static readonly string DefaultDownloadedDefaultSongsFolder = Path.Combine(UserDataRoot, "DefaultSongs");

    /// <summary>Where a user-relocated song pack folder path is persisted (task queue item 7b,
    /// Session 10) -- a single line of plain text, same "tiny marker file under UserDataRoot"
    /// pattern as ScorebugPresetPath/LeadInWhistleEnabledPath above, not JSON, since it's one
    /// value.</summary>
    static readonly string DefaultSongsFolderOverridePath = Path.Combine(UserDataRoot, "default_songs_folder_override.txt");

    /// <summary>Where DefaultSongPackService extracts the pack, and where ImportDefaultPackForTeam/
    /// GetDefaultPackTeams (via DefaultSongsFolder below) look for it -- a user-chosen relocation
    /// (SetDefaultSongsFolderOverride) takes priority over the UserDataRoot default. Was a
    /// `readonly` field; converted to a property so every one of its 16 existing call sites
    /// (ConfigStore/DefaultSongPackService/WebMainForm) picks up a relocation automatically
    /// without needing to change any of them -- don't revert this back to a field, that would
    /// silently break the relocate feature for every reader that cached the old value.</summary>
    public static string DownloadedDefaultSongsFolder =>
        GetDefaultSongsFolderOverride() ?? DefaultDownloadedDefaultSongsFolder;

    /// <summary>Returns the user's chosen relocation folder, or null if the pack is still at the
    /// default UserDataRoot location. Cached in memory after first read since this is checked on
    /// every DownloadedDefaultSongsFolder access (including inside tight loops like
    /// ImportDefaultPackForTeam's file scan) -- re-reading a text file off disk that often would
    /// be wasteful for a value that only ever changes via SetDefaultSongsFolderOverride, which
    /// updates the cache itself.</summary>
    static string? _defaultSongsFolderOverrideCache;
    static bool _defaultSongsFolderOverrideLoaded;
    static string? GetDefaultSongsFolderOverride()
    {
        if (!_defaultSongsFolderOverrideLoaded)
        {
            _defaultSongsFolderOverrideLoaded = true;
            try
            {
                _defaultSongsFolderOverrideCache = File.Exists(DefaultSongsFolderOverridePath)
                    ? File.ReadAllText(DefaultSongsFolderOverridePath).Trim()
                    : null;
                if (string.IsNullOrWhiteSpace(_defaultSongsFolderOverrideCache)) _defaultSongsFolderOverrideCache = null;
            }
            catch { _defaultSongsFolderOverrideCache = null; } // corrupt/unreadable marker shouldn't crash startup
        }
        return _defaultSongsFolderOverrideCache;
    }

    /// <summary>Relocates the default song pack to <paramref name="newFolder"/> -- moves whatever
    /// already exists at the current DownloadedDefaultSongsFolder location (if anything) into the
    /// new one, then persists the override so every future read of DownloadedDefaultSongsFolder
    /// (and therefore DefaultSongsFolder/GetDefaultPackTeams/ImportDefaultPackForTeam) resolves to
    /// the new location. Passing null/empty resets to the default UserDataRoot location. Returns
    /// false (and leaves everything untouched) if the move itself fails, e.g. destination on a
    /// different drive with a permissions issue, or newFolder is an existing non-empty directory
    /// that isn't already the pack (moving INTO an unrelated populated folder would silently mix
    /// the pack's index.json/team folders with whatever was already there).</summary>
    public static bool SetDefaultSongsFolderOverride(string? newFolder)
    {
        string oldFolder = DownloadedDefaultSongsFolder; // resolve BEFORE changing the cache below

        if (string.IsNullOrWhiteSpace(newFolder))
        {
            try { if (File.Exists(DefaultSongsFolderOverridePath)) File.Delete(DefaultSongsFolderOverridePath); }
            catch { return false; }
            _defaultSongsFolderOverrideCache = null;
            _defaultSongsFolderOverrideLoaded = true;
            return true;
        }

        newFolder = Path.GetFullPath(newFolder);
        if (string.Equals(Path.GetFullPath(oldFolder), newFolder, StringComparison.OrdinalIgnoreCase))
            return true; // no-op, already there

        try
        {
            if (Directory.Exists(newFolder) && Directory.EnumerateFileSystemEntries(newFolder).Any())
                return false; // refuse to move into an already-populated unrelated folder
            Directory.CreateDirectory(Path.GetDirectoryName(newFolder) ?? newFolder);
            if (Directory.Exists(oldFolder) && Directory.EnumerateFileSystemEntries(oldFolder).Any())
            {
                if (Directory.Exists(newFolder)) Directory.Delete(newFolder); // empty, checked above
                Directory.Move(oldFolder, newFolder);
            }
            else
            {
                Directory.CreateDirectory(newFolder); // nothing to move yet -- just point future imports here
            }

            Directory.CreateDirectory(UserDataRoot);
            AtomicWriteAllText(DefaultSongsFolderOverridePath, newFolder);
            _defaultSongsFolderOverrideCache = newFolder;
            _defaultSongsFolderOverrideLoaded = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Path to the default song pack, whichever source actually has it on this
    /// machine. Songs\Default\{Conference}\{Team}\{EventKey}.mp3. Prefers the bundled copy
    /// (older/full installs) over the downloaded one so a machine with both never double-counts
    /// or picks the wrong copy.</summary>
    public static string DefaultSongsFolder =>
        Directory.Exists(BundledDefaultSongsFolder) && Directory.EnumerateFileSystemEntries(BundledDefaultSongsFolder).Any()
            ? BundledDefaultSongsFolder
            : DownloadedDefaultSongsFolder;

    /// <summary>True once a default song pack (bundled or downloaded) is actually present --
    /// the signal the UI uses to decide whether to offer the one-time download prompt at all.</summary>
    public static bool HasDefaultSongPack =>
        (Directory.Exists(BundledDefaultSongsFolder) && Directory.EnumerateFileSystemEntries(BundledDefaultSongsFolder).Any())
        || (Directory.Exists(DownloadedDefaultSongsFolder) && Directory.EnumerateFileSystemEntries(DownloadedDefaultSongsFolder).Any());

    static string DefaultSongsIndexPath => Path.Combine(DefaultSongsFolder, "index.json");

    public static string LoadScorebugPresetName() =>
        File.Exists(ScorebugPresetPath) ? File.ReadAllText(ScorebugPresetPath).Trim() : ScorebugPreset.CollegeFootball27.Name;

    public static void SaveScorebugPresetName(string name)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(ScorebugPresetPath, name);
    }

    /// <summary>Persists the Mixer panel's Lead-In Whistle on/off toggle across restarts --
    /// without this, the whistle would silently default back on every launch just because the
    /// clip file still exists on disk, ignoring a user who explicitly turned it off last session.</summary>
    public static bool LoadLeadInWhistleEnabled() =>
        !File.Exists(LeadInWhistleEnabledPath) || File.ReadAllText(LeadInWhistleEnabledPath).Trim() != "false";

    public static void SaveLeadInWhistleEnabled(bool enabled)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(LeadInWhistleEnabledPath, enabled ? "true" : "false");
    }

    /// <summary>The one simple "which scorebug skin" choice, part of the LOCK IN / GAMETIME flow
    /// (owner scope decision 2026-08-13: never expose the reader's own multi-tab settings/
    /// calibration UI, and never ask about resolution -- that's auto-detected). Empty means "not
    /// chosen yet," which is what triggers the inline picker; once chosen it's remembered and the
    /// picker is skipped on every future GAMETIME unless the user reopens Reader Hub to change it.</summary>
    public static string LoadSavedScorebugSkin() =>
        File.Exists(ScorebugSizeChoicePath) ? File.ReadAllText(ScorebugSizeChoicePath).Trim() : "";

    public static void SaveScorebugSkinChoice(string skinName)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(ScorebugSizeChoicePath, skinName);
    }

    /// <summary>One-time opt-in acceptance for the reader's RAM-read mode -- see
    /// ScoreboardReaderRamModeEnabledPath's own doc comment. False (and no warning shown yet) by
    /// default; screen-mode is always the default regardless of this flag.</summary>
    public static bool LoadScoreboardReaderRamModeEnabled() =>
        File.Exists(ScoreboardReaderRamModeEnabledPath) && File.ReadAllText(ScoreboardReaderRamModeEnabledPath).Trim() == "true";

    public static void SaveScoreboardReaderRamModeEnabled(bool enabled)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(ScoreboardReaderRamModeEnabledPath, enabled ? "true" : "false");
    }

    /// <summary>See RemotePlayModePath's doc comment. False by default.</summary>
    public static bool LoadRemotePlayModeEnabled() =>
        File.Exists(RemotePlayModePath) && File.ReadAllText(RemotePlayModePath).Trim() == "true";

    public static void SaveRemotePlayModeEnabled(bool enabled)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(RemotePlayModePath, enabled ? "true" : "false");
    }

    sealed record SupabaseSettings(string Url, string AnonKey);

    /// <summary>(Url, AnonKey), both "" if never configured. CloudDatabaseService treats either
    /// being blank as "not configured" and no-ops rather than throwing.</summary>
    public static (string Url, string AnonKey) LoadSupabaseSettings()
    {
        if (!File.Exists(SupabaseSettingsPath)) return ("", "");
        try
        {
            var settings = JsonSerializer.Deserialize<SupabaseSettings>(File.ReadAllText(SupabaseSettingsPath));
            return (settings?.Url ?? "", settings?.AnonKey ?? "");
        }
        catch
        {
            return ("", "");
        }
    }

    public static void SaveSupabaseSettings(string url, string anonKey)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(SupabaseSettingsPath, JsonSerializer.Serialize(new SupabaseSettings(url, anonKey)));
    }

    /// <summary>
    /// Imports default song pack assignments for a team. Looks in the bundled
    /// Songs\Default\{conference}\{teamName}\ folder and maps each EventKey-named
    /// .mp3 to the corresponding TriggerEntry in the profile.
    /// Returns the number of events auto-assigned.
    /// </summary>
    /// <summary>Resolves the on-disk folder for a team's slice of the default pack (searches
    /// every conference subfolder of DefaultSongsFolder for one matching teamName), or null if
    /// the pack isn't present or has nothing for this team. Shared by ImportDefaultPackForTeam
    /// and the Sound Bank album's "Default Pack" section (WebMainForm.
    /// GetDefaultPackSongsForTeamFromWeb) so both use the exact same lookup + traversal guard.
    ///
    /// teamName ultimately comes from the roster (TeamColors.All / custom_teams.json), but this
    /// method's own signature makes no such guarantee to callers -- sanitize defensively so a
    /// crafted name (e.g. "..\..\..\SomeFolder") can't walk Path.Combine outside DefaultSongsFolder
    /// to enumerate/read files from an arbitrary directory.
    ///
    /// REGRESSION FIX (v1.0.53->v1.0.54): the previous version also blanket-replaced '.', '/' and
    /// '\' with '_', which mangled any real team name containing a period (e.g. a TeamBuilder-added
    /// "St. <something>") so it no longer matched its actual on-disk folder -- that team's default
    /// pack silently imported 0 songs while every dot-free team (the vast majority) kept working,
    /// which is why only some users/teams saw "no sound". Only strip characters that are actually
    /// invalid in a filename, then verify the resolved path is still inside DefaultSongsFolder as
    /// defense-in-depth against traversal (handles ".." without punishing legitimate single dots).</summary>
    public static string? FindDefaultPackTeamFolder(string teamName)
    {
        string safeTeamName = string.Join("_", teamName.Split(Path.GetInvalidFileNameChars()));
        if (!Directory.Exists(DefaultSongsFolder)) return null;

        string defaultSongsRoot = Path.GetFullPath(DefaultSongsFolder);
        foreach (var confDir in Directory.GetDirectories(DefaultSongsFolder))
        {
            string candidate = Path.Combine(confDir, safeTeamName);
            string resolvedCandidate = Path.GetFullPath(candidate);
            if (!resolvedCandidate.StartsWith(defaultSongsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>overwrite=false (default) preserves the original "only fills empty slots" safety
    /// net every silent/automatic caller (SetGameTeamsFromWeb, the old fill-only Auto-Assign)
    /// relies on. overwrite=true backs the explicit Auto-Assign "overwrite" confirm flow only --
    /// the caller must have already gotten the user's yes/no on wiping their current arrangement,
    /// this method itself has no such gate.</summary>
    /// <summary>Same traversal-guard pattern as FindDefaultPackTeamFolder, but returns the
    /// CONFERENCE folder itself (e.g. Songs\Default\SEC\) instead of the team's subfolder inside
    /// it -- for conference-wide files (chants/hype cues not specific to any one school) sitting
    /// directly in that folder rather than under a team name. Resolves by finding which
    /// conference folder actually contains a subfolder for this team, same lookup
    /// FindDefaultPackTeamFolder does, just returning one level up.</summary>
    public static string? FindDefaultPackConferenceFolder(string teamName)
    {
        string safeTeamName = string.Join("_", teamName.Split(Path.GetInvalidFileNameChars()));
        if (!Directory.Exists(DefaultSongsFolder)) return null;

        string defaultSongsRoot = Path.GetFullPath(DefaultSongsFolder);
        foreach (var confDir in Directory.GetDirectories(DefaultSongsFolder))
        {
            string candidate = Path.Combine(confDir, safeTeamName);
            string resolvedCandidate = Path.GetFullPath(candidate);
            if (!resolvedCandidate.StartsWith(defaultSongsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Directory.Exists(candidate)) return confDir;
        }
        return null;
    }

    /// <summary>Filename -> EventKey convention shared by every default-pack importer (team,
    /// conference, generic): "Offense_ Earned First Down.mp3" -> "Offense: Earned First Down",
    /// with a trailing "_2"/"_3" variant-index suffix stripped so duplicate-source files
    /// (multiple candidates for the same event) all resolve to the one real EventKey.</summary>
    static string EventKeyFromFileName(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        string eventKey = name.Replace("_", ": ").Replace("  ", " ");
        // The variant suffix started as a trailing "_2"/"_3" in the raw filename, but by this
        // point every "_" has already become ": " above, so it now reads ": 2"/": 3" -- matching
        // on "_\d+$" here was always a no-op (there are no underscores left to match), which left
        // every variant-numbered file's EventKey stuck with its index still attached and never
        // matching the base TriggerEntry.Event it was meant to be an alternate for.
        return System.Text.RegularExpressions.Regex.Replace(eventKey, @":\s*\d+$", "");
    }

    public sealed record ConferencePackPreviewItem(string EventKey, string FileName, string FilePath, string? CurrentFile);

    /// <summary>Shared file source for "Load Conference Pack": the team's OWN subfolder first
    /// (Songs\Default\{Conference}\{Team}\*.mp3 -- the actual pack content for packs organized
    /// per-team, e.g. the real SEC pack, which has 0 loose files at the conference root), then any
    /// loose conference-wide files sitting directly in the conference folder (shared chants/cues
    /// not specific to one school). Team-specific files win on an EventKey collision, same
    /// "team-specific beats generic" precedence the wizard's local-library merge already uses
    /// (app.js's pack-before-conference ordering). Without the team-folder pass, "Load Conference
    /// Pack" found nothing for any conference whose pack is organized as team subfolders instead
    /// of loose conference-root files -- which is every real default pack on disk.</summary>
    static IEnumerable<(string File, string EventKey)> ConferencePackFiles(string teamName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? teamFolder = FindDefaultPackTeamFolder(teamName);
        if (teamFolder != null)
        {
            foreach (var file in Directory.GetFiles(teamFolder, "*.*", SearchOption.TopDirectoryOnly))
            {
                if (!AudioExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                string eventKey = EventKeyFromFileName(file);
                if (seen.Add(eventKey)) yield return (file, eventKey);
            }
        }
        string? confFolder = FindDefaultPackConferenceFolder(teamName);
        if (confFolder != null)
        {
            foreach (var file in Directory.GetFiles(confFolder, "*.*", SearchOption.TopDirectoryOnly))
            {
                if (!AudioExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                string eventKey = EventKeyFromFileName(file);
                if (seen.Add(eventKey)) yield return (file, eventKey);
            }
        }
    }

    /// <summary>Preview of what "Load Conference Pack" WOULD do, without touching the profile --
    /// one row per event the team's pack (team subfolder + loose conference-root files) has a file
    /// for, including whatever the team already has assigned (if anything) so the caller can
    /// decide per-event whether to overwrite instead of the old silent "only fill empty slots"
    /// behavior. Owner feedback: most users already have SOME songs assigned, so a backfill-only
    /// pass quietly did nothing for them -- they need to be asked, event by event, whether to
    /// replace what's there.</summary>
    public static List<ConferencePackPreviewItem> PreviewConferencePackForTeam(string teamName, List<TriggerEntry> profile)
    {
        var result = new List<ConferencePackPreviewItem>();
        foreach (var (file, eventKey) in ConferencePackFiles(teamName))
        {
            var entry = profile.FirstOrDefault(e => e.Event.Equals(eventKey, StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue; // no matching event in this profile at all
            result.Add(new ConferencePackPreviewItem(eventKey, Path.GetFileName(file), file,
                string.IsNullOrWhiteSpace(entry.AudioFile) ? null : entry.AudioFile));
        }
        return result;
    }

    /// <summary>Applies the conference pack for exactly the EventKeys the caller confirmed (empty
    /// slots the JS side decided didn't need asking about, plus any already-assigned ones the user
    /// explicitly said yes to overwriting) -- everything else from PreviewConferencePackForTeam
    /// that isn't in this set is left untouched. Returns the count actually changed.</summary>
    public static int ApplyConferencePackSelections(string teamName, List<TriggerEntry> profile, IEnumerable<string> eventKeysToAssign)
    {
        var wanted = new HashSet<string>(eventKeysToAssign, StringComparer.OrdinalIgnoreCase);
        int assigned = 0;
        foreach (var (file, eventKey) in ConferencePackFiles(teamName))
        {
            if (!wanted.Contains(eventKey)) continue;
            var entry = profile.FirstOrDefault(e => e.Event.Equals(eventKey, StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue;
            entry.AudioFile = file;
            assigned++;
        }
        return assigned;
    }

    /// <summary>Conference-wide counterpart to ImportDefaultPackForTeam -- fills from files sitting
    /// directly in the team's conference folder (TopDirectoryOnly, so team subfolders inside it are
    /// never recursed into) instead of the team's own subfolder. Meant as a second pass AFTER a
    /// team-specific import: run team-specific first (more accurate), then this to backfill
    /// whatever the team doesn't have its own song for using shared conference-wide cues.
    /// overwrite=false only fills empty slots, matching ImportDefaultPackForTeam's own default.</summary>
    public static int ImportConferencePackForTeam(string teamName, List<TriggerEntry> profile, bool overwrite = false)
    {
        string? confFolder = FindDefaultPackConferenceFolder(teamName);
        if (confFolder == null) return 0;

        int before = profile.Count(e => !string.IsNullOrWhiteSpace(e.AudioFile));
        if (overwrite)
        {
            foreach (var file in Directory.GetFiles(confFolder, "*.*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!AudioExtensions.Contains(ext)) continue;
                string eventKey = EventKeyFromFileName(file);
                var entry = profile.FirstOrDefault(e => e.Event.Equals(eventKey, StringComparison.OrdinalIgnoreCase));
                if (entry != null) entry.AudioFile = file;
            }
            return profile.Count(e => !string.IsNullOrWhiteSpace(e.AudioFile)) - before;
        }

        ImportDefaultPackFromFolder(confFolder, profile);
        return profile.Count(e => !string.IsNullOrWhiteSpace(e.AudioFile)) - before;
    }

    public static int ImportDefaultPackForTeam(string teamName, List<TriggerEntry> profile, bool overwrite = false) =>
        ImportTeamFolderForTeam(FindDefaultPackTeamFolder(teamName), profile, overwrite);

    /// <summary>Same assignment loop as ImportDefaultPackForTeam, but takes the team's folder
    /// directly instead of resolving it via FindDefaultPackTeamFolder/DefaultSongsFolder --
    /// DefaultSongsFolder prefers the BUNDLED pack over DownloadedDefaultSongsFolder whenever the
    /// bundled one exists and is non-empty (see DefaultSongsFolder's own doc comment), so anything
    /// that must specifically use a folder the user JUST imported (the "Load All" flow) needs to
    /// bypass that resolution entirely or it silently reads stale bundled files -- or nothing at
    /// all, for a team the bundled pack doesn't have -- instead of what was just picked.</summary>
    public static int ImportTeamFolderForTeam(string? teamFolder, List<TriggerEntry> profile, bool overwrite = false)
    {
        int assigned = 0;
        if (teamFolder == null || !Directory.Exists(teamFolder)) return 0;

        foreach (var file in Directory.GetFiles(teamFolder, "*.*", SearchOption.TopDirectoryOnly))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (!AudioExtensions.Contains(ext)) continue;

            string eventKey = EventKeyFromFileName(file);

            var entry = profile.FirstOrDefault(e =>
                e.Event.Equals(eventKey, StringComparison.OrdinalIgnoreCase));
            if (entry != null && (overwrite || string.IsNullOrWhiteSpace(entry.AudioFile)))
            {
                entry.AudioFile = file;
                assigned++;
            }
        }

        return assigned;
    }

    /// <summary>
    /// Returns the list of teams that have default song packs available,
    /// read from Songs\Default\index.json if present.
    /// </summary>
    public static List<string> GetDefaultPackTeams()
    {
        if (!File.Exists(DefaultSongsIndexPath)) return new List<string>();

        try
        {
            var json = File.ReadAllText(DefaultSongsIndexPath);
            var index = JsonSerializer.Deserialize<DefaultSongsIndex>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return index?.Teams ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public sealed record DefaultPackTeamEntry(string Team, string Conference);

    /// <summary>Sound Bank browsing redesign: lists every team actually present in the pack, with
    /// its conference, for a "browse another team's Sound Bank" picker. Deliberately does NOT read
    /// index.json (unlike GetDefaultPackTeams above) -- that file is only ever written by
    /// DefaultSongPackService's own download flow (DownloadedDefaultSongsFolder), so a build that
    /// ships the pack bundled instead (BundledDefaultSongsFolder, preferred by DefaultSongsFolder
    /// when present) would silently report zero teams even though the real per-team folders are
    /// right there. A live two-level scan (conference dir -> team dir) works for both cases and
    /// costs nothing meaningful for ~68 directories. "General" (a flat folder of generic fallback
    /// songs, see GetGenericProfile) has no team-shaped subfolders of its own, so it naturally
    /// contributes nothing here without needing to be special-cased out.</summary>
    public static List<DefaultPackTeamEntry> GetDefaultPackTeamsWithConference()
    {
        var result = new List<DefaultPackTeamEntry>();
        if (!Directory.Exists(DefaultSongsFolder)) return result;

        foreach (var confDir in Directory.GetDirectories(DefaultSongsFolder))
        {
            string conference = Path.GetFileName(confDir);
            foreach (var teamDir in Directory.GetDirectories(confDir))
            {
                if (!Directory.EnumerateFiles(teamDir).Any()) continue;
                result.Add(new DefaultPackTeamEntry(Path.GetFileName(teamDir), conference));
            }
        }
        return result.OrderBy(t => t.Team, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Returns a "Generic" profile that any team falls back to when they don't have
    /// their own song for a specific event. Populated from Songs\Default\General\
    /// or the first available team in the default pack. Cached in memory.
    /// </summary>
    public static List<TriggerEntry> GetGenericProfile()
    {
        var profile = BuildDefault();

        var generalDir = Path.Combine(DefaultSongsFolder, "General");
        if (Directory.Exists(generalDir))
        {
            ImportDefaultPackFromFolder(generalDir, profile);
            return profile;
        }

        // Fallback: use the first available team's songs as generic
        if (Directory.Exists(DefaultSongsFolder))
        {
            foreach (var confDir in Directory.GetDirectories(DefaultSongsFolder))
            {
                foreach (var teamDir in Directory.GetDirectories(confDir).Take(1))
                {
                    ImportDefaultPackFromFolder(teamDir, profile);
                    return profile;
                }
                break;
            }
        }

        return profile;
    }

    /// <summary>
    /// Imports all EventKey-named .mp3 files from a folder into a profile.
    /// Only fills empty slots — never overwrites existing assignments.
    /// </summary>
    public static void ImportDefaultPackFromFolder(string folder, List<TriggerEntry> profile)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (!AudioExtensions.Contains(ext)) continue;

            string eventKey = EventKeyFromFileName(file);

            var entry = profile.FirstOrDefault(e =>
                e.Event.Equals(eventKey, StringComparison.OrdinalIgnoreCase));
            if (entry != null && string.IsNullOrWhiteSpace(entry.AudioFile))
            {
                entry.AudioFile = file;
            }
        }
    }

    private sealed class DefaultSongsIndex
    {
        public List<string> Teams { get; set; } = new();
    }

    /// <summary>One-time migration, run before anything else touches the folders above.
    /// Moves anything left behind in the OLD per-version location (AppContext.BaseDirectory --
    /// where this data used to live, and gets wiped by Squirrel on every update) into the new
    /// persistent UserDataRoot. Existing files in UserDataRoot always win -- never overwrites
    /// real user data with older leftovers. Safe to call on every launch; it's a no-op once
    /// nothing is left in the old location.</summary>
    public static void MigrateFromVersionedFolderIfNeeded()
    {
        Directory.CreateDirectory(UserDataRoot);
        MoveFileIfNewer(Path.Combine(AppContext.BaseDirectory, "triggers.json"), ConfigPath);
        MoveFileIfNewer(Path.Combine(AppContext.BaseDirectory, ".firstrun_done"), FirstRunFlagPath);
        MergeFolderIfNeeded(Path.Combine(AppContext.BaseDirectory, "Songs"), SongsFolder);
        MergeFolderIfNeeded(Path.Combine(AppContext.BaseDirectory, "Profiles"), ProfilesFolder);
        // TeamBackgrounds ships bundled WITH the app (default art for every team) and gets
        // re-copied into AppContext.BaseDirectory fresh on every update -- merge-only (never
        // overwrite) so a user who drops in their own custom image keeps it, while still
        // picking up any new default images a future release adds.
        MergeFolderIfNeeded(Path.Combine(AppContext.BaseDirectory, "TeamBackgrounds"), TeamBackgroundsFolder, overwrite: false);
        MergeFolderIfNeeded(Path.Combine(AppContext.BaseDirectory, "TeamLogos"), TeamLogosFolder, overwrite: false);
    }

    static void MoveFileIfNewer(string oldPath, string newPath)
    {
        if (File.Exists(newPath) || !File.Exists(oldPath)) return;
        try { File.Copy(oldPath, newPath, overwrite: false); } catch { /* best-effort */ }
    }

    static void MergeFolderIfNeeded(string oldFolder, string newFolder, bool overwrite = false)
    {
        if (!Directory.Exists(oldFolder)) return;
        Directory.CreateDirectory(newFolder);
        foreach (string file in Directory.GetFiles(oldFolder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(oldFolder, file);
            string dest = Path.Combine(newFolder, rel);
            if (!overwrite && File.Exists(dest)) continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite);
            }
            catch { /* best-effort -- never block startup over one bad file */ }
        }
    }

    /// <summary>True until the onboarding wizard has been completed once. Gated on a marker
    /// file rather than a triggers.json field so it survives profile resets/imports.</summary>
    public static bool IsFirstRun() => !File.Exists(FirstRunFlagPath);

    public static void MarkFirstRunDone() => AtomicWriteAllText(FirstRunFlagPath, DateTime.UtcNow.ToString("O"));

    static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac", ".webm", ".ogg" };

    /// <summary>Copies a dropped/browsed audio file into Songs\ with its display name
    /// normalized to ALL CAPS, for a consistent library (drag-and-drop import never actually
    /// copied files before -- BrowseForFile just referenced wherever the original lived).
    /// Returns the new path, or null if the source isn't a recognized audio file.</summary>
    public static string? ImportIntoSongsLibrary(string sourcePath)
    {
        string ext = Path.GetExtension(sourcePath);
        if (!AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return null;

        Directory.CreateDirectory(SongsUploadedFolder);
        string baseName = Path.GetFileNameWithoutExtension(sourcePath).ToUpperInvariant();
        string destPath = Path.Combine(SongsUploadedFolder, baseName + ext);
        int suffix = 2;
        while (File.Exists(destPath) && !PathsPointToSameFile(sourcePath, destPath))
            destPath = Path.Combine(SongsUploadedFolder, $"{baseName} ({suffix++}){ext}");

        if (!PathsPointToSameFile(sourcePath, destPath))
            File.Copy(sourcePath, destPath, overwrite: false);
        return destPath;
    }

    /// <summary>One entry in "My Downloads" -- a marketplace item the user has pulled down
    /// locally, with a clear human-readable identifier ("[School] — [Name]") rather than the raw
    /// filename, so the downloads list reads clearly even when many teams' files share generic
    /// names like "Fight Song.mp3".</summary>
    public sealed record MarketplaceDownloadEntry
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type { get; init; } = ""; // "song" or "image"
        public string Name { get; init; } = "";
        public string School { get; init; } = "";
        public string Path { get; init; } = "";
        public DateTime DownloadedAt { get; init; } = DateTime.UtcNow;
    }

    // Guards every read-modify-write of the manifest file. Downloads happen on background async
    // continuations (WebView2 host-object calls aren't serialized onto one thread), so two
    // near-simultaneous downloads -- or a download racing a delete from the My Downloads tab --
    // could otherwise both load the same "before" list, each add/remove their own entry, and
    // whichever writes last silently wins, dropping the other's change on the floor.
    static readonly object MarketplaceDownloadsLock = new();

    public static List<MarketplaceDownloadEntry> LoadMarketplaceDownloads()
    {
        lock (MarketplaceDownloadsLock) return LoadMarketplaceDownloadsUnlocked();
    }

    static List<MarketplaceDownloadEntry> LoadMarketplaceDownloadsUnlocked()
    {
        if (!File.Exists(MarketplaceDownloadsManifestPath)) return new List<MarketplaceDownloadEntry>();
        try
        {
            string json = File.ReadAllText(MarketplaceDownloadsManifestPath);
            var entries = JsonSerializer.Deserialize<List<MarketplaceDownloadEntry>>(json, JsonOptions) ?? new List<MarketplaceDownloadEntry>();
            return PruneMissingFiles(entries, e => e.Path, SaveMarketplaceDownloads);
        }
        catch { return new List<MarketplaceDownloadEntry>(); } // corrupt manifest shouldn't crash the whole downloads tab
    }

    /// <summary>Self-heals "My Downloads" disk-drift: if a manifest entry's backing file has been
    /// deleted/moved outside the app, the entry would otherwise keep showing up in My Downloads
    /// forever as a "download" that immediately 404s when clicked. Every load drops entries whose
    /// file no longer exists and persists the pruned list.</summary>
    static List<T> PruneMissingFiles<T>(List<T> entries, Func<T, string> pathOf, Action<List<T>> save)
    {
        var alive = entries.Where(e => File.Exists(pathOf(e))).ToList();
        if (alive.Count != entries.Count) save(alive);
        return alive;
    }

    static void SaveMarketplaceDownloads(List<MarketplaceDownloadEntry> entries)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(MarketplaceDownloadsManifestPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    /// <summary>Records a new download in the manifest. If a prior entry already points at the
    /// same local path (a re-download after deleting the manifest entry but not the file, or a
    /// double-click), it's replaced rather than duplicated.</summary>
    public static MarketplaceDownloadEntry RecordMarketplaceDownload(string type, string name, string school, string path)
    {
        lock (MarketplaceDownloadsLock)
        {
            var entries = LoadMarketplaceDownloadsUnlocked();
            entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            var entry = new MarketplaceDownloadEntry { Type = type, Name = name, School = school, Path = path };
            entries.Add(entry);
            SaveMarketplaceDownloads(entries);
            return entry;
        }
    }

    /// <summary>Removes a "My Downloads" entry and deletes its local file. Returns false if the
    /// id wasn't found (already removed, stale UI) -- not an error, just a no-op.</summary>
    public static bool RemoveMarketplaceDownload(string id)
    {
        lock (MarketplaceDownloadsLock)
        {
            var entries = LoadMarketplaceDownloadsUnlocked();
            var entry = entries.FirstOrDefault(e => e.Id == id);
            if (entry == null) return false;

            entries.Remove(entry);
            SaveMarketplaceDownloads(entries);
            try { if (File.Exists(entry.Path)) File.Delete(entry.Path); } catch { /* best-effort */ }
            return true;
        }
    }

    /// <summary>Renames a "My Downloads" entry's display title -- local-only, doesn't touch the
    /// underlying file on disk or the marketplace listing it was downloaded from. Returns false
    /// if the id wasn't found.</summary>
    public static bool RenameMarketplaceDownload(string id, string newName)
    {
        lock (MarketplaceDownloadsLock)
        {
            var entries = LoadMarketplaceDownloadsUnlocked();
            int idx = entries.FindIndex(e => e.Id == id);
            if (idx < 0) return false;
            entries[idx] = entries[idx] with { Name = newName };
            SaveMarketplaceDownloads(entries);
            return true;
        }
    }

    /// <summary>One entry in "My Downloads" for a track the user imported/trimmed themselves
    /// (item 21's local-import pipeline), as opposed to a marketplace download
    /// (MarketplaceDownloadEntry above). Kept as its own manifest/type rather than folded into
    /// MarketplaceDownloadEntry so the "Share to Marketplace" button in the My Downloads tab can
    /// tell the two apart -- it must only ever appear on tracks that came through this pipeline,
    /// never on something already sourced from the marketplace.</summary>
    public sealed record LocalTrackEntry
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        /// <summary>True once this track has actually been shared to the marketplace -- lets the
        /// UI swap the button for a "Shared" state instead of letting the same local track get
        /// re-uploaded as a brand new marketplace item on every click.</summary>
        public bool Shared { get; init; }
        /// <summary>"song" or "pa" (marketplace upload type parity, see worker.js VALID_TYPES) --
        /// defaults to "song" so every entry saved before this field existed deserializes exactly
        /// as it always did.</summary>
        public string Type { get; init; } = "song";
    }

    // Same rationale as MarketplaceDownloadsLock above -- guards every read-modify-write of
    // local_tracks.json against two near-simultaneous calls (import racing a delete/share).
    static readonly object LocalTracksLock = new();

    public static List<LocalTrackEntry> LoadLocalTracks()
    {
        lock (LocalTracksLock) return LoadLocalTracksUnlocked();
    }

    static List<LocalTrackEntry> LoadLocalTracksUnlocked()
    {
        if (!File.Exists(LocalTracksManifestPath)) return new List<LocalTrackEntry>();
        try
        {
            string json = File.ReadAllText(LocalTracksManifestPath);
            var entries = JsonSerializer.Deserialize<List<LocalTrackEntry>>(json, JsonOptions) ?? new List<LocalTrackEntry>();
            return PruneMissingFiles(entries, e => e.Path, SaveLocalTracks);
        }
        catch { return new List<LocalTrackEntry>(); } // corrupt manifest shouldn't crash the downloads tab
    }

    static void SaveLocalTracks(List<LocalTrackEntry> entries)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(LocalTracksManifestPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    public static LocalTrackEntry RecordLocalTrack(string name, string path, string type = "song")
    {
        lock (LocalTracksLock)
        {
            var entries = LoadLocalTracksUnlocked();
            entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            var entry = new LocalTrackEntry { Name = name, Path = path, Type = string.IsNullOrWhiteSpace(type) ? "song" : type };
            entries.Add(entry);
            SaveLocalTracks(entries);
            return entry;
        }
    }

    /// <summary>Removes a locally-imported track and deletes its file -- same semantics as
    /// RemoveMarketplaceDownload (false, not an error, if the id's already gone).</summary>
    public static bool RemoveLocalTrack(string id)
    {
        lock (LocalTracksLock)
        {
            var entries = LoadLocalTracksUnlocked();
            var entry = entries.FirstOrDefault(e => e.Id == id);
            if (entry == null) return false;

            entries.Remove(entry);
            SaveLocalTracks(entries);
            try { if (File.Exists(entry.Path)) File.Delete(entry.Path); } catch { /* best-effort */ }
            return true;
        }
    }

    /// <summary>Renames a locally-imported track's display title -- same local-only semantics as
    /// RenameMarketplaceDownload, doesn't touch the file on disk. Returns false if the id isn't found.</summary>
    public static bool RenameLocalTrack(string id, string newName)
    {
        lock (LocalTracksLock)
        {
            var entries = LoadLocalTracksUnlocked();
            int idx = entries.FindIndex(e => e.Id == id);
            if (idx < 0) return false;
            entries[idx] = entries[idx] with { Name = newName };
            SaveLocalTracks(entries);
            return true;
        }
    }

    /// <summary>Flips a local track's Shared flag once it's actually been uploaded to the
    /// marketplace. Returns false if the id isn't found.</summary>
    public static bool MarkLocalTrackShared(string id)
    {
        lock (LocalTracksLock)
        {
            var entries = LoadLocalTracksUnlocked();
            int idx = entries.FindIndex(e => e.Id == id);
            if (idx < 0) return false;
            entries[idx] = entries[idx] with { Shared = true };
            SaveLocalTracks(entries);
            return true;
        }
    }

    /// <summary>Locally-persisted sign-in state -- just enough to show "signed in as X" and
    /// re-attach to the Worker-issued app session on next launch without re-running the full
    /// browser OAuth flow every time. The Google ID token itself is NOT stored here long-term
    /// (it's short-lived and single-use for the /auth/verify exchange) -- only the resulting
    /// app-level SessionToken from the Worker, which is what marketplace calls present instead.</summary>
    public sealed record AuthSession
    {
        public string Sub { get; init; } = "";
        public string Email { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Picture { get; init; }
        public string SessionToken { get; init; } = "";
        public DateTime SignedInAt { get; init; } = DateTime.UtcNow;
    }

    public static AuthSession? LoadAuthSession()
    {
        if (!File.Exists(AuthSessionPath)) return null;
        try { return JsonSerializer.Deserialize<AuthSession>(File.ReadAllText(AuthSessionPath), JsonOptions); }
        catch { return null; } // corrupt session file -- just treat as signed out, don't crash startup
    }

    public static void SaveAuthSession(AuthSession session)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(AuthSessionPath, JsonSerializer.Serialize(session, JsonOptions));
    }

    public static void ClearAuthSession()
    {
        if (File.Exists(AuthSessionPath)) File.Delete(AuthSessionPath);
    }

    /// <summary>The "universal profile" -- favorite team + lifetime stats, distinct from the
    /// per-team Save Profile feature (ConfigProfileManager, which saves song-to-situation
    /// assignments for ONE team). This is one record per Bandroom install, always saved locally
    /// so it works fully signed-out; when signed in with Google it's also mirrored to the
    /// marketplace worker's /profile endpoint so it can follow the account across devices (see
    /// WebBridge.SyncProfileToCloud/PullProfileFromCloud). Local is always the source of truth
    /// for THIS device; cloud sync is best-effort and never blocks local save/load.</summary>
    public sealed record UserProfile
    {
        public string? FavoriteTeam { get; init; }
        public int GamesWatched { get; init; }
        public int SongsTriggered { get; init; }
        public int MarketplaceUploads { get; init; }
        public int MarketplaceDownloads { get; init; }

        // -- Profile tab expansion (20-suggestion batch) --
        public string? Bio { get; init; }
        public string? RivalTeam { get; init; }
        /// <summary>Per-event trigger counts (key = TriggerEntry.Event, e.g. "Offense: Touchdown
        /// Scored") -- powers "most-triggered event". Only ever grows; never trimmed.</summary>
        public Dictionary<string, int> EventCounts { get; init; } = new();
        /// <summary>Per-team games-watched counts (key = team name, counted for BOTH home and away
        /// whenever that team appears in a GAMETIME confirmation) -- powers the per-team breakdown.</summary>
        public Dictionary<string, int> GamesWatchedByTeam { get; init; } = new();
        public int StreakCurrentDays { get; init; }
        public DateTime? StreakLastActiveDate { get; init; }
        /// <summary>Manually recorded via the Profile tab -- Bandroom has no way to auto-detect a
        /// final score, so these are user-reported, not auto-detected.</summary>
        public int FavoriteTeamWins { get; init; }
        public int FavoriteTeamLosses { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public bool ToastsEnabled { get; init; } = true;
        /// <summary>Filename (not full path) of a custom local avatar under UserDataRoot\Avatar\,
        /// shown instead of the Google avatar -- available signed-out too, unlike the Google one.</summary>
        public string? AvatarFileName { get; init; }
        /// <summary>Custom team logo edits (key = team name), synced across devices for a signed-in
        /// account -- unlike EventCounts/GamesWatchedByTeam above, this is NOT a monotonic counter,
        /// so the merge rule is "newest UpdatedAtUtc per key wins", not max-of-value (see
        /// WebBridge.MergeLatestWins). Works fully offline/signed-out same as before; this dict is
        /// just the cloud-sync record of what was last saved locally, per team.</summary>
        public Dictionary<string, TeamLogoEntry> CustomTeamLogos { get; init; } = new();

        // -- Profile Dashboard: public sharing --
        /// <summary>Google "sub" claim (AuthSession.Sub), copied in on sign-in so a public profile
        /// URL/leaderboard entry has a stable ID that survives display-name changes. Null while
        /// signed out -- IsPublicProfile can only be true for a signed-in profile (see
        /// WebBridge.TogglePublicProfile), since a public page has nothing stable to key on
        /// otherwise.</summary>
        public string? GoogleUserId { get; init; }
        /// <summary>Opt-in: when true, ProfileSyncService's push includes the public-safe subset of
        /// this record (favorite team, achievements, stats -- never Bio/RivalTeam) so the
        /// marketplace worker's /profile GET can serve it to other users, and so this profile can
        /// appear on the events/games leaderboard. Defaults false -- profile data is local-only
        /// until the user opts in.</summary>
        public bool IsPublicProfile { get; init; }
    }

    /// <summary>One custom team logo edit -- the base64 PNG bytes plus when it was saved, so a
    /// "newest wins" merge (WebBridge.MergeLatestWins) can tell which device's edit is more recent
    /// for a given team.</summary>
    public sealed record TeamLogoEntry
    {
        public string Base64Png { get; init; } = "";
        public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>Tracks, per team, the UpdatedAtUtc of the logo edit that's actually been written to
    /// TeamLogosFolder on THIS device -- lets the pull path (WebBridge, after a cloud merge) tell
    /// which merged entries are genuinely newer than what's on disk here, without re-writing a PNG
    /// (and toasting about it) on every sign-in for logos that already match. Same sidecar-manifest
    /// pattern as local_tracks.json/marketplace_downloads.json above, just for logos.</summary>
    public sealed record TeamLogoSyncManifest
    {
        public Dictionary<string, DateTime> AppliedAtUtc { get; init; } = new();
        /// <summary>True once this device has completed one full pull of CustomTeamLogos --
        /// suppresses the "Logo updated for X" toast on that very first sync (which could otherwise
        /// spam a toast per pre-existing custom logo on a brand-new device), while still writing
        /// the files themselves.</summary>
        public bool InitialSyncDone { get; init; }
    }

    static readonly string TeamLogoSyncManifestPath = Path.Combine(UserDataRoot, "team_logo_sync.json");

    public static TeamLogoSyncManifest LoadTeamLogoSyncManifest()
    {
        if (!File.Exists(TeamLogoSyncManifestPath)) return new TeamLogoSyncManifest();
        try { return JsonSerializer.Deserialize<TeamLogoSyncManifest>(File.ReadAllText(TeamLogoSyncManifestPath), JsonOptions) ?? new TeamLogoSyncManifest(); }
        catch { return new TeamLogoSyncManifest(); } // corrupt manifest -- treat as never-synced, not fatal
    }

    public static void SaveTeamLogoSyncManifest(TeamLogoSyncManifest manifest)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(TeamLogoSyncManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    /// <summary>Tracks, per team, the updatedAt of the PUBLIC (everyone-sees-it) logo this device
    /// has already applied from the marketplace worker's /teamlogos index -- distinct from
    /// TeamLogoSyncManifest above, which is about YOUR OWN account's private cross-device sync.
    /// This one gates PublicTeamLogoSyncService so it doesn't re-download/re-write a team's public
    /// logo file on every app launch once it's already been applied, and (more importantly) so a
    /// team a user has customized for THEMSELVES never gets silently clobbered by someone else's
    /// public push -- see PublicTeamLogoSyncService.SyncAsync's "skip if team is in CustomTeamLogos"
    /// check, which is the actual owner-requested guarantee this whole feature hinges on.</summary>
    public sealed record PublicTeamLogoSyncManifest
    {
        public Dictionary<string, DateTime> AppliedAtUtc { get; init; } = new();
    }

    static readonly string PublicTeamLogoSyncManifestPath = Path.Combine(UserDataRoot, "public_team_logo_sync.json");

    public static PublicTeamLogoSyncManifest LoadPublicTeamLogoSyncManifest()
    {
        if (!File.Exists(PublicTeamLogoSyncManifestPath)) return new PublicTeamLogoSyncManifest();
        try { return JsonSerializer.Deserialize<PublicTeamLogoSyncManifest>(File.ReadAllText(PublicTeamLogoSyncManifestPath), JsonOptions) ?? new PublicTeamLogoSyncManifest(); }
        catch { return new PublicTeamLogoSyncManifest(); }
    }

    public static void SavePublicTeamLogoSyncManifest(PublicTeamLogoSyncManifest manifest)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(PublicTeamLogoSyncManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public static readonly string AvatarFolder = Path.Combine(UserDataRoot, "Avatar");

    // Guards user_profile.json the same way CustomTeamsLock/MarketplaceDownloadsLock/
    // LocalTracksLock guard their own manifests -- WebView2 host-object calls aren't serialized
    // onto one thread, so a stat bump from a live game (RecordSongTriggered) and a profile edit
    // from the UI (SetBio/SetFavoriteTeam/etc.) can otherwise both read the same on-disk snapshot
    // and one's SaveUserProfile silently clobbers the other's change (lost update). Plain
    // Load/Save are still exposed for read-only callers; anything that reads-then-writes should
    // use MutateUserProfile below so the whole read-modify-write happens under one lock.
    static readonly object UserProfileLock = new();

    public static UserProfile LoadUserProfile()
    {
        lock (UserProfileLock) return LoadUserProfileUnlocked();
    }

    static UserProfile LoadUserProfileUnlocked()
    {
        if (!File.Exists(UserProfilePath)) return new UserProfile();
        try { return JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(UserProfilePath), JsonOptions) ?? new UserProfile(); }
        catch { return new UserProfile(); } // corrupt file -- start fresh rather than crash startup
    }

    public static void SaveUserProfile(UserProfile profile)
    {
        lock (UserProfileLock) SaveUserProfileUnlocked(profile);
    }

    static void SaveUserProfileUnlocked(UserProfile profile)
    {
        Directory.CreateDirectory(UserDataRoot);
        AtomicWriteAllText(UserProfilePath, JsonSerializer.Serialize(profile, JsonOptions));
    }

    /// <summary>Atomically loads, applies <paramref name="mutate"/>, and saves the user profile
    /// under a single lock -- use this instead of separate LoadUserProfile()/SaveUserProfile()
    /// calls for any read-modify-write (which is nearly every caller: SetBio, SetFavoriteTeam,
    /// RecordSongTriggered, etc.) so a concurrent mutation from another thread can't be silently
    /// lost between the read and the write. Returns the saved profile.</summary>
    public static UserProfile MutateUserProfile(Func<UserProfile, UserProfile> mutate)
    {
        lock (UserProfileLock)
        {
            var updated = mutate(LoadUserProfileUnlocked());
            SaveUserProfileUnlocked(updated);
            return updated;
        }
    }

    static bool PathsPointToSameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>Durability safeguard (audit finding): every config/profile/manifest write in this
    /// file used to go straight through File.WriteAllText onto the real target path. That call is
    /// NOT atomic -- if the process is killed (crash, forced-quit, power loss, Squirrel update
    /// racing a save) partway through, the target file is left truncated/corrupt on disk. For files
    /// with a try/catch on load that "just" resets to defaults (losing real user data silently);
    /// for ConfigPath/profile files specifically it used to be worse -- see the LoadOrCreate/
    /// LoadProfile fix notes below, those had NO try/catch at all and would crash the app outright
    /// on next launch. Standard write-to-temp-then-replace pattern: the real path is only ever
    /// updated by an atomic filesystem rename, so a crash mid-write leaves either the old good file
    /// or the new good file, never a half-written one. Same fix, applied to every writer in this
    /// class for consistency (mechanical, no behavior change for the success path).</summary>
    static void AtomicWriteAllText(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmpPath = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
        try
        {
            File.WriteAllText(tmpPath, content);
            if (File.Exists(path))
                File.Replace(tmpPath, path, null); // atomic on the same volume
            else
                File.Move(tmpPath, path); // target doesn't exist yet -- Replace requires it to
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
            throw;
        }
    }

    public static List<TriggerEntry> LoadOrCreate()
    {
        Directory.CreateDirectory(SongsFolder);
        Directory.CreateDirectory(ProfilesFolder);
        Directory.CreateDirectory(TeamBackgroundsFolder);
        Directory.CreateDirectory(TeamLogosFolder);

        if (File.Exists(ConfigPath))
        {
            // BUG FIX (audit finding): unlike every other manifest in this file, this load path had
            // no try/catch at all -- a truncated/corrupt triggers.json (e.g. from a crash mid-write
            // before the AtomicWriteAllText fix, or a bad manual edit) threw straight out of
            // LoadOrCreate, which every caller (WebMainForm.cs, Bandroom.Mac's MainWindow) invokes
            // unguarded during startup, crashing the whole app before the UI ever appears -- with no
            // way for the user to recover short of manually deleting the file. Now falls back to
            // BuildDefault() like every sibling Load* method, and preserves the unreadable file
            // (renamed, not deleted) so it isn't silently lost and can still be inspected/recovered.
            try
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<List<TriggerEntry>>(json, JsonOptions) ?? new List<TriggerEntry>();
                return EnsureAllEvents(loaded);
            }
            catch
            {
                try
                {
                    string backupPath = ConfigPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    if (!File.Exists(backupPath)) File.Copy(ConfigPath, backupPath);
                }
                catch { /* best-effort forensic copy -- never let this block recovery */ }

                var recovered = BuildDefault();
                Save(recovered);
                return recovered;
            }
        }

        var defaults = BuildDefault();
        Save(defaults);
        return defaults;
    }

    public static void Save(List<TriggerEntry> entries)
    {
        AtomicWriteAllText(ConfigPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    static string ProfilePath(string name) => Path.Combine(ProfilesFolder, $"{SanitizeFileName(name)}.json");

    // Windows reserves these basenames (case-insensitive, with or without an extension) at the
    // filesystem level -- CreateFile fails on "CON.json" exactly like it fails on "CON". A
    // user-chosen profile/team name of "con", "prn", "nul", "com1".."com9", or "lpt1".."lpt9"
    // (a real risk: profile names come straight from free-text team/preset naming, e.g. "Con"
    // Air Force-adjacent nicknames, "AUX" as a joke name, etc.) used to reach SaveProfile's
    // File.WriteAllText unguarded and throw an unhandled IOException, crashing whatever UI action
    // triggered the save. Same treatment PathsPointToSameFile-adjacent sanitizers use elsewhere.
    static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        // Trailing dots/spaces are stripped by Windows itself when the file is actually created,
        // which can otherwise turn two visually-distinct sanitized names (or a sanitized name and
        // an existing file) into the same on-disk filename without either caller knowing.
        name = name.TrimEnd('.', ' ');
        if (string.IsNullOrEmpty(name) || ReservedWindowsNames.Contains(name))
            name = "_" + name; // "_CON", "_" for an all-invalid/empty input -- never collides with a reserved name
        return name;
    }

    /// <summary>Saves the CURRENT working config (whatever's loaded/edited right now) as a
    /// named, reloadable profile -- e.g. one saved setup per team. Local disk write happens first
    /// and is always synchronous/authoritative; the Supabase mirror push (CloudDatabaseService,
    /// System 1 of BANDROOM_STREAMER_MASTER_PROMPT.md) is fire-and-forget best-effort AFTER that
    /// succeeds, so a slow/unreachable/unconfigured cloud never blocks or fails a local save --
    /// Bandroom must keep working fully offline.</summary>
    public static void SaveProfile(string name, List<TriggerEntry> entries)
    {
        Directory.CreateDirectory(ProfilesFolder);
        AtomicWriteAllText(ProfilePath(name), JsonSerializer.Serialize(entries, JsonOptions));
        if (CloudDatabaseService.IsConfigured)
            QueueCloudProfilePush(name, entries);
    }

    static readonly ConcurrentDictionary<string, CancellationTokenSource> PendingCloudPushes = new();

    /// <summary>Debounces the Supabase mirror push -- SaveProfile is called on every single tick
    /// of a volume slider's 'input' event (no debounce upstream in app.js), so pushing on every
    /// call would fire dozens of concurrent unthrottled HTTP POSTs while a user just drags one
    /// slider. Cancels any pending push for this team and schedules a new one 1.5s out; only the
    /// last call in a burst actually reaches the network, with whatever `entries` looked like at
    /// that last call. entries is cloned into JSON up front (not captured by reference) so a
    /// caller mutating the same List&lt;TriggerEntry&gt; instance after this returns (the profile
    /// lists in this app are long-lived and reused, e.g. WebMainForm._config) can't change what
    /// actually gets pushed once the delay elapses.</summary>
    static void QueueCloudProfilePush(string name, List<TriggerEntry> entries)
    {
        string snapshotJson = JsonSerializer.Serialize(entries, JsonOptions);
        var cts = new CancellationTokenSource();
        PendingCloudPushes.AddOrUpdate(name, cts, (_, old) => { old.Cancel(); return cts; });
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), cts.Token);
                var snapshot = JsonSerializer.Deserialize<List<TriggerEntry>>(snapshotJson, JsonOptions) ?? new List<TriggerEntry>();
                await CloudDatabaseService.PushTeamProfileAsync(name, snapshot);
            }
            catch (TaskCanceledException)
            {
                // Superseded by a newer save for the same team within the debounce window -- expected.
            }
            finally
            {
                PendingCloudPushes.TryRemove(new KeyValuePair<string, CancellationTokenSource>(name, cts));
            }
        });
    }

    /// <summary>BUG FIX (audit finding): this used to call File.ReadAllText directly with no
    /// File.Exists check and no try/catch -- every caller (WebMainForm.cs, Bandroom.Mac) guards
    /// most calls with a prior ListProfiles().Contains(name) check, but that's a TOCTOU race (the
    /// file can be deleted/renamed by a concurrent save/delete between the check and this call) and
    /// several call sites (e.g. team-copy flows that read a just-listed name) had no guard at all.
    /// A missing or corrupt profile file threw an unhandled exception straight out of a WebView2
    /// bridge call, which has no caller-side try/catch to land in. Now matches every other Load*
    /// method's contract: falls back to BuildDefault() (the same "no saved profile yet" shape every
    /// caller already treats a missing profile as) instead of crashing, and preserves an unreadable
    /// file for forensics instead of leaving the caller stuck retrying against a file that will
    /// never parse.</summary>
    public static List<TriggerEntry> LoadProfile(string name)
    {
        string path = ProfilePath(name);
        if (!File.Exists(path)) return BuildDefault();
        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<TriggerEntry>>(json, JsonOptions) ?? new List<TriggerEntry>();
            return EnsureAllEvents(loaded);
        }
        catch
        {
            try
            {
                string backupPath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                if (!File.Exists(backupPath)) File.Copy(path, backupPath);
            }
            catch { /* best-effort forensic copy -- never let this block recovery */ }
            return BuildDefault();
        }
    }

    public static void DeleteProfile(string name)
    {
        string path = ProfilePath(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public static DateTime? GetProfileSavedAt(string name)
    {
        string path = ProfilePath(name);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    public static List<string> ListProfiles()
    {
        Directory.CreateDirectory(ProfilesFolder);
        return Directory.GetFiles(ProfilesFolder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<TriggerEntry> BuildDefault()
    {
        var list = new List<TriggerEntry>
        {
            new() { Trigger = "down:1st", Event = "1st Down", AudioFile = "" },
            new() { Trigger = "down:2nd", Event = "2nd Down", AudioFile = "" },
            new() { Trigger = "down:3rd", Event = "3rd Down", AudioFile = "" },
            new() { Trigger = "down:4th", Event = "4th Down", AudioFile = "" },
            new() { Trigger = "flag:on", Event = "Penalty Flag", AudioFile = "" },
        };

        // The game's official "Assignable Sound Events" list -- trimmed to only the ones
        // GameWatcher can actually detect by watching the screen (see GameWatcher's "situation"
        // region). Manual Numpad-hotkey triggers for the rest of the official list were scrapped
        // (they were never a real feature -- nobody's going to memorize 29 numpad combos mid-game).
        var autoDetected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Offense: Touchdown Scored"] = "situation:touchdown",
            ["Offense: PAT Made"] = "situation:pat_good",
            ["Other: Opening Kickoff"] = "situation:kickoff",
            ["Defense: Turnover Forced"] = "situation:turnover",
            ["Defense: Tackle for Loss"] = "loss:tfl",
            ["Other: Start of 4th Quarter"] = "quarter:4th",
        };

        foreach (var (eventName, trigger) in autoDetected)
            list.Add(new TriggerEntry { Trigger = trigger, Event = eventName, AudioFile = "" });

        return EnsureAllEvents(list);
    }

    /// <summary>Every EventKey the engine's 16 evaluators (src/Bandroom.Core/Helpers) can actually
    /// emit. Kept here (not derived from Bandroom.Core, which this project doesn't reference) as
    /// the UI-side source of truth for "what can ever have a song assigned." Found 2026-08-07: for
    /// months, only the 6 events in BuildDefault's `autoDetected` dict had a TriggerEntry at all --
    /// everything else the engine could detect (Second/Third/Fourth Down variants, Field Goal Made,
    /// Safety, Penalties, all 5 Timeout variants, Victory in Hand, Iced Game, Drive Starter, etc.)
    /// had no row in any profile, never appeared in the assignment UI, and could never have audio
    /// assigned -- regardless of how correct OCR/engine detection was. Update this list whenever a
    /// new evaluator/EventKey is added on the engine side, or its songs go silently unassignable.</summary>
    /// <summary>Event keys that used to have their own assignable card but were retired because
    /// they duplicated the legacy `down:1st`/`2nd`/`3rd` cards (which already have real,
    /// working song assignments via LegacyDownEventAlias) or added clutter without a distinct
    /// sound (Drive Starter) -- explicit owner request 2026-08-07: "some of these are the same
    /// and we don't need... make this simplified for people." Kept as a real EventKey string
    /// constant (not deleted from the engine) since FirstDownHelper/OffenseDownHelper/
    /// DriveStarterHelper still emit these at runtime -- removing the UI card doesn't touch
    /// firing, it only stops EnsureAllEvents from re-creating an empty duplicate slot for it.</summary>
    /// <summary>Also pruned like RetiredEventKeys, but for a different reason: these can never
    /// actually fire yet, not just "not confirmed" -- YardLine is hardcoded to 0 everywhere
    /// (never OCR'd), so any "<= 50 = midfield" check would either never pass or always pass.
    /// Showing a permanently-dead card is worse than not showing one at all -- explicit owner
    /// request 2026-08-08 to keep simplifying the list. Re-add to AllEngineEventKeys once real
    /// YardLine data exists.</summary>
    static readonly HashSet<string> BlockedEventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Offense: Earned First Down (Midfield)",
        "Offense: Second Down (Midfield)",
        "Defense: Second Down (Midfield)",
    };

    static readonly HashSet<string> RetiredEventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Added 2026-08-11: bare legacy "1st/2nd/3rd/4th Down" cards (BuildDefault's
        // down:1st/2nd/3rd/4th rows) -- these were never pruned like their "Offense: Nth Down"
        // siblings below, so every profile showed 4 permanently-"Unassigned" duplicate cards at
        // the top of the Offense list even after the owner had already assigned real songs to the
        // canonical "Offense: Second/Third Down Short" etc cards. Owner report: confusing, look
        // broken/dead. They're not actually dead -- LegacyDownEventAlias in WebMainForm.cs still
        // falls back to these by Trigger ("down:1st" etc) directly against the saved profile data,
        // independent of whether EnsureAllEvents keeps the row visible here -- so any team that
        // already has a real legacy assignment keeps it working; this only stops re-seeding empty
        // ones for everyone else.
        "1st Down",
        "2nd Down",
        "3rd Down",
        "4th Down",
        "Offense: Earned First Down",
        "Offense: Second Down",
        // REMOVED 2026-08-12 (owner call, live game): OffenseFourthDownHelper now emits this for
        // real, dual-firing alongside "Defense: Fourth Down" so the team actually driving on 4th
        // down gets its own card too, home or away, Big Game or not (see that helper's own doc
        // comment). The "nothing emits this" note that used to justify keeping this retired no
        // longer holds -- moved down into AllEngineEventKeys below, same as "Offense: Third Down"
        // the session before.
        // REMOVED 2026-08-11 (audit finding + owner call): these fire every game (any fresh
        // drive that isn't from a kickoff or turnover -- in practice, almost always the first
        // snap after a punt) but had no assignable UI card here AND no LegacyDownEventAlias
        // fallback, so nobody could ever assign a song to them -- a silent dead-on-arrival event,
        // same bug class as Discrepancy #14/#15. Moved back into AllEngineEventKeys below.
        // "Offense: Drive Starter",
        // "Defense: Drive Starter",
        // Added 2026-08-10: KickoffHelper no longer emits either of these (see its own comment) --
        // every kickoff after the opening/second-half ones now relies on "Offense: PAT Made"
        // firing right beforehand instead of its own dedicated cue, per the owner's call that the
        // two were colliding in the scorebug's shared situation slot.
        "Other: Kickoff on Kick (Receiving)",
        "Other: Kickoff on Kick (Kicking)",
        // Added 2026-08-10: FirstDownHelper's short/long rewrite dropped the yards-gained
        // "Big Gain" branch entirely (see that file's comment) -- this key is never emitted
        // anymore. Retired like the kickoff keys above: still fires if a user already has a song
        // assigned to it (RemoveAll below only prunes empty rows), just no longer offered as an
        // assignable card for anyone starting fresh.
        "Offense: Earned First Down (Big Gain)",
        // Retired 2026-08-11 (owner audit call): redundant with the generic "Defense: Tackle for
        // Loss" cue, which already fires on the exact same snap (see DefenseHelper.cs's own
        // comment and TflHelper.cs). A song already assigned to this old key just stops firing,
        // same as every other retirement in this set.
        "Defense: Third Down (Loss)",
        // Retired 2026-08-11 (owner audit call, same reasoning as Third Down (Loss) above):
        // redundant with the generic "Defense: Tackle for Loss" cue plus the plain "Defense:
        // Fourth Down" stop cue, both of which already fire on the same snap (see
        // BigEventHelper.cs's own comment). A song already assigned to this old key just stops
        // firing, same as every other retirement in this set.
        "Defense: Fourth Down (Loss)",
        // Retired 2026-08-11 (owner audit call): no replacement cue requested, just removed.
        "Defense: No Punt Return",
    };

    public static readonly string[] AllEngineEventKeys =
    {
        // Re-added 2026-08-12 (owner report: log showed "Offense: Third Down" firing unassigned
        // with no card anywhere to fix it) -- this key was left in RetiredEventKeys from before
        // 2026-08-11, but OffenseDownHelper's rewrite that same day made it a real, currently-fired
        // key again (3rd & long fires this alongside "Defense: Third Down", see that helper's own
        // comment) -- the retirement just never got undone after that change.
        "Offense: Third Down",
        // Added 2026-08-12 (owner call, live game) -- OffenseFourthDownHelper's new counterpart to
        // "Defense: Fourth Down", so the team actually driving on 4th down gets its own card too
        // (see that helper's own comment for the full history: this key used to be correctly
        // retired since nothing emitted it, that's no longer true).
        "Offense: Fourth Down",
        // Re-added 2026-08-11 (moved down from RetiredEventKeys) -- fires on every fresh drive
        // that isn't a kickoff or turnover (in practice: almost always the first snap after a
        // punt). Was never assignable and had no legacy fallback, so was a silent dead cue.
        // Renamed same day (owner audit call) from "Offense: Drive Starter" to this clearer
        // name -- see WebMainForm.RenamedEventKeyAliases for the old-key fallback.
        "Offense: 1st Down After Punt",
        // Renamed 2026-08-11 (owner audit call, same session as the offense key above) --
        // see WebMainForm.RenamedEventKeyAliases for the old-key fallback and
        // HomeOnlyAlwaysEventKeys for the new home-only gating.
        "Defense: After Punt",
        // Added 2026-08-10 alongside OffenseDownHelper's rewrite -- 2nd/3rd down now split by
        // distance instead of firing one distance-blind card. Short = offense (this card); long
        // reuses the pre-existing "Defense: Second/Third Down" cards below, unchanged.
        "Offense: Second Down Short",
        "Offense: Third Down Short",
        // Added 2026-08-11 (owner call, live game) -- DefenseSecondDownShortHelper's counterpart
        // to "Offense: Second Down Short" above, same-tick dual-fire pairing (ducked to 60 while
        // the offense side plays at full 100 -- inverse balance of the 3rd-down-short pairing).
        "Defense: Second Down Short",
        // Added 2026-08-10 alongside FirstDownHelper's short/long split -- replaces the old
        // "Offense: Earned First Down (Big Gain)" card (now retired above).
        "Offense: Earned First Down Short",
        // Added 2026-08-10: two new Defense-side evaluators (DefenseFirstDownHelper,
        // DefenseThirdDownShortHelper) -- see their own file comments for the exact trigger
        // moment and WebMainForm.ResolveEventRouting's tier-3 (First Down) / tier-2 (Third Down
        // Short) gating.
        // Renamed 2026-08-11 (owner audit call) from "Defense: First Down" -- see
        // WebMainForm.RenamedEventKeyAliases for the old-key fallback.
        "Defense: After Opening Kick",
        // Added 2026-08-12 (owner call, live game, Big Game) -- OffenseAfterOpeningKickHelper's
        // counterpart to "Defense: After Opening Kick" above, same-tick dual-fire pairing (100 for
        // the receiving team's offense vs the kicking team's defense at 60 -- same balance as
        // "Offense/Defense: Second Down Short").
        "Offense: After Opening Kick",
        "Defense: Third Down Short",
        // Added 2026-08-11 (owner audit call) -- distinct cue for converting a 3rd down
        // specifically, fires alongside the generic Earned First Down cue on the same snap.
        // See ThirdDownConversionHelper.cs.
        "Offense: 3rd Down Conversion",
        // Added 2026-08-12 (owner's own idea, live game): a big gain on 1st down that still nets
        // a fresh 1st & 10 -- Down never changes (1 -> 1), so the plain "Offense: Earned First
        // Down" cue (which only fires on Down 2/3/4 -> 1) can't see it. See
        // FirstDownOnFirstDownHelper.cs for the play-clock-edge detection this relies on.
        "Offense: First Down on First Down",
        "Offense: PAT Made",
        "Offense: 2-Point Conversion Made",
        "Offense: Field Goal Made",
        "Offense: Iced Game by First Down",
        "Offense: Victory in Hand",
        "Offense: Touchdown Scored",
        "Defense: Third Down",
        "Defense: Fourth Down",
        // Added 2026-08-13 (owner report, real game log) -- BigEventHelper's turnover-on-downs
        // stop cue split out of the plain "Defense: Fourth Down" facing-the-down key so the two
        // distinct moments (facing 4th down vs. actually stopping the offense on it) each get
        // their own assignable song instead of double-firing one shared card. See
        // BigEventHelper.cs's own comment on this split.
        "Defense: Fourth Down Stop",
        "Defense: Second Down",
        "Defense: Second Down (Loss)", // Implemented 2026-08-07 (DefenseHelper.cs)
        "Defense: Field Goal Missed by Opponent",
        "Defense: Turnover Forced",
        "Defense: Iced Game by Turnover",
        "Defense: Safety",
        "Defense: Tackle for Loss",
        "Defense: Touchdown Scored",
        "Defense: Timeout (4 Remaining)",
        "Defense: Timeout (3 Remaining)",
        "Defense: Timeout (2 Remaining)",
        "Defense: Timeout (1 Remaining)",
        "Defense: Timeout (0 Remaining)",
        "Other: Start of 2nd Quarter",
        "Other: Start of 4th Quarter",
        "Other: Pregame Ready",
        "Other: Pregame Take the Field",
        "Other: Pregame Tunnel",
        "Other: Opening Kickoff",
        "Other: Second-Half Kickoff",
        // Added 2026-08-11 (owner report, live game): a generic "kickoff after any score" cue,
        // independent of "Offense: PAT Made" firing -- see KickoffHelper.cs's own comment for why
        // relying on PAT as the sole "kickoff's coming" signal went silent when PAT OCR was missed.
        "Other: Kickoff",
        "Penalty: Offense",
        "Penalty: Defense",
    };

    /// <summary>Appends a slot for any engine EventKey missing from `entries`, so every event the
    /// engine can detect is assignable in the UI -- without touching entries that already exist, so
    /// saved song assignments are never disturbed. Called from every load/build path.
    ///
    /// Pre-engine profiles keyed their down slots as bare "1st/2nd/3rd/4th Down" (Trigger
    /// down:1st/2nd/3rd/4th). WebMainForm.FireEventForSide already falls back to these via
    /// LegacyDownEventAlias whenever the canonical "Offense: Nth Down" slot is empty, so a legacy
    /// assignment fires correctly with no migration needed. A migration step used to run here on
    /// every load (promote legacy AudioFile into the canonical slot, then blank the legacy one) --
    /// removed 2026-08-10: since it ran on every launch, not once, it kept re-blanking the user-
    /// visible legacy card immediately after they'd just assigned a song to it, which looked
    /// exactly like "assigning a song doesn't save." The fallback already made migration
    /// functionally unnecessary for firing; it only ever helped the (retired, hidden-by-default)
    /// canonical card show a value, at the cost of destroying the one the user could actually see.</summary>
    public static List<TriggerEntry> EnsureAllEvents(List<TriggerEntry> entries)
    {
        // Prune already-persisted rows for retired duplicate events -- only when nothing was
        // ever assigned to them, so no existing user song assignment is ever silently dropped.
        entries.RemoveAll(e => (RetiredEventKeys.Contains(e.Event) || BlockedEventKeys.Contains(e.Event)) && string.IsNullOrWhiteSpace(e.AudioFile));

        var existing = new HashSet<string>(entries.Select(e => e.Event), StringComparer.OrdinalIgnoreCase);
        foreach (var key in AllEngineEventKeys)
        {
            if (!existing.Contains(key))
                entries.Add(new TriggerEntry { Trigger = $"auto:{key}", Event = key, AudioFile = "" });
        }
        return entries;
    }
}
