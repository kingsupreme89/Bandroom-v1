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

    public static readonly ScorebugPreset KamsCbsScorebug = new()
    {
        Name = "Kam's CBS Scorebug",
        BandFxY = 0.83, BandFxH = 0.14,
        PossessionFxX = 0.65, PossessionFxY = 0.85, PossessionFxW = 0.14, PossessionFxH = 0.09,
        AwayTimeoutFxX = 0.15, AwayTimeoutFxY = 0.895, AwayTimeoutFxW = 0.08, AwayTimeoutFxH = 0.025,
    };

    public static readonly List<ScorebugPreset> AllPresets = new() { KamsCbsScorebug };

    public static ScorebugPreset GetByName(string? name) =>
        AllPresets.Find(p => p.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) ?? KamsCbsScorebug;
}
