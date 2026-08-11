namespace Bandroom.Core.Helpers;

/// <summary>Fires earned-first-down events. The base event fires any time a new 1st down
/// is earned (not the opening kickoff's 1st & 10). REWRITTEN 2026-08-10 (owner's "gameplan
/// simplification" pass, same session as the 2nd/3rd down short/long split): used to split by
/// yards gained to earn the down (Big Gain 15+, else base). Replaced with a pure YardsToGo check
/// on the resulting first down, matching the 2nd/3rd down pattern -- 1st & 5-or-less credits a
/// new "Short" key, else plain "1st & 10". The old Big Gain branch is dropped entirely (owner's
/// explicit, accepted tradeoff): whatever used to trigger it now just falls through to plain
/// "1st & 10" -- there's no reliable signal left to keep it distinct from the new split.</summary>
public sealed class FirstDownHelper : IRuleEvaluator
{
    // Cheap early-out: Evaluate's own first guard.
    public bool CanFire(GameState state) => state.Delta.WasFirstDown && state.Previous.Down != 0;

    public TriggerEvent? Evaluate(GameState state)
    {
        // Must be a fresh first down (not the opening snap)
        if (!state.Delta.WasFirstDown || state.Previous.Down == 0)
            return null;

        // A turnover also resets Down to 1 for the new offense -- that's the start of a new
        // drive by possession change, not a conversion the offense "earned" mid-drive. Without
        // this guard, the tick after TurnoverHelper fires "Defense: Turnover Forced", the down
        // updating to 1st fires "Offense: Earned First Down" and (via interruptPrevious) cuts off
        // the turnover cue that was already playing -- reported live as "plays the 1st down sound
        // instead of the turnover forced sound." Same NewPossession guard DefenseHelper already
        // uses for its own analogous false-positive (STATE_MACHINE_ANALYSIS.md Discrepancy #4).
        if (state.Delta.NewPossession)
            return null;

        // Short: 5 yards or less to go on the new set of downs (only really happens near the
        // goal line or after certain penalties -- most 1st downs are a plain 1st & 10).
        if (state.Current.YardsToGo <= 5)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Earned First Down Short",
                Volume = 90,
                IsEarnedBigEvent = false
            };
        }

        // Midfield: inside opponent territory (yard line <= 50). DISABLED 2026-08-07 --
        // YardLine is hardcoded to 0 everywhere (OCR for it was never built, see TASK_BOARD.md),
        // so "<= 50" was always true, meaning this branch fired on literally every first down
        // and the base event below could never be reached. Re-enable once YardLine reads a real
        // value; until then this must stay off rather than silently misfire on every down.
        // if (state.Current.YardLine <= 50)
        // {
        //     return new TriggerEvent
        //     {
        //         EventKey = "Offense: Earned First Down (Midfield)",
        //         Volume = 85,
        //         IsEarnedBigEvent = true
        //     };
        // }

        // Base first down
        return new TriggerEvent
        {
            EventKey = "Offense: Earned First Down",
            Volume = 80,
            IsEarnedBigEvent = false
        };
    }
}