namespace Bandroom.Core.Helpers;

/// <summary>Fires "Offense: After Opening Kick" on the SAME condition DefenseFirstDownHelper
/// fires "Defense: After Opening Kick" -- a separate evaluator, not a branch inside that one, so
/// both fire on the same tick (same pattern as DefenseSecondDownShortHelper/OffenseDownHelper's
/// 2nd-down-short pairing). This one routes to the receiving team's offense (they have the ball,
/// fresh 1st & 10 right after the return); DefenseFirstDownHelper's cue routes to the kicking
/// team's defense.
///
/// Owner rule 2026-08-12 (live game, Big Game): whoever has the ball is the bigger moment here,
/// full 100 -- DefenseFirstDownHelper's counterpart is the ducked side at 60, same balance as
/// "Offense/Defense: Second Down Short".
///
/// Tracking logic deliberately duplicated from DefenseFirstDownHelper rather than shared -- same
/// standalone-evaluator shape every other paired Offense/Defense helper in this codebase uses
/// (see DefenseSecondDownShortHelper's own comment), so this evaluator's firing doesn't depend on
/// DefenseFirstDownHelper's internal state.</summary>
public sealed class OffenseAfterOpeningKickHelper : IRuleEvaluator
{
    bool _awaitingFirstSnap;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.IsKickoff)
        {
            _awaitingFirstSnap = true;
            return null;
        }

        if (!_awaitingFirstSnap)
            return null;

        _awaitingFirstSnap = false;
        if (state.Current.Down != 1)
            return null;

        return new TriggerEvent
        {
            EventKey = "Offense: After Opening Kick",
            Volume = 100,
            IsEarnedBigEvent = false
        };
    }
}
