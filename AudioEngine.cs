using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SupremeStadiumSoundSelector;

/// <summary>Keeps assigned song files in RAM so playback never has to hit the disk on the
/// hot path (game-event -> sound). Populated once per profile/GAMETIME press via Preload();
/// AudioPlayer.Play() checks this first and only falls back to a normal disk read for a
/// clip that hasn't been cached yet (e.g. a brand-new assignment made mid-game).</summary>
internal static class AudioCache
{
    static readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Preload(IEnumerable<string?> paths)
    {
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_cache.ContainsKey(path!)) continue;
            try
            {
                if (File.Exists(path)) _cache[path!] = File.ReadAllBytes(path!);
            }
            catch (Exception ex)
            {
                CrashLog.Write($"AudioCache preload failed for {path}", ex);
            }
        }
    }

    /// <summary>Called whenever the user re-assigns/re-trims a song so the cache doesn't
    /// keep serving stale bytes for that path.</summary>
    public static void Invalidate(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) _cache.TryRemove(path, out _);
    }

    public static bool TryGet(string path, out byte[] bytes) => _cache.TryGetValue(path, out bytes!);

    public static int Count => _cache.Count;
}

/// <summary>Opens a playable, volume-controllable, position-aware audio source for a given
/// path -- from the RAM cache when available, straight from disk otherwise. Wraps both
/// paths behind the same small surface (Volume get/set, CurrentTime, WaveFormat, Dispose)
/// so AudioPlayer.Play()'s existing fade-out loop doesn't need to know which one it got.</summary>
internal sealed class CachedAudioSource : IDisposable
{
    readonly IDisposable _underlying; // AudioFileReader (disk) or a WaveStream over the cached bytes
    readonly VolumeSampleProvider? _volumeProvider; // only used for the cached (in-RAM) path
    readonly AudioFileReader? _fileReader; // only used for the disk-fallback path
    readonly WaveStream? _cachedStream; // only used for the cached path (for CurrentTime)
    public ISampleProvider Sample { get; }
    public WaveFormat WaveFormat => Sample.WaveFormat;

    public float Volume
    {
        get => _fileReader?.Volume ?? _volumeProvider!.Volume;
        set { if (_fileReader != null) _fileReader.Volume = value; else _volumeProvider!.Volume = value; }
    }

    public TimeSpan CurrentTime => _fileReader?.CurrentTime ?? _cachedStream!.CurrentTime;

    /// <summary>Opens from the RAM cache when possible. .wav and .mp3 (what this app actually
    /// writes/imports) get a real in-memory WaveStream; any other format falls back to a
    /// normal disk read via AudioFileReader -- a cache miss is never a hard failure, it's
    /// exactly the same code path Play() always used before caching existed.</summary>
    public static CachedAudioSource Open(string path)
    {
        if (AudioCache.TryGet(path, out var bytes))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            WaveStream? cachedStream = ext switch
            {
                ".wav" => new WaveFileReader(new MemoryStream(bytes, writable: false)),
                ".mp3" => new Mp3FileReader(new MemoryStream(bytes, writable: false)),
                _ => null,
            };
            if (cachedStream != null) return new CachedAudioSource(cachedStream);
        }

        var fileReader = new AudioFileReader(path);
        return new CachedAudioSource(fileReader);
    }

    CachedAudioSource(AudioFileReader fileReader)
    {
        _underlying = fileReader;
        _fileReader = fileReader;
        Sample = fileReader;
    }

    CachedAudioSource(WaveStream cachedStream)
    {
        _underlying = cachedStream;
        _cachedStream = cachedStream;
        var vp = new VolumeSampleProvider(cachedStream.ToSampleProvider());
        _volumeProvider = vp;
        Sample = vp;
    }

    public void Dispose() => _underlying.Dispose();
}

// ---------------------------------------------------------------------------------------
// EQ: a small RBJ-cookbook biquad + two ready-made presets (Marching Band cleanup and the
// "Megaphone / Stadium PA" bandpass character). Both are plain ISampleProvider wrappers so
// they slot into the existing chain and cost nothing when not selected.
// ---------------------------------------------------------------------------------------

internal enum EqPreset { Off, MarchingBand, Megaphone }

/// <summary>Standard RBJ "Audio EQ Cookbook" biquad filter, one instance per channel.</summary>
internal sealed class BiQuadFilter
{
    float _b0, _b1, _b2, _a1, _a2;
    float _x1, _x2, _y1, _y2;

