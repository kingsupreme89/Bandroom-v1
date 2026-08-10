using Bandroom.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SupremeStadiumSoundSelector;

/// <summary>Loops the wrapped WaveStream back to the start instead of ending -- the standard
/// NAudio pattern for gapless ambient-bed looping (no built-in NAudio type does this).</summary>
sealed class LoopStream : WaveStream
{
    readonly WaveStream _source;
    public LoopStream(WaveStream source) { _source = source; }
    public override WaveFormat WaveFormat => _source.WaveFormat;
    public override long Length => _source.Length;
    public override long Position { get => _source.Position; set => _source.Position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _source.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                if (_source.Position == 0) break; // empty/corrupt source -- don't spin forever
                _source.Position = 0;
                continue;
            }
            totalRead += read;
        }
        return totalRead;
    }
}

/// <summary>Sound Booth item #11: a separate, persistent ambient crowd-noise bed whose
/// volume reacts to the live game state (score margin, quarter, time remaining) instead of
/// being triggered per-event like every other cue in this app. Architecturally different
/// from AudioPlayer.Play()'s per-clip model on purpose -- this needs ONE long-lived
/// WaveOutEvent that keeps looping and just has its gain nudged every tick, not a fresh
/// player per fire.
///
/// Needs an actual crowd-ambience audio file assigned (ClipPath) to do anything -- there's
/// no bundled crowd-loop asset in this repo, so the feature is fully wired but inert (silent,
/// Enabled has no effect) until the user assigns one via the Sound Booth, same as the
/// lead-in whistle pattern (off until a clip exists).</summary>
internal static class CrowdBusService
{
    public static bool Enabled = false;
    public static string? ClipPath;

    const float BaseLevel = 0.15f;
    const float CloseGameBoost = 0.25f;
    const float MidMarginBoost = 0.10f;
    const float LateQuarterBoost = 0.15f;
    const float UrgentBoost = 0.25f;

    static WaveOutEvent? _output;
    static VolumeSampleProvider? _volumeProvider;
    static AudioFileReader? _reader;
    static bool _loopStarted;
    static readonly object _lock = new();

    public static void Start(GameWatcher watcher)
    {
        lock (_lock)
        {
            if (_loopStarted) return;
            _loopStarted = true;
        }
        Task.Run(async () =>
        {
            while (true)
            {
                try { UpdatePlaybackState(watcher); }
                catch (Exception ex) { CrashLog.Write("CrowdBusService tick failed", ex); }
                await Task.Delay(500);
            }
        });
    }

    /// <summary>Called by StopAll/when watching stops so the ambient bed doesn't keep
    /// looping into a game that's no longer being watched.</summary>
    public static void Stop()
    {
        lock (_lock) StopInternal();
    }

    static void UpdatePlaybackState(GameWatcher watcher)
    {
        lock (_lock)
        {
            bool shouldPlay = Enabled && !string.IsNullOrWhiteSpace(ClipPath) && File.Exists(ClipPath);
            if (!shouldPlay)
            {
                StopInternal();
                return;
            }

            if (_output == null)
            {
                _reader = new AudioFileReader(ClipPath!);
                var loop = new LoopStream(_reader);
                ISampleProvider src = loop.WaveFormat.Channels == 1
                    ? new MonoToStereoSampleProvider(loop.ToSampleProvider())
                    : loop.ToSampleProvider();
                _volumeProvider = new VolumeSampleProvider(src) { Volume = 0f };
                _output = new WaveOutEvent();
                _output.Init(_volumeProvider);
                _output.Play();
            }

            _volumeProvider!.Volume = ComputeIntensity(watcher.LastSnapshot) * AudioPlayer.MasterVolume;
        }
    }

    static float ComputeIntensity(PlaySnapshot snap)
    {
        int margin = Math.Abs(snap.HomeScore - snap.AwayScore);
        float marginBoost = margin <= 7 ? CloseGameBoost : margin <= 14 ? MidMarginBoost : 0f;
        float quarterBoost = snap.Quarter >= 4 ? LateQuarterBoost : 0f;
        float urgentBoost = snap.Quarter >= 4 && snap.TimeRemainingSeconds <= 120 ? UrgentBoost : 0f;
        return Math.Clamp(BaseLevel + marginBoost + quarterBoost + urgentBoost, 0f, 1f);
    }

    static void StopInternal()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _volumeProvider = null;
    }
}
