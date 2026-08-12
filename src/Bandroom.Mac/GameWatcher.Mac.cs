using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bandroom.Core;
using Bandroom.Core.Helpers;

namespace Bandroom.Mac;

/// <summary>
/// macOS game-screen watcher using screencapture + bundled Python OCR helper.
/// The Python script (bandroom_ocr_bridge.py) uses macOS built-in Vision framework
/// via PyObjC to recognize text from screen captures. This avoids fragile P/Invoke
/// and works on both Apple Silicon and Intel Macs since Python+PyObjC ship with macOS.
///
/// Mirrors Windows GameWatcher.cs logic — same EventRouter, same 17 evaluators,
/// same PlaySnapshot → GameState → Route pipeline.
/// </summary>
internal sealed class MacGameWatcher
{
    public event Action<IReadOnlyList<TriggerEvent>>? EventsDetected;
    public event Action<string>? Log;
    public bool UserIsHome { get; set; }
    public string? HomeTeamName { get; set; }
    public string? AwayTeamName { get; set; }

    private EventRouter? _eventRouter;
    private PlaySnapshot _snapshotPrevious = new();
    private PlaySnapshot _snapshotCurrent = new();
    private CancellationTokenSource? _cts;
    private Process? _ocrProcess;

    // OCR region definitions — same fractional layout as Windows GameWatcher
    // x, y, w, h are fractions of screen width/height
    private static readonly (string Name, double X, double Y, double W, double H)[] Regions =
    {
        ("down",      0, 0.83, 1.0, 0.14),
        ("situation", 0, 0.83, 1.0, 0.14),
        ("quarter",   0, 0.83, 1.0, 0.14),
        ("possession",0, 0.83, 1.0, 0.14),
    };

    // Last OCR'd values per region
    private readonly Dictionary<string, string?> _lastValues = new();
    private string? _lastPossession;
    private int _lastDistance;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _eventRouter ??= CreateRouter();
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

    private async Task RunAsync(CancellationToken ct)
    {
        Log?.Invoke("[MacWatcher] Starting screencapture + Python OCR watcher...");

        // Locate the Python OCR bridge script
        string scriptPath = FindOcrBridgeScript();
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

        // Build region arguments for the Python script
        string regionsArg = JsonSerializer.Serialize(Regions.Select(r => new
        {
            r.Name, r.X, r.Y, r.W, r.H,
        }));

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

            // Read stderr on a separate task for error logging
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

            // Read OCR results from stdout line by line
            while (!_ocrProcess.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
            {
                string? line = await _ocrProcess.StandardOutput.ReadLineAsync();
                if (line == null) break;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "status")
                    {
                        Log?.Invoke($"[MacWatcher] Bridge: {root.GetProperty("message").GetString()}");
                        continue;
                    }

                    // OCR result: {"region":"down","text":"2ND & 7"}
                    if (root.TryGetProperty("region", out var regionEl) &&
                        root.TryGetProperty("text", out var textEl))
                    {
                        string region = regionEl.GetString() ?? "";
                        string text = textEl.GetString() ?? "";
                        OnRegionOcrResult(region, text);
                        BuildAndRouteSnapshot();
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
        // Look in these locations in order:
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

    /// <summary>
    /// Called when the OCR bridge returns text for a region.
    /// </summary>
    public void OnRegionOcrResult(string regionName, string rawText)
    {
        string trimmed = rawText?.Trim() ?? "";
        _lastValues[regionName] = trimmed;

        // Parse possession from OCR text
        if (regionName == "possession" && !string.IsNullOrWhiteSpace(trimmed))
        {
            _lastPossession = ParsePossession(trimmed);
        }

        // Parse distance from situation text
        if (regionName == "situation" && !string.IsNullOrWhiteSpace(trimmed))
        {
            _lastDistance = ParseDistance(trimmed);
        }
    }

    private string? ParsePossession(string text)
    {
        // Look for possession indicators in OCR'd text
        string upper = text.ToUpperInvariant();
        if (upper.Contains("HOME"))
            return "home";
        if (upper.Contains("AWAY"))
            return "away";
        return null;
    }

    private static int ParseDistance(string text)
    {
        // Extract yards-to-go from text like "2ND & 7" or "3RD & 15"
        var parts = text.Split('&', StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            string distPart = parts[1].Trim();
            // Remove non-numeric except digits
            string digits = new string(distPart.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int d)) return d;
        }
        return 0;
    }

    /// <summary>
    /// Builds a PlaySnapshot from the current OCR state and routes through the engine.
    /// </summary>
    private void BuildAndRouteSnapshot()
    {
        if (_eventRouter == null) return;

        string? downRaw = GetRegionValue("down");
        string? quarterRaw = GetRegionValue("quarter");
        string? situation = GetRegionValue("situation");

        int down = ParseOrdinal(downRaw);
        int quarter = ParseOrdinal(quarterRaw);

        var snapshot = new PlaySnapshot
        {
            Down = down,
            YardsToGo = _lastDistance,
            Quarter = quarter,
            PossessionAway = _lastPossession == "away",
            IsKickoff = situation?.Contains("kickoff", StringComparison.OrdinalIgnoreCase) == true,
            IsPAT = situation?.Contains("pat", StringComparison.OrdinalIgnoreCase) == true,
            IsTouchdown = situation?.Contains("touchdown", StringComparison.OrdinalIgnoreCase) == true,
            IsTurnover = situation?.Contains("turnover", StringComparison.OrdinalIgnoreCase) == true,
            YardLine = 0,
            HomeScore = 0,
            AwayScore = 0,
            TimeRemainingSeconds = 0,
            AwayTimeoutsRemaining = 6,
            BigGame = false,
        };

        _snapshotPrevious = _snapshotCurrent;
        _snapshotCurrent = snapshot;

        if (_snapshotPrevious.Down == 0 && _snapshotPrevious.Quarter == 0)
            return;

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

    private string? GetRegionValue(string name)
    {
        _lastValues.TryGetValue(name, out var val);
        return val;
    }

    private static int ParseOrdinal(string? value) => value?.ToLowerInvariant() switch
    {
        "1st" => 1,
        "2nd" => 2,
        "3rd" => 3,
        "4th" => 4,
        _ => 0,
    };

    private static EventRouter CreateRouter()
    {
        return new EventRouter(new IRuleEvaluator[]
        {
            new BigEventHelper(), new DefenseHelper(), new DownFieldPositionHelper(),
            new DriveStarterHelper(), new FieldGoalMissedHelper(), new FieldGoalPATHelper(),
            new FirstDownHelper(), new GameStateEventHelper(), new KickoffHelper(),
            new OffenseDownHelper(), new PenaltyHelper(), new SafetyHelper(),
            new TflHelper(), new TimeoutHelper(), new TouchdownHelper(), new TurnoverHelper(),
        });
    }
}