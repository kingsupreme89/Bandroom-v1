using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>Catches the real bug behind "I ran an old Setup.exe and now the app looks
/// broken/missing features": Squirrel's Setup.exe happily installs whatever version it was
/// built for, even backward over a newer install, with zero warning. Someone who re-downloads
/// or re-runs an old cached installer silently downgrades and has no way to tell.
///
/// Fix: remember the highest version this machine has ever actually run, in a location that
/// survives Squirrel's per-version app-X.X.X folder swaps (NOT AppContext.BaseDirectory --
/// that folder gets replaced/deleted on every update). If the CURRENT running version is lower
/// than the highest one seen before, this machine got downgraded -- tell the user plainly and
/// push them straight at Update.exe instead of a vague "update available" chime.</summary>
internal static class VersionGuard
{
    static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bandroom", "highest_version_seen.json");

    public static bool CheckAndRecord(Version current)
    {
        Version? previousHighest = ReadMarker();
        bool downgraded = previousHighest != null && current < previousHighest;

        Version toWrite = (previousHighest == null || current > previousHighest) ? current : previousHighest;
        WriteMarker(toWrite);

        return downgraded;
    }

    static Version? ReadMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            string raw = File.ReadAllText(MarkerPath);
            var doc = JsonSerializer.Deserialize<Marker>(raw);
            return doc?.Version != null ? Version.Parse(doc.Version) : null;
        }
        catch
        {
            return null; // corrupt/missing marker -- treat as "no history", never block startup
        }
    }

    static void WriteMarker(Version version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, JsonSerializer.Serialize(new Marker { Version = version.ToString() }));
        }
        catch { /* best-effort -- never block startup over a marker write failure */ }
    }

    sealed class Marker { public string? Version { get; set; } }
}
