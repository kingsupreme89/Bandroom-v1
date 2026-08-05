using System.Text.Json;

namespace SupremeStadiumSoundSelector;

internal static class ConfigStore
{
    public static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "triggers.json");
    public static readonly string SongsFolder = Path.Combine(AppContext.BaseDirectory, "Songs");
    public static readonly string ProfilesFolder = Path.Combine(AppContext.BaseDirectory, "Profiles");
    public static readonly string TeamBackgroundsFolder = Path.Combine(AppContext.BaseDirectory, "TeamBackgrounds");
    static readonly string FirstRunFlagPath = Path.Combine(AppContext.BaseDirectory, ".firstrun_done");

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

        Directory.CreateDirectory(SongsFolder);
        string baseName = Path.GetFileNameWithoutExtension(sourcePath).ToUpperInvariant();
        string destPath = Path.Combine(SongsFolder, baseName + ext);
        int suffix = 2;
        while (File.Exists(destPath) && !PathsPointToSameFile(sourcePath, destPath))
            destPath = Path.Combine(SongsFolder, $"{baseName} ({suffix++}){ext}");

        if (!PathsPointToSameFile(sourcePath, destPath))
            File.Copy(sourcePath, destPath, overwrite: false);
        return destPath;
    }

    static bool PathsPointToSameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static List<TriggerEntry> LoadOrCreate()
    {
        Directory.CreateDirectory(SongsFolder);
        Directory.CreateDirectory(ProfilesFolder);
        Directory.CreateDirectory(TeamBackgroundsFolder);

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
            new() { Trigger = "down:1st", Event = "Down: 1st", AudioFile = "" },
            new() { Trigger = "down:2nd", Event = "Down: 2nd", AudioFile = "" },
            new() { Trigger = "down:3rd", Event = "Down: 3rd", AudioFile = "" },
            new() { Trigger = "down:4th", Event = "Down: 4th", AudioFile = @"C:\Games\Mod Folder\CFB Mods\MMC_Editor_v1.1.0.2\dies irie 0.wav" },
            new() { Trigger = "flag:on", Event = "Penalty Flag", AudioFile = "" },
        };

        // The game's official "Assignable Sound Events" list (Offense / Defense / Other).
        string[] events =
        {
            "Offense: Earned First Down","Offense: Earned First Down (Big Gain)","Offense: Earned First Down (Midfield)",
            "Offense: Touchdown Scored","Offense: Second Down","Offense: Second Down (Midfield)","Offense: Third Down",
            "Offense: Field Goal Made","Offense: Drive Starter","Offense: 2-Point Conversion Made","Offense: PAT Made",
            "Offense: Iced Game by First Down","Offense: Victory in Hand",
            "Defense: Touchdown Scored","Defense: Third Down","Defense: Third Down (Loss)","Defense: Fourth Down",
            "Defense: Fourth Down (Loss)","Defense: Second Down","Defense: Second Down (Midfield)","Defense: Second Down (Loss)",
            "Defense: Field Goal Missed by Opponent","Defense: Drive Starter","Defense: Turnover Forced",
            "Defense: Iced Game by Turnover","Defense: Safety",
            "Other: Opening Kickoff","Other: Second-Half Kickoff","Other: Opening Kickoff on Kick",
            "Other: Kickoff on Kick (Kicking)","Other: Kickoff on Kick (Receiving)","Other: Pregame Take the Field",
            "Other: Start of 2nd Quarter","Other: Start of 4th Quarter"
        };

        string[] banks = { "", "Ctrl+", "Shift+", "Alt+" };
        string[] digits = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };

        // Confirmed via live scorebug screenshots: these 4 states are OCR-detectable
        // (see GameWatcher's "situation" region), so they get an auto-trigger instead of
        // a hotkey. Everything else still needs a manual Numpad press for now.
        var autoDetected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Offense: Touchdown Scored"] = "situation:touchdown",
            ["Offense: PAT Made"] = "situation:pat_good",
            ["Other: Opening Kickoff"] = "situation:kickoff",
            ["Defense: Turnover Forced"] = "situation:turnover",
        };

        for (int i = 0; i < events.Length; i++)
        {
            string trigger = autoDetected.TryGetValue(events[i], out var stateTrigger)
                ? stateTrigger
                : $"key:{banks[i / 10]}Numpad{digits[i % 10]}";
            list.Add(new TriggerEntry { Trigger = trigger, Event = events[i], AudioFile = "" });
        }

        return list;
    }
}
