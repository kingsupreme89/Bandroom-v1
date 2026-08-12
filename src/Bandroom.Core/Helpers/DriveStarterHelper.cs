namespace Bandroom.Core.Helpers;

/// <summary>Fires "Drive Starter" when a fresh possession begins (not from a kickoff,
/// not from a turnover — those have their own events). Detected as down 1 with
/// a new possession that wasn't flagged as turnover or kickoff.
///
/// FIXED 2026-08-11: "Skip if it's a kickoff" used to check only Current.IsKickoff, a single-
/// tick gated-region signal that goes false again on the very tick Down first resolves to a
/// real value after the return (same root cause as GameWatcher's structural-turnover guard --
/// see its _awaitingPostKickoffSnap comment). That meant this check never actually caught the
/// receiving team's first snap after a kickoff -- DefenseFirstDownHelper already owns that exact
/// moment with its own cue, so this evaluator was silently double-firing "Drive Starter"
/// alongside it on every kickoff return, not just the opening one (which happened to be masked
/// by the separate Previous.Down==0 guard below, since Previous is still the pregame placeholder
/// for that one specific kickoff). Tracks the same sticky "awaiting first snap after kickoff"
/// state as GameWatcher instead of trusting the single-tick flag.</summary>
public sealed class DriveStarterHelper : IRuleEvaluator
{
    bool _awaitingPostKickoffSnap;

    public TriggerEvent? Evaluate(GameState state)
    {
        // Read the guard's value from entering this tick before updating it, same ordering
        // reasoning as GameWatcher's own copy of this pattern -- the tick Down first resolves is
        // exactly the tick that must still be suppressed.
        bool wasAwaitingPostKickoffSnap = _awaitingPostKickoffSnap;
        if (state.Current.IsKickoff)
            _awaitingPostKickoffSnap = true;
        else if (_awaitingPostKickoffSnap && state.Current.Down is >= 1 and <= 4)
            _awaitingPostKickoffSnap = false;

        // Must be a change of possession to a new 1st down
        if (!state.Delta.NewPossession || state.Current.Down != 1)
            return null;

        // Skip if it's a kickoff or turnover — those fire their own events
        if (state.Current.IsKickoff || state.Current.IsTurnover || wasAwaitingPostKickoffSnap)
            return null;

        // Skip if this tick also looks like a mid-drive first-down conversion
        // (Delta.WasFirstDown: Current.Down==1 && Previous.Down>1). A real change of
        // possession and an earned first down should never both be true on the same
        // tick, but NewPossession comes from a separate OCR color sample than Down and
        // can misread true for one tick right as the "1ST DOWN" banner covers the
        // possession-indicator region (STATE_MACHINE_ANALYSIS.md Race #1). Reported live
        // as "away team got 1st down is also linking to [Drive Starter]" -- the earned
        // first down cue was correct, this evaluator's false-positive was the second one
        // riding along on the same noisy tick. WasFirstDown is the more specific signal
        // (it also requires a real prior down > 1), so it wins the tie.
        if (state.Delta.WasFirstDown)
            return null;

        // Skipping opening snap (Previous.Down == 0 means no prior state)
        if (state.Previous.Down == 0)
            return null;

        // MERGED 2026-08-11 (live owner call): this used to fire its own "Offense: 1st Down
        // After Punt" card, separate from a mid-drive earned first down -- owner reported it
        // reads as confusing (a fresh drive's 1st & 10 is still just "1st down" to a band) and
        // it kept ending up unassigned/mis-assigned as a result, plus fired instead of playing
        // nothing while the real 1st-down cue got delayed. Now just fires the SAME event as an
        // earned first down (same short/long split FirstDownHelper uses), so it always has
        // whichever song "1st Down" already has assigned instead of being its own silent card.
        // See WebMainForm.RenamedEventKeyAliases for the old-key fallback so a profile that
        // already had a song assigned specifically under the old "1st Down After Punt" key still
        // gets used if "Offense: Earned First Down" itself is unassigned.
        if (state.UserHasPossession)
        {
            return new TriggerEvent
            {
                EventKey = state.Current.YardsToGo <= 5 ? "Offense: Earned First Down Short" : "Offense: Earned First Down",
                Volume = 70,
                IsEarnedBigEvent = false
            };
        }

        // Renamed 2026-08-11 (owner audit call), same session as the offense side's rename --
        // "Defense: After Punt" for the same clarity reason. Also made home-only-always (see
        // WebMainForm.HomeOnlyAlwaysEventKeys) per the owner's explicit call: this never plays
        // for the away side, Big Game or not.
        return new TriggerEvent
        {
            EventKey = "Defense: After Punt",
            Volume = 70,
            IsEarnedBigEvent = false
        };
    }
}