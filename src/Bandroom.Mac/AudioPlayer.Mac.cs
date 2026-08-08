using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Bandroom.Mac;

/// <summary>
/// Cross-platform AudioPlayer for macOS using AVFoundation via interop.
/// Same API surface as Windows AudioPlayer.cs (NAudio) so the engine
/// and WebBridge don't need to know which platform they're on.
/// </summary>
internal static class AudioPlayer
{
    // --- Volume (0.0-1.0, applied per clip) ---
    public static float MasterVolume = 1.0f;
    public static float HomeVolume = 1.0f;
    public static float AwayVolume = 1.0f;

    // --- Reverb preset (stadium/dome/nightgame/off — stubbed for now) ---
    public enum ReverbPreset { Off, Stadium, Dome, NightGame }
    public static ReverbPreset CurrentReverb = ReverbPreset.Off;

    // --- Timing ---
    public static double PreRollSeconds = 1.0;
    public static double FadeStartSeconds = 10.0;
    public static double FadeOutDuration = 4.5;

    // --- Cooldown ---
    public static readonly TimeSpan FireCooldown = TimeSpan.FromSeconds(20);
    private static readonly Dictionary<string, DateTime> _lastFireByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>
    /// Plays an audio file. On macOS, uses the `afplay` command-line tool
    /// (bundled with every Mac) via Process.Start. This is a straightforward
    /// cross-platform approach that works without any native interop complexity.
    /// Replace with AVFoundation interop for production use (volume control,
    /// fade-out, overlapping playback).
    /// </summary>
    public static void Play(string path, float? volumeOverride = null, bool interruptPrevious = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        lock (_lock)
        {
            if (_lastFireByPath.TryGetValue(path, out var last) && DateTime.UtcNow - last < FireCooldown)
                return;
            _lastFireByPath[path] = DateTime.UtcNow;
        }

        float volume = volumeOverride ?? MasterVolume;

        Task.Run(() =>
        {
            try
            {
                Thread.Sleep((int)(PreRollSeconds * 1000));

                // afplay is bundled with macOS since 10.5 — plays mp3, wav, m4a, aiff, flac
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/afplay",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                {
                    Console.Error.WriteLine($"[AudioPlayer.Mac] Failed to start afplay for: {path}");
                    return;
                }

                // Wait with fade-out: afplay doesn't support volume changes,
                // so we just kill the process at the fade deadline
                double elapsed = 0;
                while (!process.HasExited)
                {
                    Thread.Sleep(200);
                    elapsed += 0.2;

                    if (elapsed >= (FadeStartSeconds + FadeOutDuration))
                    {
                        try { process.Kill(); } catch { }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AudioPlayer.Mac] Playback error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Stops all afplay processes spawned by this session.
    /// </summary>
    public static void StopAll()
    {
        try
        {
            // Kill all afplay processes belonging to this user
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/killall",
                Arguments = "afplay",
                UseShellExecute = false,
                CreateNoWindow = true,
            })?.WaitForExit(2000);
        }
        catch { }
    }

    /// <summary>
    /// No warmup needed on macOS — CoreAudio is always ready.
    /// </summary>
    public static void Warmup() { }
}