using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Bandroom.Core;
using Bandroom.Core.Helpers;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SupremeStadiumSoundSelector;

/// <summary>One OCR'd HUD region: a crop box (as fractions of the window rect) plus a
/// regex to pull the value out. Uncalibrated regions (FxW == 0) are skipped entirely.</summary>
internal sealed class WatchedRegion
{
    public required string Name;
    public double FxX, FxY, FxW, FxH;
    public required Regex Pattern;
    public string? Last;
    public string? LastRawText;
    public DateTime CooldownUntil;
    public bool Calibrated => FxW > 0 && FxH > 0;
}

internal sealed class GameWatcher
{
    public event Action<bool>? WindowFoundChanged;
    public event Action<string?>? DownChanged;
    /// <summary>Fires for any region (including "down") whenever its OCR'd value changes
    /// to a new non-null value -- edge-triggered, same as DownChanged but generic.</summary>
    public event Action<string, string?>? RegionChanged;
    /// <summary>Fires when the down/distance ribbon's background color flips between the home
    /// team's color / the away team's color / neutral (black, e.g. kickoff) -- confirmed via
    /// live screenshots that this same ribbon (the "down" region below) fills with whichever
    /// team currently has the ball, not just down/distance text. "home"/"away"/null,
    /// edge-triggered like DownChanged.</summary>
    public event Action<string?>? PossessionChanged;
    public event Action<string>? Log;

    /// <summary>Lets the host resolve a sampled ribbon color to "home"/"away"/null (host owns
    /// the home/away team color table via ConfigStore/TeamColors, set from the Matchup picker)
    /// without GameWatcher depending on those types directly. Null delegate or null result ->
    /// possession never fires.</summary>
    public Func<Color, string?>? ResolveTeamColor;

    /// <summary>Which named crop-position preset (see ScorebugPreset.cs) is currently applied
    /// to the down/situation/quarter band and the possession-color box. Setting this re-applies
    /// the new preset's fractions to the live regions immediately, so a change takes effect on
    /// the very next poll without needing a restart.</summary>
    ScorebugPreset _activePreset = ScorebugPreset.KamsCbsScorebugV3;
    public ScorebugPreset ActivePreset
    {
        get => _activePreset;
        set { _activePreset = value; ApplyScorebugPreset(value); }
    }

    void ApplyScorebugPreset(ScorebugPreset preset)
    {
        foreach (var region in _regions)
        {
            // "flag" was added 2026-08-07 sharing the same band as down/situation/quarter (see
            // its calibration comment above). awayscore/homescore/clock share the SAME vertical
            // band too (BandFxY/BandFxH). FIXED 2026-08-10: their horizontal FxX/FxW used to be
            // hardcoded CBS-specific offsets shared by every preset -- promoted to per-preset
            // fields (ScorebugPreset.AwayScoreFx*/HomeScoreFx*/ClockFx*) once the CFB 27 default
            // scorebug update proved a single shared X/W could never be correct for two skins
            // with genuinely different score/clock layouts at once.
            if (region.Name is "down" or "situation" or "quarter" or "flag")
            {
                region.FxY = preset.BandFxY;
                region.FxH = preset.BandFxH;
            }
            else if (region.Name == "awayscore")
            {
                region.FxX = preset.AwayScoreFxX; region.FxW = preset.AwayScoreFxW;
                region.FxY = preset.BandFxY; region.FxH = preset.BandFxH;
            }
            else if (region.Name == "homescore")
            {
                region.FxX = preset.HomeScoreFxX; region.FxW = preset.HomeScoreFxW;
                region.FxY = preset.BandFxY; region.FxH = preset.BandFxH;
            }
            else if (region.Name == "clock")
            {
                region.FxX = preset.ClockFxX; region.FxW = preset.ClockFxW;
                region.FxY = preset.BandFxY; region.FxH = preset.BandFxH;
            }
            // penaltyagainst/banner added 2026-08-11 -- previously hardcoded once in the _regions
            // initializer below and never repositioned per preset at all, so every non-CBS preset
            // silently read CBS's screen coordinates for penalty and scoring-banner detection. See
            // ScorebugPreset.PenaltyAgainstFx*/BannerFx* doc comment.
            else if (region.Name == "penaltyagainst")
            {
                region.FxX = preset.PenaltyAgainstFxX; region.FxY = preset.PenaltyAgainstFxY;
                region.FxW = preset.PenaltyAgainstFxW; region.FxH = preset.PenaltyAgainstFxH;
            }
            else if (region.Name == "banner")
            {
                region.FxX = preset.BannerFxX; region.FxY = preset.BannerFxY;
                region.FxW = preset.BannerFxW; region.FxH = preset.BannerFxH;
            }
        }
    }

    /// <summary>Fires when the down/distance ribbon shows a negative distance-to-go (e.g.
    /// "3rd & -4") -- confirmed via a live screenshot that the ribbon reads down and distance
    /// together as one string ("3rd & 7"), so no new OCR region was needed, just a wider regex
    /// on the same "down" crop already in use. Side-agnostic -- the host attributes it to
    /// whichever side is NOT the current possession color, since a negative distance means the
    /// offense (the possession side) just lost yards.</summary>
    public event Action? TackleForLossDetected;
    /// <summary>Fires after every OCR tick with the list of matched evaluator events.
    /// Empty list when no evaluators matched this tick. Host (WebMainForm) wires these
    /// to FireEventForSide.</summary>
    public event Action<IReadOnlyList<TriggerEvent>>? EventsDetected;
    /// <summary>True when the user's selected team is the Home team. Must be set before
    /// starting the watcher so the engine knows which side to attribute events to.</summary>
    public bool UserIsHome { get; set; }

    /// <summary>Reflects the current snapshot's manual Big Game toggle (see ConfigStore.
    /// BigGameSettings) -- read by WebMainForm.OnEngineEventsDetected to decide whether the
    /// away side plays every event at full volume (Big Game) or only IsEarnedBigEvent ones at
    /// 25% (ordinary game, away team only sends a small travel pep band).</summary>
    public bool IsBigGame => _snapshotCurrent.BigGame;

    /// <summary>Set alongside UserIsHome (see WebMainForm.SetGameTeamsFromWeb) so the "penalty"
    /// region can determine WHICH team a penalty was called against -- the game's own penalty
    /// decision overlay shows "Against &lt;Team Name&gt;" text, which only means something once
    /// compared against these. Added 2026-08-07 alongside penalty-side OCR calibration.</summary>
    public string? HomeTeamName { get; set; }
    public string? AwayTeamName { get; set; }

    /// <summary>Optional OCR-matching aliases for TeamBuilder custom schools -- the school picker
    /// shows the institution name (e.g. "Idaho State"), but the game's own penalty banner may
    /// render the mascot instead (e.g. "Bengals"). Checked alongside HomeTeamName/AwayTeamName
    /// below so custom schools without a name-for-name scoreboard match still resolve. Blank/null
    /// for built-in roster teams (their canonical Name already matches what the game shows).</summary>
    public string? HomeTeamMascot { get; set; }
    public string? AwayTeamMascot { get; set; }
    static readonly Regex DistancePattern = new(@"&\s*(-?\d+)", RegexOptions.IgnoreCase);
    string? _lastDistanceRaw;
    string? _lastFiredDistanceRaw;
    DateTime _lossCooldownUntil;

