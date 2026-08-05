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
        logoUrl = LogoUrl(t.Name),
    }));

    static string? LogoUrl(string teamName)
    {
        string? path = TeamLogo.FindImagePath(teamName);
        if (path == null) return null;
        return "https://teamlogo/" + Uri.EscapeDataString(Path.GetFileName(path));
    }

    /// <summary>Fallback badge text for teams without a logo file in TeamLogos\ (see
    /// TeamLogo.FindImagePath) -- most of the roster still falls back to this monogram.</summary>
    static string Initials(string teamName)
    {
        var words = teamName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2) return (words[0][0].ToString() + words[1][0]).ToUpperInvariant();
        return words.Length == 1 ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant() : "?";
    }

    public string GetCategories() => JsonSerializer.Serialize(_host.GetCategoryCounts()
        .Select(kv => new { name = kv.Key, assigned = kv.Value.assigned, total = kv.Value.total }));

    public string GetActiveTeam() => Theme.ActiveTeam.Name;

    /// <summary>Live "people running Bandroom right now" count, or -1 if the ticker isn't
    /// configured/reachable -- JS treats -1 as "hide the ticker entirely".</summary>
    public async Task<int> GetActiveUserCount() => await UserCountService.GetCountAsync() ?? -1;

    public bool IsFirstRun() => ConfigStore.IsFirstRun();

    public void CompleteFirstRun(string teamName)
    {
        _host.SelectTeamFromWeb(teamName);
        ConfigStore.MarkFirstRunDone();
    }

    public string GetAppVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";

    public void SelectTeam(string name) => _host.SelectTeamFromWeb(name);

    public string? GetTeamBackgroundUrl(string teamName)
    {
        string? path = TeamBackdrop.FindImagePath(teamName);
        if (path == null) return null;
        return "https://teambg/" + Uri.EscapeDataString(Path.GetFileName(path));
    }

    public string ToggleWatching() => _host.ToggleWatchingFromWeb();
    public void OpenSettings() => _host.OpenSettingsFromWeb();
    public void ShowUpdate() => _host.ShowUpdateDialogFromWeb();
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
    public void SetHomeVolume(int percent) => _host.SetHomeVolumeFromWeb(percent);
    public void SetAwayVolume(int percent) => _host.SetAwayVolumeFromWeb(percent);
    public int GetHomeVolume() => _host.GetHomeVolumeFromWeb();
    public int GetAwayVolume() => _host.GetAwayVolumeFromWeb();
    public void SetFadeDelay(int seconds) => _host.SetFadeDelayFromWeb(seconds);
    public void SetReverb(string key) => _host.SetReverbFromWeb(key);

    /// <summary>Real changelog -- Bandroom's own GitHub Releases (version, title, bullet
    /// notes, published date). Powers the "Updates" panel (formerly a Live Feed of in-session
    /// cue fires, repurposed since a running changelog is more useful than a feed nobody
    /// looked at).</summary>
    public async Task<string> GetChangelog()
    {
        try
        {
            var releases = await ChangelogService.GetReleasesAsync();
            return JsonSerializer.Serialize(releases.Select(r => new
            {
                version = r.Version,
                title = r.Title,
                notes = r.Notes,
                publishedAt = r.PublishedAt.ToString("yyyy-MM-dd"),
                prerelease = r.Prerelease,
            }));
        }
        catch (Exception ex)
        {
            CrashLog.Write("GetChangelog failed", ex);
            return "[]";
        }
    }

    public void BeginDrag() => _host.BeginWindowDrag();
    public void MinimizeWindow() => _host.MinimizeWindowFromWeb();
    public void MaximizeWindow() => _host.MaximizeWindowFromWeb();
    public void CloseWindow() => _host.CloseWindowFromWeb();

    public string GetSavedProfiles() => JsonSerializer.Serialize(ConfigStore.ListProfiles());
    public string SaveProfileAs(string? name) => _host.SaveProfileAsFromWeb(name);
    public string? GetProfileSavedAt(string name) => _host.GetProfileSavedAtFromWeb(name);
    public void SetGameTeams(string home, string away) => _host.SetGameTeamsFromWeb(home, away);
    public string? GetGameTeams() => _host.GetGameTeamsFromWeb();
    public void ConfirmGametime(string home, string away) => _host.ConfirmGametimeFromWeb(home, away);
    public bool IsMatchupLocked() => _host.IsMatchupLockedFromWeb();
    public void CopyCurrentToAllTeams() => _host.CopyCurrentToAllTeamsFromWeb();
    public void DeleteCurrentProfile() => _host.DeleteCurrentProfileFromWeb();
    public void ExportProfile() => _host.ExportProfileFromWeb();
    public void ImportProfile() => _host.ImportProfileFromWeb();

    static string ColorHex(System.Drawing.Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