    public static BiQuadFilter HighPass(int sampleRate, float freq, float q) => Design(sampleRate, freq, q, 0, Kind.HighPass);
    public static BiQuadFilter LowShelf(int sampleRate, float freq, float q, float gainDb) => Design(sampleRate, freq, q, gainDb, Kind.LowShelf);
    public static BiQuadFilter HighShelf(int sampleRate, float freq, float q, float gainDb) => Design(sampleRate, freq, q, gainDb, Kind.HighShelf);
    public static BiQuadFilter Peaking(int sampleRate, float freq, float q, float gainDb) => Design(sampleRate, freq, q, gainDb, Kind.Peaking);
    public static BiQuadFilter LowPass(int sampleRate, float freq, float q) => Design(sampleRate, freq, q, 0, Kind.LowPass);

    enum Kind { HighPass, LowPass, LowShelf, HighShelf, Peaking }

    static BiQuadFilter Design(int sampleRate, float freq, float q, float gainDb, Kind kind)
    {
        double a = Math.Pow(10, gainDb / 40.0);
        double w0 = 2 * Math.PI * freq / sampleRate;
        double cosW0 = Math.Cos(w0), sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2 * q);
        double b0, b1, b2, a0, a1, a2;

        switch (kind)
        {
            case Kind.HighPass:
                b0 = (1 + cosW0) / 2; b1 = -(1 + cosW0); b2 = (1 + cosW0) / 2;
                a0 = 1 + alpha; a1 = -2 * cosW0; a2 = 1 - alpha;
                break;
            case Kind.LowPass:
                b0 = (1 - cosW0) / 2; b1 = 1 - cosW0; b2 = (1 - cosW0) / 2;
                a0 = 1 + alpha; a1 = -2 * cosW0; a2 = 1 - alpha;
                break;
            case Kind.LowShelf:
            {
                double sqrtA = Math.Sqrt(a);
                b0 = a * ((a + 1) - (a - 1) * cosW0 + 2 * sqrtA * alpha);
                b1 = 2 * a * ((a - 1) - (a + 1) * cosW0);
                b2 = a * ((a + 1) - (a - 1) * cosW0 - 2 * sqrtA * alpha);
                a0 = (a + 1) + (a - 1) * cosW0 + 2 * sqrtA * alpha;
                a1 = -2 * ((a - 1) + (a + 1) * cosW0);
                a2 = (a + 1) + (a - 1) * cosW0 - 2 * sqrtA * alpha;
                break;
            }
            case Kind.HighShelf:
            {
                double sqrtA = Math.Sqrt(a);
                b0 = a * ((a + 1) + (a - 1) * cosW0 + 2 * sqrtA * alpha);
                b1 = -2 * a * ((a - 1) + (a + 1) * cosW0);
                b2 = a * ((a + 1) + (a - 1) * cosW0 - 2 * sqrtA * alpha);
                a0 = (a + 1) - (a - 1) * cosW0 + 2 * sqrtA * alpha;
                a1 = 2 * ((a - 1) - (a + 1) * cosW0);
                a2 = (a + 1) - (a - 1) * cosW0 - 2 * sqrtA * alpha;
                break;
            }
            default: // Peaking
                b0 = 1 + alpha * a; b1 = -2 * cosW0; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cosW0; a2 = 1 - alpha / a;
                break;
        }

        return new BiQuadFilter
        {
            _b0 = (float)(b0 / a0), _b1 = (float)(b1 / a0), _b2 = (float)(b2 / a0),
            _a1 = (float)(a1 / a0), _a2 = (float)(a2 / a0),
        };
    }

    public float Process(float x)
    {
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        return y;
    }
}

/// <summary>Marching-band cleanup EQ: HPF rumble cut, low-shelf mud cut, presence peak,
/// high-shelf air. Separate filter chain per channel (stereo assumed, matches the rest of
/// the pipeline which always upmixes mono -> stereo before this stage).</summary>
internal sealed class ParametricEqProvider : ISampleProvider
{
    readonly ISampleProvider _source;
    readonly BiQuadFilter[] _chainL, _chainR;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public ParametricEqProvider(ISampleProvider source)
    {
        _source = source;
        int sr = source.WaveFormat.SampleRate;
        _chainL = BuildChain(sr);
        _chainR = BuildChain(sr);
    }

