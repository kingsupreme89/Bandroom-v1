using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SupremeStadiumSoundSelector;

/// <summary>One OCR'd HUD region: a crop box (as fractions of the window rect) plus a
/// regex to pull the value out. Uncalibrated regions (FxW == 0) are skipped entirely.</summary>
internal sealed class WatchedRegion
{
    public required string Name;
    public double FxX, FxY, FxW, FxH;
    public required Regex Pattern;
    public string? Last;
    public string? LastRawText;
    public DateTime CooldownUntil;
    public bool Calibrated => FxW > 0 && FxH > 0;
}

internal sealed class GameWatcher
{
    public event Action<bool>? WindowFoundChanged;
    public event Action<string?>? DownChanged;
    /// <summary>Fires for any region (including "down") whenever its OCR'd value changes
    /// to a new non-null value -- edge-triggered, same as DownChanged but generic.</summary>
    public event Action<string, string?>? RegionChanged;
    public event Action<string>? Log;

    /// <summary>Minimum time between fires for the SAME region, guarding against a
    /// flickery OCR read (e.g. "2nd" -&gt; blank -&gt; "2nd" within one second) spam-firing
    /// the same trigger repeatedly. Exposed in the UI's Settings panel, so not readonly.</summary>
    public static TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    readonly List<WatchedRegion> _regions = new()
    {
        // Calibrated against a LIVE gameplay screenshot (bottom-right score bug) --
        // NOT the pause menu, whose scoreboard sits mid-screen in a different spot.
        new WatchedRegion
        {
            Name = "down",
            FxX = 0.65, FxY = 0.85, FxW = 0.14, FxH = 0.09,
            Pattern = new Regex(@"\b(1st|2nd|3rd|4th)\b", RegexOptions.IgnoreCase),
        },
        // Penalty/flag banner -- NOT calibrated yet (FxW/FxH left at 0, so it's skipped).
        // Next time a flag happens in a live game, grab a screenshot of where the banner
        // renders and fill in FxX/FxY/FxW/FxH the same way "down" was calibrated above.
        new WatchedRegion
        {
            Name = "flag",
            FxX = 0, FxY = 0, FxW = 0, FxH = 0,
            Pattern = new Regex(@"\b(FLAG|PENALTY)\b", RegexOptions.IgnoreCase),
        },
    };

    CancellationTokenSource? _cts;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    async Task RunAsync(CancellationToken ct)
    {
        OcrEngine? ocrEngine = null;
        IntPtr hwnd = IntPtr.Zero;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = FindGameWindow();
                    WindowFoundChanged?.Invoke(hwnd != IntPtr.Zero);
                    if (hwnd == IntPtr.Zero)
                    {
                        await Task.Delay(1500, ct);
                        continue;
                    }
                }

                ocrEngine ??= OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? throw new Exception("Could not create OCR engine.");

                if (!Native.GetWindowRect(hwnd, out Native.RECT rect))
                {
                    hwnd = IntPtr.Zero;
                    WindowFoundChanged?.Invoke(false);
                    await Task.Delay(1000, ct);
                    continue;
                }

                int winW = rect.Right - rect.Left;
                int winH = rect.Bottom - rect.Top;

                foreach (var region in _regions)
                {
                    if (!region.Calibrated) continue;

                    int cropX = rect.Left + (int)(winW * region.FxX);
                    int cropY = rect.Top + (int)(winH * region.FxY);
                    int cropW = (int)(winW * region.FxW);
                    int cropH = (int)(winH * region.FxH);

                    using var bmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(cropX, cropY, 0, 0, new Size(cropW, cropH));
                    }

                    string text = await OcrBitmapAsync(ocrEngine, bmp);
                    string trimmedText = text.Trim();
                    if (trimmedText != region.LastRawText)
                    {
                        region.LastRawText = trimmedText;
                        Log?.Invoke($"[{region.Name}] OCR read: \"{trimmedText}\"");
                    }

                    var match = region.Pattern.Match(text);
                    string? currentValue = match.Success ? match.Value.ToLowerInvariant() : null;

                    if (currentValue != null && currentValue != region.Last)
                    {
                        region.Last = currentValue;

                        if (DateTime.UtcNow < region.CooldownUntil)
                        {
                            // Same value re-appeared too soon after last firing it -- almost
                            // always a flickery OCR read (e.g. "2nd" -> blank -> "2nd" inside
                            // one second), not a real second event. Update Last but don't fire.
                            Log?.Invoke($"[{region.Name}] suppressed re-fire of \"{currentValue}\" (cooldown)");
                        }
                        else
                        {
                            region.CooldownUntil = DateTime.UtcNow + Cooldown;
                            RegionChanged?.Invoke(region.Name, currentValue);
                            if (region.Name == "down") DownChanged?.Invoke(currentValue);
                        }
                    }
                    else if (currentValue == null)
                    {
                        // Banner/HUD text cleared -- reset so the SAME value can re-trigger
                        // next time it appears (e.g. a second flag later in the game).
                        region.Last = null;
                    }
                }

                await Task.Delay(400, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Watcher error: {ex.Message}");
                CrashLog.Write("Watcher error", ex);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    static async Task<string> OcrBitmapAsync(OcrEngine engine, Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;

        using var stream = new InMemoryRandomAccessStream();
        using var outputStream = stream.GetOutputStreamAt(0);
        var writer = new DataWriter(outputStream);
        writer.WriteBytes(ms.ToArray());
        await writer.StoreAsync();
        await outputStream.FlushAsync();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text;
    }

    static IntPtr FindGameWindow()
    {
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hWnd, lParam) =>
        {
            int len = Native.GetWindowTextLength(hWnd);
            if (len == 0 || !Native.IsWindowVisible(hWnd)) return true;
            var sb = new System.Text.StringBuilder(len + 1);
            Native.GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().Contains("College Football 27", StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
