using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SupremeStadiumSoundSelector;

/// <summary>Trim a song to a start/end range, preview the trimmed range, and save it as a
/// new clipped file (leaves the original untouched -- the trimmed copy is what gets
/// assigned back to the trigger row).</summary>
internal sealed class TrimmerForm : Form
{
    readonly string _sourcePath;
    readonly TimeSpan _totalDuration;

    TrackBar _sldStart = null!;
    TrackBar _sldEnd = null!;
    Label _lblStartValue = null!;
    Label _lblEndValue = null!;
    Label _lblDuration = null!;
    GlassButton _btnPreview = null!;
    GlassButton _btnStop = null!;
    GlassButton _btnSave = null!;

    WaveOutEvent? _previewOutput;

    public string? SavedFilePath { get; private set; }

    public TrimmerForm(IWin32Window owner, string sourcePath)
    {
        _sourcePath = sourcePath;

        using (var probe = new AudioFileReader(sourcePath))
            _totalDuration = probe.TotalTime;

        Text = $"Trim: {Path.GetFileName(sourcePath)}";
        Width = 420;
        Height = 320;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.TextPrimary;

        BuildUi();
        FormClosing += (_, _) => StopPreview();
    }

    void BuildUi()
    {
        var lblFile = new Label { Text = Path.GetFileName(_sourcePath), Left = 12, Top = 12, Width = 380, Height = 20, ForeColor = Theme.TextPrimary, Font = new Font(Font, FontStyle.Bold) };
        _lblDuration = new Label { Text = $"Full length: {_totalDuration:mm\\:ss\\.ff}", Left = 12, Top = 34, Width = 380, Height = 18, ForeColor = Theme.TextSecondary };

        // Tenths-of-a-second resolution, same slider-drag feel as the header's Volume control
        // (this replaces the old start/end NumericUpDown pair per user request).
        int maxTenths = (int)(_totalDuration.TotalSeconds * 10);

        var lblStart = new Label { Text = "Clip start", Left = 12, Top = 66, Width = 90, Height = 18, ForeColor = Theme.TextSecondary };
        _sldStart = new TrackBar { Left = 12, Top = 84, Width = 300, Minimum = 0, Maximum = maxTenths, Value = 0, TickStyle = TickStyle.None };
        _lblStartValue = new Label { Left = 318, Top = 84, Width = 80, Height = 22, ForeColor = Theme.TextPrimary };

        var lblEnd = new Label { Text = "Clip end", Left = 12, Top = 128, Width = 90, Height = 18, ForeColor = Theme.TextSecondary };
        _sldEnd = new TrackBar { Left = 12, Top = 146, Width = 300, Minimum = 0, Maximum = maxTenths, Value = Math.Min(maxTenths, 150), TickStyle = TickStyle.None };
        _lblEndValue = new Label { Left = 318, Top = 146, Width = 80, Height = 22, ForeColor = Theme.TextPrimary };

        _sldStart.ValueChanged += (_, _) => { if (_sldStart.Value >= _sldEnd.Value) _sldStart.Value = Math.Max(0, _sldEnd.Value - 1); UpdateSliderLabels(); };
        _sldEnd.ValueChanged += (_, _) => { if (_sldEnd.Value <= _sldStart.Value) _sldEnd.Value = Math.Min(maxTenths, _sldStart.Value + 1); UpdateSliderLabels(); };
        UpdateSliderLabels();

        _btnPreview = new GlassButton { Text = "Preview Trim", Left = 12, Top = 190, Width = 120, Height = 32 };
        _btnPreview.Click += (_, _) => PlayPreview();

        _btnStop = new GlassButton { Text = "Stop", Left = 140, Top = 190, Width = 90, Height = 32 };
        _btnStop.Click += (_, _) => StopPreview();

        var btnCancel = new GlassButton { Text = "Cancel", Left = 305, Top = 250, Width = 100, Height = 32 };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnSave = new GlassButton { Text = "Save Trimmed Copy", Left = 12, Top = 250, Width = 180, Height = 32 };
        Theme.StyleButton(_btnSave, primary: true);
        _btnSave.Click += (_, _) => SaveTrimmed();

        Controls.Add(lblFile);
        Controls.Add(_lblDuration);
        Controls.Add(lblStart);
        Controls.Add(_sldStart);
        Controls.Add(_lblStartValue);
        Controls.Add(lblEnd);
        Controls.Add(_sldEnd);
        Controls.Add(_lblEndValue);
        Controls.Add(_btnPreview);
        Controls.Add(_btnStop);
        Controls.Add(_btnSave);
        Controls.Add(btnCancel);
    }

    void UpdateSliderLabels()
    {
        _lblStartValue.Text = $"{_sldStart.Value / 10.0:0.0}s";
        _lblEndValue.Text = $"{_sldEnd.Value / 10.0:0.0}s";
    }

    (TimeSpan start, TimeSpan end)? GetRange()
    {
        var start = TimeSpan.FromSeconds(_sldStart.Value / 10.0);
        var end = TimeSpan.FromSeconds(_sldEnd.Value / 10.0);
        if (end <= start)
        {
            MessageBox.Show(this, "End must be after start.", "Trim", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        return (start, end);
    }

    void PlayPreview()
    {
        var range = GetRange();
        if (range == null) return;
        var (start, end) = range.Value;

        StopPreview();

        var reader = new AudioFileReader(_sourcePath) { Volume = AudioPlayer.MasterVolume };
        var offset = new OffsetSampleProvider(reader)
        {
            SkipOver = start,
            Take = end - start,
        };

        _previewOutput = new WaveOutEvent();
        _previewOutput.Init(offset.ToWaveProvider());
        _previewOutput.PlaybackStopped += (_, _) => { reader.Dispose(); };
        _previewOutput.Play();
    }

    void StopPreview()
    {
        _previewOutput?.Stop();
        _previewOutput?.Dispose();
        _previewOutput = null;
    }

    void SaveTrimmed()
    {
        var range = GetRange();
        if (range == null) return;
        var (start, end) = range.Value;

        StopPreview();

        Directory.CreateDirectory(ConfigStore.SongsFolder);
        string baseName = Path.GetFileNameWithoutExtension(_sourcePath);
        string outPath = Path.Combine(ConfigStore.SongsFolder, $"{baseName}_trim.wav");
        int n = 1;
        while (File.Exists(outPath))
            outPath = Path.Combine(ConfigStore.SongsFolder, $"{baseName}_trim{++n}.wav");

        try
        {
            using var reader = new AudioFileReader(_sourcePath);
            var offset = new OffsetSampleProvider(reader)
            {
                SkipOver = start,
                Take = end - start,
            };
            WaveFileWriter.CreateWaveFile16(outPath, offset);

            SavedFilePath = outPath;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save trimmed file: {ex.Message}", "Trim", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
