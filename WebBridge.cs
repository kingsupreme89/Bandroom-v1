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

    /// <summary>Downloads a Trophy Room image from the marketplace worker and sets it as
    /// <paramref name="team"/>'s local background (see TeamBackgroundDownloadService /
    /// TeamBackdrop.cs). Returns true on success; JS refreshes the backdrop afterward if that
    /// team is currently showing.</summary>
    public async Task<bool> DownloadAndSetTeamBackground(string team, string url) =>
        await _host.DownloadAndSetTeamBackgroundFromWeb(team, url);

    /// <summary>Downloads a marketplace song or image into the local library ("My Downloads")
    /// with a clear "[School] - [Name]" filename. Songs land in the Songs library so they're
    /// immediately assignable to a trigger; images land in a general downloaded-images library.
    /// Returns {success, path?, error?} as JSON rather than a bare bool so the UI can show why a
    /// download failed (network error vs. oversized vs. bad type) instead of a generic failure.</summary>
    public async Task<string> DownloadMarketplaceItem(string type, string name, string school, string url)
    {
        string? path = await MarketplaceDownloadService.DownloadAsync(type, name, school, url);
        return path != null
            ? JsonSerializer.Serialize(new { success = true, path })
            : JsonSerializer.Serialize(new { success = false, error = "Download failed -- check your connection and try again." });
    }

    /// <summary>Lists everything in "My Downloads", newest first. Every entry gets a servable
    /// URL via the "downloadedimages"/"downloadedsongs" virtual host mappings (see
    /// WebMainForm.InitWebViewAsync) -- WebView2's https-loaded page can't play/display a bare
    /// file:// path (mixed-content blocked), so both images and songs need a mapped host the
    /// same way team logos/backgrounds already do.</summary>
    public string GetMyDownloads() => JsonSerializer.Serialize(
        ConfigStore.LoadMarketplaceDownloads()
            .OrderByDescending(e => e.DownloadedAt)
            .Select(e => new
            {
                id = e.Id,
                type = e.Type,
                name = e.Name,
                school = e.School,
                downloadedAt = e.DownloadedAt.ToString("O"),
                fileUrl = (e.Type == "image" ? "https://downloadedimages/" : "https://downloadedsongs/")
                    + Uri.EscapeDataString(Path.GetFileName(e.Path)),
                schoolLogoUrl = LogoUrl(e.School),
            }));

    public bool RemoveMyDownload(string id) => ConfigStore.RemoveMarketplaceDownload(id);

    // ---- Google sign-in (scaffolded -- see GoogleAuthService.ClientId, needs a real Google
    // Cloud OAuth Client ID of type "Desktop app" before this can succeed) ----

    /// <summary>Returns the current local session as JSON ({signedIn:false} if none), or null on
    /// a corrupt/unreadable session file (treated as signed out).</summary>
    public string GetCurrentUser()
    {
        var session = ConfigStore.LoadAuthSession();
        return session == null
            ? JsonSerializer.Serialize(new { signedIn = false })
            : JsonSerializer.Serialize(new { signedIn = true, name = session.Name, email = session.Email, picture = session.Picture });
    }

    /// <summary>Runs the full browser-based Google sign-in flow (see GoogleAuthService), then
    /// exchanges the resulting ID token with the marketplace worker's /auth/verify endpoint for
    /// an app-level session token, and persists both locally. Returns the same shape as
    /// GetCurrentUser on success, or {signedIn:false, error:"..."} on any failure -- the flow
    /// depends on an external browser window and network calls, both of which can fail or be
    /// abandoned by the user, so this must never throw past the WebView2 call boundary.</summary>
    public async Task<string> SignInWithGoogle()
    {
        try
        {
            var profile = await GoogleAuthService.SignInAsync(CancellationToken.None);
            if (profile == null)
                return JsonSerializer.Serialize(new { signedIn = false, error = "Sign-in wasn't completed." });

            using var http = new HttpClient();
            var payload = JsonSerializer.Serialize(new { idToken = profile.IdToken });
            using var response = await http.PostAsync(
                "https://bandroom-marketplace.bandroom.workers.dev/auth/verify",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { signedIn = false, error = "Couldn't verify sign-in with the server." });

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string sessionToken = doc.RootElement.TryGetProperty("sessionToken", out var st) ? st.GetString() ?? "" : "";
            if (sessionToken.Length == 0)
                return JsonSerializer.Serialize(new { signedIn = false, error = "Server didn't return a session." });

            ConfigStore.SaveAuthSession(new ConfigStore.AuthSession
            {
                Sub = profile.Sub, Email = profile.Email, Name = profile.Name,
                Picture = profile.Picture, SessionToken = sessionToken,
            });

            // Merge the cloud profile (if this account has one saved from another device/install)
            // with whatever's local -- NEVER a blind overwrite. Lifetime stat counters only ever
            // go up, so taking the max of each is always safe and can never discard real local
            // history (e.g. weeks of anonymous use before a first-ever sign-in, where the cloud
            // side might still hold a stray near-empty profile from a different install reusing
            // this same Google account). Favorite team prefers whichever device set one; local
            // wins ties since it's the device the user is on right now.
            var localProfile = ConfigStore.LoadUserProfile();
            var cloudProfile = await ProfileSyncService.PullAsync(sessionToken);
            var merged = cloudProfile == null ? localProfile : new ConfigStore.UserProfile
            {
                FavoriteTeam = localProfile.FavoriteTeam ?? cloudProfile.FavoriteTeam,
                GamesWatched = Math.Max(localProfile.GamesWatched, cloudProfile.GamesWatched),
                SongsTriggered = Math.Max(localProfile.SongsTriggered, cloudProfile.SongsTriggered),
                MarketplaceUploads = Math.Max(localProfile.MarketplaceUploads, cloudProfile.MarketplaceUploads),
                MarketplaceDownloads = Math.Max(localProfile.MarketplaceDownloads, cloudProfile.MarketplaceDownloads),
            };
            ConfigStore.SaveUserProfile(merged);
            _ = ProfileSyncService.PushAsync(merged); // write the merged result back so both sides agree

            return JsonSerializer.Serialize(new { signedIn = true, name = profile.Name, email = profile.Email, picture = profile.Picture });
        }
        catch (Exception ex)
        {
            CrashLog.Write("SignInWithGoogle failed", ex);
            return JsonSerializer.Serialize(new { signedIn = false, error = "Sign-in failed -- try again." });
        }
    }

    public void SignOutOfGoogle() => ConfigStore.ClearAuthSession();

    // ---- Universal profile (favorite team + lifetime stats) --------------------------------
    // Distinct from the per-team "Save Profile" feature (ConfigProfileManager), which saves
    // song-to-situation assignments for ONE team. This is one record per install, always saved
    // locally so it works fully signed-out, and mirrored to the cloud when signed in with Google
    // (see ProfileSyncService) so it can follow the account across devices.

    public string GetUserProfile()
    {
        var p = ConfigStore.LoadUserProfile();
        return JsonSerializer.Serialize(new
        {
            favoriteTeam = p.FavoriteTeam,
            gamesWatched = p.GamesWatched,
            songsTriggered = p.SongsTriggered,
            marketplaceUploads = p.MarketplaceUploads,
            marketplaceDownloads = p.MarketplaceDownloads,
        });
    }

    public void SetFavoriteTeam(string team)
    {
        var updated = ConfigStore.LoadUserProfile() with { FavoriteTeam = team };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    /// <summary>Called from app.js right after a marketplace upload actually succeeds (not on
    /// every attempt) -- bumps the local lifetime counter and mirrors it to the cloud profile if
    /// signed in.</summary>
    public void RecordMarketplaceUpload()
    {
        var current = ConfigStore.LoadUserProfile();
        var updated = current with { MarketplaceUploads = current.MarketplaceUploads + 1 };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    /// <summary>Same as RecordMarketplaceUpload but for a completed "My Downloads" pull.</summary>
    public void RecordMarketplaceDownload()
    {
        var current = ConfigStore.LoadUserProfile();
        var updated = current with { MarketplaceDownloads = current.MarketplaceDownloads + 1 };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    /// <summary>Saves a user-cropped custom logo (base64 PNG bytes from the web crop tool's
    /// canvas) as <paramref name="team"/>'s logo, replacing any existing one under any of
    /// TeamLogo's recognized extensions -- matches the same "clear stale file under a different
    /// extension" convention TeamBackgroundDownloadService already uses. Any team can be
    /// re-logo'd this way; there's no accounts/ownership system to gate it against (this is a
    /// single-user local app, not the marketplace).</summary>
    public bool SaveCustomTeamLogo(string team, string base64Png)
    {
        if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(base64Png)) return false;
        if (TeamColors.All.All(t => t.Name != team)) return false; // only real roster entries

        try
        {
            byte[] bytes = Convert.FromBase64String(base64Png);
            if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) return false; // sanity cap

            Directory.CreateDirectory(ConfigStore.TeamLogosFolder);
            string safeTeam = System.Text.RegularExpressions.Regex.Replace(team, @"[^\w\s&-]", "").Trim();
            if (safeTeam.Length == 0) return false;

            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            {
                string oldPath = Path.Combine(ConfigStore.TeamLogosFolder, safeTeam + ext);
                if (File.Exists(oldPath)) { try { File.Delete(oldPath); } catch { /* best-effort */ } }
            }

            string outPath = Path.Combine(ConfigStore.TeamLogosFolder, safeTeam + ".png");
            File.WriteAllBytes(outPath, bytes);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"SaveCustomTeamLogo failed for \"{team}\"", ex);
            return false;
        }
    }

    public string ToggleWatching() => _host.ToggleWatchingFromWeb();
    public void OpenSettings() => _host.OpenSettingsFromWeb();
    public void ShowUpdate() => _host.ShowUpdateDialogFromWeb();
    public void RestartForUpdate() => _host.RestartForUpdateFromWeb();
    public void ResetTeamProfile() => _host.ResetTeamProfileFromWeb();
    public void OpenHelp() => _host.OpenHelpFromWeb();
    public void TriggerEffectsTest() => _host.TriggerEffectsTestFromWeb();

    // Triggers actually confirmed live in a real game, not just wired in code. Touchdown/
    // Turnover/Downs/PAT have been play-tested across sessions; Kickoff was just wired to the
    // live possession-color read and hasn't been confirmed live yet; "flag" has no calibrated
    // OCR region at all, so it can never fire. Move an entry here once the user confirms it live.
    static readonly HashSet<string> ConfirmedTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "situation:touchdown",
        "situation:turnover",
        "situation:pat_good",
        "down:1st",
        "down:2nd",
        "down:3rd",
        "down:4th",
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
    public void PlayClickSound() => _host.PlayUiClickSoundFromWeb();
    public bool IsMatchupLocked() => _host.IsMatchupLockedFromWeb();
    public void CopyCurrentToAllTeams() => _host.CopyCurrentToAllTeamsFromWeb();
    public void DeleteCurrentProfile() => _host.DeleteCurrentProfileFromWeb();
    public void ExportProfile() => _host.ExportProfileFromWeb();
    public void ImportProfile() => _host.ImportProfileFromWeb();

    static string ColorHex(System.Drawing.Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
