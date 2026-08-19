using System.Diagnostics;

namespace SupremeStadiumSoundSelector;

/// <summary>Launches and monitors BANDroom's own bundled copy of Coffee's `CollegeFB27RamReader.exe`
/// (2026-08-13 owner-approved absorption -- see ScoreboardReaderPaths' RAM section) as a hidden
/// child process, kills it on BANDroom exit. RAM mode itself stays OFF by default behind the
/// existing opt-in toggle (ConfigStore.LoadScoreboardReaderRamModeEnabled) -- bundling/owning the
/// exe is a distribution change only, NOT a reversal of the anti-cheat safety decision; RAM
/// reading still carries the same account risk to the end user regardless of who ships the exe.
///
/// Invocation contract (confirmed directly from Coffee's own `main.js`, ~lines 1014-1033):
/// `spawn(exePath, ['--service', seedPath, statusPath, String(ownPid)], { windowsHide: true,
/// stdio: 'ignore' })`. No stdin/stdout piping -- the exe communicates purely via JSON files on
/// disk. It self-attaches to the running CollegeFB27(.exe) process, retrying for up to ~30s cold
/// start.
///
/// Auto-launch is best-effort only: if the bundled exe is somehow missing from the build (should
/// never happen -- see BandAudioHook.csproj) or the process fails to start, this fails soft --
/// logs and returns false -- and BANDroom proceeds straight into GAMETIME on its existing OCR
/// path exactly as it already works today. GAMETIME must never block on this.</summary>
internal sealed class ScoreboardReaderHost
{
    Process? _process;

    public event Action<string>? Log;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Directory the reader's `--service` files live in -- ScoreboardReaderPaths.
    /// ResolveRamReaderDataDirectory(). GameWatcher polls `<this>\live-game-data.json` for the
    /// actual scoreboard payload (see that path helper's own doc comment for why that specific
    /// sibling filename, not the statusPath itself, is what carries the scoreboard).</summary>
    public string DataDirectory { get; } = ScoreboardReaderPaths.ResolveRamReaderDataDirectory();

    /// <summary>Starts the bundled RAM reader if not already running. Returns false on any
    /// failure to launch -- never throws, so callers can always fall through to OCR.</summary>
    public bool TryStartRamReader()
    {
        if (IsRunning) return true;

        string? exePath = ScoreboardReaderPaths.ResolveBundledRamReaderExecutablePath();
        if (exePath == null)
        {
            Log?.Invoke("[ScoreboardReaderHost] bundled RAM reader exe not found -- staying on OCR only");
            return false;
        }

        // Self-heal orphans (2026-08-13): if a previous BANDroom process was killed rather than
        // closed cleanly (a force-quit, a debugger stop, a crash), its RAM reader child can
        // outlive it -- the reader's own parent-pid watchdog is supposed to self-exit but isn't
        // reliably prompt about it, so a stale instance can sit there for a long time writing
        // "parent scorebug app closed" into ram-reader-status.json instead of live data, which
        // looks identical to "RAM mode isn't working" from GameWatcher's side. Only one reader
        // should ever exist at a time (we're always the one who launches it), so it's always
        // safe to clear out anything with this exact name before starting a fresh one.
        try
        {
            string exeName = Path.GetFileNameWithoutExtension(exePath);
            foreach (var stray in Process.GetProcessesByName(exeName))
            {
                try { stray.Kill(entireProcessTree: true); }
                catch (Exception ex) { Log?.Invoke($"[ScoreboardReaderHost] failed to clear stray reader (pid {stray.Id}): {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log?.Invoke($"[ScoreboardReaderHost] stray-reader scan failed: {ex.Message}"); }

        try
        {
            Directory.CreateDirectory(DataDirectory);
            // Coffee's own main.js never pre-populates this file's content either when running
            // with no screen data available (it just passes the path and lets the exe handle a
            // missing/empty seed) -- see ScoreboardReaderPaths' RAM section for the source trail.
            // Mirrored here rather than guessing at seed content the exe was never confirmed to
            // require.
            string seedPath = Path.Combine(DataDirectory, "ram-reader-no-screen-seed.json");
            string statusPath = Path.Combine(DataDirectory, "ram-reader-status.json");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add("--service");
            psi.ArgumentList.Add(seedPath);
            psi.ArgumentList.Add(statusPath);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());

            _process = Process.Start(psi);
            if (_process == null)
            {
                Log?.Invoke("[ScoreboardReaderHost] Process.Start returned null");
                return false;
            }
            Log?.Invoke($"[ScoreboardReaderHost] launched bundled RAM reader (pid {_process.Id})");
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[ScoreboardReaderHost] launch failed: {ex.Message}");
            _process = null;
            return false;
        }
    }

    /// <summary>Owner request 2026-08-19: kill and relaunch the reader -- called by GameWatcher's
    /// own watchdog (RestartRamReader) after it's seen RamReaderStatus stuck at anything other
    /// than Connected for too long mid-session. Stop() first even though TryStartRamReader
    /// already self-heals orphaned strays by name -- a process that's still technically alive but
    /// wedged (hung, stopped writing its status file, whatever got it into this state) won't
    /// necessarily show up as "exited" for IsRunning to catch, so this forces the kill unconditionally
    /// rather than relying on TryStartRamReader's own no-op-if-already-running check.</summary>
    public bool RestartRamReader()
    {
        Stop();
        return TryStartRamReader();
    }

    /// <summary>Called from FormClosing alongside _watcher.Stop() -- the reader is BANDroom's own
    /// spawned child, not something the user should have to close by hand.</summary>
    public void Stop()
    {
        if (_process is not { HasExited: false } proc) return;
        try { proc.Kill(entireProcessTree: true); }
        catch (Exception ex) { Log?.Invoke($"[ScoreboardReaderHost] stop failed: {ex.Message}"); }
        finally { _process = null; }
    }
}
