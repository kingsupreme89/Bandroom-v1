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
    readonly string _currentPath;
    readonly string _dialogTitle;
    ListBox _lstTracks = null!;
    Label _lblCurrent = null!;
    Label _lblPreviewing = null!;
    GlassButton _btnPlay = null!;
    GlassButton _btnStop = null!;

    public string? AssignedPath { get; private set; }
    public bool RequestTrim { get; private set; }
    public bool RequestClear { get; private set; }

    /// <summary>currentPath/dialogTitle are passed explicitly (not read from entry.AudioFile)
    /// so this same dialog can assign either the main song (AudioFile) or the PA Announcer clip
    /// (PaAudioFile) for the same TriggerEntry -- the caller decides which field it's editing and
    /// writes the result back to the right one; this form only ever reads _entry for the Event
    /// label shown in the subtitle.</summary>
    public AssignTrackForm(IWin32Window owner, TriggerEntry entry, IEnumerable<string> libraryPaths, string currentPath = "", string dialogTitle = "Assign Track")
    {
        _entry = entry;
        _currentPath = currentPath;
        _dialogTitle = dialogTitle;

        Text = _dialogTitle;
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
        var lblTitle = new Label { Text = _dialogTitle, Left = 16, Top = 14, AutoSize = true, Font = AppFonts.Get(13, FontStyle.Bold), ForeColor = Theme.TextPrimary };
        Controls.Add(lblTitle);

        var lblSub = new Label { Text = $"for {_entry.Event}", Left = 16, Top = 40, AutoSize = true, Font = AppFonts.Get(9), ForeColor = Theme.TextMuted };
        Controls.Add(lblSub);

        string currentLabel = string.IsNullOrWhiteSpace(_currentPath) ? "(none assigned)" : Path.GetFileNameWithoutExtension(_currentPath);
        _lblCurrent = new Label { Text = $"Current: {currentLabel}", Left = 16, Top = 62, AutoSize = true, Font = AppFonts.Get(8.5f), ForeColor = Theme.TextMuted };
        Controls.Add(_lblCurrent);

        // --- Big preview area on top (owner request: "hard to understand how to select a song" --
        // put the preview front and center instead of buried under a bare list) ---
        var previewPanel = new Panel
        {
            Left = 16, Top = 86, Width = 392, Height = 70,
            BackColor = Theme.InputFill, BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(previewPanel);

        _lblPreviewing = new Label
        {
            Text = "Select a song below to preview it",
            Left = 12, Top = 10, Width = 368, AutoSize = false, Height = 20,
            Font = AppFonts.Get(9.5f, FontStyle.Bold), ForeColor = Theme.TextPrimary,
        };
        previewPanel.Controls.Add(_lblPreviewing);

        var previewBtnFont = AppFonts.Get(9);
        _btnPlay = new GlassButton { Text = "▶ Play", Left = 12, Top = 34, Width = 90, Height = 26, Font = previewBtnFont, Enabled = false };
        _btnPlay.Click += (_, _) => PreviewSelected();
        previewPanel.Controls.Add(_btnPlay);

        _btnStop = new GlassButton { Text = "■ Stop", Left = 108, Top = 34, Width = 90, Height = 26, Font = previewBtnFont, Enabled = false };
        _btnStop.Click += (_, _) => AudioPlayer.StopAll();
        previewPanel.Controls.Add(_btnStop);

        _lstTracks = new ListBox
        {
            Left = 16, Top = 164, Width = 392, Height = 166,
            BackColor = Theme.InputFill, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
        };
        foreach (var path in library) _lstTracks.Items.Add(new TrackItem(path));
        _lstTracks.DoubleClick += (_, _) => AssignFromList();
        _lstTracks.SelectedIndexChanged += (_, _) =>
        {
            bool hasSelection = _lstTracks.SelectedItem is TrackItem;
            _btnPlay.Enabled = hasSelection;
            _btnStop.Enabled = hasSelection;
            _lblPreviewing.Text = _lstTracks.SelectedItem is TrackItem item
                ? $"Selected: {item}"
                : "Select a song below to preview it";
        };
        Controls.Add(_lstTracks);

        if (library.Count == 0)
        {
            var lblEmpty = new Label { Text = "No songs in your library yet — use Browse for file... below.", Left = 20, Top = 200, Width = 360, ForeColor = Theme.TextMuted, Font = AppFonts.Get(8.5f) };
            Controls.Add(lblEmpty);
        }

        var btnFont = AppFonts.Get(9);

        var btnAssign = new GlassButton { Text = "Assign Selected", Left = 16, Top = 340, Width = 130, Height = 30, Font = btnFont };
        Theme.StyleButton(btnAssign, primary: true);
        btnAssign.Click += (_, _) => AssignFromList();
        Controls.Add(btnAssign);

        var btnBrowse = new GlassButton { Text = "Browse for file...", Left = 154, Top = 340, Width = 130, Height = 30, Font = btnFont };
        btnBrowse.Click += (_, _) => BrowseForFile();
        Controls.Add(btnBrowse);

        var btnTrim = new GlassButton { Text = "Trim...", Left = 292, Top = 340, Width = 116, Height = 30, Font = btnFont };
        btnTrim.Enabled = !string.IsNullOrWhiteSpace(_currentPath);
        btnTrim.Click += (_, _) => { RequestTrim = true; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btnTrim);

        var btnClear = new GlassButton { Text = "Clear Assignment", Left = 16, Top = 378, Width = 130, Height = 28, Font = btnFont };
        btnClear.Enabled = !string.IsNullOrWhiteSpace(_currentPath);
        btnClear.Click += (_, _) => { RequestClear = true; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btnClear);

        var btnCancel = new GlassButton { Text = "Cancel", Left = 332, Top = 378, Width = 76, Height = 28, Font = btnFont };
        btnCancel.Click += (_, _) => { AudioPlayer.StopAll(); DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);

        Height = 460 + 76; // grew for the new preview panel
    }

    void PreviewSelected()
    {
        if (_lstTracks.SelectedItem is TrackItem item) AudioPlayer.Play(item.Path);
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
            InitialDirectory = !string.IsNullOrWhiteSpace(_currentPath) && File.Exists(_currentPath)
                ? Path.GetDirectoryName(_currentPath)
                : ConfigStore.SongsFolder,
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            AssignedPath = ConfigStore.ImportIntoSongsLibrary(ofd.FileName) ?? ofd.FileName;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    sealed record TrackItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
}
