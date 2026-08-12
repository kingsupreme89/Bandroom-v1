namespace Bandroom.Core.Helpers;

/// <summary>Fires "Offense: Touchdown Scored" (banner-based) or "Defense: Touchdown Scored"
/// (pick-six / fumble-return, score-delta-based -- see the CORRECTED note below).</summary>
public sealed class TouchdownHelper : IRuleEvaluator
{
    // Remembers the exact scoreboard total the last defense TD was attributed to, so a
    // late-arriving "TOUCHDOWN" banner for the SAME score change (see Evaluate's comment) doesn't
    // fire a second, contradictory "Offense: Touchdown Scored" for it.
    int? _lastDefenseTdHomeScore;
    int? _lastDefenseTdAwayScore;

    public bool CanFire(GameState state) => state.Current.IsTouchdown
        || state.Current.HomeScore != state.Previous.HomeScore
        || state.Current.AwayScore != state.Previous.AwayScore;

    public TriggerEvent? Evaluate(GameState state)
    {
        int homeDelta = state.Current.HomeScore - state.Previous.HomeScore;
        int awayDelta = state.Current.AwayScore - state.Previous.AwayScore;

        // CORRECTED 2026-08-11 (owner audit call): a defense TD used to rely on catching
        // IsTouchdown true on the exact same tick NewPossession resolved -- owner's report: the
        // "TOUCHDOWN" banner doesn't stay on screen long for a defensive score specifically (goes
        // straight into the ensuing kickoff, unlike an offensive TD's longer PAT/2pt follow-up
        // that gives OCR more ticks a chance to catch the banner), so that tick can get missed
        // outright on a coarse poll. Now detected purely from the scoreboard instead -- same
        // technique SafetyHelper/FieldGoalPATHelper already use elsewhere in this codebase: the
        // side whose score jumps by exactly 6 points, while that same side is the one that did
        // NOT have the ball on the previous tick, can only be a defensive score (a normal
        // offensive touchdown drive scores for whoever's possessing).
        bool homeScoredWhileNotPossessing = homeDelta == 6 && state.Previous.PossessionAway;
        bool awayScoredWhileNotPossessing = awayDelta == 6 && !state.Previous.PossessionAway;
        if (homeScoredWhileNotPossessing || awayScoredWhileNotPossessing)
        {
            _lastDefenseTdHomeScore = state.Current.HomeScore;
            _lastDefenseTdAwayScore = state.Current.AwayScore;
            return new TriggerEvent
            {
                EventKey = "Defense: Touchdown Scored",
                Volume = state.Current.BigGame ? 100 : 85,
                IsEarnedBigEvent = true
            };
        }

        // Offense touchdown -- still banner-based; the owner only flagged the defensive case
        // above as unreliable, this path is unchanged.
        if (!state.Current.IsTouchdown || state.Previous.IsTouchdown)
            return null;

        // A defense TD (handled above, possibly on an earlier tick) can have IsTouchdown catch up
        // true later once the banner finally appears -- don't double-fire an offense cue for
        // points already attributed to a defensive score.
        if (state.Current.HomeScore == _lastDefenseTdHomeScore && state.Current.AwayScore == _lastDefenseTdAwayScore)
            return null;

        return new TriggerEvent
        {
            EventKey = "Offense: Touchdown Scored",
            Volume = state.Current.BigGame ? 100 : 85,
            IsEarnedBigEvent = true
        };
    }
}