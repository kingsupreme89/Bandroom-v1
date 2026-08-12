namespace SupremeStadiumSoundSelector;

/// <summary>A named crop-position preset for the score-bug OCR regions in GameWatcher, so a
/// different broadcast skin (or a completely different game/HUD down the line) can be swapped
/// in via dropdown instead of re-deriving fractional coordinates from scratch each time.
/// BandFxY/BandFxH cover the full-width down/situation/quarter capture band, PossessionFx* is
/// the separate tight box possession-color sampling still needs (see
/// GameWatcher.SamplePossessionFromWindow) -- the down-and-distance box fills solid with the
/// offense's team color, and GameWatcher now tries this FIRST when a preset has it calibrated,
/// falling back to the underline-brightness method (AwayUnderlineFx*/HomeUnderlineFx*) only when
/// the color sample is inconclusive. See KamsCbsScorebugV3's PossessionFx* comment for why this
/// was dropped then restored.
/// "Kam's CBS Scorebug" (the original CBS calibration this file started from) was removed
/// 2026-08-09 at the owner's request -- "Kam's CBSv3" superseded it and is the only CBS-style
/// preset kept going forward.</summary>
public sealed class ScorebugPreset
{
    public string Name { get; init; } = "";
    public double BandFxY { get; init; }
    public double BandFxH { get; init; }
    public double PossessionFxX { get; init; }
    public double PossessionFxY { get; init; }
    public double PossessionFxW { get; init; }
    public double PossessionFxH { get; init; }

    /// <summary>Crop box for the AWAY team's timeout-remaining dash row (small tick marks under
    /// the team name, e.g. "AUBURN — — —"), calibrated 2026-08-07 from a live screenshot showing
    /// away=3/home=0 remaining. Sampled by brightness (see GameWatcher.SampleTimeoutSegments),
    /// NOT OCR text -- these are graphical dashes, not font glyphs.
    /// Estimated crop, not pixel-measured -- treat as a starting point needing live tuning.</summary>
    public double AwayTimeoutFxX { get; init; }
    public double AwayTimeoutFxY { get; init; }
    public double AwayTimeoutFxW { get; init; }
    public double AwayTimeoutFxH { get; init; }

    /// <summary>Owner request 2026-08-11 ("check for Home timeouts too" -- TimeoutHelper only
    /// ever read AwayTimeoutsRemaining, so a Home-team timeout was silently invisible). NOT
    /// independently measured for any preset below -- best-guess placeholder only, mirrored off
    /// each preset's own AwayUnderlineFxX -> HomeUnderlineFxX horizontal offset (same left/right
    /// team-block spacing this scorebug already uses elsewhere) applied to that preset's
    /// AwayTimeoutFxX. Y/W/H copied straight from the Away box, same horizontal strip assumption.
    /// Explicitly unverified -- confirm against a live screenshot showing a Home timeout actually
    /// used (dash count changed) before trusting this crop position.</summary>
    public double HomeTimeoutFxX { get; init; }
    public double HomeTimeoutFxY { get; init; }
    public double HomeTimeoutFxW { get; init; }
    public double HomeTimeoutFxH { get; init; }

    /// <summary>Tight positional crops for the away/home score digits and the game clock,
    /// promoted from hardcoded constants in GameWatcher.cs to per-preset fields 2026-08-10 --
    /// the CBS skin's crops (X=0.35/0.58/0.65) assumed a horizontal score/clock layout that
    /// simply doesn't hold for CFB 27's default scorebug (score digits sit flush against the
    /// team-color blocks, clock sits in a taller two-row center pill), so a single shared set
    /// of X/W values could never be correct for both skins at once. Every existing preset below
    /// keeps the original CBS-calibrated numbers as its default so none of them silently regress
    /// from this change -- only CollegeFootball27 (see below) uses the new layout.</summary>
    public double AwayScoreFxX { get; init; } = 0.35;
    public double AwayScoreFxW { get; init; } = 0.05;
    public double HomeScoreFxX { get; init; } = 0.58;
    public double HomeScoreFxW { get; init; } = 0.05;
    public double ClockFxX { get; init; } = 0.65;
    public double ClockFxW { get; init; } = 0.08;

