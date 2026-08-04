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
}
