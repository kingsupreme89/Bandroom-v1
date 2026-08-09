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

    /// <summary>Extracts an already-downloaded pack zip (e.g. from the Google Drive link opened
    /// in the system browser, since the pack isn't uploaded to R2 yet) into
    /// ConfigStore.DownloadedDefaultSongsFolder. Same move-up-a-level logic as
    /// DownloadAndExtractAsync's tail end, split out so the browser-download path doesn't need
    /// the HTTP half. progress(fractionComplete) is called at coarse milestones only --
    /// ZipFile.ExtractToDirectory has no per-entry callback to report finer-grained progress.</summary>
    public static Task<bool> ExtractExistingZipAsync(string zipPath, Action<double> progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(zipPath)) return false;

            progress(0.05);
            Directory.CreateDirectory(ConfigStore.UserDataRoot);
            string tempExtractRoot = Path.Combine(ConfigStore.UserDataRoot, "_songpack_import_tmp");
            if (Directory.Exists(tempExtractRoot)) Directory.Delete(tempExtractRoot, recursive: true);
            Directory.CreateDirectory(tempExtractRoot);

            ZipFile.ExtractToDirectory(zipPath, tempExtractRoot, overwriteFiles: true);
            ct.ThrowIfCancellationRequested();
            progress(0.85);

            // The pack zips "Default\..." at its root -- accept that either directly or nested
            // one level under a "Songs\Default" folder (matches the R2 pack.zip's own layout).
            string extractedTo = Path.Combine(tempExtractRoot, "Default");
            if (!Directory.Exists(extractedTo))
                extractedTo = Path.Combine(tempExtractRoot, "Songs", "Default");
            if (!Directory.Exists(extractedTo))
            {
                Directory.Delete(tempExtractRoot, recursive: true);
                return false;
            }

            if (Directory.Exists(ConfigStore.DownloadedDefaultSongsFolder))
                Directory.Delete(ConfigStore.DownloadedDefaultSongsFolder, recursive: true);
            Directory.Move(extractedTo, ConfigStore.DownloadedDefaultSongsFolder);
            Directory.Delete(tempExtractRoot, recursive: true);

            progress(1.0);
            return true;
        }, ct);
    }

    static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac" };

    public sealed record FolderImportResult(bool Success, string Message, List<string> TeamNames, int SongCount);

    /// <summary>Folder-flavored counterpart to ExtractExistingZipAsync -- for a user who already
    /// unzipped the pack, was handed a folder instead of a .zip, or only has ONE team's folder
    /// (not the whole pack). Merges into ConfigStore.DownloadedDefaultSongsFolder rather than
    /// wiping it first -- a single-team import must not erase teams imported earlier. Accepts
    /// three shapes for the selected folder: the full pack root (Conference\Team\*.mp3, optionally
    /// nested one level under "Default" or "Songs\Default" like the zip flow), a folder of team
    /// folders with no conference level (Team\*.mp3), or a single team's folder with the audio
    /// files directly inside it (e.g. the user pointed us straight at "Alabama").</summary>
    public static Task<FolderImportResult> ImportExistingFolderAsync(string folderPath, Action<double> progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(folderPath))
                return new FolderImportResult(false, "That folder doesn't exist.", new List<string>(), 0);
            progress(0.05);

            string root = folderPath;
            string nested = Path.Combine(folderPath, "Default");
            if (Directory.Exists(nested)) root = nested;
            else
            {
                nested = Path.Combine(folderPath, "Songs", "Default");
                if (Directory.Exists(nested)) root = nested;
            }

            Directory.CreateDirectory(ConfigStore.DownloadedDefaultSongsFolder);
            var teamsImported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int songCount = 0;
            int unmatchedCount = 0;

            bool HasAudioFilesDirectly(string dir) =>
                Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

            // Filename -> app EventKey (e.g. "Offense: 1st Down") is the exact reverse of
            // ConfigStore.ImportDefaultPackFromFolder's `name.Replace("_", ": ")` match rule --
            // this is the only filename shape that lands in the right situation slot automatically.
            string EventKeyToFileStem(string eventKey) => eventKey.Replace(": ", "_");

            void CopyFile(string sourceFile, string team, string destStem)
            {
                string destDir = Path.Combine(ConfigStore.DownloadedDefaultSongsFolder, "Imported", team);
                Directory.CreateDirectory(destDir);
                string ext = Path.GetExtension(sourceFile);
                string destPath = Path.Combine(destDir, destStem + ext);
                // A collision means a second file wants the same situation slot -- keep the first
                // (whichever wins the EventKey-exact filename that auto-fill matches on) and give
                // the rest a numbered alternate so they're still copied and browsable/assignable by
                // hand, they just won't auto-fill (matches how the app already stores alternates,
                // e.g. "Defense_Earned First Down_3.mp3").
                if (File.Exists(destPath))
                {
                    int n = 2;
                    while (File.Exists(destPath = Path.Combine(destDir, $"{destStem}_{n}{ext}"))) n++;
                }
                File.Copy(sourceFile, destPath, overwrite: true);
                songCount++;
                teamsImported.Add(team);
            }

            // Bulk-copies a folder that already IS one team's own folder (its files keep their
            // original names -- this is the official pack's own shape, Conference\Team\EventKey.mp3,
            // where the names are already exact EventKeys and don't need IntakeEngine's help).
            void CopyTeamFolder(string sourceTeamDir, string teamName)
            {
                foreach (var file in Directory.GetFiles(sourceTeamDir))
                {
                    if (!AudioExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                    CopyFile(file, teamName, Path.GetFileNameWithoutExtension(file));
                }
            }

            // Per-file classification for a folder whose OWN name doesn't identify a team (a
            // conference dump, "SEC" with 268 files loose inside it, everyone's songs mixed
            // together with team+event baked into each filename instead of folder structure --
            // owner's actual pack shape). Runs each file through IntakeEngine, the same
            // filename-parsing engine ImportLocalSongFromWeb already uses, to recover team +
            // situation from names like "sec ala '21 1st downs.mp3".
            void ClassifyFolderByFilename(string dir)
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!AudioExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;

                    var result = IntakeEngine.Process(Path.GetFileName(file));
                    if (result.Team == "Unknown") { unmatchedCount++; continue; }

                    if (result.SuggestedEventKeys.Length == 0)
                    {
                        // No situation guess at all -- still file it under the team (by original
                        // name) so it shows up in that team's library for manual assignment,
                        // rather than silently dropping a file we DID identify the team for.
                        CopyFile(file, result.Team, Path.GetFileNameWithoutExtension(file));
                        continue;
                    }
                    foreach (var eventKey in result.SuggestedEventKeys)
                        CopyFile(file, result.Team, EventKeyToFileStem(eventKey));
                }
            }

            // Recursive scan, not just 2 fixed levels -- a "conference" folder can hold loose
            // files of its own AND per-team subfolders side by side (owner report: an "SEC" folder
            // landed as one bogus "SEC" team because the scan stopped at the first audio it found
            // instead of also descending further). Every directory (any depth, guarded to 5 levels)
            // that has audio files directly inside gets handled -- as a real team's own folder if
            // its name resolves to one via IntakeEngine, otherwise per-file by filename.
            var audioDirs = new List<string>();
            void Scan(string dir, int depth)
            {
                if (depth > 5) return;
                if (HasAudioFilesDirectly(dir)) audioDirs.Add(dir);
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    Scan(sub, depth + 1);
                }
            }
            if (Directory.Exists(root)) Scan(root, 0);

            foreach (var dir in audioDirs)
            {
                var (resolvedTeam, _, matchType) = IntakeEngine.ResolveTeam(new DirectoryInfo(dir).Name);
                if (resolvedTeam != "Unknown" && matchType is "exact" or "abbreviation" or "variant")
                    CopyTeamFolder(dir, resolvedTeam);
                else
                    ClassifyFolderByFilename(dir);
            }
            progress(0.9);

            if (songCount == 0)
            {
                string why = unmatchedCount > 0
                    ? $"Found {unmatchedCount} audio file(s), but couldn't tell which team any of them belong to from their filenames."
                    : "No audio files were found in that folder.";
                return new FolderImportResult(false, why, new List<string>(), 0);
            }

            // Merge into index.json (what GetDefaultPackTeams reads) instead of overwriting it --
            // a team imported earlier (a prior folder, or the full pack) must not get forgotten.
            string indexPath = Path.Combine(ConfigStore.DownloadedDefaultSongsFolder, "index.json");
            var allTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(indexPath))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(indexPath));
                    if (doc.RootElement.TryGetProperty("Teams", out var arr))
                        foreach (var t in arr.EnumerateArray())
                            if (t.GetString() is string s) allTeams.Add(s);
                }
                catch { /* corrupt/missing index -- rebuild from what this import found */ }
            }
            foreach (var t in teamsImported) allTeams.Add(t);
            File.WriteAllText(indexPath, System.Text.Json.JsonSerializer.Serialize(new { Teams = allTeams.OrderBy(t => t).ToList() }));

            progress(1.0);
            var names = teamsImported.OrderBy(t => t).ToList();
            string msg = names.Count == 1
                ? $"Imported {songCount} song{(songCount == 1 ? "" : "s")} for {names[0]}. Open {names[0]}'s Assign panel -- matching situations are already filled in."
                : $"Imported {songCount} songs across {names.Count} teams: {string.Join(", ", names)}. Open each team's Assign panel to see them filled in.";
            if (unmatchedCount > 0)
                msg += $" {unmatchedCount} file(s) couldn't be matched to a team by filename and were skipped.";
            return new FolderImportResult(true, msg, names, songCount);
        }, ct);
    }
}