    static BiQuadFilter[] BuildChain(int sr) => new[]
    {
        BiQuadFilter.HighPass(sr, 80f, 0.71f),
        BiQuadFilter.LowShelf(sr, 200f, 1.0f, -3f),
        BiQuadFilter.Peaking(sr, 2500f, 1.4f, 4f),
        BiQuadFilter.HighShelf(sr, 8000f, 0.71f, 2f),
    };

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        for (int i = 0; i < read; i += 2)
        {
            buffer[offset + i] = Apply(_chainL, buffer[offset + i]);
            if (i + 1 < read) buffer[offset + i + 1] = Apply(_chainR, buffer[offset + i + 1]);
        }
        return read;
    }

    static float Apply(BiQuadFilter[] chain, float x)
    {
        foreach (var f in chain) x = f.Process(x);
        return x;
    }
}

/// <summary>"Old stadium PA speaker" bandpass character (roughly 500Hz-4kHz), for the
/// Megaphone preset.</summary>
internal sealed class MegaphoneEqProvider : ISampleProvider
{
    readonly ISampleProvider _source;
    readonly BiQuadFilter _hpL, _hpR, _lpL, _lpR;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public MegaphoneEqProvider(ISampleProvider source)
    {
        _source = source;
        int sr = source.WaveFormat.SampleRate;
        _hpL = BiQuadFilter.HighPass(sr, 500f, 0.8f);
        _hpR = BiQuadFilter.HighPass(sr, 500f, 0.8f);
        _lpL = BiQuadFilter.LowPass(sr, 4000f, 0.8f);
        _lpR = BiQuadFilter.LowPass(sr, 4000f, 0.8f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        for (int i = 0; i < read; i += 2)
        {
            buffer[offset + i] = _lpL.Process(_hpL.Process(buffer[offset + i]));
            if (i + 1 < read) buffer[offset + i + 1] = _lpR.Process(_hpR.Process(buffer[offset + i + 1]));
        }
        return read;
    }
}

/// <summary>Dual envelope-follower transient shaper: a fast follower detects the attack
/// (first few ms of a hit) and boosts it, a slow follower tracks sustain and can trim it.
/// Musically useful mainly on percussion-heavy marching band tracks.</summary>
internal sealed class TransientShaperProvider : ISampleProvider
{
    readonly ISampleProvider _source;
    readonly float _attackGainDb, _sustainGainDb;
    float _fastEnvL, _slowEnvL, _fastEnvR, _slowEnvR;
    readonly float _fastCoeff, _slowCoeff;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public TransientShaperProvider(ISampleProvider source, float attackGainDb = 4f, float sustainGainDb = -1f)
    {
        _source = source;
        _attackGainDb = attackGainDb;
        _sustainGainDb = sustainGainDb;
        int sr = source.WaveFormat.SampleRate;
        _fastCoeff = (float)Math.Exp(-1.0 / (sr * 0.003)); // ~3ms follower
        _slowCoeff = (float)Math.Exp(-1.0 / (sr * 0.080)); // ~80ms follower
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        float attackLin = (float)Math.Pow(10, _attackGainDb / 20.0);
        float sustainLin = (float)Math.Pow(10, _sustainGainDb / 20.0);

        for (int i = 0; i < read; i += 2)
        {
            buffer[offset + i] = Process(buffer[offset + i], ref _fastEnvL, ref _slowEnvL, attackLin, sustainLin);
            if (i + 1 < read) buffer[offset + i + 1] = Process(buffer[offset + i + 1], ref _fastEnvR, ref _slowEnvR, attackLin, sustainLin);
        }
        return read;
    }

    float Process(float x, ref float fastEnv, ref float slowEnv, float attackLin, float sustainLin)
    {
        float ax = Math.Abs(x);
        fastEnv = ax > fastEnv ? ax : _fastCoeff * fastEnv + (1 - _fastCoeff) * ax;
        slowEnv = ax > slowEnv ? ax : _slowCoeff * slowEnv + (1 - _slowCoeff) * ax;

        // transient "now" = how much louder the fast envelope is than the slow one
        float transientAmount = Math.Clamp(fastEnv - slowEnv, 0f, 1f);
        float gain = 1f + transientAmount * (attackLin - 1f);
        gain *= 1f + (1f - transientAmount) * (sustainLin - 1f);
        return Math.Clamp(x * gain, -1f, 1f);
    }
}

/// <summary>Mid/Side stereo widener. Boosts the Side (difference) channel and recombines --
/// mono-safe by construction since Mid is untouched and summing L+R cancels the added Side
/// energy back out.</summary>
internal sealed class StereoWidenerProvider : ISampleProvider
{
    readonly ISampleProvider _source;
    readonly float _widthBoost; // 0 = no change, up to ~1 = +6dB-ish on Side
    public WaveFormat WaveFormat => _source.WaveFormat;

