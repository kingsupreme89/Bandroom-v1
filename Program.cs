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
            // IMPORTANT: Sparkle auto-update configuration
            // The appcast.xml file at this URL contains the latest version info
            const string AppcastUrl = "https://raw.githubusercontent.com/strokaonair/Bandroom/main/appcast.xml";

            // TODO: Wire up Sparkle auto-update library
            // Current status: Sparkle NuGet package added, appcast.xml configured
            // Next: Implement using correct Sparkle API for .NET
            // For now: Manual updates available at GitHub releases page

            Log?.Invoke("[AutoUpdate] Appcast configured: " + AppcastUrl);
        }
        catch (Exception ex)
        {
            // Auto-update failure shouldn't crash the app
            CrashLog.Write("Auto-update initialization failed", ex);
        }
    }

    static Action<string>? Log;
}
