using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupremeStadiumSoundSelector;

/// <summary>Fetches Bandroom's own GitHub Releases so the in-app "Updates" panel can show a
/// real changelog (what's new / fixed in each version) instead of a static Live Feed of
/// in-session cue fires. Public repo, no auth token needed. Best-effort -- any failure (offline,
/// rate-limited, repo renamed) returns an empty list rather than breaking the UI.</summary>
internal static class ChangelogService
{
    const string ReleasesUrl = "https://api.github.com/repos/kingsupreme89/Bandroom-v1/releases";

    static readonly HttpClient Http = BuildClient();
    static List<ChangelogEntry>? _cache;
    static DateTime _cacheAt = DateTime.MinValue;

    static HttpClient BuildClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // GitHub's API rejects requests with no User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bandroom", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>Cached ~10 minutes -- the changelog panel can be reopened freely without
    /// re-hitting GitHub's API (and its rate limit) every time.</summary>
    public static async Task<List<ChangelogEntry>> GetReleasesAsync()
    {
        if (_cache != null && DateTime.UtcNow - _cacheAt < TimeSpan.FromMinutes(10))
            return _cache;

        try
        {
            using var stream = await Http.GetStreamAsync(ReleasesUrl);
            var raw = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream) ?? new();

            var entries = raw
                .Where(r => !r.Draft)
                .OrderByDescending(r => r.PublishedAt)
                .Select(r => new ChangelogEntry(
                    Version: r.TagName.TrimStart('v', 'V'),
                    Title: string.IsNullOrWhiteSpace(r.Name) ? r.TagName : r.Name,
                    Notes: ParseNotes(r.Body),
                    PublishedAt: r.PublishedAt,
                    Prerelease: r.Prerelease))
                .ToList();

            _cache = entries;
            _cacheAt = DateTime.UtcNow;
            return entries;
        }
        catch
        {
            return _cache ?? new List<ChangelogEntry>();
        }
    }

    /// <summary>Release notes are written as a plain "- bullet" markdown list in release.ps1's
    /// --notes text; split into individual lines here so the UI can render them as a real list
    /// instead of a wall of markdown text. Falls back to the whole body as one line if it
    /// doesn't look like a bullet list.</summary>
    static List<string> ParseNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new();

        var lines = body.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.TrimStart('-', '*', ' ').Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return lines;
    }

    sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    }
}

internal sealed record ChangelogEntry(string Version, string Title, List<string> Notes, DateTime PublishedAt, bool Prerelease);
