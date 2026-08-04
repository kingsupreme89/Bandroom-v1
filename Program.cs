using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("Unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        Application.ThreadException += (_, e) => CrashLog.Write("UI thread exception", e.Exception);

        // Per-Monitor V2: GetWindowRect and CopyFromScreen must agree on physical pixels,
        // or the OCR crop region silently drifts off target on any scaled display.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppFonts.EnsureLoaded();

        // Initialize auto-update system (Sparkle)
        InitializeAutoUpdater();

        Application.Run(new WebMainForm());
    }

    static void InitializeAutoUpdater()
    {
        try
        {
            // TODO: Uncomment and configure once Sparkle API is verified
            // URL to your appcast.xml (replace with your actual GitHub repo)
            // string appcastUrl = "https://raw.githubusercontent.com/YourUsername/Bandroom/main/appcast.xml";
            // var updater = new Sparkle.Sparkle(appcastUrl);
            // updater.CheckForUpdatesQuietly();
        }
        catch (Exception ex)
        {
            // Auto-update failure shouldn't crash the app
            CrashLog.Write("Auto-update initialization failed", ex);
        }
    }
}
