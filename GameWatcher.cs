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
    /// <summary>Fires when the down/distance ribbon's background color flips between the home
    /// team's color / the away team's color / neutral (black, e.g. kickoff) -- confirmed via
    /// live screenshots that this same ribbon (the "down" region below) fills with whichever
    /// team currently has the ball, not just down/distance text. "home"/"away"/null,
    /// edge-triggered like DownChanged.</summary>
    public event Action<string?>? PossessionChanged;
    public event Action<string>? Log;

    /// <summary>Lets the host resolve a sampled ribbon color to "home"/"away"/null (host owns
    /// the home/away team color table via ConfigStore/TeamColors, set from the Matchup picker)
    /// without GameWatcher depending on those types directly. Null delegate or null result ->
    /// possession never fires.</summary>
    public Func<Color, string?>? ResolveTeamColor;

    /// <summary>Which named crop-position preset (see ScorebugPreset.cs) is currently applied
    /// to the down/situation/quarter band and the possession-color box. Setting this re-applies
    /// the new preset's fractions to the live regions immediately, so a change takes effect on
    /// the very next poll without needing a restart.</summary>
    ScorebugPreset _activePreset = ScorebugPreset.KamsCbsScorebug;
    public ScorebugPreset ActivePreset
    {
        get => _activePreset;
        set { _activePreset = value; ApplyScorebugPreset(value); }
    }

    void ApplyScorebugPreset(ScorebugPreset preset)
    {
        foreach (var region in _regions)
        {
            if (region.Name is "down" or "situation" or "quarter")
            {
                region.FxY = preset.BandFxY;
                region.FxH = preset.BandFxH;
            }
        }
    }

    /// <summary>Fires when the down/distance ribbon shows a negative distance-to-go (e.g.
    /// "3rd & -4") -- confirmed via a live screenshot that the ribbon reads down and distance
    /// together as one string ("3rd & 7"), so no new OCR region was needed, just a wider regex
    /// on the same "down" crop already in use. Side-agnostic -- the host attributes it to
    /// whichever side is NOT the current possession color, since a negative distance means the
    /// offense (the possession side) just lost yards.</summary>
    public event Action? TackleForLossDetected;
    static readonly Regex DistancePattern = new(@"&\s*(-?\d+)", RegexOptions.IgnoreCase);
    string? _lastDistanceRaw;
    DateTime _lossCooldownUntil;

    string? _lastPossession;
    DateTime _possessionCooldownUntil;

    /// <summary>Minimum time between fires for the SAME region, guarding against a
    /// flickery OCR read (e.g. "2nd" -&gt; blank -&gt; "2nd" within one second) spam-firing
    /// the same trigger repeatedly. Exposed in the UI's Settings panel, so not readonly.</summary>
    public static TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    // See the pause/unpause re-fire fix in RunAsync below -- these regions only clear their
    // "Last" value (re-arming them to fire again) when the down/distance region actually
    // changes, not just whenever their own OCR read goes blank.
    static readonly HashSet<string> EventGatedRegions = new(StringComparer.OrdinalIgnoreCase) { "situation", "banner", "quarter" };
    bool _downChangedThisTick;

    readonly List<WatchedRegion> _regions = new()
    {
        // Spans the FULL WIDTH of the bottom score-bug band rather than one tight box, because
        // the college football broadcast rotates between several overlay skins (CBS/ABC/FOX/
        // ESPN) that each place the down/distance text at a different X position along that
        // same bottom strip. Widening horizontally (instead of calibrating one skin's exact
        // box) means any of them still gets caught. Vertical band widened slightly too as a
        // margin of safety across skins with slightly different bug heights/positions.
        // NOTE: possession-color sampling does NOT use this crop -- see PossessionCropRect,
        // which keeps the original tight box so widening this one doesn't wash out the color
        // read with background/crowd pixels.
        // Requires "&" right after the ordinal (e.g. "3rd & 7") -- the down/distance combo is
        // the ONLY place that pattern renders in this bug, which disambiguates it from the
        // quarter indicator below now that both share the same full-width capture band.
        new WatchedRegion
        {
            Name = "down",
            FxX = 0, FxY = 0.83, FxW = 1.0, FxH = 0.14,
            Pattern = new Regex(@"\b(1st|2nd|3rd|4th)\b(?=\s*&)", RegexOptions.IgnoreCase),
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
        // Same crop box as "down" -- confirmed via live screenshots that the scorebug's
        // rightmost segment cycles through down/distance AND these situational states,
        // just with a different background color per state. TOUCHDOWN is included on a
        // hunch but hasn't been confirmed to appear in this small box (it may only show
        // in the separate full-screen banner below) -- watch the log line for it in a
        // real game and drop it here if it never fires.
        new WatchedRegion
        {
            Name = "situation",
            FxX = 0, FxY = 0.83, FxW = 1.0, FxH = 0.14,
            Pattern = new Regex(@"\b(KICKOFF|PAT\s*GOOD|TOUCHDOWN|INTERCEPTED|FUMBLE|TURNOVER)\b", RegexOptions.IgnoreCase),
        },
        // Quarter indicator -- reads the HUD's quarter number (sits between the score and the
        // game clock in the bottom scorebug, e.g. "1st | 5:11 | -- | KICKOFF") so we can
        // edge-trigger "Other: Start of 4th Quarter". Shares the same full-width band as
        // "down"/"situation" above for the same broadcast-skin-independence reason -- the
        // quarter text lives in the same score-bug row, just at a different X per skin.
        // Negative lookahead excludes an ordinal followed by "&" so this never matches the
        // down/distance combo instead (see "down" above) -- reading order in the bug always
        // puts the quarter text before down/distance, so the first non-"&" ordinal found here
        // is reliably the quarter, not a down.
        new WatchedRegion
        {
            Name = "quarter",
            FxX = 0, FxY = 0.83, FxW = 1.0, FxH = 0.14,
            Pattern = new Regex(@"\b(1st|2nd|3rd|4th)\b(?!\s*&)", RegexOptions.IgnoreCase),
        },
        // The big full-screen scoring banner (e.g. "TOUCHDOWN") -- a wide white ribbon
        // across the middle-bottom of the screen, NOT the small persistent scorebug.
        // NOT calibrated yet (FxW/FxH left at 0). Grab a live screenshot at the moment
        // it appears and fill in the fractions the same way "down" was calibrated.
        new WatchedRegion
        {
            Name = "banner",
            FxX = 0, FxY = 0, FxW = 0, FxH = 0,
            Pattern = new Regex(@"\b(TOUCHDOWN|FIELD GOAL|SAFETY)\b", RegexOptions.IgnoreCase),
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
                    // Clamp to at least 1px -- a tiny/minimized game window (or a preset with a
                    // very small fractional height) can round FxW/FxH down to 0, and a 0x0
                    // Bitmap throws ArgumentException, which would otherwise trip the outer
                    // catch every single poll tick until the window is resized.
                    int cropW = Math.Max(1, (int)(winW * region.FxW));
                    int cropH = Math.Max(1, (int)(winH * region.FxH));

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

                    if (region.Name == "down")
                    {
                        SamplePossessionFromWindow(rect, winW, winH);
                        CheckForLossOfYards(text);
                    }

                    var match = region.Pattern.Match(text);
                    string? currentValue = match.Success ? NormalizeMatch(region.Name, match.Value) : null;

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
                            if (region.Name == "down") { DownChanged?.Invoke(currentValue); _downChangedThisTick = true; }
                        }
                    }
                    else if (currentValue == null && !EventGatedRegions.Contains(region.Name))
                    {
                        // Banner/HUD text cleared -- reset so the SAME value can re-trigger
                        // next time it appears (e.g. a second flag later in the game). NOT done
                        // for situation/banner/quarter below -- see EventGatedRegions.
                        region.Last = null;
                    }
                }

                // situation/banner/quarter deliberately do NOT reset on blank OCR the way other
                // regions do (see EventGatedRegions below) -- pausing the game covers the whole
                // HUD, so on unpause the exact same "touchdown"/etc. text reappears and, without
                // this gate, reads as a brand new event and re-fires the sound. Gating the reset
                // on an actual down/distance change instead (a real new snap) means a pause that
                // doesn't span a full play can never cause a re-fire, while a real next score
                // (which always involves at least one down change first -- a new drive/kickoff)
                // still re-arms normally.
                if (_downChangedThisTick)
                {
                    foreach (var region in _regions)
                        if (EventGatedRegions.Contains(region.Name)) region.Last = null;
                    _downChangedThisTick = false;
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

    /// <summary>Reads the average background color of the down/distance ribbon and resolves it
    /// to "home"/"away"/null via ResolveTeamColor, edge-triggering PossessionChanged the same
    /// way OCR'd regions do (with the same Cooldown, to avoid flicker firing on a single bad
    /// frame). Averaging the whole crop (not one sample pixel) means the mostly-solid-color
    /// background dominates even with the down/distance digits drawn on top.
    ///
    /// Deliberately NOT reusing the (now full-width) "down" region's bitmap -- that crop was
    /// widened for broadcast-skin-independent text OCR (see the "down" WatchedRegion comment
    /// above) and would wash out the color average with crowd/background pixels outside the
    /// actual ribbon. Possession color sampling stays on this original tight box, calibrated
    /// against the CBS Sports skin; if the ribbon color itself needs skin-independence too,
    /// that's a separate, harder problem (would need locating the ribbon dynamically, not just
    /// widening a crop) -- flag it if this stops matching on a different broadcast skin.</summary>
    void SamplePossessionFromWindow(Native.RECT rect, int winW, int winH)
    {
        int cropX = rect.Left + (int)(winW * _activePreset.PossessionFxX);
        int cropY = rect.Top + (int)(winH * _activePreset.PossessionFxY);
        int cropW = Math.Max(1, (int)(winW * _activePreset.PossessionFxW));
        int cropH = Math.Max(1, (int)(winH * _activePreset.PossessionFxH));

        using var bmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(cropX, cropY, 0, 0, new Size(cropW, cropH));
        }
        SamplePossession(bmp);
    }

    void SamplePossession(Bitmap bmp)
    {
        if (ResolveTeamColor == null) return;

        long r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < bmp.Height; y += 2)
        for (int x = 0; x < bmp.Width; x += 2)
        {
            var px = bmp.GetPixel(x, y);
            r += px.R; g += px.G; b += px.B;
            n++;
        }
        if (n == 0) return;
        var avg = Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));

        string? side = ResolveTeamColor(avg);

        if (side != _lastPossession)
        {
            _lastPossession = side;
            if (side != null)
            {
                if (DateTime.UtcNow < _possessionCooldownUntil)
                {
                    Log?.Invoke($"[possession] suppressed re-fire of \"{side}\" (cooldown)");
                }
                else
                {
                    _possessionCooldownUntil = DateTime.UtcNow + Cooldown;
                    Log?.Invoke($"[possession] now: {side}");
                    PossessionChanged?.Invoke(side);
                }
            }
        }
    }

    /// <summary>Reads the distance-to-go out of the SAME "down" crop already OCR'd this pass
    /// (e.g. "3rd &amp; -4") and edge-triggers TackleForLossDetected when it goes negative --
    /// confirmed via live screenshot that down+distance render as one string, so no separate
    /// region/calibration was needed.</summary>
    void CheckForLossOfYards(string text)
    {
        var match = DistancePattern.Match(text);
        string? distanceRaw = match.Success ? match.Groups[1].Value : null;
        if (distanceRaw == _lastDistanceRaw) return;
        _lastDistanceRaw = distanceRaw;
        if (distanceRaw == null) return;

        if (int.TryParse(distanceRaw, out int distance) && distance < 0)
        {
            if (DateTime.UtcNow < _lossCooldownUntil)
            {
                Log?.Invoke($"[loss] suppressed re-fire of \"{distanceRaw}\" (cooldown)");
                return;
            }
            _lossCooldownUntil = DateTime.UtcNow + Cooldown;
            Log?.Invoke($"[loss] tackle for loss detected (& {distanceRaw})");
            TackleForLossDetected?.Invoke();
        }
    }

    /// <summary>Collapses OCR-noisy variants ("PATGOOD", "PAT  GOOD") of "situation"/"banner"
    /// matches down to a stable key used in triggers.json (situation:pat_good, etc).
    /// "down"/"flag" matches pass through as plain lowercase, unchanged from before.</summary>
    static string NormalizeMatch(string regionName, string rawMatch)
    {
        string collapsed = Regex.Replace(rawMatch, @"\s+", " ").Trim().ToLowerInvariant();
        if (regionName != "situation" && regionName != "banner") return collapsed;

        return collapsed switch
        {
            "intercepted" or "fumble" or "turnover" => "turnover",
            "field goal" => "fieldgoal",
            _ => collapsed.Replace(" ", "_"),
        };
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
