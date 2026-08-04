using System.Collections.Generic;

namespace SupremeStadiumSoundSelector;

/// <summary>
/// A named preset controlling default volume, background music level, and duck amount.
/// Users pick one via dropdown: "Hype", "Chill", "Meme", etc.
/// </summary>
public sealed class VolumeProfilePreset
{
    public string Name { get; init; } = "";
    public float DefaultVolume { get; init; } = 1.0f;
    public float BackgroundVolume { get; init; } = 0.6f;
    public float MusicDuckAmount { get; init; } = 0.7f;
    public Dictionary<string, float> EventVolumeOverrides { get; init; } = new();

    public static readonly VolumeProfilePreset Hype = new()
    {
        Name = "Hype",
        DefaultVolume = 1.0f,
        BackgroundVolume = 0.8f,
        MusicDuckAmount = 0.9f,
    };

    public static readonly VolumeProfilePreset Chill = new()
    {
        Name = "Chill",
        DefaultVolume = 0.6f,
        BackgroundVolume = 0.4f,
        MusicDuckAmount = 0.4f,
    };

    public static readonly VolumeProfilePreset Meme = new()
    {
        Name = "Meme",
        DefaultVolume = 1.0f,
        BackgroundVolume = 0.3f,
        MusicDuckAmount = 1.0f,
    };

    public static readonly List<VolumeProfilePreset> AllPresets = new() { Hype, Chill, Meme };

    public static VolumeProfilePreset GetByName(string name) =>
        AllPresets.Find(p => p.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) ?? Hype;
}
