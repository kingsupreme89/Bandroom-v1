namespace SupremeStadiumSoundSelector;

/// <summary>
/// Plain-English, user-facing record of "what just happened with the audio engine" -- NOT the
/// same thing as OnLog/FlushOcrLog's ocr_debug.log (WebMainForm.cs), which is raw OCR text for
/// developers. This is the opposite audience: a non-technical user wondering "why didn't my song
/// play" needs a sentence they can actually read, not "possession null" or "Dedupe". Every entry
/// here is meant to be shown directly in the app UI (Help &amp; Guide -&gt; Event Log tab) and/or
/// exported to a file to hand to a developer for support, so keep the wording plain on the way in
/// rather than translating it later.
///
/// Small in-memory ring buffer, same "static class, simple lock, capped list" shape as CrashLog.cs
/// nearby, just kept in memory instead of written to disk on every entry (ExportEventActivityLog
/// in WebMainForm.cs handles writing it out on demand).
/// </summary>
internal static class EventActivityLog
{
    const int MaxEntries = 200;

    public readonly struct Entry
    {
        public readonly DateTime Timestamp;
        public readonly string EventKey;
        public readonly string Side; // "home", "away", or "n/a" for side-agnostic events
        public readonly string Message; // plain-English, ready to show a user as-is

        public Entry(DateTime timestamp, string eventKey, string side, string message)
        {
            Timestamp = timestamp;
            EventKey = eventKey;
            Side = side;
            Message = message;
        }

        /// <summary>Single display line, e.g. "3:42:10 PM -- Touchdown Scored (Home) -- played
        /// 'Fight Song.mp3'". Used both by the UI feed and the exported text file so the two
        /// always read identically.</summary>
        public string ToDisplayString() => $"{Timestamp:h:mm:ss tt} -- {Message}";
    }

    static readonly object Lock = new();
    static readonly List<Entry> Entries = new(MaxEntries);

    /// <summary>Appends one entry, dropping the oldest once the buffer is full. Side should be
    /// "home", "away", or "n/a" for side-agnostic ("Other:*") events. Message must already be
    /// plain English -- no internal jargon -- since it's shown to the user verbatim.</summary>
    public static void Record(string eventKey, string side, string message)
    {
        lock (Lock)
        {
            Entries.Add(new Entry(DateTime.Now, eventKey ?? "", side ?? "n/a", message ?? ""));
            if (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }
    }

    /// <summary>Snapshot of the current buffer, oldest first. Returns a copy so callers (the web
    /// bridge, the exporter) never race the lock held during Record.</summary>
    public static List<Entry> GetSnapshot()
    {
        lock (Lock)
        {
            return new List<Entry>(Entries);
        }
    }

    /// <summary>Turns an internal EventKey like "Offense: Touchdown Scored" or
    /// "Other: Opening Kickoff" into something that reads naturally in a log line -- strips the
    /// "Offense:"/"Defense:"/"Other:"/"Penalty:" routing prefix, which is meaningful to the code
    /// but not to a user reading "why didn't my song play".</summary>
    public static string FriendlyEventName(string eventKey)
    {
        if (string.IsNullOrEmpty(eventKey)) return "That event";
        int colon = eventKey.IndexOf(':');
        return colon >= 0 && colon < eventKey.Length - 1 ? eventKey[(colon + 1)..].Trim() : eventKey;
    }
}
