using System.Text.Json;

namespace Bandroom.Core;

public enum ScoreboardReaderStatus
{
    /// <summary>No `live-scoreboard.json` has ever been found at the resolved path -- reader
    /// isn't installed/running, or the configured install path is wrong.</summary>
    NotFound,
    /// <summary>File exists and is reachable, but every parsed read so far has been
    /// meta.visible == false (reader running, no scorebug detected on screen yet).</summary>
    WaitingForGameData,
    /// <summary>Latest read produced a usable, visible snapshot.</summary>
    Connected,
    /// <summary>File exists but couldn't be parsed even after retries + the transient-read grace
    /// window -- distinct from NotFound so the UI can tell "reader is broken" from "reader isn't
    /// there."</summary>
    Error,
}

/// <summary>Atomic-safe reader for Coffee's `live-scoreboard.json`. Mirrors the reader's own
/// `transient-json-reader.js`: the file is written `.tmp` then `fs.renameSync`'d into place, so a
/// poll can still occasionally land mid-swap on Windows (rename isn't guaranteed atomic across
/// all filesystems/AV scanners) -- retry a few times, and if every attempt fails, serve the last
/// known-good parse for up to a 750ms grace window rather than immediately reporting an error for
/// a one-tick race. Pure file I/O + JSON, no polling loop of its own -- callers (GameWatcher /
/// ScoreboardReaderHost) decide their own cadence (~150ms recommended per the integration plan).</summary>
public sealed class ScoreboardJsonReader
{
    const int DefaultReadAttempts = 3;
    static readonly TimeSpan GraceWindow = TimeSpan.FromMilliseconds(750);

    readonly int _readAttempts;
    (ScoreboardReaderState State, DateTime ObservedAtUtc)? _cache;

    public ScoreboardJsonReader(int readAttempts = DefaultReadAttempts)
    {
        _readAttempts = Math.Max(1, readAttempts);
    }

    /// <summary>Reads and parses <paramref name="filePath"/>, returning the resolved status
    /// alongside whatever state is usable for that status (null for NotFound/Error).</summary>
    public (ScoreboardReaderStatus Status, ScoreboardReaderState? State) Read(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return (ScoreboardReaderStatus.NotFound, null);

        var observedAt = DateTime.UtcNow;
        for (int attempt = 0; attempt < _readAttempts; attempt++)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var state = JsonSerializer.Deserialize<ScoreboardReaderState>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (state == null) continue;
                _cache = (state, observedAt);
                return (state.Meta?.Visible == false ? ScoreboardReaderStatus.WaitingForGameData : ScoreboardReaderStatus.Connected, state);
            }
            catch (IOException) { /* mid-rename race -- retry */ }
            catch (JsonException) { /* mid-write partial content -- retry */ }
        }

        // Every attempt failed -- fall back to the last good parse if it's still fresh enough to
        // trust (same grace idea as TransientJsonReader.read on the reader's own side).
        if (_cache is { } cached && observedAt - cached.ObservedAtUtc <= GraceWindow)
            return (cached.State.Meta?.Visible == false ? ScoreboardReaderStatus.WaitingForGameData : ScoreboardReaderStatus.Connected, cached.State);

        return (ScoreboardReaderStatus.Error, null);
    }

    public void ClearCache() => _cache = null;
}