    public StereoWidenerProvider(ISampleProvider source, float widthBoost = 0.5f)
    {
        if (source.WaveFormat.Channels != 2) throw new ArgumentException("StereoWidenerProvider requires a stereo source.");
        _source = source;
        _widthBoost = widthBoost;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        float sideGain = 1f + _widthBoost;
        for (int i = 0; i + 1 < read; i += 2)
        {
            float l = buffer[offset + i], r = buffer[offset + i + 1];
            float mid = (l + r) * 0.5f;
            float side = (l - r) * 0.5f * sideGain;
            buffer[offset + i] = Math.Clamp(mid + side, -1f, 1f);
            buffer[offset + i + 1] = Math.Clamp(mid - side, -1f, 1f);
        }
        return read;
    }
}

/// <summary>Lookahead brickwall limiter -- the last stage every clip passes through so
/// overlapping triggers (touchdown + PA + lead-in all at once) can never sum past the
/// ceiling and distort. 5ms lookahead delay line, fast attack, ~50ms release, optional
/// soft-clip (tanh) mode instead of a hard ceiling.</summary>
internal sealed class LimiterProvider : ISampleProvider
{
    readonly ISampleProvider _source;
    readonly float _ceiling;
    readonly bool _softClip;
    readonly int _lookaheadSamples;
    readonly float[] _delayBuf;
    int _writePos;
    float _envelope = 1f;
    readonly float _releaseCoeff;

    // Monotonic-deque sliding-window maximum (indices + values of a strictly-decreasing run of
    // candidates) instead of rescanning the whole lookahead window on every sample. The old
    // version did a full linear scan of ~lookaheadSamples entries PER OUTPUT SAMPLE (~440 for
    // 5ms @44.1kHz stereo) -- for a single ~100ms NAudio buffer callback (~8800 samples) that's
    // ~3.9M comparisons, on the always-on real-time playback path (every clip gets a
    // LimiterProvider by default), with several clips (main + PA + lead-in) potentially playing
    // concurrently. Each sample below does O(1) amortized work: at most one push and each index
    // is popped at most once over the life of the stream.
    readonly int[] _dequeIdx;
    readonly float[] _dequeVal;
    int _dequeHead, _dequeCount;
    int _sampleCounter;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public LimiterProvider(ISampleProvider source, float ceilingDb = -0.3f, float lookaheadMs = 5f, float releaseMs = 50f, bool softClip = false)
    {
        _source = source;
        _ceiling = (float)Math.Pow(10, ceilingDb / 20.0);
        _softClip = softClip;
        int sr = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        _lookaheadSamples = Math.Max(1, (int)(sr * lookaheadMs / 1000.0)) * channels;
        _delayBuf = new float[_lookaheadSamples];
        _dequeIdx = new int[_lookaheadSamples];
        _dequeVal = new float[_lookaheadSamples];
        _releaseCoeff = (float)Math.Exp(-1.0 / (sr * releaseMs / 1000.0));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        for (int i = 0; i < read; i++)
        {
            float input = buffer[offset + i];
            float delayed = _delayBuf[_writePos];
            _delayBuf[_writePos] = input;
            _writePos = (_writePos + 1) % _lookaheadSamples;

            float absInput = Math.Abs(input);
            int idx = _sampleCounter++;

            // Evict from the back anything <= this sample -- they can never be the max again
            // while this newer, at-least-as-loud sample is still in the window.
            while (_dequeCount > 0 && _dequeVal[(_dequeHead + _dequeCount - 1) % _lookaheadSamples] <= absInput)
                _dequeCount--;
            _dequeIdx[(_dequeHead + _dequeCount) % _lookaheadSamples] = idx;
            _dequeVal[(_dequeHead + _dequeCount) % _lookaheadSamples] = absInput;
            _dequeCount++;

            // Evict from the front anything that's fallen out of the lookahead window.
            while (_dequeCount > 0 && _dequeIdx[_dequeHead] <= idx - _lookaheadSamples)
            {
                _dequeHead = (_dequeHead + 1) % _lookaheadSamples;
                _dequeCount--;
            }

            float peak = _dequeCount > 0 ? _dequeVal[_dequeHead] : 0f;

            float targetGain = peak > _ceiling ? _ceiling / peak : 1f;
            _envelope = targetGain < _envelope
                ? targetGain // instant attack -- never let a peak through
                : _releaseCoeff * _envelope + (1 - _releaseCoeff) * targetGain;

            float outSample = delayed * _envelope;
            buffer[offset + i] = _softClip ? (float)Math.Tanh(outSample) : Math.Clamp(outSample, -_ceiling, _ceiling);
        }
        return read;
    }
}

/// <summary>Offline (never real-time) loudness analysis, approximating ITU-R BS.1770
/// integrated LUFS via K-weighting (a high-shelf + a high-pass biquad stage, per the spec)
/// and 400ms gated block energy, plus a simple oversampled true-peak estimate. Runs in a
/// background Task at import time; never called from the playback path.</summary>
internal static class LoudnessAnalyzer
{
    public sealed record Result(double IntegratedLufs, double TruePeakDb);

