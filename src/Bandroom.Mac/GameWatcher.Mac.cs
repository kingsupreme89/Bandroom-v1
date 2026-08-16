using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bandroom.Core;
using Bandroom.Core.Helpers;
using SupremeStadiumSoundSelector;

namespace Bandroom.Mac;

/// <summary>One OCR'd HUD region for the Mac watcher: a crop box (as fractions of the screen)
/// plus a regex to pull the value out. Mirrors Windows GameWatcher.WatchedRegion, but the Mac
/// bridge does the actual cropping in Python (Vision OCR) so this only carries the definition.</summary>
internal sealed class MacRegion
{
    public required string Name;
    public required double X, Y, W, H;
    public required Regex Pattern;
}

/// <summary>
/// macOS game-screen watcher using screencapture + bundled Python OCR helper.
///
/// EXPANDED 2026-08-12 (Session 63, "Mac OCR parity"): was 4 regions (down/situation/quarter/
/// possession) with hardcoded 0/0 scores and a dead 0-clock. Now mirrors Windows GameWatcher.cs's
/// full region set (12 OCR regions) + the same sticky/two-tick-commit discipline, so the already
/// synced evaluator list can actually fire on Mac. Regions are built from the active
/// ScorebugPreset (same preset fields Windows' ApplyScorebugPreset reads). The Python bridge now
/// emits a "cycle_end" marker after each pass so the engine routes one full snapshot per scan,
/// not once per region line.
/// </summary>
internal sealed class MacGameWatcher
{
    public event Action<IReadOnlyList<TriggerEvent>>? EventsDetected;
    public event Action<string>? Log;
    /// <summary>Fires when the OCR-derived possession side flips ("home"/"away"/null). NOTE: this
    /// is a Mac-only best-effort OCR signal (the scorebug renders no literal "HOME"/"AWAY" text,
    /// so it rarely fires) and does NOT replace Windows' color/underline pixel sampling, which is
    /// still not implemented on Mac. Kept so the routing surface exists and doesn't regress.</summary>
    public event Action<string?>? PossessionChanged;
    public bool UserIsHome { get; set; }
    public string? HomeTeamName { get; set; }
    public string? AwayTeamName { get; set; }
    public string? HomeTeamMascot { get; set; }
    public string? AwayTeamMascot { get; set; }

    private EventRouter? _eventRouter;
    private PlaySnapshot _snapshotPrevious = new();
    private PlaySnapshot _snapshotCurrent = new();
    private CancellationTokenSource? _cts;
    private Process? _ocrProcess;
    private List<MacRegion> _regions = new();
    private Dictionary<string, Regex> _patternsByName = new(StringComparer.OrdinalIgnoreCase);
    private bool _isFirstEngineTick = true;

    // --- Regex patterns (1:1 with Windows GameWatcher._regions) ---
    static readonly Regex DownOrdinalPattern = new(@"\b(1st|2nd|3rd|4th)\b(?=\s*&)", RegexOptions.IgnoreCase);
    static readonly Regex DistancePattern = new(@"&\s*(-?\d+|inches|goal)", RegexOptions.IgnoreCase);
    static readonly Regex FlagPattern = new(@"\b(FLAG|PENALTY)\b", RegexOptions.IgnoreCase);
    static readonly Regex SituationPattern = new(@"\b(KICK\s*OFF|PAT\s*GOOD|TOUCHDOWN|INTERCEPTED|FUMBLE|TURNOVER|FAIR\s*CATCH|NO\s*RETURN|TIME\s*OUT)\b", RegexOptions.IgnoreCase);
    static readonly Regex QuarterPattern = new(@"\b(1st|2nd|3rd|4th)\b(?!\s*&)", RegexOptions.IgnoreCase);
    static readonly Regex PenaltyAgainstPattern = new(@"Against\s+[A-Za-z .]{3,30}", RegexOptions.IgnoreCase);
    static readonly Regex BannerPattern = new(@"\b(TOUCHDOWN|FIELD\s*GOAL|SAFETY)\b", RegexOptions.IgnoreCase);
    static readonly Regex PregameReadyPattern = new(@"\bREADY\b", RegexOptions.IgnoreCase);
    static readonly Regex TeamRunoutPattern = new(@"COLLEGE\s+FOOTBALL", RegexOptions.IgnoreCase);
    static readonly Regex ScorePattern = new(@"\b\d{1,2}\b");
    static readonly Regex ClockPattern = new(@"\b\d{1,2}:\d{2}\b");
    static readonly Regex PlayClockPattern = new(@"\b\d{1,2}\b");
    // Mac-only placeholder possession signal (no Windows equivalent region) — see PossessionChanged.
    static readonly Regex PossessionPattern = new(@"\b(HOME|AWAY)\b", RegexOptions.IgnoreCase);

