// Platform stubs for Windows-specific types that can't compile on macOS/Avalonia.
// These provide the same public API surface used by shared source files.

using System;

namespace SupremeStadiumSoundSelector;

/// <summary>Mac-compatible CrashLog — writes to stderr instead of WinForms MessageBox.</summary>
internal static class CrashLog
{
    public static void Write(string message, Exception? ex = null)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string entry = ex != null
            ? $"[{timestamp}] {message}\n{ex}"
            : $"[{timestamp}] {message}";
        Console.Error.WriteLine(entry);

        // Also append to a local crash log file
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bandroom");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"), entry + "\n");
        }
        catch { }
    }
}

/// <summary>Mac-compatible Theme stub — provides only the members referenced by MacWebBridge.
/// Matches the real Theme.cs signature: ActiveTeam is a TeamColor, not a custom type.</summary>
internal static class Theme
{
    private static TeamColor _activeTeam = TeamColors.All[0];
    public static TeamColor ActiveTeam
    {
        get => _activeTeam;
        set => _activeTeam = value;
    }

    public static System.Drawing.Color WindowBg => System.Drawing.Color.FromArgb(15, 15, 19);
    public static System.Drawing.Color Accent => ActiveTeam.Accent;
    public static System.Drawing.Color AccentDark => ActiveTeam.Primary;
    public static System.Drawing.Color AccentBright => ActiveTeam.Accent;
}

/// <summary>Mac-compatible TeamBackdrop stub — provides FindImagePath matching Windows signature.</summary>
internal static class TeamBackdrop
{
    public static string? FindImagePath(string teamName)
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bandroom", "UserData", "TeamBackgrounds");
        if (!Directory.Exists(folder)) return null;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string path = Path.Combine(folder, teamName + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

/// <summary>Mac-compatible TeamBackgroundDownloadService stub.
/// The Windows version downloads trophy room images and saves them locally.
/// On Mac, this is a future TODO for native NSImage-based download.</summary>
internal static class TeamBackgroundDownloadService
{
    public static async System.Threading.Tasks.Task<string?> DownloadAndSaveAsync(string team, string url)
    {
        // Stub — real implementation would use HttpClient to download
        // and save to ConfigStore.TeamBackgroundsFolder
        await System.Threading.Tasks.Task.CompletedTask;
        return null;
    }
}