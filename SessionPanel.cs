using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Center column: the canvas hero card (team-color gradient, "N Cues Fired" counter),
/// the 5-button transport row, and the bottom timeline strip (fired-cue chips / track waveform
/// / detail chips). Rebuilt from the old left-column "Session" stat panel (superseded PilePeak
/// layout) into the v4 handoff's center canvas. Cue counts and the selected-event's assignment
/// status are real data from _config; the waveform bars and star-rating "Priority" chip stay
/// decorative, matching the mock and the existing BreakdownPanel precedent.</summary>
internal sealed class SessionPanel : Panel
{
    public event EventHandler? PlayClicked;
    public event EventHandler? PrevTeamClicked;
    public event EventHandler? NextTeamClicked;
    public event EventHandler? OpenLiveFeedClicked;
    public event EventHandler? OpenShortcutsClicked;

    RoundedPanel _canvas = null!;
    Label _lblTeamName = null!, _lblCuesFired = null!, _lblCaption = null!;
    FlowLayoutPanel _cueChipRow = null!;
    Label _lblNoCues = null!;
    Panel _trackBar = null!;
    Label _lblTrackName = null!;
    Label _lblDetailCategory = null!, _lblDetailStatus = null!, _lblDetailPriority = null!;

    readonly List<FeedItem> _fired = new();
    string _reverbLabel = "Off";
    int _volume = 72;
    List<TriggerEntry> _config = new();
    TriggerEntry? _selected;

    public SessionPanel()
    {
        BackColor = Color.Transparent;
        ParentChanged += (_, _) => { if (Parent != null) BackColor = Parent.BackColor; };
    }

    public void Build()
    {
        Controls.Clear();
        if (Width <= 0 || Height <= 0) return;

        const int canvasW = 420, canvasH = 280;
        const int transportH = 70;
        const int timelineH = Theme.TimelineHeight;

        int heroAreaH = Math.Max(canvasH + 20, Height - transportH - timelineH);
        _canvas = new RoundedPanel
        {
            Width = canvasW, Height = canvasH,
            Left = (Width - canvasW) / 2, Top = (heroAreaH - canvasH) / 2,
            Radius = Theme.HeroRadius, BorderColor = Color.Transparent, DropShadow = true,
        };
        Controls.Add(_canvas);
        BuildCanvasContent();

        int transportTop = heroAreaH;
        BuildTransport(transportTop, transportH);

        int timelineTop = transportTop + transportH;
        BuildTimeline(timelineTop, Height - timelineTop);

        RefreshHero();
    }

