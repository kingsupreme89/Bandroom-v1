using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

internal readonly record struct FeedItem(string Name, string Category, string Time, string Sub);

/// <summary>Right-anchored 290px slide-over: recently fired cues, newest first, capped at 8,
/// each row tinted by its category color. Opened via the bell icon in the top bar.</summary>
internal sealed class LiveFeedPanel : RoundedPanel
{
    const int MaxItems = 8;
    readonly List<FeedItem> _items = new();
    FlowLayoutPanel _list = null!;

    public LiveFeedPanel()
    {
        Width = 290;
        FillColor = Theme.PanelBg;
        BorderColor = Theme.PanelBorder;
        Radius = 16;
        DropShadow = true;
        Visible = false;
    }

    public void Build()
    {
        Controls.Clear();

        var lblTitle = new Label { Text = "Live Feed", Left = 20, Top = 18, AutoSize = true, Font = AppFonts.Get(12, FontStyle.Bold), ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        Controls.Add(lblTitle);

        var btnClose = new Label { Text = "✕", Left = Width - 34, Top = 18, AutoSize = true, Font = AppFonts.Get(10), ForeColor = Theme.TextMuted, BackColor = Color.Transparent, Cursor = Cursors.Hand };
        btnClose.Click += (_, _) => Visible = false;
        Controls.Add(btnClose);

        _list = new FlowLayoutPanel
        {
            Left = 14, Top = 50, Width = Width - 28, Height = Height - 64,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = Color.Transparent,
        };
        Controls.Add(_list);
        Render();
    }

    public void AddFired(string name, string category)
    {
        _items.Insert(0, new FeedItem(name, category, DateTime.Now.ToString("h:mm tt"), $"Fired via {Theme.ActiveTeam.Name}"));
        if (_items.Count > MaxItems) _items.RemoveAt(_items.Count - 1);
        Render();
    }

    public bool HasItems => _items.Count > 0;

    void Render()
    {
        if (_list == null) return;
        _list.Controls.Clear();
        foreach (var item in _items)
        {
            var row = new RoundedPanel
            {
                Width = _list.Width - 4, Height = 54,
                FillColor = Theme.TileFillSmall, BorderColor = Theme.PanelBorder, Radius = 10,
                Margin = new Padding(0, 0, 0, 8),
            };
            Color tint = Theme.CategoryColor(item.Category);
            var stripe = new Panel { Width = 3, Dock = DockStyle.Left, BackColor = tint };
            row.Controls.Add(stripe);

            var lblName = new Label { Text = item.Name, Left = 12, Top = 8, AutoSize = true, Font = AppFonts.Get(9.5f, FontStyle.Bold), ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
            var lblSub = new Label { Text = $"{item.Time}  ·  {item.Sub}", Left = 12, Top = 28, AutoSize = true, Font = AppFonts.Get(8), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
            row.Controls.Add(lblName);
            row.Controls.Add(lblSub);

            _list.Controls.Add(row);
        }

        if (_items.Count == 0)
        {
            var lblEmpty = new Label { Text = "No cues fired yet this session.", AutoSize = true, ForeColor = Theme.TextMuted, Font = AppFonts.Get(9), BackColor = Color.Transparent };
            _list.Controls.Add(lblEmpty);
        }
    }
}