    static ScorebugPreset ResolveActivePreset()
    {
        try { return ScorebugPreset.GetByName(ConfigStore.LoadScorebugPresetName()); }
        catch { return ScorebugPreset.KamsCbsScorebugV3; }
    }

    /// <summary>Builds the region set from a preset, mirroring Windows GameWatcher._regions
    /// initializer plus ApplyScorebugPreset's per-preset field reassignments exactly.</summary>
    static List<MacRegion> BuildRegions(ScorebugPreset preset)
    {
        // pregameready / teamrunout / playclock are intentionally NOT preset-adjusted in Windows
        // either (ApplyScorebugPreset never touches them), so their coordinates stay hardcoded the
        // same way. playclock keeps the fixed CBS band (0.83/0.14) exactly as the Windows init does.
        return new List<MacRegion>
        {
            new() { Name = "down", X = 0, Y = preset.BandFxY, W = 1.0, H = preset.BandFxH, Pattern = DownOrdinalPattern },
            new() { Name = "flag", X = 0, Y = preset.BandFxY, W = 1.0, H = preset.BandFxH, Pattern = FlagPattern },
            new() { Name = "situation", X = 0, Y = preset.BandFxY, W = 1.0, H = preset.BandFxH, Pattern = SituationPattern },
            new() { Name = "quarter", X = 0, Y = preset.BandFxY, W = 1.0, H = preset.BandFxH, Pattern = QuarterPattern },
            new() { Name = "penaltyagainst", X = preset.PenaltyAgainstFxX, Y = preset.PenaltyAgainstFxY, W = preset.PenaltyAgainstFxW, H = preset.PenaltyAgainstFxH, Pattern = PenaltyAgainstPattern },
            new() { Name = "banner", X = preset.BannerFxX, Y = preset.BannerFxY, W = preset.BannerFxW, H = preset.BannerFxH, Pattern = BannerPattern },
            new() { Name = "pregameready", X = 0.03, Y = 0.60, W = 0.95, H = 0.13, Pattern = PregameReadyPattern },
            new() { Name = "teamrunout", X = 0.30, Y = 0.38, W = 0.60, H = 0.26, Pattern = TeamRunoutPattern },
            new() { Name = "awayscore", X = preset.AwayScoreFxX, Y = preset.BandFxY, W = preset.AwayScoreFxW, H = preset.BandFxH, Pattern = ScorePattern },
            new() { Name = "homescore", X = preset.HomeScoreFxX, Y = preset.BandFxY, W = preset.HomeScoreFxW, H = preset.BandFxH, Pattern = ScorePattern },
            new() { Name = "clock", X = preset.ClockFxX, Y = preset.BandFxY, W = preset.ClockFxW, H = preset.BandFxH, Pattern = ClockPattern },
            new() { Name = "playclock", X = 0.70, Y = 0.83, W = 0.06, H = 0.14, Pattern = PlayClockPattern },
            new() { Name = "possession", X = 0, Y = preset.BandFxY, W = 1.0, H = preset.BandFxH, Pattern = PossessionPattern },
        };
    }

    // Accumulated per-scan raw reads — populated as OCR results stream in, drained once per
    // "cycle_end" marker (RouteOneCycle) so the engine routes a full snapshot exactly once per
    // scan instead of once per region line.
    private string? _readDown;
    private string? _readDistance;
    private string? _readSituation;
    private string? _readQuarter;
    private string? _readAwayScore;
    private string? _readHomeScore;
    private string? _readClock;
    private string? _readBanner;
    private string? _readPregameReady;
    private string? _readTeamRunOut;
    private string? _readPlayClock;
    private string? _readPenaltyAgainst;
    private string? _readPossession;

