namespace Bandroom.Core.Helpers;

/// <summary>Owner request 2026-08-19: a "Defense" counterpart to FirstDownHelper's "Offense:
/// Earned First Down"/"...Short" -- the defending team's own cue for "the other team's offense
/// just converted a first down against us," same dual-fire pairing shape as the existing
/// Offense/Defense Second/Third Down cards (ducked at 60/50 here vs the offense side's 80/90,
/// same balance convention: the team that just earned the down is the bigger moment).
///
/// Mirrors FirstDownHelper's exact logic (buffered 4th-down conversion-vs-punt disambiguation
/// included) rather than sharing code with it -- same "one standalone evaluator per side" pattern
/// used everywhere else in this file (OffenseSecondDownHelper/DefenseThirdDownHelper/etc) so this
/// doesn't risk touching FirstDownHelper's own already-tuned behavior. EventKey auto-routes to
/// whichever side is currently on DEFENSE (not the side that earned the down) via the existing
/// "Defense:" prefix convention -- no explicit side logic needed here, same as every other
/// Defense:* cue in this codebase.</summary>
public sealed class DefenseFirstDownAllowedHelper : IRuleEvaluator
{
    bool _awaitingFourthDownPossessionConfirm;
    int _pendingTicksRemaining;
    bool _possessionAwayAtBufferStart;
    const int MaxPendingTicks = 7; // ~1750ms at the 250ms OCR poll interval -- mirrors FirstDownHelper

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (_awaitingFourthDownPossessionConfirm)
        {
            if (state.Delta.NewPossession)
            {
                _awaitingFourthDownPossessionConfirm = false;
                return null;
            }
            if (--_pendingTicksRemaining > 0)
                return null; // keep waiting

            _awaitingFourthDownPossessionConfirm = false;

            if (state.Current.PossessionAway != _possessionAwayAtBufferStart)
                return null;

            return MakeDefenseFirstDownEvent(state);
        }

        // Must be a fresh first down (not the opening snap)
        if (!state.Delta.WasFirstDown || state.Previous.Down == 0)
            return null;

        // A turnover also resets Down to 1 for the new offense -- that's a new drive by
        // possession change, not a conversion earned mid-drive. Same guard FirstDownHelper uses.
        if (state.Delta.NewPossession)
            return null;

        // 3rd-down conversions get their own more specific "Offense: 3rd Down Conversion" cue on
        // the offense side; mirror FirstDownHelper's choice to defer here too so this doesn't
        // double up against a future defense-side conversion-specific cue.
        if (state.Previous.Down == 3)
            return null;

        // Ambiguous case: Down just reset to 1 immediately after a 4th down -- could be a genuine
        // conversion (fire) or a punt/turnover-on-downs whose possession flip hasn't caught up
        // yet (defer). Same buffer-then-decide shape as FirstDownHelper.
        if (state.Previous.Down == 4)
        {
            _awaitingFourthDownPossessionConfirm = true;
            _pendingTicksRemaining = MaxPendingTicks;
            _possessionAwayAtBufferStart = state.Current.PossessionAway;
            return null;
        }

        return MakeDefenseFirstDownEvent(state);
    }

    static TriggerEvent MakeDefenseFirstDownEvent(GameState state)
    {
        if (state.Current.YardsToGo <= 5)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Earned First Down Short",
                Volume = 50,
                IsEarnedBigEvent = false
            };
        }

        return new TriggerEvent
        {
            EventKey = "Defense: Earned First Down",
            Volume = 60,
            IsEarnedBigEvent = false
        };
    }
}
