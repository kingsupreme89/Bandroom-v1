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

    /// <summary>RMS-based loudness estimate in dBFS, reusing the same RMS pass as
    /// AudioNormalizer.NormalizeAndLimit (see AudioMetadataAnalyzer.Analyze) -- an approximation
    /// of EBU R128 integrated loudness, not a true K-weighted LUFS meter. Good enough for
    /// "does this clip sound roughly as loud as the others", not broadcast-spec compliance.</summary>
    public float? IntegratedLufsApprox { get; init; }
    public float? DurationSeconds { get; init; }

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

    /// <summary>Reads DurationSeconds and an RMS-based loudness approximation straight off the
    /// audio file (see AudioTrackMetadata.IntegratedLufsApprox doc for why this isn't true LUFS),
    /// using the same AudioFileReader NAudio entry point TrimmerForm already uses elsewhere in
    /// this app. Does not touch title/artist/school/trigger fields -- those come from
    /// IntakeEngine.AnalyzeAndSuggest, which calls this for the computed half.</summary>
    public static (float DurationSeconds, float IntegratedLufsApprox) AnalyzeAudioFile(string audioFilePath)
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

        return (durationSeconds, lufsApprox);
    }
}