    /// <summary>Tight crop directly under each team's name for possession sampling by brightness
    /// comparison (GameWatcher.SamplePossessionByUnderline), NOT color matching -- correction
    /// 2026-08-07: the possession signal on this scorebug revision is a thin underline beneath
    /// whichever team currently has the ball (brighter/lit) vs the other team's (dim), not a
    /// team-colored fill box the way PossessionFx*/SamplePossession assumed. Zero (both W/H) on
    /// older presets means "not calibrated for this preset" -- GameWatcher falls back to the
    /// legacy PossessionFx* color method in that case. Estimated crop from live screenshots
    /// (Auburn @ Georgia Tech, CBS skin) -- not pixel-measured, needs live tuning.</summary>
    public double AwayUnderlineFxX { get; init; }
    public double AwayUnderlineFxY { get; init; }
    public double AwayUnderlineFxW { get; init; }
    public double AwayUnderlineFxH { get; init; }
    public double HomeUnderlineFxX { get; init; }
    public double HomeUnderlineFxY { get; init; }
    public double HomeUnderlineFxW { get; init; }
    public double HomeUnderlineFxH { get; init; }

    /// <summary>Crop for the "PENALTY" decision overlay's "Against &lt;Team Name&gt;" text, and
    /// for the big full-screen scoring ribbon ("TOUCHDOWN"/"FIELD GOAL"/"SAFETY"). Promoted from
    /// hardcoded constants in GameWatcher.cs's _regions initializer to per-preset fields 2026-08-11
    /// -- both were calibrated only from CBS-skin screenshots and, unlike awayscore/homescore/clock,
    /// were never wired into ApplyScorebugPreset at all, so every non-CBS preset was silently
    /// reading CBS's screen coordinates for penalty and scoring-banner detection regardless of
    /// which skin was actually active. Every preset below defaults to the original CBS-calibrated
    /// numbers so none of them regress from this change.</summary>
    /// <summary>Crop for the pregame tunnel-walk chevron marker -- the white chevron shape
    /// flanking the center bowl/rivalry badge on the pregame scorebug (owner-confirmed present on
    /// every game, not just bowl games). Sampled by brightness (GameWatcher.
    /// SamplePregameEntranceFromWindow), NOT OCR text -- same technique as AwayUnderlineFx*/
    /// AwayTimeoutFx* for the same reason (it's a graphical shape, not a font glyph). Zero (FxW)
    /// means "not calibrated for this preset" -- GameStateEventHelper's quarter/down heuristic is
    /// the fallback in that case (see its ChevronMarkerFx*-independent condition).
    /// CAVEAT (same as every other crop in this file): eyeballed from one 2560x1440 screenshot
    /// (Rose Bowl, South Carolina @ Michigan pregame walkout, displayed/measured at 2000x1125),
    /// not pixel-measured against the raw image data -- a starting point needing live tuning
    /// against the actual game before fully trusting it, not an exact calibration.</summary>
    public double ChevronMarkerFxX { get; init; }
    public double ChevronMarkerFxY { get; init; }
    public double ChevronMarkerFxW { get; init; }
    public double ChevronMarkerFxH { get; init; }

    public double PenaltyAgainstFxX { get; init; } = 0.65;
    public double PenaltyAgainstFxY { get; init; } = 0.62;
    public double PenaltyAgainstFxW { get; init; } = 0.32;
    public double PenaltyAgainstFxH { get; init; } = 0.22;
    public double BannerFxX { get; init; } = 0.35;
    public double BannerFxY { get; init; } = 0.87;
    public double BannerFxW { get; init; } = 0.3;
    public double BannerFxH { get; init; } = 0.08;

