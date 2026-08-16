using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SupremeStadiumSoundSelector;

namespace Bandroom.Mac;

/// <summary>
/// Real audio trimming for the Mac port, via a system-installed `ffmpeg` (e.g. `brew install
/// ffmpeg`) instead of NAudio's OffsetSampleProvider/AudioNormalizer (Windows-only — NAudio has
/// no macOS decode backend for compressed formats like MP3). Every SaveTrim*FromWeb call site on
/// Windows used to just return "not supported" here (see the "don't fake it" precedent in
/// PrepareTrimForWhistleFromWeb's doc comment) — this makes it real when ffmpeg is present, and
/// keeps the same honest failure message (now with an actionable fix) when it isn't, rather than
/// silently degrading to an untrimmed copy.
/// </summary>
internal static class AudioTrimService
{
    static bool? _available;

    /// <summary>Checked once per process, not per call — ffmpeg's presence doesn't change while
    /// Bandroom is running.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_available.HasValue) return _available.Value;
            try
            {
                using var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/env",
                    ArgumentList = { "ffmpeg", "-version" },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                probe?.WaitForExit(3000);
                _available = probe?.ExitCode == 0;
            }
            catch
            {
                _available = false;
            }
            return _available.Value;
        }
    }

    public const string UnavailableMessage =
        "Trimming needs ffmpeg installed (e.g. via Homebrew: brew install ffmpeg) -- choose a different clip instead.";

    /// <summary>Cuts [startSec, endSec) out of sourcePath into a new 16-bit PCM WAV at outPath,
    /// with loudness normalization (-18 LUFS integrated / -1dBTP true-peak ceiling) so trimmed
    /// clips land at a consistent perceived loudness with no clipping — same practical goal as
    /// Windows' AudioNormalizer.NormalizeAndLimit (RMS-based there; ffmpeg's `loudnorm` here),
    /// not a bit-identical port of that DSP.</summary>
    public static bool TrimAndNormalize(string sourcePath, double startSec, double endSec, string outPath)
    {
        if (!IsAvailable) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            double duration = endSec - startSec;

            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/env",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("ffmpeg");
            psi.ArgumentList.Add("-y"); // overwrite outPath without prompting
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(startSec.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(duration.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-af");
            psi.ArgumentList.Add("loudnorm=I=-18:TP=-1:LRA=11");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add("44100");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("pcm_s16le"); // 16-bit PCM WAV, matching WaveFileWriter.CreateWaveFile16 on Windows
            psi.ArgumentList.Add(outPath);

            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(30000);
            return process.ExitCode == 0 && File.Exists(outPath) && new FileInfo(outPath).Length > 0;
        }
        catch (Exception ex)
        {
            CrashLog.Write("AudioTrimService.TrimAndNormalize failed", ex);
            return false;
        }
    }
}