    // Down and YardsToGo are OCR'd from the same "down" crop text but can independently succeed/
    // fail per 250ms poll (partial/garbled digits vs a clean ordinal read, or vice versa). If they
    // committed to _lastKnownDown/_lastDistanceRaw independently, a real down+distance change could
    // land on two different snapshot ticks, making DefenseHelper/TflHelper see a Down change with a
    // stale YardsToGo (or vice versa) and fire the wrong cue instead of the "(Loss)" variant --
    // STATE_MACHINE_ANALYSIS Discrepancy #13. Staged here and committed together once both resolve,
    // with a short timeout fallback so a field that never actually changes (e.g. distance parses
    // fine but never differs) doesn't block Down from updating forever.
    string? _pendingDown;
    string? _pendingDistanceRaw;
    DateTime _pendingDownDistanceDeadline;
    static readonly TimeSpan PendingDownDistanceTimeout = TimeSpan.FromMilliseconds(750);

    string? _lastPossession;
    DateTime _possessionCooldownUntil;

    /// <summary>-1 = not yet sampled (TimeoutHelper's own range check, `&lt; 0`, means it
    /// correctly just won't fire until a real reading comes in, rather than defaulting to a
    /// value that could misfire). Updated every tick by SampleTimeoutSegments.</summary>
    int _lastAwayTimeoutsRemaining = -1;

    // --- Bandroom.Core engine state ---
    EventRouter? _eventRouter;
    PlaySnapshot _snapshotPrevious = new();
    PlaySnapshot _snapshotCurrent = new();

    /// <summary>Read-only view of the most recent OCR tick's game state -- unlike
    /// EventsDetected (which only fires on a state TRANSITION), this updates every tick, so
    /// anything that needs to continuously watch score/clock/quarter (crowd intensity,
    /// controller rumble for a close 4th-quarter/OT game) can just poll it instead of the
    /// watcher needing a dedicated event for every such consumer.</summary>
    public PlaySnapshot LastSnapshot => _snapshotCurrent;
    // FIXED 2026-08-09: RouteEngineTick used to infer "is this the very first tick" from
    // `_snapshotPrevious.Down == 0 && _snapshotPrevious.Quarter == 0`, but Down/Quarter both
    // legitimately stay 0 for the ENTIRE pregame period (before kickoff, no down/quarter is on
    // screen yet), not just tick #1. So that guard kept skipping evaluators on every pregame
    // tick -- including the exact tick where Quarter flips 0->1 and Down flips 0->something,
    // which is precisely the transition GameStateEventHelper's "Other: Pregame Take the Field"
    // needs to see. On that tick Previous was STILL 0/0, so the guard swallowed it and pregame
    // could never fire. Track the real first-tick with its own flag instead.
    bool _isFirstEngineTick = true;

    /// <summary>Minimum time between fires for the SAME region, guarding against a
    /// flickery OCR read (e.g. "2nd" -&gt; blank -&gt; "2nd" within one second) spam-firing
    /// the same trigger repeatedly. Exposed in the UI's Settings panel, so not readonly.</summary>
    public static TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    // See the pause/unpause re-fire fix in RunAsync below -- these regions only clear their
    // "Last" value (re-arming them to fire again) when the down/distance region actually
    // changes, not just whenever their own OCR read goes blank.
    static readonly HashSet<string> EventGatedRegions = new(StringComparer.OrdinalIgnoreCase) { "situation", "banner", "quarter", "penaltyagainst", "pregameready" };
    bool _downChangedThisTick;

    // The "down" WatchedRegion's raw OCR crop is the whole play-by-play ticker line, which goes
    // blank constantly between plays (camera cuts, replays, no ticker text on screen) -- on those
    // ticks region.Last resets to null so the SAME down value can re-trigger DownChanged later,
    // which is correct for that purpose. But RouteEngineTick used to read region.Last directly to
    // build the snapshot's Down field, so PlaySnapshot.Down flickered to 0 on every blank tick.
    // PlayDelta.WasFirstDown requires current.Down==1 immediately after previous.Down>1 -- with a
    // 0 landing between almost every real transition, that edge was essentially never observed,
    // silently killing every "Offense: Earned First Down" detection. Track the last actually-read
    // down value here, separately from the edge-triggering region.Last, and never null it out.
    string? _lastKnownDown;

    // Same "sticky, never nulled on a blank read" pattern as _lastKnownDown above, applied to
    // score/quarter after a real live bug: FieldGoalPATHelper/FieldGoalMissedHelper/SafetyHelper
    // all fire on a single-tick HomeScore/AwayScore delta with no debounce. Score/quarter OCR
    // regions blank out (region.Last -> null -> RouteEngineTick treats as 0) not just between
    // plays but during pause menus, replay overlays, and cutscenes where the scorebug isn't drawn
    // at all -- so a real score like 14 would read as a blank/0 tick while paused, then jump back
    // to 14 on resume. That "0 -> 14" (or any transient single-digit misread landing on exactly a
    // +1/+2/+3 delta) reads to the evaluators as a PAT/2-point/field-goal having just happened,
    // firing real audio for an event that never occurred -- reported live as "random song on the
    // pause screen" and duplicate/incorrect audio. Fix: RouteEngineTick now reads these sticky
    // fields (updated only on a real parsed value, exactly like _lastKnownDown) instead of the
    // region's raw (blank-able) Last, so a score/quarter that never actually changed produces a
    // delta of 0 through any stretch of blank OCR ticks, no matter how long.
    string? _lastKnownAwayScore;
    string? _lastKnownHomeScore;
    string? _lastKnownQuarter;

