using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SupremeStadiumSoundSelector;
using Theme = SupremeStadiumSoundSelector.Theme;
using TeamBackdrop = SupremeStadiumSoundSelector.TeamBackdrop;

namespace Bandroom.Mac;

/// <summary>
/// JS-callable surface for the Mac WebView — mirrors WebBridge.cs (Windows)
/// exactly so the same wwwroot/app.js works on both platforms with zero changes.
/// Uses ConfigStore, TeamColors, CategoryMap, etc. from shared source files.
/// </summary>
public sealed class MacWebBridge
{
    private readonly MainWindow _host;

    public MacWebBridge(MainWindow host) => _host = host;

    // ---- Teams & UI ----

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

    static string Initials(string teamName)
    {
        var words = teamName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2) return (words[0][0].ToString() + words[1][0]).ToUpperInvariant();
        return words.Length == 1 ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant() : "?";
    }

    static string ColorHex(System.Drawing.Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    public string GetCategories() => JsonSerializer.Serialize(_host.GetCategoryCounts()
        .Select(kv => new { name = kv.Key, assigned = kv.Value.assigned, total = kv.Value.total }));

    public string GetActiveTeam() => Theme.ActiveTeam.Name;

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

    // ---- Settings & Watching ----

    public string ToggleWatching() => _host.ToggleWatchingFromWeb();
    public void OpenSettings() => _host.OpenSettingsFromWeb();
    public void ShowUpdate() { /* TODO: Sparkle update check */ }
    public void RestartForUpdate() { /* TODO: Sparkle restart */ }
    public void ResetTeamProfile() => _host.ResetTeamProfileFromWeb();
    public void OpenHelp() { }
    public void TriggerEffectsTest() => _host.TriggerEffectsTestFromWeb();

    // ---- Event assignment ----

    static readonly HashSet<string> ConfirmedTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "situation:touchdown", "situation:turnover", "situation:pat_good",
        "down:1st", "down:2nd", "down:3rd", "down:4th",
    };

    public string GetEventsForCategory(string? category) => JsonSerializer.Serialize(_host.GetEvents(category)
        .Select(e => new
        {
            trigger = e.Trigger,
            eventName = e.Event,
            fileName = string.IsNullOrWhiteSpace(e.AudioFile) ? null : Path.GetFileNameWithoutExtension(e.AudioFile),
            confirmed = ConfirmedTriggers.Contains(e.Trigger),
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
    public void SetReverb(string key) => AudioPlayer.CurrentReverb = key switch
    {
        "stadium" => AudioPlayer.ReverbPreset.Stadium,
        "dome" => AudioPlayer.ReverbPreset.Dome,
        "nightgame" => AudioPlayer.ReverbPreset.NightGame,
        _ => AudioPlayer.ReverbPreset.Off,
    };

    // ---- Marketplace ----

    public async Task<bool> DownloadAndSetTeamBackground(string team, string url) =>
        await _host.DownloadAndSetTeamBackgroundFromWeb(team, url);

    public async Task<string> DownloadMarketplaceItem(string type, string name, string school, string url)
    {
        string? path = await MarketplaceDownloadService.DownloadAsync(type, name, school, url);
        return path != null
            ? JsonSerializer.Serialize(new { success = true, path })
            : JsonSerializer.Serialize(new { success = false, error = "Download failed -- check your connection and try again." });
    }

    public string GetMyDownloads()
    {
        var marketplace = ConfigStore.LoadMarketplaceDownloads().Select(e => new
        {
            id = e.Id, type = e.Type, name = e.Name, school = (string?)e.School,
            downloadedAt = e.DownloadedAt, sortAt = e.DownloadedAt,
            fileUrl = (e.Type == "image" ? "https://downloadedimages/" : "https://downloadedsongs/")
                + Uri.EscapeDataString(Path.GetFileName(e.Path)),
            schoolLogoUrl = LogoUrl(e.School), source = "marketplace", shared = false, canShare = false,
        });

        var local = ConfigStore.LoadLocalTracks().Select(e => new
        {
            id = e.Id, type = "song", name = e.Name, school = (string?)null,
            downloadedAt = e.CreatedAt, sortAt = e.CreatedAt,
            fileUrl = "https://localtracks/" + Uri.EscapeDataString(Path.GetFileName(e.Path)),
            schoolLogoUrl = (string?)null, source = "local", shared = e.Shared, canShare = !e.Shared,
        });

        return JsonSerializer.Serialize(marketplace.Concat(local).OrderByDescending(e => e.sortAt)
            .Select(e => new { e.id, e.type, e.name, e.school, downloadedAt = e.downloadedAt.ToString("O"), e.fileUrl, e.schoolLogoUrl, e.source, e.shared, e.canShare }));
    }

    public bool RemoveMyDownload(string id) =>
        ConfigStore.RemoveMarketplaceDownload(id) || ConfigStore.RemoveLocalTrack(id);

    public string ImportLocalSong() => _host.ImportLocalSongFromWeb();

    public async Task<string> ShareLocalTrackToMarketplace(string id, string school)
    {
        var entry = ConfigStore.LoadLocalTracks().FirstOrDefault(e => e.Id == id);
        if (entry == null || !File.Exists(entry.Path))
            return JsonSerializer.Serialize(new { success = false, error = "That track couldn't be found." });
        if (string.IsNullOrWhiteSpace(school))
            return JsonSerializer.Serialize(new { success = false, error = "A team/school name is required." });

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var form = new System.Net.Http.MultipartFormDataContent();
            form.Add(new System.Net.Http.StringContent("song"), "type");
            form.Add(new System.Net.Http.StringContent(entry.Name), "name");
            form.Add(new System.Net.Http.StringContent(school), "school");
            var bytes = await File.ReadAllBytesAsync(entry.Path);
            var fileContent = new System.Net.Http.ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "file", Path.GetFileName(entry.Path));

            using var response = await http.PostAsync("https://bandroom-marketplace.bandroom.workers.dev/upload", form);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = "Upload failed." });

            ConfigStore.MarkLocalTrackShared(id);
            return JsonSerializer.Serialize(new { success = true });
        }
        catch { return JsonSerializer.Serialize(new { success = false, error = "Upload failed." }); }
    }

    public void RecordMarketplaceUpload()
    {
        var current = ConfigStore.LoadUserProfile();
        ConfigStore.SaveUserProfile(current with { MarketplaceUploads = current.MarketplaceUploads + 1 });
    }

    public void RecordMarketplaceDownload()
    {
        var current = ConfigStore.LoadUserProfile();
        ConfigStore.SaveUserProfile(current with { MarketplaceDownloads = current.MarketplaceDownloads + 1 });
    }

    // ---- Profile & Auth ----

    public string GetCurrentUser()
    {
        var session = ConfigStore.LoadAuthSession();
        return session == null
            ? JsonSerializer.Serialize(new { signedIn = false })
            : JsonSerializer.Serialize(new { signedIn = true, name = session.Name, email = session.Email, picture = session.Picture, signedInAt = session.SignedInAt.ToString("O") });
    }

    public async Task<string> SignInWithGoogle()
    {
        try
        {
            var profile = await GoogleAuthService.SignInAsync(CancellationToken.None);
            if (profile == null)
                return JsonSerializer.Serialize(new { signedIn = false, error = "Sign-in didn't complete." });

            using var http = new System.Net.Http.HttpClient();
            var payload = JsonSerializer.Serialize(new { idToken = profile.IdToken });
            using var response = await http.PostAsync(
                "https://bandroom-marketplace.bandroom.workers.dev/auth/verify",
                new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { signedIn = false, error = "Couldn't verify sign-in." });

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string sessionToken = doc.RootElement.TryGetProperty("sessionToken", out var st) ? st.GetString() ?? "" : "";
            if (sessionToken.Length == 0)
                return JsonSerializer.Serialize(new { signedIn = false, error = "No session token." });

            ConfigStore.SaveAuthSession(new ConfigStore.AuthSession
            {
                Sub = profile.Sub, Email = profile.Email, Name = profile.Name,
                Picture = profile.Picture, SessionToken = sessionToken,
            });

            // Merge cloud profile with local (same logic as Windows WebBridge)
            var localProfile = ConfigStore.LoadUserProfile();
            var cloudProfile = await ProfileSyncService.PullAsync(sessionToken);
            var merged = cloudProfile == null ? localProfile : new ConfigStore.UserProfile
            {
                FavoriteTeam = localProfile.FavoriteTeam ?? cloudProfile.FavoriteTeam,
                RivalTeam = localProfile.RivalTeam ?? cloudProfile.RivalTeam,
                Bio = localProfile.Bio ?? cloudProfile.Bio,
                GamesWatched = Math.Max(localProfile.GamesWatched, cloudProfile.GamesWatched),
                SongsTriggered = Math.Max(localProfile.SongsTriggered, cloudProfile.SongsTriggered),
                MarketplaceUploads = Math.Max(localProfile.MarketplaceUploads, cloudProfile.MarketplaceUploads),
                MarketplaceDownloads = Math.Max(localProfile.MarketplaceDownloads, cloudProfile.MarketplaceDownloads),
                FavoriteTeamWins = Math.Max(localProfile.FavoriteTeamWins, cloudProfile.FavoriteTeamWins),
                FavoriteTeamLosses = Math.Max(localProfile.FavoriteTeamLosses, cloudProfile.FavoriteTeamLosses),
                StreakCurrentDays = Math.Max(localProfile.StreakCurrentDays, cloudProfile.StreakCurrentDays),
                StreakLastActiveDate = localProfile.StreakLastActiveDate,
                ToastsEnabled = localProfile.ToastsEnabled,
                AvatarFileName = localProfile.AvatarFileName,
                CreatedAt = localProfile.CreatedAt,
            };
            ConfigStore.SaveUserProfile(merged);
            _ = ProfileSyncService.PushAsync(merged);

            return JsonSerializer.Serialize(new { signedIn = true, name = profile.Name, email = profile.Email, picture = profile.Picture });
        }
        catch { return JsonSerializer.Serialize(new { signedIn = false, error = "Sign-in failed." }); }
    }

    public void SignOutOfGoogle() => ConfigStore.ClearAuthSession();

    public string GetUserProfile()
    {
        var p = ConfigStore.LoadUserProfile();
        string? topEvent = null; int topEventCount = 0;
        foreach (var (evt, count) in p.EventCounts)
            if (count > topEventCount) { topEvent = evt; topEventCount = count; }

        int totalActivity = p.GamesWatched * 5 + p.SongsTriggered + p.MarketplaceUploads * 10 + p.MarketplaceDownloads * 2;
        int level = 1 + totalActivity / 50;

        return JsonSerializer.Serialize(new
        {
            favoriteTeam = p.FavoriteTeam, rivalTeam = p.RivalTeam, bio = p.Bio,
            gamesWatched = p.GamesWatched, songsTriggered = p.SongsTriggered,
            marketplaceUploads = p.MarketplaceUploads, marketplaceDownloads = p.MarketplaceDownloads,
            mostTriggeredEvent = topEvent, mostTriggeredCount = topEventCount,
            streakCurrentDays = p.StreakCurrentDays, favoriteTeamWins = p.FavoriteTeamWins,
            favoriteTeamLosses = p.FavoriteTeamLosses, createdAt = p.CreatedAt.ToString("O"),
            toastsEnabled = p.ToastsEnabled, level,
        });
    }

    public void SetFavoriteTeam(string team) { var u = ConfigStore.LoadUserProfile() with { FavoriteTeam = string.IsNullOrWhiteSpace(team) ? null : team }; ConfigStore.SaveUserProfile(u); }
    public void SetRivalTeam(string team) { var u = ConfigStore.LoadUserProfile() with { RivalTeam = string.IsNullOrWhiteSpace(team) ? null : team }; ConfigStore.SaveUserProfile(u); }
    public void SetBio(string bio) { var trimmed = (bio ?? "").Trim(); if (trimmed.Length > 140) trimmed = trimmed[..140]; ConfigStore.SaveUserProfile(ConfigStore.LoadUserProfile() with { Bio = trimmed.Length == 0 ? null : trimmed }); }
    public void SetToastsEnabled(bool enabled) { ConfigStore.SaveUserProfile(ConfigStore.LoadUserProfile() with { ToastsEnabled = enabled }); }

    // ---- Matchup / Profiles / Changelog ----

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

    public async Task<string> GetChangelog()
    {
        try { return JsonSerializer.Serialize((await ChangelogService.GetReleasesAsync()).Select(r => new { version = r.Version, title = r.Title, notes = r.Notes, publishedAt = r.PublishedAt.ToString("yyyy-MM-dd"), prerelease = r.Prerelease })); }
        catch { return "[]"; }
    }

    // ---- Window controls (Avalonia equivalents of WinForms) ----
    public void BeginDrag() => _host.BeginWindowDrag();
    public void MinimizeWindow() => _host.MinimizeWindowFromWeb();
    public void MaximizeWindow() => _host.MaximizeWindowFromWeb();
    public void CloseWindow() => _host.CloseWindowFromWeb();
    public void PlayClickSound() => _host.PlayUiClickSoundFromWeb();
}