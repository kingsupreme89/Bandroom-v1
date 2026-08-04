using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

public class MainForm : Form
{
    static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac" };

    List<TriggerEntry> _config = new();
    readonly KeyboardHook _hook = new();
    readonly GameWatcher _watcher = new();

    ChromeBar _chromeBar = null!;
    TopBar _headerBar = null!;
    Panel _body = null!;
    IconRail _leftRail = null!;
    IconRail _rightRail = null!;
    LeftPanel _leftPanel = null!;
    AdjustPanel _adjustPanel = null!;
    Panel _centerColumn = null!;
    SessionPanel _centerCanvas = null!;
    LiveFeedPanel _liveFeedPanel = null!;
    TeamWipeOverlay _wipeOverlay = null!;
    ConfettiOverlay _confetti = null!;
    ToastManager _toasts = null!;
    NotifyIcon _trayIcon = null!;

    bool _watching;
    bool _windowFound;
    bool _compact;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED -- avoids flicker from the layered overlays
            return cp;
        }
    }

    public MainForm()
    {
        Text = "Bandroom";
        Width = 1920;
        Height = 1080;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 650);
        BackColor = Theme.WindowBg;
        KeyPreview = true;

        BuildUi();

        _config = ConfigStore.LoadOrCreate();
        RefreshAll();

        _hook.KeyCombo += OnKeyCombo;
        _watcher.WindowFoundChanged += OnWindowFoundChanged;
        _watcher.DownChanged += OnDownChanged;
        _watcher.RegionChanged += OnRegionChanged;
        _watcher.Log += OnLog;

        KeyDown += MainForm_KeyDown;
        FormClosing += (_, _) => { _hook.Stop(); _watcher.Stop(); };
    }

    void BuildUi()
    {
        _chromeBar = new ChromeBar();
        Controls.Add(_chromeBar);

        _headerBar = new TopBar();
        Controls.Add(_headerBar);
        _headerBar.Build();
        _headerBar.OpenQuickAssign += (_, _) => OpenQuickAssignModal();
        _headerBar.OpenLiveFeed += (_, _) => ToggleLiveFeed();
        _headerBar.TestFire += (_, _) => FireTestCue();
        _headerBar.OpenSettings += (_, _) => OpenSettingsDialog();
        _headerBar.OcrToggleClicked += (_, _) => ToggleWatching();

        _body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.WindowBg };
        Controls.Add(_body);

        _leftRail = new IconRail();
        _body.Controls.Add(_leftRail);

        _leftPanel = new LeftPanel();
        _body.Controls.Add(_leftPanel);
        _leftPanel.TeamSelected += SwitchTeam;
        _leftPanel.CategoryClicked += cat => OpenQuickAssignModal(cat);
        _leftPanel.Build();

        _rightRail = new IconRail();
        _body.Controls.Add(_rightRail);

        _adjustPanel = new AdjustPanel();
        _body.Controls.Add(_adjustPanel);
        _adjustPanel.Build();
        _adjustPanel.VolumeChanged += v => { AudioPlayer.MasterVolume = v / 100f; _centerCanvas.SetVolume(v); };
        _adjustPanel.SensitivityChanged += _ => { /* decorative -- no OCR-confidence threshold exists to back this */ };
        _adjustPanel.ReverbSelected += preset => { AudioPlayer.CurrentReverb = preset; _headerBar.SetReverbLabel(ReverbDisplayName(preset)); _centerCanvas.SetReverb(ReverbDisplayName(preset)); };
        _adjustPanel.ResetAll += ResetTeamProfile;

        _centerColumn = new Panel { Dock = DockStyle.Fill, BackColor = Theme.WindowBg };
        _body.Controls.Add(_centerColumn);
        _centerColumn.Resize += (_, _) => LayoutCenter();

        _centerCanvas = new SessionPanel();
        _centerColumn.Controls.Add(_centerCanvas);
        _centerCanvas.PlayClicked += (_, _) => FireTestCue();
        _centerCanvas.PrevTeamClicked += (_, _) => StepTeam(-1);
        _centerCanvas.NextTeamClicked += (_, _) => StepTeam(1);
        _centerCanvas.OpenLiveFeedClicked += (_, _) => ToggleLiveFeed();
        _centerCanvas.OpenShortcutsClicked += (_, _) => new ShortcutsForm(this).ShowDialog(this);

        SetupRails();

        _liveFeedPanel = new LiveFeedPanel();
        Controls.Add(_liveFeedPanel);
        _liveFeedPanel.Build();

        _wipeOverlay = new TeamWipeOverlay { Dock = DockStyle.Fill };
        _body.Controls.Add(_wipeOverlay);
        _wipeOverlay.BringToFront();

        _confetti = new ConfettiOverlay { Dock = DockStyle.Fill };
        Controls.Add(_confetti);
        _confetti.BringToFront();

        _toasts = new ToastManager(this);

        SetupTrayIcon();
        Resize += (_, _) => PositionOverlays();
        PositionOverlays();
    }

    void SetupRails()
    {
        _leftRail.SetItems(new[]
        {
            new RailItem("teams", "⛱", "Teams", () => { }),
            new RailItem("categories", "☰", "Categories", () => { }),
            new RailItem("feed", "♪", "Feed", ToggleLiveFeed),
            new RailItem("assign", "✎", "Assign", () => OpenQuickAssignModal()),
            new RailItem("reverb", "◒", "Reverb", () => { }),
            new RailItem("help", "?", "Help", () => new ShortcutsForm(this).ShowDialog(this)),
        }, "teams");

        _rightRail.SetItems(new[]
        {
            new RailItem("filters", "◒", "Filters", () => { }),
            new RailItem("adjust", "⚙", "Adjust", () => { }),
            new RailItem("effects", "✦", "Effects", () => _confetti.Burst()),
            new RailItem("help", "?", "Help", () => new ShortcutsForm(this).ShowDialog(this)),
            new RailItem("assign", "✎", "Assign", () => OpenQuickAssignModal()),
            new RailItem("feed", "♪", "Feed", ToggleLiveFeed),
        }, "adjust");
    }

    void LayoutCenter()
    {
        if (_centerColumn.Width <= 0 || _centerColumn.Height <= 0) return;
        _centerCanvas.Bounds = new Rectangle(0, 0, _centerColumn.Width, _centerColumn.Height);
        _centerCanvas.Build();
    }

    void PositionOverlays()
    {
        _liveFeedPanel.Height = 400;
        _liveFeedPanel.Location = new Point(Width - _liveFeedPanel.Width - Theme.RailWidth - 24, Theme.ChromeBarHeight + Theme.HeaderBarHeight + 24);
        _liveFeedPanel.BringToFront();
    }

    void RefreshAll()
    {
        _leftPanel.RefreshFromConfig(_config);
        _centerCanvas.SetConfig(_config);
        _headerBar.SetReverbLabel(ReverbDisplayName(AudioPlayer.CurrentReverb));
        _centerCanvas.SetReverb(ReverbDisplayName(AudioPlayer.CurrentReverb));
        _headerBar.SetWatching(_watching, _windowFound);
        LayoutCenter();
    }

    static string ReverbDisplayName(ReverbPreset preset) => preset switch
    {
        ReverbPreset.Stadium => "Stadium",
        ReverbPreset.Dome => "Dome",
        ReverbPreset.NightGame => "Night Game",
        _ => "Off",
    };

    // --- Team switching (unified with per-team saved profiles) ---

    void StepTeam(int direction)
    {
        int idx = Array.FindIndex(TeamColors.All, t => t.Name == Theme.ActiveTeam.Name);
        int next = ((idx + direction) % TeamColors.All.Length + TeamColors.All.Length) % TeamColors.All.Length;
        SwitchTeam(TeamColors.All[next]);
    }

    void SwitchTeam(TeamColor team)
    {
        if (team.Name == Theme.ActiveTeam.Name) return;
        SaveCurrentTeamProfile();

        Color c1 = team.Accent;
        Color c2 = team.Secondary ?? c1;

        Theme.ActiveTeam = team;
        _config = ConfigStore.ListProfiles().Contains(team.Name, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(team.Name)
            : ConfigStore.BuildDefault();
        ConfigStore.Save(_config);

        _headerBar.RefreshTeam();
        _leftPanel.RefreshTeamColors();
        _adjustPanel.RefreshTeamColors();
        _centerCanvas.RefreshTeamColors();
        RefreshAll();
        Invalidate(true);
        _wipeOverlay.Play(c1, c2);
        _toasts.Show($"Switched to {team.Name}");
    }

    void SaveCurrentTeamProfile() => ConfigStore.SaveProfile(Theme.ActiveTeam.Name, _config);

    void ResetTeamProfile()
    {
        _config = ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        RefreshAll();
        _toasts.Show($"Reset assignments for {Theme.ActiveTeam.Name}");
    }

    void ClearAll()
    {
        var result = MessageBox.Show(this, "Clear every song assignment for this team? This can't be undone (unless you reload without saving).",
            "Clear All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        foreach (var entry in _config) entry.AudioFile = "";
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        RefreshAll();
        _toasts.Show("Cleared all assignments");
    }

    // --- Modals ---

    void OpenQuickAssignModal(string? categoryFilter = null)
    {
        using var dlg = new QuickAssignForm(this, _config, categoryFilter);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.EventToAssign != null)
        {
            OpenAssignTrack(dlg.EventToAssign);
        }
        else if (dlg.SelectedTeam.Name != Theme.ActiveTeam.Name)
        {
            SwitchTeam(dlg.SelectedTeam);
        }
    }

    void OpenAssignTrack(TriggerEntry entry)
    {
        var library = new List<string>();
        if (Directory.Exists(ConfigStore.SongsFolder))
            library.AddRange(Directory.GetFiles(ConfigStore.SongsFolder)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
        library.AddRange(_config
            .Where(e => !string.IsNullOrWhiteSpace(e.AudioFile) && File.Exists(e.AudioFile))
            .Select(e => e.AudioFile));

        using var dlg = new AssignTrackForm(this, entry, library);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (dlg.RequestTrim)
        {
            if (string.IsNullOrWhiteSpace(entry.AudioFile) || !File.Exists(entry.AudioFile))
            {
                MessageBox.Show(this, "Choose a song for this situation first, then you can trim it.", "Bandroom");
                return;
            }
            using var trimmer = new TrimmerForm(this, entry.AudioFile);
            if (trimmer.ShowDialog(this) == DialogResult.OK && trimmer.SavedFilePath != null)
            {
                entry.AudioFile = trimmer.SavedFilePath;
                _toasts.Show($"Trimmed and assigned: {Path.GetFileNameWithoutExtension(entry.AudioFile)}");
            }
            else return;
        }
        else if (dlg.RequestClear)
        {
            entry.AudioFile = "";
            _toasts.Show($"Cleared assignment for {entry.Event}");
        }
        else if (dlg.AssignedPath != null)
        {
            entry.AudioFile = dlg.AssignedPath;
            _toasts.Show($"Assigned \"{Path.GetFileNameWithoutExtension(entry.AudioFile)}\" to {entry.Event}");
        }
        else return;

        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
        RefreshAll();
    }

    void OpenSettingsDialog()
    {
        var opts = new SettingsForm.Options(
            AlwaysOnTop: TopMost,
            SetAlwaysOnTop: v => TopMost = v,
            Volume: (int)(AudioPlayer.MasterVolume * 100),
            SetVolume: v => { AudioPlayer.MasterVolume = v / 100f; _adjustPanel.SetVolume(v); _centerCanvas.SetVolume(v); },
            Reverb: AudioPlayer.CurrentReverb,
            SetReverb: r => { AudioPlayer.CurrentReverb = r; _headerBar.SetReverbLabel(ReverbDisplayName(r)); _centerCanvas.SetReverb(ReverbDisplayName(r)); _adjustPanel.SetReverb(r); },
            StopPlayback: () => { AudioPlayer.StopAll(); _toasts.Show("Stopped all playback"); },
            OpenSongsFolder: () => { Directory.CreateDirectory(ConfigStore.SongsFolder); System.Diagnostics.Process.Start("explorer.exe", ConfigStore.SongsFolder); },
            ClearAll: ClearAll,
            Compact: _compact,
            ToggleCompact: ToggleCompact,
            ResetTeamProfile: ResetTeamProfile
        );
        new SettingsForm(this, opts).ShowDialog(this);
    }

    void ToggleLiveFeed()
    {
        _liveFeedPanel.Visible = !_liveFeedPanel.Visible;
        if (_liveFeedPanel.Visible) { PositionOverlays(); _liveFeedPanel.BringToFront(); }
    }

    void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.K) { OpenQuickAssignModal(); e.Handled = true; }
        else if (e.KeyCode == Keys.Escape) { _liveFeedPanel.Visible = false; }
    }

    // --- Firing ---

    void FireTestCue()
    {
        // No dedicated "selected event" UI in the v4 canvas -- test-fire whatever the timeline's
        // detail chips are currently showing (most-recently-fired, else a representative default).
        var entry = _config.FirstOrDefault(e => e.Event.Contains("Touchdown", StringComparison.OrdinalIgnoreCase))
            ?? _config.FirstOrDefault();
        if (entry != null) FireEvent(entry);
    }

    void FireEvent(TriggerEntry entry)
    {
        string category = CategoryMap.Resolve(entry);
        _centerCanvas.OnCueFired(entry, category);
        _liveFeedPanel.AddFired(entry.Event, category);
        _headerBar.SetFeedBadge(_liveFeedPanel.HasItems);
        _toasts.Show($"Cue fired: {entry.Event}");
        if (category == "Scoring") _confetti.Burst();

        if (!string.IsNullOrWhiteSpace(entry.AudioFile) && File.Exists(entry.AudioFile))
            AudioPlayer.Play(entry.AudioFile);
    }

    void ToggleWatching()
    {
        if (_watching)
        {
            _hook.Stop();
            _watcher.Stop();
            _watching = false;
            _windowFound = false;
        }
        else
        {
            _hook.Start();
            _watcher.Start();
            _watching = true;
        }
        _headerBar.SetWatching(_watching, _windowFound);
    }

    void OnWindowFoundChanged(bool found)
    {
        RunOnUi(() => { _windowFound = found; _headerBar.SetWatching(_watching, _windowFound); });
    }

    void OnDownChanged(string? down)
    {
        RunOnUi(() =>
        {
            if (down == null) return;
            var entry = _config.FirstOrDefault(e => e.Trigger.Equals($"down:{down}", StringComparison.OrdinalIgnoreCase));
            if (entry != null) FireEvent(entry);
        });
    }

    void OnRegionChanged(string region, string? value)
    {
        if (region == "down") return; // already handled by OnDownChanged
        RunOnUi(() =>
        {
            var entry = _config.FirstOrDefault(e => e.Trigger.Equals($"{region}:on", StringComparison.OrdinalIgnoreCase));
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

    void OnLog(string message)
    {
        RunOnUi(() => { if (message.StartsWith("Error", StringComparison.OrdinalIgnoreCase)) _toasts.Show(message); });
    }

    // --- Compact mode / tray (existing behavior, adapted to the new shell) ---

    void ToggleCompact()
    {
        _compact = !_compact;
        _body.Visible = !_compact;
        Height = _compact ? 220 : 1080;
        MinimumSize = _compact ? new Size(700, 220) : new Size(1200, 650);
    }

    void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Bandroom",
            Visible = false,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => { _trayIcon.Visible = false; Close(); });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                _trayIcon.Visible = true;
            }
        };
        FormClosing += (_, _) => _trayIcon.Dispose();
    }

    void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        _trayIcon.Visible = false;
    }

    void RunOnUi(Action action)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(action);
        else if (IsHandleCreated) action();
    }
}
