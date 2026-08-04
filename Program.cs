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
            // Sparkle will check this URL for new versions (the appcast.xml file)
            const string AppcastUrl = "https://raw.githubusercontent.com/strokaonair/Bandroom/main/appcast.xml";

            // Create the Sparkle updater instance
            // This checks for updates on startup and periodically in background
            var updater = new Sparkle.Sparkle(AppcastUrl);

            // Start the update loop:
            // - Checks for updates on startup (first param: true)
            // - Periodically re-checks every 24 hours (second param: true)
            updater.StartLoop(true, true);
        }
        catch (Exception ex)
        {
            // Auto-update failure shouldn't crash the app
            CrashLog.Write("Auto-update initialization failed", ex);
        }
    }
}
