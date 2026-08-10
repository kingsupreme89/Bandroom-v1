namespace SupremeStadiumSoundSelector;

/// <summary>A named crop-position preset for the score-bug OCR regions in GameWatcher, so a
/// different broadcast skin (or a completely different game/HUD down the line) can be swapped
/// in via dropdown instead of re-deriving fractional coordinates from scratch each time.
/// BandFxY/BandFxH cover the full-width down/situation/quarter capture band, PossessionFx* is
/// the separate tight box possession-color sampling still needs (see
/// GameWatcher.SamplePossessionFromWindow) -- only kept by the older presets below; newer ones
/// use the underline-brightness method instead (see AwayUnderlineFx*/HomeUnderlineFx*).
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
    /// NOT OCR text -- these are graphical dashes, not font glyphs. Only away is tracked because
    /// TimeoutHelper only ever reads AwayTimeoutsRemaining, and this app always treats the user's
    /// own team as home (UserIsHome is hardcoded true in WebMainForm.SetGameTeamsFromWeb), so
    /// "away" here always means "the opponent" by this app's existing design convention.
    /// Estimated crop, not pixel-measured -- treat as a starting point needing live tuning.</summary>
    public double AwayTimeoutFxX { get; init; }
    public double AwayTimeoutFxY { get; init; }
    public double AwayTimeoutFxW { get; init; }
    public double AwayTimeoutFxH { get; init; }

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
        AwayUnderlineFxX = 0.145, AwayUnderlineFxY = 0.975, AwayUnderlineFxW = 0.075, AwayUnderlineFxH = 0.012,
        HomeUnderlineFxX = 0.395, HomeUnderlineFxY = 0.975, HomeUnderlineFxW = 0.11, HomeUnderlineFxH = 0.012,
        AwayTimeoutFxX = 0.15, AwayTimeoutFxY = 0.895, AwayTimeoutFxW = 0.08, AwayTimeoutFxH = 0.025,
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
    /// Possession/timeout crops (AwayUnderlineFx*/HomeUnderlineFx*/AwayTimeoutFx*) are
    /// DELIBERATELY LEFT UNCALIBRATED (zero) for this preset -- none of the 7 screenshots show a
    /// timeout-dash row or an underline under either team's name the way the CBS skin has. This
    /// skin instead shows a small arrow ("▲") next to the ball-position number, which looks like
    /// it's this HUD's possession indicator, but that's a fundamentally different signal
    /// (position/shape of an arrow glyph, not underline brightness or a color-filled box) that
    /// GameWatcher has no sampling method for yet. Possession detection will not work on this
    /// preset until either a live screenshot showing timeout dashes/underline surfaces, or new
    /// arrow-detection logic gets built -- don't guess coordinates for a signal that may not
    /// exist in this skin at all.</summary>
    public static readonly ScorebugPreset CollegeFootball27 = new()
    {
        Name = "College Football 27",
        BandFxY = 0.870, BandFxH = 0.075,
        AwayScoreFxX = 0.395, AwayScoreFxW = 0.035,
        HomeScoreFxX = 0.595, HomeScoreFxW = 0.035,
        ClockFxX = 0.465, ClockFxW = 0.06,
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
