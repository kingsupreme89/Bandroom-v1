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
        // FIXED: logo/background files are always saved back to the SAME filename (the crop
        // tool overwrites <team>.png in place), so this URL string was identical before and
        // after a crop-and-save. fillTeamSwatch happens to work anyway because it rebuilds a
        // fresh <img> via innerHTML every render, but applyBackground/applyVsBackdrop mutate an
        // existing element's style.backgroundImage/src in place -- setting a CSS/attr property to
        // a string identical to its current value is a browser no-op, so the newly-cropped image
        // silently never displayed until the user switched teams and back (or restarted). A
        // last-write-time query param makes the URL genuinely change whenever the file's content
        // changes, regardless of which DOM-update pattern the caller uses.
        return $"https://teamlogo/{Uri.EscapeDataString(Path.GetFileName(path))}?v={File.GetLastWriteTimeUtc(path).Ticks}";
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
        // FIXED: see the matching comment on LogoUrl() above -- same stable-filename +
        // in-place-DOM-mutation no-op issue, except here nothing else masked it, so a saved
        // background crop never visibly updated at all until a team switch or app restart.
        return $"https://teambg/{Uri.EscapeDataString(Path.GetFileName(path))}?v={File.GetLastWriteTimeUtc(path).Ticks}";
    }

    /// <summary>Downloads a Trophy Room image from the marketplace worker and sets it as
    /// <paramref name="team"/>'s local background (see TeamBackgroundDownloadService /
    /// TeamBackdrop.cs). Returns true on success; JS refreshes the backdrop afterward if that
    /// team is currently showing.</summary>
    public async Task<bool> DownloadAndSetTeamBackground(string team, string url) =>
        await _host.DownloadAndSetTeamBackgroundFromWeb(team, url);

    /// <summary>"Set as Background" from My Downloads -- the image is already local, so this
    /// looks it up by download id and copies it directly instead of re-fetching over HTTP.</summary>
    public bool SetTeamBackgroundFromDownload(string downloadId) => _host.SetTeamBackgroundFromDownloadFromWeb(downloadId);

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
    /// <summary>My Downloads now merges TWO sources: real marketplace downloads (as before) and
    /// tracks the user imported/trimmed themselves via the local-import pipeline (item 21,
    /// ConfigStore.LoadLocalTracks). Local entries get source="local" plus shared/canShare flags
    /// so app.js only ever shows the "Share to Marketplace" button on tracks that came through
    /// that pipeline -- never on an item that's already sourced from the marketplace.</summary>
    public string GetMyDownloads()
    {
        // FIX (Music Library UX Brief v2 §2.3 "My Downloads drift"): this used to trust the
        // manifest blindly -- a file deleted outside the app (disk cleanup, antivirus quarantine,
        // manual delete in Explorer) left a manifest entry that still rendered as a normal,
        // playable/downloadable tile with no indication anything was wrong. Checking File.Exists
        // here on every open makes the list self-healing against the manifest drifting from disk:
        // a missing file is still LISTED (so the user can see it and remove it -- per brief §4,
        // "a broken/missing local file is visibly flagged, not silently absent"), but is flagged
        // via fileExists=false so app.js can disable play/set-as-background and show a clear
        // "missing" state instead of silently trying (and failing) to load a dead file.
        var marketplace = ConfigStore.LoadMarketplaceDownloads().Select(e => new
        {
            id = e.Id,
            type = e.Type,
            name = e.Name,
            school = (string?)e.School,
            downloadedAt = e.DownloadedAt,
            sortAt = e.DownloadedAt,
            fileUrl = (e.Type == "image" ? "https://downloadedimages/" : "https://downloadedsongs/")
                + Uri.EscapeDataString(Path.GetFileName(e.Path)),
            schoolLogoUrl = LogoUrl(e.School),
            source = "marketplace",
            shared = false,
            canShare = false,
            fileExists = File.Exists(e.Path),
        });

        var local = ConfigStore.LoadLocalTracks().Select(e => new
        {
            id = e.Id,
            type = "song",
            name = e.Name,
            school = (string?)null,
            downloadedAt = e.CreatedAt,
            sortAt = e.CreatedAt,
            fileUrl = "https://localtracks/" + Uri.EscapeDataString(Path.GetFileName(e.Path)),
            schoolLogoUrl = (string?)null,
            source = "local",
            shared = e.Shared,
            canShare = !e.Shared,
            fileExists = File.Exists(e.Path),
        });

        var combined = marketplace.Concat(local)
            .OrderByDescending(e => e.sortAt)
            .Select(e => new
            {
                e.id, e.type, e.name, e.school,
                downloadedAt = e.downloadedAt.ToString("O"),
                e.fileUrl, e.schoolLogoUrl, e.source, e.shared, e.canShare, e.fileExists,
            });
        return JsonSerializer.Serialize(combined);
    }

    /// <summary>Handles removal for either "My Downloads" source (see GetMyDownloads) -- tries
    /// the marketplace-download manifest first, then the local-track manifest, since an id only
    /// ever exists in one of the two.</summary>
    public bool RemoveMyDownload(string id) =>
        ConfigStore.RemoveMarketplaceDownload(id) || ConfigStore.RemoveLocalTrack(id);

    /// <summary>End-user "import my own song" pipeline (item 21) -- runs the whole native flow
    /// (choose file, name the track, trim/normalize via the existing TrimmerForm) on the UI
    /// thread since it's all modal dialogs. Returns {success, path?, name?} on completion, or
    /// {success:false, cancelled:true} if the user backed out at any step.</summary>
    public string ImportLocalSong() => _host.ImportLocalSongFromWeb();

    static readonly System.Net.Http.HttpClient ShareHttp = BuildShareHttpClient();
    static System.Net.Http.HttpClient BuildShareHttpClient()
    {
        var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Bandroom", "1.0"));
        return http;
    }

    /// <summary>Uploads a locally-imported track (item 21) to the marketplace worker, using the
    /// exact same request shape (multipart: type/name/school/file) a normal in-album upload
    /// sends from app.js's confirmUpload -- so it shows up in that school's Sound Bank exactly
    /// like any other upload. Opt-in only: never called automatically, only from the "Share to
    /// Marketplace" button on a My Downloads tile whose source is "local". Marks the track
    /// Shared on success so the button doesn't re-offer a duplicate upload.</summary>
    public async Task<string> ShareLocalTrackToMarketplace(string id, string school)
    {
        var entry = ConfigStore.LoadLocalTracks().FirstOrDefault(e => e.Id == id);
        if (entry == null || !File.Exists(entry.Path))
            return JsonSerializer.Serialize(new { success = false, error = "That track couldn't be found." });
        if (string.IsNullOrWhiteSpace(school))
            return JsonSerializer.Serialize(new { success = false, error = "A team/school name is required." });

        try
        {
            // BUG FIX (Session 11, task 1 verification): this used to hardcode "song" here
            // regardless of entry.Type, so any track recorded as "pa" (see LocalTrackEntry.Type --
            // set from PromptDialog.ShowTrackNaming's Song/PA choice, see ImportLocalSongFromWeb
            // above) got uploaded to the marketplace mis-tagged as a plain song. That silently
            // mis-indexes it: it'd show up in the Songs shelf/list instead of wherever PA clips
            // are meant to be filtered to, with no error anywhere since the worker's isValidType
            // accepts "pa" just as happily as "song" -- the bug was purely in which string got
            // sent, not a rejected upload. entry.Type is only ever "song" or "pa" (see its own
            // doc comment) so this is safe without extra validation here.
            using var form = new System.Net.Http.MultipartFormDataContent();
            form.Add(new System.Net.Http.StringContent(entry.Type), "type");
            form.Add(new System.Net.Http.StringContent(entry.Name), "name");
            form.Add(new System.Net.Http.StringContent(school), "school");
            var bytes = await File.ReadAllBytesAsync(entry.Path);
            var fileContent = new System.Net.Http.ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "file", Path.GetFileName(entry.Path));

            using var response = await ShareHttp.PostAsync(
                "https://bandroom-marketplace.bandroom.workers.dev/upload", form);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });

            ConfigStore.MarkLocalTrackShared(id);
            return JsonSerializer.Serialize(new { success = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write($"ShareLocalTrackToMarketplace failed for \"{entry.Name}\"", ex);
            return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });
        }
    }

    /// <summary>Marketplace song uploads (app.js's "+ Upload Song" tile in a team's Sound Bank)
    /// went straight from file picker to the worker with no trim step -- every other song path
    /// in the app (local import, AssignTrackForm) goes through TrimmerForm first. Reuses that
    /// exact same native pick/name/trim/normalize pipeline (ImportLocalSongFromWeb) instead of
    /// building a second one, then shares the resulting trimmed local track straight to
    /// <paramref name="school"/> via the existing ShareLocalTrackToMarketplace path -- so a
    /// marketplace upload now ends up trimmed/normalized exactly like a locally-kept track, and
    /// also lands in My Downloads since ImportLocalSongFromWeb registers it there regardless.</summary>
    public async Task<string> ImportAndUploadSongToMarketplace(string school)
    {
        if (string.IsNullOrWhiteSpace(school))
            return JsonSerializer.Serialize(new { success = false, error = "No team selected." });

        var raw = _host.ImportLocalSongFromWeb();
        using var import = JsonDocument.Parse(raw);
        var root = import.RootElement;
        if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
            return raw; // cancelled or failed at the pick/name/trim stage -- same shape either way

        var path = root.GetProperty("path").GetString();
        var entry = ConfigStore.LoadLocalTracks().FirstOrDefault(e => e.Path == path);
        if (entry == null)
            return JsonSerializer.Serialize(new { success = false, error = "Track was trimmed but couldn't be found for upload." });

        return await ShareLocalTrackToMarketplace(entry.Id, school);
    }

    // ---- Profile sharing ("Load Profile from Others") -----------------------------------------
    // Shares/loads a whole team's trigger->song assignment MAP, not audio bytes -- the uploaded
    // JSON only ever contains trigger/event/filename, never a full local path (those are
    // machine-specific and meaningless on someone else's PC). Applying a downloaded profile
    // works by filename match against the applier's OWN Songs library (same portability model
    // Export/Import already use) -- entries with no local match are reported back and left
    // Unassigned rather than silently guessed at. This is real, working auto-assign-by-filename
    // for the common "everyone's using the same default pack + similarly-named uploads" case,
    // not a fake button -- but it can't summon audio files that don't exist on this machine.
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
            using var form = new System.Net.Http.MultipartFormDataContent();
            form.Add(new System.Net.Http.StringContent("profile"), "type");
            form.Add(new System.Net.Http.StringContent($"{team} profile ({entries.Count} songs)"), "name");
            form.Add(new System.Net.Http.StringContent(team), "school");
            var fileContent = new System.Net.Http.ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(json));
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            form.Add(fileContent, "file", $"{team}-profile.json");

            using var response = await ShareHttp.PostAsync(
                "https://bandroom-marketplace.bandroom.workers.dev/upload", form);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });

            return JsonSerializer.Serialize(new { success = true, count = entries.Count });
        }
        catch (Exception ex)
        {
            CrashLog.Write($"ShareCurrentProfileToMarketplace failed for \"{team}\"", ex);
            return JsonSerializer.Serialize(new { success = false, error = "Upload failed -- check your connection and try again." });
        }
    }

    /// <summary>Lists profiles other people have shared for a given school -- backs the
    /// "Load Profile from Others" pill.</summary>
    public async Task<string> GetMarketplaceProfiles(string school)
    {
        try
        {
            using var response = await ShareHttp.GetAsync(
                $"https://bandroom-marketplace.bandroom.workers.dev/list?type=profile&school={Uri.EscapeDataString(school)}&sort=newest");
            if (!response.IsSuccessStatusCode) return "{\"items\":[]}";
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write($"GetMarketplaceProfiles failed for \"{school}\"", ex);
            return "{\"items\":[]}";
        }
    }

    /// <summary>Downloads a shared profile and applies whatever it can match by filename against
    /// this machine's own Songs library (see class-level comment above). Returns
    /// {success, applied, total, unmatched:[eventName,...]} so the UI can tell the user exactly
    /// what got auto-assigned and what still needs a manual upload.</summary>
    public async Task<string> ApplyMarketplaceProfile(string fileUrl)
    {
        try
        {
            using var response = await ShareHttp.GetAsync(fileUrl);
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
        catch (Exception ex)
        {
            CrashLog.Write("ApplyMarketplaceProfile failed", ex);
            return JsonSerializer.Serialize(new { success = false, error = "Couldn't apply that profile -- try again." });
        }
    }

    // ---- Admin marketplace override (owner-only, never shipped to end users) -----------------
    // Unlike google_client_secret.local.txt, this file is deliberately NOT added as a
    // <Content Include> in BandAudioHook.csproj and never copied into the build output. It's
    // read straight from this fixed source-checkout path, which only ever exists on the app
    // owner's own dev machine -- a normal Squirrel-installed copy on an end user's machine has
    // no such path and no such file, so AdminTokenPath simply doesn't exist there and every
    // method below silently no-ops. If a future session "fixes" this by wiring the file into
    // the csproj like the Google secret, it would ship the admin bypass secret inside every
    // installer, letting any user extract it and impersonate the admin against worker.js's
    // X-Admin-Token check -- do not do that.
    const string AdminTokenPath = @"D:\Claude\Projects\tools\BandAudioHook\admin_token.local.txt";
    static readonly string? _adminToken = LoadAdminToken();

    static string? LoadAdminToken()
    {
        try
        {
            if (!File.Exists(AdminTokenPath)) return null;
            var token = File.ReadAllText(AdminTokenPath).Trim();
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch (Exception ex)
        {
            CrashLog.Write("LoadAdminToken failed", ex);
            return null;
        }
    }

    /// <summary>Whether this build/machine has admin capability -- true only when
    /// admin_token.local.txt exists next to the source checkout (see AdminTokenPath comment
    /// above). app.js gates the admin edit/delete UI on this so regular users never see it.</summary>
    public bool IsAdminMode() => _adminToken != null;

    /// <summary>Admin-only delete that bypasses the ownerToken check on worker.js's DELETE
    /// /item/&lt;type&gt;/&lt;id&gt; via X-Admin-Token, for items this device never uploaded (the
    /// normal per-owner delete in app.js's deleteUploadedItem still handles your own uploads).
    /// No-ops with an error if IsAdminMode() is false.</summary>
    public async Task<string> AdminDeleteMarketplaceItem(string type, string id)
    {
        if (_adminToken == null)
            return JsonSerializer.Serialize(new { success = false, error = "Admin mode is not active." });

        try
        {
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Delete,
                $"https://bandroom-marketplace.bandroom.workers.dev/item/{type}/{Uri.EscapeDataString(id)}");
            request.Headers.Add("X-Admin-Token", _adminToken);
            using var response = await ShareHttp.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"Delete failed: {(int)response.StatusCode}" });
            return JsonSerializer.Serialize(new { success = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write($"AdminDeleteMarketplaceItem failed for {type}/{id}", ex);
            return JsonSerializer.Serialize(new { success = false, error = "Delete failed -- check your connection and try again." });
        }
    }

    /// <summary>Admin-only edit of an item's name/school via worker.js's admin-exclusive PATCH
    /// /item/&lt;type&gt;/&lt;id&gt; (no ownerToken fallback there -- X-Admin-Token or nothing).
    /// No-ops with an error if IsAdminMode() is false.</summary>
    public async Task<string> AdminEditMarketplaceItem(string type, string id, string newName, string newSchool)
    {
        if (_adminToken == null)
            return JsonSerializer.Serialize(new { success = false, error = "Admin mode is not active." });

        try
        {
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Patch,
                $"https://bandroom-marketplace.bandroom.workers.dev/item/{type}/{Uri.EscapeDataString(id)}");
            request.Headers.Add("X-Admin-Token", _adminToken);
            request.Content = new System.Net.Http.StringContent(
                JsonSerializer.Serialize(new { name = newName, school = newSchool }),
                System.Text.Encoding.UTF8, "application/json");
            using var response = await ShareHttp.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"Edit failed: {(int)response.StatusCode}" });
            return JsonSerializer.Serialize(new { success = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write($"AdminEditMarketplaceItem failed for {type}/{id}", ex);
            return JsonSerializer.Serialize(new { success = false, error = "Edit failed -- check your connection and try again." });
        }
    }

    // ---- Google sign-in (scaffolded -- see GoogleAuthService.ClientId, needs a real Google
    // Cloud OAuth Client ID of type "Desktop app" before this can succeed) ----

    /// <summary>Returns the current local session as JSON ({signedIn:false} if none), or null on
    /// a corrupt/unreadable session file (treated as signed out).</summary>
    public string GetCurrentUser()
    {
        var session = ConfigStore.LoadAuthSession();
        return session == null
            ? JsonSerializer.Serialize(new { signedIn = false })
            : JsonSerializer.Serialize(new
            {
                signedIn = true, name = session.Name, email = session.Email, picture = session.Picture,
                signedInAt = session.SignedInAt.ToString("O"),
            });
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
                // The most common real cause of a silent timeout here (browser opened, never
                // redirects back) is the OAuth consent screen still being in "Testing" mode --
                // Google shows the user its own "Access blocked" page and never calls back to our
                // local listener, so from here it's indistinguishable from "user closed the tab".
                // Naming the likely cause beats a generic message that makes a real user think
                // the feature is just broken.
                return JsonSerializer.Serialize(new { signedIn = false, error = "Sign-in didn't complete. If your Google account isn't added as a test user yet, Google blocks sign-in silently -- check the OAuth consent screen's test user list, or close the browser tab and try again." });

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
                // Identity/preference fields: local wins if set, cloud fills in only if local is
                // empty -- never let signing in on a second device silently overwrite a choice
                // already made on this one.
                FavoriteTeam = localProfile.FavoriteTeam ?? cloudProfile.FavoriteTeam,
                RivalTeam = localProfile.RivalTeam ?? cloudProfile.RivalTeam,
                Bio = localProfile.Bio ?? cloudProfile.Bio,
                // Lifetime counters only ever go up -- max() is always safe, can never discard
                // real local history.
                GamesWatched = Math.Max(localProfile.GamesWatched, cloudProfile.GamesWatched),
                SongsTriggered = Math.Max(localProfile.SongsTriggered, cloudProfile.SongsTriggered),
                MarketplaceUploads = Math.Max(localProfile.MarketplaceUploads, cloudProfile.MarketplaceUploads),
                MarketplaceDownloads = Math.Max(localProfile.MarketplaceDownloads, cloudProfile.MarketplaceDownloads),
                FavoriteTeamWins = Math.Max(localProfile.FavoriteTeamWins, cloudProfile.FavoriteTeamWins),
                FavoriteTeamLosses = Math.Max(localProfile.FavoriteTeamLosses, cloudProfile.FavoriteTeamLosses),
                StreakCurrentDays = Math.Max(localProfile.StreakCurrentDays, cloudProfile.StreakCurrentDays),
                // Per-key dictionaries: union of both sides, max value per key -- same reasoning
                // as the plain counters above, just applied per-entry instead of to one total.
                EventCounts = MergeCounts(localProfile.EventCounts, cloudProfile.EventCounts),
                GamesWatchedByTeam = MergeCounts(localProfile.GamesWatchedByTeam, cloudProfile.GamesWatchedByTeam),
                // Custom team logos are NOT counters -- a device could legitimately re-crop an
                // older-looking logo, which would look like "going backwards" to a max-of-value
                // merge. Newest UpdatedAtUtc per team wins instead (see MergeLatestWins).
                CustomTeamLogos = MergeLatestWins(localProfile.CustomTeamLogos, cloudProfile.CustomTeamLogos),
                // Local-only, deliberately not touched by the merge: StreakLastActiveDate,
                // FavoriteTeam avatar file, ToastsEnabled, CreatedAt (see ProfileSyncService's
                // class-level comment for why each of these stays per-device).
                StreakLastActiveDate = localProfile.StreakLastActiveDate,
                ToastsEnabled = localProfile.ToastsEnabled,
                AvatarFileName = localProfile.AvatarFileName,
                CreatedAt = localProfile.CreatedAt,
            };
            ConfigStore.SaveUserProfile(merged);
            _ = ProfileSyncService.PushAsync(merged); // write the merged result back so both sides agree

            // Write any team logo the merge picked up that isn't already on disk here yet, and
            // collect which teams actually changed -- suppressed (see ApplyPulledLogos) on this
            // device's very first-ever sync so a brand-new device pulling down many pre-existing
            // custom logos doesn't spam a toast per team.
            string[] logosUpdated = ApplyPulledLogos(merged.CustomTeamLogos);

            return JsonSerializer.Serialize(new
            {
                signedIn = true, name = profile.Name, email = profile.Email, picture = profile.Picture,
                logosUpdated,
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("SignInWithGoogle failed", ex);
            return JsonSerializer.Serialize(new { signedIn = false, error = "Sign-in failed -- try again." });
        }
    }

    public void SignOutOfGoogle() => ConfigStore.ClearAuthSession();

    /// <summary>Union of two count dictionaries, max value per shared key -- see the sign-in
    /// merge above for why (same "counters only go up" reasoning as the plain numeric fields,
    /// applied per-entry).</summary>
    static Dictionary<string, int> MergeCounts(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        var result = new Dictionary<string, int>(a);
        foreach (var (key, value) in b)
            result[key] = Math.Max(result.GetValueOrDefault(key), value);
        return result;
    }

    /// <summary>Union of two per-team logo dictionaries, keeping whichever side's entry has the
    /// newer UpdatedAtUtc per key -- unlike MergeCounts above, a logo edit isn't a counter, so
    /// "bigger wins" doesn't apply; "most recent edit wins" does. Ties keep <paramref name="a"/>
    /// (the local side, same tie-break convention as the rest of the sign-in merge).</summary>
    static Dictionary<string, ConfigStore.TeamLogoEntry> MergeLatestWins(
        Dictionary<string, ConfigStore.TeamLogoEntry> a, Dictionary<string, ConfigStore.TeamLogoEntry> b)
    {
        var result = new Dictionary<string, ConfigStore.TeamLogoEntry>(a);
        foreach (var (key, value) in b)
            if (!result.TryGetValue(key, out var existing) || value.UpdatedAtUtc > existing.UpdatedAtUtc)
                result[key] = value;
        return result;
    }

    /// <summary>Writes any merged CustomTeamLogos entry that's newer than what this device has
    /// already applied (per the team_logo_sync.json sidecar manifest) to TeamLogosFolder, so a
    /// logo edited on another device shows up here too. Returns the team names that actually
    /// changed as a result -- empty on this device's first-ever sync (files are still written,
    /// just not reported, so a brand-new device pulling down a profile with many pre-existing
    /// custom logos doesn't spam a "Logo updated" toast per team) or when nothing changed.</summary>
    static string[] ApplyPulledLogos(Dictionary<string, ConfigStore.TeamLogoEntry> merged)
    {
        var manifest = ConfigStore.LoadTeamLogoSyncManifest();
        var appliedAt = new Dictionary<string, DateTime>(manifest.AppliedAtUtc);
        var changed = new List<string>();

        foreach (var (team, entry) in merged)
        {
            if (appliedAt.TryGetValue(team, out var already) && already >= entry.UpdatedAtUtc) continue;
            try
            {
                byte[] bytes = Convert.FromBase64String(entry.Base64Png);
                string? outPath = WriteTeamLogoFile(team, bytes);
                if (outPath == null) continue;
                appliedAt[team] = entry.UpdatedAtUtc;
                changed.Add(team);
            }
            catch (Exception ex)
            {
                CrashLog.Write($"ApplyPulledLogos failed for \"{team}\"", ex);
            }
        }

        bool isFirstSync = !manifest.InitialSyncDone;
        ConfigStore.SaveTeamLogoSyncManifest(manifest with { AppliedAtUtc = appliedAt, InitialSyncDone = true });
        return isFirstSync ? Array.Empty<string>() : changed.ToArray();
    }

    /// <summary>Shared by SaveCustomTeamLogo and ApplyPulledLogos -- clears any stale file for
    /// <paramref name="team"/> under another recognized extension, then writes the PNG. Returns the
    /// path written, or null if the team name doesn't survive sanitization.</summary>
    static string? WriteTeamLogoFile(string team, byte[] pngBytes)
    {
        Directory.CreateDirectory(ConfigStore.TeamLogosFolder);
        string safeTeam = System.Text.RegularExpressions.Regex.Replace(team, @"[^\w\s&-]", "").Trim();
        if (safeTeam.Length == 0) return null;

        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            string oldPath = Path.Combine(ConfigStore.TeamLogosFolder, safeTeam + ext);
            if (File.Exists(oldPath)) { try { File.Delete(oldPath); } catch { /* best-effort */ } }
        }

        string outPath = Path.Combine(ConfigStore.TeamLogosFolder, safeTeam + ".png");
        File.WriteAllBytes(outPath, pngBytes);
        return outPath;
    }

    // ---- Universal profile (favorite team + lifetime stats) --------------------------------
    // Distinct from the per-team "Save Profile" feature (ConfigProfileManager), which saves
    // song-to-situation assignments for ONE team. This is one record per install, always saved
    // locally so it works fully signed-out, and mirrored to the cloud when signed in with Google
    // (see ProfileSyncService) so it can follow the account across devices.

    public string GetUserProfile()
    {
        var p = ConfigStore.LoadUserProfile();

        string? topEvent = null;
        int topEventCount = 0;
        foreach (var (evt, count) in p.EventCounts)
            if (count > topEventCount) { topEvent = evt; topEventCount = count; }

        // A fun number, not a precise science -- rewards a mix of watching, triggering, and
        // contributing to the marketplace rather than any single stat dominating.
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
            favoriteTeam = p.FavoriteTeam,
            rivalTeam = p.RivalTeam,
            bio = p.Bio,
            gamesWatched = p.GamesWatched,
            songsTriggered = p.SongsTriggered,
            marketplaceUploads = p.MarketplaceUploads,
            marketplaceDownloads = p.MarketplaceDownloads,
            gamesWatchedByTeam = p.GamesWatchedByTeam,
            mostTriggeredEvent = topEvent,
            mostTriggeredCount = topEventCount,
            streakCurrentDays = p.StreakCurrentDays,
            favoriteTeamWins = p.FavoriteTeamWins,
            favoriteTeamLosses = p.FavoriteTeamLosses,
            createdAt = p.CreatedAt.ToString("O"),
            toastsEnabled = p.ToastsEnabled,
            avatarUrl = p.AvatarFileName != null ? "https://avatar/" + Uri.EscapeDataString(p.AvatarFileName) : null,
            level,
            achievements,
        });
    }

    public void SetFavoriteTeam(string team)
    {
        var updated = ConfigStore.LoadUserProfile() with { FavoriteTeam = string.IsNullOrWhiteSpace(team) ? null : team };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    public void SetRivalTeam(string team)
    {
        var updated = ConfigStore.LoadUserProfile() with { RivalTeam = string.IsNullOrWhiteSpace(team) ? null : team };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    public void SetBio(string bio)
    {
        string trimmed = (bio ?? "").Trim();
        if (trimmed.Length > 140) trimmed = trimmed[..140]; // short tagline, not an essay
        var updated = ConfigStore.LoadUserProfile() with { Bio = trimmed.Length == 0 ? null : trimmed };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    public void SetToastsEnabled(bool enabled)
    {
        var updated = ConfigStore.LoadUserProfile() with { ToastsEnabled = enabled };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    /// <summary>Manually recorded result for the favorite team's game -- Bandroom has no way to
    /// OCR a final score, so this is user-reported via a button in the Profile tab, not
    /// auto-detected from GAMETIME/gameplay.</summary>
    public void RecordFavoriteTeamResult(bool win)
    {
        var current = ConfigStore.LoadUserProfile();
        var updated = win
            ? current with { FavoriteTeamWins = current.FavoriteTeamWins + 1 }
            : current with { FavoriteTeamLosses = current.FavoriteTeamLosses + 1 };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    /// <summary>Resets lifetime STATS only (games/songs/uploads/downloads/streak/event-counts/
    /// win-loss) -- deliberately keeps favorite team, rival, bio, and avatar, since those are
    /// identity choices, not counters. Distinct from ResetTeamProfile, which resets a single
    /// team's song-to-situation assignments and has nothing to do with the universal profile.</summary>
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

    /// <summary>Saves a base64 PNG as the local custom avatar (shown instead of the Google
    /// picture, and available signed-out too) -- same crop-tool-output convention as
    /// SaveCustomTeamLogo below, just a single fixed file rather than one per team.</summary>
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
        catch (Exception ex)
        {
            CrashLog.Write("UploadAvatar failed", ex);
            return false;
        }
    }

    /// <summary>Exports the whole universal profile (favorite team, stats, bio, everything) as a
    /// standalone JSON file via a native Save dialog -- separate from ConfigProfileManager's
    /// per-team export, which only covers song-to-situation assignments for one team.</summary>
    public void ExportUserProfile() => _host.ExportUserProfileFromWeb();

    public void ImportUserProfile() => _host.ImportUserProfileFromWeb();

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

    /// <summary>Saves a user-cropped custom BACKGROUND (base64 PNG bytes from the web crop tool's
    /// 16:9 canvas, see openBackgroundCropTool in app.js) as <paramref name="team"/>'s backdrop
    /// image, replacing any existing one under any of TeamBackdrop.cs's recognized extensions --
    /// same "clear stale file under a different extension" convention TeamBackgroundDownloadService
    /// already uses for marketplace-downloaded backgrounds. This is the only in-app path for a
    /// user to upload their OWN background image (as opposed to downloading one from the
    /// marketplace's Trophy Room via DownloadAndSetTeamBackground). No cloud sync -- unlike
    /// CustomTeamLogos, backgrounds aren't part of the cross-device profile sync triangle, so this
    /// only ever writes to local disk.</summary>
    public bool SaveCustomTeamBackground(string team, string base64Png)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(base64Png)) return false;
            if (TeamColors.All.All(t => t.Name != team)) return false; // only real roster entries

            byte[] bytes = Convert.FromBase64String(base64Png);
            if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) return false; // sanity cap

            Directory.CreateDirectory(ConfigStore.TeamBackgroundsFolder);
            string safeTeam = System.Text.RegularExpressions.Regex.Replace(team, @"[^\w\s&-]", "").Trim();
            if (safeTeam.Length == 0) return false;

            // Keep this extension list in sync with TeamBackdrop.cs's own Extensions -- FindImagePath
            // only looks for these, so leaving a stale file under one of them (from a prior
            // marketplace-downloaded background, say) would get found first instead of this new one.
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
            {
                string oldPath = Path.Combine(ConfigStore.TeamBackgroundsFolder, safeTeam + ext);
                if (File.Exists(oldPath)) { try { File.Delete(oldPath); } catch { /* best-effort */ } }
            }

            string outPath = Path.Combine(ConfigStore.TeamBackgroundsFolder, safeTeam + ".png");
            File.WriteAllBytes(outPath, bytes);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"SaveCustomTeamBackground failed for \"{team}\"", ex);
            return false;
        }
    }

    /// <summary>Saves a user-cropped custom logo (base64 PNG bytes from the web crop tool's
    /// canvas) as <paramref name="team"/>'s logo, replacing any existing one under any of
    /// TeamLogo's recognized extensions -- matches the same "clear stale file under a different
    /// extension" convention TeamBackgroundDownloadService already uses. Any team can be
    /// re-logo'd this way; there's no accounts/ownership system to gate it against (this is a
    /// single-user local app, not the marketplace).</summary>
    /// <summary>Returns JSON {ok, pushFailed} rather than a plain bool now -- ok is the LOCAL
    /// disk-write result (unchanged behavior, still works fully offline/signed-out); pushFailed is
    /// only ever true when signed in AND the cloud push specifically failed, so app.js can toast
    /// the two failure modes differently ("couldn't save" vs. "saved locally but couldn't sync")
    /// without a push failure ever blocking or rolling back the local save, which already
    /// succeeded by the time a push is even attempted.</summary>
    public async Task<string> SaveCustomTeamLogo(string team, string base64Png)
    {
        bool ok = false;
        bool pushFailed = false;
        try
        {
            if (string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(base64Png))
                return JsonSerializer.Serialize(new { ok, pushFailed });
            if (TeamColors.All.All(t => t.Name != team))
                return JsonSerializer.Serialize(new { ok, pushFailed }); // only real roster entries

            byte[] bytes = Convert.FromBase64String(base64Png);
            if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024)
                return JsonSerializer.Serialize(new { ok, pushFailed }); // sanity cap

            if (WriteTeamLogoFile(team, bytes) == null) return JsonSerializer.Serialize(new { ok, pushFailed });
            ok = true;

            var updatedAt = DateTime.UtcNow;
            var current = ConfigStore.LoadUserProfile();
            var newLogos = new Dictionary<string, ConfigStore.TeamLogoEntry>(current.CustomTeamLogos)
            {
                [team] = new ConfigStore.TeamLogoEntry { Base64Png = base64Png, UpdatedAtUtc = updatedAt },
            };
            var updated = current with { CustomTeamLogos = newLogos };
            ConfigStore.SaveUserProfile(updated);

            // This device just became the source of truth for this team's logo -- record it as
            // already-applied so a later pull of this same edit (echoed back from the cloud) never
            // re-writes the file it just wrote or toasts about a "change" that originated here.
            var manifest = ConfigStore.LoadTeamLogoSyncManifest();
            var appliedAt = new Dictionary<string, DateTime>(manifest.AppliedAtUtc) { [team] = updatedAt };
            ConfigStore.SaveTeamLogoSyncManifest(manifest with { AppliedAtUtc = appliedAt });

            if (ConfigStore.LoadAuthSession() != null)
                pushFailed = !await ProfileSyncService.PushAsync(updated);

            return JsonSerializer.Serialize(new { ok, pushFailed });
        }
        catch (Exception ex)
        {
            CrashLog.Write($"SaveCustomTeamLogo failed for \"{team}\"", ex);
            return JsonSerializer.Serialize(new { ok, pushFailed });
        }
    }

    public string ToggleWatching() => _host.ToggleWatchingFromWeb();
    public void OpenSettings() => _host.OpenSettingsFromWeb();
    public void ShowHelp() => _host.OpenHelpFromWeb();
    public void ShowUpdate() => _host.ShowUpdateDialogFromWeb();

    /// <summary>True once a default song pack (bundled with an older full installer, or
    /// downloaded via DefaultSongPackService) is actually present on disk -- the app-side
    /// signal for whether to show the one-time "download the song pack?" prompt at all.</summary>
    public bool HasDefaultSongPack() => ConfigStore.HasDefaultSongPack;

    public void DownloadDefaultSongPack() => _host.DownloadDefaultSongPackFromWeb();

    /// <summary>Lets the user point the app at the pack .zip they just downloaded from the
    /// Drive link (btn-songpack-download opens that externally, so the app has no other way to
    /// know the download finished). Null if they cancel the picker.</summary>
    public string? BrowseForSongPackZip() => _host.BrowseForSongPackZipFromWeb();

    public void ImportDefaultSongPackZip(string zipPath) => _host.ImportDefaultSongPackZipFromWeb(zipPath);

    /// <summary>Folder-flavored counterparts to BrowseForSongPackZip/ImportDefaultSongPackZip --
    /// for a user who already extracted the pack, or was handed a folder instead of a .zip.</summary>
    public string? BrowseForSongPackFolder() => _host.BrowseForSongPackFolderFromWeb();

    public void ImportDefaultSongPackFolder(string folderPath) => _host.ImportDefaultSongPackFolderFromWeb(folderPath);

    /// <summary>Current on-disk location of the default song pack (task queue item 7a, Session
    /// 10) -- shown in-app so it's actually clear where these files land, instead of that only
    /// being documented in code comments nobody but a developer ever reads.</summary>
    public string GetDefaultSongsFolderPath() => ConfigStore.DownloadedDefaultSongsFolder;

    /// <summary>Task queue item 7b (Session 10): lets the user move the default song pack to a
    /// different drive/folder (e.g. off a small C: drive) instead of it being permanently stuck
    /// under AppData. Native FolderBrowserDialog (same reasoning as every other filesystem picker
    /// in this class -- a web &lt;input type=file&gt; can't hand back a real directory path), then
    /// ConfigStore.SetDefaultSongsFolderOverride does the actual move + persists the new location
    /// so GetDefaultPackTeams/ImportDefaultPackForTeam keep finding songs correctly afterward.
    /// Returns JSON {success, path, error} rather than a bare bool -- "cancelled the picker" and
    /// "picked a folder but the move failed" need different toast copy on the JS side.</summary>
    public string RelocateDefaultSongsFolder()
    {
        string? chosen = _host.BrowseForFolderFromWeb("Choose where to keep the default song pack");
        if (chosen == null)
            return JsonSerializer.Serialize(new { success = false, cancelled = true });

        bool ok = ConfigStore.SetDefaultSongsFolderOverride(chosen);
        return ok
            ? JsonSerializer.Serialize(new { success = true, path = ConfigStore.DownloadedDefaultSongsFolder })
            : JsonSerializer.Serialize(new { success = false, error = "That folder already has other files in it -- pick an empty folder, or the pack's current location, and try again." });
    }
    public void RestartForUpdate() => _host.RestartForUpdateFromWeb();
    public void ResetTeamProfile() => _host.ResetTeamProfileFromWeb();
    public void OpenHelp() => _host.OpenHelpFromWeb();
    public void OpenExternalUrl(string url) => _host.OpenExternalUrlFromWeb(url);
    public void TriggerEffectsTest() => _host.TriggerEffectsTestFromWeb();
    public string FireTestEvent(string side, string eventKey) => _host.FireTestEventFromWeb(side, eventKey);
    public void UnlockMatchup() => _host.UnlockMatchupFromWeb();
    public string GetAllEventKeys() => _host.GetAllEventKeysFromWeb();

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
            paFileName = string.IsNullOrWhiteSpace(e.PaAudioFile) ? null : Path.GetFileNameWithoutExtension(e.PaAudioFile),
            confirmed = ConfirmedTriggers.Contains(e.Trigger),
        }));

    public void AssignEvent(string trigger) => _host.OpenAssignTrackFromWeb(trigger);
    public void AssignPaEvent(string trigger) => _host.OpenAssignPaTrackFromWeb(trigger);
    public void PreviewEvent(string trigger) => _host.PreviewEventFromWeb(trigger);
    public void StopPreview() => _host.StopPreviewFromWeb();

    // Clipping-island assign flow -- replaces the native AssignTrackForm popup for the main-song
    // assign path (see initClipperIsland in app.js). Trim still opens the real TrimmerForm.
    public string GetTrackLibrary() => _host.GetTrackLibraryFromWeb();
    public string GetDefaultPackSongsForTeam(string teamName) => _host.GetDefaultPackSongsForTeamFromWeb(teamName);
    public void PreviewLocalFile(string path) => _host.PreviewLocalFileFromWeb(path);
    public void PlaySoundboardSlot(string key, string path) => _host.PlaySoundboardSlotFromWeb(key, path);
    public Task<string?> ScanDynastySave() => _host.ScanDynastySaveFromWeb();
    public void AssignTrackFile(string trigger, bool isPa, string path) => _host.AssignTrackFileFromWeb(trigger, isPa, path);
    public void ClearTrackAssignment(string trigger, bool isPa) => _host.ClearTrackAssignmentFromWeb(trigger, isPa);
    public bool AddLibraryFileToDownloads(string path) => _host.AddLibraryFileToDownloadsFromWeb(path);
    public string? BrowseForAudioFile() => _host.BrowseForAudioFileFromWeb();
    public void OpenTrimmer(string trigger, bool isPa) => _host.OpenTrimmerFromWeb(trigger, isPa);
    public int GetEventVolume(string trigger) => _host.GetEventVolumeFromWeb(trigger);
    public void SetEventVolume(string trigger, int percent) => _host.SetEventVolumeFromWeb(trigger, percent);

    public void SetVolume(int percent) => _host.SetVolumeFromWeb(percent);
    public void SetHomeVolume(int percent) => _host.SetHomeVolumeFromWeb(percent);
    public void SetAwayVolume(int percent) => _host.SetAwayVolumeFromWeb(percent);
    public int GetHomeVolume() => _host.GetHomeVolumeFromWeb();
    public int GetAwayVolume() => _host.GetAwayVolumeFromWeb();
    public void SetPaVolume(int percent) => _host.SetPaVolumeFromWeb(percent);
    public int GetPaVolume() => _host.GetPaVolumeFromWeb();
    public bool GetLeadInWhistleAvailable() => _host.GetLeadInWhistleAvailableFromWeb();
    public bool GetLeadInWhistleEnabled() => _host.GetLeadInWhistleEnabledFromWeb();
    public void SetLeadInWhistleEnabled(bool enabled) => _host.SetLeadInWhistleEnabledFromWeb(enabled);
    public bool BrowseAndSetLeadInWhistle() => _host.BrowseAndSetLeadInWhistleFromWeb();
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
    public bool DuplicateProfile(string fromTeam, string toTeam) => _host.DuplicateProfileFromWeb(fromTeam, toTeam);
    public void SetGameTeams(string home, string away) => _host.SetGameTeamsFromWeb(home, away);
    public string? GetGameTeams() => _host.GetGameTeamsFromWeb();
    public void ConfirmGametime(string home, string away) => _host.ConfirmGametimeFromWeb(home, away);

    /// <summary>Task queue item 6 -- returns JSON (not the raw List&lt;string&gt;) for the same
    /// reason every other structured return in this class does: WebView2's host-object marshaling
    /// reliably hands JS primitives and JSON strings, not arbitrary .NET collection types.</summary>
    public string GetTeamsNeedingDefaultProfile(string home, string away) =>
        JsonSerializer.Serialize(_host.GetTeamsNeedingDefaultProfileFromWeb(home, away));

    /// <summary>Explicit opt-in apply of the default/starter profile for one team (task queue
    /// item 6) -- reuses ConfigStore.ImportDefaultPackForTeam, the SAME logic
    /// SetGameTeamsFromWeb's silent safety-net uses, just invoked directly and immediately rather
    /// than as a side effect of confirming the matchup. Loads the team's existing profile (or
    /// BuildDefault if none) so this is safe to call even if the team already has SOME
    /// assignments -- ImportDefaultPackForTeam only ever fills empty slots, never overwrites.</summary>
    public int ApplyDefaultProfileForTeam(string teamName) => _host.ApplyDefaultProfileForTeamFromWeb(teamName);
    public void PlayClickSound() => _host.PlayUiClickSoundFromWeb();
    public bool IsMatchupLocked() => _host.IsMatchupLockedFromWeb();
    public void CopyCurrentToAllTeams() => _host.CopyCurrentToAllTeamsFromWeb();
    public void DeleteCurrentProfile() => _host.DeleteCurrentProfileFromWeb();
    public void ExportProfile() => _host.ExportProfileFromWeb();
    /// <summary>targetTeamName is the school the imported profile should apply to (chosen by the
    /// user in the Load Profile dialog's team-select step) -- NOT assumed to be whatever team
    /// happens to be active right now, since a shared/downloaded profile is very often meant for
    /// a different team than the one currently on screen.</summary>
    public void ImportProfile(string targetTeamName) => _host.ImportProfileFromWeb(targetTeamName);

    /// <summary>Adds a user-created "TeamBuilder" custom school (name + primary/secondary color
    /// only -- no in-game OCR/matching) and returns JSON {success, error, team} following the
    /// same shape as the other add/edit bridge calls above. Rejects blank names and duplicates
    /// (case-insensitive) rather than silently overwriting or creating a shadow entry.</summary>
    public string AddCustomTeam(string name, string primaryHex, string secondaryHex)
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

            var team = TeamColors.AddCustomTeam(trimmed, primary, secondary);
            return JsonSerializer.Serialize(new
            {
                success = true,
                error = (string?)null,
                team = new { name = team.Name, primary = ColorHex(primary), secondary = ColorHex(secondary) },
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Couldn't add school: {ex.Message}" });
        }
    }

    static string ColorHex(System.Drawing.Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
