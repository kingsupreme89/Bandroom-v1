using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Left panel's "Categories" section: a vertical list, one row per category -- small
/// colored square + name + "{assigned}/{total} assigned". Clicking a row opens Quick Assign
/// pre-filtered to that category. Rebuilt from the old 3x2 tile-grid version (superseded PilePeak
/// layout) to the v4 handoff's vertical list.</summary>
internal sealed class CategoryMixPanel : Panel
{
    public event Action<string>? CategoryClicked;

    static readonly string[] Order = { "Downs", "Scoring", "Turnovers", "Special Teams", "Penalties", "Hype" };
    readonly Dictionary<string, CategoryRow> _rows = new();

    public CategoryMixPanel()
    {
        BackColor = Color.Transparent;
        ParentChanged += (_, _) => { if (Parent != null) BackColor = Parent.BackColor; };
    }

    public void Build()
    {
        Controls.Clear();
        _rows.Clear();

        var lblTitle = new Label { Text = "CATEGORIES", AutoSize = true, Left = 0, Top = 0, Font = AppFonts.Get(8, FontStyle.Bold), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        Controls.Add(lblTitle);

        int y = 22;
        foreach (var cat in Order)
        {
            var row = new CategoryRow(cat) { Left = 0, Top = y, Width = Width, Height = 40 };
            row.Click += (_, _) => CategoryClicked?.Invoke(cat);
            Controls.Add(row);
            _rows[cat] = row;
            y += 44;
        }
        Height = y;
    }

    public void RefreshFromConfig(List<TriggerEntry> config)
    {
        var byCategory = Order.ToDictionary(c => c, c => (assigned: 0, total: 0));
        foreach (var entry in config)
        {
            string cat = CategoryMap.Resolve(entry);
            if (!byCategory.ContainsKey(cat)) continue;
            var (assigned, total) = byCategory[cat];
            total++;
            if (!string.IsNullOrWhiteSpace(entry.AudioFile)) assigned++;
            byCategory[cat] = (assigned, total);
        }

        foreach (var kv in byCategory)
            if (_rows.TryGetValue(kv.Key, out var row)) row.SetCounts(kv.Value.assigned, kv.Value.total);
    }
}

internal sealed class CategoryRow : Panel
{
    readonly string _category;
    Label _lblName = null!, _lblSub = null!;
    Panel _swatch = null!;

    public CategoryRow(string category)
    {
        _category = category;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;

        _swatch = new Panel { Size = new Size(28, 28), Left = 0, Top = 6, BackColor = Color.FromArgb(46, Theme.CategoryColor(category).R, Theme.CategoryColor(category).G, Theme.CategoryColor(category).B) };
        Controls.Add(_swatch);

        _lblName = new Label { Text = category, AutoSize = true, Left = 38, Top = 4, Font = AppFonts.Get(10, FontStyle.Bold), ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        Controls.Add(_lblName);

        _lblSub = new Label { Text = "0/0 assigned", AutoSize = true, Left = 38, Top = 20, Font = AppFonts.Get(8), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        Controls.Add(_lblSub);

        foreach (Control c in Controls) c.Click += (_, _) => OnClick(EventArgs.Empty);
    }

    public void SetCounts(int assigned, int total)
    {
        _lblSub.Text = $"{assigned}/{total} assigned";
        _swatch.BackColor = Color.FromArgb(46, Theme.CategoryColor(_category).R, Theme.CategoryColor(_category).G, Theme.CategoryColor(_category).B);
    }
}
