namespace Bandroom.Core.Helpers;

/// <summary>Covers all the field-position and yardage-gain variants for 2nd/3rd/4th down
/// on both offense and defense sides. Works with the existing OffenseDownHelper and
/// DefenseHelper to fill the gaps.</summary>
public sealed class DownFieldPositionHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Previous.Down == 0 || state.Current.Down == state.Previous.Down)
            return null;

        int down = state.Current.Down;
        bool lostYards = state.Delta.LostYards;
        bool atMidfield = state.Current.YardLine <= 50;

        // --- Defense side variants ---
        // Defense = user's team does NOT have possession
        bool defenseSide = !state.UserHasPossession;

        if (defenseSide)
        {
            return down switch
            {
                2 when lostYards => Make("Defense: Second Down (Loss)", 85, true),
                2 when atMidfield => Make("Defense: Second Down (Midfield)", 75, false),
                3 when lostYards => Make("Defense: Third Down (Loss)", 85, true),
                4 when lostYards => Make("Defense: Fourth Down (Loss)", 85, true),
                _ => null
            };
        }

        // --- Offense side variants ---
        if (down == 2 && atMidfield)
        {
            return Make("Offense: Second Down (Midfield)", 75, false);
        }

        return null;
    }

    static TriggerEvent Make(string key, int vol, bool big) => new()
    {
        EventKey = key,
        Volume = vol,
        IsEarnedBigEvent = big
    };
}