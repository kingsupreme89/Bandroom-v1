namespace SupremeStadiumSoundSelector;

/// <summary>Team logo image lookup, by convention: drop a file named exactly after the team
/// (e.g. "Alabama.png") into ConfigStore.TeamLogosFolder and it's picked up automatically.
/// No image present = null, and callers fall back to the initials monogram -- most of the
/// ~148-team roster won't have a logo yet, so this must never be required.</summary>
internal static class TeamLogo
{
    static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp" };

    public static string? FindImagePath(string teamName)
    {
        foreach (var ext in Extensions)
        {
            string candidate = Path.Combine(ConfigStore.TeamLogosFolder, teamName + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
