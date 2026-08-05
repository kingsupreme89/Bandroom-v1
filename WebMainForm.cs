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
        PlayDraftChime();
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
            ResetTeamProfile: ResetTeamProfileFromWeb
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

    /// <summary>The shared "draft chime" (Assets\nfl-draft-chime.mp3) used for every moment
    /// that should grab attention: app open, GAMETIME pressed, and a new update detected --
    /// including while the app is already running unattended on someone else's machine, which
    /// is the normal case (it's always left open on one computer), so the update path fires
    /// this too, not just at launch.</summary>
    static void PlayDraftChime()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "nfl-draft-chime.mp3");
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
            AudioPlayer.Play(entry.AudioFile, volumeOverride, interruptPrevious: true);
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
    /// already know who's on offense, we know their opponent's Defense caused it.</summary>
    void OnTackleForLoss()
    {
        RunOnUi(() =>
        {
            if (_homeConfig == null || _awayConfig == null || _possession == null) return;
            string defenseSide = _possession == "home" ? "away" : "home";
            FireEventForSide(defenseSide, "Defense: Tackle for Loss");
        });
    }

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
                FireTriggerForSide(_possession, trigger);
                return;
            }

            var entry = _config.FirstOrDefault(e => e.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
            if (entry != null) FireEvent(entry);
        });
    }

    // Regions whose matched value IS the trigger key ("situation:kickoff", "banner:touchdown")
    // rather than a fixed on/off toggle ("flag:on") -- see GameWatcher.NormalizeMatch.
    static readonly HashSet<string> ValueKeyedRegions = new(StringComparer.OrdinalIgnoreCase) { "situation", "banner" };

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
                FireEventForSide(_possession, eventName);
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

                try { await Task.Delay(TimeSpan.FromMinutes(10), _lifetimeCts.Token); }
                catch (TaskCanceledException) { break; }
            }
        });
    }

    public void ShowUpdateDialogFromWeb()
    {
        // Used to silently do nothing if the background "is an update available" check
        // hadn't completed/succeeded yet (VPN hiccup, slow network, etc) -- clicking the
        // button looked broken with zero feedback. Now it always tries, and always tells
        // the user something either way.
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

                await mgr.UpdateApp();
                RunOnUi(() => UpdateManager.RestartApp());
            }
            catch (Exception ex)
            {
                CrashLog.Write("Auto-update install failed", ex);
                RunOnUi(() => MessageBox.Show(this,
                    "Update check/download failed -- check your internet connection (and VPN, if you use one) and try again.",
                    "Bandroom Update", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }
        });
    }

    void RunOnUi(Action action)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(action);
        else if (IsHandleCreated) action();
    }
}
