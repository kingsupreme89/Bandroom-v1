using NAudio.Wave;

namespace SupremeStadiumSoundSelector;

internal enum ReverbPreset { Off, Stadium, Dome, NightGame, StadiumRain, NightGamePrimeTime }

internal static class ReverbPresets
{
    // roomSize/damp/wet tuned per "space" -- bigger room = longer tail, less damping =
    // brighter/longer tail, more wet = more obviously "in the room" vs. dry/direct.
    public static (float roomSize, float damp, float wet, float width) Get(ReverbPreset preset) => preset switch
    {
        ReverbPreset.Stadium => (0.75f, 0.35f, 0.28f, 1.0f),         // big open-air tail, some absorption from the crowd
        ReverbPreset.Dome => (0.85f, 0.15f, 0.35f, 1.0f),            // enclosed, bright, long reflective tail
        ReverbPreset.NightGame => (0.55f, 0.55f, 0.20f, 0.8f),       // tighter, warmer, more damped -- cooler night air
        // Sound Booth weather variants (item #10):
        ReverbPreset.StadiumRain => (0.60f, 0.70f, 0.22f, 0.85f),    // rain absorbs highs -- muffled, close, "wet November game"
        ReverbPreset.NightGamePrimeTime => (0.65f, 0.40f, 0.24f, 1.0f), // big-game-under-the-lights: wider stereo image than NightGame
        _ => (0f, 0f, 0f, 0f),
    };
}

/// <summary>Classic "Freeverb" algorithmic reverb (Schroeder/Moorer topology: 8 parallel
/// comb filters + 4 series allpass filters per channel) -- public-domain algorithm,
/// re-implemented here as a NAudio ISampleProvider since NAudio has no built-in reverb.</summary>
internal sealed class ReverbProvider : ISampleProvider
{
    readonly ISampleProvider _source;
    readonly Comb[] _combsL, _combsR;
    readonly AllPass[] _allpassL, _allpassR;
    readonly float _wet, _dry;
    readonly float _widthL1, _widthL2, _widthR1, _widthR2;

    static readonly int[] CombTuningL = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    const int StereoSpread = 23;
    static readonly int[] AllpassTuningL = { 556, 441, 341, 225 };
    const float FixedGain = 0.015f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public ReverbProvider(ISampleProvider stereoSource, float roomSize, float damp, float wet, float width)
    {
        if (stereoSource.WaveFormat.Channels != 2)
            throw new ArgumentException("ReverbProvider requires a stereo source.");
        _source = stereoSource;

        int sr = stereoSource.WaveFormat.SampleRate;
        double scale = sr / 44100.0;

        float feedback = roomSize * 0.28f + 0.7f;
        float damp1 = damp * 0.4f;

        _combsL = CombTuningL.Select(t => new Comb((int)(t * scale), feedback, damp1)).ToArray();
        _combsR = CombTuningL.Select(t => new Comb((int)((t + StereoSpread) * scale), feedback, damp1)).ToArray();
        _allpassL = AllpassTuningL.Select(t => new AllPass((int)(t * scale), 0.5f)).ToArray();
        _allpassR = AllpassTuningL.Select(t => new AllPass((int)((t + StereoSpread) * scale), 0.5f)).ToArray();

        _wet = wet * 3f;
        _dry = 1f - wet; // keep overall level sane as wet increases
        float widthWet1 = 0.5f + width * 0.5f;
        float widthWet2 = 0.5f - width * 0.5f;
        _widthL1 = widthWet1; _widthL2 = widthWet2;
        _widthR1 = widthWet1; _widthR2 = widthWet2;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        for (int i = 0; i < samplesRead; i += 2)
        {
            float inL = buffer[offset + i];
            float inR = i + 1 < samplesRead ? buffer[offset + i + 1] : inL;
            float input = (inL + inR) * FixedGain;

            float outL = 0f, outR = 0f;
            foreach (var c in _combsL) outL += c.Process(input);
            foreach (var c in _combsR) outR += c.Process(input);
            foreach (var a in _allpassL) outL = a.Process(outL);
            foreach (var a in _allpassR) outR = a.Process(outR);

            float wetL = outL * _widthL1 + outR * _widthL2;
            float wetR = outR * _widthR1 + outL * _widthR2;

            buffer[offset + i] = inL * _dry + wetL * _wet;
            if (i + 1 < samplesRead) buffer[offset + i + 1] = inR * _dry + wetR * _wet;
        }

        return samplesRead;
    }

    sealed class Comb
    {
        readonly float[] _buffer;
        int _idx;
        float _filterStore;
        readonly float _feedback, _damp1, _damp2;

        public Comb(int size, float feedback, float damp)
        {
            _buffer = new float[Math.Max(size, 1)];
            _feedback = feedback;
            _damp1 = damp;
            _damp2 = 1f - damp;
        }

        public float Process(float input)
        {
            float output = _buffer[_idx];
            _filterStore = output * _damp2 + _filterStore * _damp1;
            _buffer[_idx] = input + _filterStore * _feedback;
            _idx = (_idx + 1) % _buffer.Length;
            return output;
        }
    }

    sealed class AllPass
    {
        readonly float[] _buffer;
        int _idx;
        readonly float _feedback;

        public AllPass(int size, float feedback)
        {
            _buffer = new float[Math.Max(size, 1)];
            _feedback = feedback;
        }

        public float Process(float input)
        {
            float bufOut = _buffer[_idx];
            float output = -input + bufOut;
            _buffer[_idx] = input + bufOut * _feedback;
            _idx = (_idx + 1) % _buffer.Length;
            return output;
        }
    }
}
