using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Flat rounded-rect panel: fill + 1px border, optional pulsing team-color glow.
/// The design's "soft drop shadow" around the main panel is approximated with a few
/// widening, fading rounded-rect strokes behind the panel (GDI+ has no blur-behind).</summary>
internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.CardRadius;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Theme.PanelBg;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.PanelBorder;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DropShadow { get; set; } = false;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Glow { get; set; } = false;

    double _glowPhase;
    readonly System.Windows.Forms.Timer _glowTimer;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;

        _glowTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _glowTimer.Tick += (_, _) =>
        {
            _glowPhase += 0.0375; // ~3s loop at 60ms ticks
            if (_glowPhase > Math.PI * 2) _glowPhase -= Math.PI * 2;
            if (Glow) Invalidate();
        };
        _glowTimer.Start();
        Disposed += (_, _) => _glowTimer.Dispose();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { /* handled in OnPaint to avoid flicker/square fill-through */ }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (Parent != null)
        {
            using var parentBrush = new SolidBrush(Parent.BackColor);
            g.FillRectangle(parentBrush, ClientRectangle);
        }

        var rect = new Rectangle(2, 2, Width - 5, Height - 5);

        if (DropShadow)
        {
            for (int i = 5; i >= 1; i--)
            {
                var shadowRect = Rectangle.Inflate(rect, i * 2, i * 2);
                using var shadowPath = RoundedRect(shadowRect, Radius + i * 2);
                using var shadowPen = new Pen(Color.FromArgb(8, 0, 0, 0), 2);
                g.DrawPath(shadowPen, shadowPath);
            }
        }

        using var path = RoundedRect(rect, Radius);
        using var fill = new SolidBrush(FillColor);
        g.FillPath(fill, path);

        Color borderColor = BorderColor;
        float borderWidth = 1f;
        if (Glow)
        {
            // Lerp alpha between a subtle and a stronger glow, ease-in-out via sine.
            double t = (Math.Sin(_glowPhase) + 1) / 2; // 0..1
            int alpha = (int)(70 + t * 120);
            borderColor = Color.FromArgb(alpha, Theme.ActiveTeam.Accent);
            borderWidth = 1.5f;
        }

        using var border = new Pen(borderColor, borderWidth);
        g.DrawPath(border, path);
    }

    static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0 || d > bounds.Width || d > bounds.Height)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
