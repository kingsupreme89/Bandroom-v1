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

    /// <summary>Where the installer would have bundled the default song pack, if this build
    /// includes it (dev builds and any future full build still do -- see BundleDefaultSongs in
    /// BandAudioHook.csproj). Public releases from v1.0.48 on don't, so this is empty there.</summary>
    static readonly string BundledDefaultSongsFolder = Path.Combine(AppContext.BaseDirectory, "Songs", "Default");
    /// <summary>Where DefaultSongPackService extracts the pack after the user opts into the
    /// one-time download (see cloudflare-defaultsongs). Lives under UserDataRoot so, like
    /// everything else there, Squirrel updates never touch or wipe it.</summary>
    public static readonly string DownloadedDefaultSongsFolder = Path.Combine(UserDataRoot, "DefaultSongs");

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
        File.Exists(ScorebugPresetPath) ? File.ReadAllText(ScorebugPresetPath).Trim() : ScorebugPreset.KamsCbsScorebug.Name;

    public static void SaveScorebugPresetName(string name)
    {
        Directory.CreateDirectory(UserDataRoot);
        File.WriteAllText(ScorebugPresetPath, name);
    }

    /// <summary>
    /// Imports default song pack assignments for a team. Looks in the bundled
    /// Songs\Default\{conference}\{teamName}\ folder and maps each EventKey-named
    /// .mp3 to the corresponding TriggerEntry in the profile.
    /// Returns the number of events auto-assigned.
    /// </summary>
    public static int ImportDefaultPackForTeam(string teamName, List<TriggerEntry> profile)
    {
        int assigned = 0;

        // Search all conference subfolders for this team
        string? teamFolder = null;
        if (Directory.Exists(DefaultSongsFolder))
        {
            foreach (var confDir in Directory.GetDirectories(DefaultSongsFolder))
            {
                string candidate = Path.Combine(confDir, teamName);
                if (Directory.Exists(candidate))
                {
                    teamFolder = candidate;
                    break;
                }
            }
        }

        if (teamFolder == null) return 0;

        foreach (var file in Directory.GetFiles(teamFolder, "*.*", SearchOption.TopDirectoryOnly))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (!AudioExtensions.Contains(ext)) continue;

            // Filename format: Offense_ Earned First Down.mp3 → "Offense: Earned First Down"
            string name = Path.GetFileNameWithoutExtension(file);
            string eventKey = name.Replace("_", ": ").Replace("  ", " ");
            // Handle variant suffixes like "Offense_ Touchdown Scored_2" → "Offense: Touchdown Scored"
            eventKey = System.Text.RegularExpressions.Regex.Replace(eventKey, @"_\d+$", "");

            var entry = profile.FirstOrDefault(e =>
                e.Event.Equals(eventKey, StringComparison.OrdinalIgnoreCase));
            if (entry != null && string.IsNullOrWhiteSpace(entry.AudioFile))
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

            string name = Path.GetFileNameWithoutExtension(file);
            string eventKey = name.Replace("_", ": ").Replace("  ", " ");
            eventKey = System.Text.RegularExpressions.Regex.Replace(eventKey, @"_\d+$", "");

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
            return JsonSerializer.Deserialize<List<MarketplaceDownloadEntry>>(json, JsonOptions) ?? new List<MarketplaceDownloadEntry>();
        }
        catch { return new List<MarketplaceDownloadEntry>(); } // corrupt manifest shouldn't crash the whole downloads tab
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
            return JsonSerializer.Deserialize<List<LocalTrackEntry>>(json, JsonOptions) ?? new List<LocalTrackEntry>();
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

    public static readonly string AvatarFolder = Path.Combine(UserDataRoot, "Avatar");

    public static UserProfile LoadUserProfile()
    {
        if (!File.Exists(UserProfilePath)) return new UserProfile();
        try { return JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(UserProfilePath), JsonOptions) ?? new UserProfile(); }
        catch { return new UserProfile(); } // corrupt file -- start fresh rather than crash startup
    }

    public static void SaveUserProfile(UserProfile profile)
    {
        Directory.CreateDirectory(UserDataRoot);
        File.WriteAllText(UserProfilePath, JsonSerializer.Serialize(profile, JsonOptions));
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
    /// named, reloadable profile -- e.g. one saved setup per team.</summary>
    public static void SaveProfile(string name, List<TriggerEntry> entries)
    {
        Directory.CreateDirectory(ProfilesFolder);
        File.WriteAllText(ProfilePath(name), JsonSerializer.Serialize(entries, JsonOptions));
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
            new() { Trigger = "down:4th", Event = "4th Down", AudioFile = @"C:\Games\Mod Folder\CFB Mods\MMC_Editor_v1.1.0.2\dies irie 0.wav" },
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
        "Offense: Drive Starter",
        "Defense: Drive Starter",
    };

    public static readonly string[] AllEngineEventKeys =
    {
        "Offense: Earned First Down (Big Gain)",
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
    /// saved song assignments are never disturbed. Called from every load/build path.</summary>
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
