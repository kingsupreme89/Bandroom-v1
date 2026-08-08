using System.IO.Compression;
using System.Net.Http;

namespace SupremeStadiumSoundSelector;

/// <summary>Downloads and extracts the optional default song pack (2,241 files, ~2.8GB zipped)
/// from Cloudflare R2, one time, only if the user opts in. Pulled out of the installer as of
/// v1.0.48 to stay under GitHub Releases' 2GB-per-asset cap -- see cloudflare-defaultsongs.</summary>
internal static class DefaultSongPackService
{
    const string Endpoint = "https://bandroom-defaultsongs.bandroom.workers.dev";
    static readonly string ZipPath = Path.Combine(ConfigStore.UserDataRoot, "default_songs_pack.zip");

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(2) };

    public record PackInfo(long SizeBytes, string Etag);

    /// <summary>Asks the worker how big the pack is, so the UI can show a real number
    /// ("2.8 GB") before the user commits to downloading it.</summary>
    public static async Task<PackInfo?> GetPackInfoAsync()
    {
        try
        {
            using var res = await Http.GetAsync($"{Endpoint}/pack-info");
            if (!res.IsSuccessStatusCode) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return new PackInfo(
                doc.RootElement.GetProperty("size").GetInt64(),
                doc.RootElement.GetProperty("etag").GetString() ?? "");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Downloads pack.zip and extracts it into ConfigStore.DownloadedDefaultSongsFolder.
    /// Resumes from a partial local zip on retry (Range request) rather than restarting from
    /// zero, since this is a multi-GB download over a real user's home connection.
    /// progress(fractionComplete, bytesDownloaded, totalBytes) is called throughout.</summary>
    public static async Task<bool> DownloadAndExtractAsync(Action<double, long, long> progress, CancellationToken ct)
    {
        var info = await GetPackInfoAsync();
        if (info == null) return false;

        Directory.CreateDirectory(ConfigStore.UserDataRoot);

        long existingBytes = File.Exists(ZipPath) ? new FileInfo(ZipPath).Length : 0;
        if (existingBytes > info.SizeBytes) { existingBytes = 0; File.Delete(ZipPath); } // stale/corrupt partial

        using (var req = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/pack.zip"))
        {
            if (existingBytes > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return false;

            await using var httpStream = await res.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(ZipPath, existingBytes > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write);

            var buffer = new byte[1024 * 1024];
            long downloaded = existingBytes;
            int read;
            while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                progress(Math.Clamp((double)downloaded / info.SizeBytes, 0, 1), downloaded, info.SizeBytes);
            }
        }

        // Extraction has no great way to report fine-grained progress (ZipFile doesn't expose
        // per-entry callbacks), so this reports as one long "99%" step rather than faking ticks.
        progress(0.99, info.SizeBytes, info.SizeBytes);

        if (Directory.Exists(ConfigStore.DownloadedDefaultSongsFolder))
            Directory.Delete(ConfigStore.DownloadedDefaultSongsFolder, recursive: true);
        Directory.CreateDirectory(ConfigStore.DownloadedDefaultSongsFolder);

        ZipFile.ExtractToDirectory(ZipPath, ConfigStore.UserDataRoot, overwriteFiles: true);
        // The zip was made from "Songs/Default", so it extracts to UserDataRoot\Songs\Default --
        // move that up to DownloadedDefaultSongsFolder (UserDataRoot\DefaultSongs) so it doesn't
        // collide with ConfigStore.SongsFolder's own "Songs" directory for user-uploaded tracks.
        string extractedTo = Path.Combine(ConfigStore.UserDataRoot, "Songs", "Default");
        if (Directory.Exists(extractedTo))
        {
            if (Directory.Exists(ConfigStore.DownloadedDefaultSongsFolder))
                Directory.Delete(ConfigStore.DownloadedDefaultSongsFolder, recursive: true);
            Directory.Move(extractedTo, ConfigStore.DownloadedDefaultSongsFolder);
        }

        File.Delete(ZipPath);
        progress(1.0, info.SizeBytes, info.SizeBytes);
        return true;
    }
}
