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
/// Opens system default browser on launch.
/// </summary>
public partial class MainWindow : Window
{
    static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aiff", ".flac" };

    // ---- State (mirrors WebMainForm.cs exactly) ----
    private List<TriggerEntry> _config = new();
    private readonly KeyboardHook _hook = new();
    private readonly MacGameWatcher _watcher = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly MacWebBridge _bridge;
    private bool _watching;
    private bool _windowFound;
    private TeamColor? _homeTeam, _awayTeam;
    private List<TriggerEntry>? _homeConfig, _awayConfig;
    private string? _possession;
    private bool _matchupLocked;
    private readonly EventRouter _router;

    // Match Windows: both sides fire now
    private const bool HomeOnlyEventsForNow = false;
    private bool _useEngineForEvents = true;

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

        // Lead-in whistle
        if (File.Exists(ConfigStore.LeadInWhistlePath))
        {
            AudioPlayer.LeadInClipPath = ConfigStore.LeadInWhistlePath;
            AudioPlayer.LeadInEnabled = ConfigStore.LoadLeadInWhistleEnabled();
        }

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
            StartWebServer();

            string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwroot))
                wwwroot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));

            if (Directory.Exists(wwwroot))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = "http://localhost:18765/index.html",
                    UseShellExecute = false,
                };
                System.Diagnostics.Process.Start(psi);

                LoadingText.Text = $"Bandroom\nhttp://localhost:18765\n17 evaluators · {ConfigStore.AllEngineEventKeys.Length} events";
            }
            else
            {
                LoadingText.Text = $"wwwroot not found at: {wwwroot}";
            }

            PlayDraftChime();
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Error: {ex.Message}";
        }
    }

    /// <summary>The shared "draft chime" for app open.</summary>
    static void PlayDraftChime()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "nfl-draft-chime.mp3");
        AudioPlayer.Play(path);
    }

    /// <summary>The GAMETIME confirmation cue.</summary>
    static void PlayGametimeSound()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "gametime-tackle.mp3");
        AudioPlayer.Play(path);
    }

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
        if (path.StartsWith("/avatar/")) return Path.Combine(ConfigStore.AvatarFolder, path[8..]);
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
        new Bandroom.Core.Helpers.NoPuntReturnHelper(),
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
            if (_homeConfig == null || _awayConfig == null) return;
            string side = _possession ?? "home";

            foreach (var evt in events)
            {
                bool routesLikeDefense = evt.EventKey.StartsWith("Defense:") || evt.EventKey == "Penalty: Offense";
                string routedSide = routesLikeDefense
                    ? (side == "home" ? "away" : "home")
                    : side;

                bool sideAllowed = HomeOnlyEventsForNow ? routedSide == "home" : true;
                if (sideAllowed)
                {
                    string result = FireEventForSide(routedSide, evt.EventKey);
                    OnLog($"[engine] {evt.EventKey} -> {routedSide}: {result}");
                }
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

    private static readonly string OcrLogPath = Path.Combine(AppContext.BaseDirectory, "ocr_debug.log");
    private static readonly object OcrLogLock = new();

    private void OnLog(string message)
    {
        lock (OcrLogLock)
        {
            try
            {
                File.AppendAllText(OcrLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                if (new FileInfo(OcrLogPath).Length > 2_000_000)
                {
                    var lines = File.ReadAllLines(OcrLogPath);
                    File.WriteAllLines(OcrLogPath, lines.Skip(Math.Max(0, lines.Length - 2000)));
                }
            }
            catch { }
        }
    }

    // =========================================================================
    // Core event wiring
    // =========================================================================

    static readonly Dictionary<string, string> LegacyDownEventAlias = new()
    {
        ["Offense: Earned First Down"] = "down:1st",
        ["Offense: Second Down"] = "down:2nd",
        ["Offense: Third Down"] = "down:3rd",
        ["Offense: Fourth Down"] = "down:4th",
    };

    /// <summary>Returns what happened so callers can see "fired", "no song assigned", etc.</summary>
    string FireEventForSide(string side, string eventName, bool bypassCooldown = false)
    {
        var config = side == "home" ? _homeConfig : _awayConfig;
        if (config == null) return "no-profile";

        // Try the team's own profile first, then fall back to Generic profile
        var entry = config.FirstOrDefault(e => e.Event == eventName);
        if (entry == null || string.IsNullOrWhiteSpace(entry.AudioFile))
        {
            var generic = ConfigStore.GetGenericProfile();
            entry = generic?.FirstOrDefault(e => e.Event == eventName);
        }

        if ((entry == null || string.IsNullOrWhiteSpace(entry.AudioFile))
            && LegacyDownEventAlias.TryGetValue(eventName, out var legacyTrigger))
        {
            var legacyEntry = config.FirstOrDefault(e => e.Trigger.Equals(legacyTrigger, StringComparison.OrdinalIgnoreCase));
            if (legacyEntry != null && !string.IsNullOrWhiteSpace(legacyEntry.AudioFile))
                entry = legacyEntry;
        }

        if (entry == null || string.IsNullOrWhiteSpace(entry.AudioFile)) return "unassigned";

        if (bypassCooldown) AudioPlayer.ClearCooldown(entry.AudioFile);
        if (!File.Exists(entry.AudioFile)) return "file-missing";
        FireEvent(entry, side == "home" ? AudioPlayer.HomeVolume : AudioPlayer.AwayVolume);
        return "fired:" + Path.GetFileName(entry.AudioFile);
    }

    static DateTime _lastSongTriggerCloudSync = DateTime.MinValue;

    void FireEvent(TriggerEntry entry, float? volumeOverride = null)
    {
        float eventVolumeScale = Math.Clamp(entry.Volume, 0, 100) / 100f;

        if (!string.IsNullOrWhiteSpace(entry.AudioFile) && File.Exists(entry.AudioFile))
        {
            float mainVolume = (volumeOverride ?? AudioPlayer.MasterVolume) * eventVolumeScale;
            AudioPlayer.Play(entry.AudioFile, mainVolume, interruptPrevious: true);
            RecordSongTriggered(entry.Event);

            // PA Announcer layer: plays concurrently with main cue
            if (!string.IsNullOrWhiteSpace(entry.PaAudioFile) && File.Exists(entry.PaAudioFile))
                AudioPlayer.Play(entry.PaAudioFile, AudioPlayer.PaVolume * eventVolumeScale, interruptPrevious: false);
        }
    }

    static void RecordSongTriggered(string eventName)
    {
        var current = ConfigStore.LoadUserProfile();
        var eventCounts = new Dictionary<string, int>(current.EventCounts);
        if (!string.IsNullOrWhiteSpace(eventName))
            eventCounts[eventName] = eventCounts.GetValueOrDefault(eventName) + 1;
        var updated = current with { SongsTriggered = current.SongsTriggered + 1, EventCounts = eventCounts };
        ConfigStore.SaveUserProfile(updated);

        if (DateTime.UtcNow - _lastSongTriggerCloudSync < TimeSpan.FromSeconds(30)) return;
        _lastSongTriggerCloudSync = DateTime.UtcNow;
        _ = ProfileSyncService.PushAsync(updated);
    }

    static void RecordGameWatched(string homeName, string awayName)
    {
        var current = ConfigStore.LoadUserProfile();
        var byTeam = new Dictionary<string, int>(current.GamesWatchedByTeam);
        foreach (var team in new[] { homeName, awayName })
            byTeam[team] = byTeam.GetValueOrDefault(team) + 1;

        var today = DateTime.Now.Date;
        int streak = current.StreakCurrentDays;
        if (current.StreakLastActiveDate == today) { }
        else if (current.StreakLastActiveDate == today.AddDays(-1)) streak += 1;
        else streak = 1;

        var updated = current with
        {
            GamesWatched = current.GamesWatched + 1,
            GamesWatchedByTeam = byTeam,
            StreakCurrentDays = streak,
            StreakLastActiveDate = today,
        };
        ConfigStore.SaveUserProfile(updated);
        _ = ProfileSyncService.PushAsync(updated);
    }

    void SaveCurrentTeamProfile()
    {
        ConfigStore.SaveProfile(SupremeStadiumSoundSelector.Theme.ActiveTeam.Name, _config);
        RefreshHomeAwayConfigIfNeeded(SupremeStadiumSoundSelector.Theme.ActiveTeam.Name);
    }

    void RefreshHomeAwayConfigIfNeeded(string savedTeamName)
    {
        if (_homeTeam is { } home && string.Equals(home.Name, savedTeamName, StringComparison.OrdinalIgnoreCase))
            _homeConfig = ConfigStore.LoadProfile(savedTeamName);
        if (_awayTeam is { } away && string.Equals(away.Name, savedTeamName, StringComparison.OrdinalIgnoreCase))
            _awayConfig = ConfigStore.LoadProfile(savedTeamName);
    }

    // =========================================================================
    // Public methods called from MacWebBridge
    // =========================================================================

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

    public List<TriggerEntry> GetEvents(string? category)
    {
        if (string.IsNullOrEmpty(category) || category == "All") return _config;
        return _config.Where(e => CategoryMap.Resolve(e) == category).ToList();
    }

    // ---- Assign Track (web-native, no native dialog dependency) ----

    public void OpenAssignTrackFromWeb(string trigger) { /* Native dialog stub — web UI handles this */ }

    public void OpenAssignPaTrackFromWeb(string trigger) { /* Native dialog stub — web UI handles this */ }

    public void PreviewEventFromWeb(string trigger)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        float eventVolumeScale = Math.Clamp(entry.Volume, 0, 100) / 100f;
        if (!string.IsNullOrWhiteSpace(entry.AudioFile) && File.Exists(entry.AudioFile))
            AudioPlayer.Play(entry.AudioFile, AudioPlayer.MasterVolume * eventVolumeScale, interruptPrevious: true, isPreview: true);
        if (!string.IsNullOrWhiteSpace(entry.PaAudioFile) && File.Exists(entry.PaAudioFile))
            AudioPlayer.Play(entry.PaAudioFile, AudioPlayer.PaVolume * eventVolumeScale, interruptPrevious: false, isPreview: true);
    }

    public void StopPreviewFromWeb() => AudioPlayer.StopAll();

    // ---- Clipping-island assign flow ----

    public string GetTrackLibraryFromWeb()
    {
        var library = new List<string>();
        if (Directory.Exists(ConfigStore.SongsFolder))
            library.AddRange(Directory.GetFiles(ConfigStore.SongsFolder, "*", SearchOption.AllDirectories)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));

        var items = library.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(p => new { name = Path.GetFileNameWithoutExtension(p), path = p });
        return JsonSerializer.Serialize(items);
    }

    public void PreviewLocalFileFromWeb(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            AudioPlayer.Play(path, AudioPlayer.MasterVolume, interruptPrevious: true, isPreview: true);
    }

    public void AssignTrackFileFromWeb(string trigger, bool isPa, string path)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        if (isPa) entry.PaAudioFile = path; else entry.AudioFile = path;
        ConfigStore.Save(_config);
        SaveCurrentTeamProfile();
    }

    public void ClearTrackAssignmentFromWeb(string trigger, bool isPa) => AssignTrackFileFromWeb(trigger, isPa, "");

    public string? BrowseForAudioFileFromWeb()
    {
        // On macOS, use NSOpenPanel via a synchronous Task.Run
        // For now, return null — Avalonia file picker integration is complex
        return null;
    }

    public void OpenTrimmerFromWeb(string trigger, bool isPa)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        string currentPath = isPa ? entry.PaAudioFile : entry.AudioFile;
        if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath)) return;
        // TrimmerForm is WinForms-specific — on Mac this opens nothing
        // The assign flow uses the web clipping island instead
    }

    public int GetEventVolumeFromWeb(string trigger) => _config.FirstOrDefault(e => e.Trigger == trigger)?.Volume ?? 100;

    public void SetEventVolumeFromWeb(string trigger, int percent)
    {
        var entry = _config.FirstOrDefault(e => e.Trigger == trigger);
        if (entry == null) return;
        entry.Volume = Math.Clamp(percent, 0, 100);
        SaveCurrentTeamProfile();
    }

    // ---- Watching toggle ----

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

    public void UnlockMatchupFromWeb() => _matchupLocked = false;

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

    public string FireTestEventFromWeb(string side, string eventKey) => FireEventForSide(side, eventKey, bypassCooldown: true);

    public string GetAllEventKeysFromWeb() => JsonSerializer.Serialize(ConfigStore.AllEngineEventKeys);

    // ---- Volume controls ----

    public void SetVolumeFromWeb(int percent) => AudioPlayer.MasterVolume = percent / 100f;
    public void SetHomeVolumeFromWeb(int percent) => AudioPlayer.HomeVolume = percent / 100f;
    public void SetAwayVolumeFromWeb(int percent) => AudioPlayer.AwayVolume = percent / 100f;
    public int GetHomeVolumeFromWeb() => (int)(AudioPlayer.HomeVolume * 100);
    public int GetAwayVolumeFromWeb() => (int)(AudioPlayer.AwayVolume * 100);
    public void SetPaVolumeFromWeb(int percent) => AudioPlayer.PaVolume = percent / 100f;
    public int GetPaVolumeFromWeb() => (int)(AudioPlayer.PaVolume * 100);
    public void SetFadeDelayFromWeb(int seconds) => AudioPlayer.FadeStartSeconds = seconds;

    // ---- Matchup ----

    public void SetGameTeamsFromWeb(string homeName, string awayName)
    {
        _homeTeam = TeamColors.All.FirstOrDefault(t => t.Name == homeName);
        _awayTeam = TeamColors.All.FirstOrDefault(t => t.Name == awayName);
        _homeConfig = ConfigStore.ListProfiles().Contains(homeName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(homeName) : ConfigStore.BuildDefault();
        _awayConfig = ConfigStore.ListProfiles().Contains(awayName, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(awayName) : ConfigStore.BuildDefault();
        _watcher.UserIsHome = true;
        _useEngineForEvents = true;
        _possession = null;

        // Auto-assign default songs if profile is empty
        bool homeHasAssignments = _homeConfig.Any(e => !string.IsNullOrWhiteSpace(e.AudioFile));
        bool awayHasAssignments = _awayConfig.Any(e => !string.IsNullOrWhiteSpace(e.AudioFile));
        if (!homeHasAssignments) ConfigStore.ImportDefaultPackForTeam(homeName, _homeConfig);
        if (!awayHasAssignments) ConfigStore.ImportDefaultPackForTeam(awayName, _awayConfig);
    }

    public void ConfirmGametimeFromWeb(string homeName, string awayName)
    {
        SetGameTeamsFromWeb(homeName, awayName);
        _matchupLocked = true;
        _hook.Start();
        _watcher.Start();
        _watching = true;
        PlayGametimeSound();
        RecordGameWatched(homeName, awayName);
    }

    public string? GetGameTeamsFromWeb() =>
        _homeTeam.HasValue && _awayTeam.HasValue
            ? JsonSerializer.Serialize(new { home = _homeTeam.Value.Name, away = _awayTeam.Value.Name, locked = _matchupLocked })
            : null;

    public bool IsMatchupLockedFromWeb() => _matchupLocked;

    // ---- Team selection & profiles ----

    public void SelectTeamFromWeb(string name)
    {
        var team = TeamColors.All.FirstOrDefault(t => t.Name == name);
        if (team.Name == null || team.Name == SupremeStadiumSoundSelector.Theme.ActiveTeam.Name) return;
        SaveCurrentTeamProfile();
        SupremeStadiumSoundSelector.Theme.ActiveTeam = team;
        _config = ConfigStore.ListProfiles().Contains(team.Name, StringComparer.OrdinalIgnoreCase)
            ? ConfigStore.LoadProfile(team.Name)
            : ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
    }

    public string SaveProfileAsFromWeb(string? name)
    {
        ConfigStore.Save(_config);
        string target = string.IsNullOrWhiteSpace(name) ? SupremeStadiumSoundSelector.Theme.ActiveTeam.Name : name.Trim();
        ConfigStore.SaveProfile(target, _config);
        RefreshHomeAwayConfigIfNeeded(target);
        return target;
    }

    public string? GetProfileSavedAtFromWeb(string name) => ConfigStore.GetProfileSavedAt(name)?.ToString("h:mm tt");

    // ---- Backgrounds ----

    public async Task<bool> DownloadAndSetTeamBackgroundFromWeb(string team, string url)
    {
        string? saved = await TeamBackgroundDownloadService.DownloadAndSaveAsync(team, url);
        return saved != null;
    }

    public bool SetTeamBackgroundFromDownloadFromWeb(string downloadId)
    {
        var entry = ConfigStore.LoadMarketplaceDownloads().FirstOrDefault(e => e.Id == downloadId && e.Type == "image");
        if (entry == null) return false;
        string? saved = TeamBackgroundDownloadService.SetFromLocalFile(entry.School ?? "", entry.Path);
        return saved != null;
    }

    // ---- Profiles ----

    public string ImportLocalSongFromWeb() => JsonSerializer.Serialize(new { success = false });

    public void CopyCurrentToAllTeamsFromWeb()
    {
        var snapshot = _config.Select(e => new TriggerEntry { Trigger = e.Trigger, Event = e.Event, AudioFile = e.AudioFile }).ToList();
        Task.Run(() =>
        {
            foreach (var team in TeamColors.All)
            {
                if (team.Name == SupremeStadiumSoundSelector.Theme.ActiveTeam.Name) continue;
                ConfigStore.SaveProfile(team.Name, snapshot);
            }
        });
    }

    public void DeleteCurrentProfileFromWeb()
    {
        ConfigStore.DeleteProfile(SupremeStadiumSoundSelector.Theme.ActiveTeam.Name);
        _config = ConfigStore.BuildDefault();
        ConfigStore.Save(_config);
    }

    public void ExportProfileFromWeb() { }
    public void ImportProfileFromWeb() { }
    public void ExportUserProfileFromWeb() { }
    public void ImportUserProfileFromWeb() { }

    // ---- Default song pack ----

    public void DownloadDefaultSongPackFromWeb()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                bool ok = await DefaultSongPackService.DownloadAndExtractAsync(
                    (frac, downloaded, total) => { }, _lifetimeCts.Token);
            }
            catch { }
        });
    }

    public string? BrowseForSongPackZipFromWeb() => null;

    public void ImportDefaultSongPackZipFromWeb(string zipPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await DefaultSongPackService.ExtractExistingZipAsync(zipPath,
                    frac => { }, _lifetimeCts.Token);
            }
            catch { }
        });
    }

    // ---- Open Settings (stub — web UI handles audio settings) ----
    public void OpenSettingsFromWeb() { }

    // ---- Window controls ----

    public void BeginWindowDrag() { }

    public void MinimizeWindowFromWeb() => WindowState = WindowState.Minimized;
    public void MaximizeWindowFromWeb() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal : WindowState.Maximized;
    public void CloseWindowFromWeb() => Close();

    public void PlayUiClickSoundFromWeb()
    {
        // Synthesized tick — minimal implementation for Mac
        Task.Run(() =>
        {
            try
            {
                string tempDir = Path.GetTempPath();
                string clickPath = Path.Combine(tempDir, "bandroom_click.wav");
                int sampleRate = 44100;
                int n = 10 * sampleRate / 1000;
                var buf = new float[n];
                var rng = new Random();
                float prev = 0f;
                for (int i = 0; i < n; i++)
                {
                    float env = MathF.Pow(1f - (float)i / n, 4f);
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    prev = prev * 0.6f + noise * 0.4f;
                    buf[i] = prev * 0.14f * env;
                }
                var bytes = new byte[buf.Length * 2];
                for (int i = 0; i < buf.Length; i++)
                {
                    short s = (short)(Math.Clamp(buf[i], -1f, 1f) * 32767);
                    bytes[i * 2] = (byte)(s & 0xFF);
                    bytes[i * 2 + 1] = (byte)(s >> 8);
                }

                using (var fs = new FileStream(clickPath, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs))
                {
                    int dataSize = bytes.Length;
                    bw.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 });
                    bw.Write(36 + dataSize);
                    bw.Write(new byte[] { 0x57, 0x41, 0x56, 0x45 });
                    bw.Write(new byte[] { 0x66, 0x6D, 0x74, 0x20 });
                    bw.Write(16);
                    bw.Write((short)1);
                    bw.Write((short)1);
                    bw.Write(sampleRate);
                    bw.Write(sampleRate * 2);
                    bw.Write((short)2);
                    bw.Write((short)16);
                    bw.Write(new byte[] { 0x64, 0x61, 0x74, 0x61 });
                    bw.Write(dataSize);
                    bw.Write(bytes);
                }

                // Quick play through afplay for the click sound (short, disposable)
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/afplay",
                    Arguments = $"\"{clickPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(500);
                try { File.Delete(clickPath); } catch { }
            }
            catch { }
        });
    }
}