    readonly List<WatchedRegion> _regions = new()
    {
        // Spans the FULL WIDTH of the bottom score-bug band rather than one tight box, because
        // the college football broadcast rotates between several overlay skins (CBS/ABC/FOX/
        // ESPN) that each place the down/distance text at a different X position along that
        // same bottom strip. Widening horizontally (instead of calibrating one skin's exact
        // box) means any of them still gets caught. Vertical band widened slightly too as a
        // margin of safety across skins with slightly different bug heights/positions.
        // NOTE: possession-color sampling does NOT use this crop -- see PossessionCropRect,
        // which keeps the original tight box so widening this one doesn't wash out the color
        // read with background/crowd pixels.
        // Requires "&" right after the ordinal (e.g. "3rd & 7") -- the down/distance combo is
        // the ONLY place that pattern renders in this bug, which disambiguates it from the
        // quarter indicator below now that both share the same full-width capture band.
        new WatchedRegion
        {
            Name = "down",
            FxX = 0, FxY = 0.83, FxW = 1.0, FxH = 0.14,
            Pattern = new Regex(@"\b(1st|2nd|3rd|4th)\b(?=\s*&)", RegexOptions.IgnoreCase),
        },
        // Penalty/flag banner -- calibrated 2026-08-07 from a live screenshot (Auburn @ Georgia
        // Tech, CBS skin): the FLAG state renders in the exact same rightmost box as
        // "situation"/"down" below (yellow background, "FLAG" text) instead of a separate
        // banner, so it reuses that same full-width band rather than needing its own crop.
        new WatchedRegion
        {
            Name = "flag",
            FxX = 0, FxY = 0.83, FxW = 1.0, FxH = 0.14,
            Pattern = new Regex(@"\b(FLAG|PENALTY)\b", RegexOptions.IgnoreCase),
        },
        // Same crop box as "down" -- confirmed via live screenshots that the scorebug's
        // rightmost segment cycles through down/distance AND these situational states,
        // just with a different background color per state. TOUCHDOWN is included on a
        // hunch but hasn't been confirmed to appear in this small box (it may only show
        // in the separate full-screen banner below) -- watch the log line for it in a
        // real game and drop it here if it never fires.
        new WatchedRegion
        {
            Name = "situation",
            FxX = 0, FxY = 0.83, FxW = 1.0, FxH = 0.14,
            // FIXED: every other multi-word phrase here defensively allows \s* for OCR
            // word-splitting (PAT\s*GOOD, FAIR\s*CATCH, NO\s*RETURN) -- KICKOFF was the one
            // exception, requiring an exact unbroken match. A wide-tracked/stylized "KICKOFF"
            // graphic OCR-splitting into "KICK OFF" would silently fail this pattern entirely
            // (no match, situation stays whatever it was before) -- see NormalizeMatch's new
            // "kick off" => "kickoff" case below for the other half of this fix.
            // TIME\s*OUT added 2026-08-10 from a live CFB 27 screenshot (Georgia State @ Georgia
            // Southern) showing "Time Out" rendered as spelled-out text in this exact band/slot
            // during a timeout, same as KICKOFF replacing the down/distance line -- confirmed the
            // same underlying scorebug skin as the calibrated Georgia/LSU shots, just unranked
            // teams (no rank number) and different team colors. No downstream evaluator currently
            // keys off this specific value (TimeoutHelper reads the dash-count crop instead, not
            // this text), so it just normalizes to "time_out" via NormalizeMatch's default
            // fallback and sits available in PlaySnapshot.Situation if something needs it later.
            Pattern = new Regex(@"\b(KICK\s*OFF|PAT\s*GOOD|TOUCHDOWN|INTERCEPTED|FUMBLE|TURNOVER|FAIR\s*CATCH|NO\s*RETURN|TIME\s*OUT)\b", RegexOptions.IgnoreCase),
        },
        // Quarter indicator -- reads the HUD's quarter number (sits between the score and the
        // game clock in the bottom scorebug, e.g. "1st | 5:11 | -- | KICKOFF") so we can
        // edge-trigger "Other: Start of 4th Quarter". Shares the same full-width band as
        // "down"/"situation" above for the same broadcast-skin-independence reason -- the
        // quarter text lives in the same score-bug row, just at a different X per skin.
        // Negative lookahead excludes an ordinal followed by "&" so this never matches the
        // down/distance combo instead (see "down" above) -- reading order in the bug always
        // puts the quarter text before down/distance, so the first non-"&" ordinal found here
        // is reliably the quarter, not a down.
        new WatchedRegion
        {
            Name = "quarter",
            FxX = 0, FxY = 0.83, FxW = 1.0, FxH = 0.14,
            Pattern = new Regex(@"\b(1st|2nd|3rd|4th)\b(?!\s*&)", RegexOptions.IgnoreCase),
        },
        // Penalty decision overlay -- calibrated 2026-08-07 from a live screenshot: when a flag
        // is thrown, a separate two-card "PENALTY" overlay appears (accept/decline on one side,
        // the penalized player + "NEUTRAL ZONE INFRACTION - 5 YDS / Against Georgia Tech" on the
        // other). "Against <Team Name>" is the ONLY signal available for which side committed
        // the penalty -- the persistent scorebug's "flag" ribbon (see "flag" region above) is
        // just yellow, not team-colored, so it can't tell offense/defense apart by itself.
        // RouteEngineTick compares this region's matched text against HomeTeamName/AwayTeamName
        // to resolve IsPenaltyOnOffense/IsPenaltyOnDefense. Estimated crop from the one
        // screenshot seen so far (right-hand card, lower text) -- not guaranteed to be this
        // overlay's exact position across every penalty type, may need widening once tested live.
        new WatchedRegion
        {
            Name = "penaltyagainst",
            FxX = 0.65, FxY = 0.62, FxW = 0.32, FxH = 0.22,
            Pattern = new Regex(@"Against\s+[A-Za-z .]{3,30}", RegexOptions.IgnoreCase),
        },
        // The big full-screen scoring banner (e.g. "TOUCHDOWN") -- a wide white ribbon across
        // the bottom-middle of the screen, NOT the small persistent scorebug (it replaces/sits
        // over that area momentarily). Calibrated 2026-08-07 from a live TOUCHDOWN screenshot.
        // Estimated crop, generously widened around the white ribbon text -- not pixel-measured.
        new WatchedRegion
        {
            Name = "banner",
            FxX = 0.35, FxY = 0.87, FxW = 0.3, FxH = 0.08,
            Pattern = new Regex(@"\b(TOUCHDOWN|FIELD GOAL|SAFETY)\b", RegexOptions.IgnoreCase),
        },
        // Score + clock -- unlike down/situation/quarter above, bare digits have no unique
        // textual pattern to regex-match against inside the full-width band (a score digit and
        // a clock digit and a down number all just look like "\d+" to a regex), so these use
        // TIGHT positional crops instead, same reasoning as PossessionCropRect being separate
        // from the wide band. Calibrated 2026-08-07 from live CBS-skin screenshots at 1920x1080
        // (Auburn @ Georgia Tech) -- estimated by eye from the images, not pixel-measured, so
        // treat these as a starting point that likely needs a small live-tightening pass, not
        // as exact. Same FxY/FxH as the wide band above since the score bug shares one row.
        // Pregame team-intro/"READY" screen -- UNCALIBRATED PLACEHOLDER. FxX/FxY/FxW/FxH below
        // are NOT measured from a live screenshot of the CFB27 pregame READY screen and must be
        // treated as 0 (Calibrated => false, so this region is skipped entirely until someone
        // fills these in) -- see the "flag"/"banner" regions above for the same honest-flagging
        // pattern before they got calibrated, and ScorebugPreset.ConsoleScorebugV1's doc comment
        // for the project's convention of saying so explicitly instead of guessing.
        //
        // CRITICAL: when calibrating this for real, the crop box AND the regex below must both
        // stay anchored on team-color-INDEPENDENT elements only -- the READY screen's side panels
        // are colored per-matchup (e.g. red/blue for Ohio State vs Michigan, completely different
        // colors for any other pairing), so this must never key off panel color. Anchor on the
        // "READY" text's fixed screen position instead (or a center rivalry/game-name badge, or
        // the ratings-badge layout -- whatever is confirmed team-neutral from a real screenshot).
        // This project already had to fix three bugs this session caused by exactly the opposite
        // mistake (color-matching something that isn't actually team-neutral) -- see commit
        // b6e1c8f ("Fix dead TFL/Defense/BigEvent signal, kickoff OCR word-split, and possession
        // misread during situation banners"). Do not repeat that pattern here.
        new WatchedRegion
        {
            Name = "pregameready",
            FxX = 0, FxY = 0, FxW = 0, FxH = 0,
            Pattern = new Regex(@"\bREADY\b", RegexOptions.IgnoreCase),
        },
        new WatchedRegion
        {
            Name = "awayscore",
            FxX = 0.35, FxY = 0.83, FxW = 0.05, FxH = 0.14,
            Pattern = new Regex(@"\b\d{1,2}\b"),
        },
        new WatchedRegion
        {
            Name = "homescore",
            FxX = 0.58, FxY = 0.83, FxW = 0.05, FxH = 0.14,
            Pattern = new Regex(@"\b\d{1,2}\b"),
        },
        new WatchedRegion
        {
            Name = "clock",
            FxX = 0.65, FxY = 0.83, FxW = 0.08, FxH = 0.14,
            Pattern = new Regex(@"\b\d{1,2}:\d{2}\b"),
        },
    };

