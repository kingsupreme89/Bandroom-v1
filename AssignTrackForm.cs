using System.Drawing;
using System.Windows.Forms;

namespace SupremeStadiumSoundSelector;

/// <summary>Styled track picker replacing the raw OpenFileDialog-only flow. Lists audio files
/// found in the Songs library (plus anything already assigned elsewhere, even outside that
/// folder) for one-click assignment, with Browse/Trim/Clear still available underneath --
/// those are real, tested features (per the design handoff's non-goal: don't drop existing
/// functionality for the sake of the new look).</summary>
internal sealed class AssignTrackForm : Form
{
    readonly TriggerEntry _entry;
    ListBox _lstTracks = null!;
    Label _lblCurrent = null!;

    public string? AssignedPath { get; private set; }
    public bool RequestTrim { get; private set; }
    public bool RequestClear { get; private set; }

    public AssignTrackForm(IWin32Window owner, TriggerEntry entry, IEnumerable<string> libraryPaths)
    {
        _entry = entry;

        Text = "Assign Track";
        Width = 440;
        Height = 460;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.PanelBg;
        ForeColor = Theme.TextPrimary;

        BuildUi(libraryPaths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).ToList());
    }

    void BuildUi(List<string> library)
    {
        var lblTitle = new Label { Text = "Assign Track", Left = 16, Top = 14, AutoSize = true, Font = AppFonts.Get(13, FontStyle.Bold), ForeColor = Theme.TextPrimary };
        Controls.Add(lblTitle);

        var lblSub = new Label { Text = $"for {_entry.Event}", Left = 16, Top = 40, AutoSize = true, Font = AppFonts.Get(9), ForeColor = Theme.TextMuted };
        Controls.Add(lblSub);

        string currentLabel = string.IsNullOrWhiteSpace(_entry.AudioFile) ? "(none assigned)" : Path.GetFileNameWithoutExtension(_entry.AudioFile);
        _lblCurrent = new Label { Text = $"Current: {currentLabel}", Left = 16, Top = 62, AutoSize = true, Font = AppFonts.Get(8.5f), ForeColor = Theme.TextMuted };
        Controls.Add(_lblCurrent);

        _lstTracks = new ListBox
        {
            Left = 16, Top = 90, Width = 392, Height = 240,
            BackColor = Theme.InputFill, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
        };
        foreach (var path in library) _lstTracks.Items.Add(new TrackItem(path));
        _lstTracks.DoubleClick += (_, _) => AssignFromList();
        Controls.Add(_lstTracks);

        if (library.Count == 0)
        {
            var lblEmpty = new Label { Text = "No songs in your library yet — use Browse for file... below.", Left = 20, Top = 200, Width = 360, ForeColor = Theme.TextMuted, Font = AppFonts.Get(8.5f) };
            Controls.Add(lblEmpty);
        }

        var btnAssign = new GlassButton { Text = "Assign Selected", Left = 16, Top = 340, Width = 130, Height = 30 };
        Theme.StyleButton(btnAssign, primary: true);
        btnAssign.Click += (_, _) => AssignFromList();
        Controls.Add(btnAssign);

        var btnBrowse = new GlassButton { Text = "Browse for file...", Left = 154, Top = 340, Width = 130, Height = 30 };
        btnBrowse.Click += (_, _) => BrowseForFile();
        Controls.Add(btnBrowse);

        var btnTrim = new GlassButton { Text = "Trim...", Left = 292, Top = 340, Width = 116, Height = 30 };
        btnTrim.Enabled = !string.IsNullOrWhiteSpace(_entry.AudioFile);
        btnTrim.Click += (_, _) => { RequestTrim = true; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btnTrim);

        var btnClear = new GlassButton { Text = "Clear Assignment", Left = 16, Top = 378, Width = 160, Height = 28 };
        btnClear.Enabled = !string.IsNullOrWhiteSpace(_entry.AudioFile);
        btnClear.Click += (_, _) => { RequestClear = true; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btnClear);

        var btnCancel = new GlassButton { Text = "Cancel", Left = 332, Top = 378, Width = 76, Height = 28 };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);
    }

    void AssignFromList()
    {
        if (_lstTracks.SelectedItem is not TrackItem item) return;
        AssignedPath = item.Path;
        DialogResult = DialogResult.OK;
        Close();
    }

    void BrowseForFile()
    {
        Directory.CreateDirectory(ConfigStore.SongsFolder);
        using var ofd = new OpenFileDialog
        {
            Filter = "Audio files (*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac)|*.mp3;*.wav;*.wma;*.m4a;*.aiff;*.flac|All files (*.*)|*.*",
            Title = $"Choose song for: {_entry.Event}",
            InitialDirectory = !string.IsNullOrWhiteSpace(_entry.AudioFile) && File.Exists(_entry.AudioFile)
                ? Path.GetDirectoryName(_entry.AudioFile)
                : ConfigStore.SongsFolder,
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            AssignedPath = ofd.FileName;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    sealed record TrackItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
}
