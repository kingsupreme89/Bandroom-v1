using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SupremeStadiumSoundSelector;

/// <summary>Trim a song to a start/end range, preview the trimmed range, and save it as a
/// new clipped file (leaves the original untouched -- the trimmed copy is what gets
/// assigned back to the trigger row). Includes waveform visualization and prompts for
/// team/song name on save.</summary>
internal sealed class TrimmerForm : Form
{
    readonly string _sourcePath;
    readonly TimeSpan _totalDuration;
    Bitmap? _waveformBitmap; // rendered once off the UI thread; Paint just blits it + the two marker lines

    TrackBar _sldStart = null!;
    TrackBar _sldEnd = null!;
    NumericUpDown _numStart = null!;
    NumericUpDown _numEnd = null!;
    Label _lblDuration = null!;
    GlassButton _btnPreview = null!;
    GlassButton _btnStop = null!;
    GlassButton _btnSave = null!;
    DoubleBufferedPanel _waveformPanel = null!;

    /// <summary>Plain Panel isn't double-buffered by default -- dragging the trim sliders was
    /// re-painting flicker-prone, tearing frames since every ValueChanged tick invalidated it.</summary>
    sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() =>
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    WaveOutEvent? _previewOutput;

    public string? SavedFilePath { get; private set; }

    public TrimmerForm(IWin32Window owner, string sourcePath)
    {
        _sourcePath = sourcePath;

        using (var probe = new AudioFileReader(sourcePath))
            _totalDuration = probe.TotalTime;

        Text = $"Trim: {Path.GetFileName(sourcePath)}";
        // ClientSize (not Width/Height) -- Height includes the OS title bar, whose actual
        // pixel height varies by Windows version/DPI, which is exactly what clipped the
        // bottom button row before: content was laid out assuming a client area taller than
        // what FixedDialog actually left available.
        ClientSize = new Size(700, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.TextPrimary;

        BuildUi();
        FormClosing += (_, _) => { StopPreview(); _waveformBitmap?.Dispose(); };

        // Load waveform async to keep UI responsive
        Task.Run(LoadWaveformData);
    }

    void BuildUi()
    {
        var fontBold = AppFonts.Get(11, FontStyle.Bold);
        var fontRegular = AppFonts.Get(11);
        var fontSmall = AppFonts.Get(10);

        var lblFile = new Label { Text = Path.GetFileName(_sourcePath), Left = 12, Top = 12, Width = 660, Height = 20, ForeColor = Theme.TextPrimary, Font = fontBold };
        _lblDuration = new Label { Text = $"Full length: {_totalDuration:mm\\:ss\\.ff}", Left = 12, Top = 34, Width = 660, Height = 18, ForeColor = Theme.TextSecondary, Font = fontSmall };

        // Waveform display panel (interactive)
        _waveformPanel = new DoubleBufferedPanel
        {
            Left = 12,
            Top = 60,
            Width = 660,
            Height = 120,
            BackColor = Theme.TileFillSmall,
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            ForeColor = Theme.Border
        };
        _waveformPanel.Paint += WaveformPanel_Paint;
        _waveformPanel.MouseDown += WaveformPanel_MouseDown;
        _waveformPanel.MouseMove += WaveformPanel_MouseMove;
        _waveformPanel.MouseUp += (_, _) => _draggingHandle = null;

        int maxTenths = (int)(_totalDuration.TotalSeconds * 10);

        var lblStart = new Label { Text = "Clip start", Left = 12, Top = 190, Width = 90, Height = 18, ForeColor = Theme.TextSecondary, Font = fontSmall };
        _sldStart = new TrackBar { Left = 12, Top = 208, Width = 460, Minimum = 0, Maximum = maxTenths, Value = 0, TickStyle = TickStyle.None };
        _numStart = new NumericUpDown
        {
            Left = 480, Top = 208, Width = 80, Height = 22, Font = fontSmall,
            DecimalPlaces = 1, Increment = 0.1m, Minimum = 0, Maximum = maxTenths / 10m,
        };

        var lblEnd = new Label { Text = "Clip end", Left = 12, Top = 242, Width = 90, Height = 18, ForeColor = Theme.TextSecondary, Font = fontSmall };
        _sldEnd = new TrackBar { Left = 12, Top = 260, Width = 460, Minimum = 0, Maximum = maxTenths, Value = Math.Min(maxTenths, 150), TickStyle = TickStyle.None };
        _numEnd = new NumericUpDown
        {
            Left = 480, Top = 260, Width = 80, Height = 22, Font = fontSmall,
            DecimalPlaces = 1, Increment = 0.1m, Minimum = 0, Maximum = maxTenths / 10m,
        };

        // Slider drag and numeric entry stay in sync both ways -- whichever one the user
        // touches last wins, guarding against re-entrant events (slider change updates the
        // numeric box, which would otherwise fire its own change and re-touch the slider).
        bool syncing = false;
        _sldStart.ValueChanged += (_, _) =>
        {
            if (_sldStart.Value >= _sldEnd.Value) _sldStart.Value = Math.Max(0, _sldEnd.Value - 1);
            if (!syncing) { syncing = true; _numStart.Value = _sldStart.Value / 10m; syncing = false; }
            _waveformPanel.Invalidate();
        };
        _sldEnd.ValueChanged += (_, _) =>
        {
            if (_sldEnd.Value <= _sldStart.Value) _sldEnd.Value = Math.Min(maxTenths, _sldStart.Value + 1);
            if (!syncing) { syncing = true; _numEnd.Value = _sldEnd.Value / 10m; syncing = false; }
            _waveformPanel.Invalidate();
        };
        _numStart.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true; _sldStart.Value = Math.Clamp((int)(_numStart.Value * 10), 0, maxTenths); syncing = false;
        };
        _numEnd.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true; _sldEnd.Value = Math.Clamp((int)(_numEnd.Value * 10), 0, maxTenths); syncing = false;
        };
        _numStart.Value = _sldStart.Value / 10m;
        _numEnd.Value = _sldEnd.Value / 10m;

