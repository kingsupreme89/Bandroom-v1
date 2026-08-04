using System.Windows.Forms;
using Squirrel;

namespace SupremeStadiumSoundSelector;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Squirrel hooks must run before anything else — handles install/update/uninstall events
        // that the installer fires on first run, update, and uninstall.
        SquirrelAwareApp.HandleEvents(
            onInitialInstall: (_, tools) => tools.CreateShortcutForThisExe(),
            onAppUpdate:      (_, tools) => tools.CreateShortcutForThisExe(),
            onAppUninstall:   (_, tools) => tools.RemoveShortcutForThisExe()
        );

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("Unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        Application.ThreadException += (_, e) => CrashLog.Write("UI thread exception", e.Exception);

        // Per-Monitor V2: GetWindowRect and CopyFromScreen must agree on physical pixels,
        // or the OCR crop region silently drifts off target on any scaled display.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppFonts.EnsureLoaded();

        Application.Run(new WebMainForm());
    }
}