    CancellationTokenSource? _cts;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        // FIXED: was `??=`, so only the FIRST Start() call in the process's lifetime built fresh
        // evaluators -- every subsequent GAMETIME (Stop Watching, then start a new game) reused
        // the SAME evaluator instances, carrying over per-game state like KickoffHelper's
        // opening/second-half-kickoff-already-fired flags into the next game. Now every Start()
        // gets a clean set, matching "Stop Watching... unlock and start a new one" being a real
        // new-game boundary. Same reasoning for resetting the snapshots and first-tick flag --
        // without this, a 2nd+ game's Previous.Quarter starts at whatever the last game ended on
        // instead of 0, so pregame ("Previous.Quarter == 0 && Current.Quarter == 1") could only
        // ever fire once per app launch, not once per game.
        _eventRouter = CreateEventRouter();
        _snapshotPrevious = new();
        _snapshotCurrent = new();
        _isFirstEngineTick = true;
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    async Task RunAsync(CancellationToken ct)
    {
        OcrEngine? ocrEngine = null;
        IntPtr hwnd = IntPtr.Zero;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = FindGameWindow();
                    WindowFoundChanged?.Invoke(hwnd != IntPtr.Zero);
                    if (hwnd == IntPtr.Zero)
                    {
                        // Not on the tackle-detection critical path -- this only fires while the
                        // game window hasn't been found at all yet, so left at 1500ms.
                        await Task.Delay(1500, ct);
                        continue;
                    }
                    Log?.Invoke("[watcher] game window found, hwnd acquired");
                }

                ocrEngine ??= OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? throw new Exception("Could not create OCR engine.");

                // Minimized windows report a valid-looking (often off-screen, e.g. Left=-32000)
                // RECT with a positive but tiny/garbage size, and PrintWindow against a minimized
                // window silently returns a blank/black bitmap instead of failing -- previously
                // this meant the loop kept "succeeding" every tick forever while producing zero
                // usable OCR text, with nothing in the log to explain why. Treat minimized the
                // same as "window gone" so it's visible and retried instead of spinning silently.
                if (Native.IsIconic(hwnd))
                {
                    Log?.Invoke("[watcher] game window is minimized -- pausing capture until restored");
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (!Native.GetWindowRect(hwnd, out Native.RECT rect))
                {
                    Log?.Invoke("[watcher] GetWindowRect failed -- window handle went stale, will re-search");
                    hwnd = IntPtr.Zero;
                    WindowFoundChanged?.Invoke(false);
                    // Not on the tackle-detection critical path -- window handle just went stale
                    // (e.g. game closed/minimized), so left at 1000ms.
                    await Task.Delay(1000, ct);
                    continue;
                }

                int winW = rect.Right - rect.Left;
                int winH = rect.Bottom - rect.Top;
                if (winW <= 0 || winH <= 0)
                {
                    Log?.Invoke($"[watcher] window rect has non-positive size ({winW}x{winH}) -- skipping this tick");
                    await Task.Delay(1000, ct);
                    continue;
                }

                // PrintWindow (tried first) came back blank against this game -- EA's anti-cheat
                // (EAAntiCheat.GameServiceLauncher.exe, confirmed running alongside CFB27) blocks
                // direct window-content capture APIs as a standard anti-cheat measure, so
                // PrintWindow "succeeds" but renders nothing. Graphics.CopyFromScreen (plain
                // desktop pixel capture, not a window-content API) isn't blocked, so that's back
                // to being the actual capture method. Its real limitation is that it reads
                // whatever's visibly on top at those screen coordinates -- so it only produces
                // useful OCR while the game window is genuinely the foreground window (nothing,
                // including Bandroom itself, drawn on top of the capture region). Skip and log
                // instead of silently reading garbage when that's not true, rather than trying to
                // guess at wrong content the way the old title-substring bug did.
                if (Native.GetForegroundWindow() != hwnd)
                {
                    Log?.Invoke("[watcher] game window isn't focused/foreground -- skipping capture this tick (bring the game to the front to resume detection)");
                    await Task.Delay(500, ct);
                    continue;
                }

                using var fullBmp = new Bitmap(winW, winH, PixelFormat.Format32bppArgb);
                using (var fg = Graphics.FromImage(fullBmp))
                {
                    fg.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(winW, winH));
                }

