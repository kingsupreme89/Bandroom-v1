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
    public static readonly string TeamBackgroundsFolder = Path.Combine(UserDataRoot, "TeamBackgrounds");
    public static readonly string TeamLogosFolder = Path.Combine(UserDataRoot, "TeamLogos");
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
            File.WriteAllText(CustomTeamsPath, JsonSerializer.Serialize(entries, JsonOptions));
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
        File.WriteAllText(BigGameSettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        _bigGameSettingsCache = settings;
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
            File.WriteAllText(DefaultSongsFolderOverridePath, newFolder);
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
        File.Exists(ScorebugPresetPath) ? File.ReadAllText(ScorebugPresetPath).Trim() : ScorebugPreset.KamsCbsScorebugV3.Name;

    public static void SaveScorebugPresetName(string name)
    {
        Directory.CreateDirectory(UserDataRoot);
        File.WriteAllText(ScorebugPresetPath, name);
    }

    /// <summary>Persists the Mixer panel's Lead-In Whistle on/off toggle across restarts --
    /// without this, the whistle would silently default back on every launch just because the
    /// clip file still exists on disk, ignoring a user who explicitly turned it off last session.</summary>
    public static bool LoadLeadInWhistleEnabled() =>
        !File.Exists(LeadInWhistleEnabledPath) || File.ReadAllText(LeadInWhistleEnabledPath).Trim() != "false";

    public static void SaveLeadInWhistleEnabled(bool enabled)
    {
        Directory.CreateDirectory(UserDataRoot);
        File.WriteAllText(LeadInWhistleEnabledPath, enabled ? "true" : "false");
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
        File.WriteAllText(SupabaseSettingsPath, JsonSerializer.Serialize(new SupabaseSettings(url, anonKey)));
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

    public static void MarkFirstRunDone() => File.WriteAllText(FirstRunFlagPath, DateTime.UtcNow.ToString("O"));

    static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac" };

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
        File.WriteAllText(MarketplaceDownloadsManifestPath, JsonSerializer.Serialize(entries, JsonOptions));
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
        File.WriteAllText(LocalTracksManifestPath, JsonSerializer.Serialize(entries, JsonOptions));
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
        File.WriteAllText(AuthSessionPath, JsonSerializer.Serialize(session, JsonOptions));
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
        File.WriteAllText(TeamLogoSyncManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
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
        File.WriteAllText(PublicTeamLogoSyncManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
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
        File.WriteAllText(UserProfilePath, JsonSerializer.Serialize(profile, JsonOptions));
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

    public static List<TriggerEntry> LoadOrCreate()
    {
        Directory.CreateDirectory(SongsFolder);
        Directory.CreateDirectory(ProfilesFolder);
        Directory.CreateDirectory(TeamBackgroundsFolder);
        Directory.CreateDirectory(TeamLogosFolder);

        if (File.Exists(ConfigPath))
        {
            string json = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<List<TriggerEntry>>(json, JsonOptions) ?? new List<TriggerEntry>();
            return EnsureAllEvents(loaded);
        }

        var defaults = BuildDefault();
        Save(defaults);
        return defaults;
    }

    public static void Save(List<TriggerEntry> entries)
    {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    static string ProfilePath(string name) => Path.Combine(ProfilesFolder, $"{SanitizeFileName(name)}.json");

    static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
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
        File.WriteAllText(ProfilePath(name), JsonSerializer.Serialize(entries, JsonOptions));
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

    public static List<TriggerEntry> LoadProfile(string name)
    {
        string json = File.ReadAllText(ProfilePath(name));
        var loaded = JsonSerializer.Deserialize<List<TriggerEntry>>(json, JsonOptions) ?? new List<TriggerEntry>();
        return EnsureAllEvents(loaded);
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
        "Offense: Earned First Down",
        "Offense: Second Down",
        "Offense: Third Down",
        // Added 2026-08-08 alongside OffenseDownHelper now emitting this (STATE_MACHINE_ANALYSIS.md
        // Discrepancy #10) -- same simplification bucket as Second/Third Down above: fires at
        // runtime, reachable via LegacyDownEventAlias's new down:4th mapping (see WebMainForm.cs),
        // just no separate UI card.
        "Offense: Fourth Down",
        "Offense: Drive Starter",
        "Defense: Drive Starter",
        // Added 2026-08-10: KickoffHelper no longer emits either of these (see its own comment) --
        // every kickoff after the opening/second-half ones now relies on "Offense: PAT Made"
        // firing right beforehand instead of its own dedicated cue, per the owner's call that the
        // two were colliding in the scorebug's shared situation slot.
        "Other: Kickoff on Kick (Receiving)",
        "Other: Kickoff on Kick (Kicking)",
    };

    public static readonly string[] AllEngineEventKeys =
    {
        "Offense: Earned First Down (Big Gain)",
        // Added 2026-08-10 alongside OffenseDownHelper's rewrite -- 2nd/3rd down now split by
        // distance instead of firing one distance-blind card. Short = offense (this card); long
        // reuses the pre-existing "Defense: Second/Third Down" cards below, unchanged.
        "Offense: Second Down Short",
        "Offense: Third Down Short",
        "Offense: PAT Made",
        "Offense: 2-Point Conversion Made",
        "Offense: Field Goal Made",
        "Offense: Iced Game by First Down",
        "Offense: Victory in Hand",
        "Offense: Touchdown Scored",
        "Defense: Third Down",
        "Defense: Fourth Down",
        "Defense: Third Down (Loss)",
        "Defense: Second Down",
        "Defense: Second Down (Loss)", // Implemented 2026-08-07 (DefenseHelper.cs)
        "Defense: Fourth Down (Loss)", // Implemented 2026-08-07 (BigEventHelper.cs)
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
        "Other: Opening Kickoff",
        "Other: Second-Half Kickoff",
        "Other: Kickoff on Kick (Receiving)",
        "Other: Kickoff on Kick (Kicking)",
        "Penalty: Offense",
        "Penalty: Defense",
        "Defense: No Punt Return",
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
