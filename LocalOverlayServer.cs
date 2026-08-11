using System.Net;
using System.Text;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>Serves the Band Director "Stream Overlay" page to an external browser (OBS Browser
/// Source) -- the Windows app previously had no local HTTP server at all (only
/// GoogleAuthService.cs's one-shot OAuth-redirect listener), unlike the Mac/Avalonia port which
/// already runs a full HttpListener on this same port (see src/Bandroom.Mac/MainWindow.axaml.cs)
/// since it has no embedded webview of its own. This is a much smaller version: just the one
/// static overlay page plus one JSON polling endpoint, not a full bridge-RPC surface -- the
/// Windows app already has WebView2 for its own UI, it only needs this for content an external
/// browser (not the app's own window) can load.
///
/// No real chat source exists yet (Twitch/YouTube OAuth is a later phase) -- /overlay/chat/data
/// always returns an empty message list for now. The page itself already polls this endpoint and
/// renders whatever comes back, so wiring a real chat feed later is just changing what this
/// endpoint returns, no page changes needed.</summary>
public sealed class LocalOverlayServer
{
    const string Prefix = "http://localhost:18765/";
    HttpListener? _listener;

    public void Start()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _ = Task.Run(Listen);
        }
        catch (Exception ex)
        {
            // Non-fatal -- e.g. port already in use by another Bandroom instance. The app still
            // works fully without this; only the OBS overlay URL won't resolve.
            Console.Error.WriteLine($"[LocalOverlayServer] Failed to start: {ex.Message}");
            _listener = null;
        }
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    void Listen()
    {
        while (_listener != null && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch { return; } // listener stopped

            try
            {
                var path = ctx.Request.Url!.AbsolutePath;
                if (path == "/overlay/chat/data")
                    ServeChatData(ctx);
                else if (path == "/overlay/chat" || path == "/overlay/chat/")
                    ServeChatPage(ctx);
                else
                    ctx.Response.StatusCode = 404;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LocalOverlayServer] Request error: {ex.Message}");
                try { ctx.Response.StatusCode = 500; } catch { }
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }
    }

    static void ServeChatPage(HttpListenerContext ctx)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "overlay-chat.html");
        if (!File.Exists(path))
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        var bytes = File.ReadAllBytes(path);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    static void ServeChatData(HttpListenerContext ctx)
    {
        // Always empty until real Twitch/YouTube chat wiring exists -- see class doc comment.
        var json = JsonSerializer.Serialize(new { messages = Array.Empty<object>() });
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }
}
