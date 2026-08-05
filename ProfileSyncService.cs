using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>Best-effort mirror of the local UserProfile (favorite team + lifetime stats) to the
/// marketplace worker's /profile endpoint when signed in with Google -- lets the "universal
/// profile" follow the account across devices/reinstalls. Local storage (ConfigStore.UserProfile)
/// is always the real source of truth for THIS device; every call here is safe to fire-and-forget
/// since any failure (signed out, network down, worker unreachable) just means the next successful
/// sync catches up -- nothing here is ever allowed to block or fail local save/load.</summary>
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
                stats = new
                {
                    gamesWatched = profile.GamesWatched,
                    songsTriggered = profile.SongsTriggered,
                    marketplaceUploads = profile.MarketplaceUploads,
                    marketplaceDownloads = profile.MarketplaceDownloads,
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

            string? favoriteTeam = root.TryGetProperty("favoriteTeam", out var ft) && ft.ValueKind == JsonValueKind.String
                ? ft.GetString() : null;
            var stats = root.TryGetProperty("stats", out var st) ? st : default;
            int GetInt(string name) =>
                stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetInt32() : 0;

            return new ConfigStore.UserProfile
            {
                FavoriteTeam = favoriteTeam,
                GamesWatched = GetInt("gamesWatched"),
                SongsTriggered = GetInt("songsTriggered"),
                MarketplaceUploads = GetInt("marketplaceUploads"),
                MarketplaceDownloads = GetInt("marketplaceDownloads"),
            };
        }
        catch { return null; }
    }
}
