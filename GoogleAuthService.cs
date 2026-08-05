using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>Google sign-in for a desktop app. Google's OAuth policy actively blocks sign-in
/// inside an embedded webview (WebView2 included) -- it detects the embedded browser and refuses
/// with "this browser or app may not be secure" rather than letting the user log in. The correct
/// pattern for a native/desktop app is Authorization Code + PKCE with a loopback redirect: open
/// the user's real system browser to Google's consent page, spin up a short-lived local
/// HttpListener on 127.0.0.1 to catch the redirect with the auth code, then exchange that code
/// for tokens directly from this process (no client secret needed -- PKCE is what makes a public/
/// desktop OAuth client safe without one). See README/handoff notes for the one-time Google Cloud
/// Console setup this depends on (OAuth consent screen + a Client ID of type "Desktop app").</summary>
internal static class GoogleAuthService
{
    // TODO: fill in once the Google Cloud Console OAuth client is created (Desktop app type).
    // A Desktop-app client ID is not a secret in the traditional sense (PKCE covers that), but
    // don't wire in a Web-application-type client ID here -- Google's redirect URI rules for that
    // type don't allow the loopback pattern this code relies on.
    public const string ClientId = "REPLACE_WITH_DESKTOP_OAUTH_CLIENT_ID.apps.googleusercontent.com";

    const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    const string Scope = "openid email profile";

    static readonly HttpClient Http = new();

    public sealed record GoogleProfile(string Sub, string Email, string Name, string? Picture, string IdToken);

    /// <summary>Runs the full sign-in flow: opens the system browser, waits for the redirect,
    /// exchanges the code for tokens, and decodes the ID token's claims for display. Returns null
    /// if the user closes the browser without completing sign-in, or on any failure -- never
    /// throws past this boundary since it's driven by an external browser window the app has no
    /// control over closing/timing out.</summary>
    public static async Task<GoogleProfile?> SignInAsync(CancellationToken ct)
    {
        if (ClientId.StartsWith("REPLACE_WITH_")) return null; // not configured yet

        string codeVerifier = GeneratePkceVerifier();
        string codeChallenge = PkceChallengeFromVerifier(codeVerifier);
        string state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        // Port 0 isn't valid for HttpListener prefixes, so pick a fixed high port. If it's ever
        // taken by something else, sign-in fails cleanly (returns null) rather than hanging --
        // acceptable for a first pass since a busy port on this exact one is unlikely.
        const int port = 51823;
        string redirectUri = $"http://127.0.0.1:{port}/callback/";
        listener.Prefixes.Add(redirectUri);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            CrashLog.Write("Google sign-in: couldn't bind local redirect listener", ex);
            return null;
        }

        try
        {
            string authUrl = $"{AuthEndpoint}?" + string.Join("&",
                $"client_id={Uri.EscapeDataString(ClientId)}",
                $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
                "response_type=code",
                $"scope={Uri.EscapeDataString(Scope)}",
                $"state={Uri.EscapeDataString(state)}",
                $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
                "code_challenge_method=S256");

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(3)); // don't hang forever if the user abandons the browser tab

            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            if (completed != contextTask) return null; // timed out or cancelled

            var context = await contextTask;
            var query = context.Request.QueryString;
            string? returnedState = query["state"];
            string? code = query["code"];

            string responseHtml = returnedState == state && code != null
                ? "<html><body style='font-family:sans-serif;text-align:center;padding-top:80px'>Signed in -- you can close this tab and go back to Bandroom.</body></html>"
                : "<html><body style='font-family:sans-serif;text-align:center;padding-top:80px'>Sign-in didn't complete -- you can close this tab.</body></html>";
            byte[] buf = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html";
            context.Response.OutputStream.Write(buf, 0, buf.Length);
            context.Response.Close();

            if (returnedState != state || code == null) return null; // state mismatch guards against a CSRF/redirect-injection attempt

            return await ExchangeCodeAsync(code, codeVerifier, redirectUri);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Google sign-in failed", ex);
            return null;
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task<GoogleProfile?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        });

        using var response = await Http.PostAsync(TokenEndpoint, form);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("id_token", out var idTokenEl)) return null;
        string idToken = idTokenEl.GetString() ?? "";

        // Decoded here for immediate local display only (name/email/picture in the profile tab).
        // This is NOT signature-verified -- fine for "what do we show the user about themselves"
        // but never trust a client-decoded JWT for anything that writes shared/server state.
        // The Worker's /auth/verify endpoint re-verifies the signature server-side before
        // trusting this same token to link marketplace identity -- see worker.js.
        var claims = DecodeJwtPayloadUnverified(idToken);
        if (claims == null) return null;

        string sub = claims.Value.TryGetProperty("sub", out var s) ? s.GetString() ?? "" : "";
        string email = claims.Value.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
        string name = claims.Value.TryGetProperty("name", out var n) ? n.GetString() ?? email : email;
        string? picture = claims.Value.TryGetProperty("picture", out var p) ? p.GetString() : null;
        if (sub.Length == 0) return null;

        return new GoogleProfile(sub, email, name, picture, idToken);
    }

    static JsonElement? DecodeJwtPayloadUnverified(string jwt)
    {
        string[] parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
        try
        {
            byte[] bytes = Convert.FromBase64String(payload);
            return JsonDocument.Parse(bytes).RootElement;
        }
        catch { return null; }
    }

    static string GeneratePkceVerifier()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    static string PkceChallengeFromVerifier(string verifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
