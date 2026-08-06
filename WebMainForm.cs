using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Squirrel;
using Squirrel.Sources;

namespace SupremeStadiumSoundSelector;

/// <summary>WebView2 host shell -- replaces the native WinForms UI (MainForm.cs and friends)
/// with an HTML/CSS/JS page for real glassmorphism (backdrop-filter blur), per the Session 21
/// decision. Backend (audio/OCR/hotkeys/config) is untouched and reused as-is; this class is
/// just the window + the JS&lt;-&gt;C# bridge (WebBridge.cs).</summary>
public sealed class WebMainForm : Form
{
    static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac" };
    static readonly string[] CategoryOrder = { "Downs", "Scoring", "Turnovers", "Special Teams", "Penalties", "Hype" };

    List<TriggerEntry> _config = new();
    readonly KeyboardHook _hook = new();
    readonly GameWatcher _watcher = new();
    readonly CancellationTokenSource _lifetimeCts = new();
    WebView2 _webView = null!;
    bool _updateAvailable;
    bool _watching;
    bool _windowFound;

    // Matchup: lets a user pick who's home/away for THIS game and load each side's own saved
    // profile, so both teams' customized songs are available at once -- Offense/Defense sounds
    // fire from whichever side's config actually did the thing, resolved via GameWatcher's
    // color-sampled possession read, instead of always assuming "the active team".
    TeamColor? _homeTeam, _awayTeam;
    List<TriggerEntry>? _homeConfig, _awayConfig;
    string? _possession;

    /// <summary>True from the moment GAMETIME is pressed until watching is stopped. While
    /// locked, _homeTeam/_awayTeam (and therefore which physical side OCR-detected events route
    /// to) can't change -- only the SONGS assigned to each side can, via the Home/Away toggle.
    /// This matches the real workflow: you set the matchup once at kickoff, then Stop Watching
    /// is the one signal that means "this game is over, I might pick a different matchup next."</summary>
    bool _matchupLocked;

    public WebMainForm()
    {
        Text = "Bandroom";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Width = 1920;
        Height = 1080;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 650);
        BackColor = Theme.WindowBg;
        FormBorderStyle = FormBorderStyle.None; // chrome bar is drawn in HTML now, no OS titlebar/URL bar
        KeyPreview = true;

        _webView = new WebView2 { Dock = DockStyle.Fill };
        ((Control)_webView).AllowDrop = true;
        _webView.DragEnter += OnSongDragEnter;
        _webView.DragDrop += OnSongDragDrop;
        Controls.Add(_webView);

        ConfigStore.MigrateFromVersionedFolderIfNeeded();
        _config = ConfigStore.LoadOrCreate();

        _hook.KeyCombo += OnKeyCombo;
        _watcher.WindowFoundChanged += OnWindowFoundChanged;
        _watcher.DownChanged += OnDownChanged;
        _watcher.RegionChanged += OnRegionChanged;
        _watcher.PossessionChanged += side => _possession = side;
        _watcher.TackleForLossDetected += OnTackleForLoss;
        _watcher.ResolveTeamColor = ResolveTeamColor;
        _watcher.Log += OnLog;
        _watcher.ActivePreset = ScorebugPreset.GetByName(ConfigStore.LoadScorebugPresetName());

        FormClosing += (_, _) => { _hook.Stop(); _watcher.Stop(); };

