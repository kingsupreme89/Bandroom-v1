namespace Bandroom.Core.Helpers;

public sealed class BigEventHelper : IRuleEvaluator
{
    public TriggerEvent? Evaluate(GameState state)
    {
        // A turnover (interception/fumble) is also a possession flip -- TurnoverHelper already
        // covers that with its own cue, so skip here to avoid both firing together.
        if (state.Current.Down == 3 && state.Delta.NewPossession && !state.Current.IsTurnover)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Third Down",
                Volume = state.Current.BigGame ? 100 : 80,
                IsEarnedBigEvent = state.Current.BigGame
            };
        }

        // REMOVED 2026-08-11 (owner audit call): "Defense: Fourth Down (Loss)" retired as its own
        // card, same reasoning as "Defense: Third Down (Loss)"'s retirement earlier this session
        // (see DefenseHelper.cs) -- a 4th-down loss is already covered by the generic "Defense:
        // Tackle for Loss" cue (TflHelper, fires on the same snap) PLUS the plain "Defense: Fourth
        // Down" stop cue right below (a 4th-down loss essentially always ends the drive, so
        // Delta.NewPossession fires right alongside it). A separate 4th-down-specific Loss key was
        // redundant with both. The buffered edge-detection this branch used (DownDistanceBuffer)
        // is gone too -- nothing else in this class needed it.

        // A missed field goal is also a 4th-down possession flip -- FieldGoalMissedHelper already
        // covers that specific case with its own cue, so skip here to avoid both firing together.
        //
        // SPLIT OFF 2026-08-13 (owner report, real game log): this branch used to fire the SAME
        // "Defense: Fourth Down" key OffenseDownHelper fires when the offense merely FACES a 4th
        // down (down 3->4 transition, no possession change yet -- see that file's down==4 case).
        // Those are two genuinely different moments (facing 4th down vs. the stop/turnover-on-
        // downs actually completing a few ticks later once NewPossession resolves), sharing one
        // key with one assigned song -- reported live as "4th Down (Home BG) fired twice at the
        // same timestamp, same song, no penalty around it," because EventRouter's same-tick dedupe
        // only catches same-tick collisions, not two evaluators firing the identical key a few
        // ticks apart. Owner's explicit ask: split this into its own "Defense: Fourth Down Stop"
        // event/card so a defensive 4th-down stop (turnover on downs) has its own distinct song
        // slot instead of double-firing the plain "Defense: Fourth Down" facing-the-down cue.
        // "Defense: Fourth Down" itself is untouched -- OffenseDownHelper's facing-4th-down cue is
        // a real, separate, still-wanted moment the owner did not ask to remove.
        if (state.Current.Down == 4 && state.Delta.NewPossession && !state.Current.IsFieldGoalAttempt && !state.Current.IsTurnover)
        {
            return new TriggerEvent
            {
                EventKey = "Defense: Fourth Down Stop",
                Volume = state.Current.BigGame ? 100 : 80,
                IsEarnedBigEvent = state.Current.BigGame
            };
        }

        return null;
    }
}
