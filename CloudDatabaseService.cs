using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>
/// Thin REST client for the Supabase project described in BANDROOM_STREAMER_MASTER_PROMPT.md
/// ("System 1: Cloud Database Migration"). Talks to Supabase's auto-generated PostgREST API over
/// plain HttpClient (no supabase-csharp package needed -- every table is just a REST endpoint).
///
/// v1 scope: a write-through mirror of team profiles into a `team_profiles` table
/// (team_name TEXT PRIMARY KEY, config JSONB, updated_at TIMESTAMPTZ), NOT the fully-normalized
/// teams/event_triggers/songs/team_configs schema from the master prompt. That schema is still
/// the end-goal for cross-device querying ("all touchdown songs for SEC teams") and is captured
/// in schema.sql for when System 3 (marketplace metadata) needs it, but mapping every TriggerEntry
/// to normalized team_id/trigger_id/song_id foreign keys is a bigger migration than "make the
/// existing per-team profile visible from other devices" requires. A JSONB blob keyed by team name
/// mirrors ConfigStore's own ProfilesFolder\{team}.json shape exactly, so there's no lossy mapping.
///
/// ConfigStore.SaveProfile pushes here best-effort (fire-and-forget, errors swallowed to CrashLog)
/// AFTER the local JSON write already succeeded -- local disk stays the single source of truth for
/// every read in this app (LoadProfile never calls out to the network), so Bandroom keeps working
/// exactly as before with no internet at all. This class only ever adds visibility from other
/// devices (e.g. browsing/editing team_profiles from the Supabase table editor on a phone); it does
/// not yet make this app itself read from the cloud.
/// </summary>
internal static class CloudDatabaseService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>True once both a Supabase project URL and anon key have been configured (see
    /// ConfigStore.LoadSupabaseSettings) -- every method here no-ops instead of throwing when this
    /// is false, so callers can fire-and-forget without checking first.</summary>
    public static bool IsConfigured
    {
        get
        {
            var (url, key) = ConfigStore.LoadSupabaseSettings();
            return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key);
        }
    }

    static HttpRequestMessage NewRequest(HttpMethod method, string path, string supabaseUrl, string anonKey)
    {
        var req = new HttpRequestMessage(method, $"{supabaseUrl.TrimEnd('/')}/rest/v1/{path}");
        req.Headers.Add("apikey", anonKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anonKey);
        return req;
    }

    /// <summary>Upserts one row into `team_profiles` (see schema.sql). Best-effort: any failure
    /// (no internet, Supabase not configured, table missing, etc.) is logged and swallowed --
    /// callers must never let a cloud push failure affect the local save that already happened.</summary>
    public static async Task PushTeamProfileAsync(string teamName, List<TriggerEntry> entries)
    {
        var (url, key) = ConfigStore.LoadSupabaseSettings();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;

        try
        {
            var payload = new { team_name = teamName, config = entries, updated_at = DateTime.UtcNow };
            var req = NewRequest(HttpMethod.Post, "team_profiles", url, key);
            // Prefer: resolution=merge-duplicates -- PostgREST's upsert-on-conflict, matching
            // team_profiles' team_name PRIMARY KEY (see schema.sql) so re-saving an existing
            // team's profile updates its row instead of erroring on a duplicate key.
            req.Headers.Add("Prefer", "resolution=merge-duplicates");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync();
                CrashLog.Write($"CloudDatabaseService push failed for {teamName}",
                    new HttpRequestException($"{(int)resp.StatusCode} {body}"));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write($"CloudDatabaseService push failed for {teamName}", ex);
        }
    }

    /// <summary>Fetches one team's cloud-mirrored profile, or null if not configured, unreachable,
    /// or the team has no row yet. Not called anywhere in the hot LoadProfile path today (see class
    /// remarks) -- available for a future "restore from cloud" / multi-device pull action.</summary>
    public static async Task<List<TriggerEntry>?> PullTeamProfileAsync(string teamName)
    {
        var (url, key) = ConfigStore.LoadSupabaseSettings();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return null;

        try
        {
            string encoded = Uri.EscapeDataString(teamName);
            var req = NewRequest(HttpMethod.Get, $"team_profiles?team_name=eq.{encoded}&select=config", url, key);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
            var configElement = doc.RootElement[0].GetProperty("config");
            return JsonSerializer.Deserialize<List<TriggerEntry>>(configElement.GetRawText());
        }
        catch (Exception ex)
        {
            CrashLog.Write($"CloudDatabaseService pull failed for {teamName}", ex);
            return null;
        }
    }
}