    void BuildCanvasContent()
    {
        _canvas.Controls.Clear();
        _lblTeamName = new Label
        {
            Text = Theme.ActiveTeam.Name.ToUpperInvariant(), AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Width = _canvas.Width, Height = 20, Top = 90, Left = 0,
            Font = AppFonts.Get(9, FontStyle.Bold), ForeColor = Color.FromArgb(178, 255, 255, 255), BackColor = Color.Transparent,
        };
        _canvas.Controls.Add(_lblTeamName);

        _lblCuesFired = new Label
        {
            Text = "0 Cues Fired", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Width = _canvas.Width, Height = 54, Top = 112, Left = 0,
            Font = AppFonts.Get(30, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent,
        };
        _canvas.Controls.Add(_lblCuesFired);

        _lblCaption = new Label
        {
            Text = "Off reverb · 72% volume", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Width = _canvas.Width, Height = 20, Top = 168, Left = 0,
            Font = AppFonts.Get(9.5f), ForeColor = Color.FromArgb(178, 255, 255, 255), BackColor = Color.Transparent,
        };
        _canvas.Controls.Add(_lblCaption);
    }

    void BuildTransport(int top, int height)
    {
        int centerX = Width / 2;
        var buttons = new[]
        {
            ("⟲", -110, 38, (EventHandler)((_, _) => OpenLiveFeedClicked?.Invoke(this, EventArgs.Empty))),
            ("⏮", -58, 38, (EventHandler)((_, _) => PrevTeamClicked?.Invoke(this, EventArgs.Empty))),
            ("▶", 0, 52, (EventHandler)((_, _) => PlayClicked?.Invoke(this, EventArgs.Empty))),
            ("⏭", 58, 38, (EventHandler)((_, _) => NextTeamClicked?.Invoke(this, EventArgs.Empty))),
            ("⟳", 110, 38, (EventHandler)((_, _) => OpenShortcutsClicked?.Invoke(this, EventArgs.Empty))),
        };

        foreach (var (glyph, offset, size, handler) in buttons)
        {
            bool isPlay = size == 52;
            var btn = new TransportButton(isPlay)
            {
                Size = new Size(size, size),
                Left = centerX + offset - size / 2,
                Top = top + (height - size) / 2,
                Text = glyph,
            };
            btn.Click += handler;
            Controls.Add(btn);
        }
    }

    void BuildTimeline(int top, int height)
    {
        var strip = new Panel { Left = 0, Top = top, Width = Width, Height = Math.Max(120, height), BackColor = Theme.ChromeBg, Padding = new Padding(18, 12, 18, 12) };
        Controls.Add(strip);

        int y = 4;

        var lblCues = new Label { Text = "Cues", AutoSize = true, Left = 18, Top = top + y, Font = AppFonts.Get(8.5f), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        Controls.Add(lblCues);

        _cueChipRow = new FlowLayoutPanel
        {
            Left = 80, Top = top + y - 2, Width = Width - 100, Height = 26,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true,
            BackColor = Color.Transparent,
        };
        Controls.Add(_cueChipRow);
        _lblNoCues = new Label { Text = "No cues fired this session yet.", AutoSize = true, ForeColor = Color.FromArgb(120, Theme.TextMuted.R, Theme.TextMuted.G, Theme.TextMuted.B), Font = AppFonts.Get(8.5f), BackColor = Color.Transparent };
        y += 36;

        var lblTrack = new Label { Text = "Track", AutoSize = true, Left = 18, Top = top + y, Font = AppFonts.Get(8.5f), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        Controls.Add(lblTrack);

        _trackBar = new WaveformBar { Left = 80, Top = top + y - 3, Width = Width - 100, Height = 26 };
        Controls.Add(_trackBar);
        _lblTrackName = new Label { Text = "Unassigned", AutoSize = true, Font = AppFonts.Get(9), ForeColor = Color.White, BackColor = Color.Transparent };
        _trackBar.Controls.Add(_lblTrackName);
        y += 36;

        int chipY = top + y;
        int chipW = (Width - 36 - 16) / 3;
        _lblDetailCategory = BuildDetailChip("Category", 18, chipY, chipW, Theme.TextPrimary);
        _lblDetailStatus = BuildDetailChip("Status", 18 + chipW + 8, chipY, chipW, Theme.TextMuted);
        _lblDetailPriority = BuildDetailChip("Priority", 18 + (chipW + 8) * 2, chipY, chipW, Theme.ActiveTeam.Accent);
    }

    Label BuildDetailChip(string label, int x, int y, int w, Color valueColor)
    {
        var chip = new RoundedPanel { Left = x, Top = y, Width = w, Height = 44, FillColor = Theme.TileFillSmall, BorderColor = Theme.PanelBorder, Radius = 8 };
        Controls.Add(chip);
        var lblLabel = new Label { Text = label, AutoSize = true, Left = 10, Top = 6, Font = AppFonts.Get(7.5f), ForeColor = Theme.TextMuted2, BackColor = Color.Transparent };
        chip.Controls.Add(lblLabel);
        var lblValue = new Label { Text = "—", AutoSize = true, Left = 10, Top = 22, Font = AppFonts.Get(9.5f, FontStyle.Bold), ForeColor = valueColor, BackColor = Color.Transparent };
        chip.Controls.Add(lblValue);
        return lblValue;
    }

    public void SetConfig(List<TriggerEntry> config)
    {
        _config = config;
        _selected ??= _config.FirstOrDefault();
        RefreshSelectedDetails();
    }

    public void OnCueFired(TriggerEntry entry, string category)
    {
        _fired.Insert(0, new FeedItem(entry.Event, category, DateTime.Now.ToString("h:mm tt"), $"Fired via {Theme.ActiveTeam.Name}"));
        if (_fired.Count > 24) _fired.RemoveAt(_fired.Count - 1);
        _selected = entry;
        RefreshHero();
        RefreshCueChips();
        RefreshSelectedDetails();
    }

    void RefreshHero()
    {
        if (_lblCuesFired == null) return;
        _lblCuesFired.Text = $"{_fired.Count} Cues Fired";
        _lblCaption.Text = $"{_reverbLabel} reverb · {_volume}% volume";
        _lblTeamName.Text = Theme.ActiveTeam.Name.ToUpperInvariant();
    }

    void RefreshCueChips()
    {
        if (_cueChipRow == null) return;
        _cueChipRow.Controls.Clear();
        if (_fired.Count == 0)
        {
            _cueChipRow.Controls.Add(_lblNoCues);
            return;
        }
        foreach (var item in _fired)
        {
            Color tint = Theme.CategoryColor(item.Category);
            var chip = new Panel
            {
                Size = new Size(20, 20), Margin = new Padding(0, 2, 4, 2),
                BackColor = Color.FromArgb(128, tint.R, tint.G, tint.B),
            };
            var tip = new ToolTip();
            tip.SetToolTip(chip, item.Name);
            _cueChipRow.Controls.Add(chip);
        }
    }

    void RefreshSelectedDetails()
    {
        if (_lblTrackName == null) return;
        var entry = _selected;
        if (entry == null) { entry = _config.FirstOrDefault(e => e.Event.Contains("Touchdown", StringComparison.OrdinalIgnoreCase)) ?? _config.FirstOrDefault(); }
        if (entry == null) return;

        bool assigned = !string.IsNullOrWhiteSpace(entry.AudioFile);
        string trackLabel = assigned ? Path.GetFileNameWithoutExtension(entry.AudioFile) : "Unassigned";
        _lblTrackName.Text = trackLabel;

        string category = CategoryMap.Resolve(entry);
        _lblDetailCategory.Text = category;
        _lblDetailCategory.ForeColor = Theme.CategoryColor(category);
        _lblDetailStatus.Text = assigned ? "Assigned" : "Unassigned";
        _lblDetailStatus.ForeColor = assigned ? Theme.Success : Theme.TextMuted2;

        int hash = 0;
        foreach (char c in entry.Event) hash = (hash * 31 + c) & int.MaxValue;
        int filled = 1 + (hash % 5);
        _lblDetailPriority.Text = new string('★', filled) + new string('☆', 5 - filled);
        _lblDetailPriority.ForeColor = Theme.ActiveTeam.Accent;
    }

    public void SetReverb(string label) { _reverbLabel = label; RefreshHero(); }
    public void SetVolume(int volume) { _volume = volume; RefreshHero(); }

    public void RefreshTeamColors()
    {
        RefreshHero();
        BuildCanvasContent();
        Invalidate(true);
    }
}

/// <summary>Circular transport-style icon button (loop-left / skip-back / big play / skip-
/// forward / loop-right). Reuses the design's team-color-filled play + translucent-outline
/// secondary style.</summary>
internal sealed class TransportButton : Panel
{
    readonly bool _primary;
    bool _hover;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public new string Text { get; set; } = "";

    public TransportButton(bool primary)
    {
        _primary = primary;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent != null) using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_primary)
        {
            Color tint = Theme.ActiveTeam.Accent;
            using var fill = new SolidBrush(tint);
            g.FillEllipse(fill, rect);
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(_hover ? 24 : 15, 255, 255, 255));
            g.FillEllipse(fill, rect);
            using var border = new Pen(Color.FromArgb(26, 255, 255, 255), 1);
            g.DrawEllipse(border, rect);
        }

        Color glyphColor = _primary ? Color.White : Color.FromArgb(201, 211, 220);
        using var font = AppFonts.Get(_primary ? 14 : 12, FontStyle.Regular);
        TextRenderer.DrawText(g, Text, font, rect, glyphColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Static illustrative waveform bar for the timeline's "Track" row, plus the
/// currently-selected event's assigned track name. The bars themselves are decorative (no
/// real audio analysis happens here), matching the mock's own static waveform.</summary>
internal sealed class WaveformBar : Panel
{
    public WaveformBar()
    {
        BackColor = Theme.TileFillSmall;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Parent != null) using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);
        using var fill = new SolidBrush(Theme.TileFillSmall);
        g.FillRectangle(fill, ClientRectangle);

        using var barBrush = new SolidBrush(Color.FromArgb(140, 255, 255, 255));
        int barCount = 30;
        int x = 10;
        for (int i = 0; i < barCount; i++)
        {
            int h = 4 + ((i * 53) % (Height - 8));
            g.FillRectangle(barBrush, x, (Height - h) / 2, 2, h);
            x += 4;
            if (x > Width - 140) break;
        }

        if (Controls.Count > 0 && Controls[0] is Label lbl)
            lbl.Location = new Point(x + 10, (Height - lbl.PreferredHeight) / 2);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
}