    /// <summary>A revised scorebug layout ("v3") the owner identified from a fresh batch of live
    /// screenshots 2026-08-07 -- kept as its own preset rather than overwriting KamsCbsScorebug,
    /// per this file's whole point (swap presets, don't hand-edit coordinates every time the game
    /// updates its HUD). Possession here is read via the underline-brightness method, not color
    /// matching -- see AwayUnderlineFx*/HomeUnderlineFx* above for why. away=Auburn (left block),
    /// home=Georgia Tech (right block) in the calibrating screenshots, matching this app's
    /// existing "home = the user's own team" convention.</summary>
    public static readonly ScorebugPreset KamsCbsScorebugV3 = new()
    {
        Name = "Kam's CBSv3",
        BandFxY = 0.83, BandFxH = 0.14,
        // RESTORED 2026-08-12: this is the SAME color-match crop the original "Kam's CBS
        // Scorebug" preset had (0.89/0.848/0.095/0.104, "far more reliable than the underline
        // dashes" per that preset's own 2026-08-08 calibration note) -- it never actually broke,
        // it just got silently dropped when that preset was retired 2026-08-09 in favor of this
        // V3 preset, which only ever received the underline crop as a replacement. Independently
        // re-confirmed 2026-08-12 from 6 live Georgia/Florida screenshots (clearly distinct red
        // vs blue, no color-confusion risk): the rightmost down-and-distance box ("1st & 10",
        // "3rd & 5") is filled solid with whichever team currently has the ball's color -- same
        // BandFxY/BandFxH strip as V3 already uses, so the old crop should still land correctly.
        // GameWatcher.SamplePossessionFromWindow now tries this FIRST (falls back to the
        // underline method only when the color sample is inconclusive), reversing V3's original
        // underline-first order -- see that method's own comment.
        PossessionFxX = 0.89, PossessionFxY = 0.848, PossessionFxW = 0.095, PossessionFxH = 0.104,
        // Score/clock/penalty/banner values below were previously left unset and silently inherited
        // from this class's field defaults (which happen to equal these exact numbers, since CBS is
        // what those defaults were originally calibrated from). Made explicit 2026-08-11 so CBS
        // doesn't depend on an implicit fallback that could change out from under it -- see the
        // AwayScoreFx*/PenaltyAgainstFx*/BannerFx* doc comments above for why.
        AwayScoreFxX = 0.35, AwayScoreFxW = 0.05,
        HomeScoreFxX = 0.58, HomeScoreFxW = 0.05,
        ClockFxX = 0.65, ClockFxW = 0.08,
        PenaltyAgainstFxX = 0.65, PenaltyAgainstFxY = 0.62, PenaltyAgainstFxW = 0.32, PenaltyAgainstFxH = 0.22,
        BannerFxX = 0.35, BannerFxY = 0.87, BannerFxW = 0.3, BannerFxH = 0.08,
        // REVERTED 2026-08-12: an attempted recalibration off a live screenshot (see git history)
        // guessed a "white top-line" signal that pixel-sampling later proved wrong -- both team
        // blocks fluctuate through similar brightness ranges in that region (mostly reflecting
        // text density, not possession), so the new crop landed somewhere neither side ever cleared
        // the confidence margin, and possession stopped resolving AT ALL ("we haven't figured out
        // which team has the ball yet" on every play) -- worse than this original crop, which at
        // least sometimes read correctly. Back to the pre-2026-08-12 values until there's a
        // properly identified, pixel-verified signal (needs clean screenshots of BOTH a confirmed
        // home-possession AND a confirmed away-possession frame to isolate what actually differs).
        AwayUnderlineFxX = 0.145, AwayUnderlineFxY = 0.975, AwayUnderlineFxW = 0.075, AwayUnderlineFxH = 0.012,
        HomeUnderlineFxX = 0.395, HomeUnderlineFxY = 0.975, HomeUnderlineFxW = 0.11, HomeUnderlineFxH = 0.012,
        AwayTimeoutFxX = 0.15, AwayTimeoutFxY = 0.895, AwayTimeoutFxW = 0.08, AwayTimeoutFxH = 0.025,
        // Placeholder mirror (see HomeTimeoutFx*'s doc comment): AwayUnderlineFxX (0.145) ->
        // HomeUnderlineFxX (0.395) is a +0.25 offset on this preset; applied to AwayTimeoutFxX.
        HomeTimeoutFxX = 0.40, HomeTimeoutFxY = 0.895, HomeTimeoutFxW = 0.08, HomeTimeoutFxH = 0.025,
        // ChevronMarkerFx* -- added 2026-08-12 (owner report: pregame walkout/chevron screen is the
        // SAME cutscene regardless of which live-play scorebug skin (Kam's CBS vs default CFB27
        // HUD) is selected, so CollegeFootball27's chevron crop -- calibrated from the Rose Bowl
        // walkout screenshot, see that preset's own comment -- applies here unchanged too, not just
        // to that one preset. Copied as-is rather than re-eyeballed.
        ChevronMarkerFxX = 0.4275, ChevronMarkerFxY = 0.849, ChevronMarkerFxW = 0.0225, ChevronMarkerFxH = 0.04,
    };

