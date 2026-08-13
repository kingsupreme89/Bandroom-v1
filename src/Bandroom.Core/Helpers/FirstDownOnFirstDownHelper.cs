namespace Bandroom.Core.Helpers;

/// <summary>Fires when the offense earns a fresh set of downs WITHOUT the down number ever
/// changing -- e.g. a long run/pass on 1st & 10 that gains well past the sticks, resulting in a
/// new 1st & 10 the ribbon reads identically to the old one. FirstDownHelper's WasFirstDown only
/// triggers on Down going from >1 to 1, so this exact case (1 -> 1) was previously invisible: the
/// down/distance ribbon looks the same whether it's still the original set of downs mid-play or a
/// brand new one, with no edge to key off.
///
/// Added 2026-08-12 (owner's own idea, live game): use the play clock box (GameWatcher's
/// "playclock" region / PlaySnapshot.IsPlayClockCounting) as an unambiguous play-boundary. It
/// counts down pre-snap, then shows "--" from the snap through the whole play (and any dead-ball
/// overlay) until the ribbon is ready for the next snap. Record Down/YardsToGo the moment it stops
/// counting (the situation right before the snap) and compare against Down/YardsToGo the moment it
/// starts counting again (the situation for the next snap, i.e. after the play settled). If Down
/// was 1 both times but YardsToGo went back UP (a real gain only ever holds distance steady or
/// shrinks it within the same set of downs -- it only jumps back up on a fresh set), that's a first
/// down earned on 1st down.
///
/// Guards against turnovers/kickoff returns starting a new drive at 1st & 10 the same way
/// FirstDownHelper does (NewPossession check) -- those aren't "on 1st down" conversions, they're a
/// new team's opening snap.</summary>
public sealed class FirstDownOnFirstDownHelper : IRuleEvaluator
{
    int? _downAtSnap;
    int? _yardsToGoAtSnap;

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        // Play clock just stopped counting -- the ball is about to be/has just been snapped.
        // Record the down/distance the ribbon shows right now, before the play changes it.
        if (state.Previous.IsPlayClockCounting && !state.Current.IsPlayClockCounting)
        {
            _downAtSnap = state.Current.Down;
            _yardsToGoAtSnap = state.Current.YardsToGo;
            return null;
        }

        // Play clock resumed counting -- the previous play (and any dead-ball overlay after it)
        // is fully over and the ribbon has updated for the next snap.
        if (!state.Previous.IsPlayClockCounting && state.Current.IsPlayClockCounting)
        {
            int? downAtSnap = _downAtSnap;
            int? yardsToGoAtSnap = _yardsToGoAtSnap;
            _downAtSnap = null;
            _yardsToGoAtSnap = null;

            if (state.Delta.NewPossession) return null;
            if (downAtSnap != 1 || state.Current.Down != 1) return null;
            if (yardsToGoAtSnap is null || state.Current.YardsToGo <= yardsToGoAtSnap.Value) return null;

            return new TriggerEvent
            {
                EventKey = "Offense: First Down on First Down",
                Volume = 90,
                IsEarnedBigEvent = true
            };
        }

        return null;
    }
}
