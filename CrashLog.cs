namespace SupremeStadiumSoundSelector;

/// <summary>
/// Best-effort user-facing side of the global crash guard (see Program.cs for where these hook
/// in). Deliberately separate from CrashLog: CrashLog's only job is "write bytes to disk", this
/// is "tell the human", and the two must not be coupled in a way where one failing takes the
/// other down with it -- a MessageBox failing to show must never prevent the log write, and a
/// disk-full/permissions failure writing crash.log must never throw its way back out into a
/// handler that's already busy reporting a crash.
/// </summary>
internal static class CrashGuard
{
    /// <summary>
    /// Shows a small non-blocking-in-spirit heads-up that something went wrong and was logged.
    /// Wrapped in its own try/catch: this runs from inside exception handlers, so if showing the
    /// dialog itself throws (e.g. called too early before a window handle exists, or off a
    /// thread that can't touch UI), we swallow that too rather than letting a crash-reporting
    /// path become a second crash.
    /// </summary>
    public static void TryNotifyUser(string message)
    {
        try
        {
            System.Windows.Forms.MessageBox.Show(
                message,
                "Bandroom",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
        catch
        {
            // If even showing the dialog fails, there is nothing more this method can safely do.
        }
    }
}

internal static class CrashLog
{
    static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
    static readonly object Lock = new();

    public static void Write(string context, Exception ex)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\r\n\r\n");
            }
        }
        catch
        {
            // If we can't even write the crash log, there's nothing more we can do here.
        }
    }
}
