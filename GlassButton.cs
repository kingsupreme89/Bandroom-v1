using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>A flat, rounded, gradient-filled button that reads as "glass" -- WinForms has
/// no real backdrop-blur API (that needs WPF/WinUI composition), so this is a visual
/// approximation: soft top-to-bottom gradient, thin light border, brightens on hover.</summary>
internal sealed class GlassButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    bool _hover;

    public GlassButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        BackColor = Theme.PanelBg;
        Cursor = Cursors.Hand;
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 7);

        Color top = Primary ? Theme.AccentBright : Color.FromArgb(_hover ? 58 : 46, _hover ? 58 : 46, _hover ? 62 : 50);
        Color bottom = Primary ? Theme.Accent : Color.FromArgb(_hover ? 40 : 30, _hover ? 40 : 30, _hover ? 44 : 34);

        using var fill = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical);
        g.FillPath(fill, path);

        using var border = new Pen(Primary ? Theme.AccentBright : Color.FromArgb(70, 255, 255, 255), 1);
        g.DrawPath(border, path);

        TextRenderer.DrawText(g, Text, Font, rect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
