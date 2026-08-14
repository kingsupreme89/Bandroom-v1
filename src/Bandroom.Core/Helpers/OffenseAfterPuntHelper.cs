namespace Bandroom.Core.Helpers;

/// <summary>Added 2026-08-13 (owner report, real game log: "needed an event that was the away bg
/// off after punt"). DriveStarterHelper already fires "Offense: Earned First Down"/"...Short" for
/// a fresh drive start when it's the USER's own team (UserHasPossession) taking over, and
/// "Defense: After Punt" (home-only, see WebMainForm.HomeOnlyAlwaysEventKeys) when it's the other
/// side -- but that second branch only ever gives the home crowd a "we stopped/received them" cue.
/// It never gave the AWAY team's own band a hype cue for THEIR fresh drive starting, the exact
/// same "Offense: Earned First Down" moment the home side already gets when the shoe's on the
/// other foot. Separate standalone evaluator, same tick as DriveStarterHelper (same "separate
/// standalone evaluator" pattern as DefenseThirdDownShortHelper/BigEventHelper elsewhere in this
/// file) rather than a branch inside DriveStarterHelper, so it doesn't touch that file's own
/// existing behavior/tests at all -- this only ADDS the missing away-side fire, both keep firing
/// side-by-side same as any other Offense/Defense same-tick pairing in this codebase.
///
/// EventKey has no "Defense:"/"Offense:" side-flip subtlety to get wrong here -- "Offense: Earned
/// First Down" already auto-routes to whichever side currently has the ball (see OffenseDownHelper
/// header comment), so firing it for the non-user side here reaches the away band automatically
/// once WebMainForm.ResolveEventRouting processes it, no new routing rule needed.</summary>
public sealed class OffenseAfterPuntHelper : IRuleEvaluator
{
    bool _awaitingPostKickoffSnap;

    public TriggerEvent? Evaluate(GameState state)
    {
        // Same sticky post-kickoff tracking as DriveStarterHelper -- see that file's own comment
        // for why a single-tick IsKickoff check isn't enough to catch a kickoff return's first snap.
        bool wasAwaitingPostKickoffSnap = _awaitingPostKickoffSnap;
        if (state.Current.IsKickoff)
            _awaitingPostKickoffSnap = true;
        else if (_awaitingPostKickoffSnap && state.Current.Down is >= 1 and <= 4)
            _awaitingPostKickoffSnap = false;

        if (!state.Delta.NewPossession || state.Current.Down != 1)
            return null;

        if (state.Current.IsKickoff || state.Current.IsTurnover || wasAwaitingPostKickoffSnap)
            return null;

        // Same tie-break as DriveStarterHelper -- a same-tick earned first down (WasFirstDown) is
        // the more specific signal and wins.
        if (state.Delta.WasFirstDown)
            return null;

        if (state.Previous.Down == 0)
            return null;

        // Only the case DriveStarterHelper's own Offense branch DOESN'T already cover: the OTHER
        // side (not the user/home team) just started this fresh drive.
        if (state.UserHasPossession)
            return null;

        return new TriggerEvent
        {
            EventKey = state.Current.YardsToGo <= 5 ? "Offense: Earned First Down Short" : "Offense: Earned First Down",
            Volume = 70,
            IsEarnedBigEvent = false
        };
    }
}
