namespace Bandroom.Core.Helpers;

public sealed class TimeoutHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // FIXED 2026-08-11 (owner report: a real timeout with plenty of time on the clock never
        // triggered anything) -- used to gate on TimeRemainingSeconds <= 240, a "2-minute drill"
        // heuristic that silently ate every timeout called earlier in the game. Now gates on the
        // actual scorebug signal instead: only look at a timeout-count decrement while the
        // "Time Out" banner (PlaySnapshot.IsTimeout, GameWatcher's "situation" region) is actually
        // visible on screen, any time in the game.
        if (!state.Current.IsTimeout)
            return null;

        // FIXED 2026-08-11 (owner report: a Home-team timeout never made any cue play at all) --
        // this used to ONLY ever read AwayTimeoutsRemaining, gated by `if (state.UserHasPossession)
        // return null` at the top (UserHasPossession means Home has the ball, since UserIsHome is
        // hardcoded true -- so that guard meant "only look at Away's count while Away has the
        // ball"). PlaySnapshot.HomeTimeoutsRemaining didn't exist, so a Home timeout was silently
        // invisible regardless of who had the ball. Now checks both, each under the SAME kind of
        // guard as before (a side's timeout count is only meaningful to react to while THAT side
        // currently has the ball) -- symmetric, not a redesign of the original convention.
        // "Defense:" EventKey prefix means WebMainForm.ResolveEventRouting flips this to whichever
        // side does NOT currently have the ball, so both branches correctly attribute the cue to
        // the opposing defense that forced the timeout.
        if (!state.UserHasPossession) // Away has the ball
        {
            var awayEvent = TryFireForDecrement(state.Current.AwayTimeoutsRemaining, state.Previous.AwayTimeoutsRemaining, state.Current.BigGame);
            if (awayEvent != null) return awayEvent;
        }
        if (state.UserHasPossession) // Home has the ball
        {
            var homeEvent = TryFireForDecrement(state.Current.HomeTimeoutsRemaining, state.Previous.HomeTimeoutsRemaining, state.Current.BigGame);
            if (homeEvent != null) return homeEvent;
        }
        return null;
    }

    // Edge-trigger on an actual decrement -- this evaluator used to only check current state with
    // no previous-state comparison, so it fired on EVERY tick the level condition held: ~4x/second,
    // masked down to once per FireCooldown (20s) rather than once per real timeout.
    // STATE_MACHINE_ANALYSIS.md Discrepancy #1.
    static TriggerEvent? TryFireForDecrement(int current, int previous, bool bigGame)
    {
        if (current < 0 || current > 6) return null;
        if (current >= previous) return null;

        return new TriggerEvent
        {
            EventKey = current switch
            {
                4 => "Defense: Timeout (4 Remaining)",
                3 => "Defense: Timeout (3 Remaining)",
                2 => "Defense: Timeout (2 Remaining)",
                1 => "Defense: Timeout (1 Remaining)",
                0 => "Defense: Timeout (0 Remaining)",
                _ => string.Empty
            },
            Volume = bigGame ? 100 : 65,
            IsEarnedBigEvent = false
        };
    }
}