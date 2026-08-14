namespace SupremeStadiumSoundSelector;

/// <summary>Team logo image lookup, by convention: drop a file named exactly after the team
/// (e.g. "Alabama.png") into ConfigStore.TeamLogosFolder and it's picked up automatically.
/// No image present = null, and callers fall back to the initials monogram -- most of the
/// ~148-team roster won't have a logo yet, so this must never be required.</summary>
internal static class TeamLogo
{
    static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp" };

    /// <summary>Must exactly match WebBridge.WriteTeamLogoFile's own sanitization -- BUG FIX:
    /// custom-uploaded logos (SaveCustomTeamLogo) are written under this sanitized name (strips
    /// apostrophes/periods/etc, e.g. "Hawai'i" -> "Hawaii"), but this lookup used to search for
    /// the raw, unsanitized teamName + ext. Any team whose name contained a stripped character
    /// saved its logo successfully to disk but could never find it again on the next lookup --
    /// silently falling back to the old logo/initials, which is exactly the "logo editor didn't
    /// retain info after saving" report. Checking BOTH the sanitized and raw names below so
    /// existing manually-dropped files with punctuation in their filename still work too.</summary>
    static string Sanitize(string teamName) =>
        System.Text.RegularExpressions.Regex.Replace(teamName, @"[^\w\s&-]", "").Trim();

    public static string? FindImagePath(string teamName) => FindImagePathIn(ConfigStore.TeamLogosFolder, teamName);

    /// <summary>Icon-only variant lookup (see ConfigStore.TeamIconsFolder) -- same name-matching
    /// rules as FindImagePath, just a different folder. Returns null (never falls back internally)
    /// so callers can tell "no icon uploaded yet" apart from "no logo at all" and fall back to the
    /// full logo themselves.</summary>
    public static string? FindIconImagePath(string teamName) => FindImagePathIn(ConfigStore.TeamIconsFolder, teamName);

    static string? FindImagePathIn(string folder, string teamName)
    {
        string sanitized = Sanitize(teamName);
        foreach (var ext in Extensions)
        {
            string candidate = Path.Combine(folder, sanitized + ext);
            if (File.Exists(candidate)) return candidate;
        }
        if (sanitized != teamName)
        {
            foreach (var ext in Extensions)
            {
                string candidate = Path.Combine(folder, teamName + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