    // Sticky committed values (never nulled on a blank OCR read). Mirrors Windows GameWatcher's
    // _lastKnownDown/_lastKnownAwayScore/_lastKnownHomeScore/_lastKnownQuarter/_lastDistanceRaw
    // doc comments for the phantom-fire bugs this avoids (a blank read during a pause/replay must
    // never look like a score dropping to 0 and rebounding).
    private string? _lastKnownDown;
    private string? _lastDistanceRaw;
    private string? _lastKnownAwayScore;
    private string? _lastKnownHomeScore;
    private string? _lastKnownQuarter;
    private string? _pendingAwayScore;
    private string? _pendingHomeScore;
    private string? _pendingQuarter;
    private string? _pendingDown;
    private string? _pendingDistanceRaw;
    private DateTime _pendingDownDistanceDeadline;
    static readonly TimeSpan PendingDownDistanceTimeout = TimeSpan.FromMilliseconds(750);

    // Sticky "gated" situation values, mirroring Windows EventGatedRegions (situation/banner/
    // penaltyagainst/teamrunout): they must survive blank OCR frames through a scoring play (and a
    // pause/unpause that shows the same text again must not count as a new event). Cleared only on
    // a real committed down change.
    private string? _stickySituation;
    private string? _stickyBanner;
    private string? _stickyPenaltyAgainst;
    private string? _stickyTeamRunOut;
    private string? _committedDownForStickyClear;

    private string? _lastPossession;

