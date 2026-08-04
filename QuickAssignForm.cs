using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>The v4 handoff's "Quick Assign" modal: team select + a live-filtered list of all
/// 33 events (optionally pre-filtered by category, when opened from the left panel's
/// Categories list), each row showing its category. Picking a row opens Assign Track.
/// Replaces the old TeamPickerForm (same shape: team combobox + search + event list), restyled
/// to the v4 modal palette and reachable via Ctrl/Cmd+K, the header's session chevron, or a
/// Categories-list row.</summary>
internal sealed class QuickAssignForm : Form
{
    readonly List<TriggerEntry> _config;
    ComboBox _cboTeam = null!;
    TextBox _txtSearch = null!;
    ListBox _lstEvents = null!;

    public TeamColor SelectedTeam { get; private set; }
    public TriggerEntry? EventToAssign { get; private set; }
    public bool TeamChanged { get; private set; }

    public QuickAssignForm(IWin32Window owner, List<TriggerEntry> config, string? categoryFilter = null)
    {
        _config = config;
        SelectedTeam = Theme.ActiveTeam;

        Text = "Quick Assign";
        Width = 460;
        Height = 460;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.ChromeBg;
        ForeColor = Theme.TextPrimary;

        BuildUi(categoryFilter);
    }

    void BuildUi(string? categoryFilter)
    {
        var lblTeam = new Label { Text = "TEAM", Left = 16, Top = 14, AutoSize = true, ForeColor = Theme.TextMuted, Font = AppFonts.Get(8, FontStyle.Bold) };
        Controls.Add(lblTeam);

        _cboTeam = new ComboBox
        {
            Left = 16, Top = 32, Width = 412, Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.InputFill,
            ForeColor = Theme.TextPrimary,
        };
        foreach (var team in TeamColors.All) _cboTeam.Items.Add(team.Name);
        int startIdx = Array.FindIndex(TeamColors.All, t => t.Name == SelectedTeam.Name);
        _cboTeam.SelectedIndex = Math.Max(0, startIdx);
        _cboTeam.SelectedIndexChanged += (_, _) => { SelectedTeam = TeamColors.All[_cboTeam.SelectedIndex]; TeamChanged = true; };
        Controls.Add(_cboTeam);

        _txtSearch = new TextBox
        {
            Left = 16, Top = 72, Width = 412, Height = 26,
            BackColor = Theme.InputFill, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Quick assign… type an event name",
            Text = categoryFilter ?? "",
        };
        _txtSearch.TextChanged += (_, _) => FilterEvents();
        Controls.Add(_txtSearch);

        _lstEvents = new ListBox
        {
            Left = 16, Top = 106, Width = 412, Height = 270,
            BackColor = Theme.InputFill, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 24,
        };
        _lstEvents.DrawItem += LstEvents_DrawItem;
        _lstEvents.DoubleClick += (_, _) => AssignSelected();
        Controls.Add(_lstEvents);
        FilterEvents(categoryFilter);

        var btnClose = new GlassButton { Text = "Close", Left = 352, Top = 386, Width = 76, Height = 30 };
        btnClose.Click += (_, _) => { DialogResult = TeamChanged ? DialogResult.OK : DialogResult.Cancel; Close(); };
        Controls.Add(btnClose);
    }

    void LstEvents_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var entry = (TriggerEntry)_lstEvents.Items[e.Index];
        e.DrawBackground();
        string category = CategoryMap.Resolve(entry);
        using var nameFont = AppFonts.Get(9.5f);
        using var catFont = AppFonts.Get(8);
        var rect = e.Bounds;
        TextRenderer.DrawText(e.Graphics, entry.Event, nameFont, new Rectangle(rect.X + 8, rect.Y, rect.Width - 100, rect.Height), Theme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics, category, catFont, new Rectangle(rect.Right - 96, rect.Y, 88, rect.Height), Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        e.DrawFocusRectangle();
    }

    void FilterEvents(string? seedTerm = null)
    {
        string term = (seedTerm ?? _txtSearch?.Text ?? "").Trim();
        _lstEvents.Items.Clear();
        foreach (var entry in _config)
        {
            bool matches = term.Length == 0
                || entry.Event.Contains(term, StringComparison.OrdinalIgnoreCase)
                || CategoryMap.Resolve(entry).Contains(term, StringComparison.OrdinalIgnoreCase);
            if (matches) _lstEvents.Items.Add(entry);
        }
    }

    void AssignSelected()
    {
        if (_lstEvents.SelectedItem is not TriggerEntry entry) return;
        EventToAssign = entry;
        DialogResult = DialogResult.OK;
        Close();
    }
}
