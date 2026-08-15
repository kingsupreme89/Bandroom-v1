using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>Posts a message to the marketplace worker's chat endpoints (/chat/&lt;channel&gt;/post).
/// Mirrors ProfileSyncService's pattern -- the session token lives in ConfigStore.LoadAuthSession(),
/// never in JS, so posting has to go through this bridge-callable service rather than a raw fetch()
/// from app.js. Reading (/chat/&lt;channel&gt;/list) needs no auth and is called directly from
/// app.js like /list and /leaderboard already are.</summary>
internal static class MarketplaceChatService
{
    const string BaseUrl = "https://bandroom-marketplace.bandroom.workers.dev";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Returns { ok, error?, message? } as a JSON string (parsed by app.js) -- never throws.</summary>
    public static async Task<string> PostAsync(string channel, string content, string? attachedItemId, string? attachedItemType)
    {
        var session = ConfigStore.LoadAuthSession();
        if (session == null) return JsonSerializer.Serialize(new { ok = false, error = "signin_required" });

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                content,
                attachedItemId = string.IsNullOrWhiteSpace(attachedItemId) ? null : attachedItemId,
                attachedItemType = string.IsNullOrWhiteSpace(attachedItemType) ? null : attachedItemType,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/{Uri.EscapeDataString(channel)}/post")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { ok = false, error = $"http_{(int)response.StatusCode}" });
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(new { ok = true, message = doc.RootElement.GetProperty("message") });
        }
        catch (Exception ex)
        {
            CrashLog.Write("MarketplaceChatService.PostAsync failed", ex);
            return JsonSerializer.Serialize(new { ok = false, error = "network" });
        }
    }
}
