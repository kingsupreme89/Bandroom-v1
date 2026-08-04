using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Left panel's "Popular Teams" section: a live-search box + 2-column grid of team
/// swatch tiles (gradient block + name), 8 shown at a time. Extracted from the old modal-only
/// TeamPickerForm so it can live inline per the v4 handoff's always-visible left panel.</summary>
internal sealed class TeamGridPanel : Panel
{
    public event Action<TeamColor>? TeamSelected;

    const int TileCount = 8;
    TextBox _search = null!;
    Panel _grid = null!;
    string _filter = "";

    public TeamGridPanel()
    {
        BackColor = Color.Transparent;
        ParentChanged += (_, _) => { if (Parent != null) BackColor = Parent.BackColor; };
    }

    public void Build()
    {
        Controls.Clear();
        int y = 0;

        _search = new TextBox
        {
            Left = 0, Top = y, Width = Width, Height = 24,
            BackColor = Theme.InputFill, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Search teams",
            Font = AppFonts.Get(9),
        };
        _search.TextChanged += (_, _) => { _filter = _search.Text.Trim(); RenderGrid(); };
        Controls.Add(_search);
        y += 34;

        var lblPopular = new Label { Text = "POPULAR TEAMS", AutoSize = true, Left = 0, Top = y, Font = AppFonts.Get(8, FontStyle.Bold), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        Controls.Add(lblPopular);
        y += 20;

        _grid = new Panel { Left = 0, Top = y, Width = Width, Height = 2 * 74 + 8, BackColor = Color.Transparent };
        Controls.Add(_grid);

        RenderGrid();
    }

    void RenderGrid()
    {
        _grid.Controls.Clear();
        var matches = TeamColors.All
            .Where(t => t.Name != "General")
            .Where(t => _filter.Length == 0 || t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .Take(TileCount)
            .ToList();

        int gap = 8;
        int colW = (Width - gap) / 2;
        for (int i = 0; i < matches.Count; i++)
        {
            int row = i / 2, col = i % 2;
            var tile = new TeamTile(matches[i])
            {
                Left = col * (colW + gap), Top = row * 74, Width = colW, Height = 66,
                Selected = matches[i].Name == Theme.ActiveTeam.Name,
            };
            tile.Click += (_, _) => TeamSelected?.Invoke(matches[i]);
            _grid.Controls.Add(tile);
        }
    }

    public void RefreshTeamColors() => RenderGrid();
}

internal sealed class TeamTile : Panel
{
    readonly TeamColor _team;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }

    public TeamTile(TeamColor team)
    {
        _team = team;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent != null) using (var pb = new SolidBrush(Parent.BackColor)) g.FillRectangle(pb, ClientRectangle);

        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        using var outerPath = RoundedRect(outer, 8);
        if (Selected)
        {
            using var fill = new SolidBrush(Color.FromArgb(15, 255, 255, 255));
            g.FillPath(fill, outerPath);
            using var border = new Pen(Color.FromArgb(102, Theme.ActiveTeam.Accent), 1);
            g.DrawPath(border, outerPath);
        }

        var swatchRect = new Rectangle(4, 4, Width - 8, 34);
        using var swatchPath = RoundedRect(swatchRect, 6);
        Color c1 = _team.Accent;
        Color c2 = _team.Secondary ?? Color.FromArgb(Math.Max(0, c1.R - 60), Math.Max(0, c1.G - 60), Math.Max(0, c1.B - 60));
        using var swatchBrush = new LinearGradientBrush(swatchRect, c1, c2, 135f);
        g.FillPath(swatchBrush, swatchPath);

        using var nameFont = AppFonts.Get(8.5f);
        var textRect = new Rectangle(6, 42, Width - 12, 18);
        TextRenderer.DrawText(g, _team.Name, nameFont, textRect, Theme.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
    }

    static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0 || d > bounds.Width || d > bounds.Height) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
