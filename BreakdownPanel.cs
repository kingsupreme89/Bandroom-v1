using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>The "Breakdown" table: category tabs, event/track/status/priority grid with
/// reassign(pencil)/test-fire(play) actions, 7-per-page pagination. Wraps a DataGridView
/// rather than hand-rolling row rendering/hit-testing -- the grid's Browse/Test/Trim/Clear
/// flow already works and is tested against the live game; this keeps that reliability while
/// presenting the new look. Trim/Clear move inside the Assign Track modal (pencil action)
/// instead of being separate grid buttons, per the design's 2-icon-action row.</summary>
internal sealed class BreakdownPanel : Panel
{
    public event Action<TriggerEntry>? RequestAssign;
    public event Action<TriggerEntry>? RequestTestFire;

    const int PageSize = 7;

    DataGridView _grid = null!;
    FlowLayoutPanel _tabRow = null!;
    Panel _pagerRow = null!;
    Label _lblShowing = null!;
    readonly Dictionary<string, Label> _tabLabels = new();

    List<TriggerEntry> _config = new();
    string _activeTab = "All";
    string _search = "";
    int _page = 1;

    public BreakdownPanel()
    {
        BackColor = Color.Transparent;
        ParentChanged += (_, _) => { if (Parent != null) BackColor = Parent.BackColor; };
    }

