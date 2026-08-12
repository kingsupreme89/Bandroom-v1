using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Bandroom.Core;
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
    static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac", ".ogg" };
    static readonly string[] CategoryOrder = { "Offense", "Defense", "Situations" };
    const int ResizeMargin = 6; // shared between the Form's Padding and the WM_NCHITTEST edge test

    List<TriggerEntry> _config = new();
    readonly KeyboardHook _hook = new();
    readonly GameWatcher _watcher = new();
    readonly LocalOverlayServer _overlayServer = new();
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

    /// <summary>Owner request 2026-08-11: three switchable song-assignment PRESETS per team --
    /// "Home", "Away", "Big Game" -- instead of just one profile. Null means the plain/base profile
    /// (unchanged pre-existing behavior); "Home"/"Away"/"BigGame" means _config is currently editing
    /// that team's alternate preset instead. Stored as a composite profile name via
    /// TeamPresetProfileKey (e.g. "Georgia · Away") through the SAME ConfigStore.SaveProfile/
    /// LoadProfile a plain team profile already uses -- presets are just extra named profiles, no
    /// new storage format. Reset to null on every team switch (SelectTeamFromWeb) so switching
    /// teams always lands back on the plain base profile, matching pre-preset behavior exactly
    /// unless the user explicitly picks a preset pill.</summary>
    string? _activeTeamPreset;

    /// <summary>preset is one of "Home"/"Away"/"BigGame" (identifiers, matching the JS pill
    /// values) -- translated to a nicer-spaced display name only in the composite profile key
    /// used for on-disk storage/the Save/Load Profile list.</summary>
    static string TeamPresetProfileKey(string teamName, string? preset) => preset switch
    {
        null or "" => teamName,
        "BigGame" => $"{teamName} · Big Game",
        _ => $"{teamName} · {preset}"
    };

    /// <summary>Which profile GAMEPLAY should actually use for a side once the matchup is
    /// confirmed -- "BigGame" preset if the Big Game toggle is on (ConfigStore.BigGameSettings,
    /// same flag GameWatcher.IsBigGame reads), else "Home"/"Away" depending which side of THIS
    /// game teamName is on. Falls back to the plain base profile's OWN key if that preset was
    /// never saved for this team (does NOT check the plain profile actually exists on disk --
    /// callers use LoadGameplayProfile below, which handles that fallback safely).</summary>
    static string GameplayProfileKey(string teamName, bool isHomeSide)
    {
        string preset = ConfigStore.LoadBigGameSettings().Enabled ? "BigGame" : (isHomeSide ? "Home" : "Away");
        string key = TeamPresetProfileKey(teamName, preset);
        return ConfigStore.ListProfiles().Contains(key, StringComparer.OrdinalIgnoreCase) ? key : teamName;
    }

    /// <summary>Loads whatever GameplayProfileKey resolves to, falling all the way back to
    /// BuildDefault() if even the plain team profile doesn't exist yet (brand new team) -- same
    /// safety net SetGameTeamsFromWeb always had before presets existed, just centralized here so
    /// both SetGameTeamsFromWeb and RefreshHomeAwayConfigIfNeeded share it instead of one of them
    /// risking a raw LoadProfile() throw on a team with nothing saved at all.</summary>
    static List<TriggerEntry> LoadGameplayProfile(string teamName, bool isHomeSide)
    {
        string key = GameplayProfileKey(teamName, isHomeSide);
        return ConfigStore.ListProfiles().Contains(key, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(key)
            : ConfigStore.BuildDefault();
    }

    /// <summary>True from the moment GAMETIME is pressed until watching is stopped. While
    /// locked, _homeTeam/_awayTeam (and therefore which physical side OCR-detected events route
    /// to) can't change -- only the SONGS assigned to each side can, via the Home/Away toggle.
    /// This matches the real workflow: you set the matchup once at kickoff, then Stop Watching
    /// is the one signal that means "this game is over, I might pick a different matchup next."</summary>
    bool _matchupLocked;
    bool _useEngineForEvents;

    public WebMainForm()
    {
        // BUG FIX (Session 11, urgent/live): window rendered far too small on scaled/high-res
        // displays (Discord report, v1.0.50, "very small screen when DLing the most recent
        // release" -- screenshot showed the whole app shrunk into a tiny corner of the screen).
        // Root cause: this form never set AutoScaleMode, so WinForms defaulted to
        // AutoScaleMode.Font -- which scales controls by a font-metric ratio computed against
        // whatever font size the OS's current DPI happens to produce, NOT the actual per-monitor
        // scale factor. Program.cs already opts the whole app into
        // Application.SetHighDpiMode(HighDpiMode.PerMonitorV2) (for OCR capture math -- see its
        // own comment on GetWindowRect/CopyFromScreen needing physical pixels), but a
        // PerMonitorV2-aware app needs AutoScaleMode.Dpi on every form, not Font -- Font-based
        // autoscale and PerMonitorV2 DPI awareness are two different, incompatible scaling
        // systems, and mixing them is a well-documented WinForms footgun whose classic symptom is
        // exactly this: the window renders far smaller than its designed size. Explicitly set to
        // Dpi so WinForms scales this form using the real per-monitor DPI value instead of a
        // mismatched font-metric guess. MUST be set before any Size/Width/Height assignment below
        // -- WinForms captures AutoScaleDimensions from the form's state at the point autoscale
        // is applied (on handle creation / Load), and changing the mode after sizing has already
        // been requested doesn't retroactively fix a scale pass that already ran wrong.
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Bandroom";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        TopMost = ConfigStore.LoadAppWindowSettings().AlwaysOnTop;

        // FIXED 2026-08-12 (owner report: fade-out volume setting doesn't survive app close and
        // relaunch) -- ConfigStore.SavePlaybackTimingSettings has existed since the Settings-panel
        // migration (see PlaybackTimingSettings' own doc comment), and DOES write these 4 fields to
        // disk correctly. But nothing ever called LoadPlaybackTimingSettings back at startup to
        // re-apply the saved values to AudioPlayer's static fields -- the save path was write-only,
        // so every relaunch silently fell back to AudioPlayer's hardcoded C# defaults (10s fade
        // delay, 4.5s fade duration, 0s pre-roll, 2s cooldown) regardless of what was saved.
        var savedTiming = ConfigStore.LoadPlaybackTimingSettings();
        AudioPlayer.PreRollSeconds = savedTiming.PreRollSeconds;
        AudioPlayer.FadeStartSeconds = savedTiming.FadeStartSeconds;
        AudioPlayer.FadeOutDuration = savedTiming.FadeOutDuration;
        GameWatcher.Cooldown = TimeSpan.FromSeconds(savedTiming.CooldownSeconds);

        // BUG FIX (owner report, 2026-08-10): window was hardcoded to 1920x1080 with
        // CenterScreen, with nothing anywhere clamping it to the actual monitor. On any display
        // smaller than 1920x1080 (or a multi-monitor rig where the primary isn't that size), the
        // window is simply bigger than the screen it opens on, so it visibly spills onto a second
        // monitor -- and Maximize can't "fix" it because the window itself is still oversized for
        // one screen's working area, so content gets clipped with no way to reach it. This is also
        // the likely cause of the "song picker never pops up" report: AssignTrackForm opens with
        // StartPosition.CenterParent against this window as owner, so if this window's bounds
        // straddle/exceed the visible desktop, the dialog can center itself into the off-screen
        // portion -- it *is* opening, just invisible off-screen. Clamp to the working area (not
        // full bounds -- excludes the taskbar) of whichever screen the app is starting on.
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Width = Math.Min(1920, workingArea.Width);
        Height = Math.Min(1080, workingArea.Height);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(Math.Min(1200, workingArea.Width), Math.Min(650, workingArea.Height));
        BackColor = Theme.WindowBg;
        FormBorderStyle = FormBorderStyle.None; // chrome bar is drawn in HTML now, no OS titlebar/URL bar
        KeyPreview = true;

        // BUG FIX (owner report, 2026-08-10): edge-drag resize (see the WM_NCHITTEST override
        // below) never actually worked because WebView2 was Dock.Fill covering every pixel,
        // including the outer edge -- so the mouse was always over the WebView2 child window at
        // the border, not the Form, and WM_NCHITTEST (a non-client message resolved per-window by
        // screen point) went to WebView2 and got swallowed instead of reaching the Form's
        // override. Give the Form a thin owned strip around the WebView2 matching the hit-test
        // margin below, so those edge pixels actually belong to the Form and the resize drag can
        // be grabbed.
        Padding = new Padding(ResizeMargin);

        _webView = new WebView2 { Dock = DockStyle.Fill };
        ((Control)_webView).AllowDrop = true;
        _webView.DragEnter += OnSongDragEnter;
        _webView.DragDrop += OnSongDragDrop;
        Controls.Add(_webView);

        ConfigStore.MigrateFromVersionedFolderIfNeeded();
        _config = ConfigStore.LoadOrCreate();

        _hook.KeyCombo += OnKeyCombo;
        _hook.Cutoff += OnCutoff;
        _watcher.WindowFoundChanged += OnWindowFoundChanged;
        _watcher.DownChanged += OnDownChanged;
        _watcher.RegionChanged += OnRegionChanged;
        _watcher.PossessionChanged += side => _possession = side;
        _watcher.TackleForLossDetected += OnTackleForLoss;
        _watcher.EventsDetected += OnEngineEventsDetected;
        _watcher.ResolveTeamColor = ResolveTeamColor;
        // Engine is now always active — always run the rule engine evaluators.
        // PossessionChanged still fires (it updates _possession for the engine to use).
        // Was gated on _homeConfig/_awayConfig being non-null, which silently killed
        // all events until "Set Matchup" was pressed. Fixed 2026-08-07.
        _useEngineForEvents = true;
        _watcher.Log += OnLog;
        _watcher.ActivePreset = ScorebugPreset.GetByName(ConfigStore.LoadScorebugPresetName());

        // Lead-in whistle: the clip itself (if one was ever set via TrimmerForm's "Set as
        // Lead-In Whistle") lives at a fixed path and survives restarts on its own; only the
        // on/off toggle needs its own persisted flag (see ConfigStore.LoadLeadInWhistleEnabled).
        if (File.Exists(ConfigStore.LeadInWhistlePath))
        {
            AudioPlayer.LeadInClipPath = ConfigStore.LeadInWhistlePath;
            AudioPlayer.LeadInEnabled = ConfigStore.LoadLeadInWhistleEnabled();
        }

        // Volume sliders (Master/Home/Away/PA/Whistle) previously reset to 100% every launch --
        // see ConfigStore.AudioSettings' own doc comment for the full gap. Apply the saved
        // percentages (or defaults, first run) to AudioPlayer's in-memory volume state once here;
        // each SetXVolumeFromWeb setter persists back on every change.
        var savedAudio = ConfigStore.LoadAudioSettings();
        AudioPlayer.MasterVolume = savedAudio.MasterVolume / 100f;
        AudioPlayer.HomeVolume = savedAudio.HomeVolume / 100f;
        AudioPlayer.AwayVolume = savedAudio.AwayVolume / 100f;
        AudioPlayer.PaVolume = savedAudio.PaVolume / 100f;
        AudioPlayer.WhistleVolume = savedAudio.WhistleVolume / 100f;

        NormalizeExistingLibraryOnce();

        _overlayServer.Start();
        FormClosing += (_, _) => { _hook.Stop(); _watcher.Stop(); FlushOcrLog(); _overlayServer.Stop(); };

        Load += async (_, _) =>
        {
            await InitWebViewAsync();
            InitAutoUpdater();
            UserCountService.StartHeartbeat(_lifetimeCts.Token);
            PlayDraftChime();
            // Public (everyone-sees-it) team logo sync -- fire-and-forget, works whether or not
            // this device is signed in (pulling the public index needs no auth, only pushing
            // does). Refreshes whatever team is currently showing so a newly-applied public logo
            // shows up without needing a restart.
            _ = SyncPublicTeamLogosAsync();
        };
        // AUDIT FIX (Admin-2 pass): Cancel() alone never Dispose()s the CTS -- harmless in
        // practice since the process exits right after, but a real leak if that ever changes
        // (e.g. a future "restart WebView without exiting" path). Dispose() after Cancel() is
        // always safe here since nothing observes _lifetimeCts.Token past FormClosing.
        FormClosing += (_, _) => { _lifetimeCts.Cancel(); _lifetimeCts.Dispose(); };
    }

    /// <summary>Best-effort startup pass for public (everyone-sees-it) team logos -- see
    /// PublicTeamLogoSyncService.SyncAsync for the actual pull/guardrail logic. Only tells the web
    /// UI to refresh if something actually changed, so this never fires an unnecessary repaint on
    /// the far more common case of "nothing new since last launch".</summary>
    async Task SyncPublicTeamLogosAsync()
    {
        try
        {
            var updated = await PublicTeamLogoSyncService.SyncAsync(_lifetimeCts.Token);
            if (updated.Count > 0)
                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:publiclogosupdated'))"));
        }
        catch (Exception ex)
        {
            CrashLog.Write("SyncPublicTeamLogosAsync failed", ex);
        }
    }

#if DEBUG
    /// <summary>Kills any msedgewebview2.exe process whose command line references OUR OWN
    /// WebView2Data folder -- i.e. an orphan left over from a previous Bandroom.exe that was
    /// force-killed (dev-loop `taskkill` during a rebuild) rather than closed cleanly, so
    /// WebView2's normal child-process cleanup on graceful exit never ran. An orphan like this
    /// can keep the profile's cache files locked/serving stale content independent of a brand
    /// new process's own Network.clearBrowserCache call, which is what actually caused a real
    /// CSS edit not to show up after a full rebuild+relaunch (confirmed live). Matches ONLY
    /// processes whose command line contains this exact folder path -- deliberately does NOT
    /// touch other apps' msedgewebview2.exe processes (Windows Search/Widgets both run their
    /// own, confirmed via Get-CimInstance during manual debugging this same session).</summary>
    static void KillOrphanedWebView2Processes(string userDataFolder)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");
            foreach (System.Management.ManagementObject proc in searcher.Get())
            {
                var cmdLine = proc["CommandLine"] as string;
                if (string.IsNullOrEmpty(cmdLine) || !cmdLine.Contains(userDataFolder, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    int pid = Convert.ToInt32(proc["ProcessId"]);
                    System.Diagnostics.Process.GetProcessById(pid).Kill();
                }
                catch { /* already exited, or access denied -- not worth failing startup over */ }
            }
        }
        catch (Exception ex)
        {
            // WMI can be unavailable/slow in some environments -- never block startup over this.
            CrashLog.Write("KillOrphanedWebView2Processes failed", ex);
        }
    }
