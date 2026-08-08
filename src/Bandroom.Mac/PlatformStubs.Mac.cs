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
/// TeamColor.Accent is non-nullable (Primary ?? DefaultAccent), Primary is Color?.</summary>
internal static class Theme
{
    private static TeamColor _activeTeam = TeamColors.All[0];
    private static readonly System.Drawing.Color _defaultDark = System.Drawing.Color.FromArgb(79, 70, 229);

    public static TeamColor ActiveTeam
    {
        get => _activeTeam;
        set => _activeTeam = value;
    }

    public static System.Drawing.Color WindowBg => System.Drawing.Color.FromArgb(15, 15, 19);

    // Accent is non-nullable — TeamColor.Accent always returns a Color (Primary ?? DefaultAccent)
    public static System.Drawing.Color Accent => _activeTeam.Accent;

    // Primary is Color? — fall back to a dark indigo if null
    public static System.Drawing.Color AccentDark => _activeTeam.Primary ?? _defaultDark;

    // Same as Accent
    public static System.Drawing.Color AccentBright => _activeTeam.Accent;
}

/// <summary>Mac-compatible TeamBackdrop stub — provides FindImagePath matching Windows signature.
/// Checks both UserData/TeamBackgrounds (downloaded) and the bundled TeamBackgrounds folder.</summary>
internal static class TeamBackdrop
{
    public static string? FindImagePath(string teamName)
    {
        // 1. Check UserData/TeamBackgrounds (downloaded via marketplace)
        string userFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bandroom", "UserData", "TeamBackgrounds");
        if (Directory.Exists(userFolder))
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string path = Path.Combine(userFolder, teamName + ext);
                if (File.Exists(path)) return path;
            }
        }

        // 2. Check bundled TeamBackgrounds folder in app base directory
        string appBase = AppDomain.CurrentDomain.BaseDirectory;
        string bundledFolder = Path.Combine(appBase, "TeamBackgrounds");
        if (!Directory.Exists(bundledFolder))
        {
            // Fallback for dev builds
            bundledFolder = Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "TeamBackgrounds"));
        }
        if (Directory.Exists(bundledFolder))
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string path = Path.Combine(bundledFolder, teamName + ext);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }
}

/// <summary>Mac-compatible TeamBackgroundDownloadService — downloads team background images
/// and saves them to the UserData/TeamBackgrounds folder.</summary>
internal static class TeamBackgroundDownloadService
{
    public static async System.Threading.Tasks.Task<string?> DownloadAndSaveAsync(string team, string url)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bandroom", "UserData", "TeamBackgrounds");
            Directory.CreateDirectory(folder);

            // Determine extension from URL or default to .jpg
            string ext = ".jpg";
            try
            {
                string urlPath = new Uri(url).AbsolutePath;
                string urlExt = Path.GetExtension(urlPath).ToLowerInvariant();
                if (urlExt == ".png" || urlExt == ".jpg" || urlExt == ".jpeg")
                    ext = urlExt;
            }
            catch { }

            string filePath = Path.Combine(folder, team + ext);

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            byte[] imageBytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(filePath, imageBytes);

            return filePath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TeamBackgroundDownloadService.Mac] Download failed: {ex.Message}");
            return null;
        }
    }
}