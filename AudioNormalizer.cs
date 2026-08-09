using NAudio.Wave;

namespace SupremeStadiumSoundSelector;

/// <summary>RMS-normalize-and-soft-limit DSP, extracted out of TrimmerForm so the embedded
/// web trimmer (WebMainForm.SaveTrimFromWeb) can reuse the exact same processing instead of
/// duplicating it -- every trimmed clip should sound the same regardless of which UI made it.</summary>
internal static class AudioNormalizer
{
    // -18 dBFS RMS is a common "feels consistently loud without pinning the meter" target for
    // short game-day clips -- explicit user ask was "songs all have the same volume", which RMS
    // (perceived loudness) fixes far better than peak normalization would (two clips can share
    // the same peak level and still sound wildly different in loudness).
    const float TargetRmsDb = -18f;
    // -1 dBFS true-peak ceiling leaves headroom so the limiter below only ever has to catch rare
    // transients, not do heavy lifting every clip.
    const float CeilingDb = -1f;

    static float DbToLinear(float db) => (float)Math.Pow(10, db / 20.0);

    /// <summary>Reads every sample from <paramref name="source"/>, applies a single RMS-based
    /// makeup-gain pass so all clips land at the same perceived loudness, then soft-limits
    /// (tanh knee, not a hard clamp) anything that would exceed the ceiling so a boosted
    /// transient never produces audible digital clipping.</summary>
    public static ISampleProvider NormalizeAndLimit(ISampleProvider source)
    {
        var samples = new List<float>();
        var buf = new float[source.WaveFormat.SampleRate * source.WaveFormat.Channels];
        int read;
        while ((read = source.Read(buf, 0, buf.Length)) > 0)
            samples.AddRange(new ArraySegment<float>(buf, 0, read));

        if (samples.Count == 0)
            return new FloatArraySampleProvider(Array.Empty<float>(), source.WaveFormat);

        double sumSquares = 0;
        foreach (var s in samples) sumSquares += (double)s * s;
        float rms = (float)Math.Sqrt(sumSquares / samples.Count);

        float targetRms = DbToLinear(TargetRmsDb);
        float ceiling = DbToLinear(CeilingDb);
        // Silence/near-silence: skip gain (would otherwise blow up toward infinity) and just
        // pass the (already near-zero) samples through the limiter stage untouched.
        float gain = rms > 0.0001f ? targetRms / rms : 1f;

        var outArr = new float[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            float v = samples[i] * gain;
            float mag = Math.Abs(v);
            if (mag > ceiling)
            {
                float over = (mag - ceiling) / (1f - ceiling);
                mag = ceiling + (1f - ceiling) * (float)Math.Tanh(over);
                v = Math.Sign(v) * mag;
            }
            outArr[i] = v;
        }
        return new FloatArraySampleProvider(outArr, source.WaveFormat);
    }

    /// <summary>Minimal ISampleProvider wrapper over an in-memory float buffer, used to feed the
    /// normalized/limited samples back into WaveFileWriter.</summary>
    sealed class FloatArraySampleProvider : ISampleProvider
    {
        readonly float[] _data;
        int _position;
        public WaveFormat WaveFormat { get; }

        public FloatArraySampleProvider(float[] data, WaveFormat format)
        {
            _data = data;
            WaveFormat = format;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = _data.Length - _position;
            int n = Math.Min(available, count);
            if (n <= 0) return 0;
            Array.Copy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
