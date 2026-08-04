using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>56px app header bar (v4 handoff): left = team-color avatar chip + "Stadium.ai"
/// wordmark (the "." tinted with the active team color); center = "{Team} · Live Session"
/// session/team switcher (opens Quick Assign); right = reverb-label pill, a small Settings
/// gear (not in the original spec -- kept so the still-real timing/volume/compact-mode/clear
/// controls in SettingsForm stay reachable, per the handoff's own "keep Settings accessible
/// from somewhere" allowance), a "Live Feed" outline button, and a solid team-color
/// "Export Card" button. Per the design file's own script, that last button's click handler is
/// wired to the same test-fire action as the transport's play button -- reproduced verbatim.</summary>
internal sealed class TopBar : Panel
{
    public event EventHandler? OpenQuickAssign;
    public event EventHandler? OpenLiveFeed;
    public event EventHandler? TestFire;
    public event EventHandler? OpenSettings;
    public event EventHandler? OcrToggleClicked;

    Label _wordmarkDot = null!;
    Label _sessionLabel = null!;
    Label _reverbPill = null!;
    Label _ocrPill = null!;
    TeamChip _teamChip = null!;
    Label _feedBadge = null!;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FeedHasItems { get; set; }

    public TopBar()
    {
        Height = Theme.HeaderBarHeight;
        Dock = DockStyle.Top;
        BackColor = Theme.ChromeBg;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Divider, 1);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    public void Build()
    {
        Controls.Clear();

        // --- Left: avatar chip + wordmark ---
        _teamChip = new TeamChip { Size = new Size(24, 24), Left = 20, Top = 16 };
        Controls.Add(_teamChip);

        var wmFont = AppFonts.Get(15, FontStyle.Bold);
        var lblStadium = new Label { Text = "Stadium", AutoSize = true, Left = 52, Top = 17, Font = wmFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        Controls.Add(lblStadium);
        int x = 52 + lblStadium.PreferredWidth;
        _wordmarkDot = new Label { Text = ".", AutoSize = true, Left = x - 3, Top = 17, Font = wmFont, ForeColor = Theme.ActiveTeam.Accent, BackColor = Color.Transparent };
        Controls.Add(_wordmarkDot);
        x += _wordmarkDot.PreferredWidth - 3;
        var lblAi = new Label { Text = "ai", AutoSize = true, Left = x, Top = 17, Font = wmFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        Controls.Add(lblAi);

        // --- Center: session/team switcher ---
        var sessionFont = AppFonts.Get(11, FontStyle.Bold);
        _sessionLabel = new Label { Text = $"{Theme.ActiveTeam.Name} · Live Session", AutoSize = true, Top = 20, Font = sessionFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Cursor = Cursors.Hand };
        var chevron = new Label { Text = "▾", AutoSize = true, Top = 21, Font = AppFonts.Get(9), ForeColor = Theme.TextMuted, BackColor = Color.Transparent, Cursor = Cursors.Hand };
        int totalW = _sessionLabel.PreferredWidth + 6 + chevron.PreferredWidth;
        _sessionLabel.Left = (Width - totalW) / 2;
        chevron.Left = _sessionLabel.Left + _sessionLabel.PreferredWidth + 6;
        _sessionLabel.Click += (_, _) => OpenQuickAssign?.Invoke(this, EventArgs.Empty);
        chevron.Click += (_, _) => OpenQuickAssign?.Invoke(this, EventArgs.Empty);
        Controls.Add(_sessionLabel);
        Controls.Add(chevron);

        // --- Right cluster, built right-to-left ---
        int rx = Width - 20;

        var exportBtn = new GlassButton { Text = "⚡ Export Card", Size = new Size(126, 30), Top = 13 };
        Theme.StyleButton(exportBtn, primary: true);
        exportBtn.Click += (_, _) => TestFire?.Invoke(this, EventArgs.Empty);
        rx -= exportBtn.Width; exportBtn.Left = rx; rx -= 10;

        var liveFeedBtn = new GlassButton { Text = "Live Feed", Size = new Size(88, 30), Top = 13 };
        liveFeedBtn.Click += (_, _) => OpenLiveFeed?.Invoke(this, EventArgs.Empty);
        rx -= liveFeedBtn.Width; liveFeedBtn.Left = rx; rx -= 10;

        _feedBadge = new Label { Text = "", Size = new Size(8, 8), Top = 12, BackColor = Theme.CategoryPenalties, Visible = FeedHasItems };
        _feedBadge.Left = liveFeedBtn.Left + liveFeedBtn.Width - 10;

        var gear = new Label { Text = "⚙", Size = new Size(22, 22), Top = 17, Font = AppFonts.Get(11), ForeColor = Theme.TextMuted, BackColor = Color.Transparent, Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleCenter };
        gear.Click += (_, _) => OpenSettings?.Invoke(this, EventArgs.Empty);
        rx -= gear.Width; gear.Left = rx; rx -= 10;

        _reverbPill = new Label
        {
            Text = "Off ▾", AutoSize = false, Height = 28, Top = 14,
            Font = AppFonts.Get(9.5f), ForeColor = Theme.TextMuted2,
            BackColor = Theme.InputFill, TextAlign = ContentAlignment.MiddleCenter,
        };
        _reverbPill.Width = 90;
        rx -= _reverbPill.Width; rx -= 10; _reverbPill.Left = rx;

        // OCR "watching" toggle -- kept as an unmissable, clickable pill (per the earlier
        // discoverability fix: a muted text label was upgraded to a visible pill-button here;
        // the v4 rebuild must not regress that).
        _ocrPill = new Label
        {
            Text = "○  Start Watching", AutoSize = false, Height = 28, Top = 14,
            Font = AppFonts.Get(9.5f, FontStyle.Bold), ForeColor = Theme.TextMuted,
            BackColor = Theme.InputFill, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
        };
        _ocrPill.Width = 132;
        rx -= _ocrPill.Width; _ocrPill.Left = rx;
        _ocrPill.Click += (_, _) => OcrToggleClicked?.Invoke(this, EventArgs.Empty);

        Controls.Add(_reverbPill);
        Controls.Add(_ocrPill);
        Controls.Add(gear);
        Controls.Add(liveFeedBtn);
        Controls.Add(_feedBadge);
        Controls.Add(exportBtn);
    }

    public void SetWatching(bool watching, bool windowFound)
    {
        _ocrPill.Text = watching
            ? (windowFound ? "●  Watching" : "○  Waiting for CFB27…")
            : "○  Start Watching";
        var color = watching && windowFound ? Theme.Success : watching ? Theme.ActiveTeam.Accent : Theme.TextMuted;
        _ocrPill.ForeColor = color;
        _ocrPill.BackColor = Color.FromArgb(40, color.R, color.G, color.B);
    }

    public void RefreshTeam()
    {
        _wordmarkDot.ForeColor = Theme.ActiveTeam.Accent;
        _sessionLabel.Text = $"{Theme.ActiveTeam.Name} · Live Session";
        _teamChip.Invalidate();
        Build();
    }

    public void SetReverbLabel(string label) => _reverbPill.Text = $"{label} ▾";

    public void SetFeedBadge(bool hasItems)
    {
        FeedHasItems = hasItems;
        if (_feedBadge != null) _feedBadge.Visible = hasItems;
    }
}

/// <summary>24x24 rounded-square team-color gradient chip with the team's initial (header
/// wordmark's leading glyph, per the handoff's `teamSwatchBg` + `profileInitial`).</summary>
internal sealed class TeamChip : Panel
{
    public TeamChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent != null) using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);

        var team = Theme.ActiveTeam;
        Color c1 = team.Accent;
        Color c2 = team.Secondary ?? Color.FromArgb(Math.Max(0, c1.R - 60), Math.Max(0, c1.G - 60), Math.Max(0, c1.B - 60));

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 7);
        using var brush = new LinearGradientBrush(rect, c1, c2, 45f);
        g.FillPath(brush, path);

        string initial = string.IsNullOrEmpty(team.Name) ? "?" : team.Name[0].ToString();
        using var font = AppFonts.Get(10, FontStyle.Bold);
        TextRenderer.DrawText(g, initial, font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
