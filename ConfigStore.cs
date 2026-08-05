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
    static readonly string FirstRunFlagPath = Path.Combine(UserDataRoot, ".firstrun_done");
    static readonly string ScorebugPresetPath = Path.Combine(UserDataRoot, "scorebug_preset.txt");

    public static string LoadScorebugPresetName() =>
        File.Exists(ScorebugPresetPath) ? File.ReadAllText(ScorebugPresetPath).Trim() : ScorebugPreset.KamsCbsScorebug.Name;

    public static void SaveScorebugPresetName(string name)
    {
        Directory.CreateDirectory(UserDataRoot);
        File.WriteAllText(ScorebugPresetPath, name);
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

    public static List<MarketplaceDownloadEntry> LoadMarketplaceDownloads()
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
        var entries = LoadMarketplaceDownloads();
        entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        var entry = new MarketplaceDownloadEntry { Type = type, Name = name, School = school, Path = path };
        entries.Add(entry);
        SaveMarketplaceDownloads(entries);
        return entry;
    }

    /// <summary>Removes a "My Downloads" entry and deletes its local file. Returns false if the
    /// id wasn't found (already removed, stale UI) -- not an error, just a no-op.</summary>
    public static bool RemoveMarketplaceDownload(string id)
    {
        var entries = LoadMarketplaceDownloads();
        var entry = entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return false;

        entries.Remove(entry);
        SaveMarketplaceDownloads(entries);
        try { if (File.Exists(entry.Path)) File.Delete(entry.Path); } catch { /* best-effort */ }
        return true;
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
            return JsonSerializer.Deserialize<List<TriggerEntry>>(json, JsonOptions) ?? new List<TriggerEntry>();
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
        return JsonSerializer.Deserialize<List<TriggerEntry>>(json, JsonOptions) ?? new List<TriggerEntry>();
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

        return list;
    }
}
