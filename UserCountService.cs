using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SupremeStadiumSoundSelector;

/// <summary>Anonymous "how many people have Bandroom open right now" ticker. Pings a tiny
/// Cloudflare Worker every 60s with a random per-install GUID (no IP/hostname/anything
/// identifying is sent beyond what the HTTP request itself carries); the worker counts
/// distinct GUIDs seen in the last 2 minutes. Entirely best-effort -- any failure here
/// (no endpoint configured yet, network down, worker not deployed) is swallowed silently
/// so it can never affect the app's real functionality.</summary>
internal static class UserCountService
{
    // Set this once cloudflare-usercount/DEPLOY.md has been run and you have a real
    // workers.dev URL. Left blank == feature quietly does nothing.
    const string Endpoint = "";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    static readonly string InstallIdPath = Path.Combine(AppContext.BaseDirectory, ".install_id");
    static readonly Lazy<string> InstallId = new(LoadOrCreateInstallId);

    static int _lastCount;
    static DateTime _lastCountFetch = DateTime.MinValue;

    static string LoadOrCreateInstallId()
    {
        try
        {
            if (File.Exists(InstallIdPath))
            {
                string existing = File.ReadAllText(InstallIdPath).Trim();
                if (existing.Length > 0) return existing;
            }
        }
        catch { /* fall through to generating a fresh one */ }

        string id = Guid.NewGuid().ToString("N");
        try { File.WriteAllText(InstallIdPath, id); } catch { /* non-critical */ }
        return id;
    }

    /// <summary>Fire-and-forget background loop; call once at startup.</summary>
    public static void StartHeartbeat(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Endpoint)) return;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var payload = JsonSerializer.Serialize(new { id = InstallId.Value });
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    await Http.PostAsync($"{Endpoint}/heartbeat", content, ct);
                }
                catch { /* best-effort */ }

                try { await Task.Delay(TimeSpan.FromSeconds(60), ct); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    /// <summary>Cached ~20s so the UI can poll freely without hammering the worker.</summary>
    public static async Task<int?> GetCountAsync()
    {
        if (string.IsNullOrEmpty(Endpoint)) return null;
        if (DateTime.UtcNow - _lastCountFetch < TimeSpan.FromSeconds(20)) return _lastCount;

        try
        {
            var json = await Http.GetStringAsync($"{Endpoint}/count");
            using var doc = JsonDocument.Parse(json);
            _lastCount = doc.RootElement.GetProperty("count").GetInt32();
            _lastCountFetch = DateTime.UtcNow;
            return _lastCount;
        }
        catch
        {
            return null; // stay silent in the UI rather than show a stale/wrong number
        }
    }
}
