using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>Best-effort mirror of the local UserProfile (favorite team + lifetime stats) to the
/// marketplace worker's /profile endpoint when signed in with Google -- lets the "universal
/// profile" follow the account across devices/reinstalls. Local storage (ConfigStore.UserProfile)
/// is always the real source of truth for THIS device; every call here is safe to fire-and-forget
/// since any failure (signed out, network down, worker unreachable) just means the next successful
/// sync catches up -- nothing here is ever allowed to block or fail local save/load.
///
/// NOT synced: Bio's local-only 140-char cap is enforced client-side too, AvatarFileName (a local
/// file reference -- meaningless on another device, so it never round-trips through the cloud),
/// ToastsEnabled (a per-device preference, not something that should follow you), CreatedAt
/// (each device's local "first save" timestamp is its own, not merged).</summary>
internal static class ProfileSyncService
{
    const string BaseUrl = "https://bandroom-marketplace.bandroom.workers.dev";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task PushAsync(ConfigStore.UserProfile profile)
    {
        var session = ConfigStore.LoadAuthSession();
        if (session == null) return;
        try
        {
            string payload = JsonSerializer.Serialize(new
            {
                favoriteTeam = profile.FavoriteTeam,
                rivalTeam = profile.RivalTeam,
                bio = profile.Bio,
                stats = new
                {
                    gamesWatched = profile.GamesWatched,
                    songsTriggered = profile.SongsTriggered,
                    marketplaceUploads = profile.MarketplaceUploads,
                    marketplaceDownloads = profile.MarketplaceDownloads,
                    favoriteTeamWins = profile.FavoriteTeamWins,
                    favoriteTeamLosses = profile.FavoriteTeamLosses,
                    streakCurrentDays = profile.StreakCurrentDays,
                    eventCounts = profile.EventCounts,
                    gamesWatchedByTeam = profile.GamesWatchedByTeam,
                },
            });
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/profile")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
            await Http.SendAsync(request);
        }
        catch { /* best-effort -- local save already succeeded regardless */ }
    }

    /// <summary>Pulls the cloud-saved profile down right after a fresh sign-in, so favorites/stats
    /// saved from another device or a prior install show up here too. Returns null if there's
    /// nothing saved yet (brand-new account) or the call fails for any reason -- callers treat
    /// null as "nothing to restore", not as an error.</summary>
    public static async Task<ConfigStore.UserProfile?> PullAsync(string sessionToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("found", out var foundEl) || foundEl.ValueKind != JsonValueKind.True) return null;

            string? GetStr(JsonElement el, string name) =>
                el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            string? favoriteTeam = GetStr(root, "favoriteTeam");
            string? rivalTeam = GetStr(root, "rivalTeam");
            string? bio = GetStr(root, "bio");
            var stats = root.TryGetProperty("stats", out var st) ? st : default;

            int GetInt(string name) =>
                stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetInt32() : 0;

            Dictionary<string, int> GetDict(string name)
            {
                var result = new Dictionary<string, int>();
                if (stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object)
                    foreach (var prop in obj.EnumerateObject())
                        if (prop.Value.ValueKind == JsonValueKind.Number) result[prop.Name] = prop.Value.GetInt32();
                return result;
            }

            return new ConfigStore.UserProfile
            {
                FavoriteTeam = favoriteTeam,
                RivalTeam = rivalTeam,
                Bio = bio,
                GamesWatched = GetInt("gamesWatched"),
                SongsTriggered = GetInt("songsTriggered"),
                MarketplaceUploads = GetInt("marketplaceUploads"),
                MarketplaceDownloads = GetInt("marketplaceDownloads"),
                FavoriteTeamWins = GetInt("favoriteTeamWins"),
                FavoriteTeamLosses = GetInt("favoriteTeamLosses"),
                StreakCurrentDays = GetInt("streakCurrentDays"),
                EventCounts = GetDict("eventCounts"),
                GamesWatchedByTeam = GetDict("gamesWatchedByTeam"),
            };
        }
        catch { return null; }
    }
}