    public static Result Analyze(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader.WaveFormat.Channels == 1 ? new MonoToStereoSampleProvider(reader) : reader;
        int sr = source.WaveFormat.SampleRate;

        // K-weighting: high-shelf ~+4dB @ 1500Hz then high-pass @ ~38Hz, per BS.1770.
        var shelfL = BiQuadFilter.HighShelf(sr, 1500f, 0.7f, 4f);
        var shelfR = BiQuadFilter.HighShelf(sr, 1500f, 0.7f, 4f);
        var hpL = BiQuadFilter.HighPass(sr, 38f, 0.5f);
        var hpR = BiQuadFilter.HighPass(sr, 38f, 0.5f);

        int blockSize = sr * 400 / 1000 * 2; // 400ms, interleaved stereo
        var buffer = new float[blockSize];
        var blockLoudnesses = new List<double>();
        double truePeak = 0;

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            double sumSq = 0;
            for (int i = 0; i < read; i += 2)
            {
                float l = shelfL.Process(buffer[i]); l = hpL.Process(l);
                float r = i + 1 < read ? shelfR.Process(buffer[i + 1]) : l;
                if (i + 1 < read) r = hpR.Process(r);
                sumSq += l * l + r * r;
                truePeak = Math.Max(truePeak, Math.Max(Math.Abs(buffer[i]), i + 1 < read ? Math.Abs(buffer[i + 1]) : 0));
            }
            int samplePairs = read / 2;
            if (samplePairs == 0) continue;
            double meanSq = sumSq / samplePairs;
            double blockLoudness = -0.691 + 10 * Math.Log10(Math.Max(meanSq, 1e-12));
            if (blockLoudness > -70.0) blockLoudnesses.Add(blockLoudness); // absolute gate
        }

        double integrated = blockLoudnesses.Count > 0
            ? -0.691 + 10 * Math.Log10(blockLoudnesses.Select(b => Math.Pow(10, (b + 0.691) / 10)).DefaultIfEmpty(0).Average())
            : -70.0;

        double truePeakDb = 20 * Math.Log10(Math.Max(truePeak, 1e-6));
        return new Result(integrated, truePeakDb);
    }

    /// <summary>Gain (linear multiplier) to reach targetLufs, clamped so the result never
    /// exceeds ceilingDb true peak after the gain is applied.</summary>
    public static float ComputeGain(Result measured, double targetLufs, double ceilingDb)
    {
        double gainDb = targetLufs - measured.IntegratedLufs;
        double resultingPeakDb = measured.TruePeakDb + gainDb;
        if (resultingPeakDb > ceilingDb) gainDb -= resultingPeakDb - ceilingDb;
        return (float)Math.Pow(10, gainDb / 20.0);
    }
}

/// <summary>What kind of clip is being normalized -- each gets its own LUFS target per the
/// Sound Booth spec (Phase 1 item #3): band songs punchy, PA speech-clear, whistle cuts
/// through.</summary>
internal enum LoudnessKind { Song, Pa, LeadInWhistle }

