using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>230px right ("Adjust") panel: Volume + Fire Sensitivity sliders, and a 3-tile
/// Reverb picker (Stadium / Dome / Night Game). Volume and Reverb are wired to the real
/// AudioPlayer state; Fire Sensitivity is decorative (no OCR-confidence threshold exists in
/// the app to back it), matching the mock's own "decorative Fire Sensitivity" allowance.</summary>
internal sealed class AdjustPanel : Panel
{
    public event Action<int>? VolumeChanged;
    public event Action<int>? SensitivityChanged;
    public event Action<ReverbPreset>? ReverbSelected;
    public event Action? ResetAll;

    TrackBar _volumeSlider = null!, _sensitivitySlider = null!;
    Label _lblVolumeVal = null!, _lblSensitivityVal = null!;
    readonly Dictionary<ReverbPreset, ReverbTile> _reverbTiles = new();
    int _sensitivity = 50;
    ReverbPreset _reverb = ReverbPreset.Off;

    public AdjustPanel()
    {
        Width = Theme.SidePanelWidth;
        Dock = DockStyle.Right;
        BackColor = Theme.PanelBg;
        Padding = new Padding(16);
    }

    public void Build()
    {
        Controls.Clear();
        int innerWidth = Width - 32;
        int y = 0;

        var lblAdjust = new Label { Text = "Adjust", AutoSize = true, Left = 0, Top = y, Font = AppFonts.Get(12.5f, FontStyle.Bold), ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        Controls.Add(lblAdjust);

        var lblReset = new Label { Text = "Reset all", AutoSize = true, Top = y + 2, Font = AppFonts.Get(9), ForeColor = Theme.TextMuted, BackColor = Color.Transparent, Cursor = Cursors.Hand };
        lblReset.Left = innerWidth - lblReset.PreferredWidth;
        lblReset.Click += (_, _) => ResetAll?.Invoke();
        Controls.Add(lblReset);
        y += 34;

        var lblSession = new Label { Text = "SESSION", AutoSize = true, Left = 0, Top = y, Font = AppFonts.Get(8, FontStyle.Bold), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        Controls.Add(lblSession);
        y += 20;

        (_lblVolumeVal, _volumeSlider) = BuildSlider("Volume", 72, innerWidth, ref y);
        _volumeSlider.ValueChanged += (_, _) => { _lblVolumeVal.Text = _volumeSlider.Value.ToString(); VolumeChanged?.Invoke(_volumeSlider.Value); };

        (_lblSensitivityVal, _sensitivitySlider) = BuildSlider("Fire Sensitivity", _sensitivity, innerWidth, ref y);
        _sensitivitySlider.ValueChanged += (_, _) => { _sensitivity = _sensitivitySlider.Value; _lblSensitivityVal.Text = _sensitivity.ToString(); SensitivityChanged?.Invoke(_sensitivity); };

        y += 4;
        var lblReverb = new Label { Text = "REVERB", AutoSize = true, Left = 0, Top = y, Font = AppFonts.Get(8, FontStyle.Bold), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        Controls.Add(lblReverb);
        y += 20;

        int gap = 8;
        int tileW = (innerWidth - gap * 2) / 3;
        var presets = new[] { ReverbPreset.Stadium, ReverbPreset.Dome, ReverbPreset.NightGame };
        for (int i = 0; i < presets.Length; i++)
        {
            var preset = presets[i];
            var tile = new ReverbTile(preset) { Left = i * (tileW + gap), Top = y, Width = tileW, Height = 54 };
            tile.Click += (_, _) => SelectReverb(preset);
            Controls.Add(tile);
            _reverbTiles[preset] = tile;
        }
        RestyleReverbTiles();
    }

    (Label, TrackBar) BuildSlider(string label, int value, int innerWidth, ref int y)
    {
        var lblLabel = new Label { Text = label, AutoSize = true, Left = 0, Top = y, Font = AppFonts.Get(9.5f), ForeColor = Theme.TextMuted2, BackColor = Color.Transparent };
        Controls.Add(lblLabel);
        var lblVal = new Label { Text = value.ToString(), AutoSize = true, Top = y, Font = AppFonts.Get(9.5f), ForeColor = Theme.TextMuted2, BackColor = Color.Transparent };
        lblVal.Left = innerWidth - lblVal.PreferredWidth;
        Controls.Add(lblVal);
        y += 18;

        var slider = new TrackBar { Left = -4, Top = y - 4, Width = innerWidth + 8, Height = 30, Minimum = 0, Maximum = 100, Value = value, TickStyle = TickStyle.None, BackColor = Theme.PanelBg };
        Controls.Add(slider);
        y += 32;
        return (lblVal, slider);
    }

    void SelectReverb(ReverbPreset preset)
    {
        _reverb = preset;
        RestyleReverbTiles();
        ReverbSelected?.Invoke(preset);
    }

    void RestyleReverbTiles()
    {
        foreach (var kv in _reverbTiles) kv.Value.Selected = kv.Key == _reverb;
        foreach (var kv in _reverbTiles) kv.Value.Invalidate();
    }

    public void SetVolume(int v) { _volumeSlider.Value = Math.Clamp(v, 0, 100); _lblVolumeVal.Text = _volumeSlider.Value.ToString(); }
    public void SetReverb(ReverbPreset preset) { _reverb = preset; RestyleReverbTiles(); }
    public void RefreshTeamColors() => Invalidate(true);
}

internal sealed class ReverbTile : Panel
{
    readonly ReverbPreset _preset;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    public ReverbTile(ReverbPreset preset)
    {
        _preset = preset;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    static string Label(ReverbPreset p) => p switch
    {
        ReverbPreset.Stadium => "Stadium",
        ReverbPreset.Dome => "Dome",
        ReverbPreset.NightGame => "Night Game",
        _ => "Off",
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent != null) using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 9);
        Color tint = Theme.ActiveTeam.Accent;

        if (Selected)
        {
            using var fill = new SolidBrush(Color.FromArgb(41, tint.R, tint.G, tint.B));
            g.FillPath(fill, path);
            using var border = new Pen(Color.FromArgb(128, tint.R, tint.G, tint.B), 1);
            g.DrawPath(border, path);
        }
        else
        {
            using var fill = new SolidBrush(Theme.TileFillSmall);
            g.FillPath(fill, path);
            using var border = new Pen(Theme.PanelBorder, 1);
            g.DrawPath(border, path);
        }

        var swatchRect = new Rectangle(4, 4, Width - 8, 30);
        using var swatchPath = RoundedRect(swatchRect, 6);
        if (Selected)
        {
            using var swatchBrush = new LinearGradientBrush(swatchRect, tint, Theme.ActiveTeam.Secondary ?? tint, 135f);
            g.FillPath(swatchBrush, swatchPath);
        }
        else
        {
            using var swatchBrush = new SolidBrush(Color.FromArgb(20, 255, 255, 255));
            g.FillPath(swatchBrush, swatchPath);
        }

        using var font = AppFonts.Get(7.5f);
        TextRenderer.DrawText(g, Label(_preset), font, new Rectangle(0, 38, Width, 14), Theme.TextMuted2, TextFormatFlags.HorizontalCenter);
    }

    static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0 || d > bounds.Width || d > bounds.Height) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
