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

    // FIXED 2026-08-12 (audit finding): IsTouchdown (from the non-sticky "situation" OCR region)
    // and HomeScore/AwayScore (from a separately-sticky OCR region, see GameWatcher.RouteEngineTick)
    // are two independently-timed reads, same class of split-tick race as the down/yards-to-go gap
    // DownDistanceBuffer exists for elsewhere in this codebase. The defense-TD detection below is
    // score-delta-based specifically because the "TOUCHDOWN" banner can get missed for a defensive
    // score (goes straight to the ensuing kickoff) -- but that means the banner can ALSO arrive
    // BEFORE the scoreboard catches up, not just after. On that ordering, the old code fired
    // "Offense: Touchdown Scored" off the banner immediately (no score delta yet to prove
    // otherwise), and then the score-delta branch correctly fired "Defense: Touchdown Scored" a
    // tick or two later for the SAME real event once the scoreboard resolved -- two contradictory
    // cues for one score, wrong side credited first. Now: when the banner edge fires with no score
    // delta yet visible, buffer briefly instead of assuming offense, so the scoreboard has a chance
    // to prove it's actually a defensive score before this fires anything.
    bool _awaitingOffenseScoreConfirm;
    int _pendingTicksRemaining;
    const int MaxPendingTicks = 4; // ~1000ms at the 250ms OCR poll interval

    public bool CanFire(GameState state) => state.Current.IsTouchdown
        || state.Previous.IsTouchdown
        || _awaitingOffenseScoreConfirm
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
            // The score just resolved to a defensive TD -- if an offense fire was pending on an
            // earlier tick's banner edge for this exact same event, abandon it permanently. This
            // is the actual close of the race: without this, the pending timeout below would still
            // fire a contradictory "Offense: Touchdown Scored" once MaxPendingTicks elapsed.
            _awaitingOffenseScoreConfirm = false;
            return new TriggerEvent
            {
                EventKey = "Defense: Touchdown Scored",
                Volume = state.Current.BigGame ? 100 : 85,
                IsEarnedBigEvent = true
            };
        }

        if (_awaitingOffenseScoreConfirm)
        {
            // Any score movement at all that ISN'T the defensive pattern above (e.g. the
            // possessing side's own +6/+7/+8 confirming a normal offensive TD) settles the race in
            // offense's favor immediately, no need to wait out the rest of the buffer.
            bool scoreResolved = homeDelta != 0 || awayDelta != 0;
            if (!scoreResolved && --_pendingTicksRemaining > 0)
                return null; // keep waiting for the scoreboard to confirm which side actually scored

            _awaitingOffenseScoreConfirm = false;
            return MakeOffenseEvent(state);
        }

        // Offense touchdown -- banner-edge triggered.
        if (!state.Current.IsTouchdown || state.Previous.IsTouchdown)
            return null;

        // A defense TD (handled above, possibly on an earlier tick) can have IsTouchdown catch up
        // true later once the banner finally appears -- don't double-fire an offense cue for
        // points already attributed to a defensive score.
        if (state.Current.HomeScore == _lastDefenseTdHomeScore && state.Current.AwayScore == _lastDefenseTdAwayScore)
            return null;

        // Banner is up but the scoreboard hasn't moved yet THIS tick -- can't yet rule out this
        // being a defensive score whose score-delta simply hasn't landed. Buffer briefly rather
        // than assuming offense outright (see class-level comment for the race this closes).
        if (homeDelta == 0 && awayDelta == 0)
        {
            _awaitingOffenseScoreConfirm = true;
            _pendingTicksRemaining = MaxPendingTicks;
            return null;
        }

        return MakeOffenseEvent(state);
    }

    static TriggerEvent MakeOffenseEvent(GameState state) => new()
    {
        EventKey = "Offense: Touchdown Scored",
        Volume = state.Current.BigGame ? 100 : 85,
        IsEarnedBigEvent = true
    };
}