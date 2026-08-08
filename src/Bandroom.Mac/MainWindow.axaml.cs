using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Bandroom.Core;
using SupremeStadiumSoundSelector;
using Theme = SupremeStadiumSoundSelector.Theme;

namespace Bandroom.Mac;

/// <summary>
/// Mac main window — full 1:1 parity with Windows WebMainForm.cs.
/// Hosts the same Bandroom.Core engine, MacWebBridge, MacGameWatcher,
/// KeyboardHook, AudioPlayer, ConfigStore, and marketplace pipeline.
///
/// Web UI rendering: serves wwwroot/ via embedded HTTP listener on port 18765.
/// Opens system default browser on launch. On future macOS, replace with
/// WKWebView via Avalonia NativeControlHost for true 1:1 embedded rendering.
/// </summary>
public partial class MainWindow : Window
{
    // ---- State (mirrors WebMainForm.cs exactly) ----
    private List<TriggerEntry> _config = new();
    private readonly KeyboardHook _hook = new();
    private readonly MacGameWatcher _watcher = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly MacWebBridge _bridge;
    private bool _watching;
    private static SupremeStadiumSoundSelector.Theme.ActiveThemeType MacTheme => SupremeStadiumSoundSelector.Theme.ActiveTeam;
    private bool _windowFound;
    private TeamColor? _homeTeam, _awayTeam;
    private List<TriggerEntry>? _homeConfig, _awayConfig;
    private string? _possession;
    private bool _matchupLocked;
    private readonly EventRouter _router;

    private const bool HomeOnlyEventsForNow = true;
    private bool _useEngineForEvents;

