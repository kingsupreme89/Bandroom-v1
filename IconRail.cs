using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

internal sealed record RailItem(string Key, string Glyph, string Label, Action Action);

/// <summary>64px icon rail used on both sides of the body: 6 stacked icon buttons with tiny
/// labels underneath. The active item gets a rounded highlight tinted with the active team
/// color; icon color switches from muted gray to the team color when active.</summary>
internal sealed class IconRail : Panel
{
    readonly List<RailItem> _items = new();
    readonly List<RailButton> _buttons = new();
    string _active = "";

    public IconRail()
    {
        Width = Theme.RailWidth;
        Dock = DockStyle.Left;
        BackColor = Theme.ChromeBg;
        Padding = new Padding(0, 14, 0, 0);
    }

    public void SetItems(IEnumerable<RailItem> items, string initialActive)
    {
        _items.Clear();
        _items.AddRange(items);
        _active = initialActive;
        Build();
    }

    void Build()
    {
        Controls.Clear();
        _buttons.Clear();
        int y = 14;
        foreach (var item in _items)
        {
            var btn = new RailButton(item) { Left = (Width - 48) / 2, Top = y, Width = 48, Height = 52 };
            btn.Click += (_, _) => { SetActive(item.Key); item.Action(); };
            Controls.Add(btn);
            _buttons.Add(btn);
            y += 58;
        }
        Restyle();
    }

    public void SetActive(string key)
    {
        _active = key;
        Restyle();
    }

    void Restyle()
    {
        foreach (var b in _buttons) b.Active = b.Item.Key == _active;
    }
}

internal sealed class RailButton : Panel
{
    public RailItem Item { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get; set; }

    public RailButton(RailItem item)
    {
        Item = item;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        ParentChanged += (_, _) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent != null) using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);

        Color tint = Theme.ActiveTeam.Accent;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (Active)
        {
            using var path = RoundedRect(rect, Theme.RailItemRadius);
            using var fill = new SolidBrush(Color.FromArgb(36, tint.R, tint.G, tint.B));
            g.FillPath(fill, path);
        }

        var iconColor = Active ? tint : Theme.TextMuted;
        var iconRect = new Rectangle(0, 6, Width, 20);
        using var iconFont = AppFonts.Get(13, FontStyle.Regular);
        TextRenderer.DrawText(g, Item.Glyph, iconFont, iconRect, iconColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var textColor = Active ? Theme.TextPrimary : Theme.TextMuted;
        using var labelFont = AppFonts.Get(7.2f);
        TextRenderer.DrawText(g, Item.Label, labelFont, new Rectangle(0, 30, Width, 18), textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
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
