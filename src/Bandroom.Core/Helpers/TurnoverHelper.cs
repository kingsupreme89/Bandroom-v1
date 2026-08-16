namespace Bandroom.Core.Helpers;

/// <summary>Fires "Defense: Turnover Forced" when possession flips mid-play AND the turnover flag
/// is set (interception or fumble). Also fires "Defense: Iced Game by Turnover" late in the 4th
/// quarter -- but see the CORRECTED note below, that one is broader than a plain turnover.
///
/// FIXED 2026-08-11 (owner report, live big game: away fumbled -- recovered by their OWN offense,
/// 1st &amp; 10 -> 2nd &amp; 23, no possession change -- but this fired "Turnover Forced" anyway,
/// covering up the TFL cue that should've played). Root cause: `IsTurnover` is set purely from the
/// HUD's on-screen INTERCEPTED/FUMBLE/TURNOVER text (GameWatcher.cs's situation regex) -- a FUMBLE
/// on screen doesn't mean the recovering team changed; the doc comment above already said "when
/// possession flips... AND the turnover flag is set" but the code below never actually checked
/// NewPossession. Now it does. A same-team-recovered fumble no longer masquerades as a turnover --
/// it just falls through to whatever the down/distance actually did (TflHelper's generic "Tackle
/// for Loss" cue already covers the loss-yardage case with no separate "Fumble" card needed; see
/// that file's own label, now "Tackle for Loss / Fumble"). A REAL interception or fumble that
/// flips possession is unaffected -- NewPossession is true for those, same as always.</summary>
public sealed class TurnoverHelper : IRuleEvaluator
{
    // Cheap early-out: either a real turnover, or a possession flip (checked more precisely
    // below for the iced-game case) can qualify now.
    // MUST stay a superset of every condition Evaluate actually checks below -- CanFire==false
    // skips Evaluate entirely (see IRuleEvaluator's own doc comment), so a future Evaluate branch
    // added without a matching OR term here would silently never fire, the exact bug class this
    // evaluator was already broken by once (2026-08-11: doc comment said NewPossession, code
    // didn't check it). Covered by EvaluatorTests.CanFire_False_Implies_Evaluate_Null_AllHelpers,
    // added 2026-08-16 (state-machine audit finding #4) across every IRuleEvaluator, not just this
    // one, so this specific comment is a reminder, not the only guard.
    public bool CanFire(GameState state) => state.Current.IsTurnover || state.Delta.NewPossession;

    public TriggerEvent? Evaluate(GameState state)
    {
        // CORRECTED 2026-08-11 (owner audit call): "iced the game" means the team that's actually
        // WINNING just got the ball back late -- that's what seals it, not "a turnover happened."
        // The old version fired this on ANY real turnover (IsTurnover) in the window regardless of
        // who was ahead, so a team that was LOSING and just intercepted a pass with 90 seconds left
        // -- still needing to score -- got the same "game sealed" cue as an actual game-ending
        // takeaway. Now: fires whenever possession flips (real turnover OR the trailing team
        // simply turning it over on downs/punting it away) to whichever side is ahead on the
        // scoreboard right now. A flip while the score is still tied doesn't count -- nobody's
        // "up" yet, so nothing is sealed.
        //
        // DOCUMENTED 2026-08-16 (state-machine audit finding #2): "turning it over on downs" above
        // means this INTENTIONALLY overlaps with BigEventHelper's "Defense: Fourth Down Stop"
        // (Down==4 && NewPossession && !IsFieldGoalAttempt && !IsTurnover) whenever that stop also
        // lands in this Iced-Game window (Quarter>=4, TimeRemainingSeconds<=120) with the new
        // possessor already winning -- both cues fire on the same tick, same as the documented
        // Offense/Defense pairs on 2nd/3rd down elsewhere in this codebase. Unlike those pairs this
        // was never cross-referenced or tested until now -- see
        // EvaluatorTests.BigEventHelper_And_TurnoverHelper_BothFire_OnLateGameFourthDownStop.
        if (state.Delta.NewPossession && state.Current.Quarter >= 4 && state.Current.TimeRemainingSeconds <= 120)
        {
            bool newPossessorIsHome = !state.Current.PossessionAway;
            int homeLead = state.Current.HomeScore - state.Current.AwayScore;
            bool newPossessorIsWinning = newPossessorIsHome ? homeLead > 0 : homeLead < 0;
            if (newPossessorIsWinning)
            {
                return new TriggerEvent
                {
                    EventKey = "Defense: Iced Game by Turnover",
                    Volume = 100,
                    IsEarnedBigEvent = true
                };
            }
        }

        if (!state.Current.IsTurnover || state.Previous.IsTurnover || !state.Delta.NewPossession)
            return null;

        return new TriggerEvent
        {
            EventKey = "Defense: Turnover Forced",
            Volume = state.Current.BigGame ? 100 : 80,
            IsEarnedBigEvent = true
        };
    }
}