    public void Start()
    {
        _cts = new CancellationTokenSource();

        // Build regions fresh from the current preset (so a preset switch made before watching
        // takes effect), matching Windows reapplying the preset on ActivePreset change.
        var preset = ResolveActivePreset();
        _regions = BuildRegions(preset);
        _patternsByName = _regions.ToDictionary(r => r.Name, r => r.Pattern, StringComparer.OrdinalIgnoreCase);

        // Same clean-slate reset as Windows GameWatcher.Start: every sticky field that can survive
        // into a second game must be wiped, or the new game's first read computes bogus deltas.
        _eventRouter = CreateRouter();
        _snapshotPrevious = new();
        _snapshotCurrent = new();
        _isFirstEngineTick = true;
        _lastKnownDown = null;
        _lastDistanceRaw = null;
        _lastKnownAwayScore = null;
        _lastKnownHomeScore = null;
        _lastKnownQuarter = null;
        _pendingAwayScore = null;
        _pendingHomeScore = null;
        _pendingQuarter = null;
        _pendingDown = null;
        _pendingDistanceRaw = null;
        _stickySituation = null;
        _stickyBanner = null;
        _stickyPenaltyAgainst = null;
        _stickyTeamRunOut = null;
        _committedDownForStickyClear = null;
        _lastPossession = null;
        ResetPerCycleReads();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            if (_ocrProcess != null && !_ocrProcess.HasExited)
            {
                _ocrProcess.Kill();
                _ocrProcess.WaitForExit(2000);
            }
        }
        catch { }
        _ocrProcess?.Dispose();
        _ocrProcess = null;
    }

    void ResetPerCycleReads()
    {
        _readDown = _readDistance = _readSituation = _readQuarter = _readAwayScore = _readHomeScore = null;
        _readClock = _readBanner = _readPregameReady = _readTeamRunOut = _readPlayClock = null;
        _readPenaltyAgainst = _readPossession = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        Log?.Invoke("[MacWatcher] Starting screencapture + Python OCR watcher...");

        string? scriptPath = FindOcrBridgeScript();
        if (scriptPath == null)
        {
            Log?.Invoke("[MacWatcher] ERROR: bandroom_ocr_bridge.py not found. OCR disabled.");
            return;
        }

        if (!File.Exists("/usr/bin/screencapture"))
        {
            Log?.Invoke("[MacWatcher] ERROR: screencapture not found. OCR disabled.");
            return;
        }

        string pythonPath = "/usr/bin/python3";
        if (!File.Exists(pythonPath))
        {
            Log?.Invoke("[MacWatcher] ERROR: python3 not found at /usr/bin/python3. OCR disabled.");
            return;
        }

        string regionsArg = JsonSerializer.Serialize(_regions.Select(r => new { r.Name, r.X, r.Y, r.W, r.H }));

        Log?.Invoke($"[MacWatcher] Launching OCR bridge: {pythonPath} {scriptPath}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{scriptPath}\" --regions '{regionsArg}' --interval 250",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            _ocrProcess = Process.Start(psi);
            if (_ocrProcess == null)
            {
                Log?.Invoke("[MacWatcher] ERROR: Failed to start Python OCR process.");
                return;
            }

            Log?.Invoke($"[MacWatcher] OCR bridge started (PID {_ocrProcess.Id}).");

            _ = Task.Run(() =>
            {
                try
                {
                    while (!_ocrProcess.StandardError.EndOfStream && !ct.IsCancellationRequested)
                    {
                        string? errLine = _ocrProcess.StandardError.ReadLine();
                        if (errLine != null)
                            Log?.Invoke($"[MacWatcher:stderr] {errLine}");
                    }
                }
                catch { }
            }, ct);

            while (!ct.IsCancellationRequested)
            {
                string? line = await _ocrProcess.StandardOutput.ReadLineAsync();
                if (line == null) break;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeEl))
                    {
                        string type = typeEl.GetString() ?? "";
                        if (type == "status")
                        {
                            Log?.Invoke($"[MacWatcher] Bridge: {root.GetProperty("message").GetString()}");
                            continue;
                        }
                        if (type == "error")
                        {
                            if (root.TryGetProperty("message", out var msgEl))
                                Log?.Invoke($"[MacWatcher] Bridge error: {msgEl.GetString()}");
                            continue;
                        }
                        if (type == "cycle_end")
                        {
                            RouteOneCycle();
                            continue;
                        }
                    }

                    // OCR result: {"region":"down","text":"2ND & 7"}
                    if (root.TryGetProperty("region", out var regionEl) &&
                        root.TryGetProperty("text", out var textEl))
                    {
                        string region = regionEl.GetString() ?? "";
                        string text = textEl.GetString() ?? "";
                        OnRegionOcrResult(region, text);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[MacWatcher] Fatal error: {ex.Message}");
        }
        finally
        {
            Log?.Invoke("[MacWatcher] OCR bridge stopped.");
        }
    }

    private static string? FindOcrBridgeScript()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "bandroom_ocr_bridge.py"),
            Path.Combine(AppContext.BaseDirectory, "scripts", "bandroom_ocr_bridge.py"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "Bandroom.Mac", "bandroom_ocr_bridge.py")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "scripts", "bandroom_ocr_bridge.py")),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        return null;
    }

    /// <summary>Called when the OCR bridge returns text for a region. Decodes each region into its
    /// per-cycle `_read*` slot via the same regex patterns Windows uses, so RouteOneCycle can
    /// commit sticky values and build a complete snapshot.</summary>
    public void OnRegionOcrResult(string regionName, string rawText)
    {
        string text = rawText?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!_patternsByName.TryGetValue(regionName, out var pattern)) return;

        switch (regionName)
        {
            case "down":
                {
                    var m = pattern.Match(text);
                    _readDown = m.Success ? m.Value : null;
                    var d = DistancePattern.Match(text);
                    _readDistance = d.Success ? NormalizeDistanceRaw(d.Groups[1].Value) : null;
                    break;
                }
            case "situation":
                {
                    var m = pattern.Match(text);
                    _readSituation = m.Success ? NormalizeMatch(regionName, m.Value) : null;
                    break;
                }
            case "quarter":
                {
                    var m = pattern.Match(text);
                    _readQuarter = m.Success ? NormalizeMatch(regionName, m.Value) : null;
                    break;
                }
            case "banner":
                {
                    var m = pattern.Match(text);
                    _readBanner = m.Success ? NormalizeMatch(regionName, m.Value) : null;
                    break;
                }
            case "penaltyagainst":
                {
                    var m = pattern.Match(text);
                    _readPenaltyAgainst = m.Success ? m.Value : null;
                    break;
                }
            case "pregameready":
                _readPregameReady = pattern.IsMatch(text) ? "ready" : null;
                break;
            case "teamrunout":
                _readTeamRunOut = pattern.IsMatch(text) ? "college football" : null;
                break;
            case "awayscore":
                {
                    var m = pattern.Match(text);
                    _readAwayScore = m.Success ? m.Value : null;
                    break;
                }
            case "homescore":
                {
                    var m = pattern.Match(text);
                    _readHomeScore = m.Success ? m.Value : null;
                    break;
                }
            case "clock":
                {
                    var m = pattern.Match(text);
                    _readClock = m.Success ? m.Value : null;
                    break;
                }
            case "playclock":
                _readPlayClock = pattern.IsMatch(text) ? text : null;
                break;
            case "possession":
                _readPossession = ParsePossession(text);
                break;
            case "flag":
                // Unused on Mac (Windows only gates possession-color sampling on it, which isn't
                // implemented here) — kept in the region set for log/parity completeness.
                break;
        }
    }

    private string? ParsePossession(string text)
    {
        string upper = text.ToUpperInvariant();
        if (upper.Contains("HOME"))
            return "home";
        if (upper.Contains("AWAY"))
            return "away";
        return null;
    }

    /// <summary>Commits this cycle's reads into sticky fields, builds a full PlaySnapshot, and
    /// routes it through the rule engine exactly once per scan. Mirrors Windows RouteEngineTick.</summary>
    private void RouteOneCycle()
    {
        if (_eventRouter == null) return;

        // Sticky commit: down/distance (paired) and score/quarter (two-tick confirm), same as
        // Windows, so a single bad OCR frame can't fire phantom scoring/quarter/down events.
        CommitDownAndDistance(_readDown, _readDistance);
        if (_readAwayScore != null) CommitValueIfConfirmed(_readAwayScore, ref _pendingAwayScore, ref _lastKnownAwayScore);
        if (_readHomeScore != null) CommitValueIfConfirmed(_readHomeScore, ref _pendingHomeScore, ref _lastKnownHomeScore);
        if (_readQuarter != null) CommitValueIfConfirmed(_readQuarter, ref _pendingQuarter, ref _lastKnownQuarter);

        // Mirror Windows EventGatedRegions reset: a real committed down change re-arms the gated
        // situation/banner values for the next snap (cleared BEFORE applying this cycle's reads so
        // a new value landing on the same tick is still captured).
        if (_lastKnownDown != null && _lastKnownDown != _committedDownForStickyClear)
        {
            _committedDownForStickyClear = _lastKnownDown;
            _stickySituation = null;
            _stickyBanner = null;
            _stickyPenaltyAgainst = null;
            _stickyTeamRunOut = null;
        }
        if (_readSituation != null) _stickySituation = _readSituation;
        if (_readBanner != null) _stickyBanner = _readBanner;
        if (_readPenaltyAgainst != null) _stickyPenaltyAgainst = _readPenaltyAgainst;
        if (_readTeamRunOut != null) _stickyTeamRunOut = _readTeamRunOut;

        // Possession flip (OCR best-effort) — fires PossessionChanged in lockstep so MainWindow's
        // routing side and this snapshot's PossessionAway stay consistent.
        if (!string.IsNullOrWhiteSpace(_readPossession) && _readPossession != _lastPossession)
        {
            _lastPossession = _readPossession;
            Log?.Invoke($"[MacWatcher] possession (OCR): {_lastPossession}");
            PossessionChanged?.Invoke(_lastPossession);
        }

        int down = ParseOrdinal(_lastKnownDown);
        int quarter = ParseOrdinal(_lastKnownQuarter);
        int yardsToGo = int.TryParse(_lastDistanceRaw, out var yd) ? yd : 0;
        int awayScore = int.TryParse(_lastKnownAwayScore, out var aScore) ? aScore : 0;
        int homeScore = int.TryParse(_lastKnownHomeScore, out var hScore) ? hScore : 0;
        int timeRemainingSeconds = ParseClockToSeconds(_readClock);

        // Penalty side resolution (mirrors Windows RouteEngineTick): compare the "Against <Team>"
        // text against the matchup's team names/mascots, and gate on a real possession read rather
        // than guessing null-as-home.
        bool? possessionIsHomeNow = _lastPossession == null ? null : _lastPossession != "away";
        bool? penalizedIsHome = null;
        string? penaltyText = _stickyPenaltyAgainst;
        if (penaltyText != null)
        {
            if (!string.IsNullOrEmpty(HomeTeamName) && penaltyText.Contains(HomeTeamName, StringComparison.OrdinalIgnoreCase))
                penalizedIsHome = true;
            else if (!string.IsNullOrEmpty(AwayTeamName) && penaltyText.Contains(AwayTeamName, StringComparison.OrdinalIgnoreCase))
                penalizedIsHome = false;
            else if (!string.IsNullOrEmpty(HomeTeamMascot) && penaltyText.Contains(HomeTeamMascot, StringComparison.OrdinalIgnoreCase))
                penalizedIsHome = true;
            else if (!string.IsNullOrEmpty(AwayTeamMascot) && penaltyText.Contains(AwayTeamMascot, StringComparison.OrdinalIgnoreCase))
                penalizedIsHome = false;
        }
        bool isPenaltyOnOffense = penalizedIsHome.HasValue && possessionIsHomeNow.HasValue && penalizedIsHome.Value == possessionIsHomeNow.Value;
        bool isPenaltyOnDefense = penalizedIsHome.HasValue && possessionIsHomeNow.HasValue && penalizedIsHome.Value != possessionIsHomeNow.Value;

        var snapshot = new PlaySnapshot
        {
            Down = down,
            YardsToGo = yardsToGo,
            Quarter = quarter,
            PossessionAway = _lastPossession == "away",
            IsKickoff = _stickySituation == "kickoff",
            IsPAT = _stickySituation == "pat_good",
            IsTouchdown = _stickySituation == "touchdown",
            IsTurnover = _stickySituation == "turnover",
            IsNoPuntReturn = _stickySituation == "nopuntreturn",
            IsTimeout = _stickySituation == "time_out",
            IsPenaltyOnOffense = isPenaltyOnOffense,
            IsPenaltyOnDefense = isPenaltyOnDefense,
            IsPregameReady = _readPregameReady == "ready",
            // Mac has no chevron/black-screen brightness sampling yet, so the pregame take-the-field
            // signal relies entirely on GameStateEventHelper's quarter/down heuristic for now.
            IsPregameEntranceMarker = false,
            IsTeamRunOut = _stickyTeamRunOut == "college football",
            IsPlayClockCounting = _readPlayClock != null,
            IsFieldGoalAttempt = _stickyBanner == "fieldgoal",
            YardLine = 0,
            HomeScore = homeScore,
            AwayScore = awayScore,
            TimeRemainingSeconds = timeRemainingSeconds,
            // -1 = "not sampled": Mac has no timeout-dash brightness sampling yet, so TimeoutHelper
            // (whose own range check reads < 0 as "don't fire") correctly stays silent rather than
            // guessing wrong.
            AwayTimeoutsRemaining = -1,
            HomeTimeoutsRemaining = -1,
            BigGame = ConfigStore.LoadBigGameSettings().Enabled,
        };

        _snapshotPrevious = _snapshotCurrent;
        _snapshotCurrent = snapshot;

        bool isFirstTick = _isFirstEngineTick;
        _isFirstEngineTick = false;

        // Drain per-cycle reads AFTER building so the next scan starts fresh.
        ResetPerCycleReads();

        if (isFirstTick) return;

        var state = new GameState
        {
            Current = _snapshotCurrent,
            Previous = _snapshotPrevious,
            UserIsHome = UserIsHome,
        };

        var results = _eventRouter.Route(state);
        if (results.Count > 0)
            EventsDetected?.Invoke(results);
    }

    void CommitDownAndDistance(string? currentDown, string? distanceRaw)
    {
        bool wasPending = _pendingDown != null || _pendingDistanceRaw != null;

        if (currentDown != null && currentDown != _lastKnownDown) _pendingDown = currentDown;
        if (distanceRaw != null && distanceRaw != _lastDistanceRaw) _pendingDistanceRaw = distanceRaw;

        bool isPending = _pendingDown != null || _pendingDistanceRaw != null;
        if (!isPending) return;

        if (!wasPending) _pendingDownDistanceDeadline = DateTime.UtcNow + PendingDownDistanceTimeout;

        bool bothReady = _pendingDown != null && _pendingDistanceRaw != null;
        bool timedOut = DateTime.UtcNow >= _pendingDownDistanceDeadline;
        if (!bothReady && !timedOut) return;

        if (_pendingDown != null) _lastKnownDown = _pendingDown;
        if (_pendingDistanceRaw != null) _lastDistanceRaw = _pendingDistanceRaw;
        _pendingDown = null;
        _pendingDistanceRaw = null;
    }

    static void CommitValueIfConfirmed(string currentValue, ref string? pending, ref string? committed)
    {
        if (currentValue == committed) { pending = null; return; }
        if (currentValue == pending) { committed = currentValue; pending = null; }
        else { if (pending != null) committed = pending; pending = currentValue; }
    }

    static string NormalizeDistanceRaw(string raw) => int.TryParse(raw, out _) ? raw : "1";

    /// <summary>Collapses OCR-noisy variants down to the same stable keys Windows GameWatcher.
    /// NormalizeMatch produces (situation:pat_good, situation:kickoff, banner:fieldgoal, etc).</summary>
    static string NormalizeMatch(string regionName, string rawMatch)
    {
        string collapsed = Regex.Replace(rawMatch, @"\s+", " ").Trim().ToLowerInvariant();
        if (regionName != "situation" && regionName != "banner") return collapsed;

        return collapsed switch
        {
            "intercepted" or "fumble" or "turnover" => "turnover",
            "field goal" => "fieldgoal",
            "fair catch" or "no return" => "nopuntreturn",
            "kick off" => "kickoff",
            _ => collapsed.Replace(" ", "_"),
        };
    }

    static int ParseOrdinal(string? value) => value?.ToLowerInvariant() switch
    {
        "1st" => 1,
        "2nd" => 2,
        "3rd" => 3,
        "4th" => 4,
        _ => 0,
    };

    static int ParseClockToSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var parts = value.Split(':');
        if (parts.Length != 2) return 0;
        if (!int.TryParse(parts[0], out int minutes) || !int.TryParse(parts[1], out int seconds)) return 0;
        return minutes * 60 + seconds;
    }

    // SYNCED 2026-08-12 (parity with Windows GameWatcher.CreateEventRouter): 26 evaluators,
    // including FirstDownOnFirstDownHelper and RunOutHelper which the Mac list was missing.
    private static EventRouter CreateRouter()
    {
        return new EventRouter(new IRuleEvaluator[]
        {
            new BigEventHelper(),
            new DefenseFirstDownHelper(),
            new DefenseHelper(),
            new DefenseSecondDownShortHelper(),
            new DefenseThirdDownHelper(),
            new DownFieldPositionHelper(),
            new DriveStarterHelper(),
            new FieldGoalMissedHelper(),
            new FieldGoalPATHelper(),
            new FirstDownHelper(),
            new FirstDownOnFirstDownHelper(),
            new GameStateEventHelper(),
            new KickoffHelper(),
            new OffenseAfterOpeningKickHelper(),
            new OffenseDownHelper(),
            new OffenseFourthDownHelper(),
            new PenaltyHelper(),
            new PregameHelper(),
            new RunOutHelper(),
            new SafetyHelper(),
            new ThirdDownConversionHelper(),
            new TflHelper(),
            new TimeoutHelper(),
            new TouchdownHelper(),
            new TurnoverHelper(),
        });
    }
}