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

        // Other kickoffs: user's team receiving = user has possession
        if (state.UserHasPossession)
        {
            return new TriggerEvent
            {
                EventKey = "Other: Kickoff on Kick (Receiving)",
                Volume = 75,
                IsEarnedBigEvent = false
            };
        }

        return new TriggerEvent
        {
            EventKey = "Other: Kickoff on Kick (Kicking)",
            Volume = 75,
            IsEarnedBigEvent = false
        };
    }
}