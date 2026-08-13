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

    /// <summary>Owner request 2026-08-11: a relaunch (or a crash) wiped the whole in-memory log,
    /// so exporting right after startup produced an empty file even though the previous run's
    /// activity was still sitting in event_log_live.txt on disk. Reloads that file back into
    /// Entries once at process start. The live file only stores the already-formatted display
    /// string (see ToDisplayString/WriteLiveFile), not the structured EventKey/Side, so restored
    /// entries carry EventKey="" and Side="n/a" -- fine, since both the UI feed and the exporter
    /// only ever render ToDisplayString for the text the user actually reads. Best-effort: a
    /// missing/corrupt file just means starting with an empty log, same as before this existed.</summary>
    static EventActivityLog()
    {
        try
        {
            string path = Path.Combine(ConfigStore.UserDataRoot, "event_log_live.txt");
            if (!File.Exists(path)) return;
            var restored = new List<Entry>();
            foreach (var line in File.ReadAllLines(path))
            {
                int sep = line.IndexOf(" -- ", StringComparison.Ordinal);
                if (sep < 0) continue;
                var ts = DateTime.TryParse(line[..sep], out var parsed) ? parsed : DateTime.Now;
                restored.Add(new Entry(ts, "", "n/a", line[(sep + 4)..]));
            }
            if (restored.Count > MaxEntries)
                restored = restored.GetRange(restored.Count - MaxEntries, MaxEntries);
            Entries.AddRange(restored);
        }
        catch { /* best-effort -- worst case we start with an empty log, same as before */ }
    }

    /// <summary>Appends one entry, dropping the oldest once the buffer is full. Side should be
    /// "home", "away", or "n/a" for side-agnostic ("Other:*") events. Message must already be
    /// plain English -- no internal jargon -- since it's shown to the user verbatim.</summary>
    public static void Record(string eventKey, string side, string message)
    {
        List<Entry> snapshot;
        lock (Lock)
        {
            Entries.Add(new Entry(DateTime.Now, eventKey ?? "", side ?? "n/a", message ?? ""));
            if (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
            snapshot = new List<Entry>(Entries);
        }
        WriteLiveFile(snapshot);
    }

    /// <summary>Owner request 2026-08-11 ("event log needs to update in real time -- it's not
    /// now"): ExportEventActivityLogFromWeb only ever wrote a fresh timestamped snapshot on
    /// manual button click -- opened once in an external editor, it never changed again, which
    /// reads as "frozen" during a live game even though the in-app Event Log tab (which polls
    /// GetEventActivityLog every 2s) was updating fine the whole time. This gives the "read it in
    /// a text editor" workflow (see the "whatsthedeal" review flow) the same liveness: one FIXED
    /// filename, rewritten in full on every single Record() call (cheap -- MaxEntries caps this
    /// at 200 short lines), so an editor with auto-reload-on-change (VS Code, etc.) shows new
    /// events appear within a second of them firing, no re-export/re-open needed. Best-effort --
    /// a write failure here must never take down the audio pipeline that called Record().</summary>
    static void WriteLiveFile(List<Entry> snapshot)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.UserDataRoot);
            string path = Path.Combine(ConfigStore.UserDataRoot, "event_log_live.txt");
            File.WriteAllLines(path, snapshot.Select(e => e.ToDisplayString()));
        }
        catch { /* best-effort -- logging must never crash the caller */ }
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

    /// <summary>FIXED 2026-08-12 (owner report -- log showed bare "Third Down (Home)" for a 3rd &amp;
    /// long, while the assign screen's card for that exact same EventKey reads "3rd & Long", so
    /// hunting for "the card that matches what the log just said" led nowhere): this used to just
    /// strip the "Offense:"/"Defense:" prefix and print whatever was left ("Offense: Third Down" ->
    /// "Third Down"), a completely separate, unsynced label source from wwwroot/app.js's
    /// EVENT_FRIENDLY_NAMES map that actually names the assignable cards. Mirrors that map's down/
    /// distance entries here so the log and the UI always agree on what to call the same EventKey --
    /// keep both in sync any time either one changes. Anything not in this table (scoring, penalties,
    /// kickoffs, etc) still falls back to the old prefix-strip behavior, unchanged.</summary>
    static readonly Dictionary<string, string> FriendlyNameOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Offense: Second Down"] = "2nd & Long",
        ["Offense: Second Down Short"] = "2nd & Short",
        ["Offense: Third Down"] = "3rd & Long",
        ["Offense: Third Down Short"] = "3rd & Short",
        ["Defense: Second Down"] = "2nd & Long",
        ["Defense: Second Down Short"] = "2nd & Short",
        ["Defense: Third Down"] = "3rd & Long",
        ["Defense: Third Down Short"] = "3rd & Short",
        ["Defense: Fourth Down"] = "4th Down",
        ["Offense: Fourth Down"] = "4th Down",
        ["Defense: Fourth Down (Loss)"] = "4th Down After a Loss",
        // FIXED 2026-08-12 (owner report: log/UI labels showing the bare words "Offense"/
        // "Defense"/"Other" read as confusing on their own) -- the generic colon-strip fallback
        // below turns "Penalty: Offense" into just "Offense" with nothing else, since there's no
        // second word after the prefix to strip down to. Every other EventKey's fallback already
        // leaves a real phrase behind (e.g. "Other: Opening Kickoff" -> "Opening Kickoff"); these
        // two are the only ones that don't, so they need an explicit override like the down/
        // distance keys above.
        ["Penalty: Offense"] = "Penalty - Your Team",
        ["Penalty: Defense"] = "Penalty - Opponent",
    };

    /// <summary>Turns an internal EventKey like "Offense: Touchdown Scored" or
    /// "Other: Opening Kickoff" into something that reads naturally in a log line -- strips the
    /// "Offense:"/"Defense:"/"Other:"/"Penalty:" routing prefix, which is meaningful to the code
    /// but not to a user reading "why didn't my song play". Down/distance keys use
    /// FriendlyNameOverrides instead, see its own comment for why.</summary>
    public static string FriendlyEventName(string eventKey)
    {
        if (string.IsNullOrEmpty(eventKey)) return "That event";
        if (FriendlyNameOverrides.TryGetValue(eventKey, out var overridden)) return overridden;
        int colon = eventKey.IndexOf(':');
        return colon >= 0 && colon < eventKey.Length - 1 ? eventKey[(colon + 1)..].Trim() : eventKey;
    }
}
