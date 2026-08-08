namespace Bandroom.Core.Helpers;

/// <summary>Fires when possession flips mid-play AND the turnover flag is set
/// (interception or fumble). Also fires "iced game by turnover" in the 4th quarter
/// with under 2 minutes remaining.</summary>
public sealed class TurnoverHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsTurnover || state.Previous.IsTurnover)
            return null;

        // Iced game by turnover: 4th quarter, under 120 seconds
        if (state.Current.Quarter >= 4 && state.Current.TimeRemainingSeconds <= 120)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Iced Game by Turnover",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }

        return new TriggerEvent
        {
            EventKey = "Defense: Turnover Forced",
            Volume = state.Current.BigGame ? 100 : 80,
            IsEarnedBigEvent = true
        };
    }
}