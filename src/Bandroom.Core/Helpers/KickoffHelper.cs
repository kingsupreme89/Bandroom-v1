namespace Bandroom.Core.Helpers;

/// <summary>Fires kickoff events. Which variant fires depends on the quarter
/// (opening vs second-half) and possession side (kicking vs receiving).
/// All kickoff events fire once on the transition from not-kickoff to kickoff.
/// 
/// BUG FIX 2026-08-09: Previous.IsKickoff was the edge-trigger guard, but "situation"
/// is a gated region (EventGatedRegions in GameWatcher.cs) -- its Last value only clears
/// when the "down" region changes, not when the kickoff banner disappears. During a kickoff
/// sequence the down doesn't change (it stays on "1st & 10" through the whole broadcast
/// cutaway), so Previous.IsKickoff stayed true across every tick after the first one,
/// and KickoffHelper could never fire again. Switched to a self-tracked _didFire flag that
/// resets when IsKickoff goes false, which is immune to both the first-tick guard and the
/// gated-region issue.</summary>
public sealed class KickoffHelper : IRuleEvaluator
{
    bool _didFire;

    // FIXED 2026-08-09: Quarter == 1/3 alone doesn't mean "the FIRST kickoff of that quarter" --
    // e.g. a Q1 pick-six followed by the ensuing kickoff is still Quarter == 1, so every kickoff
    // for the rest of the opening quarter kept re-firing "Other: Opening Kickoff", and likewise
    // any later Q3 kickoff re-fired "Other: Second-Half Kickoff". These now gate on "haven't
    // fired this variant yet THIS GAME" (see GameWatcher.Start(), which now rebuilds evaluators
    // fresh per game so this doesn't leak into the next one either) so only the true first
    // kickoff of each half gets the special cue; every kickoff after that falls through to the
    // ordinary kicking/receiving branches below.
    bool _openingKickoffFired;
    bool _secondHalfKickoffFired;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsKickoff)
        {
            _didFire = false;
            return null;
        }

        if (_didFire)
            return null;

        _didFire = true;

        // Opening kickoff: 1st quarter, first time kickoff is seen this game
        if (state.Current.Quarter == 1 && !_openingKickoffFired)
        {
            _openingKickoffFired = true;
            return new TriggerEvent
            {
                EventKey = "Other: Opening Kickoff",
                Volume = 90,
                IsEarnedBigEvent = true
            };
        }

        // Second-half kickoff: 3rd quarter, first time kickoff is seen this half
        if (state.Current.Quarter == 3 && !_secondHalfKickoffFired)
        {
            _secondHalfKickoffFired = true;
            return new TriggerEvent
            {
                EventKey = "Other: Second-Half Kickoff",
                Volume = 90,
                IsEarnedBigEvent = true
            };
        }

        // RESTORED 2026-08-11 (owner report, live game): a TD scored fired its own cue, but
        // neither "Offense: PAT Made" nor any kickoff cue followed it -- the 2026-08-10 removal
        // below had assumed PAT Made was a reliable enough "kickoff's coming" proxy to not need
        // its own cue, but PAT Made depends on OCR catching the "PAT GOOD" situation text
        // (FieldGoalPATHelper's `state.Current.IsPAT` check) on the right tick, which isn't
        // guaranteed -- a missed PAT read means the kickoff after ANY mid-game score goes
        // silent. Now fires a single generic "Other: Kickoff" cue on every kickoff transition
        // that isn't the opening/second-half one, independent of whether PAT fired.
        //
        // Old removed comment, kept for context: "every OTHER kickoff (after any mid-game score)
        // used to fire 'Other: Kickoff on Kick (Receiving/Kicking)' here. Owner's explicit call:
        // these collided with PAT GOOD detection ... weren't worth the noise." Reversed by the
        // owner this session -- simplified back to one plain cue (not the old receiving/kicking
        // split) rather than reintroducing that complexity.
        return new TriggerEvent
        {
            EventKey = "Other: Kickoff",
            Volume = 80,
            IsEarnedBigEvent = false
        };
    }
}