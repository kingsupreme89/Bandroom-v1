using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

internal static class PromptDialog
{
    public static string? Show(IWin32Window owner, string title, string label, string defaultValue = "")
    {
        using var form = new Form
        {
            Text = title,
            Width = 380,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
        };

        var lbl = new Label { Text = label, Left = 12, Top = 12, Width = 340, Height = 20 };
        var txt = new TextBox { Left = 12, Top = 36, Width = 340, Text = defaultValue };
        var btnOk = new Button { Text = "OK", Left = 196, Top = 70, Width = 75, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Left = 277, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.Add(lbl);
        form.Controls.Add(txt);
        form.Controls.Add(btnOk);
        form.Controls.Add(btnCancel);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text)
            ? txt.Text.Trim()
            : null;
    }

    /// <summary>Result of <see cref="ShowTrackNaming"/> -- Name already has the " w/ Crowd" suffix
    /// baked in when CrowdNoise was checked, so every caller downstream (disk write, marketplace
    /// upload) just uses Name as-is and never has to remember to append the suffix itself.</summary>
    public sealed record TrackNamingResult(string Name, string Type, bool CrowdNoise);

    /// <summary>Same track-naming prompt as <see cref="Show"/>, extended for the local-import
    /// pipeline (item 21) with a Song/PA type choice (parity with the marketplace's "pa" upload
    /// type) and a "recorded with crowd noise" checkbox. The suffix is applied here, once, before
    /// returning -- never left to a caller to remember.</summary>
    public static TrackNamingResult? ShowTrackNaming(IWin32Window owner, string title, string label, string defaultValue = "")
    {
        using var form = new Form
        {
            Text = title,
            Width = 380,
            Height = 230,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
        };

        var lbl = new Label { Text = label, Left = 12, Top = 12, Width = 340, Height = 20 };
        var txt = new TextBox { Left = 12, Top = 36, Width = 340, Text = defaultValue };

        var lblType = new Label { Text = "Type", Left = 12, Top = 68, Width = 340, Height = 18 };
        var radSong = new RadioButton { Text = "Song", Left = 12, Top = 88, Width = 100, Checked = true };
        var radPa = new RadioButton { Text = "PA Announcer Clip", Left = 120, Top = 88, Width = 200 };

        var chkCrowd = new CheckBox { Text = "Recorded with crowd noise", Left = 12, Top = 116, Width = 340 };

        var btnOk = new Button { Text = "OK", Left = 196, Top = 150, Width = 75, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Left = 277, Top = 150, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.Add(lbl);
        form.Controls.Add(txt);
        form.Controls.Add(lblType);
        form.Controls.Add(radSong);
        form.Controls.Add(radPa);
        form.Controls.Add(chkCrowd);
        form.Controls.Add(btnOk);
        form.Controls.Add(btnCancel);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog(owner) != DialogResult.OK || string.IsNullOrWhiteSpace(txt.Text))
            return null;

        string baseName = txt.Text.Trim();
        string name = chkCrowd.Checked ? $"{baseName} w/ Crowd" : baseName;
        string type = radPa.Checked ? "pa" : "song";
        return new TrackNamingResult(name, type, chkCrowd.Checked);
    }
}