                foreach (var region in _regions)
                {
                    if (!region.Calibrated) continue;

                    // Clamp to >= 0 -- a negative fractional region coordinate would otherwise
                    // produce a crop rectangle with a negative origin, which can throw or read
                    // garbage when drawn from fullBmp below.
                    int cropX = Math.Max(0, (int)(winW * region.FxX));
                    int cropY = Math.Max(0, (int)(winH * region.FxY));
                    // Clamp to at least 1px -- a tiny/minimized game window (or a preset with a
                    // very small fractional height) can round FxW/FxH down to 0, and a 0x0
                    // Bitmap throws ArgumentException, which would otherwise trip the outer
                    // catch every single poll tick until the window is resized.
                    int cropW = Math.Max(1, Math.Min((int)(winW * region.FxW), winW - cropX));
                    int cropH = Math.Max(1, Math.Min((int)(winH * region.FxH), winH - cropY));

                    using var bmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.DrawImage(fullBmp, new Rectangle(0, 0, cropW, cropH),
                            new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                    }

                    string text = await OcrBitmapAsync(ocrEngine, bmp);
                    string trimmedText = text.Trim();
                    if (trimmedText != region.LastRawText)
                    {
                        region.LastRawText = trimmedText;
                        Log?.Invoke($"[{region.Name}] OCR read: \"{trimmedText}\"");
                    }

                    if (region.Name == "down")
                    {
                        // Skip possession color-sampling while a penalty flag is up -- the
                        // rightmost box (which PossessionFx* now targets, see ScorebugPreset)
                        // turns bright yellow for "FLAG" instead of showing a team color, and
                        // Tennessee's own primary color (orange) sits close enough to yellow in
                        // RGB space that ResolveTeamColor's 90-unit tolerance could misread a
                        // penalty review as "Tennessee has the ball." One-tick-stale flag state is
                        // fine here (this "down" region processes before "flag" is re-read the
                        // same tick, but polls every 250ms).
                        bool flagActive = _regions.FirstOrDefault(r => r.Name == "flag")?.Last != null;
                        // FIXED: the same full-width band also repaints to non-team-color states
                        // for KICKOFF/TOUCHDOWN/TURNOVER/PAT GOOD/etc (see the "situation" region
                        // right below "down"), not just FLAG -- but only FLAG had a guard here.
                        // Sampling possession color during e.g. a TOUCHDOWN celebration frame can
                        // feed a bogus color into ResolveTeamColor and flip _lastPossession right
                        // when TouchdownHelper checks state.Delta.NewPossession, attributing the
                        // score to the wrong team. Generalizing the existing FLAG guard to also
                        // cover "situation" being active closes that gap.
                        bool situationActive = _regions.FirstOrDefault(r => r.Name == "situation")?.Last != null;
                        if (!flagActive && !situationActive) SamplePossessionFromWindow(fullBmp, winW, winH);
                    }

                    var match = region.Pattern.Match(text);
                    string? currentValue = match.Success ? NormalizeMatch(region.Name, match.Value) : null;

                    if (region.Name == "down")
                    {
                        var distanceMatch = DistancePattern.Match(text);
                        string? distanceRaw = distanceMatch.Success ? distanceMatch.Groups[1].Value : null;
                        CheckForLossOfYards(distanceRaw);
                        CommitDownAndDistance(currentValue, distanceRaw);
                    }
                    if (region.Name == "awayscore" && currentValue != null) _lastKnownAwayScore = currentValue;
                    if (region.Name == "homescore" && currentValue != null) _lastKnownHomeScore = currentValue;
                    if (region.Name == "quarter" && currentValue != null) _lastKnownQuarter = currentValue;

                    if (currentValue != null && currentValue != region.Last)
                    {
                        region.Last = currentValue;

                        if (DateTime.UtcNow < region.CooldownUntil)
                        {
                            // Same value re-appeared too soon after last firing it -- almost
                            // always a flickery OCR read (e.g. "2nd" -> blank -> "2nd" inside
                            // one second), not a real second event. Update Last but don't fire.
                            Log?.Invoke($"[{region.Name}] suppressed re-fire of \"{currentValue}\" (cooldown)");
                        }
                        else
                        {
                            region.CooldownUntil = DateTime.UtcNow + Cooldown;
                            RegionChanged?.Invoke(region.Name, currentValue);
                            if (region.Name == "down") { DownChanged?.Invoke(currentValue); _downChangedThisTick = true; }
                        }
                    }
                    else if (currentValue == null && !EventGatedRegions.Contains(region.Name))
                    {
                        // Banner/HUD text cleared -- reset so the SAME value can re-trigger
                        // next time it appears (e.g. a second flag later in the game). NOT done
                        // for situation/banner/quarter below -- see EventGatedRegions.
                        region.Last = null;
                    }
                }

                // situation/banner/quarter deliberately do NOT reset on blank OCR the way other
                // regions do (see EventGatedRegions below) -- pausing the game covers the whole
                // HUD, so on unpause the exact same "touchdown"/etc. text reappears and, without
                // this gate, reads as a brand new event and re-fires the sound. Gating the reset
                // on an actual down/distance change instead (a real new snap) means a pause that
                // doesn't span a full play can never cause a re-fire, while a real next score
                // (which always involves at least one down change first -- a new drive/kickoff)
                // still re-arms normally.
                if (_downChangedThisTick)
                {
                    foreach (var region in _regions)
                        if (EventGatedRegions.Contains(region.Name)) region.Last = null;
                    _downChangedThisTick = false;
                }

                // --- Run the Bandroom.Core rule engine from this tick's OCR snapshot ---
                RouteEngineTick();

