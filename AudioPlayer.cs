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
    // Sound Booth overhaul: was 1.0s -- that full second between a game event and the sound
    // actually starting was the #1 complaint. RAM caching (AudioCache, below) is what makes
    // 0.0 safe: there's no more disk-read stall hiding under the old delay.
    public static double PreRollSeconds = 0.0;
    public static double FadeStartSeconds = 10.0;
    public static double FadeOutDuration = 4.5;

    // ---- Sound Booth toggles (all off by default; every one gets a plain-English (i)
    // description in the UI). Bypassable as a group via NoEffectsBypass. ----
    public static bool NoEffectsBypass = false;
    public static EqPreset CurrentEq = EqPreset.Off;
    public static bool TransientShaperEnabled = false;
    public static bool StereoWidenerEnabled = false;
    public static float StereoWidenerAmount = 0.5f;
    public static bool LimiterEnabled = true; // safety stage -- on by default, not really "an effect"
    public static bool DuckingEnabled
    {
        get => AudioDuckingController.Enabled;
        set => AudioDuckingController.Enabled = value;
    }
    // Off by default -- see SubBassEnhancerProvider's doc comment: ships opt-in until it's
    // been confirmed to actually sound smooth, not forced on for everyone.
    public static SubBassIntensity SubBassLevel = SubBassIntensity.Off;

    /// <summary>Owner request: a short cue (e.g. a band whistle) prepended immediately before
    /// every real triggered clip AND every manual preview, with zero gap -- like a drum major's
    /// whistle before the band hits, not a separate sound followed by a pause. Off by default
    /// until a clip is actually assigned via LeadInClipPath.</summary>
    public static bool LeadInEnabled = false;
    public static string? LeadInClipPath = null;

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

    /// <summary>Clears the cooldown for one clip so a manual re-trigger (test hook, "fire again"
    /// button) isn't silently swallowed by the same 20s dedup meant for live-game OCR flicker.
    /// Preview/real engine fires still go through the normal cooldown untouched.</summary>
    public static void ClearCooldown(string path)
    {
        lock (Lock) _lastFireByPath.Remove(path);
    }

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

        Task.Run(async () =>
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
                // Task.Delay instead of Thread.Sleep -- this callback already runs on a threadpool
                // thread via Task.Run, and Delay frees that thread back to the pool for the 30ms
                // wait instead of blocking it.
                await Task.Delay(30);
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
    /// <param name="isPreview">True for manual Preview-button playback (assign cards, trimmer).
    /// Skips PreRollSeconds (owner: preview clicks must start audio instantly, the pre-roll gap
    /// is only meaningful for real game triggers) and skips the FireCooldown gate (owner: without
    /// this, clicking Preview on the same clip twice within 20s silently played nothing, with no
    /// error -- looked exactly like a broken/unassigned event).</param>
    /// <param name="isHighPriorityEvent">True for Touchdown/Turnover/Safety-class triggers --
    /// when DuckingEnabled is on, every OTHER currently-playing clip is ducked to 40% for a
    /// couple seconds so this one (and any PA layer playing alongside it) cuts through clearly.</param>
    /// <param name="isBigHitEvent">True for Tackle-for-Loss-class triggers -- when SubBassLevel
    /// isn't Off, blends a low-end "thump" under this clip specifically.</param>
    /// <param name="isPregameEvent">True for the pregame walkout trigger ("Other: Pregame
    /// Ready") -- applies the Tunnel bandpass/reverb/saturation treatment instead of the
    /// normal reverb preset for this one clip.</param>
    public static void Play(string path, float? volumeOverride = null, bool interruptPrevious = false, bool isPreview = false, bool isHighPriorityEvent = false, bool isBigHitEvent = false, bool isPregameEvent = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        if (!isPreview)
        {
            lock (Lock)
            {
                if (_lastFireByPath.TryGetValue(path, out var last) && DateTime.UtcNow - last < FireCooldown)
                    return; // this exact clip already fired too recently -- different clips don't block each other
                _lastFireByPath[path] = DateTime.UtcNow;
            }
        }

        if (interruptPrevious) StopAll();
        if (isHighPriorityEvent) AudioDuckingController.OnHighPriorityEventFired();

        float volume = volumeOverride ?? MasterVolume;

        Task.Run(() =>
        {
            try
            {
                if (!isPreview) Thread.Sleep((int)(PreRollSeconds * 1000)); // brief delay so it doesn't feel like it's stepping on the trigger moment

                // RAM cache first (Sound Booth: item #2) -- falls back to a normal disk read
                // via AudioFileReader inside CachedAudioSource.Open if this path hasn't been
                // preloaded (e.g. a brand-new assignment made mid-game).
                using var audio = CachedAudioSource.Open(path);
                using var output = new WaveOutEvent();
                lock (Lock) ActiveOutputs.Add(output);
                audio.Volume = volume;
                AudioFileReader? leadInReader = null;

                try
                {
                    ISampleProvider source = audio.WaveFormat.Channels == 1
                        ? new MonoToStereoSampleProvider(audio.Sample)
                        : audio.Sample;

                    bool bypass = NoEffectsBypass; // "No Effects" button: skip every DSP stage, dry signal only

                    if (!bypass)
                    {
                        if (isPregameEvent)
                        {
                            // Tunnel treatment replaces the normal reverb preset for this one
                            // clip -- it already includes its own reverb stage, stacking the
                            // user's chosen preset on top would just be mud.
                            source = new TunnelFilterProvider(source);
                        }
                        else
                        {
                            var preset = CurrentReverb;
                            if (preset != ReverbPreset.Off)
                            {
                                var (roomSize, damp, wet, width) = ReverbPresets.Get(preset);
                                source = new ReverbProvider(source, roomSize, damp, wet, width);
                            }
                        }

                        source = CurrentEq switch
                        {
                            EqPreset.MarchingBand => new ParametricEqProvider(source),
                            EqPreset.Megaphone => new MegaphoneEqProvider(source),
                            _ => source,
                        };

                        if (TransientShaperEnabled) source = new TransientShaperProvider(source);
                        if (StereoWidenerEnabled) source = new StereoWidenerProvider(source, StereoWidenerAmount);
                        if (isBigHitEvent && SubBassLevel != SubBassIntensity.Off) source = new SubBassEnhancerProvider(source, SubBassLevel);
                    }

                    // Lead-in whistle: built from the main clip's post-mono/pre-reverb WaveFormat
                    // so SequencedSampleProvider can hand off from one to the other mid-stream on
                    // a single WaveOutEvent -- that's what makes it gapless (no Stop/re-Init
                    // between the two clips, which is where an audible click/pause would come from).
                    // Owner request: applies to preview playback too (isPreview only skips the
                    // pre-roll delay/cooldown gate, not the whistle -- previews should sound like
                    // the real in-game cue).
                    {
                        var leadIn = BuildLeadInProvider(source.WaveFormat, volume, out leadInReader);
                        if (leadIn != null) source = new SequencedSampleProvider(leadIn, source);
                    }

                    // Master safety stage -- last thing before output, protects against clipping
                    // when multiple clips overlap. Not affected by the "No Effects" bypass since
                    // it's a safety net, not a creative effect.
                    if (LimiterEnabled) source = new LimiterProvider(source);

                    output.Init(source.ToWaveProvider());
                    output.Play();

                    while (output.PlaybackState == PlaybackState.Playing)
                    {
                        double elapsed = audio.CurrentTime.TotalSeconds;
                        float duckMul = isHighPriorityEvent ? 1f : AudioDuckingController.GetGainMultiplier();
                        // Previews always track the live master volume slider instead of the
                        // value captured when Play() started -- `volume` above is a one-time
                        // snapshot (PreviewLocalFileFromWeb/PreviewEventFromWeb pass
                        // AudioPlayer.MasterVolume as volumeOverride at call time), so dragging
                        // the slider mid-preview had no audible effect until the NEXT preview
                        // started. Real game fires keep the snapshot -- an event's volume
                        // shouldn't drift mid-clip just because the slider moved.
                        float liveVolume = isPreview ? MasterVolume : volume;

                        if (elapsed >= FadeStartSeconds)
                        {
                            double fadeProgress = FadeOutDuration <= 0 ? 1.0 : (elapsed - FadeStartSeconds) / FadeOutDuration;
                            if (fadeProgress >= 1.0)
                            {
                                output.Stop();
                                break;
                            }
                            audio.Volume = liveVolume * (float)(1.0 - fadeProgress) * duckMul;
                            Thread.Sleep(30); // finer steps during the fade for a smooth ramp
                        }
                        else
                        {
                            audio.Volume = liveVolume * duckMul;
                            Thread.Sleep(15); // fast enough to track the ~20ms duck attack smoothly
                        }
                    }
                }
                finally
                {
                    lock (Lock) ActiveOutputs.Remove(output);
                    leadInReader?.Dispose();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("Playback error", ex);
            }
        });
    }

    /// <summary>Builds the lead-in whistle as an ISampleProvider resampled/channel-matched to
    /// `targetFormat` so it can feed the same WaveOutEvent as the main clip via
    /// SequencedSampleProvider (mismatched sample rate/channel count would otherwise either throw
    /// or play back garbled). Returns null (no whistle) if disabled, unset, missing, or unreadable
    /// -- a broken lead-in clip should never block the real event's own audio from playing.
    /// `reader` is returned separately so the caller can dispose it once playback finishes.
    /// `volume` is the same per-call volume (volumeOverride ?? MasterVolume) the main clip uses --
    /// previously hard-coded to the static MasterVolume field here, which meant muting a side
    /// (AwayVolume=0) or the PA layer (PaVolume=0) via volumeOverride still let the whistle play
    /// at full volume.</summary>
    static ISampleProvider? BuildLeadInProvider(WaveFormat targetFormat, float volume, out AudioFileReader? reader)
    {
        reader = null;
        if (!LeadInEnabled || string.IsNullOrWhiteSpace(LeadInClipPath) || !File.Exists(LeadInClipPath))
            return null;

        try
        {
            reader = new AudioFileReader(LeadInClipPath) { Volume = volume };
            ISampleProvider src = reader.WaveFormat.Channels == 1
                ? new MonoToStereoSampleProvider(reader)
                : reader;
            if (src.WaveFormat.SampleRate != targetFormat.SampleRate)
                src = new WdlResamplingSampleProvider(src, targetFormat.SampleRate);
            return src;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Lead-in whistle load failed", ex);
            reader?.Dispose();
            reader = null;
            return null;
        }
    }
}

/// <summary>Plays `first` to completion, then seamlessly continues with `second` on the same
/// underlying stream/WaveOutEvent -- no Stop/re-Init between them, which is what keeps the
/// hand-off gapless. Both providers must already share sample rate and channel count.</summary>
sealed class SequencedSampleProvider : ISampleProvider
{
    readonly ISampleProvider _first;
    readonly ISampleProvider _second;
    bool _onSecond;

    public SequencedSampleProvider(ISampleProvider first, ISampleProvider second)
    {
        if (first.WaveFormat.SampleRate != second.WaveFormat.SampleRate ||
            first.WaveFormat.Channels != second.WaveFormat.Channels)
            throw new ArgumentException("Sequenced providers must share sample rate and channel count.");
        _first = first;
        _second = second;
    }

    public WaveFormat WaveFormat => _second.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_onSecond)
        {
            int read = _first.Read(buffer, offset, count);
            if (read > 0) return read;
            _onSecond = true;
        }
        return _second.Read(buffer, offset, count);
    }
}
