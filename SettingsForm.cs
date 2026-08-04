namespace SupremeStadiumSoundSelector;

/// <summary>Everything the new "Stadium.ai" Console design didn't reserve screen space for,
/// but that's still real, working functionality from the original app: audio timing tuning,
/// volume/reverb, always-on-top, stop playback, songs folder shortcut, clear-all, compact
/// mode, and resetting the active team's saved assignments. Consolidated here instead of
/// cluttering the new Console layout.</summary>
internal sealed class SettingsForm : Form
{
    public sealed record Options(
        bool AlwaysOnTop, Action<bool> SetAlwaysOnTop,
        int Volume, Action<int> SetVolume,
        ReverbPreset Reverb, Action<ReverbPreset> SetReverb,
        Action StopPlayback, Action OpenSongsFolder, Action ClearAll,
        bool Compact, Action ToggleCompact,
        Action ResetTeamProfile);

    readonly Options _opts;
    NumericUpDown _numPreRoll = null!, _numFadeStart = null!, _numFadeOut = null!, _numCooldown = null!;
    TrackBar _volumeSlider = null!;
    Label _lblVolume = null!;
    ComboBox _cboReverb = null!;
    CheckBox _chkAlwaysOnTop = null!;

    public SettingsForm(IWin32Window owner, Options opts)
    {
        _opts = opts;

        Text = "Settings";
        Width = 400;
        Height = 620;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.TextPrimary;

        BuildUi();
    }

