using System.Net.Http;
using System.Net.Http.Headers;

namespace SupremeStadiumSoundSelector;

/// <summary>Downloads a Trophy Room image (from the cloudflare-marketplace R2 bucket, via the
/// worker's /file/&lt;key&gt; endpoint) and saves it as this team's local background image, per
/// the TeamBackdrop.cs convention (ConfigStore.TeamBackgroundsFolder\&lt;team&gt;.&lt;ext&gt;).
/// Downloaded server-side (not piped through the JS&lt;-&gt;host bridge) since that bridge is
/// awkward/likely size-limited for binary blobs -- this is a small, one-purpose HttpClient, kept
/// separate from ChangelogService/UserCountService's clients since it hits an arbitrary
/// user-supplied URL (the marketplace worker) rather than a fixed trusted endpoint.</summary>
internal static class TeamBackgroundDownloadService
{
    static readonly HttpClient Http = BuildClient();

    static HttpClient BuildClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bandroom", "1.0"));
        return http;
    }

    // Keep this in sync with TeamBackdrop.cs's own Extensions list -- FindImagePath only looks
    // for these, so saving under an extension outside this list would silently never be found.
    static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp" };

    /// <summary>Downloads <paramref name="url"/> and saves it as the background for
    /// <paramref name="teamName"/>, replacing any existing one (including under a different
    /// extension, so switching from a .png background to a .jpg one doesn't leave the old file
    /// behind to be picked up first by FindImagePath's extension-order scan). Returns the saved
    /// path, or null on any failure (bad URL, network error, unrecognized/oversized content).</summary>
    public static async Task<string?> DownloadAndSaveAsync(string teamName, string url)
    {
        if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(url)) return null;

        // Only ever accept http(s) URLs from the marketplace worker -- guards against a
        // malformed/malicious url value making it into a file:// or other unexpected scheme.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            return null;

        try
        {
            using var response = await Http.GetAsync(parsed, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            // Cap at 25MB (matches the marketplace worker's own MAX_UPLOAD_BYTES) so a
            // misbehaving/huge response can't fill the disk or hang on a slow connection.
            const long maxBytes = 25 * 1024 * 1024;
            if (response.Content.Headers.ContentLength is long len && len > maxBytes) return null;

            string ext = ExtensionFromResponse(response, parsed);
            if (!AllowedExtensions.Contains(ext)) ext = ".jpg"; // safe default; content is still whatever bytes we got

            Directory.CreateDirectory(ConfigStore.TeamBackgroundsFolder);
            string safeTeam = System.Text.RegularExpressions.Regex.Replace(teamName, @"[^\w\s-]", "").Trim();
            if (safeTeam.Length == 0) return null;

            // Remove any existing background for this team under ANY of the recognized
            // extensions first, so a re-download under a different extension doesn't leave a
            // stale file that FindImagePath's extension-order scan would find first.
            foreach (var oldExt in AllowedExtensions)
            {
                string oldPath = Path.Combine(ConfigStore.TeamBackgroundsFolder, safeTeam + oldExt);
                if (File.Exists(oldPath))
                {
                    try { File.Delete(oldPath); } catch { /* best-effort */ }
                }
            }

            string outPath = Path.Combine(ConfigStore.TeamBackgroundsFolder, safeTeam + ext);

            await using var httpStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);

            // Enforce the size cap while streaming too -- ContentLength above can be absent
            // (chunked transfer) even though the actual body is oversized.
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await httpStream.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    fileStream.Close();
                    try { File.Delete(outPath); } catch { /* best-effort */ }
                    return null;
                }
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
            }

            if (total == 0)
            {
                try { File.Delete(outPath); } catch { /* best-effort */ }
                return null;
            }

            return outPath;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"TeamBackgroundDownloadService failed for \"{teamName}\" <- {url}", ex);
            return null;
        }
    }

    static string ExtensionFromResponse(HttpResponseMessage response, Uri url)
    {
        // Prefer the URL's own path extension (the worker's /file/<key> URLs preserve the
        // original uploaded filename in the key) since it's more reliable than a generic
        // Content-Type the worker may not have set precisely.
        string urlExt = Path.GetExtension(url.AbsolutePath);
        if (!string.IsNullOrEmpty(urlExt) && AllowedExtensions.Contains(urlExt)) return urlExt.ToLowerInvariant();

        return response.Content.Headers.ContentType?.MediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/bmp" => ".bmp",
            _ => ".jpg",
        };
    }
}
