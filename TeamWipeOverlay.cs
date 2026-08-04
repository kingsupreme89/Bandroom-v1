using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>~0.7s horizontal color-wipe transition played across the body when the active team
/// changes, using the new team's two brand colors (per the v4 handoff's "Team switching"
/// interaction). A gradient band slides left-to-right across the full body width, then hides.</summary>
internal sealed class TeamWipeOverlay : Panel
{
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    double _elapsed;
    const double DurationSeconds = 0.7;
    Color _c1, _c2;

    public TeamWipeOverlay()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Visible = false;
        _timer.Tick += (_, _) =>
        {
            _elapsed += _timer.Interval / 1000.0;
            if (_elapsed >= DurationSeconds) { _timer.Stop(); Visible = false; return; }
            Invalidate();
        };
    }

    public void Play(Color c1, Color c2)
    {
        _c1 = c1; _c2 = c2;
        _elapsed = 0;
        Visible = true;
        BringToFront();
        _timer.Start();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Visible || Width <= 0) return;
        double t = Math.Clamp(_elapsed / DurationSeconds, 0, 1);
        // band travels from -bandWidth to Width+bandWidth across the duration
        int bandWidth = Math.Max(200, Width / 3);
        int bandX = (int)(-bandWidth + t * (Width + bandWidth * 2));

        var rect = new Rectangle(bandX, 0, bandWidth, Height);
        using var brush = new LinearGradientBrush(new Rectangle(bandX, 0, Math.Max(1, bandWidth), Height),
            Color.FromArgb(230, _c1), Color.FromArgb(230, _c2), LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }
}
