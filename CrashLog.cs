namespace SupremeStadiumSoundSelector;

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
