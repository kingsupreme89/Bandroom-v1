using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SupremeStadiumSoundSelector;

internal static class AudioPlayer
{
    /// <summary>0.0-1.0 master volume, applied to every triggered clip. A future volume
    /// slider in the UI just needs to set this.</summary>
    public static float MasterVolume = 1.0f;

    /// <summary>Independent volumes for matchup-mode side-aware events (home team's touchdown,
    /// away team's turnover, etc.) -- lets one side be turned down/muted without affecting the
    /// other, since both sides' cues can legitimately fire close together in a real game.</summary>
    public static float HomeVolume = 1.0f;
    public static float AwayVolume = 1.0f;

    /// <summary>Independent volume for the PA Announcer layer (TriggerEntry.PaAudioFile) --
    /// separate from MasterVolume/HomeVolume/AwayVolume so a user can turn the announcer up/down
    /// or mute it without affecting the main song cues, since PA clips play concurrently with
    /// (not instead of) the main AudioFile for the same event.</summary>
    public static float PaVolume = 1.0f;

    /// <summary>Which "room" preset (if any) triggered clips are played through. Off = dry,
    /// no processing overhead.</summary>
    public static ReverbPreset CurrentReverb = ReverbPreset.Off;

    // All exposed in the UI's Settings panel now, so no longer const.
    public static double PreRollSeconds = 1.0;
    public static double FadeStartSeconds = 10.0;
    public static double FadeOutDuration = 4.5;

    static readonly object Lock = new();
    static readonly List<WaveOutEvent> ActiveOutputs = new();

    /// <summary>Cooldown between repeat fires of the SAME clip -- covers the real bug this was
    /// added for: GameWatcher's OCR re-detects the same on-screen state (e.g. "First Down")
    /// after a replay overlay clears and the live feed reappears, firing the same cue twice in
    /// a few seconds. 20s blocks that without needing to fix OCR debouncing separately.
    /// Scoped per audio file path (not global) so a home-team cue and an away-team cue firing
    /// seconds apart in a real matchup don't block each other -- they're different clips.</summary>
    public static readonly TimeSpan FireCooldown = TimeSpan.FromSeconds(20);
    static readonly Dictionary<string, DateTime> _lastFireByPath = new(StringComparer.OrdinalIgnoreCase);

    static bool _warmedUp;

    /// <summary>Opens a throwaway WaveOutEvent against a few ms of silence and immediately
    /// tears it down. Call once at app startup (see Program.cs), off the UI thread.
    ///
    /// Why: NAudio's WaveOutEvent.Init() does a real waveOutOpen() against the Windows audio
    /// driver/session, which has measurable one-time cost the FIRST time any process touches
    /// the audio subsystem after launch (COM/driver/session negotiation). Play() below still
    /// opens a fresh WaveOutEvent per clip (needed for overlapping cues -- see Play's doc
    /// comment), so that per-call open itself isn't removed, but paying the one-time
    /// subsystem cold-start cost here means the FIRST real tackle/trigger sound doesn't
    /// silently eat an extra chunk of latency on top of the fast path everything after it
    /// gets. Safe to call more than once; only the first call does anything.</summary>
    public static void Warmup()
    {
        if (_warmedUp) return;
        _warmedUp = true;

        Task.Run(() =>
        {
            try
            {
                // 20ms of silence, mono 44.1kHz -- just enough for the driver to actually open
                // and start, nothing audible, nothing worth generating more of.
                var format = new NAudio.Wave.WaveFormat(44100, 16, 1);
                var silence = new byte[(int)(format.AverageBytesPerSecond * 0.02)];
                using var provider = new NAudio.Wave.RawSourceWaveStream(new MemoryStream(silence), format);
                using var output = new WaveOutEvent();
                output.Init(provider);
                output.Play();
                Thread.Sleep(30);
                output.Stop();
            }
            catch (Exception ex)
            {
                // Never let a warmup failure be user-visible -- worst case, the first real
                // Play() just pays the cold-start cost it would have paid anyway.
                CrashLog.Write("AudioPlayer warmup failed", ex);
            }
        });
    }

    /// <summary>Immediately stops every clip currently playing (or waiting out its pre-roll
    /// delay). Used by the UI's "Stop All" button.</summary>
    public static void StopAll()
    {
        lock (Lock)
        {
            foreach (var o in ActiveOutputs)
            {
                try { o.Stop(); } catch { /* already stopping/disposed */ }
            }
        }
    }

    /// <param name="volumeOverride">If set, used instead of MasterVolume -- lets side-aware
    /// events (home/away) play at their own independently-configured volume.</param>
    /// <param name="interruptPrevious">True for real in-game trigger cues (Touchdown, PAT,
    /// Kickoff, etc, via WebMainForm.FireEvent): the newest event on the field is what's
    /// actually happening right now, so it should cut off whatever cue is still playing from
    /// a moment ago rather than overlap with it -- explicit user call: "second event always
    /// takes priority." Left false for one-off UI chimes (app open, GAMETIME, update) that have
    /// no reason to fight over the speaker.</param>
    public static void Play(string path, float? volumeOverride = null, bool interruptPrevious = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        lock (Lock)
        {
            if (_lastFireByPath.TryGetValue(path, out var last) && DateTime.UtcNow - last < FireCooldown)
                return; // this exact clip already fired too recently -- different clips don't block each other
            _lastFireByPath[path] = DateTime.UtcNow;
        }

        if (interruptPrevious) StopAll();

        float volume = volumeOverride ?? MasterVolume;

        Task.Run(() =>
        {
            try
            {
                Thread.Sleep((int)(PreRollSeconds * 1000)); // brief delay so it doesn't feel like it's stepping on the trigger moment

                using var reader = new AudioFileReader(path);
                using var output = new WaveOutEvent();
                lock (Lock) ActiveOutputs.Add(output);
                reader.Volume = volume;

                try
                {
                    ISampleProvider source = reader.WaveFormat.Channels == 1
                        ? new MonoToStereoSampleProvider(reader)
                        : reader;

                    var preset = CurrentReverb;
                    if (preset != ReverbPreset.Off)
                    {
                        var (roomSize, damp, wet, width) = ReverbPresets.Get(preset);
                        source = new ReverbProvider(source, roomSize, damp, wet, width);
                    }

                    output.Init(source.ToWaveProvider());
                    output.Play();

                    while (output.PlaybackState == PlaybackState.Playing)
                    {
                        double elapsed = reader.CurrentTime.TotalSeconds;

                        if (elapsed >= FadeStartSeconds)
                        {
                            double fadeProgress = FadeOutDuration <= 0 ? 1.0 : (elapsed - FadeStartSeconds) / FadeOutDuration;
                            if (fadeProgress >= 1.0)
                            {
                                output.Stop();
                                break;
                            }
                            reader.Volume = volume * (float)(1.0 - fadeProgress);
                            Thread.Sleep(30); // finer steps during the fade for a smooth ramp
                        }
                        else
                        {
                            reader.Volume = volume;
                            Thread.Sleep(200);
                        }
                    }
                }
                finally
                {
                    lock (Lock) ActiveOutputs.Remove(output);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("Playback error", ex);
            }
        });
    }
}
