using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>34px "mac window chrome" strip: traffic-light dots, a new-window glyph, back/
/// forward chevrons, and a faint right-aligned icon cluster (clock/share/plus). Purely
/// decorative window dressing per the v4 handoff -- no URL bar (explicitly dropped), no
/// real window controls (the real title bar is already suppressed elsewhere/unused).</summary>
internal sealed class ChromeBar : Panel
{
    public ChromeBar()
    {
        Height = Theme.ChromeBarHeight;
        Dock = DockStyle.Top;
        BackColor = Theme.ChromeBg;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.ChromeBg)) g.FillRectangle(bg, ClientRectangle);

        int cx = 14, cy = Height / 2;
        DrawDot(g, cx, cy, ColorTranslator.FromHtml("#ff5f57"));
        DrawDot(g, cx + 19, cy, ColorTranslator.FromHtml("#febc2e"));
        DrawDot(g, cx + 38, cy, ColorTranslator.FromHtml("#28c840"));

        Color faint = Color.FromArgb(90, 201, 211, 220);
        using var pen = new Pen(faint, 1.6f);
        int gx = cx + 38 + 22;
        // new-window glyph (rounded rect + split)
        g.DrawRectangle(pen, gx, cy - 6, 15, 12);
        g.DrawLine(pen, gx + 6, cy - 6, gx + 6, cy + 6);
        gx += 26;
        DrawChevron(g, pen, gx, cy, left: true);
        gx += 16;
        DrawChevron(g, pen, gx, cy, left: false);

        // right-aligned faint icon cluster: clock, share, plus
        using var pen2 = new Pen(faint, 1.4f);
        int rx = Width - 18;
        DrawPlus(g, pen2, rx, cy); rx -= 22;
        DrawShare(g, pen2, rx, cy); rx -= 22;
        DrawClock(g, pen2, rx, cy);
    }

    static void DrawDot(Graphics g, int cx, int cy, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - 5, cy - 5, 11, 11);
    }

    static void DrawChevron(Graphics g, Pen pen, int cx, int cy, bool left)
    {
        var pts = left
            ? new[] { new Point(cx + 3, cy - 6), new Point(cx - 3, cy), new Point(cx + 3, cy + 6) }
            : new[] { new Point(cx - 3, cy - 6), new Point(cx + 3, cy), new Point(cx - 3, cy + 6) };
        g.DrawLines(pen, pts);
    }

    static void DrawClock(Graphics g, Pen pen, int cx, int cy)
    {
        g.DrawEllipse(pen, cx - 7, cy - 7, 14, 14);
        g.DrawLine(pen, cx, cy - 4, cx, cy);
        g.DrawLine(pen, cx, cy, cx + 3, cy + 2);
    }

    static void DrawShare(Graphics g, Pen pen, int cx, int cy)
    {
        g.DrawLine(pen, cx, cy - 7, cx, cy + 5);
        g.DrawLine(pen, cx - 5, cy - 2, cx, cy - 7);
        g.DrawLine(pen, cx + 5, cy - 2, cx, cy - 7);
        g.DrawLine(pen, cx - 7, cy + 8, cx + 7, cy + 8);
    }

    static void DrawPlus(Graphics g, Pen pen, int cx, int cy)
    {
        g.DrawLine(pen, cx, cy - 7, cx, cy + 7);
        g.DrawLine(pen, cx - 7, cy, cx + 7, cy);
    }
}