#endif

    async Task InitWebViewAsync()
    {
        string userDataFolder = Path.Combine(AppContext.BaseDirectory, "WebView2Data");
#if DEBUG
        KillOrphanedWebView2Processes(userDataFolder);
#endif
        Directory.CreateDirectory(userDataFolder);
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        var core = _webView.CoreWebView2;
#if DEBUG
        // BUG FIX: with context menus AND browser accelerator keys (F12) both disabled below,
        // there was NO way to open DevTools in this app at all -- every live rendering mystery
        // this session had to be diagnosed blind, by re-reading source and hoping. Debug-only:
        // keep the right-click context menu (which includes "Inspect") so DevTools stays reachable.
        core.Settings.AreDefaultContextMenusEnabled = true;
#else
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

#if DEBUG
        // BUG FIX: userDataFolder above is a persistent on-disk profile (WebView2Data next to the
        // exe), so Chromium's HTTP disk cache survives across app relaunches. index.html's
        // <link>/<script> tags for style.css/app.js have no cache-busting query string, and the
        // virtual-host-mapped wwwroot doesn't set no-cache response headers -- so an edited
        // style.css/app.js on disk could keep getting served stale after a rebuild+relaunch,
        // intermittently depending on Chromium's freshness heuristics (confirmed live: a real CSS
        // change to the Sound Booth knobs didn't show up after a full rebuild+relaunch until this
        // fix). Debug-only: clearing the cache on every launch is wasted work for real users who
        // aren't editing wwwroot files between launches.
        try { await core.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}"); }
        catch (Exception ex) { CrashLog.Write("WebView2 cache clear failed", ex); }
#endif

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
        Directory.CreateDirectory(ConfigStore.TrimSourceFolder);
        core.SetVirtualHostNameToFolderMapping("trimsrc", ConfigStore.TrimSourceFolder, CoreWebView2HostResourceAccessKind.Allow);

        core.AddHostObjectToScript("bandroom", new WebBridge(this));

        // AUDIT FIX (Admin-2 pass): nothing was watching for the WebView2 render process itself
        // dying mid-session (renderer crash/OOM, or the whole browser process going away) -- with
        // no handler, that leaves the window showing a permanently frozen/blank page with the JS
        // bridge gone and no way back short of relaunching the whole app. RenderProcessExited is
        // recoverable (the browser process survives; reloading respins a renderer and the
        // host-object/virtual-host mappings above are unaffected since those live on `core`, not
        // the renderer). A full BrowserProcessExited is not recoverable from here -- log it and
        // leave the window as-is rather than looping on a Reload() that can't succeed.
        core.ProcessFailed += (_, e) =>
        {
            CrashLog.Write($"WebView2 process failed: kind={e.ProcessFailedKind}", new Exception(e.Reason.ToString()));
            if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited) return;
            RunOnUi(() => { try { core.Reload(); } catch (Exception ex) { CrashLog.Write("WebView2 reload after crash failed", ex); } });
        };

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

    /// <summary>Sets a team's background from a My Downloads image that's already local (id
    /// looked up in the marketplace-download manifest) -- the "Set as Background" action on the
    /// My Downloads grid, distinct from DownloadAndSetTeamBackgroundFromWeb above which fetches
    /// a fresh marketplace URL over HTTP.</summary>
    public bool SetTeamBackgroundFromDownloadFromWeb(string downloadId)
    {
        var entry = ConfigStore.LoadMarketplaceDownloads().FirstOrDefault(e => e.Id == downloadId && e.Type == "image");
        if (entry == null) return false;
        string? saved = TeamBackgroundDownloadService.SetFromLocalFile(entry.School ?? "", entry.Path);
        if (saved != null && Theme.ActiveTeam.Name == entry.School)
            RunOnUi(() => _ = _webView.ExecuteScriptAsync($"window.applyBackground && window.applyBackground({JsonSerializer.Serialize(entry.School)})"));
        return saved != null;
    }

    public void SelectTeamFromWeb(string name)
    {
        var team = TeamColors.All.FirstOrDefault(t => t.Name == name);
        if (team.Name == null || team.Name == Theme.ActiveTeam.Name) return;
        SaveCurrentTeamProfile();

        Theme.ActiveTeam = team;
        _activeTeamPreset = null; // switching teams always lands back on the plain base profile
        _config = ConfigStore.ListProfiles().Contains(team.Name, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(team.Name)
            : ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
        PushCategories();
    }

    /// <summary>The three preset pills under the active team's editing bar. preset is ""
    /// (plain/base profile) or "Home"/"Away"/"BigGame". Saves whatever was being edited under the
    /// PREVIOUS preset first (so switching pills never silently drops in-progress edits), then
    /// loads the target preset -- falling back to the team's plain base profile as a starting
    /// point if that preset hasn't been saved yet (so switching to an unused preset for the first
    /// time starts from what the user already has, not a blank slate), and finally BuildDefault if
    /// even that doesn't exist. Returns which preset ended up active (mirrors the input, but lets
    /// the JS side stay in sync without a separate getter).</summary>
    public string SwitchTeamPresetFromWeb(string preset)
    {
        SaveCurrentTeamProfile();
        _activeTeamPreset = string.IsNullOrEmpty(preset) ? null : preset;
        string key = TeamPresetProfileKey(Theme.ActiveTeam.Name, _activeTeamPreset);
        var profiles = ConfigStore.ListProfiles();
        _config = profiles.Contains(key, StringComparer.OrdinalIgnoreCase) ? ConfigStore.LoadProfile(key)
            : profiles.Contains(Theme.ActiveTeam.Name, StringComparer.OrdinalIgnoreCase) ? ConfigStore.LoadProfile(Theme.ActiveTeam.Name)
            : ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
        PushCategories();
        return _activeTeamPreset ?? "";
    }

    /// <summary>Copies one preset onto another for the ACTIVE team ("copy Big Game preset to
    /// Home", owner's own example) -- same load-then-save-under-a-different-name shape as
    /// DuplicateProfileFromWeb above, just scoped to this team's own presets instead of two
    /// different teams. fromPreset/toPreset are each "" (plain/base) or "Home"/"Away"/"BigGame".
    /// If the destination is the preset currently being edited, refreshes _config immediately so
    /// the UI reflects the copy without needing a manual pill re-click.</summary>
    public bool CopyTeamPresetFromWeb(string fromPreset, string toPreset)
    {
        string teamName = Theme.ActiveTeam.Name;
        string fromKey = TeamPresetProfileKey(teamName, string.IsNullOrEmpty(fromPreset) ? null : fromPreset);
        string toKey = TeamPresetProfileKey(teamName, string.IsNullOrEmpty(toPreset) ? null : toPreset);
        if (!ConfigStore.ListProfiles().Contains(fromKey, StringComparer.OrdinalIgnoreCase)) return false;

        var entries = ConfigStore.LoadProfile(fromKey);
        ConfigStore.SaveProfile(toKey, entries);

        string activeKey = TeamPresetProfileKey(teamName, _activeTeamPreset);
        if (string.Equals(toKey, activeKey, StringComparison.OrdinalIgnoreCase))
        {
            _config = entries;
            ConfigStore.Save(_config);
            PushCategories();
        }
        RefreshHomeAwayConfigIfNeeded(teamName);
        return true;
    }

    /// <summary>Which of the active team's three presets actually have a saved profile on disk --
    /// drives the "configured" dot on each pill (same idea as .team-swatch.configured elsewhere).
    /// JSON object: {"Home":bool,"Away":bool,"BigGame":bool}.</summary>
    public string GetTeamPresetStatusFromWeb()
    {
        string teamName = Theme.ActiveTeam.Name;
        var profiles = ConfigStore.ListProfiles();
        var status = new Dictionary<string, bool>
        {
            ["Home"] = profiles.Contains(TeamPresetProfileKey(teamName, "Home"), StringComparer.OrdinalIgnoreCase),
            ["Away"] = profiles.Contains(TeamPresetProfileKey(teamName, "Away"), StringComparer.OrdinalIgnoreCase),
            ["BigGame"] = profiles.Contains(TeamPresetProfileKey(teamName, "BigGame"), StringComparer.OrdinalIgnoreCase),
        };
        return JsonSerializer.Serialize(status);
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

    /// <summary>Copies fromTeam's saved profile onto toTeam ("Duplicate Profile To..." context
    /// menu item, wwwroot/app.js duplicateTeamProfile). Was calling a bridge method that didn't
    /// exist (bridge.DuplicateProfile) -- WebView2 throws on an unresolved host-object call,
    /// which the JS caller's .catch() surfaced as a generic "Failed to duplicate profile" toast,
    /// but under some call paths (right-click before the context menu's own try/catch settled)
    /// this reached the user as an unhandled exception instead. Returns false rather than
    /// throwing if fromTeam has no saved profile, so the JS side's existing catch/toast handles
    /// it the same way as any other failure.</summary>
    public bool DuplicateProfileFromWeb(string fromTeam, string toTeam)
    {
        if (string.IsNullOrWhiteSpace(fromTeam) || string.IsNullOrWhiteSpace(toTeam)) return false;
        if (!ConfigStore.ListProfiles().Contains(fromTeam, StringComparer.OrdinalIgnoreCase)) return false;

        var entries = ConfigStore.LoadProfile(fromTeam);
        ConfigStore.SaveProfile(toTeam, entries);

        // If the destination is the currently active/loaded team, refresh in-memory state so the
        // UI reflects the duplicated profile immediately instead of only on next reload.
        if (string.Equals(toTeam, Theme.ActiveTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            _config = entries;
            ConfigStore.Save(_config);
            PushCategories();
        }
        RefreshHomeAwayConfigIfNeeded(toTeam);
        return true;
    }

    /// <summary>Task queue item 6 (Session 11): SetGameTeamsFromWeb below already silently
    /// auto-fills an empty team profile from the default pack the moment a matchup is confirmed
    /// (see its own "Auto-assign default songs ONLY if empty" comment) -- that safety net is left
    /// completely untouched here, still fires exactly as before regardless of this method, so
    /// in-game audio never regresses. This is a NEW, purely informational check the JS side calls
    /// BEFORE confirming the matchup, so the previously-silent auto-fill becomes a visible,
    /// explicit choice: "these teams have nothing assigned yet, continuing will auto-fill them
    /// from the Default Song Pack -- or cancel to assign songs yourself first." Mirrors the exact
    /// same "has assignments" check SetGameTeamsFromWeb uses, just read-only.</summary>
    public List<string> GetTeamsNeedingDefaultProfileFromWeb(string homeName, string awayName)
    {
        var needsDefault = new List<string>();
        foreach (var name in new[] { homeName, awayName })
        {
            var config = ConfigStore.ListProfiles().Contains(name, StringComparer.OrdinalIgnoreCase)
                ? ConfigStore.LoadProfile(name) : ConfigStore.BuildDefault();
            if (!config.Any(e => !string.IsNullOrWhiteSpace(e.AudioFile)))
                needsDefault.Add(name);
        }
        return needsDefault;
    }

    /// <summary>Task queue item 6 -- explicit opt-in counterpart to GetTeamsNeedingDefaultProfileFromWeb
    /// above. Reuses ConfigStore.ImportDefaultPackForTeam (fills empty slots only, never
    /// overwrites) against whatever profile the team already has (or ConfigStore.BuildDefault if
    /// none), then saves it -- same shape as SetGameTeamsFromWeb's own silent safety-net below,
    /// just callable directly. Returns the count assigned so the JS side can show a real number
    /// ("Filled 28 songs") instead of a generic success toast.</summary>
    public int ApplyDefaultProfileForTeamFromWeb(string teamName)
    {
        var config = ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(teamName) : ConfigStore.BuildDefault();
        int assigned = ConfigStore.ImportDefaultPackForTeam(teamName, config);
        if (assigned > 0) ConfigStore.SaveProfile(teamName, config);
        RefreshHomeAwayConfigIfNeeded(teamName);
        RefreshActiveConfigIfNeeded(teamName, config);
        return assigned;
    }

    /// <summary>Overwrite variant of ApplyDefaultProfileForTeamFromWeb (Events-page Auto-Assign
    /// "Overwrite" confirm flow) -- replaces EVERY slot the default pack has a file for, not just
    /// empty ones. JS is responsible for getting an explicit yes/no from the user before calling
    /// this; nothing here re-confirms.</summary>
    public int ApplyDefaultProfileForTeamOverwriteFromWeb(string teamName)
    {
        var config = ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(teamName) : ConfigStore.BuildDefault();
        int assigned = ConfigStore.ImportDefaultPackForTeam(teamName, config, overwrite: true);
        if (assigned > 0) ConfigStore.SaveProfile(teamName, config);
        RefreshHomeAwayConfigIfNeeded(teamName);
        RefreshActiveConfigIfNeeded(teamName, config);
        return assigned;
    }

    /// <summary>Auto-Assign's "Load Conference Pack" option -- owner-requested alternative to the
    /// team-specific default pack: fills from files sitting directly in the team's CONFERENCE
    /// folder (shared chants/hype cues not tied to one school) instead of that team's own
    /// subfolder. overwrite=true replaces every slot the conference pack has a file for (same
    /// "JS gets an explicit yes/no first" contract as ApplyDefaultProfileForTeamOverwriteFromWeb);
    /// overwrite=false only backfills empty slots, meant to run as a second pass after the
    /// team-specific pack so a team's own real songs always win over generic conference ones.</summary>
    public int ApplyConferencePackForTeamFromWeb(string teamName, bool overwrite)
    {
        var config = ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(teamName) : ConfigStore.BuildDefault();
        int assigned = ConfigStore.ImportConferencePackForTeam(teamName, config, overwrite);
        if (assigned > 0) ConfigStore.SaveProfile(teamName, config);
        RefreshHomeAwayConfigIfNeeded(teamName);
        RefreshActiveConfigIfNeeded(teamName, config);
        return assigned;
    }

    /// <summary>Preview step for "Load Conference Pack" -- lists every event the conference folder
    /// has a file for, including what's already assigned (if anything), WITHOUT changing the
    /// profile. JS uses this to prompt per already-assigned event before calling
    /// ApplyConferencePackSelectionsFromWeb with the confirmed set -- most users already have some
    /// songs assigned, so silently skipping those (the old backfill-only default) meant the button
    /// often did nothing visible for them.</summary>
    public string PreviewConferencePackForTeamFromWeb(string teamName)
    {
        var config = ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(teamName) : ConfigStore.BuildDefault();
        var items = ConfigStore.PreviewConferencePackForTeam(teamName, config);
        // Record properties are PascalCase in C# (EventKey, FileName, ...) but every other
        // JS-facing JSON payload in this bridge uses lowerCamelCase keys (anonymous types with
        // lowercase local names) -- app.js reads item.eventKey/item.fileName/item.currentFile, so
        // this needs the same casing rather than the PascalCase System.Text.Json emits by default
        // for a record's real property names.
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        return System.Text.Json.JsonSerializer.Serialize(items, opts);
    }

    /// <summary>Applies exactly the EventKeys the user confirmed from the conference-pack preview
    /// (JSON string array) -- see ConfigStore.ApplyConferencePackSelections.</summary>
    public int ApplyConferencePackSelectionsFromWeb(string teamName, string eventKeysJson)
    {
        var config = ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(teamName) : ConfigStore.BuildDefault();
        var eventKeys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(eventKeysJson) ?? new List<string>();
        int assigned = ConfigStore.ApplyConferencePackSelections(teamName, config, eventKeys);
        if (assigned > 0) ConfigStore.SaveProfile(teamName, config);
        RefreshHomeAwayConfigIfNeeded(teamName);
        RefreshActiveConfigIfNeeded(teamName, config);
        return assigned;
    }

    /// <summary>BUG FIX (found via pre-release audit): _config -- the in-memory snapshot behind
    /// both the visible category/situation counts AND every autosave path (SaveCurrentTeamProfile,
    /// AssignTrack, etc) -- was only ever refreshed by SelectTeamFromWeb/DuplicateProfileFromWeb/
    /// ImportProfileFromWeb, never by the Auto-Assign fill/overwrite calls above. Auto-Assigning
    /// the CURRENTLY ACTIVE team wrote the new assignments to disk correctly, but left the stale
    /// pre-Auto-Assign _config sitting in memory -- the very next autosave (e.g. tweaking one
    /// unrelated song) would silently overwrite disk with that stale copy, undoing the Auto-Assign
    /// with no error or warning. Harmless for the old fill-only behavior (re-running it was a
    /// no-op either way), but a real data-loss risk once Auto-Assign became a destructive
    /// overwrite. Mirrors the same "if this is the active team, sync _config + PushCategories()"
    /// pattern DuplicateProfileFromWeb/ImportProfileFromWeb already use elsewhere in this file.</summary>
    void RefreshActiveConfigIfNeeded(string teamName, List<TriggerEntry> newConfig)
    {
        if (!string.Equals(teamName, Theme.ActiveTeam.Name, StringComparison.OrdinalIgnoreCase)) return;
        _config = newConfig;
        PushCategories();
    }

    /// <summary>Matchup picker (JS) calls this with both teams for the game about to be
    /// watched. Loads each team's OWN saved profile if they have one (falls back to defaults
    /// otherwise) -- these are separate from _config/Theme.ActiveTeam, which stay pointed at
    /// whatever the sidebar/background is currently showing.</summary>
    /// <summary>See GameWatcher.UserTeamOnLeftSide's doc comment -- CFB27 draws teams by the
    /// game's own home/away stadium assignment, which is independent of which team the user
    /// picked as "their side" here. Must be called before SetGameTeamsFromWeb/
    /// ConfirmGametimeFromWeb resets _possession to null and (re)starts sampling, or the flag
    /// won't be in effect for the very first possession read of the game.</summary>
    public void SetUserTeamScreenSideFromWeb(bool onLeftSide) => _watcher.UserTeamOnLeftSide = onLeftSide;

    public void SetGameTeamsFromWeb(string homeName, string awayName)
    {
        _homeTeam = TeamColors.All.FirstOrDefault(t => t.Name == homeName);
        _awayTeam = TeamColors.All.FirstOrDefault(t => t.Name == awayName);
        // GameplayProfileKey picks each team's "BigGame" preset if the Big Game toggle is on,
        // else their "Home"/"Away" preset for whichever side they're actually on this game --
        // falling back to the plain base profile for a team with no presets saved.
        _homeConfig = LoadGameplayProfile(homeName, isHomeSide: true);
        _awayConfig = LoadGameplayProfile(awayName, isHomeSide: false);
        _watcher.UserIsHome = true; // The user's selected team in the matchup picker IS the home team
        _watcher.HomeTeamName = homeName;
        _watcher.AwayTeamName = awayName;
        _watcher.HomeTeamMascot = _homeTeam?.Mascot;
        _watcher.AwayTeamMascot = _awayTeam?.Mascot;
        _useEngineForEvents = true; // Enable engine-driven event routing now that matchup is confirmed
        _possession = null;

        // Auto-assign default songs ONLY if this team's profile is completely empty
        // (no user-assigned songs yet). Never overwrite custom assignments.
        bool homeHasAssignments = _homeConfig.Any(e => !string.IsNullOrWhiteSpace(e.AudioFile));
        bool awayHasAssignments = _awayConfig.Any(e => !string.IsNullOrWhiteSpace(e.AudioFile));
        int homeAssigned = 0, awayAssigned = 0;
        if (!homeHasAssignments) homeAssigned = ConfigStore.ImportDefaultPackForTeam(homeName, _homeConfig);
        if (!awayHasAssignments) awayAssigned = ConfigStore.ImportDefaultPackForTeam(awayName, _awayConfig);
        // Save back under the SAME key that was actually loaded (GameplayProfileKey) -- not just
        // the bare team name -- so a default-pack fill on an empty preset lands back in that
        // preset's own file, not a different one RefreshHomeAwayConfigIfNeeded would never look at.
        if (homeAssigned > 0) ConfigStore.SaveProfile(GameplayProfileKey(homeName, isHomeSide: true), _homeConfig);
        if (awayAssigned > 0) ConfigStore.SaveProfile(GameplayProfileKey(awayName, isHomeSide: false), _awayConfig);
    }

    /// <summary>The GAMETIME button. Same wiring as SetGameTeamsFromWeb, plus the confirmation
    /// chime and the lock -- meant to be pressed once, while still on CFB 27's own team-select
    /// screen, right before kickoff.</summary>
    public void ConfirmGametimeFromWeb(string homeName, string awayName)
    {
        SetGameTeamsFromWeb(homeName, awayName);
        _matchupLocked = true;
        StartWatchingIfMatchupSet(); // GAMETIME locks the matchup AND starts watching in one press
        PlayGametimeSound();
        RecordGameWatched(homeName, awayName);
        ConfigStore.SaveLastMatchup(homeName, awayName);
    }

    /// <summary>JSON for the matchup dialog's "Last: Away @ Home" pill (see
    /// ConfigStore.LoadLastMatchup) -- null/empty-object shape when no GAMETIME has ever been
    /// confirmed yet, so the dialog can hide the pill entirely rather than showing a broken one.</summary>
    public string GetLastMatchupFromWeb()
    {
        var last = ConfigStore.LoadLastMatchup();
        return last == null ? "{}" : JsonSerializer.Serialize(new { home = last.HomeName, away = last.AwayName });
    }

    /// <summary>Unlocks the matchup (so Set Matchup can pick different teams) WITHOUT stopping
    /// the OCR watcher/input hook -- previously the only way to unlock was Stop Watching, which
    /// also kills the live capture session entirely. Lets the owner correct a wrong matchup pick
    /// mid-session without losing the watcher state.</summary>
    public void UnlockMatchupFromWeb() => _matchupLocked = false;

    /// <summary>Bumps the universal profile's lifetime "games watched" counter, per-team
    /// breakdown, and daily streak -- see ConfigStore.UserProfile. Fire-and-forget cloud sync,
    /// local save is what actually matters here since it must never delay/interrupt GAMETIME
    /// locking in.</summary>
    static void RecordGameWatched(string homeName, string awayName)
    {
        var updated = ConfigStore.MutateUserProfile(current =>
        {
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

            return current with
            {
                GamesWatched = current.GamesWatched + 1,
                GamesWatchedByTeam = byTeam,
                StreakCurrentDays = streak,
                StreakLastActiveDate = today,
            };
        });
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
        var updated = ConfigStore.MutateUserProfile(current =>
        {
            var eventCounts = new Dictionary<string, int>(current.EventCounts);
            if (!string.IsNullOrWhiteSpace(eventName))
                eventCounts[eventName] = eventCounts.GetValueOrDefault(eventName) + 1;
            return current with { SongsTriggered = current.SongsTriggered + 1, EventCounts = eventCounts };
        });

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

    /// <summary>Pre-engine profiles have real songs assigned under the old bare "1st/2nd/3rd
    /// Down" (Trigger-keyed down:1st/2nd/3rd) slots -- dead since _useEngineForEvents went
    /// permanent-true (OnDownChanged, the only thing that ever fired them, now always bails).
    /// These three map 1:1 onto the engine's offense-side equivalent (same "my team is driving"
    /// meaning), so a canonical miss falls back to the matching legacy trigger rather than losing
    /// an already-assigned file. "4th Down" now has an engine equivalent too (OffenseDownHelper
    /// added "Offense: Fourth Down" 2026-08-08, STATE_MACHINE_ANALYSIS.md Discrepancy #10) --
    /// aliased here the same way, so an already-assigned legacy down:4th file (e.g. the shipped
    /// default "dies irie 0.wav") is reachable again instead of staying permanently silent.</summary>
    static readonly Dictionary<string, string> LegacyDownEventAlias = new()
    {
        ["Offense: Earned First Down"] = "down:1st",
        ["Offense: Second Down"] = "down:2nd",
        ["Offense: Third Down"] = "down:3rd",
        ["Offense: Fourth Down"] = "down:4th",
        // Added 2026-08-10 alongside OffenseDownHelper's short/long rewrite: a returning user's
        // pre-existing "down:2nd"/"down:3rd" legacy assignment now surfaces under the new Short
        // cards too, so their old 2nd/3rd Down song isn't silently orphaned by the split -- short
        // is the more common/exciting case, so it's the more sensible default landing spot.
        ["Offense: Second Down Short"] = "down:2nd",
        ["Offense: Third Down Short"] = "down:3rd",
    };

    /// <summary>EventKeys that got renamed after already shipping as assignable cards -- maps the
    /// CURRENT key to the OLD key, so ResolveEntryForEvent can fall back to a song someone already
    /// assigned under the old name instead of it going silently unassigned after the rename. Add an
    /// entry here every time an EventKey changes, same convention as ScorebugPreset.LegacyNameAliases.</summary>
    static readonly Dictionary<string, string> RenamedEventKeyAliases = new()
    {
        // Renamed 2026-08-11 (owner audit call) -- clearer name for what this event actually is.
        ["Defense: After Punt"] = "Defense: Drive Starter",
        // MERGED 2026-08-11 (live owner call): "Offense: 1st Down After Punt" retired as its own
        // card and folded into the regular earned-first-down cue (see DriveStarterHelper) -- if
        // "Offense: Earned First Down" itself has no song assigned, fall back to whatever was
        // already assigned under the old "1st Down After Punt" (or, one hop further back,
        // "Drive Starter") key rather than going silent.
        ["Offense: Earned First Down"] = "Offense: 1st Down After Punt",
        ["Defense: After Opening Kick"] = "Defense: First Down",
    };

    /// <summary>Returns what actually happened so callers that need visible feedback (the test
    /// hook) can tell "fired," "no song assigned," and "no matchup loaded" apart instead of all
    /// three looking identically like silence. Real engine/legacy callers ignore the return.</summary>
    TriggerEntry? ResolveEntryForEvent(string side, string eventName)
    {
        var config = side == "home" ? _homeConfig : _awayConfig;
        if (config == null) return null;

        // Try the team's own profile first, then fall back to the Generic profile
        var entry = config.FirstOrDefault(e => e.Event == eventName);
        if (entry == null || string.IsNullOrWhiteSpace(entry.AudioFile))
        {
            // Fall back to Generic profile for shared default sounds
            var generic = ConfigStore.GetGenericProfile();
            entry = generic?.FirstOrDefault(e => e.Event == eventName);
        }

        if ((entry == null || string.IsNullOrWhiteSpace(entry.AudioFile))
            && LegacyDownEventAlias.TryGetValue(eventName, out var legacyTrigger))
        {
            var legacyEntry = config.FirstOrDefault(e => e.Trigger.Equals(legacyTrigger, StringComparison.OrdinalIgnoreCase));
            if (legacyEntry != null && !string.IsNullOrWhiteSpace(legacyEntry.AudioFile))
                entry = legacyEntry;
        }

        if ((entry == null || string.IsNullOrWhiteSpace(entry.AudioFile))
            && RenamedEventKeyAliases.TryGetValue(eventName, out var oldEventName))
        {
            var renamedEntry = config.FirstOrDefault(e => e.Event == oldEventName);
            if (renamedEntry != null && !string.IsNullOrWhiteSpace(renamedEntry.AudioFile))
                entry = renamedEntry;
        }
        return entry;
    }

    // volumeMultiplier added 2026-08-10 for the Big Game away-side rule (see
    // OnEngineEventsDetected): when it's NOT a Big Game (away team only sends a small travel pep
    // band, per the owner's real band experience), away-side cues that do fire still play
    // quietly instead of at the same volume as a fully-present band. Defaults to 1 (no change)
    // so every other caller (home side, previews, tests) is unaffected.
    // interruptPrevious added 2026-08-10 for the same-tick multi-fire fix (see
    // OnEngineEventsDetected): two evaluators (e.g. Offense: Third Down Short and the new
    // Defense: Third Down Short) can now legitimately fire on the same tick, routed to opposite
    // sides. Since this app has one shared audio pipeline (AudioPlayer.StopAll, not separate
    // home/away channels), firing both with the default interruptPrevious:true would have the
    // second call's StopAll() kill the first clip almost immediately -- only the last-processed
    // event would ever actually be audible. Defaults to true (unchanged behavior for every
    // existing single-event-per-tick caller); OnEngineEventsDetected passes false for every
    // event after the first successfully-fired one in a tick's batch, same trick already used
    // for the PA announcer layer (FireEvent fires that with interruptPrevious:false so it
    // layers instead of cutting the main cue off).
    string FireEventForSide(string side, string eventName, bool bypassCooldown = false, float volumeMultiplier = 1f, bool interruptPrevious = true)
    {
        if ((side == "home" ? _homeConfig : _awayConfig) == null) return "no-profile";
        var entry = ResolveEntryForEvent(side, eventName);

        if (entry == null || string.IsNullOrWhiteSpace(entry.AudioFile)) return "unassigned";

        if (bypassCooldown) AudioPlayer.ClearCooldown(entry.AudioFile);
        if (!File.Exists(entry.AudioFile)) return "file-missing";
        FireEvent(entry, (side == "home" ? AudioPlayer.HomeVolume : AudioPlayer.AwayVolume) * volumeMultiplier, interruptPrevious, side);
        return "fired:" + Path.GetFileName(entry.AudioFile);
    }

    /// <summary>Turns FireEventForSide's terse result code ("fired:x.mp3", "unassigned",
    /// "no-profile", "file-missing") into the plain-English EventActivityLog entry a user reading
    /// the Event Log tab actually needs. Kept next to FireEventForSide since it's the only other
    /// place that needs to understand its return values.
    ///
    /// On a real fire, re-resolves the entry (cheap -- same lookup FireEventForSide just did) to
    /// check for a Track Info sidecar (AudioTrackMetadata) and, if one exists, logs the richer
    /// "Title (Category, Instrumentation)" form instead of just the raw filename -- e.g. "LSU
    /// Golden Band -- 'Neck' (Hype Sting, Heavy Brass) fired on 3rd Down" instead of
    /// "Neck_trimmed.wav". Falls back to the filename-only line when there's no sidecar, same as
    /// before this existed.</summary>
    void RecordFireResult(string eventKey, string side, string result)
    {
        string name = EventActivityLog.FriendlyEventName(eventKey);
        // Owner request 2026-08-11: the log line alone couldn't tell a Big Game fire apart from an
        // ordinary one (e.g. "2nd & Short Away" -- was this the Big Game's louder/alternate cue or
        // not?). _watcher.IsBigGame is the same flag FireEvent's own BigGameAudioFile fallback
        // already keys off (line ~2599 above), so this tag reflects exactly what actually played.
        string sideLabel = DisplaySide(side) + (_watcher.IsBigGame ? " BG" : "");
        if (result.StartsWith("fired:"))
        {
            string filename = result["fired:".Length..];
            string detail = $"played '{filename}'";
            var entry = ResolveEntryForEvent(side, eventKey);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.AudioFile))
            {
                var meta = AudioTrackMetadataStore.Load(entry.AudioFile);
                if (meta != null && (!string.IsNullOrWhiteSpace(meta.StandardTitle) || !string.IsNullOrWhiteSpace(meta.StandardArtist)))
                {
                    string title = !string.IsNullOrWhiteSpace(meta.StandardTitle) ? meta.StandardTitle! : filename;
                    string artistPart = !string.IsNullOrWhiteSpace(meta.StandardArtist) ? $"{meta.StandardArtist} -- " : "";
                    var tags = new List<string>();
                    if (!string.IsNullOrWhiteSpace(meta.MarketplaceCategory)) tags.Add(meta.MarketplaceCategory!);
                    if (!string.IsNullOrWhiteSpace(meta.ProminentInstrumentation)) tags.Add(meta.ProminentInstrumentation!);
                    string tagPart = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "";
                    detail = $"played {artistPart}'{title}'{tagPart}";
                }
            }
            EventActivityLog.Record(eventKey, side, $"{name} ({sideLabel}) -- {detail}");
        }
        else if (result == "unassigned")
            EventActivityLog.Record(eventKey, side, $"{name} ({sideLabel}) -- no song assigned, nothing played");
        else if (result == "file-missing")
            EventActivityLog.Record(eventKey, side, $"{name} ({sideLabel}) -- skipped: the assigned song file couldn't be found on disk");
        else if (result == "no-profile")
            EventActivityLog.Record(eventKey, side, $"{name} ({sideLabel}) -- skipped: no team profile is loaded yet");
    }

    static string DisplaySide(string side) => side == "home" ? "Home" : side == "away" ? "Away" : side;

    void FireTriggerForSide(string side, string trigger)
    {
        var config = side == "home" ? _homeConfig : _awayConfig;
        var entry = config?.FirstOrDefault(e => e.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        if (entry != null) FireEvent(entry, side == "home" ? AudioPlayer.HomeVolume : AudioPlayer.AwayVolume);
    }

    void SaveCurrentTeamProfile()
    {
        ConfigStore.SaveProfile(TeamPresetProfileKey(Theme.ActiveTeam.Name, _activeTeamPreset), _config);
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
        // Reloads from GameplayProfileKey, NOT necessarily savedTeamName itself -- an edit made
        // while a different preset than the one currently live for this game is active (e.g.
        // tweaking the "Away" preset for a team that's actually playing Home this game, or vice
        // versa) must NOT leak into gameplay. GameplayProfileKey recomputes which preset SHOULD be
        // live every time, so only an edit to the actually-active preset (or the plain profile,
        // when no preset applies) ever hot-refreshes _homeConfig/_awayConfig.
        bool isHome = _homeTeam is { } home && string.Equals(home.Name, savedTeamName, StringComparison.OrdinalIgnoreCase);
        bool isAway = _awayTeam is { } away && string.Equals(away.Name, savedTeamName, StringComparison.OrdinalIgnoreCase);
        if (isHome) _homeConfig = LoadGameplayProfile(savedTeamName, isHomeSide: true);
        if (isAway) _awayConfig = LoadGameplayProfile(savedTeamName, isHomeSide: false);

        // FIXED 2026-08-11 (live bug: "random" playback delays reported mid-game). StartWatching-
        // IfMatchupSet's AudioCache.Preload only ever runs once, at GAMETIME -- any song assigned
        // or re-assigned to Home/Away AFTER that (exactly what testing/tweaking mid-game is) never
        // gets warmed into RAM, so its first real fire falls through to CachedAudioSource's cold
        // disk-read fallback. That stall only hits whichever specific event was just touched, so
        // it read as random/intermittent rather than a consistent every-time delay. Re-preload in
        // the background (not blocking the save/UI) whenever a Home/Away profile that's actually
        // live in the current matchup gets saved, same list-building as StartWatchingIfMatchupSet.
        if (isHome || isAway)
        {
            var toCache = (_homeConfig ?? Enumerable.Empty<TriggerEntry>())
                .Concat(_awayConfig ?? Enumerable.Empty<TriggerEntry>())
                .SelectMany(e => new[] { e.AudioFile, e.PaAudioFile });
            Task.Run(() => AudioCache.Preload(toCache));
        }
    }

    public string ToggleWatchingFromWeb()
    {
        if (_watching)
        {
            _hook.Stop();
            _watcher.Stop();
            CrowdBusService.Stop();
            _watching = false;
            _windowFound = false;
            // Stop Watching is the one explicit "this game is over" signal (see _matchupLocked) --
            // unlock so a new GAMETIME press can pick a different matchup for the next game.
            _matchupLocked = false;
        }
        else
        {
            string? failure = StartWatchingIfMatchupSet();
            if (failure != null) return failure;
        }
        return WatchStateString();
    }

    /// <summary>Shared by the manual Watch toggle and GAMETIME -- explicit owner request: GAMETIME
    /// should lock the matchup AND start watching in one press instead of needing a second manual
    /// "Start Watching" step afterward. Refuses to start without a matchup set for the same reason
    /// ToggleWatchingFromWeb always did (OCR needs _homeTeam/_awayTeam resolved first).</summary>
    string? StartWatchingIfMatchupSet()
    {
        if (_homeTeam is null || _awayTeam is null) return "no-matchup";
        if (_watching) return null;

        // Sound Booth item #2: load every assigned song into RAM once, right before the game
        // starts, so no trigger during the game has to wait on a disk read. Must finish BEFORE
        // watching starts below -- this used to be a fire-and-forget Task.Run, which raced
        // _hook.Start()/_watcher.Start(): if the very first event of the game fired before the
        // background preload finished, that trigger fell through to a cold synchronous disk read
        // with PreRollSeconds now at 0.0 (dropped from 1.0 specifically because caching was
        // assumed to remove that stall), reintroducing the exact stall this cache exists to avoid,
        // on the highest-stakes earliest plays. Blocking here briefly on GAMETIME press is the
        // correct trade: a one-time short wait before kickoff instead of an audible gap mid-play.
        var toCache = (_homeConfig ?? Enumerable.Empty<TriggerEntry>())
            .Concat(_awayConfig ?? Enumerable.Empty<TriggerEntry>())
            .SelectMany(e => new[] { e.AudioFile, e.PaAudioFile })
            .Append(AudioPlayer.LeadInClipPath);
        AudioCache.Preload(toCache);

        // Set BEFORE Start() below -- GameWatcher.Start() fires WindowFoundChanged synchronously
        // (RunAsync runs up to its first await before Start() returns), and that handler reads
        // _watching via WatchStateString() to push the "waiting"/"watching" pill state to the web
        // UI. If _watching were still false at that point, every watch start would report "off"
        // to the page, wiping out the pill and (since setWatching("off") also clears the
        // client-side matchupLocked flag) hiding the unlock button while the backend was still
        // genuinely locked/watching.
        _watching = true;

        ControllerRumbleService.Start(_watcher);
        CrowdBusService.Start(_watcher);

        _hook.Start();
        _watcher.Start();
        return null;
    }

    string WatchStateString() => !_watching ? "off" : _windowFound ? "watching" : "waiting";

    /// <summary>Settings modal migration: these used to be inline lambdas built inline inside
    /// OpenSettingsFromWeb (native SettingsForm.cs, now deleted) -- extracted into named
    /// ...FromWeb methods, same convention as every other web-bridge-backed action, now that the
    /// themed Profile overlay's Settings tab calls them directly instead of a native dialog.</summary>
    public void StopPlaybackFromWeb() => AudioPlayer.StopAll();

    public void OpenSongsFolderFromWeb()
    {
        Directory.CreateDirectory(ConfigStore.SongsFolder);
        System.Diagnostics.Process.Start("explorer.exe", ConfigStore.SongsFolder);
    }

    public void ClearAllAssignmentsFromWeb() => ClearAll();

    public bool GetAlwaysOnTopFromWeb() => ConfigStore.LoadAppWindowSettings().AlwaysOnTop;

    public void SetAlwaysOnTopFromWeb(bool enabled)
    {
        TopMost = enabled;
        ConfigStore.SaveAppWindowSettings(new ConfigStore.AppWindowSettings(enabled));
    }

    public string GetPlaybackTimingSettingsFromWeb() =>
        System.Text.Json.JsonSerializer.Serialize(ConfigStore.LoadPlaybackTimingSettings());

    internal void SavePlaybackTimingSettingsFromWeb(ConfigStore.PlaybackTimingSettings settings)
    {
        AudioPlayer.PreRollSeconds = settings.PreRollSeconds;
        AudioPlayer.FadeStartSeconds = settings.FadeStartSeconds;
        AudioPlayer.FadeOutDuration = settings.FadeOutDuration;
        GameWatcher.Cooldown = TimeSpan.FromSeconds(settings.CooldownSeconds);
        ConfigStore.SavePlaybackTimingSettings(settings);
    }

    /// <summary>Matchup-screen scorebug switcher (pill + arrows, owner-requested) -- same
    /// underlying ActivePreset/SaveScorebugPresetName plumbing OpenSettingsFromWeb's native dialog
    /// already uses, just exposed directly to the web UI so it's reachable without opening
    /// Settings. Returns the list of preset names in ScorebugPreset.AllPresets order plus which
    /// one is active, so app.js can cycle with </> without needing its own copy of the list.</summary>
    public string GetScorebugPresetsFromWeb() => System.Text.Json.JsonSerializer.Serialize(new
    {
        names = ScorebugPreset.AllPresets.Select(p => p.Name).ToArray(),
        active = _watcher.ActivePreset.Name,
    });

    /// <summary>Powers the Help &amp; Guide "Event Log" tab -- plain-English feed of what the
    /// audio engine did/skipped and why, for a user wondering "why didn't my song play" without
    /// needing to send a developer ocr_debug.log. Same JSON-array-of-objects shape as other
    /// list-returning bridge methods (see GetChangelog above).</summary>
    public string GetEventActivityLogFromWeb() => System.Text.Json.JsonSerializer.Serialize(
        EventActivityLog.GetSnapshot().Select(e => new
        {
            text = e.ToDisplayString(),
            eventKey = e.EventKey,
            side = e.Side,
        }));

    /// <summary>Writes the current Event Log buffer to a timestamped text file under
    /// UserDataRoot (same folder family as everything else the user owns -- see ConfigStore's
    /// UserDataRoot comment) so it can be attached to a support request. Returns the full path so
    /// the UI can tell the user exactly where it landed.</summary>
    public string ExportEventActivityLogFromWeb()
    {
        try
        {
            string fileName = $"event_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string path = Path.Combine(ConfigStore.UserDataRoot, fileName);
            Directory.CreateDirectory(ConfigStore.UserDataRoot);
            var lines = EventActivityLog.GetSnapshot().Select(e => e.ToDisplayString());
            File.WriteAllLines(path, lines);
            return path;
        }
        catch (Exception ex)
        {
            CrashLog.Write("ExportEventActivityLogFromWeb failed", ex);
            return "";
        }
    }

    public void SetScorebugPresetFromWeb(string name)
    {
        _watcher.ActivePreset = ScorebugPreset.GetByName(name);
        ConfigStore.SaveScorebugPresetName(name);
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


    public void OpenExternalUrlFromWeb(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

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
        if (entry != null) OpenAssignTrack(entry, isPa: false);
        PushCategories();
    }

    /// <summary>PA Announcer variant of OpenAssignTrackFromWeb -- same picker dialog, targets
    /// entry.PaAudioFile instead of entry.AudioFile. See OpenAssignTrack.</summary>
    public void OpenAssignPaTrackFromWeb(string trigger)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry != null) OpenAssignTrack(entry, isPa: true);
        PushCategories();
    }

    /// <summary>Manual Preview button on an assign card -- deliberately NOT routed through
    /// FireEvent (that's for real in-game firing: it records SongsTriggered/EventCounts stats
    /// and toasts "bandroom:triggerfired", neither of which should happen just because someone
    /// clicked Preview while assigning a song). Uses AudioPlayer's isPreview path so playback
    /// starts instantly (no PreRollSeconds delay) and repeated clicks on the same clip always
    /// play (no FireCooldown gate) -- both of those exist for real game cues, not previewing.</summary>
    // FIXED 2026-08-12 (owner report, Sound Booth mixer: "I don't hear a change in Master or
    // Away or Home") -- the fallback Preview path (no clip selected on the Assignment screen, so
    // Sound Booth's Preview button falls back to this canned test-cue trigger instead of
    // PreviewLocalFileFromWeb) hardcoded AudioPlayer.MasterVolume same as that method did before
    // its own fix -- same bug, different call site. contextParamKey threads through the same way.
    public void PreviewEventFromWeb(string trigger, string? contextParamKey = null)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        float eventVolumeScale = Math.Clamp(entry.Volume, 0, 100) / 100f;
        if (!string.IsNullOrWhiteSpace(entry.AudioFile) && File.Exists(entry.AudioFile))
            AudioPlayer.Play(entry.AudioFile, VolumeForSoundBoothContext(contextParamKey) * eventVolumeScale, interruptPrevious: true, isPreview: true, playLeadInWhistle: entry.PlayLeadInWhistle, liveVolumeSource: () => VolumeForSoundBoothContext(contextParamKey) * eventVolumeScale, speed2x: entry.PlaybackSpeed2x, whistleSpeed: entry.WhistleSpeed);
        if (!string.IsNullOrWhiteSpace(entry.PaAudioFile) && File.Exists(entry.PaAudioFile))
            AudioPlayer.Play(entry.PaAudioFile, AudioPlayer.PaVolume * eventVolumeScale, interruptPrevious: false, isPreview: true, liveVolumeSource: () => AudioPlayer.PaVolume * eventVolumeScale);
    }

    public int GetEventVolumeFromWeb(string trigger) => _config.FirstOrDefault(e => e.Trigger == trigger)?.Volume ?? 100;

    public void SetEventVolumeFromWeb(string trigger, int percent)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        entry.Volume = Math.Clamp(percent, 0, 100);
        SaveCurrentTeamProfile();
    }

    public void SetEventPlayLeadInWhistleFromWeb(string trigger, bool enabled)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        entry.PlayLeadInWhistle = enabled;
        SaveCurrentTeamProfile();
    }

    /// <summary>Speed toggle button on an event card's transport strip -- see
    /// AudioPlayer.Play's speed2x param / SoundTouchSpeedSampleProvider. Plain on/off per card,
    /// same convention as SetEventPlayLeadInWhistleFromWeb above.</summary>
    public void SetEventPlaybackSpeed2xFromWeb(string trigger, bool enabled)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        entry.PlaybackSpeed2x = enabled;
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        PushCategories();
    }

    /// <summary>Fixed cycle of whistle-speed presets for the per-event whistle-speed button --
    /// keeps the value from drifting to an unreasonable speed by accident (no free-typed number),
    /// same "click to cycle" convention SetEventPlaybackSpeed2xFromWeb's on/off button used, just
    /// more than two states. Kept in one place so app.js's WHISTLE_SPEED_PRESETS (display labels)
    /// and this cycle order can't drift apart.</summary>
    static readonly double[] WhistleSpeedPresets = { 1.0, 1.15, 1.3, 1.5 };

    /// <summary>Per-event whistle-speed button (event card's transport strip) -- REPLACED
    /// BrowseAndSetEventAltWhistleFromWeb/ClearEventAltWhistleFromWeb/SaveTrimAsEventAltWhistleFromWeb
    /// 2026-08-12 (owner request: "remove the alternate whistle and just make that button a
    /// whistle speed toggle"). Cycles TriggerEntry.WhistleSpeed through WhistleSpeedPresets and
    /// returns the new value so the UI can relabel the button immediately.</summary>
    public double CycleEventWhistleSpeedFromWeb(string trigger)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return 1.0;
        int idx = Array.IndexOf(WhistleSpeedPresets, entry.WhistleSpeed);
        entry.WhistleSpeed = WhistleSpeedPresets[(idx + 1 + WhistleSpeedPresets.Length) % WhistleSpeedPresets.Length];
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        PushCategories();
        return entry.WhistleSpeed;
    }

    public void StopPreviewFromWeb() => AudioPlayer.StopAll();

    public void TriggerEffectsTestFromWeb()
    {
        var entry = _config.FirstOrDefault(e => e.Event.Contains("Touchdown", StringComparison.OrdinalIgnoreCase))
            ?? _config.FirstOrDefault();
        if (entry != null) FireEvent(entry);
    }

    /// <summary>Test hook: fires a specific EventKey for a specific side through the exact same
    /// FireEventForSide path the real engine uses, without needing a live game/OCR feed. Lets the
    /// owner test event wiring, LegacyDownEventAlias fallback, and side-routing volume directly
    /// from the debug panel.</summary>
    public string FireTestEventFromWeb(string side, string eventKey) => FireEventForSide(side, eventKey, bypassCooldown: true);

    public string GetAllEventKeysFromWeb() => System.Text.Json.JsonSerializer.Serialize(ConfigStore.AllEngineEventKeys);

    /// <summary>isPa selects which field on entry this dialog session edits: false = the main
    /// song (AudioFile, unchanged behavior), true = the PA Announcer clip (PaAudioFile, new).
    /// Same picker/browse/trim/clear flow either way -- only which TriggerEntry field gets read
    /// for display and written back on close differs.</summary>
    void OpenAssignTrack(TriggerEntry entry, bool isPa)
    {
        var library = new List<string>();
        if (Directory.Exists(ConfigStore.SongsFolder))
            library.AddRange(Directory.GetFiles(ConfigStore.SongsFolder, "*", SearchOption.AllDirectories)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));

        string currentPath = isPa ? entry.PaAudioFile : entry.AudioFile;
        string title = isPa ? "Assign PA Announcer Clip" : "Assign Track";
        using var dlg = new AssignTrackForm(this, entry, library, currentPath, title);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.RequestTrim)
        {
            if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
            {
                MessageBox.Show(this, "Choose a song for this situation first, then you can trim it.", "Bandroom");
                return;
            }
            // Clip start/end sliders + Save, same as the "Volume" slider style in the main
            // shell -- TrimmerForm already has this, just wiring it back in here.
            // TrimmerForm's constructor reads the file's duration up front (AudioFileReader),
            // which throws uncaught if the file was deleted/became unreadable between the
            // File.Exists check above and here, or is a format NAudio can't decode -- catch it
            // here instead of letting it crash the caller.
            TrimmerForm trimmer;
            try { trimmer = new TrimmerForm(this, currentPath); }
            catch (Exception ex)
            {
                CrashLog.Write("TrimmerForm open failed", ex);
                MessageBox.Show(this, "Couldn't open this song for trimming -- the file may be missing or in an unsupported format.", "Bandroom");
                return;
            }
            using (trimmer)
            {
                if (trimmer.ShowDialog(this) == DialogResult.OK && trimmer.SavedFilePath != null)
                {
                    if (isPa) entry.PaAudioFile = trimmer.SavedFilePath; else entry.AudioFile = trimmer.SavedFilePath;
                }
                else return;
            }
        }
        else if (dlg.RequestClear)
        {
            if (isPa) entry.PaAudioFile = ""; else entry.AudioFile = "";
        }
        else if (dlg.AssignedPath != null)
        {
            if (isPa) entry.PaAudioFile = dlg.AssignedPath; else entry.AudioFile = dlg.AssignedPath;
        }
        else return;

        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        NormalizeAssignmentInBackground(entry, isPa);
    }

    /// <summary>Sound Booth item #3: kicks off offline LUFS analysis + gain-matched copy for
    /// a freshly-assigned clip, off the UI thread (never blocks the assign flow). Re-points the
    /// entry at the normalized copy and re-saves once it's ready; a normalization failure just
    /// leaves the original assignment in place (LoudnessNormalizationService itself never
    /// throws -- see its own catch-and-return-sourcePath fallback).</summary>
    void NormalizeAssignmentInBackground(TriggerEntry entry, bool isPa)
    {
        string path = isPa ? entry.PaAudioFile : entry.AudioFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var kind = isPa ? LoudnessKind.Pa : LoudnessKind.Song;

        Task.Run(() =>
        {
            string normalized = LoudnessNormalizationService.NormalizeToTarget(path, kind);
            if (normalized == path) return; // nothing changed (already normalized, or it failed)

            RunOnUi(() =>
            {
                if (isPa) entry.PaAudioFile = normalized; else entry.AudioFile = normalized;
                ConfigStore.Save(_config);
                SaveCurrentTeamProfile();
                AudioCache.Invalidate(path);
            });
        });
    }

    /// <summary>Owner request: "all songs the same volume" retroactively, not just for songs
    /// assigned going forward (NormalizeAssignmentInBackground already covers those). Sweeps
    /// every saved team profile once, normalizing every AudioFile/PaAudioFile/BigGameAudioFile
    /// that isn't already a normalized copy, and re-saves any profile it touched. Guarded by
    /// ConfigStore.LibraryNormalizedMarkerPath so this only ever runs once per install --
    /// LoudnessNormalizationService's own per-file sidecar cache would make repeat sweeps cheap
    /// anyway, but there's no reason to re-scan every profile on every launch. Runs entirely off
    /// the UI thread; a failure on one file/profile never blocks the rest (both this loop and
    /// NormalizeToTarget itself catch-and-continue).</summary>
    void NormalizeExistingLibraryOnce()
    {
        if (File.Exists(ConfigStore.LibraryNormalizedMarkerPath)) return;

        Task.Run(() =>
        {
            try
            {
                foreach (var teamName in ConfigStore.ListProfiles())
                {
                    try
                    {
                        var entries = ConfigStore.LoadProfile(teamName);
                        bool changed = false;
                        foreach (var entry in entries)
                        {
                            if (!string.IsNullOrWhiteSpace(entry.AudioFile) && File.Exists(entry.AudioFile))
                            {
                                string normalized = LoudnessNormalizationService.NormalizeToTarget(entry.AudioFile, LoudnessKind.Song);
                                if (normalized != entry.AudioFile) { entry.AudioFile = normalized; changed = true; }
                            }
                            if (!string.IsNullOrWhiteSpace(entry.PaAudioFile) && File.Exists(entry.PaAudioFile))
                            {
                                string normalized = LoudnessNormalizationService.NormalizeToTarget(entry.PaAudioFile, LoudnessKind.Pa);
                                if (normalized != entry.PaAudioFile) { entry.PaAudioFile = normalized; changed = true; }
                            }
                            if (!string.IsNullOrWhiteSpace(entry.BigGameAudioFile) && File.Exists(entry.BigGameAudioFile))
                            {
                                string normalized = LoudnessNormalizationService.NormalizeToTarget(entry.BigGameAudioFile, LoudnessKind.Song);
                                if (normalized != entry.BigGameAudioFile) { entry.BigGameAudioFile = normalized; changed = true; }
                            }
                        }
                        if (changed) ConfigStore.SaveProfile(teamName, entries);
                    }
                    catch (Exception ex)
                    {
                        CrashLog.Write($"NormalizeExistingLibraryOnce: profile '{teamName}' failed", ex);
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigStore.LibraryNormalizedMarkerPath)!);
                File.WriteAllText(ConfigStore.LibraryNormalizedMarkerPath, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                CrashLog.Write("NormalizeExistingLibraryOnce failed", ex);
            }
        });
    }

    /// <summary>Web equivalent of AssignTrackForm's library list -- same source (everything
    /// under ConfigStore.SongsFolder), just JSON instead of a ListBox. Backs the clipping
    /// island's inline song picker (see initClipperIsland in app.js), which replaces the native
    /// AssignTrackForm popup for the main-song assign flow -- Trim still opens the real
    /// TrimmerForm (see OpenTrimmerFromWeb) since that's real waveform-cut/normalize work, not
    /// something worth re-implementing in JS.
    ///
    /// Tags each entry with a "source" so the JS side can group/label the list instead of
    /// dumping every file from every subfolder into one flat wall of songs (owner feedback,
    /// Session 10) -- SongsFolder mixes THREE genuinely different origins that all land as plain
    /// audio files with no other distinguishing marker: drag-drop/Browse imports and marketplace
    /// downloads both physically land in SongsUploadedFolder (see ConfigStore.ImportIntoSongsLibrary
    /// and MarketplaceDownloadService), trimmed clips land in SongsTrimmedFolder, and the "import
    /// my own song" pipeline lands in LocalTracksFolder. Marketplace downloads are distinguished
    /// from plain imports by cross-referencing ConfigStore.LoadMarketplaceDownloads() (the only
    /// place that distinction is recorded) rather than folder alone, since both share
    /// SongsUploadedFolder. NOTE: the default song pack (ConfigStore.DefaultSongsFolder) is
    /// deliberately NOT included here -- it lives outside SongsFolder entirely (see
    /// DefaultSongPackService's own comment on why) and is never copied in, so it can't be part
    /// of this mess; don't "fix" that by scanning it in too.</summary>
    public string GetTrackLibraryFromWeb()
    {
        var library = new List<string>();
        if (Directory.Exists(ConfigStore.SongsFolder))
            library.AddRange(Directory.GetFiles(ConfigStore.SongsFolder, "*", SearchOption.AllDirectories)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));

        var marketplacePaths = new HashSet<string>(
            ConfigStore.LoadMarketplaceDownloads().Where(e => e.Type == "song").Select(e => e.Path),
            StringComparer.OrdinalIgnoreCase);

        string SourceFor(string path)
        {
            if (path.StartsWith(ConfigStore.SongsTrimmedFolder, StringComparison.OrdinalIgnoreCase)) return "trimmed";
            if (path.StartsWith(ConfigStore.LocalTracksFolder, StringComparison.OrdinalIgnoreCase)) return "local";
            if (marketplacePaths.Contains(path)) return "marketplace";
            return "uploaded"; // drag-drop or AssignTrackForm Browse import -- ImportIntoSongsLibrary
        }

        var items = library.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(p => new { name = Path.GetFileNameWithoutExtension(p), path = p, source = SourceFor(p) });
        return System.Text.Json.JsonSerializer.Serialize(items);
    }

    /// <summary>Lists a team's slice of the (locally imported) default song pack, so the Sound
    /// Bank album can actually show what "Import Song Pack" put there -- these files never show
    /// up as marketplace/community uploads (the album grid's normal data source, see
    /// renderTeamAlbumGrid in app.js) since they're purely local and get assigned straight into
    /// event slots, not published anywhere. Owner report: importing a team's pack and then
    /// opening that team's Sound Bank looked like nothing happened, because this section didn't
    /// exist -- the import DID work, there was just nowhere in this view to see it.</summary>
    public string GetDefaultPackSongsForTeamFromWeb(string teamName)
    {
        string? teamFolder = ConfigStore.FindDefaultPackTeamFolder(teamName);
        if (teamFolder == null) return "[]";

        // "category" strips the trailing "_4"/" 4" index the importer appends when several files
        // resolved to the same trigger (e.g. "Defense_Drive Starter_4" -> "Defense_Drive Starter")
        // so the web UI can group this flat folder listing into Spotify-style category clusters
        // instead of one long alphabetical list of near-duplicate names (owner report: the flat
        // list read as unsorted noise once a team had 100+ pack extras).
        var items = Directory.GetFiles(teamFolder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                string name = Path.GetFileNameWithoutExtension(p);
                string category = System.Text.RegularExpressions.Regex.Replace(name, @"[\s_]+\d+$", "").Trim();
                if (category.Length == 0) category = "Other";
                return new { name, path = p, category, title = ReadAudioTitleTag(p) };
            });
        return System.Text.Json.JsonSerializer.Serialize(items);
    }

    /// <summary>Conference-wide counterpart to GetDefaultPackSongsForTeamFromWeb -- lists files
    /// sitting directly in the team's conference folder (shared cues, not this specific team's own
    /// subfolder) so Auto-Assign's "Load Conference Pack" option and the guided wizard's candidate
    /// list can both show/search them the same way team-specific pack songs already are.</summary>
    public string GetConferencePackSongsForTeamFromWeb(string teamName)
    {
        string? confFolder = ConfigStore.FindDefaultPackConferenceFolder(teamName);
        if (confFolder == null) return "[]";

        var items = Directory.GetFiles(confFolder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                string name = Path.GetFileNameWithoutExtension(p);
                string category = System.Text.RegularExpressions.Regex.Replace(name, @"[\s_]+\d+$", "").Trim();
                if (category.Length == 0) category = "Other";
                return new { name, path = p, category, title = ReadAudioTitleTag(p) };
            });
        return System.Text.Json.JsonSerializer.Serialize(items);
    }

    /// <summary>Sound Bank browsing redesign: pack filenames are EventKey-shaped
    /// ("Defense_Third Down_5.mp3", see DefaultSongPackService's EventKeyToFileStem), which tells
    /// you nothing about the actual song -- but the files themselves generally carry a real ID3
    /// Title tag (owner-confirmed via Explorer's Title column, e.g. "sec socar '21 D stop") that's
    /// far more useful to show. Returns null (never throws) for files with no tag, a blank tag, or
    /// any read failure (corrupt file, locked, unsupported format) -- callers fall back to
    /// `category` in that case, same as before this existed.</summary>
    static string? ReadAudioTitleTag(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            string title = file.Tag.Title?.Trim() ?? "";
            return title.Length > 0 ? title : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sound Bank browsing redesign: every team that actually has a Default Song Pack
    /// slice on disk, with its conference, for the Assignment screen's "Browse another team's
    /// Sound Bank" picker -- see ConfigStore.GetDefaultPackTeamsWithConference for why this is a
    /// live folder scan rather than trusting index.json.</summary>
    public string GetDefaultPackTeamsFromWeb() =>
        System.Text.Json.JsonSerializer.Serialize(ConfigStore.GetDefaultPackTeamsWithConference());

    /// <summary>Maps a Sound Booth mixer knob's param key ("home-volume"/"away-volume"/etc) to
    /// the live AudioPlayer field it controls -- shared by PreviewLocalFileFromWeb's one-time
    /// volume and its liveVolumeSource so dragging that knob during an active preview is audible
    /// immediately, not just on the next preview click. Unknown/empty keys (including the hero
    /// knob's own "master-volume") fall back to Master, same as if no context were selected.</summary>
    static float VolumeForSoundBoothContext(string? contextParamKey) => contextParamKey switch
    {
        "home-volume" => AudioPlayer.HomeVolume,
        "away-volume" => AudioPlayer.AwayVolume,
        "pa-volume" => AudioPlayer.PaVolume,
        "whistle-volume" => AudioPlayer.WhistleVolume,
        _ => AudioPlayer.MasterVolume,
    };

    /// <summary>Preview an arbitrary local library file from the clipping island's song list --
    /// distinct from PreviewEventFromWeb (which previews whatever's already assigned to a
    /// trigger). Routes through the same native AudioPlayer as everything else local, not a JS
    /// &lt;audio&gt; element -- local filesystem paths aren't reliably fetchable from the WebView2
    /// content process, same reason PreviewEventFromWeb doesn't either.
    /// FIXED 2026-08-12 (owner report: "I don't hear a change in Master or Away or Home" while
    /// dragging Sound Booth knobs) -- this call previously hardcoded AudioPlayer.MasterVolume as
    /// a one-time snapshot with no liveVolumeSource at all, so it never reflected Home/Away/PA/
    /// Whistle no matter which context knob was selected, and didn't even track live Master
    /// changes mid-playback. contextParamKey (the Sound Booth context knob's currently selected
    /// param, e.g. "home-volume") now picks which AudioPlayer field this preview tracks, live, for
    /// its whole duration via VolumeForSoundBoothContext.</summary>
    public void PreviewLocalFileFromWeb(string path, string? contextParamKey = null)
    {
        // Punch-list item 5: AudioPlayer.Play's playLeadInWhistle parameter defaults to TRUE
        // (it's built for the real event-firing path) -- this call site never overrode it, so
        // the universal lead-in whistle was incorrectly playing every time someone previewed a
        // song from the song list while assigning, not just when a song actually fires from an
        // event card. Explicitly false here; PreviewEventFromWeb (the event card's own Preview
        // button) is untouched and still honors entry.PlayLeadInWhistle as before.
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            AudioPlayer.Play(path, VolumeForSoundBoothContext(contextParamKey), interruptPrevious: true, isPreview: true,
                playLeadInWhistle: false, liveVolumeSource: () => VolumeForSoundBoothContext(contextParamKey));
    }

    /// <summary>Soundboard bar (currently hidden via #soundboard-bar[hidden] -- not yet a shipped
    /// feature) plays a user-assigned favorite by key. Was calling a bridge method that didn't
    /// exist (bridge?.PlaySoundboardSlot), which would have thrown the moment the bar is
    /// unhidden. Same fire-and-forget path as PreviewLocalFileFromWeb above, since a soundboard
    /// hit is conceptually the same as a manual preview -- play immediately, interrupt whatever's
    /// already playing.</summary>
    public void PlaySoundboardSlotFromWeb(string key, string path)
    {
        // Same reasoning as PreviewLocalFileFromWeb above (punch-list item 5) -- a manual
        // soundboard hit isn't a real event-card trigger, so it shouldn't carry the lead-in
        // whistle either.
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            AudioPlayer.Play(path, AudioPlayer.MasterVolume, interruptPrevious: true, isPreview: true, playLeadInWhistle: false);
    }

    /// <summary>Dynasty-mode save-file scanning (currently has no caller anywhere in app.js --
    /// unreachable UI, same "coded but never wired" state as the soundboard bar). Was calling a
    /// bridge method that didn't exist (bridge.ScanDynastySave). There is no actual CFB27 dynasty
    /// save-file format parser anywhere in this codebase -- reverse-engineering that format is a
    /// real, separate feature, not something to fake here. Returns null (the JS caller's existing
    /// .catch() already renders that as "No dynasty save found") instead of throwing, so a future
    /// UI hookup doesn't crash the instant it's wired up -- it'll just honestly report nothing
    /// found until a real parser exists.</summary>
    public Task<string?> ScanDynastySaveFromWeb() => Task.FromResult<string?>(null);

    /// <summary>Returns false (no-op) if `trigger` doesn't match any entry in the current
    /// profile -- callers that report a count of successful assignments (e.g.
    /// WebBridge.ApplyMarketplaceProfile) should check this instead of assuming a match, since a
    /// shared profile can reference a trigger key that's been retired/renamed since it was
    /// exported.</summary>
    public bool AssignTrackFileFromWeb(string trigger, bool isPa, string path)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return false;
        if (isPa) entry.PaAudioFile = path; else entry.AudioFile = path;
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        PushCategories();
        NormalizeAssignmentInBackground(entry, isPa);
        return true;
    }

    public void ClearTrackAssignmentFromWeb(string trigger, bool isPa) => AssignTrackFileFromWeb(trigger, isPa, "");

    /// <summary>Renames the audio file (and its .meta.json sidecar) currently assigned to
    /// `trigger` on disk to "{newBaseName}{original extension}", then rewrites every reference to
    /// the old path -- in this profile's in-memory _config/_homeConfig/_awayConfig AND in every
    /// OTHER saved profile file on disk (a shared file can be assigned under multiple team
    /// profiles) -- so no assignment silently breaks. Local-only: never touches the marketplace,
    /// so retitling a downloaded/shared song never pushes the rename back to whoever uploaded it
    /// (same "local override, don't sync out" shape as CustomTeamLogos in
    /// PublicTeamLogoSyncService). Returns the new file name, or null if the trigger has no
    /// assigned file or the rename target already exists as a DIFFERENT unrelated file (handled
    /// via a numeric suffix, not a failure) or the source file is missing.</summary>
    public string? RenameAssignedTrackFromWeb(string trigger, string newBaseName)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        var oldPath = entry?.AudioFile;
        if (entry == null || string.IsNullOrWhiteSpace(oldPath) || !File.Exists(oldPath)) return null;

        var dir = Path.GetDirectoryName(oldPath)!;
        var ext = Path.GetExtension(oldPath);
        var safeName = ConfigStore.SanitizeFileName(newBaseName);
        if (string.IsNullOrWhiteSpace(safeName)) return null;

        var newPath = Path.Combine(dir, safeName + ext);
        int suffix = 1;
        while (File.Exists(newPath) && !string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase))
            newPath = Path.Combine(dir, $"{safeName} ({++suffix}){ext}");

        if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(oldPath);

        File.Move(oldPath, newPath);
        var oldSidecar = oldPath + ".meta.json";
        if (File.Exists(oldSidecar)) File.Move(oldSidecar, newPath + ".meta.json");

        RewriteAudioFileReferences(oldPath, newPath);
        return Path.GetFileName(newPath);
    }

    /// <summary>Swaps `oldPath` for `newPath` on any TriggerEntry.AudioFile/PaAudioFile/
    /// BigGameAudioFile that referenced it, across the in-memory active/home/away configs (saved
    /// immediately) and every other team's saved profile file on disk (so a rename made while
    /// editing one team doesn't orphan the same shared file assigned under another team).</summary>
    void RewriteAudioFileReferences(string oldPath, string newPath)
    {
        static bool Retarget(TriggerEntry e, string oldPath, string newPath)
        {
            bool changed = false;
            if (string.Equals(e.AudioFile, oldPath, StringComparison.OrdinalIgnoreCase)) { e.AudioFile = newPath; changed = true; }
            if (string.Equals(e.PaAudioFile, oldPath, StringComparison.OrdinalIgnoreCase)) { e.PaAudioFile = newPath; changed = true; }
            if (string.Equals(e.BigGameAudioFile, oldPath, StringComparison.OrdinalIgnoreCase)) { e.BigGameAudioFile = newPath; changed = true; }
            return changed;
        }

        bool activeChanged = false;
        foreach (var e in _config) activeChanged |= Retarget(e, oldPath, newPath);
        if (activeChanged) SaveCurrentTeamProfile();

        if (_homeConfig != null) foreach (var e in _homeConfig) Retarget(e, oldPath, newPath);
        if (_awayConfig != null) foreach (var e in _awayConfig) Retarget(e, oldPath, newPath);

        foreach (var profileName in ConfigStore.ListProfiles())
        {
            if (activeChanged && profileName == TeamPresetProfileKey(Theme.ActiveTeam.Name, _activeTeamPreset))
                continue; // already saved above

            var entries = ConfigStore.LoadProfile(profileName);
            bool changed = false;
            foreach (var e in entries) changed |= Retarget(e, oldPath, newPath);
            if (changed) ConfigStore.SaveProfile(profileName, entries);
        }
    }

    /// <summary>Punch-list item 3: "copy assignment from another event" button on an event card
    /// -- lets an unassigned (or already-assigned) event pull in another same-team event's
    /// existing song/PA/lead-in-whistle setting as a starting point, e.g. "3rd and short"
    /// borrowing "2nd and short"'s song. Both triggers are looked up in the SAME _config (the
    /// currently active team's profile, same source every other AssignTrackFileFromWeb-style
    /// method reads from) so this can never cross teams. Copies AudioFile + PaAudioFile +
    /// PlayLeadInWhistle together rather than needing two separate calls (main-song assign
    /// already normally goes hand-in-hand with the PA clip in the same picker). Returns false if
    /// either trigger doesn't resolve or the source has nothing assigned yet.</summary>
    public bool CopyEventAssignmentFromWeb(string sourceTrigger, string targetTrigger)
    {
        var source = _config.FirstOrDefault(e => e.Trigger == sourceTrigger);
        var target = _config.FirstOrDefault(e => e.Trigger == targetTrigger);
        if (source == null || target == null) return false;
        if (string.IsNullOrWhiteSpace(source.AudioFile) && string.IsNullOrWhiteSpace(source.PaAudioFile)) return false;

        target.AudioFile = source.AudioFile;
        target.PaAudioFile = source.PaAudioFile;
        target.PlayLeadInWhistle = source.PlayLeadInWhistle;
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        PushCategories();
        if (!string.IsNullOrWhiteSpace(target.AudioFile)) NormalizeAssignmentInBackground(target, isPa: false);
        if (!string.IsNullOrWhiteSpace(target.PaAudioFile)) NormalizeAssignmentInBackground(target, isPa: true);
        return true;
    }

    /// <summary>Clipper song-list "Share to..." action -- pushes a library file directly onto
    /// ANOTHER team's event, unlike CopyEventAssignmentFromWeb above which only reaches events on
    /// the currently active team (both triggers looked up in the same live _config). This loads/
    /// saves the TARGET team's saved profile file directly via ConfigStore.LoadProfile/SaveProfile
    /// so it can reach any team's roster, including ones never loaded this session. If the target
    /// happens to be the active team, updates the live _config/PushCategories too instead of only
    /// the on-disk copy, so the UI doesn't go stale (same "looks right in source, stale in the
    /// deployed/loaded copy" class of bug Session 47's handoff flagged for the built app.js).</summary>
    public bool AssignLibraryFileToTeamEventFromWeb(string teamName, string trigger, string path)
    {
        if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(path)) return false;
        if (TeamColors.All.All(t => t.Name != teamName)) return false;

        bool isActiveTeam = string.Equals(teamName, Theme.ActiveTeam.Name, StringComparison.OrdinalIgnoreCase);
        var profile = isActiveTeam
            ? _config
            : (ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
                ? ConfigStore.LoadProfile(teamName)
                : ConfigStore.BuildDefault());

        var entry = profile.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return false;
        entry.AudioFile = path;

        if (isActiveTeam)
        {
            ConfigStore.Save(_config);
            SaveCurrentTeamProfile();
            PushCategories();
            NormalizeAssignmentInBackground(entry, isPa: false);
        }
        else
        {
            ConfigStore.SaveProfile(teamName, profile);
        }
        return true;
    }

    /// <summary>Team+event picker data for the Clipper's cross-team "Share to..." action -- same
    /// shape as GetEventsForCategory (WebBridge.cs) but reads the TARGET team's saved profile (or
    /// engine defaults if it's never been saved) instead of the live _config, so the picker can
    /// show any team's event list/current assignment without switching the active team away from
    /// whatever the user is actually working on.</summary>
    public string GetEventsForTeamFromWeb(string teamName, string? category)
    {
        if (TeamColors.All.All(t => t.Name != teamName)) return "[]";
        bool isActiveTeam = string.Equals(teamName, Theme.ActiveTeam.Name, StringComparison.OrdinalIgnoreCase);
        var profile = isActiveTeam
            ? _config
            : (ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
                ? ConfigStore.LoadProfile(teamName)
                : ConfigStore.BuildDefault());
        var filtered = string.IsNullOrEmpty(category) || category == "All"
            ? profile
            : profile.Where(e => CategoryMap.Resolve(e) == category).ToList();
        return JsonSerializer.Serialize(filtered.Select(e => new
        {
            trigger = e.Trigger,
            eventName = e.Event,
            fileName = string.IsNullOrWhiteSpace(e.AudioFile) ? null : Path.GetFileNameWithoutExtension(e.AudioFile),
        }));
    }

    /// <summary>Big Game conditional-alternate slot (TriggerEntry.BigGameAudioFile, added
    /// 2026-08-10) -- deliberately a separate pair of methods rather than folding into
    /// AssignTrackFileFromWeb's isPa bool, since BigGameAudioFile isn't a layered PA clip, it's a
    /// full alternate for AudioFile (see FireEvent's IsBigGame fallback). Skips
    /// NormalizeAssignmentInBackground's loudness pass that the main/PA slots get -- acceptable
    /// simplification for a first pass, revisit if Big Game clips come in noticeably louder/
    /// quieter than their normalized counterparts.</summary>
    public bool AssignBigGameTrackFileFromWeb(string trigger, string path)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return false;
        entry.BigGameAudioFile = path;
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        PushCategories();
        return true;
    }

    public void ClearBigGameTrackAssignmentFromWeb(string trigger) => AssignBigGameTrackFileFromWeb(trigger, "");

    /// <summary>DL button on a song-picker row in the clipping island -- files there already
    /// live in ConfigStore.SongsFolder, so this doesn't copy/move anything, it just registers
    /// the file in the My Downloads manifest (same one local imports use, see
    /// ConfigStore.RecordLocalTrack) so it shows up sorted there too instead of only being
    /// reachable by re-browsing the whole Songs library every time.</summary>
    public bool AddLibraryFileToDownloadsFromWeb(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        ConfigStore.RecordLocalTrack(Path.GetFileNameWithoutExtension(path), path);
        return true;
    }

    /// <summary>Native OpenFileDialog for "Browse for file..." on the island -- there's no web
    /// equivalent that can hand back a real filesystem path (a web &lt;input type=file&gt; only
    /// exposes a Blob, not a path AudioPlayer can play from), so this stays a native call same as
    /// AssignTrackForm's own Browse button did. Was returning the picked file's ORIGINAL path
    /// as-is -- assigned fine, but the file never actually "landed" anywhere inside Bandroom's own
    /// library (owner report: "I don't even know where the import from folder songs land"), so it
    /// never showed up in Clipper search/GetTrackLibrary, broke if the original file was later
    /// moved/renamed/deleted, and wouldn't survive if the source was on removable media. Now runs
    /// it through ConfigStore.ImportIntoSongsLibrary first, same as AssignTrackForm's own Browse
    /// button already does -- copies into Songs\uploaded, returns that copy's path instead.</summary>
    public string? BrowseForAudioFileFromWeb()
    {
        using var dlg = new OpenFileDialog { Filter = "Audio Files|*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac|All Files|*.*", Title = "Choose Audio File" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return null;
        return ConfigStore.ImportIntoSongsLibrary(dlg.FileName) ?? dlg.FileName;
    }

    /// <summary>Multi-select counterpart to BrowseForAudioFileFromWeb -- owner request for a
    /// batch "Add Songs" pill in the Clipper song list, for adding several files at once instead
    /// of repeating Browse-for-file one at a time. Same ImportIntoSongsLibrary copy-in per file
    /// (so every added song shows up in the library the same way a single Browse would), reports
    /// which ones failed rather than silently dropping them.</summary>
    public string AddSongsBatchFromWeb()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac|All Files|*.*",
            Title = "Add Songs (choose one or more)",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return JsonSerializer.Serialize(new { addedCount = 0, failedNames = Array.Empty<string>() });

        int addedCount = 0;
        var failedNames = new List<string>();
        foreach (var path in dlg.FileNames)
        {
            try
            {
                var imported = ConfigStore.ImportIntoSongsLibrary(path);
                if (imported != null) addedCount++;
                else failedNames.Add(Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                CrashLog.Write($"AddSongsBatchFromWeb failed for \"{path}\"", ex);
                failedNames.Add(Path.GetFileName(path));
            }
        }
        return JsonSerializer.Serialize(new { addedCount, failedNames });
    }

    /// <summary>Native folder picker -- backs WebBridge.RelocateDefaultSongsFolder (task queue
    /// item 7b, Session 10). Same "no web equivalent can hand back a real filesystem path"
    /// reasoning as BrowseForAudioFileFromWeb above, folder flavor.</summary>
    public string? BrowseForFolderFromWeb(string description)
    {
        using var dlg = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
    }

    public string? BrowseForSongPackZipFromWeb()
    {
        using var dlg = new OpenFileDialog { Filter = "Zip Archive|*.zip", Title = "Locate Downloaded Song Pack" };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    /// <summary>Folder-flavored counterpart to BrowseForSongPackZipFromWeb -- for a user who
    /// already unzipped the pack, or was handed a plain folder instead of a .zip.</summary>
    public string? BrowseForSongPackFolderFromWeb()
    {
        using var dlg = new FolderBrowserDialog { Description = "Locate Song Pack Folder", UseDescriptionForTitle = true };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// <summary>Extracts a pack zip the user already downloaded from the Drive link into
    /// ConfigStore.DownloadedDefaultSongsFolder -- the local counterpart to
    /// DownloadDefaultSongPackFromWeb's R2 path below, minus the HTTP download half. Reuses the
    /// same bandroom:songpack* events so app.js's existing progress UI just works for both.</summary>
    public void ImportDefaultSongPackZipFromWeb(string zipPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:songpackimporting'))"));

                bool ok = await DefaultSongPackService.ExtractExistingZipAsync(zipPath,
                    (frac, file) => RunOnUi(() => _ = _webView.ExecuteScriptAsync(
                        $"window.dispatchEvent(new CustomEvent('bandroom:songpackimportprogress', {{ detail: {{ fraction: {frac.ToString(System.Globalization.CultureInfo.InvariantCulture)}, file: {System.Text.Json.JsonSerializer.Serialize(file)} }} }}))")),
                    _lifetimeCts.Token);

                RunOnUi(() => _ = _webView.ExecuteScriptAsync(
                    ok ? "window.dispatchEvent(new CustomEvent('bandroom:songpackready'))"
                       : "window.dispatchEvent(new CustomEvent('bandroom:songpackimportfailed'))"));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Default song pack import failed", ex);
                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:songpackimportfailed'))"));
            }
        });
    }

    /// <summary>Folder-flavored counterpart to ImportDefaultSongPackZipFromWeb -- for a user who
    /// already extracted the pack (or was handed a folder instead of the .zip). Same
    /// bandroom:songpack* events, so app.js's existing progress UI works unchanged for either path.
    /// overwrite=true is the "Load All" button's path: JS has already gotten an explicit yes/no
    /// from the user before calling this (same contract as ApplyDefaultProfileForTeamOverwriteFromWeb),
    /// so on top of replacing colliding files on disk (see DefaultSongPackService.CopyFile) this
    /// also re-runs ImportDefaultPackForTeam(overwrite:true) for every team the import found, so the
    /// newly-copied songs actually land in each team's event slots instead of just sitting in the
    /// library unassigned.</summary>
    public void ImportDefaultSongPackFolderFromWeb(string folderPath, bool overwrite = false)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:songpackimporting'))"));

                var result = await DefaultSongPackService.ImportExistingFolderAsync(folderPath,
                    (frac, file) => RunOnUi(() => _ = _webView.ExecuteScriptAsync(
                        $"window.dispatchEvent(new CustomEvent('bandroom:songpackimportprogress', {{ detail: {{ fraction: {frac.ToString(System.Globalization.CultureInfo.InvariantCulture)}, file: {System.Text.Json.JsonSerializer.Serialize(file)} }} }}))")),
                    _lifetimeCts.Token, overwrite);

                string resultMessage = result.Message;
                if (result.Success && overwrite)
                {
                    int teamsAssigned = 0;
                    foreach (var teamName in result.TeamNames)
                    {
                        // Read straight from where DefaultSongPackService.CopyFile just wrote this
                        // team's files (DownloadedDefaultSongsFolder\Imported\{team}) rather than
                        // going through ImportDefaultPackForTeam/DefaultSongsFolder -- that path
                        // prefers the BUNDLED pack whenever one exists and is non-empty, which would
                        // silently ignore what the user just picked (or find nothing at all for a
                        // team the bundled pack doesn't have).
                        string importedFolder = Path.Combine(ConfigStore.DownloadedDefaultSongsFolder, "Imported", teamName);
                        var config = ConfigStore.ListProfiles().Contains(teamName, StringComparer.OrdinalIgnoreCase)
                            ? ConfigStore.LoadProfile(teamName) : ConfigStore.BuildDefault();
                        int assigned = ConfigStore.ImportTeamFolderForTeam(importedFolder, config, overwrite: true);
                        if (assigned > 0)
                        {
                            ConfigStore.SaveProfile(teamName, config);
                            RefreshHomeAwayConfigIfNeeded(teamName);
                            // RefreshActiveConfigIfNeeded calls PushCategories(), which touches the
                            // WebView2 CoreWebView2 directly -- that object is thread-affinitized to
                            // the UI thread, and this whole method runs inside Task.Run (a
                            // thread-pool thread), so this must be marshaled through RunOnUi like
                            // every other _webView touch in this method already is.
                            RunOnUi(() => RefreshActiveConfigIfNeeded(teamName, config));
                            teamsAssigned++;
                        }
                    }
                    if (teamsAssigned > 0)
                        resultMessage += $" Re-assigned event slots for {teamsAssigned} team{(teamsAssigned == 1 ? "" : "s")}.";
                }

                string msgJson = System.Text.Json.JsonSerializer.Serialize(resultMessage);
                string teamsJson = System.Text.Json.JsonSerializer.Serialize(result.TeamNames);
                RunOnUi(() => _ = _webView.ExecuteScriptAsync(result.Success
                    ? $"window.dispatchEvent(new CustomEvent('bandroom:songpackready', {{ detail: {{ message: {msgJson}, teamNames: {teamsJson} }} }}))"
                    : $"window.dispatchEvent(new CustomEvent('bandroom:songpackimportfailed', {{ detail: {{ message: {msgJson} }} }}))"));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Default song pack folder import failed", ex);
                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:songpackimportfailed'))"));
            }
        });
    }

    /// <summary>Trim button on the island -- same TrimmerForm flow OpenAssignTrack's RequestTrim
    /// branch used, just callable directly against whatever's currently assigned instead of
    /// needing the native picker dialog open first.</summary>
    public void OpenTrimmerFromWeb(string trigger, bool isPa)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        string currentPath = isPa ? entry.PaAudioFile : entry.AudioFile;
        if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
        {
            MessageBox.Show(this, "Choose a song for this situation first, then you can trim it.", "Bandroom");
            return;
        }
        TrimmerForm trimmer;
        try { trimmer = new TrimmerForm(this, currentPath); }
        catch (Exception ex)
        {
            CrashLog.Write("TrimmerForm open failed", ex);
            MessageBox.Show(this, "Couldn't open this song for trimming -- the file may be missing or in an unsupported format.", "Bandroom");
            return;
        }
        using (trimmer)
        {
            if (trimmer.ShowDialog(this) == DialogResult.OK && trimmer.SavedFilePath != null)
            {
                if (isPa) entry.PaAudioFile = trimmer.SavedFilePath; else entry.AudioFile = trimmer.SavedFilePath;
                ConfigStore.Save(_config);
                SaveCurrentTeamProfile();
                PushCategories();
            }
        }
    }

    /// <summary>Copies the trigger's currently-assigned song into the single-slot TrimSourceFolder
    /// and hands back a `trimsrc://` URL + duration so app.js can embed the waveform trimmer
    /// directly in the Events/Assign panel instead of popping the native TrimmerForm (per the
    /// owner's "clipper embedded in the song list, not a popup" request). Read-only probe --
    /// nothing is trimmed or saved here, see SaveTrimFromWeb/SaveTrimAsLeadInWhistleFromWeb below.</summary>
    public string PrepareTrimFromWeb(string trigger, bool isPa)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return JsonSerializer.Serialize(new { ok = false, error = "No such trigger." });
        string currentPath = isPa ? entry.PaAudioFile : entry.AudioFile;
        if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
            return JsonSerializer.Serialize(new { ok = false, error = "Choose a song for this situation first, then you can trim it." });

        try
        {
            Directory.CreateDirectory(ConfigStore.TrimSourceFolder);
            // Single working slot -- only one trim panel is ever open at a time, so this doesn't
            // need to be keyed by trigger; just clear whatever a previous trim session left here.
            foreach (var f in Directory.GetFiles(ConfigStore.TrimSourceFolder))
                try { File.Delete(f); } catch { /* best-effort */ }

            string fileName = "current" + Path.GetExtension(currentPath);
            string destPath = Path.Combine(ConfigStore.TrimSourceFolder, fileName);
            File.Copy(currentPath, destPath, overwrite: true);

            double durationSec;
            using (var probe = new NAudio.Wave.AudioFileReader(destPath)) durationSec = probe.TotalTime.TotalSeconds;

            return JsonSerializer.Serialize(new
            {
                ok = true,
                url = $"https://trimsrc/{Uri.EscapeDataString(fileName)}",
                fileName = Path.GetFileName(currentPath),
                durationSec,
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("PrepareTrimFromWeb failed", ex);
            return JsonSerializer.Serialize(new { ok = false, error = "Couldn't prepare that file for trimming." });
        }
    }

    /// <summary>Whistle-mode counterpart to PrepareTrimFromWeb -- there's no trigger/event to key
    /// off of when picking a lead-in whistle from Clipper Island's own song library, so this takes
    /// the chosen library path directly instead of looking up an entry's AudioFile/PaAudioFile.
    /// Same single working slot in TrimSourceFolder, same read-only probe semantics.</summary>
    public string PrepareTrimForWhistleFromWeb(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return JsonSerializer.Serialize(new { ok = false, error = "That file couldn't be found." });

        try
        {
            Directory.CreateDirectory(ConfigStore.TrimSourceFolder);
            foreach (var f in Directory.GetFiles(ConfigStore.TrimSourceFolder))
                try { File.Delete(f); } catch { /* best-effort */ }

            string fileName = "current" + Path.GetExtension(path);
            string destPath = Path.Combine(ConfigStore.TrimSourceFolder, fileName);
            File.Copy(path, destPath, overwrite: true);

            double durationSec;
            using (var probe = new NAudio.Wave.AudioFileReader(destPath)) durationSec = probe.TotalTime.TotalSeconds;

            return JsonSerializer.Serialize(new
            {
                ok = true,
                url = $"https://trimsrc/{Uri.EscapeDataString(fileName)}",
                fileName = Path.GetFileName(path),
                durationSec,
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("PrepareTrimForWhistleFromWeb failed", ex);
            return JsonSerializer.Serialize(new { ok = false, error = "Couldn't prepare that file for trimming." });
        }
    }

    /// <summary>Trims TrimSourceFolder's current file to [startSec, endSec), runs it through the
    /// same AudioNormalizer.NormalizeAndLimit pass TrimmerForm uses, saves it into
    /// SongsTrimmedFolder, and assigns it back to the trigger -- the embedded-panel equivalent of
    /// TrimmerForm.SaveTrimmed, minus the team/song-name prompts (this is re-trimming an
    /// already-assigned, already-named track, not creating a new library entry from scratch).</summary>
    public string SaveTrimFromWeb(string trigger, bool isPa, double startSec, double endSec, string? sourceName = null)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return JsonSerializer.Serialize(new { ok = false, error = "No such trigger." });
        string? srcPath = Directory.Exists(ConfigStore.TrimSourceFolder)
            ? Directory.GetFiles(ConfigStore.TrimSourceFolder).FirstOrDefault() : null;
        if (srcPath == null) return JsonSerializer.Serialize(new { ok = false, error = "Trim source is gone -- reopen the trimmer." });
        if (endSec <= startSec) return JsonSerializer.Serialize(new { ok = false, error = "End must be after start." });

        try
        {
            Directory.CreateDirectory(ConfigStore.SongsTrimmedFolder);
            // sourceName is the file that was actually loaded into the trimmer (see
            // PrepareTrimFromWeb/PrepareTrimForWhistleFromWeb) -- pass it explicitly rather than
            // re-deriving from entry.AudioFile/PaAudioFile, since the trimmer can now be pointed at
            // a different library song than whatever's currently assigned to this trigger.
            string currentPath = isPa ? entry.PaAudioFile : entry.AudioFile;
            string baseName = !string.IsNullOrWhiteSpace(sourceName) ? Path.GetFileNameWithoutExtension(sourceName)
                : !string.IsNullOrWhiteSpace(currentPath) ? Path.GetFileNameWithoutExtension(currentPath) : trigger;
            string safeBase = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^\w\s-]", "").Replace(" ", "_");
            string outPath = Path.Combine(ConfigStore.SongsTrimmedFolder, $"{safeBase}.wav");
            int n = 1;
            while (File.Exists(outPath))
                outPath = Path.Combine(ConfigStore.SongsTrimmedFolder, $"{safeBase}_{n++}.wav");

            using (var reader = new NAudio.Wave.AudioFileReader(srcPath))
            {
                var offset = new NAudio.Wave.SampleProviders.OffsetSampleProvider(reader)
                {
                    SkipOver = TimeSpan.FromSeconds(startSec),
                    Take = TimeSpan.FromSeconds(endSec - startSec),
                };
                var normalized = AudioNormalizer.NormalizeAndLimit(offset);
                NAudio.Wave.WaveFileWriter.CreateWaveFile16(outPath, normalized);
            }

            if (isPa) entry.PaAudioFile = outPath; else entry.AudioFile = outPath;
            ConfigStore.Save(_config);
            SaveCurrentTeamProfile();
            PushCategories();

            return JsonSerializer.Serialize(new { ok = true, fileName = Path.GetFileName(outPath) });
        }
        catch (Exception ex)
        {
            CrashLog.Write("SaveTrimFromWeb failed", ex);
            return JsonSerializer.Serialize(new { ok = false, error = "Couldn't save the trimmed clip." });
        }
    }

    /// <summary>Embedded-panel equivalent of TrimmerForm.SaveTrimmedAsLeadInWhistle -- same
    /// "no per-clip file, always overwrites the one active whistle" behavior.</summary>
    public string SaveTrimAsLeadInWhistleFromWeb(double startSec, double endSec)
    {
        string? srcPath = Directory.Exists(ConfigStore.TrimSourceFolder)
            ? Directory.GetFiles(ConfigStore.TrimSourceFolder).FirstOrDefault() : null;
        if (srcPath == null) return JsonSerializer.Serialize(new { ok = false, error = "Trim source is gone -- reopen the trimmer." });
        if (endSec <= startSec) return JsonSerializer.Serialize(new { ok = false, error = "End must be after start." });

        try
        {
            Directory.CreateDirectory(ConfigStore.SongsFolder);
            using (var reader = new NAudio.Wave.AudioFileReader(srcPath))
            {
                var offset = new NAudio.Wave.SampleProviders.OffsetSampleProvider(reader)
                {
                    SkipOver = TimeSpan.FromSeconds(startSec),
                    Take = TimeSpan.FromSeconds(endSec - startSec),
                };
                var normalized = AudioNormalizer.NormalizeAndLimit(offset);
                NAudio.Wave.WaveFileWriter.CreateWaveFile16(ConfigStore.LeadInWhistlePath, normalized);
            }
            AudioPlayer.LeadInClipPath = ConfigStore.LeadInWhistlePath;
            AudioPlayer.LeadInEnabled = true;
            return JsonSerializer.Serialize(new { ok = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write("SaveTrimAsLeadInWhistleFromWeb failed", ex);
            return JsonSerializer.Serialize(new { ok = false, error = "Couldn't save the whistle clip." });
        }
    }

    // Owner report: volume sliders reset to 100% every launch -- persist to ConfigStore.AudioSettings
    // on every change. Debounced: the UI sliders fire SetXVolumeFromWeb on every `input` tick while
    // being dragged (dozens of calls/second), and a synchronous File.WriteAllText on every one of
    // those would be wasteful disk thrashing for a value that only needs to be durable once the user
    // stops moving the slider. 400ms settle, same ballpark as the Sound Booth knobs' own JS-side
    // commit debounce.
    System.Threading.Timer? _audioSettingsSaveTimer;
    void PersistAudioSettingsDebounced()
    {
        _audioSettingsSaveTimer ??= new System.Threading.Timer(_ =>
        {
            try
            {
                ConfigStore.SaveAudioSettings(new ConfigStore.AudioSettings(
                    (int)(AudioPlayer.MasterVolume * 100), (int)(AudioPlayer.HomeVolume * 100),
                    (int)(AudioPlayer.AwayVolume * 100), (int)(AudioPlayer.PaVolume * 100),
                    (int)(AudioPlayer.WhistleVolume * 100)));
            }
            catch (Exception ex) { CrashLog.Write("PersistAudioSettingsDebounced failed", ex); }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _audioSettingsSaveTimer.Change(400, System.Threading.Timeout.Infinite);
    }

    public void SetVolumeFromWeb(int percent) { AudioPlayer.MasterVolume = percent / 100f; PersistAudioSettingsDebounced(); }
    public int GetVolumeFromWeb() => (int)(AudioPlayer.MasterVolume * 100);

    /// <summary>Matchup-mode independent side volumes -- lets the home and away team's cues be
    /// balanced (or one muted) separately, since both sides' events can legitimately fire close
    /// together in a real game now that possession detection routes each to its own side.</summary>
    public void SetHomeVolumeFromWeb(int percent) { AudioPlayer.HomeVolume = percent / 100f; PersistAudioSettingsDebounced(); }
    public void SetAwayVolumeFromWeb(int percent) { AudioPlayer.AwayVolume = percent / 100f; PersistAudioSettingsDebounced(); }
    public int GetHomeVolumeFromWeb() => (int)(AudioPlayer.HomeVolume * 100);
    public int GetAwayVolumeFromWeb() => (int)(AudioPlayer.AwayVolume * 100);

    /// <summary>PA Announcer layer volume -- independent of Master/Home/Away since PA clips play
    /// concurrently with (not instead of) the main song for the same event. See AudioPlayer.PaVolume.</summary>
    public void SetPaVolumeFromWeb(int percent) { AudioPlayer.PaVolume = percent / 100f; PersistAudioSettingsDebounced(); }
    public int GetPaVolumeFromWeb() => (int)(AudioPlayer.PaVolume * 100);

    /// <summary>Lead-in whistle volume -- independent of Master/Home/Away/PA, same reasoning as
    /// SetPaVolumeFromWeb. See AudioPlayer.WhistleVolume.</summary>
    public void SetWhistleVolumeFromWeb(int percent) { AudioPlayer.WhistleVolume = percent / 100f; PersistAudioSettingsDebounced(); }
    public int GetWhistleVolumeFromWeb() => (int)(AudioPlayer.WhistleVolume * 100);

    public bool GetLeadInWhistleAvailableFromWeb() => !string.IsNullOrWhiteSpace(AudioPlayer.LeadInClipPath) && File.Exists(AudioPlayer.LeadInClipPath);
    public bool GetLeadInWhistleEnabledFromWeb() => AudioPlayer.LeadInEnabled;
    public void SetLeadInWhistleEnabledFromWeb(bool enabled)
    {
        AudioPlayer.LeadInEnabled = enabled;
        ConfigStore.SaveLeadInWhistleEnabled(enabled);
    }

    /// <summary>Task queue item 5 (Session 11): the ONLY way to set a lead-in whistle used to be
    /// TrimmerForm's "Set as Lead-In Whistle" button, buried inside the trim-a-song flow -- there
    /// was no direct upload path at all. Native OpenFileDialog (same "web &lt;input type=file&gt;
    /// can't hand back a real filesystem path" reasoning as every other browse method in this
    /// class), copies the chosen file straight to ConfigStore.LeadInWhistlePath (overwriting
    /// whatever was there -- this IS the "replace" flow, not just "add"), points AudioPlayer at
    /// it, and turns it on so picking a new whistle doesn't silently stay disabled from a
    /// previous session. Returns bool, not JSON -- there's nothing else the caller needs back.</summary>
    public bool BrowseAndSetLeadInWhistleFromWeb()
    {
        using var dlg = new OpenFileDialog { Filter = "Audio Files|*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac|All Files|*.*", Title = "Choose Lead-In Whistle Clip" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;

        try
        {
            Directory.CreateDirectory(ConfigStore.SongsFolder);
            File.Copy(dlg.FileName, ConfigStore.LeadInWhistlePath, overwrite: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("BrowseAndSetLeadInWhistleFromWeb failed to copy file", ex);
            return false;
        }

        AudioPlayer.LeadInClipPath = ConfigStore.LeadInWhistlePath;
        AudioPlayer.LeadInEnabled = true;
        ConfigStore.SaveLeadInWhistleEnabled(true);

        // Sound Booth item #3: the whistle gets its own -12 LUFS target (short transient, needs
        // to cut through) -- normalize in the background, same as song/PA assignment.
        string whistlePath = ConfigStore.LeadInWhistlePath;
        Task.Run(() =>
        {
            string normalized = LoudnessNormalizationService.NormalizeToTarget(whistlePath, LoudnessKind.LeadInWhistle);
            if (normalized == whistlePath) return;
            RunOnUi(() => AudioPlayer.LeadInClipPath = normalized);
        });

        return true;
    }

    /// <summary>"Fire Sensitivity" slider -- delay in seconds before a fired cue starts fading
    /// out. No fade-in: AudioPlayer.Play already jumps straight to full volume, only the
    /// fade-OUT ramp exists, so this only ever tunes when that ramp begins.
    /// FIXED 2026-08-12 (owner report: fade-out setting doesn't survive a relaunch) -- this only
    /// ever wrote AudioPlayer.FadeStartSeconds (an in-memory static), never through
    /// ConfigStore.SavePlaybackTimingSettings the way the Settings panel's own "Apply Timing" path
    /// does (see ConfigStore.PlaybackTimingSettings' doc comment -- this exact class of bug already
    /// happened once before for all 4 of these fields and got fixed for the Settings-panel path,
    /// but this separate knob/slider control quietly regressed the same fix). Now persists through
    /// the same record, preserving whatever PreRollSeconds/FadeOutDuration/CooldownSeconds are
    /// already saved so this slider doesn't clobber settings adjusted elsewhere.</summary>
    public void SetFadeDelayFromWeb(int seconds)
    {
        AudioPlayer.FadeStartSeconds = seconds;
        var current = ConfigStore.LoadPlaybackTimingSettings();
        ConfigStore.SavePlaybackTimingSettings(current with { FadeStartSeconds = seconds });
    }
    public int GetFadeDelayFromWeb() => (int)AudioPlayer.FadeStartSeconds;

    // "dome"/"stadiumrain" retired 2026-08-11 (owner call) -- falls to Off below like any other
    // unrecognized key, so a profile that saved one of those before removal doesn't crash.
    public void SetReverbFromWeb(string key) => AudioPlayer.CurrentReverb = key switch
    {
        "stadium" => ReverbPreset.Stadium,
        "nightgame" => ReverbPreset.NightGame,
        "nightgameprimetime" => ReverbPreset.NightGamePrimeTime,
        _ => ReverbPreset.Off,
    };
    public string GetReverbFromWeb() => AudioPlayer.CurrentReverb switch
    {
        ReverbPreset.Stadium => "stadium",
        ReverbPreset.NightGame => "nightgame",
        ReverbPreset.NightGamePrimeTime => "nightgameprimetime",
        _ => "off",
    };

    /// <summary>Live IN/OUT peak levels for the Sound Booth's meters (0-1). Decays each poll so
    /// the bars fall back toward 0 between clips instead of sticking at the last hit -- there's
    /// no continuous "currently playing" signal to key off, so decay-per-read is the simplest
    /// way to get meters that look alive without a dedicated silence-detection path.</summary>
    public string GetCurrentLevelsFromWeb()
    {
        float inLvl = AudioPlayer.CurrentInputLevel;
        float outLvl = AudioPlayer.CurrentOutputLevel;
        AudioPlayer.CurrentInputLevel *= 0.6f;
        AudioPlayer.CurrentOutputLevel *= 0.6f;
        return $"{{\"in\":{inLvl.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"out\":{outLvl.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
    }

    // ---- Sound Booth toggles (Sound Booth dashboard -> WebBridge -> here, mirrors the
    // existing SetReverbFromWeb/SetFadeDelayFromWeb pattern above). ----
    public string GetEqPresetFromWeb() => AudioPlayer.CurrentEq.ToString().ToLowerInvariant();
    public void SetEqPresetFromWeb(string key) => AudioPlayer.CurrentEq = key switch
    {
        "marchingband" => EqPreset.MarchingBand,
        "megaphone" => EqPreset.Megaphone,
        _ => EqPreset.Off,
    };

    public bool GetTransientShaperEnabledFromWeb() => AudioPlayer.TransientShaperEnabled;
    public void SetTransientShaperEnabledFromWeb(bool enabled) => AudioPlayer.TransientShaperEnabled = enabled;

    public bool GetStereoWidenerEnabledFromWeb() => AudioPlayer.StereoWidenerEnabled;
    public void SetStereoWidenerEnabledFromWeb(bool enabled) => AudioPlayer.StereoWidenerEnabled = enabled;

    public bool GetDuckingEnabledFromWeb() => AudioPlayer.DuckingEnabled;
    public void SetDuckingEnabledFromWeb(bool enabled) => AudioPlayer.DuckingEnabled = enabled;

    public bool GetNoEffectsBypassFromWeb() => AudioPlayer.NoEffectsBypass;
    public void SetNoEffectsBypassFromWeb(bool enabled) => AudioPlayer.NoEffectsBypass = enabled;

    public bool GetControllerRumbleEnabledFromWeb() => ControllerRumbleService.Enabled;
    public void SetControllerRumbleEnabledFromWeb(bool enabled) => ControllerRumbleService.Enabled = enabled;

    public string GetSubBassLevelFromWeb() => AudioPlayer.SubBassLevel.ToString().ToLowerInvariant();
    public void SetSubBassLevelFromWeb(string level) => AudioPlayer.SubBassLevel = level switch
    {
        "subtle" => SubBassIntensity.Subtle,
        "stadium" => SubBassIntensity.Stadium,
        "earthquake" => SubBassIntensity.Earthquake,
        _ => SubBassIntensity.Off,
    };

    public bool GetCrowdBusEnabledFromWeb() => CrowdBusService.Enabled;
    public void SetCrowdBusEnabledFromWeb(bool enabled) => CrowdBusService.Enabled = enabled;
    public bool GetCrowdBusClipAvailableFromWeb() => !string.IsNullOrWhiteSpace(CrowdBusService.ClipPath) && File.Exists(CrowdBusService.ClipPath);

    /// <summary>Same native-file-picker pattern as BrowseAndSetLeadInWhistleFromWeb -- there's
    /// no bundled crowd-ambience loop in this repo, so the user supplies their own.</summary>
    public bool BrowseAndSetCrowdBusClipFromWeb()
    {
        using var dlg = new OpenFileDialog { Filter = "Audio Files|*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac|All Files|*.*", Title = "Choose Crowd Ambience Loop" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;

        try
        {
            Directory.CreateDirectory(ConfigStore.SongsFolder);
            string dest = Path.Combine(ConfigStore.SongsFolder, "crowd-ambience" + Path.GetExtension(dlg.FileName));
            File.Copy(dlg.FileName, dest, overwrite: true);
            CrowdBusService.ClipPath = dest;
            CrowdBusService.Enabled = true;
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("BrowseAndSetCrowdBusClipFromWeb failed to copy file", ex);
            return false;
        }
    }

    /// <summary>Read-only: Windows' own system output volume/mute, alongside Bandroom's own
    /// LUFS-normalized levels (item #3) -- so the Sound Booth can tell a user "your songs are
    /// balanced, but Windows itself is muted/turned down" instead of that looking like a
    /// Bandroom bug.</summary>
    public string GetSystemVolumeInfoFromWeb()
    {
        var info = SystemVolumeService.GetCurrentOutputVolume();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            known = info != null,
            volumePercent = info != null ? (int)Math.Round(info.VolumeScalar * 100) : 100,
            muted = info?.Muted ?? false,
        });
    }

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
        const int margin = ResizeMargin;
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
        // BUG FIX (audit cross-check): this snapshot dropped PaAudioFile and Volume, silently
        // losing every PA-announcer assignment and per-event volume tweak on every "Copy to All
        // Teams" -- only the main AudioFile survived the copy.
        var snapshot = _config.Select(e => new TriggerEntry { Trigger = e.Trigger, Event = e.Event, AudioFile = e.AudioFile, PaAudioFile = e.PaAudioFile, Volume = e.Volume }).ToList();
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
        // Every other config-mutating path here (SaveProfileAs/Duplicate/Import/Auto-Assign)
        // re-syncs _homeConfig/_awayConfig after touching disk -- this one didn't, so deleting
        // the active team's profile mid-matchup left stale in-memory home/away data for the
        // rest of the game even though disk (and the just-reset _config) had moved on.
        RefreshHomeAwayConfigIfNeeded(Theme.ActiveTeam.Name);
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

    /// <summary>targetTeamName is chosen by the user in the Load Profile dialog's team-select
    /// step (app.js openLoadProfileDialog) BEFORE this runs -- a shared/downloaded profile is
    /// very often meant for a team other than whatever is currently active, so this always
    /// imports into the explicitly-picked team rather than assuming Theme.ActiveTeam. If the
    /// target happens to be the currently-active team, _config/PushCategories are updated live
    /// too so the assign screen reflects the import immediately; otherwise it's saved straight to
    /// that team's profile on disk and picked up next time that team is selected.</summary>
    public void ImportProfileFromWeb(string targetTeamName)
    {
        if (string.IsNullOrWhiteSpace(targetTeamName)) return;
        RunOnUi(() =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = $"Import Profile for {targetTeamName}",
                Filter = "Bandroom Profile (*.json)|*.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var imported = System.Text.Json.JsonSerializer.Deserialize<List<TriggerEntry>>(
                    File.ReadAllText(dlg.FileName),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (imported == null) return;

                ConfigStore.SaveProfile(targetTeamName, imported);
                RefreshHomeAwayConfigIfNeeded(targetTeamName);

                if (string.Equals(targetTeamName, Theme.ActiveTeam.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _config = imported;
                    ConfigStore.Save(_config);
                    PushCategories();
                }

                // profileschanged (not profileimported -- that event is for the separate
                // universal user-profile/stats import flow) is what the save-list/song-assign UI
                // already listens for on any trigger-profile mutation.
                _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:profileschanged'))");
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
                var rng = Random.Shared;
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

    // FIXED 2026-08-11 (live bug: a touchdown fired right after a turnover -- e.g. a fumble
    // recovery followed by a score a play or two later -- and the touchdown cue's StopAll()
    // cut the still-playing turnover cue off abruptly mid-clip instead of letting the two big
    // moments coexist). Both are already flagged isHighPriority specifically so OTHER audio
    // ducks out of their way instead of getting hard-stopped -- but interruptPrevious was
    // unconditionally true for any new real trigger, so a second big-moment cue stopped the
    // first ANYWAY before ducking even had a chance to matter. Tracks the last high-priority
    // fire and skips the interrupt (layers + ducks instead, same trick same-tick multi-fires
    // already use) if another one lands within this grace window.
    static readonly TimeSpan HighPriorityOverlapGrace = TimeSpan.FromSeconds(6);
    DateTime _lastHighPriorityFireUtc = DateTime.MinValue;

    void FireEvent(TriggerEntry entry, float? volumeOverride = null, bool interruptPrevious = true, string? channel = null)
    {
        // Per-event volume (TriggerEntry.Volume, 0-100) is a multiplier on top of whichever base
        // volume this call would already use -- lets one card be balanced quieter/louder without
        // touching Master/Home/Away/PA.
        float eventVolumeScale = Math.Clamp(entry.Volume, 0, 100) / 100f;

        // Big Game conditional alternate (TriggerEntry.BigGameAudioFile, added 2026-08-10) --
        // used INSTEAD of AudioFile, not layered like PaAudioFile below, only when both a Big
        // Game is currently flagged and a variant is actually assigned. Falls back to the
        // ordinary AudioFile otherwise, same as if no variant existed.
        string audioFile = (_watcher.IsBigGame && !string.IsNullOrWhiteSpace(entry.BigGameAudioFile))
            ? entry.BigGameAudioFile
            : entry.AudioFile;

        if (!string.IsNullOrWhiteSpace(audioFile) && File.Exists(audioFile))
        {
            float mainVolume = (volumeOverride ?? AudioPlayer.MasterVolume) * eventVolumeScale;
            // Sound Booth item #9: Touchdown/Turnover/Safety are the "big moment" events that
            // duck everything else so this cue (and its PA layer below) cuts through clearly.
            bool isHighPriority = entry.Event.Contains("Touchdown", StringComparison.OrdinalIgnoreCase)
                || entry.Event.Contains("Turnover", StringComparison.OrdinalIgnoreCase)
                || entry.Event.Contains("Safety", StringComparison.OrdinalIgnoreCase);
            bool isBigHit = entry.Event.Contains("Tackle for Loss", StringComparison.OrdinalIgnoreCase);
            bool isPregame = entry.Event.Contains("Pregame Ready", StringComparison.OrdinalIgnoreCase);
            RecordSongTriggered(entry.Event);

            // See HighPriorityOverlapGrace's doc comment: a second big moment within the grace
            // window layers/ducks instead of hard-stopping the first.
            if (isHighPriority)
            {
                if (DateTime.UtcNow - _lastHighPriorityFireUtc < HighPriorityOverlapGrace)
                    interruptPrevious = false;
                _lastHighPriorityFireUtc = DateTime.UtcNow;
            }

            // REMOVED 2026-08-11: the configurable Sound Start Delay (0-5000ms, previously
            // _soundStartDelayMs) between an event firing and playback actually starting, and the
            // _soundFireGeneration staleness-guard machinery that existed solely to keep a stale
            // delayed play from clobbering fresher audio -- owner's explicit call to drop the
            // feature entirely. Back to firing synchronously, same as before that feature existed.
            string paFile = entry.PaAudioFile;
            bool paExists = !string.IsNullOrWhiteSpace(paFile) && File.Exists(paFile);
            AudioPlayer.Play(audioFile, mainVolume, interruptPrevious: interruptPrevious, isHighPriorityEvent: isHighPriority, isBigHitEvent: isBigHit, isPregameEvent: isPregame, playLeadInWhistle: entry.PlayLeadInWhistle, speed2x: entry.PlaybackSpeed2x, whistleSpeed: entry.WhistleSpeed, channel: channel);
            // PA Announcer layer: plays concurrently with the main cue above, not instead of it,
            // so interruptPrevious MUST be false here -- true would call StopAll() and kill the
            // main clip that was just started a line above. Fired after, not before, the main
            // Play() call for the same reason (StopAll stops everything already in ActiveOutputs).
            // isHighPriorityEvent must match the main cue's -- the whole point of ducking on a
            // Touchdown/Turnover/Safety is so this PA layer cuts through clearly too; leaving it
            // false here would duck the PA clip right along with everything else it's supposed to
            // rise above.
            if (paExists)
                AudioPlayer.Play(paFile, AudioPlayer.PaVolume * eventVolumeScale, interruptPrevious: false, isHighPriorityEvent: isHighPriority);
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

    /// <summary>Legacy TFL fire path -- gated off now that TflHelper covers the same signal
    /// through the engine (STATE_MACHINE_ANALYSIS.md Discrepancy #9: this and the engine's
    /// TflHelper both fired "Defense: Tackle for Loss" for the same play, masked only by
    /// AudioPlayer.FireCooldown suppressing the second call within 20s -- not a fix, just a
    /// window where it happened to not be audible). Kept, not deleted, as the fallback path for
    /// if _useEngineForEvents is ever turned off again, same as OnDownChanged/OnRegionChanged
    /// above.</summary>
    void OnTackleForLoss()
    {
        if (_useEngineForEvents) return;
        RunOnUi(() =>
        {
            if (_homeConfig == null || _awayConfig == null || _possession == null) return;
            string defenseSide = _possession == "home" ? "away" : "home";
            FireEventForSide(defenseSide, "Defense: Tackle for Loss");
        });
    }

    /// <summary>Receives a list of matched evaluator events from GameWatcher's rule engine
    /// and routes each to the correct side's audio pipeline via FireEventForSide.
    /// "Offense:*" events fire for the possession side; "Defense:*" fires for the opposite.</summary>
    /// <summary>Home-only-always tier (REDESIGN 2026-08-10, replaces the old two-tier gating):
    /// these Defense:*-prefixed cues never play for away, period -- not even during a Big Game.
    /// Distinct from the ordinary Defense tier below, which DOES unlock for away during a Big
    /// Game. Owner's explicit call for these two specifically ("3rd & Long" and the new
    /// "Defense: After Opening Kick" post-kickoff cue, renamed 2026-08-11 from "Defense: First
    /// Down") -- rare/subtle enough that even a full away band wouldn't bother, per the owner's
    /// real band experience.</summary>
    static readonly HashSet<string> HomeOnlyAlwaysEventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // "Defense: Third Down" REMOVED from this set 2026-08-11 (owner, live game): a real 3rd &
        // long stop is a big enough moment that away's own band should get it too now, same
        // balanced-dual-fire treatment as "Defense/Offense: Third Down Short" -- see
        // DefenseThirdDownHelper (100%) + OffenseDownHelper's long branch (60%, new
        // "Offense: Third Down" key). Reverses the "rare/subtle enough that even a full away band
        // wouldn't bother" call made earlier the same day.
        // "Defense: After Opening Kick" REMOVED 2026-08-12 (owner, live game, Big Game): same
        // reversal as "Defense: Third Down" above -- now a balanced dual-fire with the new
        // "Offense: After Opening Kick" (see OffenseAfterOpeningKickHelper), so it needs ordinary
        // Defense:* routing (home always, away during Big Game) instead of never-plays-for-away.
        // Added 2026-08-11 (owner audit call, same session as the rename) -- home crowd cue for
        // stopping the opponent right after a punt; away's own travel band shouldn't get this one.
        "Defense: After Punt",
    };

    /// <summary>The actual routing rules for one engine event -- side-flip (Defense:*/Penalty:
    /// Offense route to the opposite of possession) and the Big Game away-volume gate. Pulled out
    /// of OnEngineEventsDetected's loop body so FireTestEventRoutedFromWeb (the Ctrl+Shift+T test
    /// hook's "routed" fire button) exercises the *exact same* logic instead of a hand-copied
    /// reimplementation that could silently drift from the real path.
    ///
    /// REDESIGN 2026-08-10: the old version gated ANY event routed to "away" (checked
    /// routedSide=="away" regardless of prefix), which wrongly throttled an away team's own
    /// Offense:*-prefixed cues too -- very likely the root cause behind a live dev-build report
    /// ("I'm on offense and a defensive song played, wrong side of the ball"; not fully traced
    /// before this rewrite, re-verify against a live build). Real model, worked out with the
    /// owner: Big Game gating should ONLY ever apply to Defense:*-prefixed cues (celebrating
    /// stopping the opponent -- a small travel band plausibly skips those). Offense:*-prefixed
    /// cues (hyping your OWN team's play) always fire full volume for whoever's driving, home or
    /// away, Big Game irrelevant -- scoped to just the down/distance cards discussed this
    /// session, not touching Offense: Touchdown/PAT/Field Goal/etc, which already have sensible
    /// existing IsEarnedBigEvent 25%/100% behavior nobody flagged as wrong.
    ///
    /// Three tiers now (was two):
    /// 1. Home-only-always (HomeOnlyAlwaysEventKeys) -- never plays for away, Big Game or not.
    /// 2. Un-gated Offense:* -- always full volume for whoever's driving.
    /// 3. Ordinary Defense:* -- home always; away only during Big Game (full volume then),
    ///    otherwise away only for IsEarnedBigEvent cues at 25%. Same as the old single tier.</summary>
    (string routedSide, float volumeMultiplier, bool allowed, string skipReason) ResolveEventRouting(
        string eventKey, bool isEarnedBigEvent, string possessionSide)
    {
        bool routesLikeDefense = (eventKey.StartsWith("Defense:") && eventKey != "Defense: Touchdown Scored")
            || eventKey == "Penalty: Offense";
        string routedSide = routesLikeDefense
            ? (possessionSide == "home" ? "away" : "home")
            : possessionSide;

        bool sideAllowed = HomeOnlyEventsForNow ? routedSide == "home" : true;
        float volumeMultiplier = 1f;
        string reason = HomeOnlyEventsForNow && routedSide != "home"
            ? "away-team events are turned off right now"
            : "";

        if (sideAllowed && routedSide == "away")
        {
            if (HomeOnlyAlwaysEventKeys.Contains(eventKey))
            {
                sideAllowed = false;
                reason = "this event is home-only, even during a Big Game";
            }
            else if (eventKey.StartsWith("Offense:", StringComparison.OrdinalIgnoreCase))
            {
                // Un-gated -- your own team's hype cue always plays for whoever's driving.
            }
            else if (!_watcher.IsBigGame)
            {
                if (!isEarnedBigEvent)
                {
                    sideAllowed = false;
                    reason = "not a Big Game -- away only plays big/earned events";
                }
                volumeMultiplier = 0.25f;
            }
        }

        if (sideAllowed) volumeMultiplier *= FieldPositionVolumeMultiplier(routedSide);

        return (routedSide, volumeMultiplier, sideAllowed, reason);
    }

    /// <summary>Owner request 2026-08-12 ("big game volume system... only on the cfb27 default
    /// scorebug logic"): CFB27's arrow next to the ball-position number (see
    /// GameWatcher.ArrowUp's doc comment) flags which half of the field the ball is on. Home plays
    /// full volume / away plays quieter when the arrow's up, and the reverse when it's down --
    /// simulates the two teams' band sections being physically seated on opposite ends of the
    /// stadium, closer to one end zone than the other. Stacks (multiplies) with the existing
    /// Big-Game-away-quiet multiplier above rather than replacing it, per the owner's explicit call
    /// to keep tonight's already-confirmed routing/volume logic intact and layer this on top --
    /// scoped to Big Game + CFB27 only, a no-op (1x) for every other combination. Best-effort OCR
    /// (see ArrowUp) -- ArrowUp being null (not yet read, wrong preset, ambiguous frame) is a no-op
    /// on purpose, same "don't guess" philosophy as the rest of this file's possession handling.</summary>
    float FieldPositionVolumeMultiplier(string side)
    {
        if (!_watcher.IsBigGame) return 1f;
        if (_watcher.ActivePreset.Name != ScorebugPreset.CollegeFootball27.Name) return 1f;
        if (_watcher.ArrowUp is not bool arrowUp) return 1f;

        bool homeLoud = arrowUp;
        return side == "home"
            ? (homeLoud ? 1f : 0.6f)
            : (homeLoud ? 0.6f : 1f);
    }

    /// <summary>Test-hook path (see FireTestEventFromWeb for the simpler unrouted version) --
    /// takes a POSSESSION side (which team currently has the ball) rather than a fire side, and
    /// runs it through the real ResolveEventRouting so the Defense:*-prefix side-flip and the Big
    /// Game away-volume gate are actually exercised, not bypassed. isEarnedBigEvent is a manual
    /// checkbox in the test panel since it's normally set per-play by whichever helper fired the
    /// event, not derivable from the EventKey string alone.</summary>
    public string FireTestEventRoutedFromWeb(string possessionSide, string eventKey, bool isEarnedBigEvent)
    {
        var (routedSide, volumeMultiplier, allowed, reason) = ResolveEventRouting(eventKey, isEarnedBigEvent, possessionSide);
        if (!allowed) return $"blocked:{reason}";
        return $"{routedSide}|" + FireEventForSide(routedSide, eventKey, bypassCooldown: true, volumeMultiplier: volumeMultiplier);
    }

    /// <summary>Test-hook path for the same-tick double-fire scenario (e.g. Offense: Third Down
    /// Short + the new Defense: Third Down Short firing together, routed to opposite sides) --
    /// fires both through ResolveEventRouting exactly like a real tick's batch would, first
    /// event interrupting, second layering, so the fix in FireEventForSide/OnEngineEventsDetected
    /// (see their own comments) can be heard/verified without a live game.</summary>
    public string FireTestEventPairFromWeb(string possessionSide, string eventKeyA, string eventKeyB, bool isEarnedBigEvent)
    {
        var results = new List<string>();
        bool firedYet = false;
        foreach (var eventKey in new[] { eventKeyA, eventKeyB })
        {
            var (routedSide, volumeMultiplier, allowed, reason) = ResolveEventRouting(eventKey, isEarnedBigEvent, possessionSide);
            if (!allowed) { results.Add($"{eventKey}={routedSide}|blocked:{reason}"); continue; }
            string result = FireEventForSide(routedSide, eventKey, bypassCooldown: true, volumeMultiplier: volumeMultiplier, interruptPrevious: !firedYet);
            if (result.StartsWith("fired:")) firedYet = true;
            results.Add($"{eventKey}={routedSide}|{result}");
        }
        return string.Join(";", results);
    }

    void OnEngineEventsDetected(IReadOnlyList<TriggerEvent> events)
    {
        RunOnUi(() =>
        {
            // If matchup not set yet, fall back to the single-team config (legacy mode).
            if (_homeConfig == null || _awayConfig == null) return;
            // STATE_MACHINE_ANALYSIS.md Race #3: this used to default to "home" when possession
            // hadn't been read yet (right after GAMETIME, before the first real possession-color
            // sample), which could fire a "Defense:*" cue for the wrong team -- e.g. an away-team
            // defensive stop misrouted to home's audio. Every other possession-dependent read in
            // this file (penalizedIsHome/possessionIsHomeNow in GameWatcher.RouteEngineTick,
            // OnTackleForLoss above) already waits for a real read instead of guessing; this now
            // matches that convention rather than being the one place that guesses wrong.
            if (_possession == null)
            {
                // Side-specific events can't be routed yet -- log each one as skipped so a user
                // wondering "why didn't my song play" has an answer instead of silence. Only the
                // side-specific ones are logged here; "Other:*" events below this branch still
                // fire normally and get their own fired/skipped log entries.
                foreach (var evt in events.Where(e => !e.EventKey.StartsWith("Other:")))
                {
                    EventActivityLog.Record(evt.EventKey, "n/a",
                        $"{EventActivityLog.FriendlyEventName(evt.EventKey)} -- skipped: we haven't figured out which team has the ball yet");
                }
                // Side-agnostic "Other:*" events (kickoff, quarter starts, pregame) aren't tied to
                // a real team the way Offense:/Defense: cues are, but they CAN legitimately fire
                // before _possession has ever been read -- possession sampling is itself suppressed
                // while a "situation" like kickoff is on screen (GameWatcher.RouteEngineTick's
                // situationActive gate), so opening kickoff can hit this branch on literally the
                // game's first tick. Fall back to "home" for just these; every side-specific event
                // still returns above and waits for a real read, preserving the STATE_MACHINE_
                // ANALYSIS.md Race #3 fix (guessing "home" there misroutes a defensive/offensive
                // cue to the wrong team -- that risk doesn't apply to a side-agnostic cue).
                bool otherFiredYet = false;
                foreach (var evt in events.Where(e => e.EventKey.StartsWith("Other:")))
                {
                    string result = FireEventForSide("home", evt.EventKey, interruptPrevious: !otherFiredYet);
                    if (result.StartsWith("fired:")) otherFiredYet = true;
                    OnLog($"[engine] {evt.EventKey} -> home (no possession read yet): {result}");
                    RecordFireResult(evt.EventKey, "home", result);
                }
                return;
            }
            string side = _possession;

            // Same-tick multi-fire fix (see FireEventForSide's interruptPrevious doc comment):
            // only the FIRST event actually fired this tick clears out leftover audio from
            // before this tick; every subsequent one in the same batch layers instead of cutting
            // the first off. Two evaluators (e.g. Offense: Third Down Short + the new
            // Defense: Third Down Short) can legitimately both fire on one tick, routed to
            // opposite sides -- without this, only the last-processed one would ever be audible.
            bool firedYet = false;

            foreach (var evt in events)
            {
                // "Penalty: Offense" (the offense committed it) is a special case alongside the
                // "Defense:*" prefix check: PenaltyHelper's own intent (see its comments) is that
                // an offense penalty cue plays for the DEFENSE side (celebrating the opponent's
                // mistake), same direction as every "Defense:*" event, even though its own
                // EventKey is named after who committed the penalty, not who should hear the cue.
                // Found 2026-08-07: without this, "Penalty: Offense" silently routed to the
                // offense's own side instead -- moot until penalty-side OCR detection existed to
                // ever populate it, but real now that it does.
                // "Defense: Touchdown Scored" is the odd one out among "Defense:*" keys: every
                // other Defense event fires while `side`/_possession still reflects the offense
                // that's on the field, so flipping to the opposite side reaches the defense. But
                // TouchdownHelper only returns this key when state.Delta.NewPossession is true --
                // i.e. a pick-six/fumble-return TD, where GameState's own possession has ALREADY
                // flipped to the team that just scored by the time this event fires. _possession
                // is driven by the same real-time possession sampling, so `side` here already IS
                // the scoring team. Flipping it (like every other "Defense:" key) routed the pick-
                // six celebration cue to the team that got scored on instead of the team that
                // scored -- reported live as "home TD linking to away" (or vice versa).
                var (routedSide, volumeMultiplier, sideAllowed, reason) = ResolveEventRouting(evt.EventKey, evt.IsEarnedBigEvent, side);

                if (sideAllowed)
                {
                    string result = FireEventForSide(routedSide, evt.EventKey, volumeMultiplier: volumeMultiplier, interruptPrevious: !firedYet);
                    if (result.StartsWith("fired:")) firedYet = true;
                    OnLog($"[engine] {evt.EventKey} -> {routedSide}: {result}");
                    RecordFireResult(evt.EventKey, routedSide, result);
                }
                else
                {
                    OnLog($"[engine] {evt.EventKey} -> {routedSide}: blocked ({reason})");
                    EventActivityLog.Record(evt.EventKey, routedSide,
                        $"{EventActivityLog.FriendlyEventName(evt.EventKey)} ({DisplaySide(routedSide)}) -- skipped: {reason}");
                }
            }
        });
    }

    /// <summary>Temporary simplification, requested by the owner ahead of a major push: only the
    /// HOME team's events should fire for now. Away-side firing (via FireTriggerForSide/
    /// FireEventForSide below) is disabled, not deleted, so it's a one-line revert once away-side
    /// support is wanted again. Tackle-for-Loss is deliberately exempt -- see OnTackleForLoss.
    /// TURNED OFF 2026-08-07: was silently killing all events when possession read as "away".</summary>
    const bool HomeOnlyEventsForNow = false;

    void OnDownChanged(string? down)
    {
        if (_useEngineForEvents) return;
        RunOnUi(() =>
        {
            if (down == null) return;
            string trigger = $"down:{down}";
            if (_homeConfig != null && _awayConfig != null && _possession != null)
            {
                bool sideAllowed = HomeOnlyEventsForNow ? _possession == "home" : true;
                if (sideAllowed) FireTriggerForSide(_possession, trigger);
                return;
            }
            var entry = _config.FirstOrDefault(e => e.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
            if (entry != null) FireEvent(entry);
        });
    }

    static readonly HashSet<string> ValueKeyedRegions = new(StringComparer.OrdinalIgnoreCase) { "situation", "banner", "quarter" };
    static readonly Dictionary<string, string> SideAwareEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["touchdown"] = "Offense: Touchdown Scored",
        ["turnover"] = "Defense: Turnover Forced",
        ["pat_good"] = "Offense: PAT Made",
        ["kickoff"] = "Other: Opening Kickoff",
    };

    void OnRegionChanged(string region, string? value)
    {
        if (_useEngineForEvents) return;
        if (region == "down") return;
        RunOnUi(() =>
        {
            if (region == "situation" && value != null && _homeConfig != null && _awayConfig != null
                && _possession != null && SideAwareEvents.TryGetValue(value, out var eventName))
            {
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

    /// <summary>Right Ctrl "band cutoff" -- global, unconditional AudioPlayer.StopAll(), no config
    /// lookup (unlike OnKeyCombo above). Works even while some other app has focus, same as every
    /// other KeyboardHook combo. Also pokes the web page so the UI can show a quick confirmation
    /// that the cutoff actually landed.</summary>
    void OnCutoff()
    {
        AudioPlayer.StopAll();
        RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:cutoff'))"));
    }

    static readonly string OcrLogPath = Path.Combine(AppContext.BaseDirectory, "ocr_debug.log");
    static readonly object OcrLogLock = new();

    // In-memory buffer for OnLog below -- see its doc comment for why this exists instead of
    // a File.AppendAllText + periodic File.ReadAllLines/WriteAllLines rewrite on every call.
    static readonly List<string> _ocrLogBuffer = new();
    static DateTime _ocrLogLastFlush = DateTime.MinValue;
    const int OcrLogFlushEveryNCalls = 20;
    static readonly TimeSpan OcrLogFlushInterval = TimeSpan.FromSeconds(3);

    /// <summary>Was a no-op -- every OCR region read (down/situation/flag/possession/etc, see
    /// GameWatcher's Log?.Invoke call sites) went nowhere, so there was no way to see what text
    /// actually got read on a tick where a trigger silently failed to fire. Appends to
    /// ocr_debug.log next to the exe, capped to roughly the last ~2000 lines so it can't grow
    /// unbounded over a long game session.
    ///
    /// This fires ~4x/second during a live game (once per OCR tick). Originally did a
    /// File.AppendAllText (open/write/close) on every single call, plus periodically a full
    /// File.ReadAllLines + File.WriteAllLines rewrite of the whole log to enforce the 2MB cap --
    /// both real disk I/O on the OCR hot path. Now buffers lines in memory and only touches disk
    /// every OcrLogFlushEveryNCalls calls (or every OcrLogFlushInterval, whichever comes first),
    /// and the size-cap rewrite only runs at flush time, not on every call.</summary>
    void OnLog(string message)
    {
        lock (OcrLogLock)
        {
            _ocrLogBuffer.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");

            bool dueByCount = _ocrLogBuffer.Count >= OcrLogFlushEveryNCalls;
            bool dueByTime = DateTime.UtcNow - _ocrLogLastFlush >= OcrLogFlushInterval;
            if (!dueByCount && !dueByTime)
                return;

            try
            {
                File.AppendAllText(OcrLogPath, string.Join(Environment.NewLine, _ocrLogBuffer) + Environment.NewLine);
                _ocrLogBuffer.Clear();
                _ocrLogLastFlush = DateTime.UtcNow;

                if (new FileInfo(OcrLogPath).Length > 2_000_000)
                {
                    var lines = File.ReadAllLines(OcrLogPath);
                    File.WriteAllLines(OcrLogPath, lines.Skip(Math.Max(0, lines.Length - 2000)));
                }
            }
            catch (Exception ex) { CrashLog.Write("OCR log write failed", ex); }
        }
    }

    /// <summary>Flushes any buffered OnLog lines to disk immediately, bypassing the count/time
    /// thresholds. Must be called from crash/shutdown paths so the last ~20 lines / ~3 seconds of
    /// OCR reads aren't silently dropped right when they'd matter most for diagnosis.</summary>
    internal static void FlushOcrLog()
    {
        lock (OcrLogLock)
        {
            if (_ocrLogBuffer.Count == 0) return;
            try
            {
                File.AppendAllText(OcrLogPath, string.Join(Environment.NewLine, _ocrLogBuffer) + Environment.NewLine);
                _ocrLogBuffer.Clear();
                _ocrLogLastFlush = DateTime.UtcNow;
            }
            catch (Exception ex) { CrashLog.Write("OCR log flush failed", ex); }
        }
    }

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

    /// <summary>User opts into the one-time default song pack download (see
    /// DefaultSongPackService). Same fire-and-forget-with-progress-events shape as
    /// ShowUpdateDialogFromWeb above, just against a different download.</summary>
    public void DownloadDefaultSongPackFromWeb()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:songpackdownloading'))"));

                bool ok = await DefaultSongPackService.DownloadAndExtractAsync((frac, downloaded, total) =>
                    RunOnUi(() => _ = _webView.ExecuteScriptAsync(
                        $"window.dispatchEvent(new CustomEvent('bandroom:songpackprogress', {{ detail: {{ fraction: {frac.ToString(System.Globalization.CultureInfo.InvariantCulture)}, downloaded: {downloaded}, total: {total} }} }}))")),
                    _lifetimeCts.Token);

                RunOnUi(() => _ = _webView.ExecuteScriptAsync(
                    ok ? "window.dispatchEvent(new CustomEvent('bandroom:songpackready'))"
                       : "window.dispatchEvent(new CustomEvent('bandroom:songpackfailed'))"));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Default song pack download failed", ex);
                RunOnUi(() => _ = _webView.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('bandroom:songpackfailed'))"));
            }
        });
    }

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

        // Filename-based auto-indexing (IntakeEngine): suggest a clean title/team/trigger from
        // the raw filename so the naming dialog starts prefilled instead of the user typing a
        // clean name in from scratch. Purely a suggestion -- never auto-assigns anything; the
        // user still has to accept/edit it and, later, actually assign the track to a trigger.
        var suggestion = IntakeEngine.Process(Path.GetFileName(ofd.FileName));
        var label = suggestion.Team != "Unknown" || suggestion.TriggerSource != "fallback"
            ? $"Enter a name for this track: (suggested: {suggestion.Team} / {suggestion.PrimaryTrigger})"
            : "Enter a name for this track:";

        // ShowTrackNaming (Task: add PA as a content type + crowd-noise checkbox) also collects
        // the Song/PA type choice and, if checked, bakes " w/ Crowd" into the name here -- name
        // already carries the suffix by the time anything downstream (TrimmerForm's disk write,
        // the local-tracks manifest, a later marketplace share) ever sees it.
        var naming = PromptDialog.ShowTrackNaming(this, "Name Your Track", label, suggestion.CleanedTitle);
        if (naming == null)
            return System.Text.Json.JsonSerializer.Serialize(new { success = false, cancelled = true });

        TrimmerForm trimmer;
        try { trimmer = new TrimmerForm(this, ofd.FileName, presetSongName: naming.Name, presetType: naming.Type); }
        catch (Exception ex)
        {
            CrashLog.Write("TrimmerForm open failed", ex);
            MessageBox.Show(this, "Couldn't open this file for trimming -- it may be missing or in an unsupported format.", "Bandroom");
            return System.Text.Json.JsonSerializer.Serialize(new { success = false, cancelled = true });
        }
        using (trimmer)
        {
            if (trimmer.ShowDialog(this) != DialogResult.OK || trimmer.SavedFilePath == null)
                return System.Text.Json.JsonSerializer.Serialize(new { success = false, cancelled = true });

            return System.Text.Json.JsonSerializer.Serialize(new { success = true, path = trimmer.SavedFilePath, name = naming.Name });
        }
    });
}