                // This is the ONE delay on the actual tackle-to-sound critical path: it gates how
                // often the "down" region gets re-OCR'd, and "down" is what both DownChanged and
                // CheckForLossOfYards (tackle-for-loss) key off of. Dropped from 400ms to 250ms --
                // knocks up to 150ms off detection latency on average. Not pushed lower than that:
                // OCR itself takes real time per region per tick, and going much faster starts
                // trading noticeably more CPU for diminishing returns against a 2s Cooldown that
                // already dominates perceived responsiveness on repeat events. The other
                // Task.Delay calls in this loop (1500ms window-search retry, 1000ms rect-failure/
                // error backoff) are recovery paths for "no game window" / "OCR threw", not part
                // of steady-state detection, so they were left alone.
                await Task.Delay(250, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Watcher error: {ex.Message}");
                CrashLog.Write("Watcher error", ex);
                // Not on the tackle-detection critical path -- this only runs after an exception
                // already broke the tick, so left at 1000ms as an error backoff.
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Reads the average background color of the down/distance ribbon and resolves it
    /// to "home"/"away"/null via ResolveTeamColor, edge-triggering PossessionChanged the same
    /// way OCR'd regions do (with the same Cooldown, to avoid flicker firing on a single bad
    /// frame). Averaging the whole crop (not one sample pixel) means the mostly-solid-color
    /// background dominates even with the down/distance digits drawn on top.
    ///
    /// Deliberately NOT reusing the (now full-width) "down" region's bitmap -- that crop was
    /// widened for broadcast-skin-independent text OCR (see the "down" WatchedRegion comment
    /// above) and would wash out the color average with crowd/background pixels outside the
    /// actual ribbon. Possession color sampling stays on this original tight box, calibrated
    /// against the CBS Sports skin; if the ribbon color itself needs skin-independence too,
    /// that's a separate, harder problem (would need locating the ribbon dynamically, not just
    /// widening a crop) -- flag it if this stops matching on a different broadcast skin.</summary>
    /// <summary>Reads the AWAY team's timeout-remaining dash row and counts how many of the
    /// (assumed 3) segments are "lit" (bright dash) vs "dark" (used/empty). Same crop+screenshot
    /// technique as SamplePossessionFromWindow, but averaged PER-SEGMENT instead of across the
    /// whole box -- averaging the whole row into one blob would lose exactly the information
    /// needed (3 lit vs 1 lit both average out differently, but not reliably enough to trust).
    /// Threshold (128 luminance) assumes bright dashes on the scorebug's dark navy background,
    /// matching every screenshot seen calibrating this. Returns -1 if the preset's timeout box
    /// isn't calibrated (FxW/FxH still 0), so callers can tell "not sampled" from "sampled as
    /// zero remaining."</summary>
    int SampleTimeoutSegments(Bitmap fullBmp, int winW, int winH)
    {
        if (_activePreset.AwayTimeoutFxW <= 0 || _activePreset.AwayTimeoutFxH <= 0) return -1;

        int cropX = Math.Max(0, (int)(winW * _activePreset.AwayTimeoutFxX));
        int cropY = Math.Max(0, (int)(winH * _activePreset.AwayTimeoutFxY));
        int cropW = Math.Max(3, Math.Min((int)(winW * _activePreset.AwayTimeoutFxW), winW - cropX));
        int cropH = Math.Max(1, Math.Min((int)(winH * _activePreset.AwayTimeoutFxH), winH - cropY));

        using var bmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(fullBmp, new Rectangle(0, 0, cropW, cropH),
                new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
        }

        const int segments = 3;
        int segmentWidth = Math.Max(1, cropW / segments);
        int litCount = 0;
        for (int s = 0; s < segments; s++)
        {
            long luminanceSum = 0;
            int n = 0;
            int startX = s * segmentWidth;
            int endX = Math.Min(cropW, startX + segmentWidth);
            for (int y = 0; y < cropH; y++)
            for (int x = startX; x < endX; x++)
            {
                var px = bmp.GetPixel(x, y);
                luminanceSum += (px.R + px.G + px.B) / 3;
                n++;
            }
            if (n > 0 && (luminanceSum / n) >= 128) litCount++;
        }
        return litCount;
    }

    void SamplePossessionFromWindow(Bitmap fullBmp, int winW, int winH)
    {
        _lastAwayTimeoutsRemaining = SampleTimeoutSegments(fullBmp, winW, winH);

        // Underline-brightness method takes priority when the active preset has it calibrated
        // (see ScorebugPreset.AwayUnderlineFx*/HomeUnderlineFx*) -- correction 2026-08-07: the
        // real possession signal is a thin lit/dim underline under the team name, not a
        // team-colored fill box. Falls back to the old color-match method (SamplePossession)
        // for presets that predate this, e.g. KamsCbsScorebug.
        if (_activePreset.AwayUnderlineFxW > 0 && _activePreset.HomeUnderlineFxW > 0)
        {
            SamplePossessionByUnderline(fullBmp, winW, winH);
            return;
        }

        int cropX = Math.Max(0, (int)(winW * _activePreset.PossessionFxX));
        int cropY = Math.Max(0, (int)(winH * _activePreset.PossessionFxY));
        int cropW = Math.Max(1, Math.Min((int)(winW * _activePreset.PossessionFxW), winW - cropX));
        int cropH = Math.Max(1, Math.Min((int)(winH * _activePreset.PossessionFxH), winH - cropY));

        using var bmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(fullBmp, new Rectangle(0, 0, cropW, cropH),
                new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
        }
        SamplePossession(bmp);
    }

    /// <summary>Reads the average brightness of the crop directly under each team's name and
    /// calls possession for whichever side is brighter (lit underline = has the ball), same
    /// segment-luminance technique as SampleTimeoutSegments. Requires a clear winner -- if both
    /// sides read within a small margin of each other (ambiguous frame, mid-transition, OCR
    /// caught a half-rendered frame), this deliberately does nothing rather than guess, same
    /// "don't fire on uncertain data" philosophy as the rest of GameWatcher.</summary>
    void SamplePossessionByUnderline(Bitmap fullBmp, int winW, int winH)
    {
        double awayBrightness = SampleCropBrightness(fullBmp, winW, winH,
            _activePreset.AwayUnderlineFxX, _activePreset.AwayUnderlineFxY,
            _activePreset.AwayUnderlineFxW, _activePreset.AwayUnderlineFxH);
        double homeBrightness = SampleCropBrightness(fullBmp, winW, winH,
            _activePreset.HomeUnderlineFxX, _activePreset.HomeUnderlineFxY,
            _activePreset.HomeUnderlineFxW, _activePreset.HomeUnderlineFxH);

        const double minMargin = 15; // luminance points (0-255 scale) -- ignore too-close-to-call frames
        string? side = (awayBrightness - homeBrightness) switch
        {
            > minMargin => "away",
            < -minMargin => "home",
            _ => null,
        };
        if (side == null) return;

        if (side != _lastPossession)
        {
            // FIXED: this used to set _lastPossession = side unconditionally, THEN check the
            // cooldown and bail before firing PossessionChanged -- so a flip during the cooldown
            // window updated the snapshot's PossessionAway (which evaluators read fresh every
            // tick) while WebMainForm._possession (which only updates via the PossessionChanged
            // event, and is what routes "Defense:"/"Offense:" cues to home vs away) silently kept
            // the STALE side. That desync meant an evaluator could correctly detect "user is on
            // defense" and fire "Defense: Second Down", but the routing layer still thought the
            // OLD team had the ball and sent it to that team's wrong (often Offense) audio slot --
            // exactly the "home offense sound plays on defense" / "home first down always fires"
            // reports. Now a flip within the cooldown window is ignored entirely (neither
            // _lastPossession nor the event updates), so the two stay in lockstep -- the next real
            // flip after cooldown expires updates both together.
            if (DateTime.UtcNow < _possessionCooldownUntil)
            {
                Log?.Invoke($"[possession] suppressed re-fire of \"{side}\" (cooldown)");
                return;
            }
            _lastPossession = side;
            _possessionCooldownUntil = DateTime.UtcNow + Cooldown;
            Log?.Invoke($"[possession] now: {side} (underline brightness away={awayBrightness:F0} home={homeBrightness:F0})");
            PossessionChanged?.Invoke(side);
        }
    }

    static double SampleCropBrightness(Bitmap fullBmp, int winW, int winH, double fxX, double fxY, double fxW, double fxH)
    {
        int cropX = Math.Max(0, (int)(winW * fxX));
        int cropY = Math.Max(0, (int)(winH * fxY));
        int cropW = Math.Max(1, Math.Min((int)(winW * fxW), winW - cropX));
        int cropH = Math.Max(1, Math.Min((int)(winH * fxH), winH - cropY));

        using var bmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(fullBmp, new Rectangle(0, 0, cropW, cropH),
                new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
        }

        long luminanceSum = 0;
        int n = 0;
        for (int y = 0; y < cropH; y++)
        for (int x = 0; x < cropW; x++)
        {
            var px = bmp.GetPixel(x, y);
            luminanceSum += (px.R + px.G + px.B) / 3;
            n++;
        }
        return n == 0 ? 0 : (double)luminanceSum / n;
    }

    void SamplePossession(Bitmap bmp)
    {
        if (ResolveTeamColor == null) return;

        long r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < bmp.Height; y += 2)
        for (int x = 0; x < bmp.Width; x += 2)
        {
            var px = bmp.GetPixel(x, y);
            r += px.R; g += px.G; b += px.B;
            n++;
        }
        if (n == 0) return;
        var avg = Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));

        string? side = ResolveTeamColor(avg);

        // FIXED: same desync as SamplePossessionByUnderline -- see its comment. _lastPossession
        // must only update together with the PossessionChanged event, or the snapshot
        // (PossessionAway) and WebMainForm's routing side (_possession) drift apart during the
        // cooldown window.
        if (side != null && side != _lastPossession)
        {
            if (DateTime.UtcNow < _possessionCooldownUntil)
            {
                Log?.Invoke($"[possession] suppressed re-fire of \"{side}\" (cooldown)");
            }
            else
            {
                _lastPossession = side;
                _possessionCooldownUntil = DateTime.UtcNow + Cooldown;
                Log?.Invoke($"[possession] now: {side}");
                PossessionChanged?.Invoke(side);
            }
        }
    }

    /// <summary>Reads the distance-to-go out of the SAME "down" crop already OCR'd this pass
    /// (e.g. "3rd &amp; -4") and edge-triggers TackleForLossDetected when it goes negative --
    /// confirmed via live screenshot that down+distance render as one string, so no separate
    /// region/calibration was needed.</summary>
    void CheckForLossOfYards(string? distanceRaw)
    {
        if (distanceRaw == null || distanceRaw == _lastFiredDistanceRaw) return;
        _lastFiredDistanceRaw = distanceRaw;

        if (int.TryParse(distanceRaw, out int distance) && distance < 0)
        {
            if (DateTime.UtcNow < _lossCooldownUntil)
            {
                Log?.Invoke($"[loss] suppressed re-fire of \"{distanceRaw}\" (cooldown)");
                return;
            }
            _lossCooldownUntil = DateTime.UtcNow + Cooldown;
            Log?.Invoke($"[loss] tackle for loss detected (& {distanceRaw})");
            TackleForLossDetected?.Invoke();
        }
    }

    /// <summary>Stages a Down and/or YardsToGo change and commits both to _lastKnownDown/
    /// _lastDistanceRaw together once both have resolved from OCR, so RouteEngineTick's snapshot
    /// never sees one field advance a tick ahead of the other (see the field comments on
    /// _pendingDown above). Falls back to committing whatever is pending after a short timeout.</summary>
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

    /// <summary>Collapses OCR-noisy variants ("PATGOOD", "PAT  GOOD") of "situation"/"banner"
    /// matches down to a stable key used in triggers.json (situation:pat_good, etc).
    /// "down"/"flag" matches pass through as plain lowercase, unchanged from before.</summary>
    static string NormalizeMatch(string regionName, string rawMatch)
    {
        string collapsed = Regex.Replace(rawMatch, @"\s+", " ").Trim().ToLowerInvariant();
        if (regionName != "situation" && regionName != "banner") return collapsed;

        return collapsed switch
        {
            "intercepted" or "fumble" or "turnover" => "turnover",
            "field goal" => "fieldgoal",
            "fair catch" or "no return" => "nopuntreturn",
            "kick off" => "kickoff", // pairs with the KICK\s*OFF pattern fix above
            _ => collapsed.Replace(" ", "_"),
        };
    }

    static async Task<string> OcrBitmapAsync(OcrEngine engine, Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;

        using var stream = new InMemoryRandomAccessStream();
        using var outputStream = stream.GetOutputStreamAt(0);
        var writer = new DataWriter(outputStream);
        writer.WriteBytes(ms.ToArray());
        await writer.StoreAsync();
        await outputStream.FlushAsync();

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text;
    }

    /// <summary>Parses an ordinal string ("1st","2nd","3rd","4th") to an int, or 0 on failure.</summary>
    static int ParseOrdinal(string? value) => value?.ToLowerInvariant() switch
    {
        "1st" => 1, "2nd" => 2, "3rd" => 3, "4th" => 4,
        _ => 0
    };

    /// <summary>Parses the "clock" region's "m:ss" text (e.g. "7:23") into total seconds, or 0
    /// on failure/not-yet-calibrated. Matches the game clock format seen in the CBS scorebug.</summary>
    static int ParseClockToSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var parts = value.Split(':');
        if (parts.Length != 2) return 0;
        if (!int.TryParse(parts[0], out int minutes) || !int.TryParse(parts[1], out int seconds)) return 0;
        return minutes * 60 + seconds;
    }

    /// <summary>Builds a PlaySnapshot from the current OCR region state and routes it through
    /// all 16 evaluators, firing EventsDetected with the results.</summary>
    void RouteEngineTick()
    {
        if (_eventRouter == null) return;

        var situationRegion = _regions.FirstOrDefault(r => r.Name == "situation");
        var clockRegion = _regions.FirstOrDefault(r => r.Name == "clock");
        var penaltyAgainstRegion = _regions.FirstOrDefault(r => r.Name == "penaltyagainst");
        var pregameReadyRegion = _regions.FirstOrDefault(r => r.Name == "pregameready");
        var bannerRegion = _regions.FirstOrDefault(r => r.Name == "banner");

        int down = ParseOrdinal(_lastKnownDown);
        int quarter = ParseOrdinal(_lastKnownQuarter);
        string? situation = situationRegion?.Last;

        int yardsToGo = 0;
        if (_lastDistanceRaw != null && int.TryParse(_lastDistanceRaw, out int d))
            yardsToGo = d;

        // Sticky last-known values, not the raw region reads -- see the _lastKnownAwayScore/
        // _lastKnownHomeScore/_lastKnownQuarter field comments for why (blank OCR during a pause
        // menu/replay must never look like a score dropping to 0 and rebounding).
        int awayScore = int.TryParse(_lastKnownAwayScore, out int aScore) ? aScore : 0;
        int homeScore = int.TryParse(_lastKnownHomeScore, out int hScore) ? hScore : 0;
        int timeRemainingSeconds = ParseClockToSeconds(clockRegion?.Last);

        // REDEFINED 2026-08-10: BigGame used to be an auto-detect "close score, late quarter"
        // heuristic. Replaced with a pure manual read of ConfigStore.BigGameSettings.Enabled --
        // see that field's doc comment for why (it's now "both bands physically present," a fact
        // about the real-world matchup no OCR signal can detect, not a live-score condition).
        var bigGameSettings = ConfigStore.LoadBigGameSettings();
        bool isBigGame = bigGameSettings.Enabled;

        // "penaltyagainst" holds "Against <Team Name>" text while the penalty decision overlay
        // is up (null otherwise -- see EnsureAllEvents/the region's own comment for why this is
        // the only way to resolve which side committed it). Compare against the known team
        // names to figure out whether the penalized team currently has the ball (= offense) or
        // not (= defense). If team names haven't been set yet (HomeTeamName/AwayTeamName null,
        // e.g. matchup not confirmed) or the text doesn't match either name, both flags stay
        // false -- PenaltyHelper simply won't fire rather than guessing wrong.
        // FIXED 2026-08-07: was `_lastPossession != "away"`, which reads null (possession not
        // detected yet) as "home has it" instead of "unknown" -- silently misrouted penalty
        // events during the pre-detection window right after a matchup is confirmed. Now null
        // stays null so penalty routing waits for a real possession read, same guessing-avoidance
        // rule already used for penalizedIsHome below.
        bool? possessionIsHomeNow = _lastPossession == null ? null : _lastPossession != "away";
        bool? penalizedIsHome = null;
        string? penaltyText = penaltyAgainstRegion?.Last;
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

        // Structural turnover backstop, added 2026-08-10 (owner's own rule, from years in the
        // band watching this exact flow): "if the possession switches on any down besides 4th,
        // that's a turnover." Doesn't replace the OCR-text check (situation == "turnover", which
        // catches INTERCEPTED/FUMBLE/TURNOVER-on-downs) -- ORs with it, so a turnover still fires
        // even on a tick where the CFB 27 default HUD's interception/fumble text hasn't been
        // calibrated yet (see ScorebugPreset.CollegeFootball27's still-open situation-text gaps)
        // or OCR simply misses the frame. Guards: _snapshotPrevious.Down != 4 excludes punts and
        // turnover-on-downs (both change possession on a real 4th down, neither is a "turnover"
        // by the owner's own definition); Down != 0 excludes the pregame/not-yet-read state;
        // excluding any kickoff-adjacent tick excludes the ordinary receiving-team-gets-the-ball
        // "flip" after a score, which is not a turnover either.
        bool possessionFlipped = _lastPossession != null && _snapshotPrevious.PossessionAway != (_lastPossession == "away");
        bool structuralTurnover = possessionFlipped
            && _snapshotPrevious.Down != 4 && _snapshotPrevious.Down != 0
            && situation != "kickoff" && !_snapshotPrevious.IsKickoff;

        var snapshot = new PlaySnapshot
        {
            Down = down,
            YardsToGo = yardsToGo,
            Quarter = quarter,
            PossessionAway = _lastPossession == "away",
            IsKickoff = situation == "kickoff",
            IsPAT = situation == "pat_good",
            IsTouchdown = situation == "touchdown",
            IsTurnover = situation == "turnover" || structuralTurnover,
            IsNoPuntReturn = situation == "nopuntreturn",
            IsPenaltyOnOffense = isPenaltyOnOffense,
            IsPenaltyOnDefense = isPenaltyOnDefense,
            // Not sticky like _lastKnownDown/_lastKnownAwayScore/etc: the READY screen is a
            // one-shot pregame overlay, not a value that legitimately needs to survive a blank
            // OCR tick, so PregameHelper's edge-trigger (Previous.IsPregameReady == false &&
            // Current.IsPregameReady == true) reading straight off region.Last is correct here.
            IsPregameReady = pregameReadyRegion?.Last == "ready",
            IsFieldGoalAttempt = bannerRegion?.Last == "fieldgoal",
            YardLine = 0,
            HomeScore = homeScore,
            AwayScore = awayScore,
            TimeRemainingSeconds = timeRemainingSeconds,
            AwayTimeoutsRemaining = _lastAwayTimeoutsRemaining,
            BigGame = isBigGame,
        };

        _snapshotPrevious = _snapshotCurrent;
        _snapshotCurrent = snapshot;

        // Skip only the true first tick of the game -- Previous is a placeholder `new()` with
        // no real prior read, so comparing it against Current would fire every evaluator
        // simultaneously. See _isFirstEngineTick's declaration for why this can't be inferred
        // from Previous.Down/Quarter == 0 (that's also true, correctly, throughout pregame).
        if (_isFirstEngineTick)
        {
            _isFirstEngineTick = false;
            return;
        }

        var state = new GameState
        {
            Current = _snapshotCurrent,
            Previous = _snapshotPrevious,
            UserIsHome = UserIsHome,
        };

        // The onDuplicateDropped callback fires when two rule evaluators both matched the same
        // EventKey on this tick and only the first is kept (see EventRouter.Dedupe's own comment) --
        // logged here in plain English for the user-facing Event Log rather than inside
        // Bandroom.Core, which has no UI-facing logging of its own.
        var results = _eventRouter.Route(state, dupe =>
            EventActivityLog.Record(dupe.EventKey, "n/a", $"{EventActivityLog.FriendlyEventName(dupe.EventKey)} -- skipped: duplicate of an event we just fired this instant"));
        if (results.Count > 0)
            EventsDetected?.Invoke(results);
    }

    static EventRouter CreateEventRouter()
    {
        var rules = new IRuleEvaluator[]
        {
            new BigEventHelper(),
            new DefenseFirstDownHelper(),
            new DefenseHelper(),
            new DefenseThirdDownShortHelper(),
            new DownFieldPositionHelper(),
            new DriveStarterHelper(),
            new FieldGoalMissedHelper(),
            new FieldGoalPATHelper(),
            new FirstDownHelper(),
            new GameStateEventHelper(),
            new KickoffHelper(),
            new OffenseDownHelper(),
            new NoPuntReturnHelper(),
            new PenaltyHelper(),
            new PregameHelper(),
            new SafetyHelper(),
            new TflHelper(),
            new TimeoutHelper(),
            new TouchdownHelper(),
            new TurnoverHelper(),
        };
        return new EventRouter(rules);
    }

    // Matching by process name (not window-title text) is deliberate: other apps commonly have
    // the game's name in their own title too (e.g. a mod manager's title bar shows
    // "(College Football 27)" for the game it's managing), and a title-substring match would
    // grab whichever window enumerates first -- silently OCR'ing the wrong window's screen
    // region instead of ever finding the real game. "CollegeFB27" is the actual game's process
    // name, confirmed live via Get-Process.
    // "CollegeFB27" is the PC game's own process. Console/Remote Play testers never run that --
    // they run Sony's PS Remote Play client (process "RemotePlay"), which shows the exact same
    // in-game UI inside its own window. Without this, FindGameWindow() always returned
    // IntPtr.Zero for a console tester regardless of which ScorebugPreset was selected in
    // Settings -- the OCR regions were calibrated for Remote Play captures (see
    // ScorebugPreset.ConsoleScorebugV1) but the watcher could never even find a window to read
    // them from. Chrome Remote Desktop/other capture tools aren't matched here since the owner's
    // testers use PS Remote Play specifically; add more names here if that changes.
    static readonly string[] GameProcessNames = { "CollegeFB27", "RemotePlay" };

    // Xbox app streaming (Remote Play from an Xbox console) is a UWP/MSIX package, not a plain
    // Win32 exe like RemotePlay.exe -- its actual top-level window is owned by the shared
    // "ApplicationFrameHost.exe" host process, not by Xbox.exe itself, so Xbox.exe's own
    // MainWindowHandle is reliably IntPtr.Zero and the same Process.GetProcessesByName +
    // MainWindowHandle check that works for RemotePlay/CollegeFB27 would silently find nothing --
    // the exact class of bug the PS Remote Play fix above closed, just for a different reason.
    // Matched by window TITLE on ApplicationFrameHost instead, since every UWP app's host window
    // is titled after the app itself.
    const string XboxHostProcessName = "ApplicationFrameHost";
    const string XboxWindowTitleContains = "Xbox";

    static IntPtr FindGameWindow()
    {
        foreach (var name in GameProcessNames)
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(name))
            {
                IntPtr hWnd = proc.MainWindowHandle;
                if (hWnd != IntPtr.Zero && Native.IsWindowVisible(hWnd)) return hWnd;
            }
        }

        IntPtr xboxHwnd = FindXboxAppWindow();
        if (xboxHwnd != IntPtr.Zero) return xboxHwnd;

        return IntPtr.Zero;
    }

    static IntPtr FindXboxAppWindow()
    {
        if (System.Diagnostics.Process.GetProcessesByName(XboxHostProcessName).Length == 0)
            return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hWnd, _) =>
        {
            if (!Native.IsWindowVisible(hWnd)) return true;
            int len = Native.GetWindowTextLength(hWnd);
            if (len == 0) return true;
            var sb = new System.Text.StringBuilder(len + 1);
            Native.GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().Contains(XboxWindowTitleContains, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
