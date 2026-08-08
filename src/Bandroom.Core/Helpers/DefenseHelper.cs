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

        if (state.Current.Down == 3 && state.Delta.LostYards)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down (Loss)",
                Volume = state.Current.BigGame ? 100 : 75,
                IsEarnedBigEvent = true
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
