using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>How-to-use card, opened from the "Help" rail button. Rewritten for the WebView2
/// shell's actual flow (team grid / category chips / inline situations list) -- the old
/// version described the abandoned native modal's Ctrl+K quick-assign flow, which no longer
/// exists.</summary>
internal sealed class ShortcutsForm : Form
{
    static readonly (string, string)[] Steps =
    {
        ("1. Pick a team", "Click a color swatch in the Team panel (left). Its stadium background loads and every situation switches to that team's saved sounds."),
        ("2. Browse situations", "Click a category chip (Downs, Scoring, ... or All) in the bar to open a list of every situation in it."),
        ("3. Assign a sound", "In that list, click \"Assign / Edit\" on a situation to pick (or trim) an audio file for it."),
        ("4. Preview / Stop", "Once assigned, \"Preview\" plays it right there; \"Stop\" kills whatever's currently playing."),
        ("5. Start Watching", "Top-right pill turns the app on to auto-fire sounds as it reads the game screen -- green means it found the game window."),
        ("6. Adjust", "Right panel: master Volume, Fire Sensitivity (delay in seconds before a cue fades out -- no fade-in), and Reverb room."),
    };

    public ShortcutsForm(IWin32Window owner)
    {
        Text = "How to Use Bandroom";
        Width = 460;
        Height = 420;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.PanelBg;
        ForeColor = Theme.TextPrimary;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        var lblTitle = new Label { Text = "How to Use Bandroom", Left = 20, Top = 16, AutoSize = true, Font = AppFonts.Get(13, FontStyle.Bold), ForeColor = Theme.TextPrimary };
        Controls.Add(lblTitle);

        int y = 52;
        foreach (var (heading, body) in Steps)
        {
            var lblHeading = new Label { Text = heading, Left = 20, Top = y, Width = 420, AutoSize = true, Font = AppFonts.Get(10, FontStyle.Bold), ForeColor = Theme.TextPrimary };
            Controls.Add(lblHeading);
            y += 20;

            var lblBody = new Label { Text = body, Left = 20, Top = y, Width = 420, AutoSize = false, Height = 34, Font = AppFonts.Get(9), ForeColor = Theme.TextMuted };
            Controls.Add(lblBody);
            y += 40;
        }

        var btnClose = new GlassButton { Text = "Got it", Left = 344, Top = y + 4, Width = 96, Height = 28 };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);
        Height = y + 80;
    }
}