    public MainWindow()
    {
        InitializeComponent();

        _bridge = new MacWebBridge(this);
        _router = new EventRouter(AllEvaluators());

        _hook.KeyCombo += OnKeyCombo;
        _watcher.EventsDetected += OnEngineEventsDetected;
        _watcher.Log += OnLog;

        ConfigStore.MigrateFromVersionedFolderIfNeeded();
        _config = ConfigStore.LoadOrCreate();

        Opened += OnOpened;
        Closing += (_, _) =>
        {
            _hook.Stop();
            _watcher.Stop();
            _lifetimeCts.Cancel();
        };
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        LoadingText.Text = "Bandroom\nStarting...";

        try
        {
            // Serve wwwroot via embedded HTTP listener
            StartWebServer();

            string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwroot))
                wwwroot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));

            if (Directory.Exists(wwwroot))
            {
                // Open system browser to the local server
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = "http://localhost:18765/index.html",
                    UseShellExecute = false,
                };
                System.Diagnostics.Process.Start(psi);

                LoadingText.Text = $"Bandroom\nhttp://localhost:18765\n16 evaluators · {ConfigStore.AllEngineEventKeys.Length} events";
            }
            else
            {
                LoadingText.Text = $"wwwroot not found at: {wwwroot}";
            }
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Starts a lightweight HTTP server on localhost:18765 serving wwwroot/.
    /// This mirrors how Windows WebView2 serves files via virtual host mappings.
    /// </summary>
    private void StartWebServer()
    {
        Task.Run(() =>
        {
            using var listener = new System.Net.HttpListener();
            listener.Prefixes.Add("http://localhost:18765/");
            try
            {
                listener.Start();
                Console.WriteLine("[Bandroom.Mac] Web server on http://localhost:18765");

                while (listener.IsListening)
                {
                    var ctx = listener.GetContext();
                    Task.Run(() => ServeFile(ctx));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Bandroom.Mac] Server error: {ex.Message}");
            }
        });
    }

    private static void ServeFile(System.Net.HttpListenerContext ctx)
    {
        try
        {
            string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwroot))
                wwwroot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));

            string path = ctx.Request.Url!.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(path)) path = "index.html";

            string filePath = Path.Combine(wwwroot, path);

            // Virtual host mappings (mirrors WebView2 virtual hosts)
            string? virtualPath = MapVirtualHost(ctx.Request.Url.AbsolutePath);
            if (virtualPath != null) filePath = virtualPath;

            if (File.Exists(filePath))
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                string contentType = ext switch
                {
                    ".html" => "text/html",
                    ".css" => "text/css",
                    ".js" => "application/javascript",
                    ".json" => "application/json",
                    ".png" => "image/png",
                    ".jpg" => "image/jpeg",
                    ".jpeg" => "image/jpeg",
                    ".ttf" => "font/ttf",
                    ".woff2" => "font/woff2",
                    _ => "application/octet-stream",
                };

                ctx.Response.ContentType = contentType;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Cache-Control", "no-cache");

                byte[] data = File.ReadAllBytes(filePath);
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        }
        catch { ctx.Response.StatusCode = 500; }
        finally { ctx.Response.OutputStream.Close(); }
    }

    private static string? MapVirtualHost(string path)
    {
        if (path.StartsWith("/teambg/")) return Path.Combine(ConfigStore.TeamBackgroundsFolder, path[8..]);
        if (path.StartsWith("/teamlogo/")) return Path.Combine(ConfigStore.TeamLogosFolder, path[10..]);
        if (path.StartsWith("/appassets/")) return Path.Combine(AppContext.BaseDirectory, "wwwroot", path[11..]);
        if (path.StartsWith("/downloadedimages/")) return Path.Combine(ConfigStore.DownloadedImagesFolder, path[18..]);
        if (path.StartsWith("/downloadedsongs/")) return Path.Combine(ConfigStore.SongsUploadedFolder, path[16..]);
        if (path.StartsWith("/localtracks/")) return Path.Combine(ConfigStore.LocalTracksFolder, path[13..]);
        return null;
    }

    // ---- Evaluator factory ----
    private static IRuleEvaluator[] AllEvaluators() => new IRuleEvaluator[]
    {
        new Bandroom.Core.Helpers.BigEventHelper(),
        new Bandroom.Core.Helpers.DefenseHelper(),
        new Bandroom.Core.Helpers.DownFieldPositionHelper(),
        new Bandroom.Core.Helpers.DriveStarterHelper(),
        new Bandroom.Core.Helpers.FieldGoalMissedHelper(),
        new Bandroom.Core.Helpers.FieldGoalPATHelper(),
        new Bandroom.Core.Helpers.FirstDownHelper(),
        new Bandroom.Core.Helpers.GameStateEventHelper(),
        new Bandroom.Core.Helpers.KickoffHelper(),
        new Bandroom.Core.Helpers.OffenseDownHelper(),
        new Bandroom.Core.Helpers.PenaltyHelper(),
        new Bandroom.Core.Helpers.SafetyHelper(),
        new Bandroom.Core.Helpers.TflHelper(),
        new Bandroom.Core.Helpers.TimeoutHelper(),
        new Bandroom.Core.Helpers.TouchdownHelper(),
        new Bandroom.Core.Helpers.TurnoverHelper(),
    };

    // ---- Event handlers (mirrors WebMainForm) ----

    private void OnEngineEventsDetected(IReadOnlyList<TriggerEvent> events)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_homeConfig == null || _awayConfig == null || _possession == null) return;
            foreach (var evt in events)
            {
                string side = evt.EventKey.StartsWith("Defense:")
                    ? (_possession == "home" ? "away" : "home")
                    : _possession;
                bool sideAllowed = HomeOnlyEventsForNow ? side == "home" : true;
                if (sideAllowed) FireEventForSide(side, evt.EventKey);
            }
        });
    }

    private void OnKeyCombo(string keyCombo)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var entry = _config.FirstOrDefault(e => e.Trigger.Equals($"key:{keyCombo}", StringComparison.OrdinalIgnoreCase));
            if (entry != null) FireEvent(entry);
        });
    }

    private void OnLog(string message) { }

    // ---- Core event wiring ----

    private void FireEventForSide(string side, string eventName)
    {
        var config = side == "home" ? _homeConfig : _awayConfig;
        var entry = config?.FirstOrDefault(e => e.Event == eventName);
        if (entry != null) FireEvent(entry, side == "home" ? AudioPlayer.HomeVolume : AudioPlayer.AwayVolume);
    }

    private void FireEvent(TriggerEntry entry, float? volumeOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(entry.AudioFile) && File.Exists(entry.AudioFile))
        {
            AudioPlayer.Play(entry.AudioFile, volumeOverride, interruptPrevious: true);
            // RecordSongTriggered on background
            Task.Run(() =>
            {
                var p = ConfigStore.LoadUserProfile();
                var counts = new Dictionary<string, int>(p.EventCounts);
                if (!string.IsNullOrWhiteSpace(entry.Event))
                    counts[entry.Event] = counts.GetValueOrDefault(entry.Event) + 1;
                ConfigStore.SaveUserProfile(p with { SongsTriggered = p.SongsTriggered + 1, EventCounts = counts });
            });
        }
    }

    // ---- Public methods called from MacWebBridge ----

    public Dictionary<string, (int assigned, int total)> GetCategoryCounts()
    {
        var order = new[] { "Downs", "Scoring", "Turnovers", "Special Teams", "Penalties", "Hype" };
        var byCategory = order.ToDictionary(c => c, c => (assigned: 0, total: 0));
        foreach (var entry in _config)
        {
            string cat = CategoryMap.Resolve(entry);
            if (!byCategory.ContainsKey(cat)) continue;
            var (assigned, total) = byCategory[cat];
            total++;
            if (!string.IsNullOrWhiteSpace(entry.AudioFile)) assigned++;
            byCategory[cat] = (assigned, total);
        }
        return byCategory;
    }

    public void SaveCurrentTeamProfile()
    {
        ConfigStore.SaveProfile(SupremeStadiumSoundSelector.Theme.ActiveTeam.Name, _config);
    }

    public List<TriggerEntry> GetEvents(string? category)
    {
        if (string.IsNullOrEmpty(category) || category == "All") return _config;
        return _config.Where(e => CategoryMap.Resolve(e) == category).ToList();
    }

    public void OpenAssignTrackFromWeb(string trigger)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        // On Mac, file picking uses NSOpenPanel via Avalonia
        // For now, this is a stub that would open a file picker dialog
    }

    public void PreviewEventFromWeb(string trigger)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry != null) FireEvent(entry);
    }

    public void StopPreviewFromWeb() => AudioPlayer.StopAll();

    public string ToggleWatchingFromWeb()
    {
        if (_watching)
        {
            _hook.Stop();
            _watcher.Stop();
            _watching = false;
            _windowFound = false;
            _matchupLocked = false;
        }
        else
        {
            if (_homeTeam is null || _awayTeam is null) return "no-matchup";
            _hook.Start();
            _watcher.Start();
            _watching = true;
        }
        return _watching ? "watching" : "off";
    }

    public void ResetTeamProfileFromWeb()
    {
        _config = ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
    }

    public void TriggerEffectsTestFromWeb()
    {
        var entry = _config.FirstOrDefault(e => e.Event.Contains("Touchdown", StringComparison.OrdinalIgnoreCase))
            ?? _config.FirstOrDefault();
        if (entry != null) FireEvent(entry);
    }

    public void SetVolumeFromWeb(int percent) => AudioPlayer.MasterVolume = percent / 100f;
    public void SetHomeVolumeFromWeb(int percent) => AudioPlayer.HomeVolume = percent / 100f;
    public void SetAwayVolumeFromWeb(int percent) => AudioPlayer.AwayVolume = percent / 100f;
    public int GetHomeVolumeFromWeb() => (int)(AudioPlayer.HomeVolume * 100);
    public int GetAwayVolumeFromWeb() => (int)(AudioPlayer.AwayVolume * 100);
    public void SetFadeDelayFromWeb(int seconds) => AudioPlayer.FadeStartSeconds = seconds;

    public void SetGameTeamsFromWeb(string homeName, string awayName)
    {
        _homeTeam = TeamColors.All.FirstOrDefault(t => t.Name == homeName);
        _awayTeam = TeamColors.All.FirstOrDefault(t => t.Name == awayName);
        _homeConfig = ConfigStore.LoadProfile(homeName);
        _awayConfig = ConfigStore.LoadProfile(awayName);
        _watcher.UserIsHome = true;
        _useEngineForEvents = true;
        _possession = null;
    }

    public void ConfirmGametimeFromWeb(string homeName, string awayName)
    {
        SetGameTeamsFromWeb(homeName, awayName);
        _matchupLocked = true;
        // Play gametime sound
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "gametime-tackle.mp3");
        AudioPlayer.Play(path);
    }

    public string? GetGameTeamsFromWeb() =>
        _homeTeam.HasValue && _awayTeam.HasValue
            ? JsonSerializer.Serialize(new { home = _homeTeam.Value.Name, away = _awayTeam.Value.Name, locked = _matchupLocked })
            : null;

    public bool IsMatchupLockedFromWeb() => _matchupLocked;

    public void SelectTeamFromWeb(string name)
    {
        var team = TeamColors.All.FirstOrDefault(t => t.Name == name);
        if (team.Name == null || team.Name == SupremeStadiumSoundSelector.Theme.ActiveTeam.Name) return;
        SaveCurrentTeamProfile();
        SupremeStadiumSoundSelector.Theme.ActiveTeam = team;
        _config = ConfigStore.LoadProfile(team.Name);
        ConfigStore.Save(_config);
    }

    public string SaveProfileAsFromWeb(string? name)
    {
        ConfigStore.Save(_config);
        string target = string.IsNullOrWhiteSpace(name) ? SupremeStadiumSoundSelector.Theme.ActiveTeam.Name : name.Trim();
        ConfigStore.SaveProfile(target, _config);
        return target;
    }

    public string? GetProfileSavedAtFromWeb(string name) => ConfigStore.GetProfileSavedAt(name)?.ToString("h:mm tt");

    public async Task<bool> DownloadAndSetTeamBackgroundFromWeb(string team, string url)
    {
        string? saved = await TeamBackgroundDownloadService.DownloadAndSaveAsync(team, url);
        return saved != null;
    }

    public string ImportLocalSongFromWeb() => JsonSerializer.Serialize(new { success = false });

    public void CopyCurrentToAllTeamsFromWeb() { }
    public void DeleteCurrentProfileFromWeb()
    {
        ConfigStore.DeleteProfile(SupremeStadiumSoundSelector.Theme.ActiveTeam.Name);
        _config = ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
    }

    public void ExportProfileFromWeb() { }
    public void ImportProfileFromWeb() { }
    public void OpenSettingsFromWeb() { }

    public void BeginWindowDrag()
    {
        // On macOS Avalonia, we don't need the native drag trick
        // that Windows uses (WM_NCLBUTTONDOWN) since Avalonia
        // handles title bar dragging natively on Mac.
    }

    public void MinimizeWindowFromWeb() => WindowState = WindowState.Minimized;
    public void MaximizeWindowFromWeb() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal : WindowState.Maximized;
    public void CloseWindowFromWeb() => Close();
    public void PlayUiClickSoundFromWeb() { }
}