    /// <summary>Third scorebug layout, calibrated 2026-08-08 from two 1920x1080 screenshots the
    /// owner sent (Texas @ Oklahoma kickoff; Purdue @ Indiana 1st &amp; 10) -- one an actual PS5
    /// Remote Play-style capture, one a PC capture with an on-screen hardware monitor overlay in
    /// the corner (unrelated to the scorebug itself, just confirms this is the same in-game UI at
    /// the same screen-space position regardless of capture path). IMPORTANT: these coordinates
    /// are eyeballed from the screenshots, not pixel-measured against the actual source images --
    /// same caveat as AwayTimeoutFx*/AwayUnderlineFx* above ("estimated crop... needs live
    /// tuning"). Treat this preset as a starting point to refine against a live game, not a
    /// finished calibration. Possession here uses the underline-brightness method (same reasoning
    /// as V3): this scorebug style shows a bright underline beneath the team currently on offense
    /// rather than a team-colored fill box.</summary>
    /// <summary>Overwritten 2026-08-10 (was "College Football 27 Console" / "Console/Remote Play
    /// v1") -- the game update changed the default scorebug to the same layout on PC and console
    /// both, per 7 live screenshots the owner sent (Georgia @ LSU, Alabama @ LSU, multiple game
    /// states: kickoff, pregame team-select, 2nd/3rd down live play). Renamed to drop "Console"
    /// since it's no longer console-specific. Band widened taller than the old console preset
    /// (BandFxH 0.10 -> 0.075 actually shrunk slightly, see below) to cover BOTH the team-color
    /// score row AND the down-and-distance ("2nd &amp; 5") row rendered just below it in this
    /// skin's taller two-row center pill -- confirmed from the screenshots that "1st | 6:11 |
    /// [ball-position]" and "2nd &amp; 5" are two separate lines inside one pill, not one line.
    /// AwayScoreFx*/HomeScoreFx*/ClockFx* are NEW per-preset fields (see their declaration above)
    /// -- this skin's score digits sit flush against each team's color block and the clock sits
    /// centered in the pill, both at very different X positions than the CBS skin's crops.
    /// IMPORTANT CAVEAT, same as every preset in this file: coordinates below are eyeballed from
    /// screenshots (2560x1440, displayed/measured at 2000x1125), not pixel-measured against the
    /// raw image data -- treat as a strong starting point needing live tuning, not exact.
    /// Possession -- calibrated 2026-08-10 from two comparison screenshots: one where LSU (the
    /// right/home-side team) had the ball, showing a small ball-position number + arrow ("26▲")
    /// rendered inside the center pill right next to the home score block; one where Georgia
    /// (left/away-side team) was kicking off, showing the equivalent "35▼" indicator on the
    /// OPPOSITE (away) side of the pill instead. Confirms this indicator flips sides by team the
    /// same way the CBS skin's underline did, just repositioned -- reused
    /// AwayUnderlineFx*/HomeUnderlineFx* and GameWatcher.SamplePossessionByUnderline's existing
    /// brightness-comparison method as-is (no new detection code needed), just pointed at this
    /// skin's arrow-slot position instead of an actual underline. CAVEAT: only 2 data points (one
    /// of them a kickoff, not a live snap) -- confirm this still flips correctly during a normal
    /// live play before trusting it fully.
    /// Timeouts -- calibrated 2026-08-10 from a tight crop of LSU's (home) block showing 3 small
    /// lit pill/dash segments under "TIGERS" (3 timeouts remaining, none used). AwayTimeoutFx*
    /// below is MIRRORED from that home-side measurement, not independently confirmed -- Georgia's
    /// (away) block has logo-then-name-then-score ordering vs LSU's score-then-name-then-logo, so
    /// the mirror assumption is shakier than a direct measurement would be. Get a Georgia-side
    /// close-up (ideally with a timeout already used, e.g. 2/3 remaining, to confirm the
    /// lit-vs-unlit visual difference too) to firm this up.
    /// HomeTimeoutFx* (added 2026-08-11) is a SECOND mirror -- taking the already-mirrored
    /// AwayTimeoutFxX above and mirroring it back across using the AwayUnderlineFxX/
    /// HomeUnderlineFxX delta, rather than restoring the original real LSU measurement (which
    /// wasn't kept). Since the real data point for THIS preset was actually the home side, this
    /// double-mirrored guess is probably the shakiest of the three presets' Home placeholders --
    /// prioritize re-measuring this one directly from a live screenshot.</summary>
    public static readonly ScorebugPreset CollegeFootball27 = new()
    {
        Name = "College Football 27",
        BandFxY = 0.870, BandFxH = 0.075,
        AwayScoreFxX = 0.395, AwayScoreFxW = 0.035,
        HomeScoreFxX = 0.595, HomeScoreFxW = 0.035,
        ClockFxX = 0.465, ClockFxW = 0.06,
        // BannerFx* -- calibrated 2026-08-11 from a live CFB 27 TOUCHDOWN screenshot (Montana St
        // kickoff return, screenshot batch in OneDrive\Pictures\Screenshots 1\, #490). Unlike the
        // CBS skin's separate full-screen ribbon, this skin's TOUCHDOWN/FIELD GOAL/SAFETY banner
        // just replaces the ENTIRE persistent scorebug pill in place (team logos still visible on
        // both ends, white text fills the whole center where the score/clock normally sit) -- so
        // the crop needs to span the full bar width, not a narrow center box like CBS's. Reuses
        // the same BandFxY/BandFxH vertical band as the rest of the bug since it's the same strip.
        // PenaltyAgainstFx* -- still NOT calibrated from a CFB 27 screenshot (none seen showing a
        // live penalty-decision overlay in this batch either). Left equal to the CBS-calibrated
        // numbers as an explicit placeholder (same "clone as a starting point, flag it" convention
        // as CollegeFootball26Console below) rather than inventing new coordinates with no basis --
        // get a live CFB 27 penalty-overlay screenshot to replace this for real.
        PenaltyAgainstFxX = 0.65, PenaltyAgainstFxY = 0.62, PenaltyAgainstFxW = 0.32, PenaltyAgainstFxH = 0.22,
        BannerFxX = 0.15, BannerFxY = 0.870, BannerFxW = 0.70, BannerFxH = 0.075,
        AwayUnderlineFxX = 0.430, AwayUnderlineFxY = 0.876, AwayUnderlineFxW = 0.045, AwayUnderlineFxH = 0.024,
        HomeUnderlineFxX = 0.550, HomeUnderlineFxY = 0.876, HomeUnderlineFxW = 0.045, HomeUnderlineFxH = 0.024,
        AwayTimeoutFxX = 0.253, AwayTimeoutFxY = 0.925, AwayTimeoutFxW = 0.091, AwayTimeoutFxH = 0.012,
        // Double-mirrored placeholder -- see the long Timeouts comment above. AwayUnderlineFxX
        // (0.430) -> HomeUnderlineFxX (0.550) is a +0.12 offset; applied to AwayTimeoutFxX.
        HomeTimeoutFxX = 0.373, HomeTimeoutFxY = 0.925, HomeTimeoutFxW = 0.091, HomeTimeoutFxH = 0.012,
        // ChevronMarkerFx* -- calibrated 2026-08-12 from the Rose Bowl pregame walkout screenshot
        // (see ChevronMarkerFx*'s doc comment above for the full caveat). Left chevron only --
        // one clear signal is enough, and this crop is deliberately tight around just the shape's
        // brightest stroke so a near-miss crop reads dim instead of accidentally catching the
        // badge or team-color blocks next to it.
        ChevronMarkerFxX = 0.4275, ChevronMarkerFxY = 0.849, ChevronMarkerFxW = 0.0225, ChevronMarkerFxH = 0.04,
    };