    void BuildUi()
    {
        int y = 16;

        var lblAudioTiming = new Label { Text = "AUDIO TIMING", Left = 12, Top = y, AutoSize = true, Font = AppFonts.Get(8, FontStyle.Bold), ForeColor = Theme.TextMuted };
        Controls.Add(lblAudioTiming);
        y += 22;

        (Label lbl, NumericUpDown num) Row(string label, decimal value, decimal min, decimal max, decimal increment)
        {
            var lbl = new Label { Text = label, Left = 12, Top = y + 3, Width = 220, Height = 20, ForeColor = Theme.TextSecondary };
            var num = new NumericUpDown
            {
                Left = 250, Top = y, Width = 100, DecimalPlaces = 2, Increment = increment,
                Minimum = min, Maximum = max, Value = value,
                BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary,
            };
            Controls.Add(lbl);
            Controls.Add(num);
            y += 30;
            return (lbl, num);
        }

        (_, _numPreRoll) = Row("Pre-roll delay (sec):", (decimal)AudioPlayer.PreRollSeconds, 0, 10, 0.25m);
        (_, _numFadeStart) = Row("Fade-out starts at (sec):", (decimal)AudioPlayer.FadeStartSeconds, 0, 120, 1m);
        (_, _numFadeOut) = Row("Fade-out duration (sec):", (decimal)AudioPlayer.FadeOutDuration, 0.1m, 20, 0.5m);
        (_, _numCooldown) = Row("Re-fire cooldown (sec):", (decimal)GameWatcher.Cooldown.TotalSeconds, 0, 10, 0.5m);

        y += 8;
        var div1 = new Panel { Left = 12, Top = y, Width = 360, Height = 1, BackColor = Theme.Divider };
        Controls.Add(div1);
        y += 16;

        var lblAudioMix = new Label { Text = "VOLUME & REVERB", Left = 12, Top = y, AutoSize = true, Font = AppFonts.Get(8, FontStyle.Bold), ForeColor = Theme.TextMuted };
        Controls.Add(lblAudioMix);
        y += 22;

        _lblVolume = new Label { Text = $"Volume: {_opts.Volume}%", Left = 12, Top = y, AutoSize = true, ForeColor = Theme.TextSecondary };
        Controls.Add(_lblVolume);
        y += 20;
        _volumeSlider = new TrackBar { Left = 8, Top = y, Width = 350, Minimum = 0, Maximum = 100, Value = _opts.Volume, TickStyle = TickStyle.None, BackColor = Theme.Bg };
        _volumeSlider.ValueChanged += (_, _) => { _lblVolume.Text = $"Volume: {_volumeSlider.Value}%"; _opts.SetVolume(_volumeSlider.Value); };
        Controls.Add(_volumeSlider);
        y += 40;

        var lblReverb = new Label { Text = "Reverb preset:", Left = 12, Top = y + 3, AutoSize = true, ForeColor = Theme.TextSecondary };
        Controls.Add(lblReverb);
        _cboReverb = new ComboBox { Left = 150, Top = y, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.CardBg, ForeColor = Theme.TextPrimary };
        _cboReverb.Items.AddRange(new object[] { "Off", "Stadium", "Dome", "Night Game" });
        _cboReverb.SelectedIndex = (int)_opts.Reverb;
        _cboReverb.SelectedIndexChanged += (_, _) => _opts.SetReverb((ReverbPreset)_cboReverb.SelectedIndex);
        Controls.Add(_cboReverb);
        y += 40;

        var div2 = new Panel { Left = 12, Top = y, Width = 360, Height = 1, BackColor = Theme.Divider };
        Controls.Add(div2);
        y += 16;

        var lblApp = new Label { Text = "APP", Left = 12, Top = y, AutoSize = true, Font = AppFonts.Get(8, FontStyle.Bold), ForeColor = Theme.TextMuted };
        Controls.Add(lblApp);
        y += 22;

        _chkAlwaysOnTop = new CheckBox { Text = "Always on top", Left = 12, Top = y, Width = 200, Checked = _opts.AlwaysOnTop, ForeColor = Theme.TextSecondary };
        _chkAlwaysOnTop.CheckedChanged += (_, _) => _opts.SetAlwaysOnTop(_chkAlwaysOnTop.Checked);
        Controls.Add(_chkAlwaysOnTop);
        y += 32;

        var btnStop = new GlassButton { Text = "Stop Playback", Left = 12, Top = y, Width = 170, Height = 30 };
        btnStop.Click += (_, _) => _opts.StopPlayback();
        Controls.Add(btnStop);

        var btnSongs = new GlassButton { Text = "Open Songs Folder", Left = 194, Top = y, Width = 170, Height = 30 };
        btnSongs.Click += (_, _) => _opts.OpenSongsFolder();
        Controls.Add(btnSongs);
        y += 38;

        var btnClearAll = new GlassButton { Text = "Clear All Assignments", Left = 12, Top = y, Width = 170, Height = 30 };
        btnClearAll.Click += (_, _) => _opts.ClearAll();
        Controls.Add(btnClearAll);

        var btnCompact = new GlassButton { Text = _opts.Compact ? "Exit Compact Mode" : "Compact Mode", Left = 194, Top = y, Width = 170, Height = 30 };
        btnCompact.Click += (_, _) => { _opts.ToggleCompact(); Close(); };
        Controls.Add(btnCompact);
        y += 38;

        var btnResetTeam = new GlassButton { Text = "Reset This Team's Assignments", Left = 12, Top = y, Width = 352, Height = 30 };
        btnResetTeam.Click += (_, _) =>
        {
            var result = MessageBox.Show(this, "Clear every saved assignment for the currently selected team? This can't be undone.",
                "Reset Team", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes) _opts.ResetTeamProfile();
        };
        Controls.Add(btnResetTeam);
        y += 46;

        var btnSave = new GlassButton { Text = "Apply Timing", Left = 12, Top = y, Width = 170, Height = 32 };
        Theme.StyleButton(btnSave, primary: true);
        btnSave.Click += (_, _) => Apply();
        Controls.Add(btnSave);

        var btnClose = new GlassButton { Text = "Close", Left = 194, Top = y, Width = 170, Height = 32 };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);
    }

    void Apply()
    {
        AudioPlayer.PreRollSeconds = (double)_numPreRoll.Value;
        AudioPlayer.FadeStartSeconds = (double)_numFadeStart.Value;
        AudioPlayer.FadeOutDuration = (double)_numFadeOut.Value;
        GameWatcher.Cooldown = TimeSpan.FromSeconds((double)_numCooldown.Value);
        MessageBox.Show(this, "Timing settings applied.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
