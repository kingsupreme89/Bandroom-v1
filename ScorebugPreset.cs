namespace SupremeStadiumSoundSelector;

/// <summary>A named crop-position preset for the score-bug OCR regions in GameWatcher, so a
/// different broadcast skin (or a completely different game/HUD down the line) can be swapped
/// in via dropdown instead of re-deriving fractional coordinates from scratch each time.
/// "Kam's CBS Scorebug" is the CBS Sports skin calibrated this session -- BandFxY/BandFxH cover
/// the full-width down/situation/quarter capture band, PossessionFx* is the separate tight box
/// possession-color sampling still needs (see GameWatcher.SamplePossessionFromWindow).</summary>
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

    public static readonly ScorebugPreset KamsCbsScorebug = new()
    {
        Name = "Kam's CBS Scorebug",
        BandFxY = 0.83, BandFxH = 0.14,
        // RECALIBRATED 2026-08-08 from a live "3rd & 12" screenshot: the old PossessionFx* box
        // (0.65/0.85/0.14/0.09) landed somewhere in the yardline/down-distance text area, not
        // reliably on team-colored fill -- and this is the DEFAULT preset (no persisted override
        // picks V3's underline method instead), so this box was silently deciding possession for
        // every game using default settings. The rightmost down-and-distance box (e.g. "3rd & 12")
        // is filled solid with whichever team is currently on offense's color -- far more reliable
        // than the underline dashes (both teams here are orange-heavy, near-identical hue) or the
        // old crop's location. Targets the right ~10% of the band, inset from the rounded corners.
        PossessionFxX = 0.89, PossessionFxY = 0.848, PossessionFxW = 0.095, PossessionFxH = 0.104,
        AwayTimeoutFxX = 0.15, AwayTimeoutFxY = 0.895, AwayTimeoutFxW = 0.08, AwayTimeoutFxH = 0.025,
    };

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

    public static readonly List<ScorebugPreset> AllPresets = new() { KamsCbsScorebug, KamsCbsScorebugV3 };

    public static ScorebugPreset GetByName(string? name) =>
        AllPresets.Find(p => p.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) ?? KamsCbsScorebug;
}
