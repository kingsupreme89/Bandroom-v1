using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>JS-callable surface exposed to the WebView2 page as `chrome.webview.hostObjects.bandroom`.
/// Thin wrapper over the existing backend (ConfigStore/CategoryMap/AudioPlayer/TeamColors) --
/// no new business logic lives here, this just adapts it to what app.js expects.</summary>
public sealed class WebBridge
{
    readonly WebMainForm _host;

    public WebBridge(WebMainForm host) => _host = host;

    public string GetTeams() => JsonSerializer.Serialize(TeamColors.All.Select(t => new
    {
        name = t.Name,
        primary = ColorHex(t.Primary ?? t.Accent),
        secondary = ColorHex(t.Secondary ?? t.Primary ?? t.Accent),
        initials = Initials(t.Name),
    }));

    /// <summary>Placeholder badge text until real logos exist (see TeamLogos\ convention in
    /// GetTeamBackgroundUrl-style lookup, once logo files are actually provided).</summary>
    static string Initials(string teamName)
    {
        var words = teamName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2) return (words[0][0].ToString() + words[1][0]).ToUpperInvariant();
        return words.Length == 1 ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant() : "?";
    }

    public string GetCategories() => JsonSerializer.Serialize(_host.GetCategoryCounts()
        .Select(kv => new { name = kv.Key, assigned = kv.Value.assigned, total = kv.Value.total }));

    public string GetActiveTeam() => Theme.ActiveTeam.Name;

    public void SelectTeam(string name) => _host.SelectTeamFromWeb(name);

    public string? GetTeamBackgroundUrl(string teamName)
    {
        string? path = TeamBackdrop.FindImagePath(teamName);
        if (path == null) return null;
        return "https://teambg/" + Uri.EscapeDataString(Path.GetFileName(path));
    }

    public string ToggleWatching() => _host.ToggleWatchingFromWeb();
    public void ToggleLiveFeed() => _host.ToggleLiveFeedFromWeb();
    public void OpenSettings() => _host.OpenSettingsFromWeb();
    public void ResetTeamProfile() => _host.ResetTeamProfileFromWeb();
    public void OpenHelp() => _host.OpenHelpFromWeb();
    public void TriggerEffectsTest() => _host.TriggerEffectsTestFromWeb();

    public string GetEventsForCategory(string? category) => JsonSerializer.Serialize(_host.GetEvents(category)
        .Select(e => new
        {
            trigger = e.Trigger,
            eventName = e.Event,
            fileName = string.IsNullOrWhiteSpace(e.AudioFile) ? null : Path.GetFileNameWithoutExtension(e.AudioFile),
        }));

    public void AssignEvent(string trigger) => _host.OpenAssignTrackFromWeb(trigger);
    public void PreviewEvent(string trigger) => _host.PreviewEventFromWeb(trigger);
    public void StopPreview() => _host.StopPreviewFromWeb();

    public void SetVolume(int percent) => _host.SetVolumeFromWeb(percent);
    public void SetFadeDelay(int seconds) => _host.SetFadeDelayFromWeb(seconds);
    public void SetReverb(string key) => _host.SetReverbFromWeb(key);

    public void BeginDrag() => _host.BeginWindowDrag();

    static string ColorHex(System.Drawing.Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
