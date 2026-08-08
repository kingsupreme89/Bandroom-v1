namespace Bandroom.Core.Helpers;

public sealed class DefenseHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // Defense = user's team does NOT have the ball
        if (state.UserHasPossession)
            return null;

        // Edge-trigger on the down actually changing -- unlike OffenseDownHelper (which already
        // did this), this fired on every single tick a defended 2nd/3rd down stayed on screen,
        // not just once on the transition. Harmless while unassigned (nothing to spam), but would
        // have hammered AudioPlayer.Play on every ~0.5s tick the instant a song was assigned.
        if (state.Current.Down == state.Previous.Down)
            return null;

        // A 3rd-down loss that ALSO flips possession (e.g. a sack-fumble the defense recovers)
        // is a turnover/stop, not just a loss -- BigEventHelper already covers that as
        // "Defense: Third Down". Without this guard both fired together: two different real
        // cues stacked on one snap, each cutting the other off via interruptPrevious.
        // STATE_MACHINE_ANALYSIS.md Discrepancy #4.
        if (state.Delta.NewPossession)
            return null;

        if (state.Current.Down == 3 && state.Delta.LostYards)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        // Plain 3rd-down stop -- the ordinary case (incomplete pass, short gain that doesn't
        // convert, no loss of yards, no turnover) had NO evaluator at all: BigEventHelper only
        // fires "Defense: Third Down" for the rare same-snap turnover (NewPossession, already
        // excluded by the guard above), and the branch above only covers a stuffed-for-a-loss
        // 3rd down. So the single most common "held them on 3rd, forced 4th down" scenario never
        // fired any cue, despite every team's default song pack already having a
        // "Defense: Third Down" song mapped for exactly this.
        if (state.Current.Down == 3)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down",
                Volume = state.Current.BigGame ? 100 : 80,
                IsEarnedBigEvent = state.Current.BigGame
            };
        }

        if (state.Current.Down == 2 && state.Delta.LostYards)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Second Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
            };
        }

        if (state.Current.Down == 2)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Second Down",
                Volume = state.Current.BigGame ? 100 : 70,
                IsEarnedBigEvent = false
            };
        }

        return null;
    }
}
