using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Design tokens ported from the "Stadium Sound Selector - UI Redesign" handoff.
/// Hex values are copied verbatim from the design's README/script -- WinForms controls can't
/// alpha-composite against arbitrary backgrounds the way CSS rgba() can, so the alpha-over-
/// panel tokens (border/divider/input fill/tile fill) are pre-blended solid approximations
/// of "white at N% over #17181c", not literal translations.</summary>
internal static class Theme
{
    // --- Base palette (v4 "editor-style layout" handoff) ---
    public static readonly Color WindowBg = ColorTranslator.FromHtml("#161719");
    public static readonly Color ChromeBg = ColorTranslator.FromHtml("#1b1c1f");   // mac chrome bar / header bar / rails
    public static readonly Color PageBg = WindowBg;
    public static readonly Color PanelBg = WindowBg;   // side panels share the window bg per spec
    public static readonly Color TextPrimary = ColorTranslator.FromHtml("#e7ecf1");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#7d8791");
    public static readonly Color TextMuted2 = ColorTranslator.FromHtml("#8b95a1");
    public static readonly Color Success = ColorTranslator.FromHtml("#3ddc84");
    public static readonly Color Warning = ColorTranslator.FromHtml("#e14b4b");

    // Blended approximations of the design's rgba(255,255,255,N) tokens over the dark bg.
    public static readonly Color PanelBorder = Color.FromArgb(43, 44, 48);   // border 0.08
    public static readonly Color Divider = Color.FromArgb(40, 41, 45);       // divider 0.06-0.1
    public static readonly Color InputFill = Color.FromArgb(36, 37, 41);     // input fill 0.05
    public static readonly Color TileFillSmall = Color.FromArgb(33, 34, 38); // small tile fill 0.04
    public static readonly Color WatermarkText = Color.FromArgb(31, 32, 36);

    // --- Legacy aliases kept so existing controls (grid, buttons) don't need touching ---
    public static readonly Color Bg = WindowBg;
    public static readonly Color CardBg = TileFillSmall;
    public static readonly Color GridBg = PanelBg;
    public static readonly Color GridAltRow = Color.FromArgb(20, 21, 25);
    public static readonly Color GridHeaderBg = TileFillSmall;
    public static readonly Color Border = PanelBorder;
    public static readonly Color TextSecondary = TextMuted2;

    // --- Spacing / radius (px) ---
    public const int PanelRadius = 24;
    public const int CardRadius = 14;
    public const int ChipRadius = 8;
    public const int RailItemRadius = 10;
    public const int HeroRadius = 20;
    public const int ModalRadius = 16;

    // --- v4 layout measurements ---
    public const int ChromeBarHeight = 34;
    public const int HeaderBarHeight = 56;
    public const int RailWidth = 64;
    public const int SidePanelWidth = 230;
    public const int TimelineHeight = 160;

    // --- Team theming ---
    // The active team drives Accent/AccentBright everywhere; defaults to "General" (cyan).
    static TeamColor _activeTeam = TeamColors.All[0];
    public static TeamColor ActiveTeam
    {
        get => _activeTeam;
        set => _activeTeam = value;
    }

    /// <summary>Muted/desaturated-for-fills version of the active team color, used where the
    /// old "restrained accent" was used (primary button fill base, selection background).</summary>
    public static Color Accent => Darken(ActiveTeam.Accent, 0.35);
    public static Color AccentDark => Darken(ActiveTeam.Accent, 0.65);
    public static Color AccentBright => ActiveTeam.Accent;

    static Color Darken(Color c, double amount)
    {
        int Lerp(int channel) => (int)(channel * (1 - amount));
        return Color.FromArgb(Lerp(c.R), Lerp(c.G), Lerp(c.B));
    }

    // --- Category colors (design spec: Downs/Hype follow the active team color) ---
    public static readonly Color CategoryScoring = ColorTranslator.FromHtml("#3ddc84");
    public static readonly Color CategoryTurnovers = ColorTranslator.FromHtml("#f5c451");
    public static readonly Color CategorySpecialTeams = ColorTranslator.FromHtml("#a855f7");
    public static readonly Color CategoryPenalties = ColorTranslator.FromHtml("#f47174");
    public static Color CategoryDowns => ActiveTeam.Accent;
    public static Color CategoryHype => ActiveTeam.Accent;

    public static readonly string[] CategoryNames =
        { "Downs", "Scoring", "Turnovers", "Special Teams", "Penalties", "Hype" };

    public static Color CategoryColor(string category) => category switch
    {
        "Downs" => CategoryDowns,
        "Scoring" => CategoryScoring,
        "Turnovers" => CategoryTurnovers,
        "Special Teams" => CategorySpecialTeams,
        "Penalties" => CategoryPenalties,
        "Hype" => CategoryHype,
        _ => CategoryDowns,
    };

    public static void StyleButton(GlassButton b, bool primary = false)
    {
        b.Primary = primary;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = GridBg;
        grid.GridColor = Border;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = GridHeaderBg;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(grid.Font.FontFamily, 8, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = GridHeaderBg;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeight = 28;
        grid.DefaultCellStyle.BackColor = GridBg;
        grid.DefaultCellStyle.ForeColor = TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = AccentDark;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = GridAltRow;
        grid.RowsDefaultCellStyle.SelectionBackColor = AccentDark;
        grid.RowsDefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowTemplate.Height = 26;
    }
}