/// <summary>Offline import-time loudness normalization (Sound Booth item #3). Never touches
/// the user's original file -- analyzes it once, writes a gained COPY next to it, and caches
/// the measurement in a small JSON sidecar keyed by the source file's last-write time so a
/// file that hasn't changed is never re-analyzed. Always runs off the UI/render thread.</summary>
internal static class LoudnessNormalizationService
{
    const double CeilingDb = -1.0; // true-peak ceiling, never clip even after normalization gain
    static string NormalizedFolder => Path.Combine(ConfigStore.SongsFolder, "..", "SongsNormalized");

    sealed record Sidecar(long SourceWriteTimeTicks, double IntegratedLufs, double TruePeakDb, double AppliedGainDb, string OutputFileName);

    static double TargetLufs(LoudnessKind kind) => kind switch
    {
        LoudnessKind.Pa => -18.0,
        LoudnessKind.LeadInWhistle => -12.0,
        _ => -14.0,
    };

    /// <summary>Analyzes (or reuses a cached analysis of) sourcePath and returns the path to a
    /// loudness-matched copy. Returns sourcePath unchanged on any failure -- normalization is
    /// a nice-to-have, never a reason playback should break.</summary>
    public static string NormalizeToTarget(string sourcePath, LoudnessKind kind)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return sourcePath;

            Directory.CreateDirectory(NormalizedFolder);
            string sidecarPath = SidecarPath(sourcePath);
            long sourceTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks;

            if (File.Exists(sidecarPath))
            {
                var cached = System.Text.Json.JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(sidecarPath));
                if (cached != null && cached.SourceWriteTimeTicks == sourceTicks)
                {
                    string cachedOut = Path.Combine(NormalizedFolder, cached.OutputFileName);
                    if (File.Exists(cachedOut)) return cachedOut;
                }
            }

            var measured = LoudnessAnalyzer.Analyze(sourcePath);
            float gain = LoudnessAnalyzer.ComputeGain(measured, TargetLufs(kind), CeilingDb);
            double gainDb = 20 * Math.Log10(Math.Max(gain, 1e-6));

            string outName = Path.GetFileNameWithoutExtension(sourcePath) + $"_norm_{kind}.wav";
            string outPath = Path.Combine(NormalizedFolder, outName);

            using (var reader = new AudioFileReader(sourcePath) { Volume = gain })
            {
                NAudio.Wave.WaveFileWriter.CreateWaveFile16(outPath, reader);
            }

            File.WriteAllText(sidecarPath, System.Text.Json.JsonSerializer.Serialize(
                new Sidecar(sourceTicks, measured.IntegratedLufs, measured.TruePeakDb, gainDb, outName)));

            return outPath;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Loudness normalization failed for {sourcePath}", ex);
            return sourcePath;
        }
    }

    static string SidecarPath(string sourcePath) =>
        Path.Combine(NormalizedFolder, Path.GetFileNameWithoutExtension(sourcePath) + ".loudness.json");
}

/// <summary>Sound Booth item #12 -- Off/Subtle/Stadium/Earthquake sub-bass "thump" for big
/// hits (currently wired to Tackle for Loss only -- this codebase has no Field Goal Block
/// detection to hook the other spec'd trigger to). Built from a wavefolder (generates lower
/// harmonic content without the harsh edge of hard digital clipping) feeding a ~60Hz
/// lowpass, blended UNDER the original signal rather than replacing it. Off by default --
/// per an explicit owner note, this one should only ship default-on once it's been listened
/// to and confirmed smooth; until then it's an opt-in toggle, not a surprise.</summary>
internal enum SubBassIntensity { Off, Subtle, Stadium, Earthquake }

internal sealed class SubBassEnhancerProvider : ISampleProvider
{
    readonly ISampleProvider _source;
    readonly BiQuadFilter _lpL, _lpR;
    readonly float _mix, _foldThreshold;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public SubBassEnhancerProvider(ISampleProvider source, SubBassIntensity intensity)
    {
        _source = source;
        int sr = source.WaveFormat.SampleRate;
        _lpL = BiQuadFilter.LowPass(sr, 60f, 0.7f);
        _lpR = BiQuadFilter.LowPass(sr, 60f, 0.7f);
        (_mix, _foldThreshold) = intensity switch
        {
            SubBassIntensity.Subtle => (0.15f, 0.6f),
            SubBassIntensity.Stadium => (0.30f, 0.45f),
            SubBassIntensity.Earthquake => (0.45f, 0.3f),
            _ => (0f, 1f),
        };
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (_mix <= 0f) return read;
        for (int i = 0; i < read; i += 2)
        {
            buffer[offset + i] = Blend(buffer[offset + i], _lpL);
            if (i + 1 < read) buffer[offset + i + 1] = Blend(buffer[offset + i + 1], _lpR);
        }
        return read;
    }