        var btnFont = AppFonts.Get(9);

        _btnPreview = new GlassButton { Text = "Preview Trim", Left = 12, Top = 296, Width = 120, Height = 32, Font = btnFont };
        _btnPreview.Click += (_, _) => PlayPreview();

        _btnStop = new GlassButton { Text = "Stop", Left = 140, Top = 296, Width = 90, Height = 32, Font = btnFont };
        _btnStop.Click += (_, _) => StopPreview();

        var btnCancel = new GlassButton { Text = "Cancel", Left = 572, Top = 344, Width = 100, Height = 32, Font = btnFont };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnSave = new GlassButton { Text = "Save & Name", Left = 12, Top = 344, Width = 180, Height = 32, Font = btnFont };
        Theme.StyleButton(_btnSave, primary: true);
        _btnSave.Click += (_, _) => SaveTrimmed();

        Controls.Add(lblFile);
        Controls.Add(_lblDuration);
        Controls.Add(_waveformPanel);
        Controls.Add(lblStart);
        Controls.Add(_sldStart);
        Controls.Add(_numStart);
        Controls.Add(lblEnd);
        Controls.Add(_sldEnd);
        Controls.Add(_numEnd);
        Controls.Add(_btnPreview);
        Controls.Add(_btnStop);
        Controls.Add(_btnSave);
        Controls.Add(btnCancel);
    }

    void LoadWaveformData()
    {
        try
        {
            var data = WaveformRenderer.GetWaveformData(_sourcePath, _waveformPanel.Width - 2, _waveformPanel.Height - 2);

            // Render the waveform to an offscreen bitmap ONCE here (background thread) so Paint
            // never has to redraw ~660 columns of lines on every slider tick -- it just blits
            // this bitmap plus the two marker lines, which is what was causing the drag lag.
            var bmp = new Bitmap(Math.Max(1, _waveformPanel.Width), Math.Max(1, _waveformPanel.Height));
            using (var g = Graphics.FromImage(bmp))
            {
                var bounds = new Rectangle(1, 1, _waveformPanel.Width - 2, _waveformPanel.Height - 2);
                WaveformRenderer.DrawWaveform(g, data, bounds, Theme.AccentBright, _waveformPanel.BackColor);
            }

            _waveformPanel.Invoke(() =>
            {
                _waveformBitmap?.Dispose();
                _waveformBitmap = bmp;
                _waveformPanel.Invalidate();
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("Waveform load error", ex);
        }
    }

    void WaveformPanel_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(_waveformPanel.BackColor);

        if (_waveformBitmap == null) return;

        e.Graphics.DrawImageUnscaled(_waveformBitmap, 0, 0);

        // Draw start/end markers on waveform
        int maxTenths = (int)(_totalDuration.TotalSeconds * 10);
        if (maxTenths > 0)
        {
            int boundsWidth = _waveformPanel.Width - 2;
            float startX = 1 + (_sldStart.Value / (float)maxTenths) * boundsWidth;
            float endX = 1 + (_sldEnd.Value / (float)maxTenths) * boundsWidth;

            using var penStart = new Pen(Theme.Success, 2);
            using var penEnd = new Pen(Theme.Warning, 2);
            e.Graphics.DrawLine(penStart, startX, 1, startX, _waveformPanel.Height - 1);
            e.Graphics.DrawLine(penEnd, endX, 1, endX, _waveformPanel.Height - 1);
        }
    }

    string? _draggingHandle = null;

    void WaveformPanel_MouseDown(object? sender, MouseEventArgs e)
    {
        int maxTenths = (int)(_totalDuration.TotalSeconds * 10);
        if (maxTenths == 0) return;

        float startX = 1 + (_sldStart.Value / (float)maxTenths) * (_waveformPanel.Width - 2);
        float endX = 1 + (_sldEnd.Value / (float)maxTenths) * (_waveformPanel.Width - 2);

        if (Math.Abs(e.X - startX) < 8)
            _draggingHandle = "start";
        else if (Math.Abs(e.X - endX) < 8)
            _draggingHandle = "end";
    }

    void WaveformPanel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingHandle == null) return;

        int maxTenths = (int)(_totalDuration.TotalSeconds * 10);
        if (maxTenths == 0) return;

        float ratio = (e.X - 1) / (float)(_waveformPanel.Width - 2);
        ratio = Math.Max(0, Math.Min(1, ratio));
        int newValue = (int)(ratio * maxTenths);

        if (_draggingHandle == "start")
            _sldStart.Value = newValue;
        else if (_draggingHandle == "end")
            _sldEnd.Value = newValue;
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

        // Prompt for team name
        var team = PromptDialog.Show(this, "Team", "Enter team name (e.g., UGA, LSU):");
        if (string.IsNullOrWhiteSpace(team)) return;

        // Prompt for song name
        var songName = PromptDialog.Show(this, "Song Name", "Enter song name or trigger description:");
        if (string.IsNullOrWhiteSpace(songName)) return;

        Directory.CreateDirectory(ConfigStore.SongsTrimmedFolder);

        // Normalize to safe filename: Team-SongName.wav
        string safeSongName = System.Text.RegularExpressions.Regex.Replace(songName, @"[^\w\s-]", "").Replace(" ", "_");
        string safeTeam = System.Text.RegularExpressions.Regex.Replace(team, @"[^\w-]", "");
        string outPath = Path.Combine(ConfigStore.SongsTrimmedFolder, $"{safeTeam}-{safeSongName}.wav");

        // Handle duplicates
        int n = 1;
        while (File.Exists(outPath))
            outPath = Path.Combine(ConfigStore.SongsTrimmedFolder, $"{safeTeam}-{safeSongName}_{n++}.wav");

        try
        {
            using var reader = new AudioFileReader(_sourcePath);
            var offset = new OffsetSampleProvider(reader)
            {
                SkipOver = start,
                Take = end - start,
            };
            var normalized = NormalizeAndLimit(offset);
            WaveFileWriter.CreateWaveFile16(outPath, normalized);

            SavedFilePath = outPath;
            MessageBox.Show(this, $"Saved: {Path.GetFileName(outPath)}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save trimmed file: {ex.Message}", "Trim", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // -18 dBFS RMS is a common "feels consistently loud without pinning the meter" target for
    // short game-day clips -- explicit user ask was "songs all have the same volume", which RMS
    // (perceived loudness) fixes far better than peak normalization would (two clips can share
    // the same peak level and still sound wildly different in loudness).
    const float TargetRmsDb = -18f;
    // -1 dBFS true-peak ceiling leaves headroom so the limiter below only ever has to catch rare
    // transients, not do heavy lifting every clip.
    const float CeilingDb = -1f;

    static float DbToLinear(float db) => (float)Math.Pow(10, db / 20.0);

    /// <summary>Reads every sample from <paramref name="source"/>, applies a single RMS-based
    /// makeup-gain pass so all clips land at the same perceived loudness, then soft-limits
    /// (tanh knee, not a hard clamp) anything that would exceed the ceiling so a boosted
    /// transient never produces audible digital clipping.</summary>
    static ISampleProvider NormalizeAndLimit(ISampleProvider source)
    {
        var samples = new List<float>();
        var buf = new float[source.WaveFormat.SampleRate * source.WaveFormat.Channels];
        int read;
        while ((read = source.Read(buf, 0, buf.Length)) > 0)
            samples.AddRange(new ArraySegment<float>(buf, 0, read));

        if (samples.Count == 0)
            return new FloatArraySampleProvider(Array.Empty<float>(), source.WaveFormat);

        double sumSquares = 0;
        foreach (var s in samples) sumSquares += (double)s * s;
        float rms = (float)Math.Sqrt(sumSquares / samples.Count);

        float targetRms = DbToLinear(TargetRmsDb);
        float ceiling = DbToLinear(CeilingDb);
        // Silence/near-silence: skip gain (would otherwise blow up toward infinity) and just
        // pass the (already near-zero) samples through the limiter stage untouched.
        float gain = rms > 0.0001f ? targetRms / rms : 1f;

        var outArr = new float[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            float v = samples[i] * gain;
            float mag = Math.Abs(v);
            if (mag > ceiling)
            {
                float over = (mag - ceiling) / (1f - ceiling);
                mag = ceiling + (1f - ceiling) * (float)Math.Tanh(over);
                v = Math.Sign(v) * mag;
            }
            outArr[i] = v;
        }
        return new FloatArraySampleProvider(outArr, source.WaveFormat);
    }

    /// <summary>Minimal ISampleProvider wrapper over an in-memory float buffer, used to feed the
    /// normalized/limited samples back into WaveFileWriter.</summary>
    sealed class FloatArraySampleProvider : ISampleProvider
    {
        readonly float[] _data;
        int _position;
        public WaveFormat WaveFormat { get; }

        public FloatArraySampleProvider(float[] data, WaveFormat format)
        {
            _data = data;
            WaveFormat = format;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = _data.Length - _position;
            int n = Math.Min(available, count);
            if (n <= 0) return 0;
            Array.Copy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
