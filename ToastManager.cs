using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Top-right toast stack: dark pill, auto-dismiss after 3s, newest at the bottom.
/// Lives as an overlay on the host form's top-right corner.</summary>
internal sealed class ToastManager
{
    readonly Control _host;
    readonly List<Panel> _active = new();

    public ToastManager(Control host) => _host = host;

    public void Show(string message)
    {
        var toast = new RoundedPanel
        {
            FillColor = Color.FromArgb(235, Theme.PanelBg),
            BorderColor = Theme.PanelBorder,
            Radius = 10,
            Height = 34,
            Parent = _host,
        };
        var lbl = new Label
        {
            Text = message, AutoSize = true, Font = AppFonts.Get(9, FontStyle.Bold),
            ForeColor = Theme.TextPrimary, BackColor = Color.Transparent,
        };
        toast.Controls.Add(lbl);
        using (var g = _host.CreateGraphics())
        {
            var size = TextRenderer.MeasureText(g, message, lbl.Font);
            toast.Width = size.Width + 32;
            lbl.Location = new Point(16, (toast.Height - size.Height) / 2 - 2);
        }

        _active.Add(toast);
        toast.BringToFront();
        RepositionAll();

        var timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            _active.Remove(toast);
            toast.Dispose();
            RepositionAll();
        };
        timer.Start();
    }

    void RepositionAll()
    {
        int y = 16;
        // Newest is at index Count-1 visually at the bottom of the stack -> render oldest on top.
        foreach (var t in _active)
        {
            t.Left = _host.Width - t.Width - 20;
            t.Top = y;
            y += t.Height + 8;
        }
    }
}