    float Blend(float x, BiQuadFilter lp)
    {
        float sub = lp.Process(Fold(x));
        return Math.Clamp(x + sub * _mix, -1f, 1f);
    }

    float Fold(float x)
    {
        float t = _foldThreshold;
        while (x > t || x < -t)
        {
            if (x > t) x = 2 * t - x;
            else if (x < -t) x = -2 * t - x;
        }
        return x;
    }
}

/// <summary>Sound Booth item #14: the "bursting out of the tunnel into the stadium" sound
/// for the pregame walkout (fires on the "Other: Pregame Ready" event). Bandpass (300Hz-
/// 4kHz, same idea as the Megaphone EQ but tuned wider/boomier) + a long, tight reverb tail +
/// a touch of soft-clip saturation for that enclosed-concrete-tunnel character. No crossfade
/// to a separate "open stadium" sound -- per an explicit owner note (no fade-ins anywhere in
/// this app, matching the existing fade-OUT-only design), this treatment is just applied to
/// the whole pregame clip; there's no second track it fades into.</summary>
internal sealed class TunnelFilterProvider : ISampleProvider
{
    readonly ISampleProvider _reverbStage;
    readonly BiQuadFilter _hpL, _hpR, _lpL, _lpR;
    const float SaturationDrive = 1.6f;
    public WaveFormat WaveFormat => _reverbStage.WaveFormat;

    public TunnelFilterProvider(ISampleProvider stereoSource)
    {
        int sr = stereoSource.WaveFormat.SampleRate;
        _hpL = BiQuadFilter.HighPass(sr, 300f, 0.8f);
        _hpR = BiQuadFilter.HighPass(sr, 300f, 0.8f);
        _lpL = BiQuadFilter.LowPass(sr, 4000f, 0.8f);
        _lpR = BiQuadFilter.LowPass(sr, 4000f, 0.8f);
        // Tunnel reverb character: long tight tail, minimal damping (hard concrete surfaces).
        _reverbStage = new ReverbProvider(new BandpassPassthrough(this, stereoSource), roomSize: 0.80f, damp: 0.20f, wet: 0.35f, width: 0.80f);
    }

    // ReverbProvider needs a real ISampleProvider to wrap; this tiny adapter runs the
    // bandpass+saturation stage first, then hands the result to ReverbProvider's Read().
    sealed class BandpassPassthrough : ISampleProvider
    {
        readonly TunnelFilterProvider _owner;
        readonly ISampleProvider _source;
        public BandpassPassthrough(TunnelFilterProvider owner, ISampleProvider source) { _owner = owner; _source = source; }
        public WaveFormat WaveFormat => _source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = 0; i < read; i += 2)
            {
                buffer[offset + i] = Saturate(_owner._lpL.Process(_owner._hpL.Process(buffer[offset + i])));
                if (i + 1 < read) buffer[offset + i + 1] = Saturate(_owner._lpR.Process(_owner._hpR.Process(buffer[offset + i + 1])));
            }
            return read;
        }
        static float Saturate(float x) => (float)Math.Tanh(x * SaturationDrive) / SaturationDrive;
    }

    public int Read(float[] buffer, int offset, int count) => _reverbStage.Read(buffer, offset, count);
}

/// <summary>Reads the current Windows system (default output device) volume/mute state --
/// read-only, never adjusts it. Sound Booth item #3: Bandroom's own LUFS normalization sets
/// each clip's loudness RELATIVE to the others; this exists so the app can tell the
/// difference between "our audio is broken" and "the user has Windows muted/turned way
/// down," instead of the two systems silently fighting with no visibility into which one is
/// responsible for what the user actually hears.</summary>
internal static class SystemVolumeService
{
    public sealed record Info(float VolumeScalar, bool Muted);

    /// <summary>Null if the platform audio endpoint can't be queried (e.g. no default device
    /// present) -- callers should treat that as "unknown," not "silent."</summary>
    public static Info? GetCurrentOutputVolume()
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            return new Info(device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute);
        }
        catch (Exception ex)
        {
            CrashLog.Write("SystemVolumeService query failed", ex);
            return null;
        }
    }
}
