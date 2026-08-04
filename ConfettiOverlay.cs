using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>~1.5s, 24-particle confetti burst for Scoring-category cue fires. Transparent
/// overlay sized to the host form, self-removing when the animation finishes.</summary>
internal sealed class ConfettiOverlay : Panel
{
    struct Particle
    {
        public float X, Y, Vx, Vy, Rotation, RotSpeed;
        public Color Color;
        public int Size;
    }

    static readonly Color[] Palette =
    {
        Theme.CategoryScoring, Theme.CategoryTurnovers, Theme.CategorySpecialTeams,
        Theme.CategoryPenalties, Color.White,
    };

    Particle[] _particles = Array.Empty<Particle>();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    double _elapsed;
    const double DurationSeconds = 1.5;

    public ConfettiOverlay()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Visible = false;
        _timer.Tick += Tick;
    }

    public void Burst()
    {
        var rnd = Random.Shared;
        _particles = new Particle[24];
        for (int i = 0; i < _particles.Length; i++)
        {
            _particles[i] = new Particle
            {
                X = rnd.Next(0, Math.Max(1, Width)),
                Y = -rnd.Next(0, 40),
                Vx = (float)(rnd.NextDouble() * 2 - 1) * 40,
                Vy = 90 + (float)rnd.NextDouble() * 120,
                Rotation = (float)(rnd.NextDouble() * 360),
                RotSpeed = (float)(rnd.NextDouble() * 6 - 3) * 60,
                Color = Palette[rnd.Next(Palette.Length)],
                Size = rnd.Next(5, 10),
            };
        }
        _elapsed = 0;
        Visible = true;
        BringToFront();
        _timer.Start();
    }

    void Tick(object? sender, EventArgs e)
    {
        double dt = _timer.Interval / 1000.0;
        _elapsed += dt;
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            p.X += p.Vx * (float)dt;
            p.Y += p.Vy * (float)dt;
            p.Rotation += p.RotSpeed * (float)dt;
        }
        Invalidate();

        if (_elapsed >= DurationSeconds)
        {
            _timer.Stop();
            Visible = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Visible) return;
        var g = e.Graphics;
        double fadeStart = DurationSeconds * 0.7;
        int alpha = _elapsed <= fadeStart ? 255 : (int)(255 * Math.Max(0, 1 - (_elapsed - fadeStart) / (DurationSeconds - fadeStart)));

        foreach (var p in _particles)
        {
            using var brush = new SolidBrush(Color.FromArgb(alpha, p.Color));
            var state = g.Save();
            g.TranslateTransform(p.X, p.Y);
            g.RotateTransform(p.Rotation);
            g.FillRectangle(brush, -p.Size / 2, -p.Size / 4, p.Size, p.Size / 2);
            g.Restore(state);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { /* stay transparent */ }
}
