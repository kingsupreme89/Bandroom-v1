using System.Net.Http.Headers;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>The "everyone sees it" counterpart to ProfileSyncService's CustomTeamLogos (which is
/// private, per-account, cross-YOUR-devices only). Pushing here makes a logo the new default that
/// OTHER users see too -- explicit owner request, with one guardrail owner also asked for: a team
/// the receiving user has already customized for themselves is never overwritten by someone else's
/// public push. Push requires being signed in (the worker's /teamlogo PUT is authed); pulling the
/// public index/applying logos does NOT require sign-in -- it's meant to benefit every user, not
/// just accounts.</summary>
internal static class PublicTeamLogoSyncService
{
    const string BaseUrl = "https://bandroom-marketplace.bandroom.workers.dev";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Fire-and-forget from SaveCustomTeamLogo right after a successful local save --
    /// "Automatic on save" per owner's explicit choice, no separate share step. Silently no-ops if
    /// not signed in (same as ProfileSyncService.PushAsync) since the worker requires auth to
    /// attribute the push to an account.</summary>
    public static async Task<bool> PushAsync(string team, byte[] pngBytes)
    {
        var session = ConfigStore.LoadAuthSession();
        if (session == null) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/teamlogo/{Uri.EscapeDataString(team)}")
            {
                Content = new ByteArrayContent(pngBytes),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
            using var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public sealed record PublicLogoIndexEntry(string Team, DateTime UpdatedAtUtc, string Url);

    /// <summary>Public, unauthenticated read of every team that currently has a published public
    /// logo. Returns an empty list on any failure (offline, worker unreachable) -- callers treat
    /// that as "nothing to sync this run", never as an error worth surfacing to the user.</summary>
    public static async Task<List<PublicLogoIndexEntry>> PullIndexAsync()
    {
        var result = new List<PublicLogoIndexEntry>();
        try
        {
            using var response = await Http.GetAsync($"{BaseUrl}/teamlogos");
            if (!response.IsSuccessStatusCode) return result;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var prop in items.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                string? updatedRaw = prop.Value.TryGetProperty("updatedAt", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                string? fileUrl = prop.Value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
                if (updatedRaw == null || fileUrl == null) continue;
                if (!DateTime.TryParse(updatedRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var updatedAt)) continue;
                result.Add(new PublicLogoIndexEntry(prop.Name, updatedAt, fileUrl));
            }
        }
        catch { /* best-effort -- offline/unreachable just means nothing syncs this run */ }
        return result;
    }

    /// <summary>Best-effort startup sync: pulls the public index, then for every team that (a) this
    /// device hasn't already applied this exact update for (PublicTeamLogoSyncManifest) and (b) the
    /// user has NOT set their own custom logo for (ConfigStore.UserProfile.CustomTeamLogos) --
    /// that's the owner-requested guarantee, a self-customized team is never overwritten by
    /// someone else's public push -- downloads the file and writes it via WebBridge's own
    /// WriteTeamLogoFile convention. Returns the list of team names actually updated so the caller
    /// can refresh the UI/toast if desired; safe to ignore for a silent background sync.</summary>
    public static async Task<List<string>> SyncAsync(CancellationToken ct)
    {
        var updated = new List<string>();
        List<PublicLogoIndexEntry> index;
        try { index = await PullIndexAsync(); }
        catch { return updated; }
        if (index.Count == 0) return updated;

        var ownCustomLogos = ConfigStore.LoadUserProfile().CustomTeamLogos;
        var manifest = ConfigStore.LoadPublicTeamLogoSyncManifest();
        var appliedAt = new Dictionary<string, DateTime>(manifest.AppliedAtUtc);

        foreach (var entry in index)
        {
            if (ct.IsCancellationRequested) break;
            // Guardrail the owner explicitly asked for: never clobber a team the user customized themselves.
            if (ownCustomLogos.ContainsKey(entry.Team)) continue;
            if (appliedAt.TryGetValue(entry.Team, out var already) && already >= entry.UpdatedAtUtc) continue;

            try
            {
                byte[] bytes = await Http.GetByteArrayAsync(entry.Url, ct);
                if (bytes.Length == 0) continue;
                if (WebBridge.WriteTeamLogoFile(entry.Team, bytes) == null) continue;
                appliedAt[entry.Team] = entry.UpdatedAtUtc;
                updated.Add(entry.Team);
            }
            catch { /* one team failing (bad file, network blip) shouldn't stop the rest */ }
        }

        if (updated.Count > 0)
            ConfigStore.SavePublicTeamLogoSyncManifest(manifest with { AppliedAtUtc = appliedAt });
        return updated;
    }
}
