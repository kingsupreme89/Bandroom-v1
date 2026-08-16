using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace SupremeStadiumSoundSelector;

/// <summary>Downloads a marketplace item (song or image) and saves it into the local library
/// under a clear, human-readable filename ("[School] - [Name].ext") rather than the original
/// uploaded filename, so "My Downloads" reads clearly. Songs land in
/// ConfigStore.SongsUploadedFolder so they immediately show up as assignable tracks in
/// AssignTrackForm -- no separate "import" step. Images land in ConfigStore.DownloadedImagesFolder,
/// a general library distinct from the single-active-background-per-team TeamBackgroundsFolder.
/// Deliberately its own HttpClient (same reasoning as TeamBackgroundDownloadService): this hits an
/// arbitrary user-supplied marketplace URL, not a small fixed set of trusted endpoints.</summary>
internal static class MarketplaceDownloadService
{
    static readonly HttpClient Http = BuildClient();
    const long MaxBytes = 25 * 1024 * 1024; // matches the marketplace worker's own upload cap

    static HttpClient BuildClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bandroom", "1.0"));
        return http;
    }

    static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac", ".webm", ".ogg" };
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp" };

    /// <summary>Downloads and saves a song or image to the appropriate local library folder.
    /// <paramref name="type"/> must be "song" or "image". Returns the saved path, or null on any
    /// failure (bad URL, network error, wrong content type for the requested type, oversized).</summary>
    public static async Task<string?> DownloadAsync(string type, string name, string school, string url)
    {
        if (type != "song" && type != "image") return null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(school) || string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            return null;

        try
        {
            using var response = await Http.GetAsync(parsed, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is long len && len > MaxBytes) return null;

            string ext = Path.GetExtension(parsed.AbsolutePath);
            var allowed = type == "song" ? AudioExtensions : ImageExtensions;
            if (string.IsNullOrEmpty(ext) || !allowed.Contains(ext))
                ext = type == "song" ? ".mp3" : ".jpg"; // safe default; content is still whatever bytes we got

            string folder = type == "song" ? ConfigStore.SongsUploadedFolder : ConfigStore.DownloadedImagesFolder;
            Directory.CreateDirectory(folder);

            // "[SCHOOL] - [Name]" so the file is identifiable outside the app too, not just in
            // the marketplace UI -- matches ConfigStore.ImportIntoSongsLibrary's ALL-CAPS
            // convention for the school part, keeps the item name as-typed for readability.
            string safeSchool = SanitizeForFileName(school).ToUpperInvariant();
            string safeName = SanitizeForFileName(name);
            string baseName = $"{safeSchool} - {safeName}";
            if (baseName.Length == 0) baseName = type == "song" ? "DOWNLOADED SONG" : "DOWNLOADED IMAGE";

            string outPath = Path.Combine(folder, baseName + ext);
            int suffix = 2;
            while (File.Exists(outPath))
                outPath = Path.Combine(folder, $"{baseName} ({suffix++}){ext}");

            await using (var httpStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        fileStream.Close();
                        try { File.Delete(outPath); } catch { /* best-effort */ }
                        return null;
                    }
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                }

                if (total == 0)
                {
                    fileStream.Close();
                    try { File.Delete(outPath); } catch { /* best-effort */ }
                    return null;
                }
            }

            ConfigStore.RecordMarketplaceDownload(type, name, school, outPath, url);
            return outPath;
        }
        catch (Exception ex)
        {
            CrashLog.Write($"MarketplaceDownloadService failed for {type} \"{name}\" ({school}) <- {url}", ex);
            return null;
        }
    }

    static string SanitizeForFileName(string s)
    {
        s = Regex.Replace(s, @"[^\w\s-]", "");
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, ' ');
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
