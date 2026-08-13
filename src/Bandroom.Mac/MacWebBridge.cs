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
    public void ShowUpdate() { /* TODO: Sparkle update check */ }
    public void RestartForUpdate() { /* TODO: Sparkle restart */ }
    public void ResetTeamProfile() => _host.ResetTeamProfileFromWeb();
    public void OpenHelp() { }
    public void TriggerEffectsTest() => _host.TriggerEffectsTestFromWeb();
    public void OpenExternalUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

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
            paFileName = string.IsNullOrWhiteSpace(e.PaAudioFile) ? null : Path.GetFileNameWithoutExtension(e.PaAudioFile),
            confirmed = ConfirmedTriggers.Contains(e.Trigger),
        }));

    public void AssignEvent(string trigger) => _host.OpenAssignTrackFromWeb(trigger);
    public void AssignPaEvent(string trigger) => _host.OpenAssignPaTrackFromWeb(trigger);
    public void PreviewEvent(string trigger) => _host.PreviewEventFromWeb(trigger);
    public void StopPreview() => _host.StopPreviewFromWeb();

    // ---- Clipping-island assign flow (mirrors Windows WebBridge) ----

    public string GetTrackLibrary() => _host.GetTrackLibraryFromWeb();
    public void PreviewLocalFile(string path) => _host.PreviewLocalFileFromWeb(path);
    public void AssignTrackFile(string trigger, bool isPa, string path) => _host.AssignTrackFileFromWeb(trigger, isPa, path);
    // Parity gap found in the 2026-08-11 audit: app.js's "Copy From..." button called this bridge
    // method name unconditionally on both platforms, but Mac had no matching MacWebBridge method --
    // would throw on click. Mirrors WebBridge.cs's naming exactly. (The sound-start-delay pairing
    // was removed 2026-08-12 -- that setting is gone app-wide, so its bridge methods are too.)
    public bool CopyEventAssignment(string sourceTrigger, string targetTrigger) => _host.CopyEventAssignmentFromWeb(sourceTrigger, targetTrigger);
    public void ClearTrackAssignment(string trigger, bool isPa) => _host.ClearTrackAssignmentFromWeb(trigger, isPa);
    public string? BrowseForAudioFile() => _host.BrowseForAudioFileFromWeb();
    public void OpenTrimmer(string trigger, bool isPa) => _host.OpenTrimmerFromWeb(trigger, isPa);
    public int GetEventVolume(string trigger) => _host.GetEventVolumeFromWeb(trigger);
    public void SetEventVolume(string trigger, int percent) => _host.SetEventVolumeFromWeb(trigger, percent);
    public bool AddLibraryFileToDownloads(string path) => _host.AddLibraryFileToDownloadsFromWeb(path);
    public string AddSongsBatch() => _host.AddSongsBatchFromWeb();
    public string PrepareTrim(string trigger, bool isPa) => _host.PrepareTrimFromWeb(trigger, isPa);
    public string SaveTrim(string trigger, bool isPa, double startSec, double endSec, string? sourceName = null) =>
        JsonSerializer.Serialize(new { ok = false, error = "Trimming isn't supported on the Mac app yet -- choose a different clip instead." });

    // ---- Default/conference song pack browsing (mirrors WebBridge.cs ~1128-1354) ----

    public string? BrowseForSongPackFolder() => _host.BrowseForSongPackFolderFromWeb();
    public void ImportDefaultSongPackFolder(string folderPath, bool overwrite = false) => _host.ImportDefaultSongPackFolderFromWeb(folderPath, overwrite);
    public string GetDefaultSongsFolderPath() => ConfigStore.DownloadedDefaultSongsFolder;
    public string GetDefaultPackSongsForTeam(string teamName) => _host.GetDefaultPackSongsForTeamFromWeb(teamName);
    public string GetDefaultPackTeams() => _host.GetDefaultPackTeamsFromWeb();
    public string GetConferencePackSongsForTeam(string teamName) => _host.GetConferencePackSongsForTeamFromWeb(teamName);
    public string PreviewConferencePackForTeam(string teamName) => _host.PreviewConferencePackForTeamFromWeb(teamName);
    public int ApplyConferencePackSelections(string teamName, string eventKeysJson) => _host.ApplyConferencePackSelectionsFromWeb(teamName, eventKeysJson);

    // ---- Whistle volume + meter levels (mirrors WebBridge.cs ~1255-1267) ----

    public void SetWhistleVolume(int percent) => AudioPlayer.WhistleVolume = percent / 100f;
    public int GetWhistleVolume() => (int)(AudioPlayer.WhistleVolume * 100);

    /// <summary>Sound Booth meters -- Mac's afplay backend has no live level-metering tap (no
    /// audio pipeline to read from, unlike NAudio's sample-provider chain on Windows), so this
    /// always reports silence rather than faking movement.</summary>
    public string GetCurrentLevels() => "{\"in\":0,\"out\":0}";

    /// <summary>Windows Core Audio (WASAPI) readout -- no macOS equivalent wired up (would need
    /// CoreAudio/AVFoundation native interop). Reports "unknown" honestly instead of faking a
    /// system volume reading.</summary>
    public string GetSystemVolumeInfo() => JsonSerializer.Serialize(new { known = false, volumePercent = 100, muted = false });

    /// <summary>Band Director dashboard's OBS overlay chat URL -- same port the Mac HttpListener
    /// already serves everything else from, so this works unmodified.</summary>
    public string GetOverlayChatUrl() => "http://localhost:18765/overlay/chat";

    // ---- Volume & Audio settings ----

    public void SetVolume(int percent) => _host.SetVolumeFromWeb(percent);
    public void SetHomeVolume(int percent) => _host.SetHomeVolumeFromWeb(percent);
    public void SetAwayVolume(int percent) => _host.SetAwayVolumeFromWeb(percent);
    public int GetHomeVolume() => _host.GetHomeVolumeFromWeb();
    public int GetAwayVolume() => _host.GetAwayVolumeFromWeb();
    public void SetPaVolume(int percent) => _host.SetPaVolumeFromWeb(percent);
    public int GetPaVolume() => _host.GetPaVolumeFromWeb();
    public int GetVolume() => (int)(AudioPlayer.MasterVolume * 100);
    public int GetFadeDelay() => _host.GetFadeDelayFromWeb();
    public string GetReverb() => _host.GetReverbFromWeb();
    public string GetScorebugPresets() => _host.GetScorebugPresetsFromWeb();
    public void SetScorebugPreset(string name) => _host.SetScorebugPresetFromWeb(name);

    // ---- Settings tab (merged into the themed Profile overlay on Windows too — see
    // Bandroom_Handoff_2026-08-11_Session40.md) ----
    public void StopPlayback() => _host.StopPlaybackFromWeb();
    public void OpenSongsFolder() => _host.OpenSongsFolderFromWeb();
    public void ClearAllAssignments() => _host.ClearAllAssignmentsFromWeb();
    public bool GetAlwaysOnTop() => _host.GetAlwaysOnTopFromWeb();
    public void SetAlwaysOnTop(bool enabled) => _host.SetAlwaysOnTopFromWeb(enabled);
    public string GetPlaybackTimingSettings() => _host.GetPlaybackTimingSettingsFromWeb();
    public void SavePlaybackTimingSettings(string settingsJson)
    {
        var settings = JsonSerializer.Deserialize<ConfigStore.PlaybackTimingSettings>(settingsJson);
        if (settings != null) _host.SavePlaybackTimingSettingsFromWeb(settings);
    }

    /// <summary>Band Director dashboard, Phase 1 — only the quick-trigger slot->EventKey mapping
    /// is real/persisted so far (see Bandroom_Handoff_2026-08-11_Session40.md).</summary>
    public string GetBandDirectorDashboardSettings() =>
        JsonSerializer.Serialize(ConfigStore.LoadBandDirectorDashboardSettings());
    public void SaveBandDirectorDashboardSettings(string quickTriggerMapJson)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(quickTriggerMapJson) ?? new Dictionary<string, string>();
        ConfigStore.SaveBandDirectorDashboardSettings(new ConfigStore.BandDirectorDashboardSettings(map));
    }
    public bool GetLeadInWhistleAvailable() => !string.IsNullOrWhiteSpace(AudioPlayer.LeadInClipPath) && File.Exists(AudioPlayer.LeadInClipPath);
    public bool GetLeadInWhistleEnabled() => AudioPlayer.LeadInEnabled;
    public void SetLeadInWhistleEnabled(bool enabled)
    {
        AudioPlayer.LeadInEnabled = enabled;
        ConfigStore.SaveLeadInWhistleEnabled(enabled);
    }
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

    public bool SetTeamBackgroundFromDownload(string downloadId) => _host.SetTeamBackgroundFromDownloadFromWeb(downloadId);

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

    public async Task<string> ImportAndUploadSongToMarketplace(string school)
    {
        if (string.IsNullOrWhiteSpace(school))
            return JsonSerializer.Serialize(new { success = false, error = "No team selected." });

        var raw = _host.ImportLocalSongFromWeb();
        using var import = JsonDocument.Parse(raw);
        var root = import.RootElement;
        if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
            return raw;

        var path = root.GetProperty("path").GetString();
        var entry = ConfigStore.LoadLocalTracks().FirstOrDefault(e => e.Path == path);
        if (entry == null)
            return JsonSerializer.Serialize(new { success = false, error = "Track was trimmed but couldn't be found for upload." });

        return await ShareLocalTrackToMarketplace(entry.Id, school);
    }

    // ---- Profile sharing ----

    public async Task<string> ShareCurrentProfileToMarketplace()
    {
        var team = Theme.ActiveTeam.Name;
        var entries = _host.GetEvents(null)
            .Where(e => !string.IsNullOrWhiteSpace(e.AudioFile))
            .Select(e => new { trigger = e.Trigger, eventName = e.Event, fileName = Path.GetFileName(e.AudioFile) })
            .ToList();
        if (entries.Count == 0)
            return JsonSerializer.Serialize(new { success = false, error = "No songs are assigned yet -- assign at least one before sharing this team's profile." });

        try
        {
            var json = JsonSerializer.Serialize(new { team, assignments = entries });
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var form = new System.Net.Http.MultipartFormDataContent();
            form.Add(new System.Net.Http.StringContent("profile"), "type");
            form.Add(new System.Net.Http.StringContent($"{team} profile ({entries.Count} songs)"), "name");
            form.Add(new System.Net.Http.StringContent(team), "school");
            var fileContent = new System.Net.Http.ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(json));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            form.Add(fileContent, "file", $"{team}-profile.json");

            using var response = await http.PostAsync("https://bandroom-marketplace.bandroom.workers.dev/upload", form);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });

            return JsonSerializer.Serialize(new { success = true, count = entries.Count });
        }
        catch
        {
            return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });
        }
    }

    public async Task<string> GetMarketplaceProfiles(string school)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await http.GetAsync(
                $"https://bandroom-marketplace.bandroom.workers.dev/list?type=profile&school={Uri.EscapeDataString(school)}&sort=newest");
            if (!response.IsSuccessStatusCode) return "{\"items\":[]}";
            return await response.Content.ReadAsStringAsync();
        }
        catch { return "{\"items\":[]}"; }
    }

    // ---- Team profile publishing (name/colors/bio/logo) -- distinct from song-assignment
    // profile sharing above. See WebBridge.cs's PublishTeamProfileToMarketplace for the Windows
    // counterpart and worker.js's "teamprofile" upload type.
    public async Task<string> PublishTeamProfileToMarketplace(string bio)
    {
        var team = Theme.ActiveTeam.Name;
        var primary = ColorHex(Theme.ActiveTeam.Primary ?? Theme.ActiveTeam.Accent);
        var secondary = ColorHex(Theme.ActiveTeam.Secondary ?? Theme.ActiveTeam.Primary ?? Theme.ActiveTeam.Accent);
        var logoUrl = LogoUrl(team);
        var trimmedBio = (bio ?? "").Trim();
        if (trimmedBio.Length > 140) trimmedBio = trimmedBio[..140];

        try
        {
            var json = JsonSerializer.Serialize(new { team, primary, secondary, bio = trimmedBio, logoUrl });
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var form = new System.Net.Http.MultipartFormDataContent();
            form.Add(new System.Net.Http.StringContent("teamprofile"), "type");
            form.Add(new System.Net.Http.StringContent($"{team} team profile"), "name");
            form.Add(new System.Net.Http.StringContent(team), "school");
            var fileContent = new System.Net.Http.ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(json));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            form.Add(fileContent, "file", $"{team}-teamprofile.json");

            using var response = await http.PostAsync("https://bandroom-marketplace.bandroom.workers.dev/upload", form);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });

            return JsonSerializer.Serialize(new { success = true, team });
        }
        catch
        {
            return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });
        }
    }

    public async Task<string> GetMarketplaceTeamProfiles()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await http.GetAsync("https://bandroom-marketplace.bandroom.workers.dev/list?type=teamprofile&sort=newest");
            if (!response.IsSuccessStatusCode) return "{\"items\":[]}";
            return await response.Content.ReadAsStringAsync();
        }
        catch { return "{\"items\":[]}"; }
    }

    public async Task<string> ApplyMarketplaceProfile(string fileUrl)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await http.GetAsync(fileUrl);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = "Couldn't download that profile -- try again." });
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var assignments = doc.RootElement.GetProperty("assignments");

            var library = new List<string>();
            if (Directory.Exists(ConfigStore.SongsFolder))
                library.AddRange(Directory.GetFiles(ConfigStore.SongsFolder, "*", SearchOption.AllDirectories));
            var byFileName = library
                .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int applied = 0, total = 0;
            var unmatched = new List<string>();
            foreach (var a in assignments.EnumerateArray())
            {
                total++;
                var trigger = a.GetProperty("trigger").GetString() ?? "";
                var eventName = a.GetProperty("eventName").GetString() ?? trigger;
                var fileName = a.GetProperty("fileName").GetString() ?? "";
                if (byFileName.TryGetValue(fileName, out var localPath))
                {
                    _host.AssignTrackFileFromWeb(trigger, isPa: false, localPath);
                    applied++;
                }
                else
                {
                    unmatched.Add(eventName);
                }
            }

            return JsonSerializer.Serialize(new { success = true, applied, total, unmatched });
        }
        catch
        {
            return JsonSerializer.Serialize(new { success = false, error = "Couldn't apply that profile -- try again." });
        }
    }

    // ---- Admin (no-op on Mac since admin token path is Windows-only) ----

    public bool IsAdminMode() => false;

    public async Task<string> AdminDeleteMarketplaceItem(string type, string id) =>
        JsonSerializer.Serialize(new { success = false, error = "Admin mode is not active." });

    public async Task<string> AdminEditMarketplaceItem(string type, string id, string newName, string newSchool) =>
        JsonSerializer.Serialize(new { success = false, error = "Admin mode is not active." });

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

            // Merge cloud profile with local
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

        var achievements = new List<object>
        {
            new { id = "favorite_team_set", label = "Picked a Favorite Team", unlocked = p.FavoriteTeam != null },
            new { id = "first_upload", label = "First Upload", unlocked = p.MarketplaceUploads >= 1 },
            new { id = "first_download", label = "First Download", unlocked = p.MarketplaceDownloads >= 1 },
            new { id = "ten_games", label = "10 Games Watched", unlocked = p.GamesWatched >= 10 },
            new { id = "hundred_games", label = "100 Games Watched", unlocked = p.GamesWatched >= 100 },
            new { id = "hundred_songs", label = "100 Songs Triggered", unlocked = p.SongsTriggered >= 100 },
            new { id = "thousand_songs", label = "1,000 Songs Triggered", unlocked = p.SongsTriggered >= 1000 },
            new { id = "week_streak", label = "7-Day Streak", unlocked = p.StreakCurrentDays >= 7 },
        };

        return JsonSerializer.Serialize(new
        {
            favoriteTeam = p.FavoriteTeam, rivalTeam = p.RivalTeam, bio = p.Bio,
            gamesWatched = p.GamesWatched, songsTriggered = p.SongsTriggered,
            marketplaceUploads = p.MarketplaceUploads, marketplaceDownloads = p.MarketplaceDownloads,
            gamesWatchedByTeam = p.GamesWatchedByTeam,
            mostTriggeredEvent = topEvent, mostTriggeredCount = topEventCount,
            streakCurrentDays = p.StreakCurrentDays, favoriteTeamWins = p.FavoriteTeamWins,
            favoriteTeamLosses = p.FavoriteTeamLosses, createdAt = p.CreatedAt.ToString("O"),
            toastsEnabled = p.ToastsEnabled,
            avatarUrl = p.AvatarFileName != null ? "https://avatar/" + Uri.EscapeDataString(p.AvatarFileName) : null,
            level, achievements,
        });
    }

    public void SetFavoriteTeam(string team) { var u = ConfigStore.LoadUserProfile() with { FavoriteTeam = string.IsNullOrWhiteSpace(team) ? null : team }; ConfigStore.SaveUserProfile(u); _ = ProfileSyncService.PushAsync(u); }
    public void SetRivalTeam(string team) { var u = ConfigStore.LoadUserProfile() with { RivalTeam = string.IsNullOrWhiteSpace(team) ? null : team }; ConfigStore.SaveUserProfile(u); _ = ProfileSyncService.PushAsync(u); }
    public void SetBio(string bio) { var trimmed = (bio ?? "").Trim(); if (trimmed.Length > 140) trimmed = trimmed[..140]; ConfigStore.SaveUserProfile(ConfigStore.LoadUserProfile() with { Bio = trimmed.Length == 0 ? null : trimmed }); }
    public void SetToastsEnabled(bool enabled) { ConfigStore.SaveUserProfile(ConfigStore.LoadUserProfile() with { ToastsEnabled = enabled }); }

    public void RecordFavoriteTeamResult(bool win)
    {
        var current = ConfigStore.LoadUserProfile();
        var updated = win
            ? current with { FavoriteTeamWins = current.FavoriteTeamWins + 1 }
            : current with { FavoriteTeamLosses = current.FavoriteTeamLosses + 1 };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    public void ResetUserProfileStats()
    {
        var current = ConfigStore.LoadUserProfile();
        var updated = current with
        {
            GamesWatched = 0, SongsTriggered = 0, MarketplaceUploads = 0, MarketplaceDownloads = 0,
            EventCounts = new(), GamesWatchedByTeam = new(),
            StreakCurrentDays = 0, StreakLastActiveDate = null,
            FavoriteTeamWins = 0, FavoriteTeamLosses = 0,
        };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    public bool UploadAvatar(string base64Png)
    {
        if (string.IsNullOrWhiteSpace(base64Png)) return false;
        try
        {
            byte[] bytes = Convert.FromBase64String(base64Png);
            if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) return false;

            Directory.CreateDirectory(ConfigStore.AvatarFolder);
            const string fileName = "avatar.png";
            File.WriteAllBytes(Path.Combine(ConfigStore.AvatarFolder, fileName), bytes);

            var updated = ConfigStore.LoadUserProfile() with { AvatarFileName = fileName };
            ConfigStore.SaveUserProfile(updated);
            _ = ProfileSyncService.PushAsync(updated);
            return true;
        }
        catch { return false; }
    }

    public bool SaveCustomTeamBackground(string team, string base64Png)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(base64Png)) return false;
            if (TeamColors.All.All(t => t.Name != team)) return false;

            byte[] bytes = Convert.FromBase64String(base64Png);
            if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) return false;

            Directory.CreateDirectory(ConfigStore.TeamBackgroundsFolder);
            string safeTeam = System.Text.RegularExpressions.Regex.Replace(team, @"[^\w\s&-]", "").Trim();
            if (safeTeam.Length == 0) return false;

            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
            {
                string oldPath = Path.Combine(ConfigStore.TeamBackgroundsFolder, safeTeam + ext);
                if (File.Exists(oldPath)) { try { File.Delete(oldPath); } catch { } }
            }

            File.WriteAllBytes(Path.Combine(ConfigStore.TeamBackgroundsFolder, safeTeam + ".png"), bytes);
            return true;
        }
        catch { return false; }
    }

    public async Task<string> SaveCustomTeamLogo(string team, string base64Png)
    {
        bool ok = false;
        bool pushFailed = false;
        try
        {
            if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(base64Png))
                return JsonSerializer.Serialize(new { ok, pushFailed });
            if (TeamColors.All.All(t => t.Name != team))
                return JsonSerializer.Serialize(new { ok, pushFailed });

            byte[] bytes = Convert.FromBase64String(base64Png);
            if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024)
                return JsonSerializer.Serialize(new { ok, pushFailed });

            Directory.CreateDirectory(ConfigStore.TeamLogosFolder);
            string safeTeam = System.Text.RegularExpressions.Regex.Replace(team, @"[^\w\s&-]", "").Trim();
            if (safeTeam.Length == 0) return JsonSerializer.Serialize(new { ok, pushFailed });

            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            {
                string oldPath = Path.Combine(ConfigStore.TeamLogosFolder, safeTeam + ext);
                if (File.Exists(oldPath)) { try { File.Delete(oldPath); } catch { } }
            }

            File.WriteAllBytes(Path.Combine(ConfigStore.TeamLogosFolder, safeTeam + ".png"), bytes);
            ok = true;

            var updatedAt = DateTime.UtcNow;
            var current = ConfigStore.LoadUserProfile();
            var newLogos = new Dictionary<string, ConfigStore.TeamLogoEntry>(current.CustomTeamLogos)
            {
                [team] = new ConfigStore.TeamLogoEntry { Base64Png = base64Png, UpdatedAtUtc = updatedAt },
            };
            var updated = current with { CustomTeamLogos = newLogos };
            ConfigStore.SaveUserProfile(updated);

            var manifest = ConfigStore.LoadTeamLogoSyncManifest();
            var appliedAt = new Dictionary<string, DateTime>(manifest.AppliedAtUtc) { [team] = updatedAt };
            ConfigStore.SaveTeamLogoSyncManifest(manifest with { AppliedAtUtc = appliedAt });

            if (ConfigStore.LoadAuthSession() != null)
                pushFailed = !await ProfileSyncService.PushAsync(updated);

            return JsonSerializer.Serialize(new { ok, pushFailed });
        }
        catch { return JsonSerializer.Serialize(new { ok, pushFailed }); }
    }

    public void ExportUserProfile() => _host.ExportUserProfileFromWeb();
    public void ImportUserProfile() => _host.ImportUserProfileFromWeb();

    // ---- Matchup / Profiles / Changelog ----

    public string GetSavedProfiles() => JsonSerializer.Serialize(ConfigStore.ListProfiles());
    public string SaveProfileAs(string? name) => _host.SaveProfileAsFromWeb(name);
    public string? GetProfileSavedAt(string name) => _host.GetProfileSavedAtFromWeb(name);
    public void SetGameTeams(string home, string away) => _host.SetGameTeamsFromWeb(home, away);
    public string? GetGameTeams() => _host.GetGameTeamsFromWeb();
    public void ConfirmGametime(string home, string away) => _host.ConfirmGametimeFromWeb(home, away);
    public bool IsMatchupLocked() => _host.IsMatchupLockedFromWeb();
    public void UnlockMatchup() => _host.UnlockMatchupFromWeb();
    public void CopyCurrentToAllTeams() => _host.CopyCurrentToAllTeamsFromWeb();
    public void DeleteCurrentProfile() => _host.DeleteCurrentProfileFromWeb();
    public void ExportProfile() => _host.ExportProfileFromWeb();
    public void ImportProfile() => _host.ImportProfileFromWeb();

    // ---- Test hooks ----

    public string GetAllEventKeys() => _host.GetAllEventKeysFromWeb();
    public string FireTestEvent(string side, string eventKey) => _host.FireTestEventFromWeb(side, eventKey);

    // ---- Default song pack ----

    public bool HasDefaultSongPack() => ConfigStore.HasDefaultSongPack;
    public void DownloadDefaultSongPack() => _host.DownloadDefaultSongPackFromWeb();
    public string? BrowseForSongPackZip() => _host.BrowseForSongPackZipFromWeb();
    public void ImportDefaultSongPackZip(string zipPath) => _host.ImportDefaultSongPackZipFromWeb(zipPath);

    // ---- Counters ----

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

    // ---- Track Info drawer / audio metadata (mirrors WebBridge.cs ~312-357) ----

    static readonly JsonSerializerOptions CamelCaseJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    string? ResolveAudioFileForTrigger(string trigger) =>
        _host.GetEvents(null).FirstOrDefault(e => e.Trigger == trigger && !string.IsNullOrWhiteSpace(e.AudioFile))?.AudioFile;

    public string GetTrackMetadata(string trigger)
    {
        var audioFile = ResolveAudioFileForTrigger(trigger);
        if (audioFile == null) return "null";
        var meta = AudioTrackMetadataStore.Load(audioFile);
        return meta != null ? JsonSerializer.Serialize(meta, CamelCaseJsonOptions) : "null";
    }

    public string SaveTrackMetadata(string trigger, string metadataJson)
    {
        var audioFile = ResolveAudioFileForTrigger(trigger);
        if (audioFile == null)
            return JsonSerializer.Serialize(new { success = false, error = "No song is assigned to this trigger yet." });
        try
        {
            var meta = JsonSerializer.Deserialize<AudioTrackMetadata>(metadataJson, CamelCaseJsonOptions) ?? new AudioTrackMetadata();
            AudioTrackMetadataStore.Save(audioFile, meta);
            return JsonSerializer.Serialize(new { success = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write("SaveTrackMetadata failed", ex);
            return JsonSerializer.Serialize(new { success = false, error = "Couldn't save track info." });
        }
    }

    /// <summary>Mac counterpart of WebBridge.AnalyzeTrackMetadata -- IntakeEngine.AnalyzeAndSuggest
    /// itself is cross-platform, but the duration/loudness half of what it returns depends on
    /// AudioTrackMetadataStore.AnalyzeAudioFile, which is a Mac stub returning zeros/nulls (see
    /// PlatformStubs.Mac.cs -- real analysis needs NAudio's AudioFileReader, Windows-only). Title/
    /// artist/school filename-parsing suggestions still come back real.</summary>
    public string AnalyzeTrackMetadata(string trigger)
    {
        var audioFile = ResolveAudioFileForTrigger(trigger);
        if (audioFile == null || !File.Exists(audioFile))
            return JsonSerializer.Serialize(new { success = false, error = "No song is assigned to this trigger yet." });
        try
        {
            var suggestion = IntakeEngine.AnalyzeAndSuggest(audioFile);
            return JsonSerializer.Serialize(new { success = true, metadata = suggestion }, CamelCaseJsonOptions);
        }
        catch (Exception ex)
        {
            CrashLog.Write("AnalyzeTrackMetadata failed", ex);
            return JsonSerializer.Serialize(new { success = false, error = "Couldn't analyze this file." });
        }
    }

    // ---- Public profile toggle (mirrors WebBridge.cs ~858) ----

    public string TogglePublicProfile(bool isPublic)
    {
        var current = ConfigStore.LoadUserProfile();
        if (isPublic && string.IsNullOrWhiteSpace(current.GoogleUserId))
            return JsonSerializer.Serialize(new { ok = false, error = "Sign in with Google first -- a public profile needs an account to publish under." });

        var updated = ConfigStore.MutateUserProfile(c => c with { IsPublicProfile = isPublic });
        _ = ProfileSyncService.PushAsync(updated);
        return JsonSerializer.Serialize(new { ok = true, isPublicProfile = updated.IsPublicProfile });
    }

    // ---- Big Game conditional slots (mirrors WebBridge.cs ~1224-1226, ~1355-1357) ----

    public bool AssignBigGameTrackFile(string trigger, string path) => _host.AssignBigGameTrackFileFromWeb(trigger, path);
    public void ClearBigGameTrackAssignment(string trigger) => _host.ClearBigGameTrackAssignmentFromWeb(trigger);
    public string GetBigGameSettings() => JsonSerializer.Serialize(ConfigStore.LoadBigGameSettings());
    public void SaveBigGameSettings(bool isBigGame) =>
        ConfigStore.SaveBigGameSettings(new ConfigStore.BigGameSettings(isBigGame, 4, 8));

    // ---- Help & Guide Event Log (mirrors WebBridge.cs ~1101-1104) ----

    public string GetEventActivityLog() => _host.GetEventActivityLogFromWeb();
    public string ExportEventActivityLog() => _host.ExportEventActivityLogFromWeb();

    // ---- Supabase settings (mirrors WebBridge.cs ~1132-1138) ----

    public string GetSupabaseSettings()
    {
        var (url, anonKey) = ConfigStore.LoadSupabaseSettings();
        return JsonSerializer.Serialize(new { url, anonKey });
    }
    public void SaveSupabaseSettings(string url, string anonKey) => ConfigStore.SaveSupabaseSettings(url, anonKey);

    // ---- Default songs folder relocate (mirrors WebBridge.cs ~1153) ----

    public async Task<string> RelocateDefaultSongsFolder()
    {
        string? chosen = await _host.BrowseForFolderFromWeb("Choose where to keep the default song pack");
        if (chosen == null)
            return JsonSerializer.Serialize(new { success = false, cancelled = true });

        bool ok = ConfigStore.SetDefaultSongsFolderOverride(chosen);
        return ok
            ? JsonSerializer.Serialize(new { success = true, path = chosen })
            : JsonSerializer.Serialize(new { success = false, error = "Couldn't move the song pack there." });
    }

    // ---- Soundboard / Dynasty save (mirrors WebBridge.cs ~1220-1221) ----

    public void PlaySoundboardSlot(string key, string path) => _host.PlaySoundboardSlotFromWeb(key, path);
    public Task<string?> ScanDynastySave() => _host.ScanDynastySaveFromWeb();

    // ---- Whistle trim/browse (mirrors WebBridge.cs ~1232-1255) ----

    public string PrepareTrimForWhistle(string path) => _host.PrepareTrimForWhistleFromWeb(path);
    public string SaveTrimAsLeadInWhistle(double startSec, double endSec) => _host.SaveTrimAsLeadInWhistleFromWeb(startSec, endSec);
    public void SetEventPlayLeadInWhistle(string trigger, bool enabled) => _host.SetEventPlayLeadInWhistleFromWeb(trigger, enabled);
    public async Task<bool> BrowseAndSetLeadInWhistle() => await _host.BrowseAndSetLeadInWhistleFromWeb();

    // ---- EQ/DSP controls (mirrors WebBridge.cs ~1265-1283) ----
    // See AudioPlayer.Mac.cs's DSP-fields doc comment: afplay has no real-time effects chain, so
    // these persist state (in-memory, matching Windows' own non-ConfigStore-backed behavior) but
    // do not change actual playback.

    public string GetEqPreset() => AudioPlayer.CurrentEq.ToString().ToLowerInvariant();
    public void SetEqPreset(string key) => AudioPlayer.CurrentEq = key switch
    {
        "marchingband" => AudioPlayer.EqPreset.MarchingBand,
        "megaphone" => AudioPlayer.EqPreset.Megaphone,
        _ => AudioPlayer.EqPreset.Off,
    };
    public bool GetTransientShaperEnabled() => AudioPlayer.TransientShaperEnabled;
    public void SetTransientShaperEnabled(bool enabled) => AudioPlayer.TransientShaperEnabled = enabled;
    public bool GetStereoWidenerEnabled() => AudioPlayer.StereoWidenerEnabled;
    public void SetStereoWidenerEnabled(bool enabled) => AudioPlayer.StereoWidenerEnabled = enabled;
    public bool GetDuckingEnabled() => AudioPlayer.DuckingEnabled;
    public void SetDuckingEnabled(bool enabled) => AudioPlayer.DuckingEnabled = enabled;
    public bool GetNoEffectsBypass() => AudioPlayer.NoEffectsBypass;
    public void SetNoEffectsBypass(bool enabled) => AudioPlayer.NoEffectsBypass = enabled;
    public bool GetControllerRumbleEnabled() => ControllerRumbleService.Enabled;
    public void SetControllerRumbleEnabled(bool enabled) => ControllerRumbleService.Enabled = enabled;
    public string GetSubBassLevel() => AudioPlayer.SubBassLevel.ToString().ToLowerInvariant();
    public void SetSubBassLevel(string level) => AudioPlayer.SubBassLevel = level switch
    {
        "subtle" => AudioPlayer.SubBassIntensity.Subtle,
        "stadium" => AudioPlayer.SubBassIntensity.Stadium,
        "earthquake" => AudioPlayer.SubBassIntensity.Earthquake,
        _ => AudioPlayer.SubBassIntensity.Off,
    };
    public bool GetCrowdBusEnabled() => CrowdBusService.Enabled;
    public void SetCrowdBusEnabled(bool enabled) => CrowdBusService.Enabled = enabled;
    public bool GetCrowdBusClipAvailable() => !string.IsNullOrWhiteSpace(CrowdBusService.ClipPath) && File.Exists(CrowdBusService.ClipPath);
    public async Task<bool> BrowseAndSetCrowdBusClip() => await _host.BrowseAndSetCrowdBusClipFromWeb();

    // ---- Profile management (mirrors WebBridge.cs ~1318-1341) ----

    public bool DuplicateProfile(string fromTeam, string toTeam) => _host.DuplicateProfileFromWeb(fromTeam, toTeam);
    public string GetTeamsNeedingDefaultProfile(string home, string away) =>
        JsonSerializer.Serialize(_host.GetTeamsNeedingDefaultProfileFromWeb(home, away));
    public int ApplyDefaultProfileForTeam(string teamName) => _host.ApplyDefaultProfileForTeamFromWeb(teamName);
    public int ApplyDefaultProfileForTeamOverwrite(string teamName) => _host.ApplyDefaultProfileForTeamOverwriteFromWeb(teamName);
    public int ApplyConferencePackForTeam(string teamName, bool overwrite) => _host.ApplyConferencePackForTeamFromWeb(teamName, overwrite);

    // ---- TeamBuilder custom team (mirrors WebBridge.cs ~1384-1418) ----

    public string AddCustomTeam(string name, string primaryHex, string secondaryHex, string mascot = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return JsonSerializer.Serialize(new { success = false, error = "A school name is required." });

            string trimmed = name.Trim();
            if (TeamColors.All.Any(t => t.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                return JsonSerializer.Serialize(new { success = false, error = $"\"{trimmed}\" already exists." });

            System.Drawing.Color primary, secondary;
            try
            {
                primary = System.Drawing.ColorTranslator.FromHtml(primaryHex);
                secondary = System.Drawing.ColorTranslator.FromHtml(secondaryHex);
            }
            catch
            {
                return JsonSerializer.Serialize(new { success = false, error = "Pick valid primary and secondary colors." });
            }

            var team = TeamColors.AddCustomTeam(trimmed, primary, secondary, mascot ?? "");
            return JsonSerializer.Serialize(new
            {
                success = true,
                error = (string?)null,
                team = new { name = team.Name, primary = ColorHex(primary), secondary = ColorHex(secondary), mascot = team.Mascot },
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Couldn't add school: {ex.Message}" });
        }
    }
}