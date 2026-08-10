using System.Text.Json;
using NAudio.Wave;

namespace SupremeStadiumSoundSelector;

/// <summary>Optional descriptive metadata for one audio clip, stored as a `.meta.json` sidecar
/// file next to the audio file itself (e.g. "Neck.mp3" -> "Neck.mp3.meta.json") rather than
/// embedded in TriggerEntry or an ID3 tag -- keeps TriggerEntry lean (it only needs to know what
/// to play), survives file moves/renames without a lossy re-tag, and means a file with no
/// sidecar simply has no metadata yet instead of needing a schema migration. Same "small JSON
/// next to the thing it describes" shape as ConfigStore's TeamLogoEntry/manifests. Uploader
/// attribution, download counts, and ratings are NOT tracked here -- those already live
/// server-side in the marketplace worker's KV per pack, so mirroring them locally would just be a
/// second, driftable copy of numbers this app doesn't own.</summary>
public sealed record AudioTrackMetadata
{
    public string? StandardTitle { get; init; }
    public string? StandardArtist { get; init; }
    public string? SchoolAbbreviation { get; init; }

    /// <summary>e.g. "High", "Mid", "Low" -- user-set or IntakeEngine-suggested, not computed.</summary>
    public string? EnergyLevel { get; init; }
    /// <summary>Free text, e.g. "Heavy Brass, Marching Snare Drums".</summary>
    public string? ProminentInstrumentation { get; init; }
    /// <summary>Free text, e.g. "00:05-00:15 (10s)" -- a suggestion for TrimmerForm, not enforced.</summary>
    public string? RecommendedTrim { get; init; }

    /// <summary>Real ITU-R BS.1770/EBU-R128 K-weighted integrated loudness in LUFS, from the same
    /// LoudnessAnalyzer (AudioEngine.cs) that already powers LoudnessNormalizationService's
    /// assignment-time normalization -- NOT the RMS-based IntegratedLufsApprox field below.
    /// Null until AnalyzeAudioFile has run for this file at least once (see
    /// AudioTrackMetadataStore.AnalyzeAudioFile).</summary>
    public float? IntegratedLufs { get; init; }
    /// <summary>True peak in dBTP, from the same LoudnessAnalyzer pass as IntegratedLufs.</summary>
    public float? TruePeakDbtp { get; init; }

    /// <summary>RMS-based loudness estimate in dBFS -- kept only so old sidecars written before
    /// IntegratedLufs existed still deserialize and show a number instead of blank while they wait
    /// to be re-analyzed. New analyses populate IntegratedLufs (real K-weighted LUFS) instead;
    /// prefer that field wherever both are present.</summary>
    public float? IntegratedLufsApprox { get; init; }
    public float? DurationSeconds { get; init; }

    // -- Marketplace / GameWatcher routing (Audio Metadata Extension) --
    /// <summary>The GameWatcher EventKey this track is meant for, e.g. "Offense: Touchdown
    /// Scored" -- a suggestion for the marketplace browser and for auto-assigning a downloaded
    /// pack's tracks to the right trigger slots, distinct from TriggerEntry.Event (the actual
    /// live assignment on THIS install).</summary>
    public string? PrimaryGameTriggerEvent { get; init; }
    /// <summary>Marketplace browse/filter category, e.g. "Audio - Fight Song", "Audio - Hype
    /// Sting" -- freeform but expected to match the categories worker.js's /list?category= filter
    /// is queried with.</summary>
    public string? MarketplaceCategory { get; init; }
    /// <summary>Suggested ReverbProvider preset name (see ReverbProvider.cs's weather/venue
    /// presets) for this specific track, e.g. "Stadium", "Dome", "Night Game" -- a per-track
    /// override suggestion, not itself applied automatically.</summary>
    public string? RecommendedReverbPreset { get; init; }
    /// <summary>Short free-text description for marketplace search/discovery, e.g. "punchy brass
    /// hit with a snare roll build" -- user-written or IntakeEngine-suggested, never computed.</summary>
    public string? AcousticFingerprint { get; init; }

    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Load/save for the `.meta.json` sidecar described on AudioTrackMetadata, plus the
/// analysis step that fills in the auto-computed fields (duration, approximate loudness) from
/// the actual audio file.</summary>
public static class AudioTrackMetadataStore
{
    static string SidecarPathFor(string audioFilePath) => audioFilePath + ".meta.json";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Null if no sidecar exists yet for this file -- callers should treat that as
    /// "no metadata available" (see AudioTrackMetadata's class doc), not an error.</summary>
    public static AudioTrackMetadata? Load(string audioFilePath)
    {
        var path = SidecarPathFor(audioFilePath);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<AudioTrackMetadata>(File.ReadAllText(path), JsonOptions); }
        catch { return null; } // corrupt sidecar -- treat as absent rather than crash the track-info drawer
    }

    public static void Save(string audioFilePath, AudioTrackMetadata metadata)
    {
        File.WriteAllText(SidecarPathFor(audioFilePath), JsonSerializer.Serialize(metadata with { UpdatedAtUtc = DateTime.UtcNow }, JsonOptions));
    }

    /// <summary>Reads DurationSeconds and loudness straight off the audio file, using the same
    /// AudioFileReader NAudio entry point TrimmerForm already uses elsewhere in this app. Does not
    /// touch title/artist/school/trigger fields -- those come from IntakeEngine.AnalyzeAndSuggest,
    /// which calls this for the computed half.
    ///
    /// Loudness is now real ITU-R BS.1770/EBU-R128 K-weighted analysis via LoudnessAnalyzer
    /// (AudioEngine.cs, added for LoudnessNormalizationService) -- the old RMS-only approximation
    /// this method used to compute is kept as a fallback ONLY if LoudnessAnalyzer throws (e.g. an
    /// exotic codec it can't decode via the K-weighting filter path but AudioFileReader still
    /// opens), so a track that fails the more expensive analysis still gets SOME loudness number
    /// instead of none.</summary>
    public static (float DurationSeconds, float? IntegratedLufs, float? TruePeakDbtp, float IntegratedLufsApprox) AnalyzeAudioFile(string audioFilePath)
    {
        using var reader = new AudioFileReader(audioFilePath);
        float durationSeconds = (float)reader.TotalTime.TotalSeconds;

        var buf = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        double sumSquares = 0;
        long sampleCount = 0;
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < read; i++) sumSquares += (double)buf[i] * buf[i];
            sampleCount += read;
        }
        float rms = sampleCount > 0 ? (float)Math.Sqrt(sumSquares / sampleCount) : 0f;
        float lufsApprox = rms > 0.0001f ? (float)(20 * Math.Log10(rms)) : -96f; // -96dBFS floor for silence

        float? realLufs = null, truePeakDbtp = null;
        try
        {
            var measured = LoudnessAnalyzer.Analyze(audioFilePath);
            realLufs = (float)measured.IntegratedLufs;
            truePeakDbtp = (float)measured.TruePeakDb; // already in dB -- see LoudnessAnalyzer.Analyze
        }
        catch { /* fall back to the RMS approximation above -- see method doc */ }

        return (durationSeconds, realLufs, truePeakDbtp, lufsApprox);
    }
}
