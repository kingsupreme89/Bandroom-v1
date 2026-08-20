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
                // Vertical-stack presets (e.g. Espn2013) override Y/H since away/home aren't on
                // the same horizontal strip there -- see ScorebugPreset.AwayScoreFxY's doc comment.
                region.FxY = preset.AwayScoreFxY ?? preset.BandFxY;
                region.FxH = preset.AwayScoreFxH ?? preset.BandFxH;
            }
            else if (region.Name == "homescore")
            {
                region.FxX = preset.HomeScoreFxX; region.FxW = preset.HomeScoreFxW;
                region.FxY = preset.HomeScoreFxY ?? preset.BandFxY;
                region.FxH = preset.HomeScoreFxH ?? preset.BandFxH;
            }
            else if (region.Name == "clock")
            {
                region.FxX = preset.ClockFxX; region.FxW = preset.ClockFxW;
                region.FxY = preset.ClockFxY ?? preset.BandFxY;
                region.FxH = preset.ClockFxH ?? preset.BandFxH;
            }
            // FIXED 2026-08-14: playclock's crop used to be hardcoded (never reached this loop at
            // all) -- see ScorebugPreset.PlayClockFx*'s doc comment for why that silently broke
            // play clock on every preset except CollegeFootball27.
            else if (region.Name == "playclock")
            {
                region.FxX = preset.PlayClockFxX; region.FxY = preset.PlayClockFxY;
                region.FxW = preset.PlayClockFxW; region.FxH = preset.PlayClockFxH;
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

    /// <summary>Read-only peek at the most recent tick's PlaySnapshot -- for the Reader Hub status
    /// panel (WebMainForm.GetScoreboardSourceStatusFromWeb) only. PlaySnapshot is init-only/
    /// immutable, so exposing the live reference is safe; nothing outside RouteEngineTick ever
    /// mutates it.</summary>
    public PlaySnapshot CurrentSnapshot => _snapshotCurrent;

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

    // --- Scoreboard reader integration (2026-08-13) ---
    // Coffee's "Scorebug Overlay App" writes an atomic `live-scoreboard.json` with exact scores/
    // possession/yard line/timeouts, read here as a second, PREFERRED snapshot source -- when
    // it's CONNECTED, its normalized fields replace the OCR/pixel guesses for score/possession/
    // yard line/down/distance/timeouts in RouteEngineTick's snapshot; when it's stale/disconnected,
    // the existing OCR path is used unchanged, exactly as before this feature existed. Event flags
    // (touchdown/kickoff/etc) stay OCR-owned always -- see PlaySnapshot's own IsTouchdown/etc
    // fields, none of which the reader ever supplies (A5 of the integration plan: reader
    // score-change events only corroborate/log, never fire an event flag on their own).
    readonly ScoreboardJsonReader _scoreboardReader = new();
    readonly GameStateNormalizer _scoreboardNormalizer = new();
    string? _scoreboardJsonPath;
    /// <summary>Exposed for WebBridge.GetScoreboardSourceStatus -- lets app.js show a live
    /// CONNECTED/WAITING FOR GAME DATA/NOT FOUND/ERROR chip without polling the file itself.</summary>
    public ScoreboardReaderStatus ScoreboardStatus { get; private set; } = ScoreboardReaderStatus.NotFound;
    ScoreboardReaderState? _lastScoreboardReaderState;

    // --- Bundled RAM reader (2026-08-13 owner-approved absorption) ---
    // Direct process-memory read of the running CollegeFB27(.exe), validated by
    // Bandroom.Core.RamReaderValidator BEFORE anything here trusts a single field out of it (see
    // that class's own doc comment for the exact port of Coffee's status/PID/freshness/per-field
    // provenance checks). Takes priority over the screen-JSON reader above when CONNECTED (more
    // authoritative -- memory, not OCR/JSON-file guessing); both still fall back to plain OCR when
    // neither reader is usable. ON by default (2026-08-19) -- see
    // ConfigStore.LoadScoreboardReaderRamModeEnabled.
    readonly GameStateNormalizer _ramNormalizer = new();
    string? _ramLiveDataPath;
    bool _ramModeEnabled;

    /// <summary>Owner request 2026-08-19 (live game, RAM reader silently degraded to OCR-only for
    /// an extended stretch with no visible recovery): the reader is meant to run for the entire
    /// session, but nothing previously noticed or acted on it going dark mid-game -- a crashed
    /// child process, or the reader's own live-game-data.json simply going stale, looked
    /// identical to "still working, just OCR happens to be primary right now" from here. Set by
    /// WebMainForm right after constructing this watcher; invoked when RamReaderStatus has been
    /// anything other than Connected for RamReaderRestartThreshold consecutive ticks while RAM
    /// mode is enabled, so the host can kill and relaunch its child process. Nullable/no-op by
    /// design (same "must never block GAMETIME" contract as ScoreboardReaderHost itself) --
    /// Explore/Mac builds that never wire this up just never get the auto-restart, not a crash.</summary>
    public Action? RestartRamReader { get; set; }
    int _ramDisconnectedTicks;
    DateTime _lastRamRestartAttemptUtc = DateTime.MinValue;
    // ~250ms poll interval (see RunAsync's Task.Delay(250)) -- 40 ticks is ~10s of the reader
    // reporting anything other than Connected before we conclude it's actually stuck, not just
    // mid-reconnect (Coffee's own reader retries self-attach for up to ~30s on a cold start, so
    // this must stay well short of double-restarting into that same cold-start window forever).
    const int RamReaderRestartThreshold = 40;
    // Floor between restart attempts -- a restart itself needs the ~30s cold-start window to
    // either succeed or fail before another one is worth trying; without this floor a reader
    // that keeps failing to re-attach would get killed and relaunched every ~10s forever.
    static readonly TimeSpan RamRestartCooldown = TimeSpan.FromSeconds(35);

    // FIXED 2026-08-19 (live bug: RAM mode consistently credited the wrong side all game --
    // "away" for plays that were actually home's, every single time, not a random flicker).
    // Root cause: the bundled reader auto-discovers its own memory offsets fresh each session
    // ("automatic-read-only-signatures-v9-special-downs" profile) rather than using fixed,
    // verified offsets, and it never resolved real team names this session (awayTeamName/
    // homeTeamName both stayed "missing") -- so its raw possession bit is just whichever memory
    // slot it happened to lock onto, with zero guarantee that slot lines up with BANDroom's own
    // home/away selection. It can come out backwards, and previously nothing ever checked.
    // Self-corrects once: the first time BOTH the reader's possession AND OCR's own independently
    // color-sampled possession (_lastPossession) are confirmed on the same tick, if they disagree,
    // assume the reader is inverted for the rest of this session and flip every reader possession
    // read from then on. OCR is trusted as the tie-breaker here specifically because it's sampled
    // directly against BANDroom's own configured home/away team colors, so it can't have this same
    // class of "which physical team is which" ambiguity the reader has.
    // RE-BUILT 2026-08-19 (owner report, live game: "Defense: 1st Down Allowed (Home)" fired for
    // a snap that was actually Home's OFFENSE on the field right after a kickoff -- the watchdog
    // line for that same tick showed away score RAM=7/OCR=0, home score RAM=0/OCR=6, i.e. RAM's
    // away/home score labels swapped almost exactly against OCR's). The original version of this
    // check (see git history) used OCR's own color-sampled possession as the tie-breaker -- ripped
    // out earlier tonight after a DIFFERENT game proved OCR's possession color-sampling itself
    // unreliable for a whole session. Score is a different, more trustworthy signal for this
    // specific question (digit OCR, not a color threshold), so it's safe to use here without
    // reopening that possession-specific bug. Self-corrects ONCE per game: the first time OCR's
    // score has settled and at least one side has scored, compares RAM's away/home scores against
    // OCR's both directly and swapped -- if the swapped pairing matches and the direct pairing
    // doesn't, RAM's home/away is backwards for this game. Locked in for the rest of the session
    // once confirmed (never re-evaluated, never flips back) so a single fluky tick can't decide it
    // and it can't oscillate mid-game the way the old possession-based version did.
    bool _ramOrientationChecked;
    bool _ramOrientationSwapped;
    const int RamOrientationConfirmTicks = 3;
    bool? _ramOrientationStreakValue;
    int _ramOrientationStreak;
    // FIXED 2026-08-19 (owner report, live game: a real earned 1st down within an ongoing drive
    // triggered a false "Defense: After Punt" ~2s later): PlaySnapshot.PossessionAway used to take
    // RAM's raw per-tick bit completely unsmoothed the instant it resolved (the very fix earlier
    // tonight that stopped OCR from corrupting a CORRECT RAM read also removed the only debounce
    // that happened to sit on this path) -- PlayDelta.NewPossession is a bare Previous!=Current
    // comparison with zero debounce of its own, so a single stray RAM tick (RAM's own internal
    // "two agreeing reads" guard isn't a hard guarantee against every possible glitch) reads as a
    // real turnover/punt to every NewPossession-gated helper. Mirrors ConfirmPossessionFlip's
    // existing 2-consecutive-tick confirmation, but purely RAM-vs-RAM (own value against its own
    // previous tick) -- NOT RAM-vs-OCR, so this can't reintroduce tonight's earlier bug where OCR's
    // own bad possession read corrupted a correct RAM one.
    bool? _lastConfirmedRamPossessionAway;
    bool? _pendingRamPossessionAway;
    int _pendingRamPossessionTicks;

    // 2026-08-19 (live game, Session 96+ follow-up): RAM's raw down/distance were observed
    // genuinely NOISY this session, not simply frozen -- e.g. distance ticking 9 -> 7 -> 9 rather
    // than holding one wrong value. IsFieldStableFor's "unchanged for RamFieldStaleThreshold"
    // window never closes on a noisy-but-moving value, so the OCR-fallback override below kept
    // engaging and disengaging tick to tick, and the FINAL down/yardsToGo fed into PlaySnapshot
    // flapped between RAM's raw value and OCR's fallback value every ~250ms tick. Down-edge
    // evaluators (DefenseThirdDownHelper etc.) fire on any Current.Down != Previous.Down, so each
    // flap re-triggered the same card repeatedly for one physical snap (owner report: "Defense:
    // 3rd & Long" firing 3x in 5s). Same 2-consecutive-agreeing-tick confirm-streak already used
    // above for RAM possession, applied here to the FINAL committed down/yardsToGo (after the
    // stale-fallback block below has already picked RAM vs. OCR for the tick) so a value can't
    // reach evaluators until it's held for 2 ticks running, regardless of which source produced it.
    const int FinalDownDistanceConfirmTicks = 1; // this many ADDITIONAL agreeing ticks after the first
    int? _lastConfirmedFinalDown;
    int? _pendingFinalDown;
    int _pendingFinalDownTicks;
    int? _lastConfirmedFinalYardsToGo;
    int? _pendingFinalYardsToGo;
    int _pendingFinalYardsToGoTicks;

    /// <summary>Debounces a per-tick candidate value: commits immediately the first time (nothing
    /// to debounce against yet), then requires the SAME new value to repeat for
    /// FinalDownDistanceConfirmTicks additional ticks before replacing the last confirmed value.
    /// A candidate that flaps between two values every tick never accumulates enough agreeing
    /// ticks to commit, so the confirmed value holds steady through the flapping.</summary>
    static void ConfirmFinalValue(int candidate, ref int? lastConfirmed, ref int? pending, ref int pendingTicks)
    {
        if (lastConfirmed is not { } confirmed)
        {
            lastConfirmed = candidate;
            return;
        }
        if (candidate == confirmed)
        {
            pending = null;
            pendingTicks = 0;
            return;
        }
        if (pending == candidate)
        {
            pendingTicks++;
            if (pendingTicks >= FinalDownDistanceConfirmTicks)
            {
                lastConfirmed = candidate;
                pending = null;
                pendingTicks = 0;
            }
        }
        else
        {
            pending = candidate;
            pendingTicks = 0;
        }
    }

    // 2026-08-19 (live game, follow-up to the down/distance flap guard above): same bug, one
    // level up. RAM's readerPossessionAway and OCR's _lastPossession are EACH independently
    // debounced (2-tick confirm-streaks of their own), but the FINAL possession value fed into
    // PlaySnapshot -- `readerPossessionAway ?? (_lastPossession == "away")` -- switches which of
    // those two already-smoothed sources it reads from every single tick, based on whether
    // HavePossession happened to resolve THIS tick. When HavePossession itself flickers true/false
    // (RAM's possession locator intermittently failing, confirmed live: same session, same "field
    // resolved once then went noisy" failure class as down/distance), the final value bounces
    // between RAM's confirmed answer and OCR's confirmed answer with no debounce on THAT switch --
    // owner report: "After Punt (Home)"/"1st Down (Away)" firing repeatedly every ~20s with no real
    // punt in between, each bounce read as a fresh turnover by the structural-turnover/first-down
    // helpers. Same ConfirmFinalValue treatment, applied to the combined final bool.
    bool? _lastConfirmedFinalPossessionAway;
    bool? _pendingFinalPossessionAway;
    int _pendingFinalPossessionTicks;

    static void ConfirmFinalPossession(bool candidate, ref bool? lastConfirmed, ref bool? pending, ref int pendingTicks)
    {
        if (lastConfirmed is not { } confirmed)
        {
            lastConfirmed = candidate;
            return;
        }
        if (candidate == confirmed)
        {
            pending = null;
            pendingTicks = 0;
            return;
        }
        if (pending == candidate)
        {
            pendingTicks++;
            if (pendingTicks >= FinalDownDistanceConfirmTicks)
            {
                lastConfirmed = candidate;
                pending = null;
                pendingTicks = 0;
            }
        }
        else
        {
            pending = candidate;
            pendingTicks = 0;
        }
    }

    /// <summary>Exposed for WebBridge.GetScoreboardSourceStatus, same reasoning as ScoreboardStatus
    /// above.</summary>
    public ScoreboardReaderStatus RamReaderStatus { get; private set; } = ScoreboardReaderStatus.NotFound;

    static readonly string[] RamGameProcessNames = { "CollegeFB27", "CollegeFB27_Trial" };

    // FIXED 2026-08-14 (real live bug, owner-confirmed): RAM reported down=1/distance=0/
    // possession=home unchanged for minutes while OCR correctly tracked the game moving to 2nd &
    // 7 with Away in possession. RAM was still "connected" -- fresh updatedAt, passing
    // RamReaderValidator's document-level 20s freshness check -- but this game's down/distance/
    // possession locators had silently broken and kept re-reporting their last good value
    // forever. The 2026-08-14 -1/HavePossession sentinel fixes above (see the two RELIABILITY
    // FIX comments in RouteEngineTick) only protect against a field that NEVER resolved; they
    // can't tell "resolved once, correctly, then got stuck" apart from "still correct, unchanged
    // because the play genuinely hasn't advanced."
    //
    // A pure "RAM unchanged for N seconds" check is NOT safe on its own -- a real down/distance
    // legitimately holds the same value for 20-40s between plays while the play clock runs, so
    // that alone would misfire on a perfectly healthy RAM reader constantly. This requires BOTH
    // sides to agree something's actually wrong: RAM frozen on one value past RamFieldStaleThreshold
    // AND OCR independently SETTLED on a different value for at least OcrFieldCorroborationWindow
    // (not just a single noisy misread -- one stray OCR digit glitch resets its own clock the
    // instant the value changes again, so a flickering/inconsistent OCR read can never trigger
    // this). Only once both are true does RouteEngineTick fall back to OCR for that one field,
    // same as if RAM had never resolved it. Self-heals the instant RAM's value changes again,
    // whether because its locator recovered or the game really did reach that down/distance/
    // possession.
    static readonly TimeSpan RamFieldStaleThreshold = TimeSpan.FromSeconds(5);
    static readonly TimeSpan OcrFieldCorroborationWindow = TimeSpan.FromSeconds(1.5);
    // See the "RAM-derived play-clock-counting signal" comment at its one call site -- a real
    // countdown ticks about once a second, so a playClock value unchanged for longer than this is
    // treated as frozen (dead ball / mid-play), not actively counting.
    static readonly TimeSpan PlayClockCountingRecencyWindow = TimeSpan.FromSeconds(1.5);
    // 2026-08-14: the reader's v1.4.9+ "ram.freshness" block is ground truth for "is the core
    // memory block (quarter/clocks/scores/down/distance/timeouts) still being live-verified right
    // now" -- straight from re-checking game memory, not our own guess from comparing against OCR.
    // Per the reader's own DATA-API.md ("Staleness: how to actually detect it"): treat either
    // clock (game clock or play clock) showing a recent change as proof the WHOLE block is live,
    // even if some other field in it (e.g. score) is legitimately unchanged for minutes. Only when
    // BOTH clocks have gone quiet past a normal dead-ball stretch is the block worth suspecting --
    // 20s here matches RamReaderValidator's own MaxDocumentAgeMs-adjacent reasoning (long enough to
    // clear a real replay/dead-ball gap, short enough to catch a genuinely broken locator quickly).
    // When this is true, the existing OCR-corroboration fallback below is trusted exactly as
    // before; when the reader predates v1.4.9 (Freshness null), that fallback runs unconditionally,
    // same as before this check existed.
    static readonly TimeSpan CoreBlockFreshnessWindow = TimeSpan.FromSeconds(20);
    int? _ramDownStableValue; DateTime? _ramDownStableSince;
    int? _ramYardsToGoStableValue; DateTime? _ramYardsToGoStableSince;
    bool? _ramPossessionStableValue; DateTime? _ramPossessionStableSince;
    int? _ocrDownStableValue; DateTime? _ocrDownStableSince;
    int? _ocrYardsToGoStableValue; DateTime? _ocrYardsToGoStableSince;
    bool? _ocrPossessionStableValue; DateTime? _ocrPossessionStableSince;

    // 2026-08-15 addition: score was deliberately left out of the original stale-RAM-field
    // fallback ("score/timeouts/yard line already have their own -1-sentinel never-resolved
    // protection") -- that protection only covers a reader that never resolves a field at all, not
    // one that resolves a field to a persistently WRONG value while everything else in the block
    // keeps updating normally (confirmed live: RAM home score stuck at a stale value for 11+
    // minutes straight while OCR settled on a different value the whole time). That mismatch let a
    // single reader-connect/disconnect blip on any tick flip the snapshot's score source between
    // OCR and RAM, which TouchdownHelper read as a real multi-point swing -- a phantom "Defense:
    // Touchdown Scored" fired off nothing but two disagreeing sources handing off mid-tick. Same
    // double-corroboration guard as down/distance/possession: only overrides once RAM's own score
    // has sat unchanged for RamFieldStaleThreshold AND OCR has independently settled on a
    // persistently different value for OcrFieldCorroborationWindow.
    int? _ramHomeScoreStableValue; DateTime? _ramHomeScoreStableSince;
    int? _ramAwayScoreStableValue; DateTime? _ramAwayScoreStableSince;
    int? _ocrHomeScoreStableValue; DateTime? _ocrHomeScoreStableSince;
    int? _ocrAwayScoreStableValue; DateTime? _ocrAwayScoreStableSince;
    string? _lastResolvedPossessionReported;

    /// <summary>True once <paramref name="value"/> has been unchanged for at least
    /// <paramref name="threshold"/>. Updates the tracking state as a side effect -- resets the
    /// clock the instant the value changes, whether that's a real play advancing (RAM side) or a
    /// one-off misread settling back down (OCR side).</summary>
    static bool IsFieldStableFor<T>(T value, ref T? stableValue, ref DateTime? stableSince, DateTime now, TimeSpan threshold) where T : struct, IEquatable<T>
    {
        if (stableValue is not { } prev || !prev.Equals(value))
        {
            stableValue = value;
            stableSince = now;
            return false;
        }
        return stableSince.HasValue && now - stableSince.Value >= threshold;
    }

    /// <summary>Best-effort PID lookup for RamReaderValidator's expectedGameProcessId check --
    /// process names confirmed from Coffee's own main.js `exactProcessNames` default
    /// (['CollegeFB27.exe', 'CollegeFB27_Trial.exe']; Process.GetProcessesByName wants the name
    /// without ".exe"). Returns null (not a failure) when the game process isn't found yet --
    /// RamReaderValidator treats a null expected PID as "accept whatever the document claims,"
    /// same as Coffee's own runtime.game.pid being unset early in acquisition.</summary>
    static int? FindRamGameProcessId()
    {
        foreach (string name in RamGameProcessNames)
        {
            var proc = System.Diagnostics.Process.GetProcessesByName(name).FirstOrDefault();
            if (proc != null) return proc.Id;
        }
        return null;
    }

    // FIXED 2026-08-11 (found from live screenshots, not caught by the code-only audit earlier
    // this session): "3rd & inches" and "1st & Goal" both render with no digit at all -- the
    // original digit-only pattern simply never matched either, silently leaving YardsToGo frozen
    // on whatever the PREVIOUS down's distance happened to be instead of updating it, which could
    // misclassify a genuinely short down as long (or vice versa) downstream. "inches" is always
    // under a yard (unambiguously short); "Goal" is owner's explicit call (2026-08-11) to also
    // treat as short for the hype logic, even though the real yard-to-go varies -- both now
    // normalize to "1" via NormalizeDistanceRaw below instead of leaving the field stale.
    // Widened alongside the down-region ordinal lookahead (2026-08-14) -- CFB26's stylized "&"
    // glyph also gets misread here, and this pattern extracts the actual distance value, not just
    // detects the ordinal, so it needs the same [&a8] connector class to keep working on the fix.
    static readonly Regex DistancePattern = new(@"[&a8]\s*(-?\d+|inches|goal)", RegexOptions.IgnoreCase);

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
    // SAFETY VALVE added 2026-08-12 (live bug: Pregame Ready fired once, then zero events for the
    // rest of the game): the freeze detector is supposed to resume "the instant the frame starts
    // changing again," but the 2-tick (~0.5s) threshold above is aggressive enough that a real,
    // legitimately-static broadcast moment (a held stats/replay overlay, a post-play freeze-frame,
    // or the sparse 24x14 sample grid unluckily landing on unchanging pixels for a few ticks) can
    // trip it, and if the hash then stays flat for any reason detection never comes back for the
    // rest of the session -- there was previously no upper bound on how long "frozen" could last.
    // No real gameplay legitimately holds pixel-identical for this long, so force an unfreeze (and
    // log loudly) if the streak ever exceeds this, regardless of why it got stuck.
    static readonly TimeSpan MaxFrozenDuration = TimeSpan.FromSeconds(10);
    DateTime? _frozenSince;
    int _frozenFrameHash;
    int _frozenFrameStreak;
    bool _frameIsFrozen;
    // Average brightness (0-255) of the same sparse grid UpdateFrozenFrameState already samples --
    // computed as a byproduct of that method (no extra GetPixel pass) so BlackScreenRunout below
    // can tell "black loading transition" apart from "static but lit" without its own scan.
    double _lastFrameBrightness = 255;

    // --- Black-screen-timed pregame runout (owner idea, live game 2026-08-12) ---
    // The chevron tunnel-walk marker (IsPregameEntranceMarker) fires too late for the owner's
    // taste, and there's no team-neutral fixed-delay "entrance" moment consistent across every
    // team to time off of instead. What IS consistent, per the owner: a black loading transition
    // screen appears, and the real team runout begins a reliable ~10s after that black screen
    // shows up. Tracked entirely here in GameWatcher (wall-clock time, via DateTime, NOT tick
    // count) rather than as a Bandroom.Core evaluator, specifically so it keeps counting down even
    // while the frozen-frame detector has suspended RouteEngineTick -- a black loading screen is
    // exactly the kind of unchanging frame that trips _frameIsFrozen almost immediately, and if
    // this lived inside the normal evaluator pipeline (which RunAsync skips entirely while frozen,
    // see "if (!_frameIsFrozen) RouteEngineTick();" below) the countdown would never advance.
    // Fires "Other: Pregame Take the Field" directly (bypassing the rule engine) once armed and
    // the delay elapses -- guarded by _pregameTakeFieldFired, a flag shared with the engine's own
    // chevron/quarter-down/kickoff signals for that same EventKey (see RouteEngineTick's dedupe
    // check right after routing results) so whichever signal trips first wins and this can never
    // double-fire the same real-world moment.
    static readonly double BlackScreenBrightnessThreshold = 10; // near-total black, 0-255 scale
    /// <summary>User-adjustable via the Audio Timing settings panel (15-45s range) -- default 15s.
    /// Was a hardcoded 13s constant confirmed live 2026-08-12, but that game's timing doesn't hold
    /// across every matchup/OS load time, so the owner wants it tunable instead of re-hardcoded.</summary>
    static TimeSpan BlackScreenRunoutDelay =>
        TimeSpan.FromSeconds(Math.Clamp(ConfigStore.LoadPlaybackTimingSettings().PregameRunoutDelaySeconds, 15.0, 45.0));
    bool _wasBlackScreen;
    DateTime? _blackScreenSince;
    bool _pregameTakeFieldFired;
    bool _sawPregameReady;

    /// <summary>Called when the owner manually fires "Other: Pregame Take the Field" via the ']'/'['
    /// hotkeys instead of waiting on the automatic READY/black-screen timer -- sets the same one-shot
    /// guard the automatic paths use so the timer (if already armed) can't also fire and double the
    /// song. Safe to call even if the timer never armed at all.</summary>
    public void MarkPregameTakeFieldFiredManually()
    {
        _pregameTakeFieldFired = true;
        _blackScreenSince = null;
    }

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
    //
    // "pregameready" deliberately NOT included (owner call 2026-08-12): unlike situation/banner/
    // quarter, which need pause/unpause protection because a mid-game pause can blank and restore
    // the SAME real event's text, the READY screen is reachable by hitting Back on the team-select
    // screen and re-readying -- the owner explicitly wants that to re-fire "Other: Pregame Ready"
    // every time, not just once per app session. Leaving it OUT of this set means its region.Last
    // resets to null the normal way (see the blank-OCR reset branch below) as soon as "READY"
    // leaves the screen, so the next time it reappears -- whether that's a real new game or a
    // Back-and-re-ready in the same session -- it reads as a fresh value and fires again.
    static readonly HashSet<string> EventGatedRegions = new(StringComparer.OrdinalIgnoreCase) { "situation", "banner", "quarter", "penaltyagainst", "teamrunout" };
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

    // Debounce for the foreground-window re-target below (added 2026-08-15): only retarget once
    // the SAME alternate candidate has been foreground for 2 consecutive ticks, so two windows
    // that are both valid candidates (e.g. a background CollegeFB27 process and a RemotePlay
    // window on the unscoped default preset) fighting for OS focus can't thrash hwnd every tick.
    IntPtr _pendingForegroundCandidate = IntPtr.Zero;
    // FIXED 2026-08-16 (state-machine audit finding #6): the "same candidate twice in a row"
    // debounce above has no escape hatch if the foreground window keeps ALTERNATING between two
    // (or more) different valid candidates every tick -- _pendingForegroundCandidate gets reset to
    // a different value each time and "second consecutive tick" never trips, so capture could
    // silently stall indefinitely with nothing but a repeated debug log line. Tracks how long
    // capture has been stalled on ANY foreground mismatch (regardless of which candidate), reset
    // the moment a real capture actually succeeds; ForegroundStallTimeout below forces a retarget
    // to whatever candidate is currently foreground once exceeded, trading a little flapping for a
    // guarantee that capture eventually resumes instead of hanging forever.
    DateTime? _foregroundStallSince;
    static readonly TimeSpan ForegroundStallTimeout = TimeSpan.FromSeconds(5);

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
            // Lookahead widened 2026-08-14 (live CFB 26/27 Remote Play bug: down/distance NEVER
            // fired) -- confirmed via ocr_debug.log that CFB26's stylized "&" glyph gets OCR'd as
            // "a" ("2nd a 8" instead of "2nd & 8"), so the old literal-"&" lookahead silently
            // never matched. [&a8] covers the confirmed misread plus "8" (another common "&"
            // confusion in condensed broadcast fonts) without loosening enough to also match
            // "quarter"'s own text -- see "quarter" region below, whose negative lookahead was
            // widened to the exact same character class so the two stay mutually exclusive.
            Pattern = new Regex(@"\b(1st|2nd|3rd|4th)\b(?=\s*[&a8]\s*\d)", RegexOptions.IgnoreCase),
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
            // Widened 2026-08-14 alongside "down"'s matching fix above -- must stay the exact same
            // exclusion shape as "down"'s lookahead or an ordinal could match BOTH regions (or
            // neither) depending on which one's regex happens to be looser.
            Pattern = new Regex(@"\b(1st|2nd|3rd|4th)\b(?!\s*[&a8]\s*\d)", RegexOptions.IgnoreCase),
        },
        // Penalty decision overlay -- RECALIBRATED 2026-08-12 (owner report: penalty never fired
        // live; sent 3 real screenshots from a Montana St @ Montana game, an actual "ENCROACHMENT
        // - 5 YDS / Against Montana" overlay). The original 2026-08-07 crop (FxY 0.62-0.84,
        // lower-right) was estimated from a DIFFERENT overlay layout than what CFB27 actually
        // shows -- the real thing is a two-card layout (left: "<Team> Choosing" Accept/Decline
        // card; right: player photo + penalty type + "Against <Team>", the card this region
        // targets) sitting noticeably higher, roughly FxY 0.50-0.84, not 0.62-0.84. "Against <Team
        // Name>" is still the only signal available for which side committed the penalty -- the
        // persistent scorebug's "flag" ribbon (see "flag" region above) is just yellow, not
        // team-colored, so it can't tell offense/defense apart by itself. RouteEngineTick compares
        // this region's matched text against HomeTeamName/AwayTeamName to resolve
        // IsPenaltyOnOffense/IsPenaltyOnDefense. Widened generously around the confirmed card
        // position (only one live matchup's screenshots to calibrate from so far).
        new WatchedRegion
        {
            Name = "penaltyagainst",
            FxX = 0.65, FxY = 0.50, FxW = 0.34, FxH = 0.34,
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
        // Pregame team-intro/"READY" screen. Calibrated 2026-08-12 from FOUR live in-game
        // screenshots (owner-provided, 2560x1440): Akron @ Tennessee (home ready), Akron @
        // Tennessee (away ready), Alabama @ Tennessee (away ready), Ball State @ Texas A&M (away
        // ready). Confirms the READY prompt replaces whichever side's team-name pill belongs to
        // the player who's hit ready (NOT a fixed center position -- it can appear in the LEFT
        // pill, the RIGHT pill, or both once everyone's ready), and that pill BACKGROUND tints to
        // that team's own color (gold/Akron, crimson/Alabama, red-black/Ball State) with text
        // color adapting for contrast (white-on-dark vs black-on-light) -- purely cosmetic, the
        // pill's on-screen position and the literal "READY" text never move. The crop deliberately
        // spans the full-width band containing BOTH team-name pills (y range confirmed identical
        // across all 4 screenshots, ~705-805px of a 1125-scaled reference) rather than either
        // team's individual pill -- this is what keeps it team-neutral/universal: it never anchors
        // on either team's helmet icon, logo, or pill color, only the OCR regex below matching
        // literal "READY" text wherever it lands in that shared band.
        //
        // CRITICAL: this must stay anchored on team-color-INDEPENDENT elements only -- do not
        // narrow this crop to one side's pill or key off pill/panel color for any future
        // tightening pass. This project already had to fix three bugs from exactly that mistake --
        // see commit b6e1c8f ("Fix dead TFL/Defense/BigEvent signal, kickoff OCR word-split, and
        // possession misread during situation banners"). Do not repeat that pattern here.
        new WatchedRegion
        {
            Name = "pregameready",
            FxX = 0.03, FxY = 0.60, FxW = 0.95, FxH = 0.13,
            Pattern = new Regex(@"\bREADY\b", RegexOptions.IgnoreCase),
        },
        // "EA SPORTS COLLEGE FOOTBALL 27" flag/title card. Calibrated 2026-08-12 from a live
        // in-game screenshot (owner-provided, 2560x1440 full-window capture of the flag screen --
        // the "COLLEGE FOOTBALL" wordmark spans roughly x:[645,1740] y:[460,700] out of a
        // 2000x1125-scaled reference, i.e. FxX~0.32-0.87, FxY~0.41-0.62; box below is that range
        // widened slightly for OCR margin of error, not pixel-exact). This screen appears at the
        // very start of the pregame broadcast sequence, before the chevron tunnel-walk and before
        // the READY screen. Team-neutral by construction -- the flag graphic and its text are
        // identical for every matchup, so (unlike "pregameready") there's no color-independence
        // caveat here. See RunOutHelper.cs and PlaySnapshot.IsTeamRunOut. Distinct from and does
        // NOT replace the chevron marker (ScorebugPreset.ChevronMarkerFx*), which stays dedicated
        // to "Other: Pregame Take the Field" -- see GameStateEventHelper.cs.
        new WatchedRegion
        {
            Name = "teamrunout",
            FxX = 0.30, FxY = 0.38, FxW = 0.60, FxH = 0.26,
            Pattern = new Regex(@"COLLEGE\s+FOOTBALL", RegexOptions.IgnoreCase),
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
        // Play clock box (the small dark ":30"/":13"-style box between the game clock and the
        // down/distance box). Calibrated 2026-08-12 from 4 live screenshots (owner-provided,
        // 2560x1440): shows a counting-down number pre-snap, and switches to literal "--" (two
        // dashes, not a blank box) the instant the ball is snapped, staying "--" through the live
        // play AND through dead-ball overlays (FLAG, FIELD GOAL result screen) until the ribbon
        // is ready for the next snap, when it resumes counting from a fresh number. That
        // counting -> "--" -> counting cycle is a clean, OCR-noise-resistant "a real play just
        // happened" edge -- see FirstDownOnFirstDownHelper.cs, which is the reason this region was
        // added: down/distance alone can't tell a first-down-on-first-down (Down stays at 1, no
        // edge on Down itself) from mid-drive stillness, but this box provides an unambiguous
        // play-boundary to gate that comparison on. Box position confirmed identical across all 4
        // screenshots (grass background, FLAG overlay, and FIELD GOAL recap screen alike).
        // Pattern intentionally only matches digits, not "--" -- currentValue/region.Last being
        // null IS the "--"/dead-ball state; no digits means no match.
        new WatchedRegion
        {
            Name = "playclock",
            FxX = 0.70, FxY = 0.83, FxW = 0.06, FxH = 0.14,
            Pattern = new Regex(@"\b\d{1,2}\b"),
        },
    };

    CancellationTokenSource? _cts;

    // FIXED 2026-08-16 (state-machine audit finding #1): Stop() only cancels `_cts` and returns
    // immediately -- it never waits for the previous RunAsync loop to actually observe
    // cancellation and exit. If Start() is called again quickly (fast preset-switch, accidental
    // double-click, or a caller that starts a new game without calling Stop() first), the OLD
    // loop iteration -- already past its last `ct.IsCancellationRequested` check -- can still run
    // RouteEngineTick() using stale readers/regions AFTER this Start() call has already reset
    // every "clean slate" field below, producing a bogus PlaySnapshot delta from a half-reset
    // watcher. `_generation` gives every RunAsync loop instance an id captured once at the top;
    // RouteEngineTick (and the final event fire) only run/fire when the loop's own generation is
    // still the current one, so a stale loop's tick becomes an inert no-op instead of a race.
    int _generation;

    // 2026-08-19 (handoff root cause #2 partial fix) -- see GameStateEventHelper.
    // SuppressOneShotsAlreadyPassed's doc comment for the full reasoning.
    GameStateEventHelper? _gameStateEventHelper;
    bool _checkedRestartOneShots;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        int myGeneration = ++_generation;
        // FIXED: was `??=`, so only the FIRST Start() call in the process's lifetime built fresh
        // evaluators -- every subsequent GAMETIME (Stop Watching, then start a new game) reused
        // the SAME evaluator instances, carrying over per-game state like KickoffHelper's
        // opening/second-half-kickoff-already-fired flags into the next game. Now every Start()
        // gets a clean set, matching "Stop Watching... unlock and start a new one" being a real
        // new-game boundary. Same reasoning for resetting the snapshots and first-tick flag --
        // without this, a 2nd+ game's Previous.Quarter starts at whatever the last game ended on
        // instead of 0, so pregame ("Previous.Quarter == 0 && Current.Quarter == 1") could only
        // ever fire once per app launch, not once per game.
        _gameStateEventHelper = new GameStateEventHelper();
        _checkedRestartOneShots = false;
        _eventRouter = CreateEventRouter(_gameStateEventHelper);
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
        _frozenSince = null;
        _wasBlackScreen = false;
        _blackScreenSince = null;
        _pregameTakeFieldFired = false;
        _sawPregameReady = false;
        _ramDownStableValue = null; _ramDownStableSince = null;
        _ramYardsToGoStableValue = null; _ramYardsToGoStableSince = null;
        _ramPossessionStableValue = null; _ramPossessionStableSince = null;
        _ocrDownStableValue = null; _ocrDownStableSince = null;
        _ocrYardsToGoStableValue = null; _ocrYardsToGoStableSince = null;
        _ocrPossessionStableValue = null; _ocrPossessionStableSince = null;
        _ramHomeScoreStableValue = null; _ramHomeScoreStableSince = null;
        _ramAwayScoreStableValue = null; _ramAwayScoreStableSince = null;
        _ocrHomeScoreStableValue = null; _ocrHomeScoreStableSince = null;
        _ocrAwayScoreStableValue = null; _ocrAwayScoreStableSince = null;
        _lastResolvedPossessionReported = null;
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
        _ramOrientationChecked = false;
        _ramOrientationSwapped = false;
        _ramOrientationStreakValue = null;
        _ramOrientationStreak = 0;
        _lastConfirmedRamPossessionAway = null;
        _pendingRamPossessionAway = null;
        _pendingRamPossessionTicks = 0;
        _lastConfirmedFinalDown = null;
        _pendingFinalDown = null;
        _pendingFinalDownTicks = 0;
        _lastConfirmedFinalYardsToGo = null;
        _pendingFinalYardsToGo = null;
        _pendingFinalYardsToGoTicks = 0;
        _lastConfirmedFinalPossessionAway = null;
        _pendingFinalPossessionAway = null;
        _pendingFinalPossessionTicks = 0;
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
        // Same "new Start() is a genuinely clean slate" reasoning as every sticky OCR field above,
        // applied to the reader-side sticky cache -- a second game must not inherit the first
        // game's last-known reader score/down/possession.
        _scoreboardNormalizer.Reset();
        _scoreboardReader.ClearCache();
        _scoreboardJsonPath = ScoreboardReaderPaths.ResolveLiveScoreboardJsonPath();
        ScoreboardStatus = ScoreboardReaderStatus.NotFound;
        _lastScoreboardReaderState = null;

        _ramNormalizer.Reset();
        _ramModeEnabled = ConfigStore.LoadScoreboardReaderRamModeEnabled();
        _ramLiveDataPath = Path.Combine(ScoreboardReaderPaths.ResolveRamReaderDataDirectory(), "live-game-data.json");
        RamReaderStatus = ScoreboardReaderStatus.NotFound;
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
        _ = RunAsync(_cts.Token, myGeneration);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    async Task RunAsync(CancellationToken ct, int myGeneration)
    {
        OcrEngine? ocrEngine = null;
        IntPtr hwnd = IntPtr.Zero;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    // Read fresh each time (not cached in a local) so switching the scorebug
                    // preset mid-session re-scopes which process(es) get searched immediately.
                    hwnd = FindGameWindow(ActivePreset.GameProcessNames ?? GameProcessNames);
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
                    _foregroundStallSince ??= DateTime.UtcNow;
                    // Before giving up this tick: the foreground window might genuinely be the
                    // game, just under a DIFFERENT candidate process than the one hwnd locked onto
                    // (see FindGameWindow's 2026-08-14 fix doc comment -- e.g. RemotePlay is what's
                    // actually on screen but a background CollegeFB27 process got matched first).
                    // Re-target instead of skipping so this doesn't loop forever in that case.
                    IntPtr fg = Native.GetForegroundWindow();
                    if (fg != IntPtr.Zero && IsCandidateGameWindow(fg, ActivePreset.GameProcessNames ?? GameProcessNames))
                    {
                        // FIXED 2026-08-16 (state-machine audit finding #6): if the foreground
                        // window keeps ALTERNATING between two+ different valid candidates,
                        // _pendingForegroundCandidate never matches twice in a row and this branch
                        // never retargets -- capture stalls indefinitely with only a repeated log
                        // line. Once stalled past ForegroundStallTimeout, force the retarget onto
                        // whatever candidate is foreground THIS tick instead of waiting for the
                        // normal 2-consecutive-tick debounce, so capture always eventually resumes.
                        bool forceRetarget = DateTime.UtcNow - _foregroundStallSince.Value > ForegroundStallTimeout;
                        if (_pendingForegroundCandidate != fg && !forceRetarget)
                        {
                            // First tick seeing this candidate -- wait for a second consecutive
                            // tick before retargeting (see _pendingForegroundCandidate doc comment).
                            _pendingForegroundCandidate = fg;
                            await Task.Delay(500, ct);
                            continue;
                        }

                        // rect/winW/winH above were measured against the OLD hwnd -- re-loop
                        // immediately so they get recomputed against the new one instead of
                        // capturing the wrong window's stale geometry this tick.
                        if (forceRetarget)
                            Log?.Invoke($"[watcher] foreground candidate kept changing for {ForegroundStallTimeout.TotalSeconds:0}s+ -- forcing retarget without waiting for a repeat");
                        else
                            Log?.Invoke("[watcher] foreground window is a different game-window candidate -- switching to it");
                        hwnd = fg;
                        _pendingForegroundCandidate = IntPtr.Zero;
                        _foregroundStallSince = null;
                        continue;
                    }
                    else
                    {
                        _pendingForegroundCandidate = IntPtr.Zero;
                        Log?.Invoke("[watcher] game window isn't focused/foreground -- skipping capture this tick (bring the game to the front to resume detection)");
                        await Task.Delay(500, ct);
                        continue;
                    }
                }
                _foregroundStallSince = null;

                using var fullBmp = new Bitmap(winW, winH, PixelFormat.Format32bppArgb);
                using (var fg = Graphics.FromImage(fullBmp))
                {
                    fg.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(winW, winH));
                }

                UpdateFrozenFrameState(fullBmp, winW, winH);
                CheckBlackScreenRunoutTrigger();

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

                    // Owner clarification 2026-08-12: the black screen CheckBlackScreenRunoutTrigger
                    // times off only ever appears AFTER the READY screen, never before it. Tracked
                    // right here in the unconditional per-region loop (NOT inside RouteEngineTick,
                    // which is skipped entirely while _frameIsFrozen -- the READY screen's fairly
                    // static art is a real candidate for tripping that gate, and this flag needs to
                    // land even if RouteEngineTick doesn't run for a few ticks).
                    // FIXED 2026-08-14 (owner report, live: runout song never fired) -- this used to
                    // only flip the guard flag and wait for a LATER black-screen transition to arm
                    // the countdown (see CheckBlackScreenRunoutTrigger). If that transition is ever
                    // missed (brightness threshold not hit, frame sampled mid-fade, etc.) the timer
                    // never starts at all. Owner wants the countdown tied directly to READY instead:
                    // arm it the instant OCR reads "ready" for the first time this game, full stop.
                    // CheckBlackScreenRunoutTrigger's own black-screen arm is left in place as a
                    // no-op fallback for the (now rare) case READY itself was never read but a
                    // pregame black screen still shows up.
                    if (region.Name == "pregameready" && currentValue == "ready" && !_sawPregameReady)
                    {
                        _sawPregameReady = true;
                        if (!_pregameTakeFieldFired && _blackScreenSince == null)
                        {
                            _blackScreenSince = DateTime.UtcNow;
                            Log?.Invoke("[watcher] READY detected -- arming pregame runout timer");
                        }
                    }

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
                if (!_frameIsFrozen && myGeneration == _generation)
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

    /// <summary>CFB27/CFB26-Console-only field-position arrow read (see ArrowUp's doc comment) --
    /// OCR's the same two crops SamplePossessionByUnderline already samples for brightness (only
    /// one of the two slots actually renders ball-position+arrow text at a time, whichever side has
    /// the ball), and looks for an up/down arrow glyph in whichever crop actually produced text.
    /// Deliberately does NOT touch _lastPossession/PossessionChanged -- this is a separate signal,
    /// read-only side effect on ArrowUp. Best-effort: OCR reliably reading a small arrow GLYPH (not
    /// a normal alphanumeric character) is unconfirmed, so a generous set of likely OCR misreads
    /// (^, v, V, Λ, and the real ▲/▼ glyphs) is accepted rather than just the exact Unicode arrows.
    /// Wired to CollegeFootball26Console the same way as CFB27 (owner request 2026-08-13) -- that
    /// preset's underline coordinates are still the unverified/cloned placeholders flagged on
    /// CollegeFootball26Console's own doc comment, so this inherits that same caveat until a real
    /// CFB 26 console screenshot is used to re-calibrate them.</summary>
    async Task SampleFieldPositionArrowFromWindow(Bitmap fullBmp, int winW, int winH, OcrEngine engine)
    {
        if (_activePreset.Name != ScorebugPreset.CollegeFootball27.Name
            && _activePreset.Name != ScorebugPreset.CollegeFootball26Console.Name) { ArrowUp = null; return; }
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
        double brightnessSum = 0;
        int sampleCount = 0;
        unchecked
        {
            int hash = 17;
            for (int gy = 0; gy < GridRows; gy++)
            {
                int y = Math.Min(winH - 1, gy * winH / GridRows);
                for (int gx = 0; gx < GridCols; gx++)
                {
                    int x = Math.Min(winW - 1, gx * winW / GridCols);
                    var px = fullBmp.GetPixel(x, y);
                    hash = hash * 31 + px.ToArgb();
                    brightnessSum += (px.R + px.G + px.B) / 3.0;
                    sampleCount++;
                }
            }
            _lastFrameBrightness = sampleCount > 0 ? brightnessSum / sampleCount : 255;

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
        {
            _frozenSince = DateTime.UtcNow;
            Log?.Invoke("[watcher] frame appears frozen (paused/menu screen) -- suspending event detection");
        }
        else if (!_frameIsFrozen && wasFrozen)
        {
            _frozenSince = null;
            Log?.Invoke("[watcher] frame is moving again -- resuming event detection");
        }
        else if (_frameIsFrozen && _frozenSince.HasValue && DateTime.UtcNow - _frozenSince.Value > MaxFrozenDuration)
        {
            // Safety valve -- see MaxFrozenDuration's doc comment. Force-clear the streak too, not
            // just the flag, so a genuinely still-static frame doesn't immediately re-trip on the
            // very next tick and silently re-suspend detection right back where it started.
            Log?.Invoke($"[watcher] frame has appeared frozen for over {MaxFrozenDuration.TotalSeconds:0}s -- forcing detection back on (this shouldn't happen for a real pause/menu screen this long)");
            _frameIsFrozen = false;
            _frozenFrameStreak = 0;
            _frozenSince = null;
        }
    }

    /// <summary>See the "Black-screen-timed pregame runout" field block's doc comment. Called
    /// unconditionally every tick (unlike RouteEngineTick, which is skipped while
    /// _frameIsFrozen) so the countdown advances even through a black loading screen the
    /// frozen-frame detector has suspended everything else for.</summary>
    void CheckBlackScreenRunoutTrigger()
    {
        if (_pregameTakeFieldFired) return;

        bool isBlackNow = _lastFrameBrightness <= BlackScreenBrightnessThreshold;
        // Only ARM off a black screen that (a) appears before any real quarter has been read --
        // once the game is actually underway, a black transition is just a camera cut/replay/etc,
        // not the pregame loading screen this feature is timing off of -- AND (b) after the READY
        // screen has actually been seen this game (owner: the real black screen only ever shows up
        // AFTER Ready, never before). Without these guards, an unrelated black flash (app launch,
        // a loading screen before Ready even appears, mid-game camera cut) could burn the one-shot
        // _pregameTakeFieldFired guard on the wrong moment.
        // FIXED 2026-08-14: the READY OCR hit above now arms _blackScreenSince directly the moment
        // it's read, which is the primary path now. This black-screen arm is a fallback only for
        // when READY itself was somehow never read but a pregame black screen still shows up --
        // `_blackScreenSince == null` here stops it from re-arming (and so restarting) a timer the
        // READY sighting already started.
        if (isBlackNow && !_wasBlackScreen && _sawPregameReady && _blackScreenSince == null && string.IsNullOrEmpty(_lastKnownQuarter))
        {
            _blackScreenSince = DateTime.UtcNow;
            Log?.Invoke("[watcher] black screen detected pre-kickoff -- arming pregame runout timer");
        }
        _wasBlackScreen = isBlackNow;

        if (_blackScreenSince.HasValue && DateTime.UtcNow - _blackScreenSince.Value >= BlackScreenRunoutDelay)
        {
            _pregameTakeFieldFired = true;
            _blackScreenSince = null;
            Log?.Invoke("[watcher] firing Other: Pregame Take the Field (black-screen timer)");
            EventsDetected?.Invoke(new List<TriggerEvent>
            {
                new() { EventKey = "Other: Pregame Take the Field", Volume = 85, IsEarnedBigEvent = true }
            });
        }
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

    /// <summary>Reads and validates the bundled RAM reader's status document -- no retry/grace
    /// window needed here unlike ScoreboardJsonReader (the screen-JSON path): the RAM reader only
    /// writes on a real change and Coffee's own 20s freshness rule (ported in RamReaderValidator)
    /// already covers staleness, so a single mid-write partial read just self-heals on the next
    /// ~250ms tick rather than needing a cached fallback.</summary>
    (ScoreboardReaderStatus, ScoreboardReaderState?) ReadRamDocument(string path)
    {
        if (!File.Exists(path)) return (ScoreboardReaderStatus.NotFound, null);
        try
        {
            string json = File.ReadAllText(path);
            var (state, fields) = RamReaderValidator.Validate(json, FindRamGameProcessId(), DateTime.UtcNow);
            return state != null
                ? (ScoreboardReaderStatus.Connected, state)
                : (ScoreboardReaderStatus.WaitingForGameData, null);
        }
        catch (IOException)
        {
            return (ScoreboardReaderStatus.WaitingForGameData, null); // mid-write race -- self-heals next tick
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[RamReader] read failed: {ex.Message}");
            return (ScoreboardReaderStatus.Error, null);
        }
    }

    /// <summary>A5 of the integration plan: compares this tick's OCR-driven score delta against
    /// what the reader independently reports, purely for the Event Log -- never fires or
    /// suppresses an actual event. Easily disabled by removing the one call site above.</summary>
    void LogScoreCorroboration(PlaySnapshot previous, PlaySnapshot current, ScoreboardReaderState readerState)
    {
        int awayDelta = current.AwayScore - previous.AwayScore;
        int homeDelta = current.HomeScore - previous.HomeScore;
        if (awayDelta == 0 && homeDelta == 0) return;

        int? readerAway = readerState.Away?.Score;
        int? readerHome = readerState.Home?.Score;
        bool agrees = (awayDelta == 0 || readerAway == current.AwayScore)
            && (homeDelta == 0 || readerHome == current.HomeScore);
        EventActivityLog.Record("n/a", "n/a", agrees
            ? "(reader corroboration) OCR score change confirmed by reader"
            : $"(reader corroboration) OCR score change NOT confirmed by reader -- reader reports away={readerAway?.ToString() ?? "?"} home={readerHome?.ToString() ?? "?"}");
    }

    // OCR/RAM watchdog dedup -- see LogRamOcrCrosscheck. Only re-logs when the mismatch SET
    // actually changes, not every ~250ms tick it happens to still be true, so a genuinely wrong-
    // game-attached RAM reader doesn't spam the Event Log once per tick for an entire game.
    string? _lastRamOcrMismatchSignature;
    string? _pendingRamOcrMismatchSignature;
    int _pendingRamOcrMismatchStreak;

    // A single flickery OCR tick (misread digit, blank frame) must not reset the dedup signature
    // to null, or the very next tick's real mismatch gets treated as "new" and re-logged. Require
    // the same mismatch signature to repeat this many consecutive ticks before it's considered
    // real and logged/latched.
    const int RamOcrMismatchConfirmTicks = 2;

    /// <summary>2026-08-13 reliability addition: RAM stays authoritative/primary for PlaySnapshot
    /// whenever it's CONNECTED (unchanged) -- this is a SILENT cross-check underneath it, not a
    /// second validation gate. BANDroom's own OCR keeps sampling every tick regardless of which
    /// source is primary (nothing here turns OCR off), so comparing "what OCR independently saw"
    /// against "what RAM says" costs nothing extra to compute -- both values already exist in
    /// RouteEngineTick's locals before the RAM override overwrites them. Purely diagnostic: logged
    /// via EventActivityLog, never blocks/overrides the RAM-sourced PlaySnapshot and never changes
    /// any trigger/event behavior. Skips entirely until OCR has read something real this game
    /// (avoids "mismatch" noise from pregame's all-zero OCR state).</summary>
    void LogRamOcrCrosscheck(int ocrDown, int ocrYardsToGo, int ocrAwayScore, int ocrHomeScore, bool ocrPossessionAway, ReaderNumericSnapshot ram)
    {
        if (ocrDown == 0 && ocrAwayScore == 0 && ocrHomeScore == 0) return;

        var mismatches = new List<string>();
        if (ocrAwayScore != ram.AwayScore) mismatches.Add($"away score RAM={ram.AwayScore} OCR={ocrAwayScore}");
        if (ocrHomeScore != ram.HomeScore) mismatches.Add($"home score RAM={ram.HomeScore} OCR={ocrHomeScore}");
        if (ocrDown != 0 && ocrDown != ram.Down) mismatches.Add($"down RAM={ram.Down} OCR={ocrDown}");
        if (ocrDown != 0 && ocrYardsToGo != ram.YardsToGo) mismatches.Add($"distance RAM={ram.YardsToGo} OCR={ocrYardsToGo}");
        if (ocrPossessionAway != ram.PossessionAway) mismatches.Add($"possession RAM={(ram.PossessionAway ? "away" : "home")} OCR={(ocrPossessionAway ? "away" : "home")} HavePossession={ram.HavePossession}");

        if (mismatches.Count == 0)
        {
            _pendingRamOcrMismatchSignature = null;
            _pendingRamOcrMismatchStreak = 0;
            _lastRamOcrMismatchSignature = null;
            return;
        }

        string signature = string.Join("|", mismatches);
        if (signature == _lastRamOcrMismatchSignature) return;

        if (signature == _pendingRamOcrMismatchSignature)
        {
            _pendingRamOcrMismatchStreak++;
        }
        else
        {
            _pendingRamOcrMismatchSignature = signature;
            _pendingRamOcrMismatchStreak = 1;
        }

        if (_pendingRamOcrMismatchStreak < RamOcrMismatchConfirmTicks) return;

        _lastRamOcrMismatchSignature = signature;
        EventActivityLog.Record("n/a", "n/a", $"(RAM/OCR watchdog) RAM is primary but disagrees with OCR -- {string.Join("; ", mismatches)}");
    }

    /// <summary>Builds a PlaySnapshot from the current OCR region state and routes it through
    /// all evaluators, firing EventsDetected with the results.</summary>
    void RouteEngineTick()
    {
        if (_eventRouter == null) return;

        // Poll the reader on the same cadence RouteEngineTick itself runs at (~250ms, gated by
        // the "down" region's own poll delay -- see the Task.Delay(250) comment near the bottom
        // of RunAsync). Reads null status/state when nothing's ever been resolved/found -- see
        // ScoreboardJsonReader.Read and ScoreboardReaderPaths' own doc comments for why this is
        // always a safe no-op fallback to OCR rather than a hard dependency.
        var (readerStatus, readerState) = _scoreboardJsonPath != null
            ? _scoreboardReader.Read(_scoreboardJsonPath)
            : (ScoreboardReaderStatus.NotFound, null);
        ScoreboardStatus = readerStatus;
        _lastScoreboardReaderState = readerState;
        ReaderNumericSnapshot? readerSnapshot = readerStatus == ScoreboardReaderStatus.Connected
            ? _scoreboardNormalizer.Normalize(readerState)
            : null;

        // Bundled RAM reader, same cadence, polled independently of the screen-JSON reader above
        // -- takes priority when CONNECTED (see this field's own doc comment for why). OFF
        // entirely (never polls the file at all) unless the user has opted in.
        ReaderNumericSnapshot? ramSnapshot = null;
        if (_ramModeEnabled && _ramLiveDataPath != null)
        {
            var (ramStatus, ramState) = ReadRamDocument(_ramLiveDataPath);
            RamReaderStatus = ramStatus;
            if (ramStatus == ScoreboardReaderStatus.Connected)
                ramSnapshot = _ramNormalizer.Normalize(ramState);
        }
        else
        {
            RamReaderStatus = ScoreboardReaderStatus.NotFound;
        }
        readerSnapshot = ramSnapshot ?? readerSnapshot;

        // Auto-restart watchdog (2026-08-19, owner request: "ram reader permanently on... reset
        // if it loses [connection]") -- see RestartRamReader's own doc comment. Only counts ticks
        // while RAM mode is actually enabled; a user who's opted out entirely shouldn't get
        // surprise restart attempts for a reader they never asked to run.
        if (_ramModeEnabled)
        {
            if (RamReaderStatus == ScoreboardReaderStatus.Connected)
            {
                _ramDisconnectedTicks = 0;
            }
            else
            {
                _ramDisconnectedTicks++;
                if (_ramDisconnectedTicks >= RamReaderRestartThreshold
                    && DateTime.UtcNow - _lastRamRestartAttemptUtc >= RamRestartCooldown)
                {
                    _lastRamRestartAttemptUtc = DateTime.UtcNow;
                    _ramDisconnectedTicks = 0;
                    Log?.Invoke($"[ScoreboardReaderHost watchdog] RAM reader not connected for {RamReaderRestartThreshold * 250}ms+ -- restarting it");
                    RestartRamReader?.Invoke();
                }
            }
        }

        var situationRegion = _regions.FirstOrDefault(r => r.Name == "situation");
        var clockRegion = _regions.FirstOrDefault(r => r.Name == "clock");
        var penaltyAgainstRegion = _regions.FirstOrDefault(r => r.Name == "penaltyagainst");
        var pregameReadyRegion = _regions.FirstOrDefault(r => r.Name == "pregameready");
        var teamRunOutRegion = _regions.FirstOrDefault(r => r.Name == "teamrunout");
        var playClockRegion = _regions.FirstOrDefault(r => r.Name == "playclock");
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
        // Previously hardcoded 0 (dead code disabling every red-zone/field-position evaluator) --
        // OCR has no yard-line region calibrated, so this only ever gets a real value from the
        // reader below.
        int yardLine = 0;
        int awayTimeoutsRemaining = _lastAwayTimeoutsRemaining;
        int homeTimeoutsRemaining = _lastHomeTimeoutsRemaining;
        bool? readerPossessionAway = null;
        // RAM-only -- OCR never resolves a numeric play clock, only the IsPlayClockCounting edge
        // below. -1 stays -1 (unavailable) unless the reader connects and resolves it.
        int playClock = -1;
        // RAM-only, "offense"/"defense"/null -- see ScoreboardReaderState.PenaltySide's doc comment.
        string? ramPenaltySide = null;
        // RAM-only, see the "RAM-derived play-clock-counting signal" comment below for why this
        // exists alongside (not replacing) the OCR-based playClockRegion check.
        bool? ramPlayClockCounting = null;

        // Captured BEFORE the reader override below can overwrite these locals -- OCR keeps
        // sampling every tick regardless of which source ends up primary (nothing turns it off),
        // so these are exactly "what OCR independently saw this tick," used only by the RAM/OCR
        // watchdog cross-check further down. Never fed into the actual snapshot when RAM is primary.
        int ocrDown = down, ocrYardsToGo = yardsToGo, ocrAwayScore = awayScore, ocrHomeScore = homeScore;
        bool ocrPossessionAway = _lastPossession == "away";

        // OCR-side half of the stale-RAM-field fallback below -- tracks whether OCR has settled
        // on its current down/distance/possession reading (as opposed to a one-tick misread), so
        // a frozen RAM value only ever gets overridden by a corroborated OCR reading, never noise.
        DateTime nowUtc = DateTime.UtcNow;
        bool ocrDownSettled = IsFieldStableFor(ocrDown, ref _ocrDownStableValue, ref _ocrDownStableSince, nowUtc, OcrFieldCorroborationWindow);
        bool ocrYardsToGoSettled = IsFieldStableFor(ocrYardsToGo, ref _ocrYardsToGoStableValue, ref _ocrYardsToGoStableSince, nowUtc, OcrFieldCorroborationWindow);
        bool ocrPossessionSettled = IsFieldStableFor(ocrPossessionAway, ref _ocrPossessionStableValue, ref _ocrPossessionStableSince, nowUtc, OcrFieldCorroborationWindow);
        bool ocrHomeScoreSettled = IsFieldStableFor(ocrHomeScore, ref _ocrHomeScoreStableValue, ref _ocrHomeScoreStableSince, nowUtc, OcrFieldCorroborationWindow);
        bool ocrAwayScoreSettled = IsFieldStableFor(ocrAwayScore, ref _ocrAwayScoreStableValue, ref _ocrAwayScoreStableSince, nowUtc, OcrFieldCorroborationWindow);

        // RAM home/away orientation check -- see _ramOrientationChecked's own doc comment. Runs
        // once per game, before the rs-driven overlay below so a confirmed swap corrects every
        // home/away-scoped RAM field (score, timeouts, possession) for the rest of this tick and
        // every tick after.
        if (!_ramOrientationChecked && readerSnapshot is { } rsForOrientation
            && rsForOrientation.AwayScore >= 0 && rsForOrientation.HomeScore >= 0
            && ocrAwayScoreSettled && ocrHomeScoreSettled && (ocrAwayScore > 0 || ocrHomeScore > 0))
        {
            bool directMatches = rsForOrientation.AwayScore == ocrAwayScore && rsForOrientation.HomeScore == ocrHomeScore;
            bool swappedMatches = rsForOrientation.AwayScore == ocrHomeScore && rsForOrientation.HomeScore == ocrAwayScore;
            bool? candidate = directMatches ? false : swappedMatches ? true : null;
            if (candidate is { } swapped)
            {
                if (_ramOrientationStreakValue == swapped) _ramOrientationStreak++;
                else { _ramOrientationStreakValue = swapped; _ramOrientationStreak = 1; }
                if (_ramOrientationStreak >= RamOrientationConfirmTicks)
                {
                    _ramOrientationChecked = true;
                    _ramOrientationSwapped = swapped;
                    Log?.Invoke($"[RAM watchdog] orientation check: RAM home/away{(swapped ? " IS" : " is NOT")} swapped vs OCR score (locked for the rest of this game)");
                }
            }
            else
            {
                _ramOrientationStreakValue = null;
                _ramOrientationStreak = 0;
            }
        }
        if (_ramOrientationChecked && _ramOrientationSwapped && readerSnapshot is { } rsToFix)
        {
            readerSnapshot = rsToFix with
            {
                AwayScore = rsToFix.HomeScore,
                HomeScore = rsToFix.AwayScore,
                AwayTimeoutsRemaining = rsToFix.HomeTimeoutsRemaining,
                HomeTimeoutsRemaining = rsToFix.AwayTimeoutsRemaining,
                PossessionAway = rsToFix.HavePossession ? !rsToFix.PossessionAway : rsToFix.PossessionAway,
            };
        }

        // Reader takes over score/possession/yard line/down/distance/timeouts when CONNECTED --
        // see the ScoreboardStatus field's own doc comment. Event flags (situation/banner/etc)
        // stay OCR-owned always; structural-turnover/penalty inference below still reads the OCR
        // possession color (_lastPossession) exactly as before, since those are OCR-specific
        // safety nets keyed to the OCR situation text, not simple pass-through fields.
        if (readerSnapshot is { } rs)
        {
            down = rs.Down != 0 ? rs.Down : down;
            quarter = rs.Quarter != 0 ? rs.Quarter : quarter;
            // RELIABILITY FIX 2026-08-14 (real bug, not speculative -- traced from the Session 72
            // handoff's "awayTimeouts/homeTimeouts... successfulReads:0" symptom back to its root
            // cause): these four used to be unconditional overwrites. GameStateNormalizer's sticky
            // cache defaulted YardsToGo/YardLine/HomeScore/AwayScore to 0 when a field had NEVER
            // resolved from the reader -- indistinguishable from "reader confirms it's really 0."
            // Whenever RAM connected (status "live", at least ONE field validated -- e.g. just
            // team names/clock) but its own per-field locator hadn't separately locked score/yard-
            // age/yard-line yet (exactly the intermittent per-session locator failure the handoff
            // doc documents for timeouts), this silently stomped a correct, already-confirmed OCR
            // score/distance down to 0 every tick, killing every score-delta evaluator
            // (TouchdownHelper/FieldGoalPATHelper/SafetyHelper/etc all read Current.HomeScore/
            // AwayScore) for the rest of the game. GameStateNormalizer now returns -1 for these
            // fields until they've genuinely resolved at least once (same -1-sentinel convention
            // AwayTimeoutsRemaining/HomeTimeoutsRemaining already used below), so this now falls
            // back to the OCR-derived local exactly like Down/Quarter/Timeouts already did.
            if (rs.YardsToGo >= 0) yardsToGo = rs.YardsToGo;
            if (rs.AwayScore >= 0) awayScore = rs.AwayScore;
            if (rs.HomeScore >= 0) homeScore = rs.HomeScore;
            timeRemainingSeconds = rs.TimeRemainingSeconds != 0 ? rs.TimeRemainingSeconds : timeRemainingSeconds;
            if (rs.YardLine >= 0) yardLine = rs.YardLine;
            if (rs.AwayTimeoutsRemaining >= 0) awayTimeoutsRemaining = rs.AwayTimeoutsRemaining;
            if (rs.HomeTimeoutsRemaining >= 0) homeTimeoutsRemaining = rs.HomeTimeoutsRemaining;
            if (rs.PlayClock >= 0) playClock = rs.PlayClock;
            ramPenaltySide = rs.PenaltySide;
            // RAM-derived play-clock-counting signal (2026-08-19) -- PlaySnapshot.IsPlayClockCounting
            // was OCR-only (playClockRegion?.Last != null), and FirstDownOnFirstDownHelper is
            // entirely dependent on that one flag toggling correctly to find its play-boundary
            // edges; if that OCR crop isn't reliably calibrated for the active preset, that helper
            // silently never fires at all. The raw playClock NUMBER alone can't distinguish
            // "counting down" from "frozen mid-play" (both are valid integers), but the reader's
            // own freshness data can: DATA-API.md's own advice ("the clocks are your canary...
            // while freshness.playClock shows recent change, that block is provably live") is
            // exactly the recency check used here -- a countdown ticks roughly once a second, so a
            // playClock value that changed within the last ~1.5s is actively counting; one that's
            // been frozen longer than that is a dead ball / mid-play, matching the OCR "--" state.
            if (rs.Freshness?.PlayClock is { ChangedAtUtc: { } playClockChangedAt })
                ramPlayClockCounting = (DateTime.UtcNow - playClockChangedAt) <= PlayClockCountingRecencyWindow;
            // RELIABILITY FIX 2026-08-14 (same bug class, arguably higher-impact -- possession
            // drives which side's Offense/Defense audio fires): ReaderNumericSnapshot.PossessionAway
            // is a plain bool, so it used to default to false ("home has it") the instant ANY
            // reader field connected, even on ticks where possession specifically had never once
            // resolved. That unconditionally overrode GameWatcher's own OCR color-sampled
            // _lastPossession for the rest of the game (readerPossessionAway ?? ... below only
            // falls back to OCR when readerPossessionAway is null, and it was never null once
            // connected). Now only trusts the reader's possession bit once HavePossession
            // confirms it has genuinely resolved at least once; otherwise stays null so OCR's own
            // possession tracking is used, unchanged.
            // REMOVED 2026-08-19 (owner report, repeated live confirmation across a whole session:
            // "this was home" every single time RAM said home and OCR disagreed): the
            // OCR-vs-RAM orientation-inversion correction this replaced was built on the
            // assumption that OCR's team-color possession sampling is the trustworthy tie-breaker
            // and RAM might have home/away backwards. Tonight's evidence was the opposite the
            // entire game -- RAM's raw possession bit was correct every single time, OCR's
            // color-sampled possession read was the one that was wrong (CollegeFootball27's own
            // possession-crop calibration doc comment already flagged this as "only 2 data points
            // ... confirm this still flips correctly" -- never fully trustworthy to begin with).
            // The confirm-streak fix earlier tonight only slowed down how fast a bad OCR signal
            // could poison the correction, it couldn't fix a session where OCR is wrong the WHOLE
            // time -- 3 consecutive comparisons against consistently-wrong OCR just reaches the
            // same wrong conclusion 3 ticks later instead of 1. Now trusts RAM's raw possession
            // bit directly and unconditionally whenever it has resolved -- no per-tick possession
            // correction applied here. The SCORE-based orientation swap above (_ramOrientationChecked/
            // _ramOrientationSwapped) is a separate, one-time-per-game check that already corrected
            // rs.PossessionAway (and rs's score/timeouts) before this point if RAM's home/away was
            // confirmed backwards -- not a revival of the possession-tie-breaker logic this comment
            // describes removing.
            if (rs.HavePossession)
            {
                bool ramPossessionAway = rs.PossessionAway;
                if (_lastConfirmedRamPossessionAway is not { } confirmed)
                {
                    // First resolution this game -- nothing to debounce against yet, trust it
                    // immediately (matches the old unconditional-trust behavior for this one case).
                    _lastConfirmedRamPossessionAway = ramPossessionAway;
                }
                else if (ramPossessionAway != confirmed)
                {
                    if (ramPossessionAway == _pendingRamPossessionAway)
                    {
                        _pendingRamPossessionTicks++;
                        if (_pendingRamPossessionTicks >= 1) // this call is the 2nd agreeing tick
                        {
                            _lastConfirmedRamPossessionAway = ramPossessionAway;
                            _pendingRamPossessionAway = null;
                            _pendingRamPossessionTicks = 0;
                        }
                    }
                    else
                    {
                        _pendingRamPossessionAway = ramPossessionAway;
                        _pendingRamPossessionTicks = 0;
                    }
                }
                else
                {
                    _pendingRamPossessionAway = null;
                    _pendingRamPossessionTicks = 0;
                }
                readerPossessionAway = _lastConfirmedRamPossessionAway;
            }
        }

        // RAM-primary watchdog (2026-08-13): silent, log-only OCR cross-check -- only meaningful
        // when RAM is what's actually driving the snapshot this tick (ramSnapshot, not just the
        // screen-JSON reader). See LogRamOcrCrosscheck's own doc comment.
        if (ramSnapshot is { } ramForWatchdog)
            LogRamOcrCrosscheck(ocrDown, ocrYardsToGo, ocrAwayScore, ocrHomeScore, ocrPossessionAway, ramForWatchdog);

        // Stale-RAM-field fallback (2026-08-14). Deliberately scoped to down/distance/possession
        // (the fields actually observed stuck live); score/timeouts/yard line already have their
        // own -1-sentinel "never resolved" protection. Runs AFTER the rs-driven assignments above
        // so it can override them back to OCR once confirmed wrong -- otherwise this is a no-op
        // and RAM's value is used exactly as before this fix existed.
        //
        // 2026-08-15 fix: this used to also require the reader's own core-block clocks to look
        // frozen (CoreBlockFreshnessWindow) before even considering a field stuck -- the idea was
        // "if the reader is demonstrably alive, don't second-guess it." In practice the game clock
        // is ticking on essentially every live tick, so that gate was permanently closed and this
        // fallback could never fire *during actual gameplay* -- exactly when a wrong-not-stale RAM
        // field (e.g. possession genuinely reading the wrong side while everything else in the
        // block keeps updating normally) does the most damage, misrouting cues to the wrong team.
        // The double corroboration below (RAM's OWN value hasn't moved for RamFieldStaleThreshold,
        // AND OCR has independently settled on a persistently different value for
        // OcrFieldCorroborationWindow) is already the real false-positive guard; requiring the
        // whole block to ALSO look frozen was redundant on top of it, not an extra safety margin.
        if (ramSnapshot is { } ramForStaleness)
        {
            // 2026-08-15 fix: each IsFieldStableFor(ram value, ...) call below MUST run every
            // single tick regardless of anything else, or its own stability clock stalls. The
            // previous version called it as the last operand of a short-circuited && -- so on any
            // tick where e.g. ocrDownSettled was still false (OCR itself mid-flicker), RAM's own
            // stability tracker was never invoked at all that tick, meaning "how long has RAM been
            // stuck" silently paused instead of accumulating. Live symptom: RAM stuck 27+ real
            // seconds on a wrong down while the fallback's own 5s threshold kept effectively
            // resetting because its clock wasn't running the whole time. Splitting the "compute
            // stability" call out from the "should I act on it" condition fixes this for all 5
            // fields the same way.
            // REMOVED 2026-08-19 (see the readerPossessionAway assignment above for the full
            // reasoning): OCR's possession read is no longer trusted as ground truth for this
            // preset -- confirmedRamPossessionAway/ramPossessionStable computed only to feed the
            // watchdog LOG line below (still useful diagnostic signal), never to override
            // readerPossessionAway anymore.
            bool correctedRamPossessionAway = ramForStaleness.PossessionAway;

            bool ramDownStable = IsFieldStableFor(ramForStaleness.Down, ref _ramDownStableValue, ref _ramDownStableSince, nowUtc, RamFieldStaleThreshold);
            bool ramYardsToGoStable = IsFieldStableFor(ramForStaleness.YardsToGo, ref _ramYardsToGoStableValue, ref _ramYardsToGoStableSince, nowUtc, RamFieldStaleThreshold);
            bool ramPossessionStable = IsFieldStableFor(correctedRamPossessionAway, ref _ramPossessionStableValue, ref _ramPossessionStableSince, nowUtc, RamFieldStaleThreshold);
            bool ramHomeScoreStable = IsFieldStableFor(ramForStaleness.HomeScore, ref _ramHomeScoreStableValue, ref _ramHomeScoreStableSince, nowUtc, RamFieldStaleThreshold);
            bool ramAwayScoreStable = IsFieldStableFor(ramForStaleness.AwayScore, ref _ramAwayScoreStableValue, ref _ramAwayScoreStableSince, nowUtc, RamFieldStaleThreshold);

            if (ocrDown != 0 && ocrDown != ramForStaleness.Down && ocrDownSettled && ramDownStable)
            {
                Log?.Invoke($"[RAM watchdog] down stuck at {ramForStaleness.Down} for {RamFieldStaleThreshold.TotalSeconds:0}s+ while OCR settled on {ocrDown} -- falling back to OCR for this field");
                down = ocrDown;
            }
            if (ocrDown != 0 && ocrYardsToGo != ramForStaleness.YardsToGo && ocrYardsToGoSettled && ramYardsToGoStable)
            {
                Log?.Invoke($"[RAM watchdog] distance stuck at {ramForStaleness.YardsToGo} for {RamFieldStaleThreshold.TotalSeconds:0}s+ while OCR settled on {ocrYardsToGo} -- falling back to OCR for this field");
                yardsToGo = ocrYardsToGo;
            }
            if (readerPossessionAway.HasValue && ocrPossessionAway != correctedRamPossessionAway && ocrPossessionSettled && ramPossessionStable)
            {
                // Log-only now (see readerPossessionAway assignment above) -- no longer overrides
                // readerPossessionAway with OCR's value.
                Log?.Invoke($"[RAM watchdog] possession disagreement: RAM stable at {(correctedRamPossessionAway ? "away" : "home")} for {RamFieldStaleThreshold.TotalSeconds:0}s+ while OCR settled on {(ocrPossessionAway ? "away" : "home")} -- trusting RAM (OCR possession no longer used as a correction source)");
            }
            if (ocrDown != 0 && ocrHomeScore != ramForStaleness.HomeScore && ocrHomeScoreSettled && ramHomeScoreStable)
            {
                Log?.Invoke($"[RAM watchdog] home score stuck at {ramForStaleness.HomeScore} for {RamFieldStaleThreshold.TotalSeconds:0}s+ while OCR settled on {ocrHomeScore} -- falling back to OCR for this field");
                homeScore = ocrHomeScore;
            }
            if (ocrDown != 0 && ocrAwayScore != ramForStaleness.AwayScore && ocrAwayScoreSettled && ramAwayScoreStable)
            {
                Log?.Invoke($"[RAM watchdog] away score stuck at {ramForStaleness.AwayScore} for {RamFieldStaleThreshold.TotalSeconds:0}s+ while OCR settled on {ocrAwayScore} -- falling back to OCR for this field");
                awayScore = ocrAwayScore;
            }
        }

        // Flap guard (2026-08-19): applied AFTER the RAM-vs-OCR fallback above has already picked
        // this tick's down/yardsToGo -- see ConfirmFinalValue's doc comment for why this exists.
        ConfirmFinalValue(down, ref _lastConfirmedFinalDown, ref _pendingFinalDown, ref _pendingFinalDownTicks);
        ConfirmFinalValue(yardsToGo, ref _lastConfirmedFinalYardsToGo, ref _pendingFinalYardsToGo, ref _pendingFinalYardsToGoTicks);
        down = _lastConfirmedFinalDown!.Value;
        yardsToGo = _lastConfirmedFinalYardsToGo!.Value;

        // 2026-08-19 (handoff root cause #2 partial fix) -- runs exactly once per Start(), the
        // first tick Quarter/Down both resolve to real values. See SuppressOneShotsAlreadyPassed's
        // doc comment: only acts when the values prove the game was already in progress before
        // this Start() call, never on a genuine fresh pregame (whose first live tick is always
        // Quarter==1/Down==1, which this leaves untouched).
        if (!_checkedRestartOneShots && quarter >= 1 && down is >= 1 and <= 4)
        {
            _checkedRestartOneShots = true;
            _gameStateEventHelper?.SuppressOneShotsAlreadyPassed(quarter, down);
        }

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
        // FIXED 2026-08-16 (owner report -- "penalty it's showing was on me and not my opponent,
        // it's like it's reading it backwards"): this used to read ONLY the raw OCR-sampled
        // _lastPossession, a separate, uncorrected pipeline from snapshot.PossessionAway below
        // (readerPossessionAway ?? (_lastPossession == "away")) -- the exact same RAM-vs-OCR
        // disagreement class the 2026-08-15 possession-routing fix addressed for down/distance
        // events, just never applied here. A penalty's FLAG overlay darkens/obscures the
        // possession ribbon (see IsNearBlack), making OCR possession sampling unusually likely to
        // be stale or wrong at exactly the moment this fires. Now reads the same RAM-primary,
        // fallback-corrected value every other evaluator uses, so penalty classification can't
        // disagree with what the event actually gets routed on.
        bool? possessionIsHomeNow = readerPossessionAway.HasValue ? !readerPossessionAway.Value
            : _lastPossession == null ? null : _lastPossession != "away";
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
        // RAM-primary, OCR fallback (2026-08-19) -- ram.penalty.side already says "offense"/
        // "defense" directly (relative to whichever team currently has the ball), so unlike the
        // OCR path above it needs no penalizedIsHome/possessionIsHomeNow team-name matching at
        // all. Falls back to the OCR-derived flags above only when RAM hasn't reported a side
        // this tick (older reader, or outside the ~10s-45s announcement window).
        bool isPenaltyOnOffense = ramPenaltySide switch
        {
            "offense" => true,
            "defense" => false,
            _ => penalizedIsHome.HasValue && possessionIsHomeNow.HasValue && penalizedIsHome.Value == possessionIsHomeNow.Value,
        };
        bool isPenaltyOnDefense = ramPenaltySide switch
        {
            "defense" => true,
            "offense" => false,
            _ => penalizedIsHome.HasValue && possessionIsHomeNow.HasValue && penalizedIsHome.Value != possessionIsHomeNow.Value,
        };

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

        ConfirmFinalPossession(readerPossessionAway ?? (_lastPossession == "away"), ref _lastConfirmedFinalPossessionAway, ref _pendingFinalPossessionAway, ref _pendingFinalPossessionTicks);

        var snapshot = new PlaySnapshot
        {
            Down = down,
            YardsToGo = yardsToGo,
            Quarter = quarter,
            PossessionAway = _lastConfirmedFinalPossessionAway!.Value,
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
            // Not sticky, same reasoning as IsPregameReady: a one-shot pregame overlay, and
            // RunOutHelper's edge-trigger needs region.Last read straight off the current tick.
            IsTeamRunOut = teamRunOutRegion?.Last == "college football",
            // Not sticky, same "read straight off region.Last" reasoning as the pregame flags
            // above -- FirstDownOnFirstDownHelper's edge-trigger needs the real current-tick
            // state (counting vs "--"), not a value held over from a stale OCR read.
            // OR'd with the RAM-derived recency signal (see its own comment above) -- either
            // source seeing "counting" is enough, so FirstDownOnFirstDownHelper still gets a real
            // play-boundary edge even on a preset/skin whose OCR playclock crop isn't reliable.
            IsPlayClockCounting = playClockRegion?.Last != null || ramPlayClockCounting == true,
            PlayClock = playClock,
            IsFieldGoalAttempt = bannerRegion?.Last == "fieldgoal",
            YardLine = yardLine,
            HomeScore = homeScore,
            AwayScore = awayScore,
            TimeRemainingSeconds = timeRemainingSeconds,
            AwayTimeoutsRemaining = awayTimeoutsRemaining,
            HomeTimeoutsRemaining = homeTimeoutsRemaining,
            BigGame = isBigGame,
        };

        // A5 of the integration plan: reader corroboration is log-only, never wired into
        // EventRouter -- raises confidence / catches a missed OCR trigger without ever being able
        // to fire an event flag by itself. Only meaningful when OCR (not the reader) produced this
        // tick's score, since a reader-primary tick's score IS the reader's own number already.
        if (readerSnapshot == null && readerState != null)
            LogScoreCorroboration(_snapshotCurrent, snapshot, readerState);

        _snapshotPrevious = _snapshotCurrent;
        _snapshotCurrent = snapshot;

        // 2026-08-15 fix (live owner report: an event correctly detected off Home's own possession
        // -- e.g. "3rd Down Conversion" -- routed to Away instead). Root cause: WebMainForm._possession
        // (which decides WHO an event routes to) was fed ONLY from PossessionChanged, which fires off
        // the raw OCR underline-brightness sample -- no RAM input, no stale-fallback correction, a
        // completely separate pipeline from snapshot.PossessionAway above (which IS RAM-primary and
        // fallback-corrected, and is what every evaluator actually used to decide what to fire). Any
        // tick where RAM and OCR disagree on possession, the evaluator and the router could disagree
        // on which team "has the ball" and misroute a correctly-detected event to the wrong side.
        // Publishing the SAME fully-resolved value the evaluators used keeps routing in lockstep with
        // detection -- fires in addition to (not instead of) the OCR sampler's own PossessionChanged,
        // so a legitimate OCR-only flip still updates routing immediately as before; this only adds
        // a second correction path for when RAM's resolved value has already moved on but the OCR
        // sampler hasn't (or won't, if OCR is what's actually wrong this tick).
        string resolvedPossessionSide = snapshot.PossessionAway ? "away" : "home";
        if (resolvedPossessionSide != _lastResolvedPossessionReported)
        {
            _lastResolvedPossessionReported = resolvedPossessionSide;
            PossessionChanged?.Invoke(resolvedPossessionSide);
        }

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

        // Shared dedupe guard with CheckBlackScreenRunoutTrigger's own direct fire of the same
        // EventKey -- whichever signal trips first wins, this stops the other from firing again.
        if (results.Any(r => r.EventKey == "Other: Pregame Take the Field"))
            _pregameTakeFieldFired = true;

        // FIXED 2026-08-16 (owner report -- "a song triggered when I quit out and went to menu"):
        // Stop() only cancels `ct`, checked at the top of RunAsync's while loop and inside a
        // handful of Task.Delay calls -- everything in between, including this whole tick's OCR
        // capture and evaluator pass above, ran to completion uninterrupted even after Stop() was
        // called mid-tick, and would still invoke EventsDetected (-> an audible clip) after the
        // user had already backed out. Re-checking right before the actual fire closes that gap
        // without needing to thread ct-checks through the entire multi-thousand-line tick body.
        if (results.Count > 0 && _cts?.IsCancellationRequested != true)
            EventsDetected?.Invoke(results);
    }

    static EventRouter CreateEventRouter(GameStateEventHelper gameStateEventHelper)
    {
        var rules = new IRuleEvaluator[]
        {
            new BigEventHelper(),
            new DefenseFirstDownAllowedHelper(),
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
            gameStateEventHelper,
            new KickoffHelper(),
            new OffenseAfterOpeningKickHelper(),
            new OffenseAfterPuntHelper(),
            new OffenseDownHelper(),
            new OffenseFourthDownHelper(),
            new OffenseSecondDownHelper(),
            new PenaltyHelper(),
            new PregameHelper(),
            new RunOutHelper(),
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

    // FIXED 2026-08-14 (live bug: "CollegeFB27" and "RemotePlay" both running at once -- e.g. a
    // background/minimized PC install still open while the owner actually plays via PS Remote
    // Play in front -- meant this always locked onto whichever process enumerated first
    // ("CollegeFB27", since it's first in GameProcessNames), even when THAT window wasn't the one
    // actually on screen. The real foreground window (RemotePlay) never matched, so the watcher's
    // GetForegroundWindow() != hwnd check in RunAsync failed forever -- "nothing is reading" with
    // no explanation. Widened same day: rather than searching every known process regardless of
    // context, the caller now passes ActivePreset.GameProcessNames (falling back to the full
    // GameProcessNames list for presets that don't declare one) -- e.g. picking "College Football
    // 27" as your scorebug preset means this only ever looks for the real PC process, so a
    // leftover Remote Play window open for something else can't get matched by accident either.
    // Still checks whether the CURRENT foreground window itself belongs to one of the candidate
    // processes first; only falls back to the old enumerate-in-order behavior if the foreground
    // window isn't a candidate at all (e.g. focus is on Bandroom itself or some unrelated app).
    static IntPtr FindGameWindow(string[] processNames)
    {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && Native.IsWindowVisible(fg) && IsCandidateGameWindow(fg, processNames)) return fg;

        foreach (var name in processNames)
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(name))
            {
                IntPtr hWnd = proc.MainWindowHandle;
                if (hWnd != IntPtr.Zero && Native.IsWindowVisible(hWnd)) return hWnd;
            }
        }

        // Xbox streaming is matched regardless of the preset's process-name scoping -- it's found
        // by window title on a shared OS host process, not a named game process, so there's
        // nothing preset-specific to scope it by.
        IntPtr xboxHwnd = FindXboxAppWindow();
        if (xboxHwnd != IntPtr.Zero) return xboxHwnd;

        return IntPtr.Zero;
    }

    /// <summary>True if hWnd belongs to one of processNames, or is the Xbox host window (matched
    /// by title, same as FindXboxAppWindow). Used by FindGameWindow to prefer whatever's actually
    /// in the foreground, and by RunAsync to re-target hwnd when a DIFFERENT candidate process has
    /// taken the foreground out from under the one it originally locked onto.</summary>
    static bool IsCandidateGameWindow(IntPtr hWnd, string[] processNames)
    {
        Native.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid != 0)
        {
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                foreach (var name in processNames)
                    if (string.Equals(proc.ProcessName, name, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(proc.ProcessName, XboxHostProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    int len = Native.GetWindowTextLength(hWnd);
                    if (len > 0)
                    {
                        var sb = new System.Text.StringBuilder(len + 1);
                        Native.GetWindowText(hWnd, sb, sb.Capacity);
                        if (sb.ToString().Contains(XboxWindowTitleContains, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            catch (ArgumentException) { /* process exited between GetWindowThreadProcessId and GetProcessById */ }
        }
        return false;
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
