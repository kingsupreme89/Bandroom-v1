using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SupremeStadiumSoundSelector;

/// <summary>Thin wrapper around the handful of Win32 calls needed to paint a real per-pixel-alpha
/// bitmap onto a WS_EX_LAYERED window. Requires the source bitmap's alpha to already be
/// PREMULTIPLIED (UpdateLayeredWindow's ULW_ALPHA contract, not plain straight alpha) -- see
/// LayeredBitmapPainter.Paint's own doc comment for where that happens.</summary>
internal static class NativeLayeredWindow
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    const int ULW_ALPHA = 0x00000002;
    const byte AC_SRC_OVER = 0x00;
    const byte AC_SRC_ALPHA = 0x01;
    const int BI_RGB = 0;
    const uint DIB_RGB_COLORS = 0;

    /// <summary>Blits a premultiplied-alpha 32bpp bitmap onto hwnd at screenLocation via
    /// UpdateLayeredWindow -- hwnd must already carry WS_EX_LAYERED (set once via CreateParams;
    /// this call does NOT set that style itself, only paints).</summary>
    public static void Paint(IntPtr hwnd, Bitmap premultiplied, Point screenLocation)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr dib = IntPtr.Zero, oldBitmap = IntPtr.Zero;
        try
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = premultiplied.Width,
                biHeight = -premultiplied.Height, // negative = top-down DIB, matches Bitmap row order
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };
            var info = new BITMAPINFO { bmiHeader = header };
            dib = CreateDIBSection(screenDc, ref info, DIB_RGB_COLORS, out IntPtr dibPixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || dibPixels == IntPtr.Zero) return;

            var locked = premultiplied.LockBits(new Rectangle(0, 0, premultiplied.Width, premultiplied.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int bytes = Math.Abs(locked.Stride) * premultiplied.Height;
                // Both source (GDI+ Format32bppPArgb) and destination (32bpp top-down DIB) use
                // the same BGRA-premultiplied byte layout and row order here, so this is a
                // straight memcpy -- no per-pixel conversion needed.
                unsafe { Buffer.MemoryCopy((void*)locked.Scan0, (void*)dibPixels, bytes, bytes); }
            }
            finally { premultiplied.UnlockBits(locked); }

            oldBitmap = SelectObject(memDc, dib);

            var dst = new POINT { X = screenLocation.X, Y = screenLocation.Y };
            var size = new SIZE { cx = premultiplied.Width, cy = premultiplied.Height };
            var src = new POINT { X = 0, Y = 0 };
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

/// <summary>Owner request 2026-08-13 ("Coffee's bug doesn't pop up on GAMETIME" -- and explicitly
/// NOT a BANDroom reimplementation, "was it gonna be coffees if not then we don't want it"): loads
/// Coffee's OWN theme HTML file (the currently-saved scorebug skin, same file Coffee's Corner's
/// gallery and the Game Settings switcher already reference -- see
/// WebMainForm.ResolveActiveScorebugThemeFile) directly, and drives it with live game data via the
/// theme's own `window.updateScorebug` bridge. That bridge (confirmed by reading the ESPN 2020 /
/// NBC 2024 / NBC 2024 Monochrome / FOX 2025 theme source directly -- `data-cfb27-bind` attributes
/// + a `window.updateScorebug`/`window.CFB27` script block) was built by Coffee for exactly this.
/// FOX 2021 is the one exception -- no live-data hook exists in that file at all, so it just shows
/// its frozen example values; that's a known limitation of that specific bundled asset (see the
/// "Coming Soon" badge on it in Coffee's Corner / the skin switcher), not something fixable here.
///
/// Shown automatically the moment watching starts (GAMETIME or manual Start Watching), hidden on
/// Stop Watching -- see WebMainForm.ShowScorebugOverlay/HideScorebugOverlay. No resize/drag chrome
/// by owner request -- fixed size (from the theme's own authored canvas, scaled to fit the
/// screen) and position, click-through (WS_EX_TRANSPARENT) so it never steals input from the game
/// underneath.
///
/// TRANSPARENCY -- third rewrite, 2026-08-13: first tried WinForms' TransparencyKey color-key
/// trick (never worked -- WebView2 renders through its own hardware compositor straight to DWM,
/// bypassing the parent window's GDI color-keying entirely). Second tried WebView2 "visual
/// hosting" -- CoreWebView2CompositionController + a Windows.UI.Composition DirectComposition
/// tree via WS_EX_NOREDIRECTIONBITMAP, the officially-documented path. That one got all the way
/// to a live diagnostic proving the page's own CSS WAS genuinely transparent
/// (getComputedStyle -> rgba(0,0,0,0) on html/body) and DefaultBackgroundColor's value stuck
/// (readback confirmed A=0) -- yet the screen still showed the theme's solid opaque green no
/// matter what, even with transparency forced before first paint. That means the composited
/// surface itself never carried a real alpha channel in this composition mode, on this
/// machine/driver -- a known rough edge of Windows.UI.Composition-hosted WebView2 visual hosting
/// that no amount of page-side CSS/JS can work around.
///
/// This version sidesteps DWM compositing entirely: WebView2 renders normally (fully opaque,
/// standard windowed WebView2 control) into an off-screen helper window
/// (_renderHost/_renderWebView, positioned far outside any monitor -- never visible, but still a
/// real HWND so the browser paints normally), gets periodically captured as a PNG via
/// CoreWebView2.CapturePreviewAsync (which DOES preserve the page's real per-pixel alpha -- a
/// separate, reliable code path from DWM live compositing), then painted onto THIS window (the
/// one actually on screen) via classic GDI UpdateLayeredWindow (WS_EX_LAYERED), which has
/// supported real per-pixel alpha since Windows 2000 and isn't subject to whatever the
/// DirectComposition path was hitting. Trade-off: capture-cadence redraws (~8fps) instead of
/// live-synced compositing -- imperceptible for a mostly-static scorebug, not worth it for
/// anything that needs to feel like real-time video.</summary>
internal sealed class ScorebugOverlayForm : Form
{
    readonly WebMainForm _host;
    readonly System.Windows.Forms.Timer _renderTimer = new() { Interval = 130 };

    // Off-screen helper window: hosts a normal (opaque, windowed) WebView2 control at the
    // theme's native canvas resolution. Never shown on any real monitor (see constructor) --
    // exists purely so WebView2 has a real HWND to paint into, which CapturePreviewAsync then
    // reads from. A second top-level Form (not a child control of `this`) because `this` needs
    // WS_EX_LAYERED (for UpdateLayeredWindow) which WebView2's own child-window rendering
    // doesn't get along with.
    readonly Form _renderHost = new()
    {
        FormBorderStyle = FormBorderStyle.None,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Left = -32000,
        Top = -32000,
        Width = 100,
        Height = 100,
    };
    readonly WebView2 _renderWebView = new() { Dock = DockStyle.Fill };

    CoreWebView2? _core;
    bool _navigationReady;

    const int BottomMargin = 24;
    const double MaxScreenWidthFraction = 0.45;

    // Set by SizeAndPositionFromTheme -- the theme's own authored canvas size (e.g. ESPN 2020 is
    // 1920x79). _renderHost/_renderWebView stay at THIS size (no overflow/scrollbar); captured
    // frames are then scaled down to Width/Height (the actual, smaller on-screen size) while
    // compositing the premultiplied bitmap each tick.
    int _canvasW = 900, _canvasH = 120;

    public ScorebugOverlayForm(WebMainForm host)
    {
        _host = host;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;

        _renderHost.Controls.Add(_renderWebView);

        SizeAndPositionFromTheme();

        _renderTimer.Tick += async (_, _) => await RenderTickAsync();
        Load += async (_, _) =>
        {
            // The off-screen host must actually be shown (Show(), not just constructed) for
            // WebView2 to paint at all -- an unshown/unrealized window's control never renders.
            // "Show" here only means "has a live HWND"; it's positioned at (-32000,-32000), far
            // outside every real monitor, so nothing is ever visibly on screen from it.
            _renderHost.Show();
            await InitAsync();
        };
        FormClosed += (_, _) =>
        {
            _renderTimer.Stop();
            _renderHost.Close();
            _renderHost.Dispose();
        };
    }

    /// <summary>Sizes/positions the window from the active theme's own authored canvas
    /// (canvasWidth/canvasHeight in library.json) -- different skins have wildly different aspect
    /// ratios (ESPN 2020 is 1920x79, NBC 2024 is 1056x146), so a single fixed box would badly
    /// distort most of them. Scaled down to fit on screen, never up -- these are broadcast-bug
    /// crops, not meant to fill half the display. Falls back to a reasonable default box if no
    /// skin is resolved yet (shouldn't normally happen -- GAMETIME's flow always ensures a skin
    /// choice exists first, see ensureScorebugSkinChosen in app.js).</summary>
    void SizeAndPositionFromTheme()
    {
        var theme = _host.ResolveActiveScorebugThemeFile();
        _canvasW = theme?.canvasWidth ?? 900;
        _canvasH = theme?.canvasHeight ?? 120;
        _renderHost.Width = Math.Max(1, _canvasW);
        _renderHost.Height = Math.Max(1, _canvasH);

        // Screen.PrimaryScreen.WorkingArea reports real physical pixels here (the app opts into
        // Application.SetHighDpiMode(HighDpiMode.PerMonitorV2) in Program.cs), so this already
        // adapts to whatever the actual display is -- 3840x2160 on a 4K monitor, 1920x1080 on
        // 1080p, etc. -- with no separate 4K/1080p branch needed.
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int maxWidth = (int)(screen.Width * MaxScreenWidthFraction);
        double scale = Math.Min(1.0, maxWidth / (double)_canvasW);

        Width = Math.Max(200, (int)(_canvasW * scale));
        Height = Math.Max(40, (int)(_canvasH * scale));
        Left = screen.Left + (screen.Width - Width) / 2;
        Top = screen.Top + screen.Height - Height - BottomMargin;
    }

    async Task InitAsync()
    {
        CrashLog.Write("ScorebugOverlayForm InitAsync started", new Exception("diagnostic"));
        try
        {
            string userDataFolder = Path.Combine(AppContext.BaseDirectory, "WebView2Data_Overlay");
            Directory.CreateDirectory(userDataFolder);
            _renderWebView.DefaultBackgroundColor = ChromaKeyColor;
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _renderWebView.EnsureCoreWebView2Async(env);

            var core = _renderWebView.CoreWebView2;
            _core = core;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("teamlogo", ConfigStore.TeamLogosFolder, CoreWebView2HostResourceAccessKind.Allow);

            // CHROMA KEY, 2026-08-14: render the theme against a SOLID known key color instead
            // of chasing real CSS/compositor transparency -- see this class's doc comment for
            // why (matches Coffee's own CFB27 app's "Green screen" approach, confirmed by
            // reading its app.asar chromaKey.js directly). WebView2's default background is set
            // to the key color below too, so even the very first, pre-script paint is on-key.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "(function(){function t(el){if(!el)return;" +
                "el.style.setProperty('background','" + ChromaKeyHex + "','important');" +
                "el.style.setProperty('background-color','" + ChromaKeyHex + "','important');}" +
                "t(document.documentElement);" +
                "document.addEventListener('DOMContentLoaded',function(){" +
                "t(document.body);t(document.getElementById('backdrop'));});})();");

            core.NavigationCompleted += async (_, _) => await OnNavigationCompletedAsync();

            await NavigateToActiveThemeAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("ScorebugOverlayForm init failed", ex);
        }
    }

    async Task NavigateToActiveThemeAsync()
    {
        var theme = _host.ResolveActiveScorebugThemeFile();
        if (theme == null)
        {
            CrashLog.Write("ScorebugOverlayForm: no scorebug skin resolved, nothing to show", new Exception("no theme"));
            return;
        }
        if (_core == null) return;

        // The theme's own folder, served over https:// like every other page in this app --
        // WebView2 applies stricter script/security behavior to bare file:// navigation than
        // to a mapped virtual host, and this keeps it consistent with how the thumbnail
        // gallery and the main window already serve everything. Re-mapped on every navigate
        // (not just once) since a skin refresh can point at a different theme's folder.
        string themeDir = Path.GetDirectoryName(theme.Value.htmlPath)!;
        _core.SetVirtualHostNameToFolderMapping("scorebugtheme", themeDir, CoreWebView2HostResourceAccessKind.Allow);

        _navigationReady = false;
        _pendingWarmupRenavigate = true;
        string fileName = Path.GetFileName(theme.Value.htmlPath);
        _lastNavigatedUrl = $"https://scorebugtheme/{Uri.EscapeDataString(fileName)}";
        _core.Navigate(_lastNavigatedUrl);
    }

    // Confirmed live (2026-08-13): a theme's FIRST navigation after a fresh page load
    // sometimes still loses the race against Chromium's own layer-promotion timing even with
    // the per-tick reinforcement script running (NBC 2024 was observed stuck opaque-green on
    // one navigation, then rendering perfectly -- real transparency, real live data -- on the
    // very next navigation of the exact same theme file straight afterward). Rather than keep
    // fighting that race on the original page instance, force one silent re-navigation shortly
    // after the first navigation completes -- a fresh page load gets a fresh first-paint, and
    // the per-tick script has a full new run at establishing transparency before anything gets
    // captured. `_pendingWarmupRenavigate` stops this from repeating forever -- only set by
    // NavigateToActiveThemeAsync (the "real" trigger), never by the rewarm navigation itself.
    bool _pendingWarmupRenavigate;
    string? _lastNavigatedUrl;

    async Task OnNavigationCompletedAsync()
    {
        if (_core == null) return;

        // Belt-and-suspenders reinforcement for elements that don't exist yet at
        // document_start, plus NBC 2024 / NBC 2024 Monochrome's own first-party transparent
        // mode (a `.transparent` class wired through their bridge's setTransparent() --
        // guarded with `&&` so it's a silent no-op on themes that don't have it).
        try
        {
            await _core.ExecuteScriptAsync(
                "(function(){var b=window.CFB27||window.cfb27||window.scorebug;" +
                "if(b&&b.setTransparent)b.setTransparent(true);" +
                "var bd=document.getElementById('backdrop');" +
                "if(bd)bd.style.setProperty('background','" + ChromaKeyHex + "','important');})();");
        }
        catch (Exception ex)
        {
            CrashLog.Write("ScorebugOverlayForm key-color CSS injection failed", ex);
        }

        _navigationReady = true;
        if (!_renderTimer.Enabled) _renderTimer.Start();

        if (_pendingWarmupRenavigate)
        {
            _pendingWarmupRenavigate = false;
            string? url = _lastNavigatedUrl;
            _ = Task.Run(async () =>
            {
                await Task.Delay(900);
                if (IsDisposed || _core == null || url == null) return;
                try { BeginInvoke(() => { if (!IsDisposed) _core?.Navigate(url); }); }
                catch (Exception ex) { CrashLog.Write("ScorebugOverlayForm warmup renavigate failed", ex); }
            });
        }
    }

    /// <summary>Every tick: push live data into the theme's own bridge (see PushLiveDataAsync's
    /// doc comment), then capture the now-updated page as a real-alpha PNG and blit it onto this
    /// window via UpdateLayeredWindow. Both steps guarded independently so a capture failure on
    /// one tick doesn't stop future ticks -- matches how PushLiveDataAsync already tolerated
    /// per-tick failures under the old composition approach.</summary>
    bool _loggedFirstTick;

    // Some themes (confirmed live: FOX 2025) are "bundler-exported" -- they show a loading
    // placeholder first, then asynchronously swap in their real content, which can reset
    // body's inline style and wipe out a one-time document_start/NavigationCompleted override
    // before it's ever actually captured (confirmed via diagnostic: computedStyle read back
    // rgb(0,255,0) on this exact theme despite both the early and post-nav overrides having
    // run). Re-running this every tick -- cheap, idempotent -- means it doesn't matter when
    // that async swap finishes; it always wins on the very next capture regardless.
    // Confirmed live (2026-08-13): even with computedStyle correctly reporting rgba(0,0,0,0)
    // on every tick, CapturePreviewAsync kept returning a byte-for-byte IDENTICAL PNG to the
    // one captured back when the page was still solid green -- Chromium had promoted body's
    // paint to an opaque GPU compositing layer during the theme's own loading-placeholder first
    // paint (FOX 2025 intentionally shows solid green while its bundler unpacks), and a later
    // style change alone doesn't invalidate that cached layer, so CapturePreviewAsync's
    // underlying compositor frame stayed stuck on the original opaque render even though CSSOM
    // was correct the whole time. Forcing a throwaway opacity change is a well-known Chromium
    // trick for exactly this -- opacity != 1 always requires allocating a brand new compositing
    // layer (it can't reuse a cached opaque one), so toggling it off and back to 1 forces a
    // fresh layer that actually respects the current (transparent) background.
    // Confirmed live (2026-08-14): the opacity-toggle layer-invalidation trick above only forces
    // Chromium to SCHEDULE a fresh compositing layer -- ExecuteScriptAsync resolves as soon as the
    // synchronous JS finishes running, not once the browser has actually PAINTED that new layer.
    // On FOX 2025 specifically, RenderTickAsync's immediate CapturePreviewAsync right after
    // ExecuteScriptAsync kept winning that race often enough to still grab the stale opaque-green
    // frame -- same underlying bug as the doc comment above this constant, just not fully closed by
    // the opacity toggle alone. Fixed by making the script an explicit Promise that only resolves
    // after two nested requestAnimationFrame callbacks (the standard "wait for a real committed
    // paint" pattern -- the first rAF fires before the frame that includes the opacity change is
    // produced, the second fires only after it's actually been composited), so RenderTickAsync's
    // await genuinely blocks until a fresh frame exists before capturing it.
    const string ForceTransparentScript =
        "(function(){return new Promise(function(resolve){" +
        "function t(el){if(!el)return;" +
        "el.style.setProperty('background','" + ChromaKeyHex + "','important');" +
        "el.style.setProperty('background-color','" + ChromaKeyHex + "','important');}" +
        "var b=window.CFB27||window.cfb27||window.scorebug;" +
        "if(b&&b.setTransparent)b.setTransparent(true);" +
        "t(document.documentElement);t(document.body);" +
        "t(document.getElementById('backdrop'));" +
        "document.body.style.setProperty('opacity','0.9999999');" +
        "void document.body.offsetHeight;" +
        "document.body.style.setProperty('opacity','1');" +
        "requestAnimationFrame(function(){requestAnimationFrame(resolve);});" +
        "});})();";

    // CHROMA KEY, 2026-08-14 (owner-directed, after seeing Coffee's OWN CFB27 Scoreboard Overlay
    // app's "Green screen" tab -- Reader & live data / Theme & placement / Green screen, with
    // Tolerance/Edge softness sliders shown at 22%/15.5%). Confirmed by reading Coffee's app.asar
    // directly (both known installs): it ships a `chromaKey.js` module (`window.CFB27ChromaKey`)
    // with `alphaForRgb(rgb, {color,tolerance,softness})` computing
    // distance = avg(|R-Rk|,|G-Gk|,|B-Bk|)/255, alpha = clamp((distance-tolerance)/softness,0,1),
    // default color #00ff00 (tolerance/softness clamped 0-0.3 / 0.005-0.2 -- 22%/15.5% is just the
    // user's tuned value shown in the screenshot). Coffee's app renders the theme on a SOLID
    // key-color canvas and chroma-keys the CAPTURED frame afterward, rather than fighting the
    // browser compositor for real alpha -- exactly what this section replicates, because that's
    // precisely what the three rewrites documented above this class kept losing to.
    //
    // Key color is MAGENTA, not green -- NBC 2024's own bundled theme HTML already declares
    // #00FF00 near its top (it was apparently authored expecting a green-screen capture already),
    // but its peacock logo <svg> also contains a real green feather (#0DB14B), whose distance from
    // pure green is ~0.217 -- almost exactly inside Coffee's own 0.22 tolerance. Green would risk
    // keying out that real artwork; magenta (#FF00FF) doesn't appear in any of the 5 bundled
    // themes' authored palettes.
    const string ChromaKeyHex = "#FF00FF";
    static readonly Color ChromaKeyColor = Color.FromArgb(255, 0, 255);
    const double ChromaKeyTolerance = 0.22;  // matches Coffee's app screenshot value
    const double ChromaKeySoftness = 0.155;  // matches Coffee's app screenshot value

    /// <summary>Per-pixel chroma key: pixels near ChromaKeyColor become transparent, with a soft
    /// falloff band (width = ChromaKeySoftness) so anti-aliased edge pixels between the theme's
    /// real artwork and the key-color canvas get a partial, blended alpha instead of a hard
    /// on/off cutoff -- that soft edge is what actually fixes the white/key-color fringe (the
    /// same antialiasing boundary the old approach's white-halo bug lived in), not just the flat
    /// keyed-out background. Mirrors Coffee's own alphaForRgb math exactly (see doc comment
    /// above). Operates on straight (non-premultiplied) alpha, BEFORE the existing
    /// premultiply/scale step in RenderTickAsync -- that step still handles the separate garbage-
    /// RGB-behind-transparent-pixels problem for whatever alpha this pass produces.</summary>
    static unsafe void ApplyChromaKey(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int keyR = ChromaKeyColor.R, keyG = ChromaKeyColor.G, keyB = ChromaKeyColor.B;
            double gain = 1.0 / ChromaKeySoftness;
            for (int y = 0; y < bmp.Height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    byte* px = row + x * 4; // BGRA byte order
                    int b = px[0], g = px[1], r = px[2];
                    double distance = (Math.Abs(r - keyR) + Math.Abs(g - keyG) + Math.Abs(b - keyB)) / (3.0 * 255.0);
                    double alpha = Math.Clamp((distance - ChromaKeyTolerance) * gain, 0.0, 1.0);
                    px[3] = (byte)Math.Round(alpha * 255.0);
                }
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    async Task RenderTickAsync()
    {
        if (_core == null || !_navigationReady) return;
        await PushLiveDataAsync();

        try
        {
            await _core.ExecuteScriptAsync(ForceTransparentScript);
            using var pngStream = new MemoryStream();
            await _core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, pngStream);
            pngStream.Position = 0;
            using var captured = new Bitmap(pngStream);
            ApplyChromaKey(captured);

            if (!_loggedFirstTick)
            {
                _loggedFirstTick = true;
                string computed = await _core.ExecuteScriptAsync(
                    "(function(){function c(el){return el?getComputedStyle(el).backgroundColor:'(missing)';}" +
                    "return JSON.stringify({html:c(document.documentElement),body:c(document.body)});})();");
                CrashLog.Write(
                    $"ScorebugOverlayForm first render tick: capturedBytes={pngStream.Length}, " +
                    $"capturedSize={captured.Width}x{captured.Height}, formWidth={Width}x{Height}, computedStyle={computed}",
                    new Exception("diagnostic"));
            }

            // Two-step, NOT a single scaled draw (confirmed live: a single interpolated
            // DrawImage straight from the captured straight-alpha PNG produced a visible white
            // fringe/border around the whole bug). Transparent pixels in a straight-alpha PNG
            // still carry arbitrary RGB (PNG encoders commonly leave white behind fully
            // transparent regions) -- bilinear interpolation blends that garbage RGB into
            // visible edge pixels purely by spatial distance, with no idea it should be
            // weighted down by near-zero alpha, which is exactly what produces a white halo at
            // every edge. Premultiplying FIRST, at native size with no scaling (so garbage RGB
            // in transparent regions gets zeroed out before any blending happens), then scaling
            // the ALREADY-premultiplied bitmap down is safe -- interpolating premultiplied data
            // is precisely what premultiplied alpha exists to make safe.
            using var premultipliedNative = new Bitmap(captured.Width, captured.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(premultipliedNative))
                g.DrawImageUnscaled(captured, 0, 0);

            using var premultiplied = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(premultiplied))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                // CompositingMode.SourceCopy here (safe now, unlike the earlier native-size
                // conversion above which needs SourceOver to actually premultiply) -- both
                // source and destination are already premultiplied 32bppPArgb, so this is a
                // pure resample with no format conversion needed.
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(premultipliedNative, new Rectangle(0, 0, Width, Height));
            }

            NativeLayeredWindow.Paint(Handle, premultiplied, new Point(Left, Top));
        }
        catch (Exception ex)
        {
            CrashLog.Write("ScorebugOverlayForm render tick failed", ex);
        }
    }

    /// <summary>Pushes live game data into the theme's own `window.updateScorebug` bridge (see
    /// this class's doc comment for how that contract was confirmed). Guarded with
    /// `window.updateScorebug &amp;&amp;` so this is a silent no-op for FOX 2021 (no bridge at all)
    /// instead of a script error, and for the bundler-exported themes (FOX 2025) whose bridge
    /// script may not have finished unpacking yet on the very first few ticks after navigation --
    /// it just starts working the moment the bridge registers, no special-casing needed.</summary>
    int _pushLogCount;

    async Task PushLiveDataAsync()
    {
        if (_core == null) return;
        try
        {
            string payload = _host.BuildScorebugOverlayPayloadJson();
            string result = await _core.ExecuteScriptAsync(
                $"(function(){{var had=!!window.updateScorebug;" +
                $"var r=had&&window.updateScorebug({payload});" +
                $"return JSON.stringify({{bridgePresent:had,applyResult:r}});}})();");
            if (_pushLogCount < 5)
            {
                _pushLogCount++;
                CrashLog.Write($"ScorebugOverlayForm push #{_pushLogCount}: payload={payload}, result={result}", new Exception("diagnostic"));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("ScorebugOverlayForm live push failed", ex);
        }
    }

    /// <summary>Called by WebMainForm right before Show() on every watch-start, in case the skin
    /// choice changed since this window was last shown (e.g. a different game with a different
    /// preferred skin) -- re-resolves and re-navigates rather than assuming the theme loaded at
    /// construction time is still the right one. Unlike the old DirectComposition approach, a
    /// normal WebView2 control fully supports repeated navigation on the same instance, so this
    /// is just a plain re-navigate -- no one-time-setup guard needed.</summary>
    public void RefreshForCurrentSkin()
    {
        SizeAndPositionFromTheme();
        _ = NavigateToActiveThemeAsync();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_LAYERED: required for UpdateLayeredWindow (see NativeLayeredWindow.Paint) to
            // actually composite this window's bitmap with real per-pixel alpha -- classic GDI
            // layered-window support, not dependent on DWM/DirectComposition at all.
            // WS_EX_TRANSPARENT keeps mouse input passing straight through to whatever's
            // underneath (the game); WS_EX_NOACTIVATE keeps it from stealing focus.
            cp.ExStyle |= 0x00080000 /* WS_EX_LAYERED */ | 0x20 /* WS_EX_TRANSPARENT */ | 0x08000000 /* WS_EX_NOACTIVATE */;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
}