        Load += async (_, _) =>
        {
            await InitWebViewAsync();
            InitAutoUpdater();
            UserCountService.StartHeartbeat(_lifetimeCts.Token);
            PlayDraftChime();
        };
        FormClosing += (_, _) => _lifetimeCts.Cancel();
    }

    async Task InitWebViewAsync()
    {
        string userDataFolder = Path.Combine(AppContext.BaseDirectory, "WebView2Data");
        Directory.CreateDirectory(userDataFolder);
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        var core = _webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping("appassets", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("teambg", ConfigStore.TeamBackgroundsFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("teamlogo", ConfigStore.TeamLogosFolder, CoreWebView2HostResourceAccessKind.Allow);
        Directory.CreateDirectory(ConfigStore.DownloadedImagesFolder);
        Directory.CreateDirectory(ConfigStore.SongsUploadedFolder);
        Directory.CreateDirectory(ConfigStore.LocalTracksFolder);
        Directory.CreateDirectory(ConfigStore.AvatarFolder);
        core.SetVirtualHostNameToFolderMapping("downloadedimages", ConfigStore.DownloadedImagesFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("downloadedsongs", ConfigStore.SongsUploadedFolder, CoreWebView2HostResourceAccessKind.Allow);
        // End-user "import my own song" pipeline (item 21) -- its own virtual host, separate
        // from downloadedsongs, since these tracks live in ConfigStore.LocalTracksFolder, not
        // SongsUploadedFolder.
        core.SetVirtualHostNameToFolderMapping("localtracks", ConfigStore.LocalTracksFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("avatar", ConfigStore.AvatarFolder, CoreWebView2HostResourceAccessKind.Allow);

        core.AddHostObjectToScript("bandroom", new WebBridge(this));

        // WebView2's disk cache can hang onto an old style.css/app.js across a Squirrel update
        // (the WebView2Data profile folder is intentionally persistent across versions, for
        // cookies/localStorage -- but that means static assets served through the virtual host
        // mapping above can get served stale instead of picking up the new version's files).
        // Force every network request this session to skip cache so UI fixes always take effect
        // right after an update, not just after a manual profile wipe.
        try { await core.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}"); }
        catch (Exception ex) { CrashLog.Write("Failed to disable WebView2 cache", ex); }

        _webView.Source = new Uri("https://appassets/index.html");
    }

    void OnSongDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnSongDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        int imported = 0;
        foreach (var path in paths)
            if (ConfigStore.ImportIntoSongsLibrary(path) != null) imported++;

        if (imported > 0)
            RunOnUi(() => _ = _webView.ExecuteScriptAsync(
                $"window.dispatchEvent(new CustomEvent('bandroom:songsimported', {{ detail: {imported} }}))"));
    }

    // --- Called from WebBridge (JS -> C#) ---

    public Dictionary<string, (int assigned, int total)> GetCategoryCounts()
    {
        var byCategory = CategoryOrder.ToDictionary(c => c, c => (assigned: 0, total: 0));
        foreach (var entry in _config)
        {
            string cat = CategoryMap.Resolve(entry);
            if (!byCategory.ContainsKey(cat)) continue;
            var (assigned, total) = byCategory[cat];
            total++;
            if (!string.IsNullOrWhiteSpace(entry.AudioFile)) assigned++;
            byCategory[cat] = (assigned, total);
        }
        return byCategory;
    }

    /// <summary>Downloads a Trophy Room image and saves it as <paramref name="team"/>'s local
    /// background (see TeamBackgroundDownloadService). This is a plain download, not a UI
    /// mutation, so it deliberately does NOT need to run on the UI thread -- only the caller
    /// (WebBridge, itself called from WebView2's own thread) awaits the result.</summary>
    public async Task<bool> DownloadAndSetTeamBackgroundFromWeb(string team, string url)
    {
        string? saved = await TeamBackgroundDownloadService.DownloadAndSaveAsync(team, url);
        return saved != null;
    }

    public void SelectTeamFromWeb(string name)
    {
        var team = TeamColors.All.FirstOrDefault(t => t.Name == name);
        if (team.Name == null || team.Name == Theme.ActiveTeam.Name) return;
        SaveCurrentTeamProfile();

        Theme.ActiveTeam = team;
        _config = ConfigStore.ListProfiles().Contains(team.Name, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(team.Name)
            : ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
        PushCategories();
    }

    /// <summary>Explicit user-triggered save, from the Save rail button. name is null/empty ->
    /// save under the active team's name (the normal case); non-empty -> save as an extra,
    /// separately-named profile without switching the active team away from it.</summary>
    public string SaveProfileAsFromWeb(string? name)
    {
        ConfigStore.Save(_config);
        string target = string.IsNullOrWhiteSpace(name) ? Theme.ActiveTeam.Name : name.Trim();
        ConfigStore.SaveProfile(target, _config);
        RefreshHomeAwayConfigIfNeeded(target);
        RunOnUi(() =>
        {
            if (_webView.CoreWebView2 != null)
                _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:profileschanged'))");
        });
        return target;
    }

    public string? GetProfileSavedAtFromWeb(string name) =>
        ConfigStore.GetProfileSavedAt(name)?.ToString("h:mm tt");

    /// <summary>Matchup picker (JS) calls this with both teams for the game about to be
    /// watched. Loads each team's OWN saved profile if they have one (falls back to defaults
    /// otherwise) -- these are separate from _config/Theme.ActiveTeam, which stay pointed at
    /// whatever the sidebar/background is currently showing.</summary>
    public void SetGameTeamsFromWeb(string homeName, string awayName)
    {
        _homeTeam = TeamColors.All.FirstOrDefault(t => t.Name == homeName);
        _awayTeam = TeamColors.All.FirstOrDefault(t => t.Name == awayName);
        _homeConfig = ConfigStore.ListProfiles().Contains(homeName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(homeName) : ConfigStore.BuildDefault();
        _awayConfig = ConfigStore.ListProfiles().Contains(awayName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(awayName) : ConfigStore.BuildDefault();
        _possession = null;
    }

    /// <summary>The GAMETIME button. Same wiring as SetGameTeamsFromWeb, plus the confirmation
    /// chime and the lock -- meant to be pressed once, while still on CFB 27's own team-select
    /// screen, right before kickoff.</summary>
    public void ConfirmGametimeFromWeb(string homeName, string awayName)
    {
        SetGameTeamsFromWeb(homeName, awayName);
        _matchupLocked = true;
        PlayGametimeSound();
        RecordGameWatched(homeName, awayName);
    }

    /// <summary>Bumps the universal profile's lifetime "games watched" counter, per-team
    /// breakdown, and daily streak -- see ConfigStore.UserProfile. Fire-and-forget cloud sync,
    /// local save is what actually matters here since it must never delay/interrupt GAMETIME
    /// locking in.</summary>
    static void RecordGameWatched(string homeName, string awayName)
    {
        var current = ConfigStore.LoadUserProfile();
        var byTeam = new Dictionary<string, int>(current.GamesWatchedByTeam);
        foreach (var team in new[] { homeName, awayName })
            byTeam[team] = byTeam.GetValueOrDefault(team) + 1;

        // Local date, not UTC -- "daily streak" means the user's own calendar day. Using UTC
        // here would make the streak reset (or double-count) around the user's local midnight
        // for anyone not near UTC, which would look broken even with genuinely consecutive days
        // of real play.
        var today = DateTime.Now.Date;
        int streak = current.StreakCurrentDays;
        if (current.StreakLastActiveDate == today)
        {
            // Already counted today -- leave streak as-is.
        }
        else if (current.StreakLastActiveDate == today.AddDays(-1))
        {
            streak += 1; // consecutive day
        }
        else
        {
            streak = 1; // gap in usage (or first-ever game) -- streak restarts
        }

        var updated = current with
        {
            GamesWatched = current.GamesWatched + 1,
            GamesWatchedByTeam = byTeam,
            StreakCurrentDays = streak,
            StreakLastActiveDate = today,
        };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    static DateTime _lastSongTriggerCloudSync = DateTime.MinValue;

    /// <summary>Bumps "songs triggered" (lifetime total + per-event breakdown, for "most-triggered
    /// event") for every real in-game cue (FireEvent), including down:* triggers which fire on
    /// nearly every single play -- the local file write stays per-trigger (cheap), but the cloud
    /// push is throttled to at most once every 30s so a live game never turns into a rapid-fire
    /// network hammer against the marketplace worker.</summary>
    static void RecordSongTriggered(string eventName)
    {
        var current = ConfigStore.LoadUserProfile();
        var eventCounts = new Dictionary<string, int>(current.EventCounts);
        if (!string.IsNullOrWhiteSpace(eventName))
            eventCounts[eventName] = eventCounts.GetValueOrDefault(eventName) + 1;
        var updated = current with { SongsTriggered = current.SongsTriggered + 1, EventCounts = eventCounts };
        ConfigStore.SaveUserProfile(updated);

        if (DateTime.UtcNow - _lastSongTriggerCloudSync < TimeSpan.FromSeconds(30)) return;
        _lastSongTriggerCloudSync = DateTime.UtcNow;
        _ = ProfileSyncService.PushAsync(updated);
    }

    public bool IsMatchupLockedFromWeb() => _matchupLocked;

    public string? GetGameTeamsFromWeb() =>
        _homeTeam.HasValue && _awayTeam.HasValue
            ? System.Text.Json.JsonSerializer.Serialize(new { home = _homeTeam.Value.Name, away = _awayTeam.Value.Name, locked = _matchupLocked })
            : null;

    /// <summary>Resolves a sampled ribbon color to "home"/"away"/null -- null covers both the
    /// neutral black background (no play in progress) and a color that doesn't clearly match
    /// either team (bad OCR crop, mid-transition frame). Distance-based, not exact match, since
    /// capture/compression introduces noise around the real hex values.</summary>
    string? ResolveTeamColor(Color sampled)
    {
        if (IsNearBlack(sampled)) return null;
        if (_homeTeam is not { } home || _awayTeam is not { } away) return null;

        int homeDist = Math.Min(ColorDistance(sampled, home.Primary), ColorDistance(sampled, home.Secondary));
        int awayDist = Math.Min(ColorDistance(sampled, away.Primary), ColorDistance(sampled, away.Secondary));
        const int MaxMatchDistance = 90;
        if (homeDist > MaxMatchDistance && awayDist > MaxMatchDistance) return null;
        return homeDist <= awayDist ? "home" : "away";
    }

    static bool IsNearBlack(Color c) => c.R < 45 && c.G < 45 && c.B < 45;

    static int ColorDistance(Color a, Color? b)
    {
        if (b is not { } c) return int.MaxValue;
        int dr = a.R - c.R, dg = a.G - c.G, db = a.B - c.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    void FireEventForSide(string side, string eventName)
    {
        var config = side == "home" ? _homeConfig : _awayConfig;
        var entry = config?.FirstOrDefault(e => e.Event == eventName);
        if (entry != null) FireEvent(entry, side == "home" ? AudioPlayer.HomeVolume : AudioPlayer.AwayVolume);
    }

    void FireTriggerForSide(string side, string trigger)
    {
        var config = side == "home" ? _homeConfig : _awayConfig;
        var entry = config?.FirstOrDefault(e => e.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        if (entry != null) FireEvent(entry, side == "home" ? AudioPlayer.HomeVolume : AudioPlayer.AwayVolume);
    }

    void SaveCurrentTeamProfile()
    {
        ConfigStore.SaveProfile(Theme.ActiveTeam.Name, _config);
        RefreshHomeAwayConfigIfNeeded(Theme.ActiveTeam.Name);
        RunOnUi(() =>
        {
            if (_webView.CoreWebView2 != null)
                _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:profileschanged'))");
        });
    }

    /// <summary>Keeps the live home/away snapshots (read by FireEventForSide during a real game)
    /// in sync with whatever was just saved to disk. _homeConfig/_awayConfig are separate
    /// in-memory copies loaded once when the matchup is confirmed (see SetGameTeamsFromWeb) --
    /// without this, assigning/saving Home or Away's songs AFTER the matchup was already set had
    /// no effect on actual gameplay until the matchup was re-confirmed from scratch.</summary>
    void RefreshHomeAwayConfigIfNeeded(string savedTeamName)
    {
        if (_homeTeam is { } home && string.Equals(home.Name, savedTeamName, StringComparison.OrdinalIgnoreCase))
            _homeConfig = ConfigStore.LoadProfile(savedTeamName);
        if (_awayTeam is { } away && string.Equals(away.Name, savedTeamName, StringComparison.OrdinalIgnoreCase))
            _awayConfig = ConfigStore.LoadProfile(savedTeamName);
    }

    public string ToggleWatchingFromWeb()
    {
        if (_watching)
        {
            _hook.Stop();
            _watcher.Stop();
            _watching = false;
            _windowFound = false;
            // Stop Watching is the one explicit "this game is over" signal (see _matchupLocked) --
            // unlock so a new GAMETIME press can pick a different matchup for the next game.
            _matchupLocked = false;
        }
        else
        {
            // Refuse to start without a Matchup set -- _homeTeam/_awayTeam (and everything
            // derived from them: ResolveTeamColor, _homeConfig/_awayConfig) stay null until
            // SetGameTeamsFromWeb runs, which otherwise silently falls back to firing one
            // team's cues for both sides. Explicit user ask: force Set Matchup first so the
            // system actually knows both teams' colors before OCR starts.
            if (_homeTeam is null || _awayTeam is null) return "no-matchup";

            _hook.Start();
            _watcher.Start();
            _watching = true;
        }
        return WatchStateString();
    }

    string WatchStateString() => !_watching ? "off" : _windowFound ? "watching" : "waiting";

    public void OpenSettingsFromWeb()
    {
        var opts = new SettingsForm.Options(
            AlwaysOnTop: TopMost,
            SetAlwaysOnTop: v => TopMost = v,
            Volume: (int)(AudioPlayer.MasterVolume * 100),
            SetVolume: v => AudioPlayer.MasterVolume = v / 100f,
            Reverb: AudioPlayer.CurrentReverb,
            SetReverb: r => AudioPlayer.CurrentReverb = r,
            StopPlayback: () => AudioPlayer.StopAll(),
            OpenSongsFolder: () => { Directory.CreateDirectory(ConfigStore.SongsFolder); System.Diagnostics.Process.Start("explorer.exe", ConfigStore.SongsFolder); },
            ClearAll: ClearAll,
            Compact: false,
            ToggleCompact: () => { },
            ResetTeamProfile: ResetTeamProfileFromWeb,
            ScorebugPresetName: _watcher.ActivePreset.Name,
            SetScorebugPresetName: name =>
            {
                _watcher.ActivePreset = ScorebugPreset.GetByName(name);
                ConfigStore.SaveScorebugPresetName(name);
            }
        );
        new SettingsForm(this, opts).ShowDialog(this);
    }

    void ClearAll()
    {
        foreach (var entry in _config) entry.AudioFile = "";
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        PushCategories();
    }

    public void ResetTeamProfileFromWeb()
    {
        _config = ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        PushCategories();
    }

    public void OpenHelpFromWeb() => new ShortcutsForm(this).ShowDialog(this);

    /// <summary>All 33 situations, or just the ones in `category` (matches CategoryMap.Resolve,
    /// same "Downs"/"Scoring"/etc bucketing the category chips use) when category is non-null.
    /// null/"All" -> every situation, for the "All" chip.</summary>
    public List<TriggerEntry> GetEvents(string? category)
    {
        if (string.IsNullOrEmpty(category) || category == "All") return _config;
        return _config.Where(e => CategoryMap.Resolve(e) == category).ToList();
    }

    public void OpenAssignTrackFromWeb(string trigger)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry != null) OpenAssignTrack(entry);
        PushCategories();
    }

    public void PreviewEventFromWeb(string trigger)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry != null) FireEvent(entry);
    }

    public void StopPreviewFromWeb() => AudioPlayer.StopAll();

    public void TriggerEffectsTestFromWeb()
    {
        var entry = _config.FirstOrDefault(e => e.Event.Contains("Touchdown", StringComparison.OrdinalIgnoreCase))
            ?? _config.FirstOrDefault();
        if (entry != null) FireEvent(entry);
    }

    void OpenAssignTrack(TriggerEntry entry)
    {
        var library = new List<string>();
        if (Directory.Exists(ConfigStore.SongsFolder))
            library.AddRange(Directory.GetFiles(ConfigStore.SongsFolder, "*", SearchOption.AllDirectories)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));

        using var dlg = new AssignTrackForm(this, entry, library);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.RequestTrim)
        {
            if (string.IsNullOrWhiteSpace(entry.AudioFile) || !File.Exists(entry.AudioFile))
            {
                MessageBox.Show(this, "Choose a song for this situation first, then you can trim it.", "Bandroom");
                return;
            }
            // Clip start/end sliders + Save, same as the "Volume" slider style in the main
            // shell -- TrimmerForm already has this, just wiring it back in here.
            using var trimmer = new TrimmerForm(this, entry.AudioFile);
            if (trimmer.ShowDialog(this) == DialogResult.OK && trimmer.SavedFilePath != null)
                entry.AudioFile = trimmer.SavedFilePath;
            else return;
        }
        else if (dlg.RequestClear) entry.AudioFile = "";
        else if (dlg.AssignedPath != null) entry.AudioFile = dlg.AssignedPath;
        else return;

        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
    }

    public void SetVolumeFromWeb(int percent) => AudioPlayer.MasterVolume = percent / 100f;

    /// <summary>Matchup-mode independent side volumes -- lets the home and away team's cues be
    /// balanced (or one muted) separately, since both sides' events can legitimately fire close
    /// together in a real game now that possession detection routes each to its own side.</summary>
    public void SetHomeVolumeFromWeb(int percent) => AudioPlayer.HomeVolume = percent / 100f;
    public void SetAwayVolumeFromWeb(int percent) => AudioPlayer.AwayVolume = percent / 100f;
    public int GetHomeVolumeFromWeb() => (int)(AudioPlayer.HomeVolume * 100);
    public int GetAwayVolumeFromWeb() => (int)(AudioPlayer.AwayVolume * 100);

    /// <summary>"Fire Sensitivity" slider -- delay in seconds before a fired cue starts fading
    /// out. No fade-in: AudioPlayer.Play already jumps straight to full volume, only the
    /// fade-OUT ramp exists, so this only ever tunes when that ramp begins.</summary>
    public void SetFadeDelayFromWeb(int seconds) => AudioPlayer.FadeStartSeconds = seconds;

    public void SetReverbFromWeb(string key) => AudioPlayer.CurrentReverb = key switch
    {
        "stadium" => ReverbPreset.Stadium,
        "dome" => ReverbPreset.Dome,
        "nightgame" => ReverbPreset.NightGame,
        _ => ReverbPreset.Off,
    };

    /// <summary>Classic Win32 "drag via titlebar" trick -- the HTML chrome bar has no native
    /// titlebar behind it (FormBorderStyle.None), so JS calls this on mousedown to let the OS
    /// handle the drag instead of hand-rolling mouse-move tracking.</summary>
    public void BeginWindowDrag()
    {
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HTCAPTION = 0x2;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    /// <summary>FormBorderStyle.None (see the constructor) drops the OS-drawn window edges
    /// entirely, which also drops the ability to resize by dragging them -- MaximizeWindow was
    /// the only way to change the window size before this. Handling WM_NCHITTEST ourselves and
    /// returning the standard HTLEFT/HTRIGHT/etc hit-test codes near the edges hands control
    /// back to DefWndProc's normal resize/cursor behavior, without needing WS_THICKFRAME.</summary>
    protected override void WndProc(ref Message m)
    {
        const int margin = 6;
        if (m.Msg == Native.WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            var cursor = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, (m.LParam.ToInt32() >> 16) & 0xFFFF));
            bool left = cursor.X <= margin, right = cursor.X >= ClientSize.Width - margin;
            bool top = cursor.Y <= margin, bottom = cursor.Y >= ClientSize.Height - margin;

            int hit = (left, right, top, bottom) switch
            {
                (true, false, true, false) => Native.HTTOPLEFT,
                (false, true, true, false) => Native.HTTOPRIGHT,
                (true, false, false, true) => Native.HTBOTTOMLEFT,
                (false, true, false, true) => Native.HTBOTTOMRIGHT,
                (true, false, false, false) => Native.HTLEFT,
                (false, true, false, false) => Native.HTRIGHT,
                (false, false, true, false) => Native.HTTOP,
                (false, false, false, true) => Native.HTBOTTOM,
                _ => Native.HTCLIENT,
            };
            if (hit != Native.HTCLIENT)
            {
                m.Result = (IntPtr)hit;
                return;
            }
        }
        base.WndProc(ref m);
    }

    public void MinimizeWindowFromWeb() => RunOnUi(() => WindowState = FormWindowState.Minimized);
    public void MaximizeWindowFromWeb() => RunOnUi(() =>
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized);
    public void CloseWindowFromWeb() => RunOnUi(() => Close());

    public void CopyCurrentToAllTeamsFromWeb()
    {
        var snapshot = _config.Select(e => new TriggerEntry { Trigger = e.Trigger, Event = e.Event, AudioFile = e.AudioFile }).ToList();
        _ = Task.Run(() =>
        {
            foreach (var team in TeamColors.All)
            {
                if (team.Name == Theme.ActiveTeam.Name) continue;
                ConfigStore.SaveProfile(team.Name, snapshot);
            }
            RunOnUi(() =>
            {
                if (_webView.CoreWebView2 != null)
                    _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:profileschanged'))");
                MessageBox.Show(this,
                    $"Your current audio setup has been copied to all {TeamColors.All.Length - 1} teams.",
                    "Profiles Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        });
    }

    public void DeleteCurrentProfileFromWeb()
    {
        ConfigStore.DeleteProfile(Theme.ActiveTeam.Name);
        _config = ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
        PushCategories();
        RunOnUi(() =>
        {
            if (_webView.CoreWebView2 != null)
                _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:profileschanged'))");
        });
    }

    public void ExportProfileFromWeb()
    {
        RunOnUi(() =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Export Profile",
                Filter = "Bandroom Profile (*.json)|*.json",
                FileName = $"{Theme.ActiveTeam.Name}.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            ConfigStore.SaveProfile(Path.GetFileNameWithoutExtension(dlg.FileName), _config);
            File.Copy(
                Path.Combine(ConfigStore.ProfilesFolder, Path.GetFileNameWithoutExtension(dlg.FileName) + ".json"),
                dlg.FileName, overwrite: true);
        });
    }

    public void ImportProfileFromWeb()
    {
        RunOnUi(() =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Import Profile",
                Filter = "Bandroom Profile (*.json)|*.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var imported = System.Text.Json.JsonSerializer.Deserialize<List<TriggerEntry>>(
                    File.ReadAllText(dlg.FileName),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (imported == null) return;
                _config = imported;
                ConfigStore.Save(_config);
                SaveCurrentTeamProfile();
                PushCategories();
            }
            catch
            {
                MessageBox.Show(this, "That file doesn't look like a valid Bandroom profile.", "Import Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    /// <summary>Exports the whole universal profile (favorite team + lifetime stats, NOT a
    /// single team's song assignments -- see ExportProfileFromWeb above for that) as a standalone
    /// JSON file.</summary>
    public void ExportUserProfileFromWeb()
    {
        RunOnUi(() =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Export Universal Profile",
                Filter = "Bandroom Universal Profile (*.json)|*.json",
                FileName = "bandroom-profile.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var profile = ConfigStore.LoadUserProfile();
                File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(
                    profile, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                CrashLog.Write($"ExportUserProfileFromWeb failed for \"{dlg.FileName}\"", ex);
                MessageBox.Show(this, "Couldn't export the profile -- see crash.log.", "Export Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    public void ImportUserProfileFromWeb()
    {
        RunOnUi(() =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Import Universal Profile",
                Filter = "Bandroom Universal Profile (*.json)|*.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var imported = System.Text.Json.JsonSerializer.Deserialize<ConfigStore.UserProfile>(
                    File.ReadAllText(dlg.FileName),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (imported == null) return;

                // AvatarFileName is a LOCAL file reference (see ConfigStore.AvatarFolder) --
                // meaningless if this profile was exported from a different device/install, where
                // that exact file never existed here. Importing it unchecked would silently point
                // the avatar at a file that 404s. Clear it unless the referenced file genuinely
                // exists on THIS device.
                if (imported.AvatarFileName != null &&
                    !File.Exists(Path.Combine(ConfigStore.AvatarFolder, imported.AvatarFileName)))
                {
                    imported = imported with { AvatarFileName = null };
                }

                ConfigStore.SaveUserProfile(imported);
                _ = ProfileSyncService.PushAsync(imported);
                _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:profileimported'))");
            }
            catch (Exception ex)
            {
                CrashLog.Write($"ImportUserProfileFromWeb failed for \"{dlg.FileName}\"", ex);
                MessageBox.Show(this, "That file doesn't look like a valid Bandroom universal profile.", "Import Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    /// <summary>The shared "draft chime" (Assets\nfl-draft-chime.mp3) used for attention-moments
    /// that AREN'T the GAMETIME press: app open, and a new update detected -- including while the
    /// app is already running unattended on someone else's machine, which is the normal case
    /// (it's always left open on one computer), so the update path fires this too, not just at
    /// launch.</summary>
    static void PlayDraftChime()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "nfl-draft-chime.mp3");
        AudioPlayer.Play(path);
    }

    /// <summary>The GAMETIME confirmation cue (Assets\gametime-tackle.mp3) -- a football tackle
    /// hit, kept separate from PlayDraftChime per user request so app-open/update-detected keep
    /// the draft chime while GAMETIME gets its own distinct sound.</summary>
    static void PlayGametimeSound()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "gametime-tackle.mp3");
        AudioPlayer.Play(path);
    }

    /// <summary>Every button/tile press in the UI calls this via WebBridge.PlayClickSound (see
    /// app.js's document-level click delegate) -- a tiny synthesized tick, not the draft chime,
    /// so ordinary UI navigation doesn't compete with real event cues. Generated in-memory like
    /// the old chime methods used to be, since a ~15ms tick doesn't need a bundled asset. Skips
    /// the AudioPlayer cooldown/fade pipeline entirely -- that machinery is built for full clips,
    /// and would only add latency here.</summary>
    public void PlayUiClickSoundFromWeb()
    {
        _ = Task.Run(() =>
        {
            try
            {
                // A pure sine tone reads as a "pop"/beep no matter how short the envelope is --
                // a real mechanical click is closer to filtered noise: a very short burst with
                // most of its energy dumped in the first millisecond, tiny bit of low-passing so
                // it isn't harsh white-noise hiss. Quieter than before too, per user ask.
                int sampleRate = 44100;
                int n = 10 * sampleRate / 1000; // ~10ms
                var buf = new float[n];
                var rng = new Random();
                float prev = 0f;
                for (int i = 0; i < n; i++)
                {
                    float env = MathF.Pow(1f - (float)i / n, 4f); // very fast decay -- most energy in the first ~2ms
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    prev = prev * 0.6f + noise * 0.4f; // light low-pass so it's a "tick" not harsh hiss
                    buf[i] = prev * 0.14f * env;
                }
                var bytes = new byte[buf.Length * 2];
                for (int i = 0; i < buf.Length; i++)
                {
                    short s = (short)(Math.Clamp(buf[i], -1f, 1f) * 32767);
                    bytes[i * 2] = (byte)(s & 0xFF);
                    bytes[i * 2 + 1] = (byte)(s >> 8);
                }
                var fmt = new NAudio.Wave.WaveFormat(sampleRate, 16, 1);
                using var stream = new NAudio.Wave.RawSourceWaveStream(new MemoryStream(bytes), fmt);
                using var wo = new NAudio.Wave.WaveOutEvent();
                wo.Init(stream);
                wo.Play();
                while (wo.PlaybackState == NAudio.Wave.PlaybackState.Playing) Thread.Sleep(5);
            }
            catch { }
        });
    }

    void PushCategories()
    {
        if (_webView.CoreWebView2 == null) return;
        _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:refresh'))");
    }

    // --- Backend event wiring (unchanged from native MainForm) ---

    void FireEvent(TriggerEntry entry, float? volumeOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(entry.AudioFile) && File.Exists(entry.AudioFile))
        {
            AudioPlayer.Play(entry.AudioFile, volumeOverride, interruptPrevious: true);
            RecordSongTriggered(entry.Event);
            // Names exactly which trigger OCR just read as a small on-screen flash, so a user can
            // confirm what fired without digging through logs -- this call isn't gated on
            // _webView.CoreWebView2 being non-null elsewhere in this file only because FireEvent
            // is never reachable before the WebView2 is initialized (all watcher events wire up
            // after InitWebViewAsync completes).
            // Skipped for down:* triggers specifically -- those fire on nearly every single play,
            // so toasting every one would spam the screen during a live game without adding real
            // information (the sound itself is already the real-time confirmation for downs).
            // Rarer situational events (touchdown/turnover/kickoff/etc.) are worth calling out.
            if (!entry.Trigger.StartsWith("down:", StringComparison.OrdinalIgnoreCase))
            {
                string safeName = entry.Event.Replace("'", "\\'");
                _ = _webView.ExecuteScriptAsync($"window.dispatchEvent(new CustomEvent('bandroom:triggerfired', {{ detail: '{safeName}' }}))");
            }
        }
    }

    void OnWindowFoundChanged(bool found)
    {
        RunOnUi(() =>
        {
            _windowFound = found;
            if (_webView.CoreWebView2 != null)
                _ = _webView.ExecuteScriptAsync($"window.dispatchEvent(new CustomEvent('bandroom:watchstate', {{ detail: '{WatchStateString()}' }}))");
        });
    }

    /// <summary>The offense (whoever the live possession color says has the ball) just showed a
    /// negative distance-to-go -- that only happens on a penalty or a loss of yards, and since we
    /// already know who's on offense, we know their opponent's Defense caused it.
    /// NOT gated by <see cref="HomeOnlyEventsForNow"/> -- this away-side TFL detection is
    /// considered "concrete"/solid (side-agnostic distance read, confirmed via live screenshot
    /// per the comments on GameWatcher.CheckForLossOfYards), so the owner explicitly asked to
    /// leave it firing for both sides while everything else gets simplified to home-only.</summary>
    void OnTackleForLoss()
    {
        RunOnUi(() =>
        {
            if (_homeConfig == null || _awayConfig == null || _possession == null) return;
            string defenseSide = _possession == "home" ? "away" : "home";
            FireEventForSide(defenseSide, "Defense: Tackle for Loss");
        });
    }

    /// <summary>Temporary simplification, requested by the owner ahead of a major push: only the
    /// HOME team's events should fire for now. Away-side firing (via FireTriggerForSide/
    /// FireEventForSide below) is disabled, not deleted, so it's a one-line revert once away-side
    /// support is wanted again. Tackle-for-Loss is deliberately exempt -- see OnTackleForLoss.</summary>
    const bool HomeOnlyEventsForNow = true;

    void OnDownChanged(string? down)
    {
        RunOnUi(() =>
        {
            if (down == null) return;
            string trigger = $"down:{down}";

            // Same live possession-color read that routes Touchdown/PAT/Kickoff/Turnover --
            // the down ribbon is colored for whichever team is on offense, so once a Matchup
            // is locked in we know whose profile should fire.
            if (_homeConfig != null && _awayConfig != null && _possession != null)
            {
                // Simplified-for-now: only fire when HOME is on offense (see HomeOnlyEventsForNow).
                // Original away-aware call, commented out rather than deleted:
                //   FireTriggerForSide(_possession, trigger);
                bool sideAllowed = HomeOnlyEventsForNow ? _possession == "home" : true;
                if (sideAllowed) FireTriggerForSide(_possession, trigger);
                return;
            }

            var entry = _config.FirstOrDefault(e => e.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
            if (entry != null) FireEvent(entry);
        });
    }

    // Regions whose matched value IS the trigger key ("situation:kickoff", "banner:touchdown")
    // rather than a fixed on/off toggle ("flag:on") -- see GameWatcher.NormalizeMatch.
    static readonly HashSet<string> ValueKeyedRegions = new(StringComparer.OrdinalIgnoreCase) { "situation", "banner", "quarter" };

    // situation:touchdown/turnover need to know WHO did it, not just that it happened --
    // resolved via _possession (GameWatcher's color-sampled read of the same ribbon) when a
    // Matchup is set. Falls through to the old single-active-team behavior otherwise, so
    // nothing regresses for anyone who hasn't set up a Matchup.
    static readonly Dictionary<string, string> SideAwareEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["touchdown"] = "Offense: Touchdown Scored",
        ["turnover"] = "Defense: Turnover Forced",
        ["pat_good"] = "Offense: PAT Made",
        ["kickoff"] = "Other: Opening Kickoff",
    };

    void OnRegionChanged(string region, string? value)
    {
        if (region == "down") return;
        RunOnUi(() =>
        {
            if (region == "situation" && value != null && _homeConfig != null && _awayConfig != null
                && _possession != null && SideAwareEvents.TryGetValue(value, out var eventName))
            {
                // Simplified-for-now: only fire when HOME is the side attributed to this
                // situation (see HomeOnlyEventsForNow). Original away-aware call, commented out
                // rather than deleted:
                //   FireEventForSide(_possession, eventName);
                bool sideAllowed = HomeOnlyEventsForNow ? _possession == "home" : true;
                if (sideAllowed) FireEventForSide(_possession, eventName);
                return;
            }

            string triggerKey = ValueKeyedRegions.Contains(region) ? $"{region}:{value}" : $"{region}:on";
            var entry = _config.FirstOrDefault(e => e.Trigger.Equals(triggerKey, StringComparison.OrdinalIgnoreCase));
            if (entry != null) FireEvent(entry);
        });
    }

    void OnKeyCombo(string keyCombo)
    {
        RunOnUi(() =>
        {
            var entry = _config.FirstOrDefault(e => e.Trigger.Equals($"key:{keyCombo}", StringComparison.OrdinalIgnoreCase));
            if (entry != null) FireEvent(entry);
        });
    }

    void OnLog(string message) { }

    void InitAutoUpdater()
    {
        // Catches the "ran an old cached Setup.exe and silently downgraded" bug -- see
        // VersionGuard.cs. This machine has run a newer build before, but is currently on an
        // older one, so tell the user plainly instead of leaving them staring at a build
        // that's missing features they've already seen, with no explanation why.
        var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        if (VersionGuard.CheckAndRecord(current))
        {
            _updateAvailable = true;
            RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:downgraded'))"));
        }

        // Run on a background thread — never block the UI for network calls. Loops for the
        // whole lifetime of the app (not just once at startup): Bandroom is normally left open
        // on one machine for hours/days, so a release shipped after launch would otherwise
        // never chime/pulse until the user happened to restart the app or manually click
        // Update -- which is exactly the gap a live user hit (pushed a release while their app
        // was already open; nothing fired until they were told to click it by hand).
        _ = Task.Run(async () =>
        {
            while (!_lifetimeCts.IsCancellationRequested)
            {
                try
                {
                    if (!_updateAvailable)
                    {
                        using var mgr = new UpdateManager(new GithubSource("https://github.com/kingsupreme89/Bandroom-v1", null, false));
                        var info = await mgr.CheckForUpdate();
                        if (info.ReleasesToApply.Count > 0)
                        {
                            _updateAvailable = true;
                            PlayDraftChime();
                            RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:updateavailable'))"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashLog.Write("Auto-update check failed", ex);
                }

                try { await Task.Delay(TimeSpan.FromMinutes(3), _lifetimeCts.Token); }
                catch (TaskCanceledException) { break; }
            }
        });
    }

    public void ShowUpdateDialogFromWeb()
    {
        // Used to silently do nothing if the background "is an update available" check
        // hadn't completed/succeeded yet (VPN hiccup, slow network, etc) -- clicking the
        // button looked broken with zero feedback. Now it always tries, and always tells
        // the user something either way. Also now surfaces real download progress and asks
        // before restarting -- it used to silently download then instantly relaunch the app
        // out from under the user with zero warning, which read as the app randomly closing.
        _ = Task.Run(async () =>
        {
            try
            {
                using var mgr = new UpdateManager(new GithubSource("https://github.com/kingsupreme89/Bandroom-v1", null, false));
                var info = await mgr.CheckForUpdate();
                if (info.ReleasesToApply.Count == 0)
                {
                    RunOnUi(() => MessageBox.Show(this,
                        "You're already on the latest version.",
                        "Bandroom Update", MessageBoxButtons.OK, MessageBoxIcon.Information));
                    return;
                }

                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:updatedownloading'))"));

                await mgr.UpdateApp(pct => RunOnUi(() =>
                    _ = _webView.ExecuteScriptAsync($"window.dispatchEvent(new CustomEvent('bandroom:updateprogress', {{ detail: {pct} }}))")));

                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:updateready'))"));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Auto-update install failed", ex);
                RunOnUi(() =>
                {
                    _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:updatefailed'))");
                    MessageBox.Show(this,
                        "Update check/download failed -- check your internet connection (and VPN, if you use one) and try again.",
                        "Bandroom Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            }
        });
    }

    /// <summary>User clicks "Restart Now" on the post-download prompt (see bandroom:updateready
    /// in app.js) -- the app no longer relaunches itself automatically the instant the download
    /// finishes.</summary>
    public void RestartForUpdateFromWeb() => RunOnUi(() => UpdateManager.RestartApp());

    void RunOnUi(Action action)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(action);
        else if (IsHandleCreated) action();
    }

    /// <summary>Synchronous counterpart to RunOnUi -- needed here (unlike every existing
    /// RunOnUi caller) because WebBridge.ImportLocalSong has to hand a real result (or
    /// "cancelled") back to the JS await, not just fire a dialog and move on. Blocks the
    /// calling (WebView2 host-object) thread until the whole modal sequence below finishes.</summary>
    T RunOnUiSync<T>(Func<T> func) =>
        IsHandleCreated && InvokeRequired ? (T)Invoke(func) : func();

    /// <summary>End-user "import my own song" pipeline (item 21): choose a local file -> name
    /// the track -> the SAME TrimmerForm/NormalizeAndLimit path marketplace tracks go through
    /// auto-opens with that name already set -> saved into ConfigStore.LocalTracksFolder and
    /// registered in the local-tracks manifest so it shows up in My Downloads immediately, with
    /// "Share to Marketplace" available. All three dialogs are modal, so this runs synchronously
    /// on the UI thread via RunOnUiSync and returns a real result to the JS caller either way.</summary>
    public string ImportLocalSongFromWeb() => RunOnUiSync(() =>
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Audio files (*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac)|*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac|All files (*.*)|*.*",
            Title = "Choose a song to import",
        };
        if (ofd.ShowDialog(this) != DialogResult.OK)
            return System.Text.Json.JsonSerializer.Serialize(new { success = false, cancelled = true });

        var name = PromptDialog.Show(this, "Name Your Track", "Enter a name for this track:");
        if (string.IsNullOrWhiteSpace(name))
            return System.Text.Json.JsonSerializer.Serialize(new { success = false, cancelled = true });

        using var trimmer = new TrimmerForm(this, ofd.FileName, presetSongName: name);
        if (trimmer.ShowDialog(this) != DialogResult.OK || trimmer.SavedFilePath == null)
            return System.Text.Json.JsonSerializer.Serialize(new { success = false, cancelled = true });

        return System.Text.Json.JsonSerializer.Serialize(new { success = true, path = trimmer.SavedFilePath, name });
    });
}
