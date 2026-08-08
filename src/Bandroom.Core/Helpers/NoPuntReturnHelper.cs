namespace Bandroom.Core.Helpers;

/// <summary>Fires when the defense stops a punt return — either a fair catch or the punt
/// sails out of bounds / into the end zone with no return. Detected via the situation OCR
/// region showing "FAIR CATCH" or "NO RETURN" text. The defense side gets the cue since
/// they're the ones who stopped the return.</summary>
public sealed class NoPuntReturnHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsNoPuntReturn || state.Previous.IsNoPuntReturn)
            return null;

        // Defense side = user does NOT have possession (the punting team has the ball,
        // so the receiving/returning team is the offense — "no return" means the punting
        // team's coverage unit won this exchange).
        if (state.UserHasPossession)
            return null;

        return new TriggerEvent
        {
            EventKey = "Defense: No Punt Return",
            Volume = 75,
            IsEarnedBigEvent = false
        };
    }
}