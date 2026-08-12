namespace Bandroom.Core.Helpers;

/// <summary>Added 2026-08-11 (owner audit call): a distinct hype cue for converting a 3RD DOWN
/// specifically into a fresh 1st down -- a bigger deal than an ordinary 2nd-down conversion, so it
/// deserves its own card instead of blending into the generic "Offense: Earned First Down"/"...
/// Short" cues FirstDownHelper already fires for every conversion regardless of which down it came
/// from. Fires ALONGSIDE FirstDownHelper's own event on the same tick (separate evaluator, not a
/// branch inside it) -- same same-tick-pairing pattern as DefenseThirdDownShortHelper firing
/// alongside OffenseDownHelper, safe because EventRouter only dedupes identical EventKeys, not
/// different ones (see WebMainForm.OnEngineEventsDetected's same-tick layering).
///
/// Deliberately does NOT re-implement FirstDownHelper's 4th-down-punt-vs-conversion ambiguity
/// buffer -- Previous.Down == 3 is never ambiguous with a punt/turnover-on-downs the way
/// Previous.Down == 4 is (a punt only ever happens ON 4th down), so a plain NewPossession guard
/// is sufficient here.</summary>
public sealed class ThirdDownConversionHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Delta.WasFirstDown || state.Previous.Down != 3)
            return null;

        // A turnover on downs / defensive score off a 3rd-down snap also resets Down to 1 for
        // the new offense -- that's a possession change, not a conversion by the team that was
        // just facing 3rd down. Same guard FirstDownHelper/DefenseHelper use for the same reason.
        if (state.Delta.NewPossession)
            return null;

        return new TriggerEvent
        {
            EventKey = "Offense: 3rd Down Conversion",
            Volume = state.Current.BigGame ? 100 : 85,
            IsEarnedBigEvent = true
        };
    }
}
