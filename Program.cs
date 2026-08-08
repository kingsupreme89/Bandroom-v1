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

        // Global crash guard -- three sources of unhandled exceptions in a WinForms+WebView2 app:
        //   1. AppDomain.UnhandledException: any non-UI thread (background Task continuations
        //      that escape without being awaited, timers, etc.). By the time .NET raises this,
        //      the process IS going down -- there is no way to stop that from here. All we can
        //      do is log everything we know before it dies, so the crash is diagnosable after
        //      the fact instead of a silent, unexplained exit.
        //   2. Application.ThreadException: exceptions on the main UI thread (button click
        //      handlers, paint events, etc.) that bubble out of WinForms' own message loop.
        //      Unlike #1, WinForms does NOT have to kill the process for these -- as long as a
        //      handler is attached AND SetUnhandledExceptionMode is explicitly set to
        //      CatchException (the default depends on whether a debugger is attached, so don't
        //      rely on the implicit default), WinForms swallows the exception after this handler
        //      returns and keeps the message loop -- and the main window -- alive.
        //   3. TaskScheduler.UnobservedTaskException: a Task faulted and nothing ever observed
        //      the fault (no .Wait()/.Result/await) before it got garbage collected. Without this
        //      handler these are silent by default in modern .NET -- they'd neither crash nor log,
        //      which is worse than a crash for diagnosability. Marking e.SetObserved() here is
        //      what prevents the finalizer thread from re-throwing it as a process-ending error.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown");
            CrashLog.Write("Unhandled exception (process terminating)", ex);
            // Best-effort only -- the CLR may already be tearing the process down by the time
            // this runs, so there's no guarantee the dialog gets a chance to paint. Still worth
            // trying: if it does show, the user gets a signal instead of a silent vanish.
            CrashGuard.TryNotifyUser("Bandroom hit an unexpected error and needs to close. Details were saved to crash.log.");
        };
        Application.ThreadException += (_, e) =>
        {
            CrashLog.Write("UI thread exception (recovered)", e.Exception);
            CrashGuard.TryNotifyUser("Something went wrong, but Bandroom kept running. Details were saved to crash.log.");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved task exception (recovered)", e.Exception);
            e.SetObserved(); // prevents this from escalating into a process-ending error
        };

        // Per-Monitor V2: GetWindowRect and CopyFromScreen must agree on physical pixels,
        // or the OCR crop region silently drifts off target on any scaled display.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppFonts.EnsureLoaded();

        // Fire-and-forget: gets the WaveOut/audio driver initialized before the first real
        // trigger fires, so THAT call isn't the one eating the one-time device-init cost (see
        // AudioPlayer.Warmup for why this matters for tackle-to-sound latency).
        AudioPlayer.Warmup();

        Application.Run(new WebMainForm());
    }
}
