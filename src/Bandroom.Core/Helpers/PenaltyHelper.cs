namespace Bandroom.Core.Helpers;

/// <summary>Fires penalty events when a penalty flag is detected.
/// Differentiates offense vs defense penalties based on the penalty flag data.</summary>
public sealed class PenaltyHelper : IRuleEvaluator
{
    // Cheap early-out: Evaluate only ever fires off these two flags.
    public bool CanFire(GameState state) => state.Current.IsPenaltyOnOffense || state.Current.IsPenaltyOnDefense;

    // FIXED 2026-08-19 (audit finding, same bug class as GameStateEventHelper's quarter-start fire
    // loop): IsPenaltyOnOffense/IsPenaltyOnDefense fall back to the raw, non-sticky OCR "Against
    // <Team Name>" text crop whenever RAM's ram.penalty.side isn't available this tick -- a single
    // false misread mid-flag-display would make the very next true read look like a brand-new
    // edge, re-firing for the SAME real penalty. Requires NotShownStreakToClear consecutive false
    // ticks before considering the flag genuinely cleared (not just one), same debounce shape as
    // KickoffHelper's own not-shown streak for the identical flicker reason -- prevents a single
    // blank tick from resetting the "already fired" guard mid-flag.
    const int NotShownStreakToClear = 2;
    bool _offenseFired;
    int _offenseNotShownStreak;
    bool _defenseFired;
    int _defenseNotShownStreak;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.IsPenaltyOnOffense)
        {
            _offenseNotShownStreak = 0;
            if (!_offenseFired)
            {
                _offenseFired = true;
                return new TriggerEvent
                {
                    EventKey = "Penalty: Offense",
                    Volume = 70,
                    IsEarnedBigEvent = false
                };
            }
        }
        else if (++_offenseNotShownStreak >= NotShownStreakToClear)
        {
            _offenseFired = false;
        }

        if (state.Current.IsPenaltyOnDefense)
        {
            _defenseNotShownStreak = 0;
            if (!_defenseFired)
            {
                _defenseFired = true;
                return new TriggerEvent
                {
                    EventKey = "Penalty: Defense",
                    Volume = 70,
                    IsEarnedBigEvent = false
                };
            }
        }
        else if (++_defenseNotShownStreak >= NotShownStreakToClear)
        {
            _defenseFired = false;
        }

        return null;
    }
}