    public void Build()
    {
        Controls.Clear();

        var lblTitle = new Label { Text = "Breakdown", AutoSize = true, Left = 0, Top = 0, Font = AppFonts.Get(13, FontStyle.Bold), ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        Controls.Add(lblTitle);

        var txtSearch = new TextBox
        {
            Left = Width - 200, Top = -2, Width = 200, Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Theme.InputFill, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Search events or tracks...",
        };
        txtSearch.TextChanged += (_, _) => SetSearch(txtSearch.Text);
        Controls.Add(txtSearch);

        _tabRow = new FlowLayoutPanel
        {
            Left = 0, Top = 28, Width = Width, Height = 28,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = false,
            BackColor = Color.Transparent,
        };
        Controls.Add(_tabRow);
        BuildTabs();

        _pagerRow = new Panel { Left = 0, Height = 26, Width = Width, Dock = DockStyle.Bottom, BackColor = Color.Transparent };
        Controls.Add(_pagerRow);

        _grid = new DataGridView
        {
            Left = 0, Top = 64, Width = Width, Height = Math.Max(60, Height - 64 - 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };

        var eventCol = new DataGridViewTextBoxColumn { Name = "Event", HeaderText = "EVENT", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
        var trackCol = new DataGridViewTextBoxColumn { Name = "Track", HeaderText = "TRACK", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
        var statusCol = new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "STATUS", Width = 100, AutoSizeMode = DataGridViewAutoSizeColumnMode.None };
        var priorityCol = new DataGridViewTextBoxColumn { Name = "Priority", HeaderText = "PRIORITY", Width = 90, AutoSizeMode = DataGridViewAutoSizeColumnMode.None };
        var editCol = new DataGridViewButtonColumn { Name = "Edit", HeaderText = "", Text = "✎", UseColumnTextForButtonValue = true, Width = 40, AutoSizeMode = DataGridViewAutoSizeColumnMode.None };
        var playCol = new DataGridViewButtonColumn { Name = "Play", HeaderText = "", Text = "▶", UseColumnTextForButtonValue = true, Width = 40, AutoSizeMode = DataGridViewAutoSizeColumnMode.None };

        _grid.Columns.AddRange(eventCol, trackCol, statusCol, priorityCol, editCol, playCol);
        Theme.StyleGrid(_grid);
        _grid.CellClick += Grid_CellClick;
        Controls.Add(_grid);

        BuildPager();
    }

    void BuildTabs()
    {
        _tabRow.Controls.Clear();
        _tabLabels.Clear();
        var names = new[] { "All" }.Concat(Theme.CategoryNames);
        foreach (var name in names)
        {
            var lbl = new Label
            {
                Text = name, AutoSize = true, Padding = new Padding(10, 5, 10, 5),
                Font = AppFonts.Get(9, FontStyle.Bold), Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 6, 0),
            };
            lbl.Click += (_, _) => SetActiveCategory(name);
            _tabRow.Controls.Add(lbl);
            _tabLabels[name] = lbl;
        }
        RestyleTabs();
    }

    void RestyleTabs()
    {
        foreach (var (name, lbl) in _tabLabels.Select(kv => (kv.Key, kv.Value)))
        {
            bool active = name == _activeTab;
            Color tint = name == "All" ? Theme.ActiveTeam.Accent : Theme.CategoryColor(name);
            lbl.ForeColor = active ? tint : Theme.TextMuted;
            lbl.BackColor = active ? Color.FromArgb(36, tint.R, tint.G, tint.B) : Color.Transparent;
        }
    }

    void BuildPager()
    {
        _pagerRow.Controls.Clear();
        var btnPrev = new GlassButton { Text = "Prev", Size = new Size(60, 24), Left = 0, Top = 0 };
        btnPrev.Click += (_, _) => { if (_page > 1) { _page--; Render(); } };
        _pagerRow.Controls.Add(btnPrev);

        var btnNext = new GlassButton { Text = "Next", Size = new Size(60, 24), Left = 70, Top = 0 };
        btnNext.Click += (_, _) => { _page++; Render(); };
        _pagerRow.Controls.Add(btnNext);

        _lblShowing = new Label { AutoSize = true, Left = 150, Top = 4, Font = AppFonts.Get(8.5f), ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
        _pagerRow.Controls.Add(_lblShowing);
    }

    public void SetActiveCategory(string category)
    {
        _activeTab = category;
        _page = 1;
        RestyleTabs();
        Render();
    }

    public void SetSearch(string term)
    {
        _search = term.Trim();
        _page = 1;
        Render();
    }

    public void SetConfig(List<TriggerEntry> config)
    {
        _config = config;
        Render();
    }

    IEnumerable<TriggerEntry> Filtered() => _config.Where(e =>
        (_activeTab == "All" || CategoryMap.Resolve(e) == _activeTab) &&
        (_search.Length == 0 ||
         e.Event.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
         (!string.IsNullOrWhiteSpace(e.AudioFile) && Path.GetFileNameWithoutExtension(e.AudioFile).Contains(_search, StringComparison.OrdinalIgnoreCase))));

    public void Render()
    {
        if (_grid == null) return;
        var filtered = Filtered().ToList();
        int totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        if (_page > totalPages) _page = totalPages;
        if (_page < 1) _page = 1;

        var pageItems = filtered.Skip((_page - 1) * PageSize).Take(PageSize).ToList();

        _grid.Rows.Clear();
        foreach (var entry in pageItems)
        {
            bool assigned = !string.IsNullOrWhiteSpace(entry.AudioFile);
            string trackLabel = assigned ? Path.GetFileNameWithoutExtension(entry.AudioFile) : "—";
            string status = assigned ? "●  Assigned" : "○  Unassigned";
            string stars = StarsFor(entry.Event);

            int idx = _grid.Rows.Add(entry.Event, trackLabel, status, stars, "✎", "▶");
            var row = _grid.Rows[idx];
            row.Tag = entry;
            row.Cells["Status"].Style.ForeColor = assigned ? Theme.Success : Theme.TextMuted;
            row.Cells["Event"].Style.ForeColor = Theme.CategoryColor(CategoryMap.Resolve(entry));
        }

        int from = filtered.Count == 0 ? 0 : (_page - 1) * PageSize + 1;
        int to = Math.Min(_page * PageSize, filtered.Count);
        _lblShowing.Text = $"Showing {from}–{to} of {filtered.Count}";
    }

    static string StarsFor(string eventName)
    {
        int hash = 0;
        foreach (char c in eventName) hash = (hash * 31 + c) & int.MaxValue;
        int filled = 1 + (hash % 5);
        return new string('★', filled) + new string('☆', 5 - filled);
    }

    void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not TriggerEntry entry) return;
        string colName = _grid.Columns[e.ColumnIndex].Name;

        if (colName == "Edit") RequestAssign?.Invoke(entry);
        else if (colName == "Play") RequestTestFire?.Invoke(entry);
    }
}