    /// <summary>Added 2026-08-09 for the owner's separate CFB 26 PS/Xbox console capture, kept as
    /// its own preset alongside the CFB 27 console one rather than overwriting it, same
    /// "swap presets, don't hand-edit coordinates" rationale as every preset above. IMPORTANT:
    /// cloned from ConsoleScorebugV1's coordinates as a starting point only -- I was not given
    /// the actual CFB 26 screenshot's pixel data to calibrate against (only an unrelated app
    /// screenshot came through in the request), so these crop boxes are UNVERIFIED for CFB 26's
    /// actual HUD and need live tuning against a real CFB 26 console screenshot before relying on
    /// this preset, same caveat as ConsoleScorebugV1 originally shipped with.</summary>
    public static readonly ScorebugPreset CollegeFootball26Console = new()
    {
        Name = "College Football 26 Console",
        BandFxY = 0.855, BandFxH = 0.10,
        AwayUnderlineFxX = 0.20, AwayUnderlineFxY = 0.965, AwayUnderlineFxW = 0.09, AwayUnderlineFxH = 0.015,
        HomeUnderlineFxX = 0.565, HomeUnderlineFxY = 0.965, HomeUnderlineFxW = 0.09, HomeUnderlineFxH = 0.015,
        AwayTimeoutFxX = 0.20, AwayTimeoutFxY = 0.905, AwayTimeoutFxW = 0.08, AwayTimeoutFxH = 0.025,
        // Placeholder mirror (see HomeTimeoutFx*'s doc comment): AwayUnderlineFxX (0.20) ->
        // HomeUnderlineFxX (0.565) is a +0.365 offset; applied to AwayTimeoutFxX. Stacks on top of
        // this preset's existing "cloned, unverified for CFB 26" caveat above.
        HomeTimeoutFxX = 0.565, HomeTimeoutFxY = 0.905, HomeTimeoutFxW = 0.08, HomeTimeoutFxH = 0.025,
    };

    public static readonly List<ScorebugPreset> AllPresets = new() { KamsCbsScorebugV3, CollegeFootball27, CollegeFootball26Console };

    /// <summary>Old preset names that got renamed, mapped to their current Name -- a returning
    /// user's saved scorebug_preset.txt (see ConfigStore.LoadScorebugPresetName) still has the
    /// old string on disk, and GetByName's exact-match lookup would otherwise silently fall back
    /// to KamsCbsScorebugV3 instead of the console preset they actually had selected. Add an
    /// entry here every time a preset's Name changes.</summary>
    static readonly Dictionary<string, string> LegacyNameAliases = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Console/Remote Play v1"] = "College Football 27",
        ["College Football 27 Console"] = "College Football 27",
    };

    public static ScorebugPreset GetByName(string? name)
    {
        if (name != null && LegacyNameAliases.TryGetValue(name, out var currentName)) name = currentName;
        return AllPresets.Find(p => p.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) ?? KamsCbsScorebugV3;
    }
}
