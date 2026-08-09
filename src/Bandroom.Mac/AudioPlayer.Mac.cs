using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bandroom.Mac;

/// <summary>
/// Cross-platform AudioPlayer for macOS using afplay (bundled with macOS since 10.5).
/// Identical API surface to Windows AudioPlayer.cs (NAudio). Uses afplay -v for volume
/// control, Process tracking for stop/interrupt, and hard-stop at fade deadline.
///
/// Limitations vs Windows (documented, not hidden):
///   - Smooth fade-out: afplay has no real-time volume ramp. Hard stop at FadeStartSeconds
///     + FadeOutDuration. Acceptable for game cues (typically short, not crossfaded).
///   - Lead-in whistle: sequential play (not gapless). Small gap between whistle and main
///     clip since they're separate afplay processes.
///   - Multi-clip overlap: afplay processes run concurrently via separate Process instances.
///     Active tracking via ConcurrentDictionary for StopAll/interrupt management.
/// </summary>
internal static class AudioPlayer
{
    // --- Volume (0.0-1.0, applied per clip) ---
    public static float MasterVolume = 1.0f;
    public static float HomeVolume = 1.0f;
    public static float AwayVolume = 1.0f;
    public static float PaVolume = 1.0f;

    public enum ReverbPreset { Off, Stadium, Dome, NightGame }
    public static ReverbPreset CurrentReverb = ReverbPreset.Off;

    public static bool LeadInEnabled = false;
    public static string? LeadInClipPath = null;

    public static double PreRollSeconds = 1.0;
    public static double FadeStartSeconds = 10.0;
    public static double FadeOutDuration = 4.5;

    public static readonly TimeSpan FireCooldown = TimeSpan.FromSeconds(20);
    private static readonly Dictionary<string, DateTime> _lastFireByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>Tracks all active afplay processes so StopAll can kill them.</summary>
    private static readonly ConcurrentDictionary<int, ActivePlayback> _activePlayers = new();

    private sealed class ActivePlayback
    {
        public int ProcessId;
        public string FilePath;
        public float BaseVolume;
        public CancellationTokenSource? FadeCts;
    }

    // =========================================================================
    // Public API (same surface as Windows AudioPlayer.cs)
    // =========================================================================

    public static void ClearCooldown(string path)
    {
        lock (_lock) _lastFireByPath.Remove(path);
    }

    public static void Warmup()
    {
        // No warmup needed — macOS CoreAudio is always ready and afplay
        // has no equivalent of NAudio's WaveOutEvent cold-start cost.
    }

    /// <summary>Kills every active afplay process tracked by this session.</summary>
    public static void StopAll()
    {
        foreach (var kvp in _activePlayers)
        {
            var pb = kvp.Value;
            try
            {
                pb.FadeCts?.Cancel();
                var proc = Process.GetProcessById(pb.ProcessId);
                if (!proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(1000);
                }
            }
            catch
            {
                // Process already exited or PID reused — safe to ignore
            }
        }
        _activePlayers.Clear();
    }

    /// <summary>
    /// Plays an audio file using macOS's built-in afplay command.
    /// </summary>
    /// <param name="volumeOverride">If set, used instead of MasterVolume.</param>
    /// <param name="interruptPrevious">True stops all current playback before starting new clip.</param>
    /// <param name="isPreview">True skips PreRollSeconds delay and FireCooldown gate.</param>
    public static void Play(string path, float? volumeOverride = null, bool interruptPrevious = false, bool isPreview = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        if (!isPreview)
        {
            lock (_lock)
            {
                if (_lastFireByPath.TryGetValue(path, out var last) &&
                    DateTime.UtcNow - last < FireCooldown)
                    return;
                _lastFireByPath[path] = DateTime.UtcNow;
            }
        }

        if (interruptPrevious) StopAll();

        float volume = Math.Clamp(volumeOverride ?? MasterVolume, 0f, 1f);

        Task.Run(() =>
        {
            CancellationTokenSource? fadeCts = null;

            try
            {
                if (!isPreview)
                    Thread.Sleep((int)(PreRollSeconds * 1000));

                // --- Lead-in whistle (sequential, not gapless on macOS) ---
                if (LeadInEnabled && !string.IsNullOrWhiteSpace(LeadInClipPath) &&
                    File.Exists(LeadInClipPath) && interruptPrevious)
                {
                    PlayProcess(LeadInClipPath, volume, wait: true, maxWaitMs: 5000);
                }

                // --- Main clip ---
                fadeCts = new CancellationTokenSource();
                var token = fadeCts.Token;

                // Start the afplay process
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/afplay",
                    Arguments = $"-v {volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    Console.Error.WriteLine($"[AudioPlayer.Mac] Failed to start afplay for: {path}");
                    return;
                }

                var playback = new ActivePlayback
                {
                    ProcessId = process.Id,
                    FilePath = path,
                    BaseVolume = volume,
                    FadeCts = fadeCts,
                };
                _activePlayers[process.Id] = playback;

                // Monitor with fade-out deadline
                double elapsed = 0;
                while (!process.HasExited && !token.IsCancellationRequested)
                {
                    Thread.Sleep(200);
                    elapsed += 0.2;

                    if (elapsed >= (FadeStartSeconds + FadeOutDuration))
                    {
                        // Hard stop — afplay doesn't support real-time fade
                        try
                        {
                            if (!process.HasExited) process.Kill();
                        }
                        catch { }
                        break;
                    }
                }

                // Cleanup: wait for process if it exited naturally
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AudioPlayer.Mac] Playback error: {ex.Message}");
            }
            finally
            {
                fadeCts?.Cancel();
                fadeCts?.Dispose();
            }
        });
    }

    /// <summary>
    /// Plays a single audio file synchronously (blocks until done or maxWaitMs).
    /// Used for lead-in whistle so the main clip starts after the whistle finishes.
    /// </summary>
    private static void PlayProcess(string path, float volume, bool wait, int maxWaitMs = 30000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/afplay",
                Arguments = $"-v {volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            if (wait)
            {
                // Wait for the clip to finish, with a hard cap
                bool exited = process.WaitForExit(maxWaitMs);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AudioPlayer.Mac] PlayProcess error: {ex.Message}");
        }
    }
}