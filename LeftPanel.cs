using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>230px left panel: team search + grid, then the categories list, in one scrollable
/// column (per the v4 handoff's Layout Overview).</summary>
internal sealed class LeftPanel : Panel
{
    public event Action<TeamColor>? TeamSelected;
    public event Action<string>? CategoryClicked;

    public TeamGridPanel TeamGrid { get; } = new();
    public CategoryMixPanel Categories { get; } = new();

    public LeftPanel()
    {
        Width = Theme.SidePanelWidth;
        Dock = DockStyle.Left;
        BackColor = Theme.PanelBg;
        AutoScroll = true;
        Padding = new Padding(16);

        TeamGrid.TeamSelected += t => TeamSelected?.Invoke(t);
        Categories.CategoryClicked += c => CategoryClicked?.Invoke(c);
    }

    public void Build()
    {
        Controls.Clear();
        int innerWidth = Width - 32;

        TeamGrid.Left = 0; TeamGrid.Top = 0; TeamGrid.Width = innerWidth;
        TeamGrid.Build();
        Controls.Add(TeamGrid);

        int catsTop = TeamGrid.Height + 18;
        Categories.Left = 0; Categories.Top = catsTop; Categories.Width = innerWidth;
        Categories.Build();
        Controls.Add(Categories);
    }

    public void RefreshFromConfig(List<TriggerEntry> config) => Categories.RefreshFromConfig(config);

    public void RefreshTeamColors() => TeamGrid.RefreshTeamColors();
}
