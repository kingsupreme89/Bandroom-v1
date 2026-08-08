using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bandroom.Core;
using Bandroom.Core.Helpers;

namespace Bandroom.Mac;

/// <summary>
/// macOS game-screen watcher using Vision framework OCR + ScreenCaptureKit.
/// Mirrors the Windows GameWatcher.cs logic but replaces WinRT OcrEngine with
/// macOS-native Vision framework and GDI CopyFromScreen with ScreenCaptureKit.
///
/// On macOS, this requires the app to have Screen Recording permission
/// (System Preferences > Privacy > Screen Recording).
/// </summary>
internal sealed class MacGameWatcher
{
    public event Action<bool>? WindowFoundChanged;
    public event Action<IReadOnlyList<TriggerEvent>>? EventsDetected;
    public event Action<string>? Log;
    public bool UserIsHome { get; set; }

    private EventRouter? _eventRouter;
    private PlaySnapshot _snapshotPrevious = new();
    private PlaySnapshot _snapshotCurrent = new();
    private CancellationTokenSource? _cts;

    // OCR region definitions (same fractional layout as Windows GameWatcher)
    private static readonly (string Name, double X, double Y, double W, double H)[] Regions =
    {
        ("down",     0, 0.83, 1.0, 0.14),
        ("situation",0, 0.83, 1.0, 0.14),
        ("quarter",  0, 0.83, 1.0, 0.14),
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

    public void Stop() => _cts?.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        Log?.Invoke("[MacWatcher] macOS Vision OCR watcher starting...");
        Log?.Invoke("[MacWatcher] Requires Screen Recording permission in System Preferences.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // TODO: Implement actual ScreenCaptureKit + Vision OCR
                // In this stub, we simulate periodic polls with a delay.
                // Real implementation:
                //   1. Use SCContentSharingPicker or SCDisplay to capture
                //   2. Feed CGImage to VNRecognizeTextRequest
                //   3. Parse OCR results into region.Last values
                //   4. Build PlaySnapshot → EventRouter.Route → EventsDetected

                BuildAndRouteSnapshot();

                await Task.Delay(250, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log?.Invoke($"[MacWatcher] Error: {ex.Message}");
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// Builds a PlaySnapshot from the current OCR state and routes through the engine.
    /// Currently uses stub values — replace with actual Vision OCR reads.
    /// </summary>
    private void BuildAndRouteSnapshot()
    {
        if (_eventRouter == null) return;

        // Parse down/quarter from region state
        string? downRaw = GetRegionValue("down");
        string? quarterRaw = GetRegionValue("quarter");
        string? situation = GetRegionValue("situation");

        int down = ParseOrdinal(downRaw);
        int quarter = ParseOrdinal(quarterRaw);
        int yardsToGo = _lastDistance;

        var snapshot = new PlaySnapshot
        {
            Down = down,
            YardsToGo = yardsToGo,
            Quarter = quarter,
            PossessionAway = _lastPossession == "away",
            IsKickoff = situation == "kickoff",
            IsPAT = situation == "pat_good",
            IsTouchdown = situation == "touchdown",
            IsTurnover = situation == "turnover",
            // Fields without OCR regions yet
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

    /// <summary>
    /// Called by the Vision OCR callback when text is recognized in a region.
    /// </summary>
    public void OnRegionOcrResult(string regionName, string rawText)
    {
        _lastValues[regionName] = rawText?.Trim();
        Log?.Invoke($"[MacWatcher:{regionName}] OCR: \"{rawText?.Trim()}\"");
    }

    private static int ParseOrdinal(string? value) => value?.ToLowerInvariant() switch
    {
        "1st" => 1, "2nd" => 2, "3rd" => 3, "4th" => 4,
        _ => 0
    };

    private static EventRouter CreateRouter()
    {
        return new EventRouter(new IRuleEvaluator[]
        {
            new BigEventHelper(), new DefenseHelper(), new DownFieldPositionHelper(),
            new DriveStarterHelper(), new FieldGoalMissedHelper(), new FieldGoalPATHelper(),
            new FirstDownHelper(), new GameStateEventHelper(), new KickoffHelper(),
            new OffenseDownHelper(), new PenaltyHelper(), new SafetyHelper(),
            new NoPuntReturnHelper(),
            new TflHelper(), new TimeoutHelper(), new TouchdownHelper(), new TurnoverHelper(),
        });
    }
}