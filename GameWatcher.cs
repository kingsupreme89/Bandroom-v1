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

            // AUDIT FIX 2026-08-12: region.Last/LastRawText/CooldownUntil are edge-trigger state
            // for the OLD crop position -- switching presets mid-session moves the crop to a
            // physically different part of the screen (see the FxX/FxY/FxW/FxH reassignment right
            // above), so whatever text/cooldown that region last observed under the PREVIOUS
            // preset describes pixels that are no longer even being sampled. Left uncleared, two
            // failure modes: (1) if the new crop happens to read the same text the old crop last
            // held, the `currentValue != region.Last` edge-trigger check below in RunAsync silently
            // swallows the first real read under the new preset as "unchanged"; (2) a still-live
            // CooldownUntil from the old preset can suppress a legitimate first fire under the new
            // one. Reset unconditionally for every region here (not just the ones whose Fx* changed
            // above) since preset-switching is rare (a user action, not a per-tick cost) and every
            // region's on-screen position is preset-dependent in general.
            region.Last = null;
            region.LastRawText = null;
            region.CooldownUntil = default;
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

    /// <summary>Added 2026-08-12 for the CFB27-only field-position volume system (owner request,
    /// live-tested build): CFB27's default scorebug shows a ball-position number with an arrow
    /// next to it ("26▲"/"35▼", see ScorebugPreset.CollegeFootball27's Possession doc comment) --
    /// up means the ball is past midfield on the offense's attacking side, down means the
    /// opposite. Sampled by SampleFieldPositionArrowFromWindow, OCR'd from the same crop already
    /// used for underline-brightness possession (no new preset fields). Null until a parseable
    /// read comes in, or whenever the active preset isn't CFB27 -- WebMainForm only applies the
    /// arrow-based multiplier when this has a value. CAVEAT: OCR reading an arrow GLYPH (not a
    /// normal font character) is unproven -- best-effort first pass, expect to tune the accepted
    /// character set and/or the crop box against a live game.</summary>
    public bool? ArrowUp { get; private set; }

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

    /// <summary>Whether the user's own team (Bandroom's "home", per UserIsHome always being true
    /// -- see SetGameTeamsFromWeb) is drawn on the LEFT side of CFB27's own scorebug this game.
    /// SamplePossessionByUnderline only ever reads screen POSITION (a brightness comparison
    /// between a left-side crop and a right-side crop, see AwayUnderlineFx*/HomeUnderlineFx*'s
    /// field names) -- it has no way to check which actual team occupies which slot. CFB27 draws
    /// teams by the GAME's own home/away assignment (whichever team's home stadium it is), which
    /// is completely independent of which team the user picked as "their side" in Bandroom's own
    /// matchup picker. Defaulting this false (assume the user's team is on the right/"home" slot,
    /// matching every preset's AwayUnderlineFx=left/HomeUnderlineFx=right field naming) preserves
    /// the existing behavior for the common case (user playing as the true home team); set true
    /// whenever the user is the visiting team in-game instead, or every possession read comes out
    /// backwards for the whole game -- reported live as "wrong side's song played," a full-game
    /// systematic inversion, not a per-tick flicker (2026-08-11).</summary>
    public bool UserTeamOnLeftSide { get; set; }
    // FIXED 2026-08-11 (found from live screenshots, not caught by the code-only audit earlier
    // this session): "3rd & inches" and "1st & Goal" both render with no digit at all -- the
    // original digit-only pattern simply never matched either, silently leaving YardsToGo frozen
    // on whatever the PREVIOUS down's distance happened to be instead of updating it, which could
    // misclassify a genuinely short down as long (or vice versa) downstream. "inches" is always
    // under a yard (unambiguously short); "Goal" is owner's explicit call (2026-08-11) to also
    // treat as short for the hype logic, even though the real yard-to-go varies -- both now
    // normalize to "1" via NormalizeDistanceRaw below instead of leaving the field stale.
    static readonly Regex DistancePattern = new(@"&\s*(-?\d+|inches|goal)", RegexOptions.IgnoreCase);

    static string NormalizeDistanceRaw(string raw) =>
        int.TryParse(raw, out _) ? raw : "1";
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
    // FIXED 2026-08-11 (live bug: false "Turnover Forced" mid-3rd-and-long, no kickoff, no real
    // turnover involved -- confirmed via Event Log). Every other OCR-derived sticky field in this
    // file (_lastKnownDown, _lastKnownAwayScore/_lastKnownHomeScore, _lastKnownQuarter) already
    // requires the SAME value on 2 consecutive ticks before committing (see CommitValueIfConfirmed)
    // because a single bad OCR frame was previously found to cause phantom score/quarter events.
    // Possession never got the same treatment -- SamplePossessionByUnderline/SamplePossession used
    // to commit and fire PossessionChanged off ONE frame's brightness/color read. Worse than an
    // ordinary one-tick blip: once a bad frame commits, _possessionCooldownUntil locks for 2
    // seconds, which blocks the VERY NEXT (correct) frame from correcting it back -- so a single
    // flicker got stuck wrong for up to 2 seconds, misrouting every event fired in that window
    // (matches the observed live sequence: a real "Defense: Third Down" fired correctly, then a
    // spurious structural-turnover fired right after off the same bad flip). Same confirm-before-
    // commit shape as CommitValueIfConfirmed, just tracked inline here since possession's commit
    // path also needs to fire PossessionChanged/reset the cooldown, which that shared helper
    // doesn't do.
    string? _pendingPossession;

    /// <summary>How many CONSECUTIVE ticks _pendingPossession has matched the newly-sampled side
    /// -- see ConfirmPossessionFlip. Reset to 0 whenever the sampled side changes.</summary>
    int _pendingPossessionTicks;

    /// <summary>How many CONSECUTIVE ticks the structural-turnover heuristic's full condition has
    /// held -- see its own comment in RouteEngineTick for why this exists (2 required, not 1).</summary>
    int _structuralTurnoverPendingTicks;

    // FIXED 2026-08-11 (live bug: repeating "Turnover Forced"/"Drive Starter" loop, alternating
    // home/away, while CFB27 was sitting on its own pause menu -- confirmed via Event Log
    // screenshot, "PAUSED" visible on screen the whole time). Root cause: nothing in this file
    // treats "the game is paused" as a distinct state -- GetForegroundWindow()'s guard above only
    // skips capture when some OTHER app has focus, but CFB27's own pause menu keeps the game
    // window focused/foreground the whole time, so capture kept running against pause-menu
    // pixels instead of the real scorebug. The possession-underline crop in particular reads
    // whatever's now drawn at its normal screen coordinates -- pause-menu content sitting there
    // produced a borderline left/right brightness split that flip-flopped every commit cycle,
    // and each flip looked like a real structural turnover (down==1, not a kickoff) to
    // RouteEngineTick, which then fired DriveStarterHelper's cue for the "new" drive too --
    // repeating for as long as the pause menu stayed up. Fix: hash a coarse sample of the WHOLE
    // captured frame every tick (not just the scorebug crop) -- live gameplay always has some
    // motion somewhere on screen (crowd, camera, HUD animation), even during a pre-snap huddle,
    // so a frame that's byte-identical to the last several ticks in a row can only mean the
    // display itself has actually stopped updating (a pause/menu/loading screen). RouteEngineTick
    // is skipped entirely while frozen -- no OCR text is trusted, no events fire -- and resumes
    // the instant the frame starts changing again.
    // LOWERED 2026-08-12 (owner report + screenshot: pausing the game fired 2 spurious events,
    // both timestamped inside the same second) -- 4 ticks (~1s) left a real exposure window where
    // the pause menu's completely different layout (a full score-summary box, not the live
    // scorebug) still got OCR'd/color-sampled as if it were real gameplay before the freeze
    // streak tripped. 2 ticks (~0.5s) still comfortably rules out real in-play stillness (a
    // pre-snap huddle still has crowd/camera motion somewhere in the coarse grid every tick) while
    // roughly halving how much of the paused/menu screen gets processed before detection suspends.
    const int FrozenFrameTicksThreshold = 2; // ~0.5s at the 250ms poll interval
    int _frozenFrameHash;
    int _frozenFrameStreak;
    bool _frameIsFrozen;

    /// <summary>-1 = not yet sampled (TimeoutHelper's own range check, `&lt; 0`, means it
    /// correctly just won't fire until a real reading comes in, rather than defaulting to a
    /// value that could misfire). Updated every tick by SampleTimeoutSegments.</summary>
    int _lastAwayTimeoutsRemaining = -1;
    /// <summary>Home counterpart, added 2026-08-11 -- see ScorebugPreset.HomeTimeoutFx*'s doc
    /// comment for the placeholder-crop caveat. -1 the same way if HomeTimeoutFxW/H aren't
    /// calibrated for the active preset (never true today; every preset now sets a placeholder).</summary>
    int _lastHomeTimeoutsRemaining = -1;
    // FIXED 2026-08-11 (systemic audit finding, same bug class as the possession-debounce fix):
    // this used to commit straight from SampleTimeoutSegments's per-tick brightness read with no
    // confirmation at all -- a single frame reading one segment dim when it's actually lit
    // (anti-aliasing, camera pan, compression artifact near the 128-luminance threshold) could
    // commit a wrong count immediately, and TimeoutHelper.cs fires "Defense: Timeout (N Remaining)"
    // on any Previous->Current decrement, so a one-tick dip (e.g. 3->2->3) fires a real phantom/
    // wrong-count event on the 3->2 tick alone. Same confirm-before-commit shape as
    // ConfirmPossessionFlip below, minus the PossessionChanged-event/cooldown bookkeeping this
    // doesn't need. -1 ("not calibrated for this preset," see SampleTimeoutSegments) passes
    // through immediately rather than needing confirmation -- it's a constant not-sampled state,
    // not a flicker risk.
    int? _pendingAwayTimeoutsRemaining;
    int? _pendingHomeTimeoutsRemaining;

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
    /// the same trigger repeatedly. Exposed in the UI's Settings panel, so not readonly.
    /// TRIMMED 2026-08-11 (owner call: this is also what forced FirstDownHelper's punt-vs-
    /// conversion buffer up to 3s -- see that file's MaxPendingTicks comment, which must stay
    /// in sync with this value's worst case). 2.0s -> 1.2s: still long enough to absorb a
    /// couple seconds of OCR flicker right at a possession change, just not as punishing on
    /// the legitimate-conversion wait it forces downstream.</summary>
    public static TimeSpan Cooldown = TimeSpan.FromSeconds(1.2);

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

    // Structural-turnover guard state: the "kickoff"/IsKickoff signal is transient (only true
    // on the tick(s) the situation ribbon actually shows "KICK OFF"), but a real kick return can
    // span several OCR ticks before the first down/distance ribbon settles on "1st & 10" for the
    // receiving team. By the time that first-down tick lands, `situation` has usually gone blank
    // and `_snapshotPrevious.IsKickoff` is already false, so the structural-turnover backstop's
    // per-tick kickoff check (below) doesn't see it and wrongly reads the receiving team getting
    // the ball as a possession-flip turnover. Track "we're still inside a kickoff-to-first-snap
    // sequence" as sticky state instead of a single-tick flag: set true the moment a kickoff is
    // observed, cleared only once a real down (1-4) is read for the first time afterward.
    bool _awaitingPostKickoffSnap;

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
    // A single misread OCR frame (e.g. "14" -> "16" -> "14" for one 250ms tick, no blank in
    // between) isn't covered by the sticky-value fix above -- that only guards against BLANK
    // reads during pauses, not a bad-but-non-blank digit read landing on a real committed value
    // for exactly one tick. Reported live as "Safety" firing off a phantom +2. Require the same
    // new value on two consecutive ticks before committing, same debounce idea as CommitDownAndDistance.
    string? _pendingAwayScore;
    string? _pendingHomeScore;
    string? _lastKnownQuarter;
    // Same single-bad-frame risk as score (a misread "1st"->"3rd"->"1st" for one tick could
    // falsely trigger GameStateEventHelper's quarter-transition cues) -- added same session,
    // same CommitValueIfConfirmed debounce, found by audit rather than a live report.
    string? _pendingQuarter;

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
            // FIELD\s*GOAL (not a literal "FIELD GOAL") -- confirmed from a live screenshot
            // 2026-08-11 that this skin's banner renders it as one solid word "FIELDGOAL", same
            // single-word style as TOUCHDOWN/SAFETY. The literal-space version silently never
            // matched, so IsFieldGoalAttempt was always false (see FieldGoalMissedHelper.cs).
            Pattern = new Regex(@"\b(TOUCHDOWN|FIELD\s*GOAL|SAFETY)\b", RegexOptions.IgnoreCase),
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
        _awaitingPostKickoffSnap = false;
        _pendingPossession = null;
        _pendingPossessionTicks = 0;
        _pendingAwayTimeoutsRemaining = null;
        _pendingHomeTimeoutsRemaining = null;
        _structuralTurnoverPendingTicks = 0;
        _frozenFrameHash = 0;
        _frozenFrameStreak = 0;
        _frameIsFrozen = false;
        // AUDIT FIX 2026-08-12: the reset list above only ever covered the fields touched by the
        // specific live bugs each was added for -- it was never treated as the COMPLETE list of
        // state that can outlive a single game. Every "sticky, deliberately never nulled on a
        // blank OCR read" field (that's the whole point of _lastKnownDown/_lastKnownAwayScore/
        // _lastKnownHomeScore/_lastKnownQuarter/_lastDistanceRaw/_lastPossession -- see their own
        // doc comments) is EXACTLY the kind of field that then silently survives a Stop-Watching-
        // then-Start-a-new-game boundary too, since nothing else ever clears them either. A second
        // game starting with e.g. _lastKnownHomeScore still at the first game's final score (or
        // _lastPossession/_possessionCooldownUntil still locked from the first game's last flip)
        // would compute a bogus PlaySnapshot delta on the new game's very first real read and could
        // fire a phantom scoring/possession event before a single legitimate OCR tick of the new
        // game has even landed. Reset every one of them here so a new Start() is a genuinely clean
        // slate, matching the reasoning already given above for _eventRouter/_snapshotPrevious.
        _lastDistanceRaw = null;
        _lastFiredDistanceRaw = null;
        _lossCooldownUntil = default;
        _pendingDown = null;
        _pendingDistanceRaw = null;
        _pendingDownDistanceDeadline = default;
        _lastPossession = null;
        _possessionCooldownUntil = default;
        _lastAwayTimeoutsRemaining = -1;
        _lastHomeTimeoutsRemaining = -1;
        _lastKnownDown = null;
        _lastKnownAwayScore = null;
        _lastKnownHomeScore = null;
        _pendingAwayScore = null;
        _pendingHomeScore = null;
        _lastKnownQuarter = null;
        _pendingQuarter = null;
        ArrowUp = null;
        _lastPregameEntranceMarker = false;
        _downChangedThisTick = false;
        // Per-region edge-trigger/cooldown state (Last/LastRawText/CooldownUntil) lives on the
        // WatchedRegion instances in _regions, which -- unlike _snapshotPrevious/_snapshotCurrent
        // above -- are NOT recreated per Start() (the list is built once as a readonly field), so
        // without this loop a second game would inherit the first game's edge-triggered "Last"
        // values (suppressing a real first-tick DownChanged/RegionChanged if the new game happens
        // to start on the same text) and still-live CooldownUntil timestamps.
        foreach (var region in _regions)
        {
            region.Last = null;
            region.LastRawText = null;
            region.CooldownUntil = default;
        }
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

                UpdateFrozenFrameState(fullBmp, winW, winH);

                foreach (var region in _regions)
                {
                    if (!region.Calibrated) continue;

                    // Clamped both low (>= 0) and high (inside the bitmap) -- see ClampCropRect's
                    // doc comment. Also guards the "tiny/minimized window rounds FxW/FxH to 0"
                    // case (0x0 Bitmap throws ArgumentException, tripping the outer catch every
                    // tick until the window is resized) via the Math.Max(1, ...) floor.
                    var (cropX, cropY, cropW, cropH) = ClampCropRect(winW, winH, region.FxX, region.FxY, region.FxW, region.FxH);

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
                        // FIXED same session (audit finding): "situation" doesn't necessarily
                        // cover TOUCHDOWN -- the "situation" region's own comment already notes
                        // TOUCHDOWN may only ever render in the separate full-screen "banner"
                        // region, not the small situation box. Without checking banner too, a
                        // touchdown celebration frame could still get its possession color
                        // sampled and misattribute the score to the wrong team, the exact bug
                        // this guard exists to prevent.
                        bool bannerActive = _regions.FirstOrDefault(r => r.Name == "banner")?.Last != null;
                        if (!flagActive && !situationActive && !bannerActive) SamplePossessionFromWindow(fullBmp, winW, winH);
                        // FIXED 2026-08-12: must NOT share the guard above -- see
                        // SampleTimeoutsFromWindow's own doc comment for why the "Time Out" banner
                        // being up is exactly the window this needs to keep sampling in.
                        SampleTimeoutsFromWindow(fullBmp, winW, winH);
                        // Field-position arrow (CFB27-only, see ArrowUp's doc comment) -- same
                        // "sample every down tick regardless of flag/situation/banner" reasoning as
                        // timeouts above: the arrow readout sits right next to the score digits,
                        // not inside the color-fill area those guards protect.
                        if (!flagActive) await SampleFieldPositionArrowFromWindow(fullBmp, winW, winH, ocrEngine);
                        // Pregame chevron marker -- same "sample unconditionally" reasoning as
                        // timeouts/arrow above, not gated on flag/situation/banner: this crop only
                        // matters during pregame anyway (see PlaySnapshot.IsPregameEntranceMarker),
                        // well before any of those in-game overlays are relevant.
                        SamplePregameEntranceFromWindow(fullBmp, winW, winH);
                    }

                    var match = region.Pattern.Match(text);
                    string? currentValue = match.Success ? NormalizeMatch(region.Name, match.Value) : null;

                    if (region.Name == "down")
                    {
                        var distanceMatch = DistancePattern.Match(text);
                        string? distanceRaw = distanceMatch.Success ? NormalizeDistanceRaw(distanceMatch.Groups[1].Value) : null;
                        // FIXED 2026-08-11 (systemic audit finding, same bug class as the
                        // possession-debounce fix): this used to call CheckForLossOfYards with
                        // the RAW per-tick distanceRaw, firing TackleForLossDetected off a single
                        // unconfirmed OCR frame -- a single misread digit/sign (e.g. "& 4" ->
                        // "& -4") could fire a phantom tackle-for-loss immediately, cooldown-only
                        // protected (which stops re-fires, not the initial false one). Swapped to
                        // read the already-debounced _lastDistanceRaw (committed by
                        // CommitDownAndDistance right below, which requires the SAME distance to
                        // resolve via its own pending/timeout buffer) instead of the raw value, so
                        // loss detection now shares the same confirmed data every other evaluator
                        // already relies on rather than a separate unconfirmed read of its own.
                        CommitDownAndDistance(currentValue, distanceRaw);
                        CheckForLossOfYards(_lastDistanceRaw);
                    }
                    if (region.Name == "awayscore" && currentValue != null)
                        CommitValueIfConfirmed(currentValue, ref _pendingAwayScore, ref _lastKnownAwayScore);
                    if (region.Name == "homescore" && currentValue != null)
                        CommitValueIfConfirmed(currentValue, ref _pendingHomeScore, ref _lastKnownHomeScore);
                    if (region.Name == "quarter" && currentValue != null)
                        CommitValueIfConfirmed(currentValue, ref _pendingQuarter, ref _lastKnownQuarter);

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
                // Skipped entirely while the frame is frozen (see _frameIsFrozen's doc comment) --
                // a paused/menu screen has no real snap/play to react to, and OCR text sampled
                // from whatever's drawn there is untrustworthy.
                if (!_frameIsFrozen)
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
    int SampleTimeoutSegments(Bitmap fullBmp, int winW, int winH) =>
        SampleTimeoutSegments(fullBmp, winW, winH, _activePreset.AwayTimeoutFxX, _activePreset.AwayTimeoutFxY,
            _activePreset.AwayTimeoutFxW, _activePreset.AwayTimeoutFxH);

    /// <summary>Home counterpart's entry point -- same brightness-sampling method, just pointed at
    /// ScorebugPreset.HomeTimeoutFx* instead of AwayTimeoutFx*.</summary>
    int SampleHomeTimeoutSegments(Bitmap fullBmp, int winW, int winH) =>
        SampleTimeoutSegments(fullBmp, winW, winH, _activePreset.HomeTimeoutFxX, _activePreset.HomeTimeoutFxY,
            _activePreset.HomeTimeoutFxW, _activePreset.HomeTimeoutFxH);

    int SampleTimeoutSegments(Bitmap fullBmp, int winW, int winH, double fxX, double fxY, double fxW, double fxH)
    {
        if (fxW <= 0 || fxH <= 0) return -1;

        var (cropX, cropY, cropWRaw, cropH) = ClampCropRect(winW, winH, fxX, fxY, fxW, fxH);
        // Needs >= 3px (one per timeout segment) rather than ClampCropRect's generic >= 1px floor
        // -- re-clamp against the same winW - cropX ceiling so this never re-introduces the
        // beyond-bounds risk ClampCropRect exists to prevent.
        //
        // BUG FIX (audit finding, 2nd pass): cropWRaw is already <= winW - cropX (that's what
        // ClampCropRect guarantees), so Math.Min(cropWRaw, winW - cropX) below was always just
        // cropWRaw -- a no-op -- and the surrounding Math.Max(3, ...) could then push cropW back
        // PAST winW - cropX whenever less than 3px of room was actually left (a small/unusual
        // capture width, or a preset's FxX sitting near the right edge). That handed DrawImage a
        // source rectangle wider than the bitmap again -- exactly what ClampCropRect exists to
        // prevent -- despite this comment's claim that it couldn't happen. Take the 3px floor
        // first, THEN clamp to whatever room is actually available (never re-widening past the
        // bound); if that leaves fewer than 3px, degrade to what's available instead of reading
        // out of bounds -- SegmentWidth below already floors at 1px per segment, so a narrower
        // crop just yields less accurate (not crashing) timeout-segment sampling.
        int cropW = Math.Min(Math.Max(3, cropWRaw), Math.Max(1, winW - cropX));

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

    /// <summary>Requires the SAME sampled count on two consecutive ticks before committing to
    /// _lastAwayTimeoutsRemaining -- see that field's doc comment for the phantom-timeout bug
    /// this closes. -1 ("not calibrated") always passes through immediately.</summary>
    void CommitTimeoutsRemainingIfConfirmed(int sample)
    {
        if (sample < 0) { _lastAwayTimeoutsRemaining = sample; _pendingAwayTimeoutsRemaining = null; return; }
        if (sample == _lastAwayTimeoutsRemaining) { _pendingAwayTimeoutsRemaining = null; return; }
        if (sample == _pendingAwayTimeoutsRemaining) { _lastAwayTimeoutsRemaining = sample; _pendingAwayTimeoutsRemaining = null; return; }
        _pendingAwayTimeoutsRemaining = sample;
    }

    /// <summary>Home counterpart -- same two-tick confirm-before-commit shape as the Away version
    /// above (see _lastAwayTimeoutsRemaining's doc comment for why), just against the Home
    /// fields.</summary>
    void CommitHomeTimeoutsRemainingIfConfirmed(int sample)
    {
        if (sample < 0) { _lastHomeTimeoutsRemaining = sample; _pendingHomeTimeoutsRemaining = null; return; }
        if (sample == _lastHomeTimeoutsRemaining) { _pendingHomeTimeoutsRemaining = null; return; }
        if (sample == _pendingHomeTimeoutsRemaining) { _lastHomeTimeoutsRemaining = sample; _pendingHomeTimeoutsRemaining = null; return; }
        _pendingHomeTimeoutsRemaining = sample;
    }

    /// <summary>FIXED 2026-08-12 (code-review finding on the IsTimeout gating change): this used
    /// to run inside SamplePossessionFromWindow, which is itself skipped whenever
    /// flagActive/situationActive/bannerActive is true (guards added to protect POSSESSION-COLOR
    /// sampling from misreading during a non-team-colored banner frame). "situation" reading
    /// "time_out" makes situationActive true for the ENTIRE window the "Time Out" banner is up --
    /// and since "situation" is also in EventGatedRegions (doesn't clear on blank OCR, only on the
    /// next down change), that block persisted well past the banner too. TimeoutHelper's new
    /// IsTimeout-based gate (see that file) needs the timeout-remaining COUNT to actually update
    /// during exactly that window to detect the decrement -- the two guards directly contradicted
    /// each other, so the "fire off the real banner" fix could never actually see a confirmed
    /// decrement and would fire for essentially no real timeout. Timeout-segment sampling has none
    /// of the color-misread failure mode the flag/situation/banner guards exist for (it reads a
    /// dash/segment lit-count in its own dedicated crop, not team color), so it's pulled out here
    /// to run unconditionally every tick instead of being gated alongside possession-color
    /// sampling.</summary>
    /// <summary>Reads the brightness of ScorebugPreset.ChevronMarkerFx* -- the white chevron
    /// shape flanking the center bowl/rivalry badge on the pregame walkout scorebug (see that
    /// field's doc comment). No-op (leaves _lastPregameEntranceMarker false) when the active
    /// preset hasn't calibrated this crop (ChevronMarkerFxW == 0), same "uncalibrated = skip, no
    /// guess" convention as every other optional region in this file -- GameStateEventHelper's
    /// quarter/down heuristic is the fallback for those presets. High threshold (200/255) since
    /// this is meant to be a solid white shape on a dark background, not a soft glow -- a
    /// near-miss crop landing on darker scorebug chrome should read comfortably below it rather
    /// than borderline-triggering.</summary>
    const double PregameChevronBrightnessThreshold = 200;
    bool _lastPregameEntranceMarker;

    void SamplePregameEntranceFromWindow(Bitmap fullBmp, int winW, int winH)
    {
        if (_activePreset.ChevronMarkerFxW <= 0)
        {
            _lastPregameEntranceMarker = false;
            return;
        }
        double brightness = SampleCropBrightness(fullBmp, winW, winH,
            _activePreset.ChevronMarkerFxX, _activePreset.ChevronMarkerFxY,
            _activePreset.ChevronMarkerFxW, _activePreset.ChevronMarkerFxH);
        _lastPregameEntranceMarker = brightness >= PregameChevronBrightnessThreshold;
    }

    void SampleTimeoutsFromWindow(Bitmap fullBmp, int winW, int winH)
    {
        CommitTimeoutsRemainingIfConfirmed(SampleTimeoutSegments(fullBmp, winW, winH));
        CommitHomeTimeoutsRemainingIfConfirmed(SampleHomeTimeoutSegments(fullBmp, winW, winH));
    }

    void SamplePossessionFromWindow(Bitmap fullBmp, int winW, int winH)
    {
        // REORDERED 2026-08-12 (owner call, backed by the original "Kam's CBS Scorebug" preset's
        // own 2026-08-08 finding): color-match on the down-and-distance box now takes priority
        // when the active preset has it calibrated (see ScorebugPreset.PossessionFx*) -- it's a
        // solid, discrete team-colored fill specifically meant to key off possession, proven more
        // reliable than the underline method back when it was actually pointed at the right box
        // (see ScorebugPreset.KamsCbsScorebugV3's restored PossessionFx* comment for the full
        // history of why this got dropped and brought back). SamplePossession itself already
        // "doesn't guess" -- ResolveTeamColor returns null on a near-black or ambiguous sample, in
        // which case this falls through to the underline method below as a secondary signal for
        // that tick, rather than reporting nothing.
        if (_activePreset.PossessionFxW > 0)
        {
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
            if (SamplePossession(bmp)) return;
        }

        if (_activePreset.AwayUnderlineFxW > 0 && _activePreset.HomeUnderlineFxW > 0)
            SamplePossessionByUnderline(fullBmp, winW, winH);
    }

    /// <summary>Reads the average brightness of the crop directly under each team's name and
    /// calls possession for whichever side is brighter (lit underline = has the ball), same
    /// segment-luminance technique as SampleTimeoutSegments. Requires a clear winner -- if both
    /// sides read within a small margin of each other (ambiguous frame, mid-transition, OCR
    /// caught a half-rendered frame), this deliberately does nothing rather than guess, same
    /// "don't fire on uncertain data" philosophy as the rest of GameWatcher.</summary>
    void SamplePossessionByUnderline(Bitmap fullBmp, int winW, int winH)
    {
        // Despite the field names, these are physically LEFT and RIGHT screen crops -- every
        // preset calibrated "AwayUnderlineFx" to the left slot and "HomeUnderlineFx" to the right
        // slot because that's how CFB27 (and broadcast convention generally) lays out the two
        // teams, not because they know which one is Bandroom's own "home" team. See
        // UserTeamOnLeftSide's doc comment for why that distinction matters.
        double leftBrightness = SampleCropBrightness(fullBmp, winW, winH,
            _activePreset.AwayUnderlineFxX, _activePreset.AwayUnderlineFxY,
            _activePreset.AwayUnderlineFxW, _activePreset.AwayUnderlineFxH);
        double rightBrightness = SampleCropBrightness(fullBmp, winW, winH,
            _activePreset.HomeUnderlineFxX, _activePreset.HomeUnderlineFxY,
            _activePreset.HomeUnderlineFxW, _activePreset.HomeUnderlineFxH);

        // Raised from 15 -> 25 (owner report 2026-08-11, live game): an 18-point flip that night
        // was reported wrong, right at the old threshold's edge -- 15 was letting genuinely
        // marginal/borderline frames through as if they were confident reads. (A separate 41-point
        // flip was also reported wrong that night; that one clears even this raised bar, so it's
        // not something a margin bump alone fixes -- occasional bad reads on otherwise-clear
        // frames are a different problem than borderline ones slipping through.)
        const double minMargin = 25; // luminance points (0-255 scale) -- ignore too-close-to-call frames
        string? physicalSide = (leftBrightness - rightBrightness) switch
        {
            > minMargin => "left",
            < -minMargin => "right",
            _ => null,
        };
        if (physicalSide == null) return;

        // Translate screen position to Bandroom's home/away labels via UserTeamOnLeftSide --
        // the whole reason this indirection exists instead of returning "left"/"right" directly.
        string side = UserTeamOnLeftSide
            ? (physicalSide == "left" ? "home" : "away")
            : (physicalSide == "left" ? "away" : "home");

        if (!ConfirmPossessionFlip(side)) return;

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
            // FIXED 2026-08-12 (live bug: TFL/Fourth Down routed to the wrong side): a phantom
            // flip committed off 2 bad-but-agreeing ticks (replay overlay/camera cut over the
            // underline crop) locks _lastPossession wrong for the WHOLE cooldown, and any event
            // that fires in that window misroutes -- the correct read shows up 1-2 ticks later
            // but was being unconditionally suppressed until the full Cooldown expired. Now a
            // late-cooldown correction is allowed, but only when it's clearly not another
            // borderline/noisy read: requires a much stronger margin than the normal 25-point
            // bar (so an ordinary marginal frame still can't trigger it) AND at least half the
            // cooldown must have already elapsed (so a same-tick flicker right after the first
            // commit still can't immediately undo it). Still goes through the same single commit
            // path below (both _lastPossession and PossessionChanged update together), so the
            // lockstep guarantee above is preserved either way.
            const double correctionMinMargin = 35;
            double correctionElapsedSeconds = (Cooldown - (_possessionCooldownUntil - DateTime.UtcNow)).TotalSeconds;
            double margin = Math.Abs(leftBrightness - rightBrightness);
            bool allowCorrection = correctionElapsedSeconds >= Cooldown.TotalSeconds / 2 && margin >= correctionMinMargin;
            if (!allowCorrection)
            {
                Log?.Invoke($"[possession] suppressed re-fire of \"{side}\" (cooldown)");
                return;
            }
            Log?.Invoke($"[possession] correcting cooldown-locked flip to \"{side}\" (margin={margin:F0}, {correctionElapsedSeconds:F2}s into cooldown)");
        }
        _pendingPossession = null;
        _lastPossession = side;
        _possessionCooldownUntil = DateTime.UtcNow + Cooldown;
        Log?.Invoke($"[possession] now: {side} (underline brightness left={leftBrightness:F0} right={rightBrightness:F0}, UserTeamOnLeftSide={UserTeamOnLeftSide})");
        PossessionChanged?.Invoke(side);
    }

    /// <summary>CFB27-only field-position arrow read (see ArrowUp's doc comment) -- OCR's the same
    /// two crops SamplePossessionByUnderline already samples for brightness (only one of the two
    /// slots actually renders ball-position+arrow text at a time, whichever side has the ball), and
    /// looks for an up/down arrow glyph in whichever crop actually produced text. Deliberately does
    /// NOT touch _lastPossession/PossessionChanged -- this is a separate signal, read-only side
    /// effect on ArrowUp. Best-effort: OCR reliably reading a small arrow GLYPH (not a normal
    /// alphanumeric character) is unconfirmed, so a generous set of likely OCR misreads (^, v, V,
    /// Λ, and the real ▲/▼ glyphs) is accepted rather than just the exact Unicode arrows.</summary>
    async Task SampleFieldPositionArrowFromWindow(Bitmap fullBmp, int winW, int winH, OcrEngine engine)
    {
        if (_activePreset.Name != ScorebugPreset.CollegeFootball27.Name) { ArrowUp = null; return; }
        if (_activePreset.AwayUnderlineFxW <= 0 || _activePreset.HomeUnderlineFxW <= 0) return;

        string awayText = await OcrCropAsync(fullBmp, winW, winH, engine,
            _activePreset.AwayUnderlineFxX, _activePreset.AwayUnderlineFxY,
            _activePreset.AwayUnderlineFxW, _activePreset.AwayUnderlineFxH);
        string homeText = await OcrCropAsync(fullBmp, winW, winH, engine,
            _activePreset.HomeUnderlineFxX, _activePreset.HomeUnderlineFxY,
            _activePreset.HomeUnderlineFxW, _activePreset.HomeUnderlineFxH);

        bool? awayArrow = ParseFieldPositionArrow(awayText);
        bool? homeArrow = ParseFieldPositionArrow(homeText);
        bool? read = awayArrow ?? homeArrow;
        if (read == null) return; // ambiguous/blank frame -- keep the last known value, same "don't guess" philosophy as possession

        if (read != ArrowUp) Log?.Invoke($"[field-position] arrow now: {(read.Value ? "up" : "down")} (away-slot=\"{awayText.Trim()}\" home-slot=\"{homeText.Trim()}\")");
        ArrowUp = read;
    }

    static bool? ParseFieldPositionArrow(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.IndexOfAny(new[] { '▲', '^', 'Λ', 'ᐱ' }) >= 0) return true;
        if (text.IndexOfAny(new[] { '▼', 'v', 'V', 'ᐯ' }) >= 0) return false;
        return null;
    }

    static async Task<string> OcrCropAsync(Bitmap fullBmp, int winW, int winH, OcrEngine engine,
        double fxX, double fxY, double fxW, double fxH)
    {
        var (cropX, cropY, cropW, cropH) = ClampCropRect(winW, winH, fxX, fxY, fxW, fxH);

        using var bmp = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(fullBmp, new Rectangle(0, 0, cropW, cropH),
                new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
        }
        return await OcrBitmapAsync(engine, bmp);
    }

    /// <summary>Requires the SAME side to be sampled on three consecutive ticks before returning
    /// true -- see _pendingPossession's own doc comment for the live bug this closes. A stray
    /// frame that disagrees with the currently-pending value resets the confirmation instead of
    /// committing, same "same value N times in a row" rule CommitValueIfConfirmed already applies
    /// to down/score/quarter.
    /// RAISED 2-&gt;3 consecutive ticks 2026-08-12 (owner report, live game: "Away" credited for a
    /// TFL and an earned first down that were both actually Home's) -- the checkbox for which
    /// side the user's team is drawn on was confirmed correct both times, so this wasn't the
    /// UserTeamOnLeftSide inversion; it matches this method's own doc comment on the 2-tick
    /// version exactly ("a phantom flip committed off 2 bad-but-agreeing ticks... locks
    /// _lastPossession wrong... TFL/Fourth Down routed to the wrong side"). Two bad-but-agreeing
    /// frames (replay overlay, camera cut, brief graphic over the underline crop) are apparently
    /// still common enough to slip through; a third agreeing tick is much less likely by chance,
    /// at the cost of ~250ms more latency on every real flip too.</summary>
    bool ConfirmPossessionFlip(string side)
    {
        if (side == _lastPossession) { _pendingPossession = null; _pendingPossessionTicks = 0; return false; }
        if (side == _pendingPossession)
        {
            _pendingPossessionTicks++;
            return _pendingPossessionTicks >= 2; // 2 here = 3rd agreeing tick overall (this call is the 2nd match after the 1st assignment below)
        }
        _pendingPossession = side;
        _pendingPossessionTicks = 0;
        return false;
    }

    /// <summary>See _frameIsFrozen's doc comment. Cheap coarse hash (a fixed sparse grid of pixel
    /// samples, not every pixel -- this runs every 250ms tick) of the WHOLE captured frame, so it
    /// catches a paused/menu screen no matter where on screen it differs from the real HUD.
    /// Updates _frameIsFrozen based on how many consecutive ticks the hash hasn't changed.</summary>
    void UpdateFrozenFrameState(Bitmap fullBmp, int winW, int winH)
    {
        const int GridCols = 24, GridRows = 14;
        unchecked
        {
            int hash = 17;
            for (int gy = 0; gy < GridRows; gy++)
            {
                int y = Math.Min(winH - 1, gy * winH / GridRows);
                for (int gx = 0; gx < GridCols; gx++)
                {
                    int x = Math.Min(winW - 1, gx * winW / GridCols);
                    hash = hash * 31 + fullBmp.GetPixel(x, y).ToArgb();
                }
            }

            if (hash == _frozenFrameHash)
            {
                _frozenFrameStreak++;
            }
            else
            {
                _frozenFrameHash = hash;
                _frozenFrameStreak = 0;
            }
        }

        bool wasFrozen = _frameIsFrozen;
        _frameIsFrozen = _frozenFrameStreak >= FrozenFrameTicksThreshold;
        if (_frameIsFrozen && !wasFrozen)
            Log?.Invoke("[watcher] frame appears frozen (paused/menu screen) -- suspending event detection");
        else if (!_frameIsFrozen && wasFrozen)
            Log?.Invoke("[watcher] frame is moving again -- resuming event detection");
    }

    /// <summary>AUDIT FIX 2026-08-12: shared crop-bounds math, factored out of four near-identical
    /// copies (main capture loop, SampleTimeoutSegments, OcrCropAsync, SampleCropBrightness) that
    /// had each clamped the origin to >= 0 but NOT to inside the bitmap's high side -- a fractional
    /// X/Y at or beyond 1.0 (bad calibration data, or any future preset value that's even slightly
    /// off) could put cropX/cropY at or past winW/winH, handing DrawImage a source rectangle that
    /// starts outside fullBmp's actual bounds on an unusual window size/aspect ratio. Clamp the
    /// origin to the last valid pixel first, then size the crop to whatever room is left.</summary>
    static (int X, int Y, int W, int H) ClampCropRect(int winW, int winH, double fxX, double fxY, double fxW, double fxH)
    {
        int cropX = Math.Clamp((int)(winW * fxX), 0, Math.Max(0, winW - 1));
        int cropY = Math.Clamp((int)(winH * fxY), 0, Math.Max(0, winH - 1));
        int cropW = Math.Max(1, Math.Min((int)(winW * fxW), winW - cropX));
        int cropH = Math.Max(1, Math.Min((int)(winH * fxH), winH - cropY));
        return (cropX, cropY, cropW, cropH);
    }

    static double SampleCropBrightness(Bitmap fullBmp, int winW, int winH, double fxX, double fxY, double fxW, double fxH)
    {
        var (cropX, cropY, cropW, cropH) = ClampCropRect(winW, winH, fxX, fxY, fxW, fxH);

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

    /// <summary>Returns true when the sampled color confidently matched a team (even if the flip
    /// itself was suppressed by cooldown/confirmation-pending below) -- callers use this to decide
    /// whether to fall back to a secondary possession signal for this tick, distinct from whether
    /// a flip actually committed.</summary>
    bool SamplePossession(Bitmap bmp)
    {
        if (ResolveTeamColor == null) return false;

        long r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < bmp.Height; y += 2)
        for (int x = 0; x < bmp.Width; x += 2)
        {
            var px = bmp.GetPixel(x, y);
            r += px.R; g += px.G; b += px.B;
            n++;
        }
        if (n == 0) return false;
        var avg = Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));

        string? side = ResolveTeamColor(avg);
        if (side == null) return false;

        // FIXED: same desync as SamplePossessionByUnderline -- see its comment. _lastPossession
        // must only update together with the PossessionChanged event, or the snapshot
        // (PossessionAway) and WebMainForm's routing side (_possession) drift apart during the
        // cooldown window. Also requires 2-consecutive-tick confirmation via ConfirmPossessionFlip
        // -- see _pendingPossession's doc comment.
        if (ConfirmPossessionFlip(side))
        {
            if (DateTime.UtcNow < _possessionCooldownUntil)
            {
                Log?.Invoke($"[possession] suppressed re-fire of \"{side}\" (cooldown)");
            }
            else
            {
                _pendingPossession = null;
                _lastPossession = side;
                _possessionCooldownUntil = DateTime.UtcNow + Cooldown;
                Log?.Invoke($"[possession] now: {side}");
                PossessionChanged?.Invoke(side);
            }
        }
        return true;
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

    /// <summary>Requires <paramref name="currentValue"/> to be read on two consecutive ticks
    /// before promoting it from <paramref name="pending"/> to <paramref name="committed"/>,
    /// filtering out a single bad OCR frame (see the _pendingAwayScore/_pendingHomeScore field
    /// comment for the live bug this fixes).
    ///
    /// FIXED same session: the original version discarded an unconfirmed `pending` outright
    /// whenever a THIRD distinct value showed up before it confirmed -- fine for a single bad
    /// misread reverting back to `committed`, but it also silently ate a real fast second score
    /// (e.g. a touchdown's new total read once, then a quick 2-point conversion's total read
    /// before the touchdown's total got its confirming second tick). The engine would then see
    /// one big delta jump straight from the old score to the newest one, which doesn't match any
    /// evaluator's expected delta, so BOTH scoring cues could go silent. Now the outgoing pending
    /// value is committed once (unconfirmed) before starting a fresh confirmation cycle on the
    /// newest read, so a real back-to-back score still produces two deltas instead of one
    /// unrecognizable one -- at the cost of a narrower residual risk (two different bad misreads
    /// landing on consecutive ticks, with no reversion in between, could still commit garbage).</summary>
    static void CommitValueIfConfirmed(string currentValue, ref string? pending, ref string? committed)
    {
        if (currentValue == committed) { pending = null; return; }

        if (currentValue == pending)
        {
            committed = currentValue;
            pending = null;
        }
        else
        {
            if (pending != null) committed = pending;
            pending = currentValue;
        }
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
        // Read the guard's value as it stood entering this tick BEFORE updating it -- the tick
        // where `down` first resolves to a real 1-4 value after a return is exactly the tick a
        // late-arriving possession-flip sample is most likely to land on, so the guard must still
        // be in effect for THIS tick's structuralTurnover check. Clearing it first (so the very
        // tick meant to be protected reads the guard as already off) would silently reopen the
        // same live bug this flag exists to close. Disarm for the NEXT tick only, after use.
        // FIXED 2026-08-11 (live bug: "Turnover Forced" fired mid-drive on an ordinary 2nd & long,
        // no real turnover, possession never actually changed): this backstop never actually
        // verified the one fact every real turnover guarantees -- the NEW offense always starts
        // at 1st down. A real interception/fumble/turnover-on-downs resets Down to 1; an ordinary
        // down progression (1st -> 2nd, 2nd -> 3rd, etc.) never does. Without this check, a
        // possession misread that's wrong for 2+ CONSECUTIVE ticks (a sustained bad read, not the
        // single-frame flicker ConfirmPossessionFlip's debounce already guards against -- e.g. a
        // brief graphic/camera transition fooling the underline-brightness sample for more than
        // one frame) could still fire a phantom turnover with zero corroborating evidence from the
        // down/distance ribbon at all. Requiring `down == 1` doesn't replace the debounce (still
        // needed to avoid a delayed/wrong PossessionChanged event even when this guard blocks the
        // phantom event itself) -- it's an independent, stronger check: even a fully-confirmed
        // possession flip shouldn't be trusted as a turnover unless the down ribbon agrees.
        // FIXED 2026-08-11 (owner's call, after 4 distinct false-positive reports in one live
        // session): this is an INFERENCE from indirect signals (possession + down), not direct
        // evidence -- unlike `situation == "turnover"` above, which reads the HUD's actual
        // INTERCEPTED/FUMBLE/TURNOVER text and fires instantly, unaffected by any of this. Every
        // false positive fixed today came from the inferred conditions below aligning for exactly
        // ONE tick. Now requires the full condition (possession flipped, down==1, all existing
        // guards) to hold on 2 CONSECUTIVE ticks before firing -- ~250ms of added latency, but only
        // on the inferred path. A real turnover with matching HUD text is completely unaffected
        // (still fires the same tick via the OCR-text OR-branch below); this only slows down the
        // fallback path for when that text isn't calibrated/missed, trading a little speed for a
        // real reduction in inferring a turnover from noisy indirect signals that happened to
        // agree for a single frame.
        bool possessionFlipped = _lastPossession != null && _snapshotPrevious.PossessionAway != (_lastPossession == "away");
        bool structuralTurnoverCandidate = possessionFlipped
            && down == 1
            && _snapshotPrevious.Down != 4 && _snapshotPrevious.Down != 0
            && situation != "kickoff" && !_snapshotPrevious.IsKickoff && !_awaitingPostKickoffSnap;
        _structuralTurnoverPendingTicks = structuralTurnoverCandidate ? _structuralTurnoverPendingTicks + 1 : 0;
        bool structuralTurnover = _structuralTurnoverPendingTicks >= 2;

        if (situation == "kickoff")
            _awaitingPostKickoffSnap = true;
        // FIXED 2026-08-11 (audit finding, downstream of the possession-debounce fix above):
        // clearing this purely off `down` resolving used to be safe because possession used to
        // commit off a single frame, so a kickoff-return flip essentially always landed at or
        // before the tick down first resolved. Now that possession requires 2-tick confirmation
        // (ConfirmPossessionFlip), down and possession are two independently-timed confirm gates
        // with no guaranteed resolution order -- if down resolves to a real value WHILE a
        // possession flip is still mid-confirmation (_pendingPossession != null), clearing the
        // guard here would drop it a tick or two before the flip actually commits, reopening the
        // exact false "Turnover Forced" bug this flag exists to prevent, just triggered by
        // kickoff returns instead of 3rd-and-long. Requiring _pendingPossession == null keeps the
        // guard armed until any in-flight possession confirmation has resolved one way or the
        // other (commits, or reverts back to matching _lastPossession and clears itself).
        else if (_awaitingPostKickoffSnap && down is >= 1 and <= 4 && _pendingPossession == null)
            _awaitingPostKickoffSnap = false;

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
            IsTimeout = situation == "time_out",
            IsPenaltyOnOffense = isPenaltyOnOffense,
            IsPenaltyOnDefense = isPenaltyOnDefense,
            // Not sticky like _lastKnownDown/_lastKnownAwayScore/etc: the READY screen is a
            // one-shot pregame overlay, not a value that legitimately needs to survive a blank
            // OCR tick, so PregameHelper's edge-trigger (Previous.IsPregameReady == false &&
            // Current.IsPregameReady == true) reading straight off region.Last is correct here.
            IsPregameReady = pregameReadyRegion?.Last == "ready",
            // Not sticky, same reasoning as IsPregameReady just above: a one-shot pregame signal,
            // not a value needing to survive a blank sampling tick.
            IsPregameEntranceMarker = _lastPregameEntranceMarker,
            IsFieldGoalAttempt = bannerRegion?.Last == "fieldgoal",
            YardLine = 0,
            HomeScore = homeScore,
            AwayScore = awayScore,
            TimeRemainingSeconds = timeRemainingSeconds,
            AwayTimeoutsRemaining = _lastAwayTimeoutsRemaining,
            HomeTimeoutsRemaining = _lastHomeTimeoutsRemaining,
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
        // EventKey on this tick and only the first (by fixed evaluator order) is kept (see
        // EventRouter.Dedupe's own comment) -- logged here in plain English for the user-facing
        // Event Log rather than inside Bandroom.Core, which has no UI-facing logging of its own.
        // 2026-08-11 audit item #3: now includes WHICH evaluator's event was kept vs dropped
        // (provenance), not just that a duplicate was dropped.
        var results = _eventRouter.Route(state, (dupe, droppedBy, keptBy) =>
            EventActivityLog.Record(dupe.EventKey, "n/a", $"{EventActivityLog.FriendlyEventName(dupe.EventKey)} -- skipped: duplicate of an event we just fired this instant ({droppedBy} was dropped in favor of {keptBy})"));

        // 2026-08-11 audit item #5 ("almost fired" ghost log): buffered evaluators (DownDistance-
        // Buffer users) append a note here when their confirmation window times out without the
        // change they were waiting for. Logged as a distinct near-miss entry rather than silently
        // doing nothing.
        foreach (var nearMiss in state.NearMisses)
            EventActivityLog.Record("n/a", "n/a", $"(near miss) {nearMiss}");

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
            new DefenseSecondDownShortHelper(),
            new DefenseThirdDownHelper(),
            new DefenseThirdDownShortHelper(),
            new DownFieldPositionHelper(),
            new DriveStarterHelper(),
            new FieldGoalMissedHelper(),
            new FieldGoalPATHelper(),
            new FirstDownHelper(),
            new GameStateEventHelper(),
            new KickoffHelper(),
            new OffenseAfterOpeningKickHelper(),
            new OffenseDownHelper(),
            new OffenseFourthDownHelper(),
            new PenaltyHelper(),
            new PregameHelper(),
            new SafetyHelper(),
            new ThirdDownConversionHelper(),
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
