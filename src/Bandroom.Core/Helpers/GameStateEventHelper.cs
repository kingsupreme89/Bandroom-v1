namespace Bandroom.Core.Helpers;

/// <summary>Fires pregame take-the-field, quarter-start, iced-game-by-first-down,
/// and victory-in-hand events. These are the "Hype" category game-state transition events
/// that detect quarter changes and late-game clinching scenarios.</summary>
public sealed class GameStateEventHelper : IRuleEvaluator
{
    // Victory in Hand's condition (4th quarter, clock <= 30s, 9+ point lead) is level-triggered,
    // not edge-triggered like everything else this class fires -- with 30 seconds of game clock
    // and a tick roughly every 250ms, that's up to ~120 evaluations where the condition holds, all
    // returning the same TriggerEvent. FireCooldown masks some of the resulting duplicates but not
    // all. Same self-tracked-flag fix as KickoffHelper's _didFire for the same class of bug.
    bool _didFireVictoryInHand;

    // FIXED 2026-08-11 (owner report, live big game): "Pregame Take the Field" fired mid-game off
    // an Away first down. Root cause -- the condition below only checked a single-tick
    // Previous/Current pair (Quarter 0->1, Down 0->something), with no guard against it matching
    // again later. A big game's extra overlays (celebration graphics, GAMEDAY-style graphics after
    // a big first down) can blank the scorebug for a tick, misreading Quarter/Down back down to 0
    // -- the very next tick where they resolve again then looks IDENTICAL to the real, one-time
    // pregame transition. Real pregame can only happen once per game, same class of bug
    // KickoffHelper's/_didFireVictoryInHand's own self-tracked flags exist to prevent -- this
    // never resets (unlike _didFireVictoryInHand, which legitimately can re-arm) since there's only
    // ever one real "take the field" moment per GAMETIME session.
    bool _didFirePregame;

    // FIXED 2026-08-19 (owner report, live game: "Start of 2nd Quarter" fired dozens of times over
    // ~5-7s intervals for the whole quarter instead of once): the Previous/Current edge check below
    // had no fire-once guard, unlike _didFirePregame/_didFireVictoryInHand right in this same class
    // -- Quarter is recomputed fresh every tick from OCR/RAM with no stability requirement (unlike
    // Down/Distance/Possession/Score, which got RAM-vs-OCR settle+stable protection 2026-08-14/15),
    // so a HUD graphic or OCR misread that periodically causes a single stray "1st" read between
    // real "2nd" reads makes Previous.Quarter=1/Current.Quarter=2 look like a fresh transition every
    // single time it happens, not just at the real quarter boundary. One real "start of 2nd/4th
    // quarter" per game, same reasoning _didFirePregame already documents for pregame.
    bool _didFireStart2ndQuarter;
    bool _didFireStart4thQuarter;

    // 2026-08-19 (handoff root cause #2): every Start() rebuilds this class fresh via
    // CreateEventRouter, so a bare process/session restart mid-game re-arms all the one-shot flags
    // above -- the next kickoff-shaped moment (e.g. the very next possession change, confirmed live:
    // an opponent's TD kickoff) then looks exactly like a fresh pregame/quarter-start transition,
    // since Previous.* is also reset to its zero default and can't tell "genuinely never happened
    // yet" from "already happened before this restart." A real new game's first live tick is always
    // Quarter==1, Down==1 (you can't start a drive already on 2nd down); anything else observed on
    // the FIRST tick a restarted session resolves real down/quarter data is unambiguous proof the
    // game was already in progress, so the one-shot moments already happened. Called by GameWatcher
    // exactly once per Start(), the first time Quarter/Down both resolve to real values.
    public void SuppressOneShotsAlreadyPassed(int quarter, int down)
    {
        if (quarter > 1 || down > 1) _didFirePregame = true;
        // Quarter itself already being 2 or 4 on this first tick means the transition INTO that
        // quarter already happened before the restart -- same reasoning as pregame above.
        if (quarter >= 2) _didFireStart2ndQuarter = true;
        if (quarter >= 4) _didFireStart4thQuarter = true;
    }

    public TriggerEvent? Evaluate(GameState state)
    {
        // --- Quarter transitions ---
        if (state.Previous.Quarter != state.Current.Quarter && state.Previous.Quarter > 0)
        {
            if (state.Current.Quarter == 2 && !_didFireStart2ndQuarter)
            {
                _didFireStart2ndQuarter = true;
                return new TriggerEvent
                {
                    EventKey = "Other: Start of 2nd Quarter",
                    Volume = 70,
                    IsEarnedBigEvent = false
                };
            }

            // NOTE: quarter 3 (halftime -> second half) is deliberately NOT handled here --
            // KickoffHelper already fires "Other: Second-Half Kickoff" for that exact moment,
            // gated on the actual kickoff situation-text edge (more precise than a bare quarter
            // transition) and registered in ConfigStore.AllEngineEventKeys. Adding a second
            // event here would just double-fire on the same real-world moment.

            if (state.Current.Quarter == 4 && !_didFireStart4thQuarter)
            {
                _didFireStart4thQuarter = true;
                return new TriggerEvent
                {
                    EventKey = "Other: Start of 4th Quarter",
                    Volume = 80,
                    IsEarnedBigEvent = true
                };
            }
        }

        // --- Pregame Take the Field: three independent signals for the same real-world moment ---
        // (1) the chevron tunnel-walk marker (IsPregameEntranceMarker, see its doc comment on
        // PlaySnapshot) -- fires earliest, during the actual walkout, before the first snap makes
        // Down/Quarter readable at all. (2) the original quarter/down heuristic, a fallback for
        // whenever the chevron crop isn't calibrated for the active ScorebugPreset (ChevronMarkerFxW
        // == 0) or the OCR/pixel read misses it on a given game. (3) ADDED 2026-08-12 (owner report,
        // live game: pregame never fired at all that game, neither chevron nor quarter/down tripped)
        // -- state.Current.IsKickoff going true is the SAME signal KickoffHelper already fires
        // "Other: Opening Kickoff" off of, confirmed reliable from that owner's own event log. Firing
        // pregame here too (if it hasn't already fired) guarantees it always fires by kickoff at the
        // latest, even on a game where both other signals miss -- late (right at kickoff, not the
        // actual walkout moment), but late beats never. One shared _didFirePregame guard covers all
        // three -- whichever trips first wins, the others are no-ops after that, so this can never
        // double-fire the same EventKey from multiple signals.
        bool chevronEdge = state.Current.IsPregameEntranceMarker && !state.Previous.IsPregameEntranceMarker;
        bool quarterDownEdge = state.Previous.Quarter == 0 && state.Current.Quarter == 1
            && state.Previous.Down == 0 && state.Current.Down > 0;
        bool kickoffFallbackEdge = state.Current.IsKickoff && !state.Previous.IsKickoff;
        if (!_didFirePregame && (chevronEdge || quarterDownEdge || kickoffFallbackEdge))
        {
            _didFirePregame = true;
            return new TriggerEvent
            {
                EventKey = "Other: Pregame Take the Field",
                Volume = 85,
                IsEarnedBigEvent = true
            };
        }

        // --- Iced Game by First Down: 4th quarter, under 2 min, offense earned a 1st down ---
        // Excludes turnover-driven down resets (e.g. a defensive INT resets Down to 1) -- those
        // are TurnoverHelper's "Iced Game by Turnover" cue, not an earned offensive conversion.
        if (state.Delta.WasFirstDown
            && !state.Delta.NewPossession
            && state.Current.Quarter >= 4
            && state.Current.TimeRemainingSeconds <= 120)
        {
            return new TriggerEvent
            {
                EventKey = "Offense: Iced Game by First Down",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }

        // --- Victory in Hand: 4th quarter under 30 seconds, leading by 9+ ---
        bool victoryInHandNow = false;
        if (state.Current.Quarter >= 4 && state.Current.TimeRemainingSeconds <= 30)
        {
            int homeLead = state.Current.HomeScore - state.Current.AwayScore;
            int awayLead = state.Current.AwayScore - state.Current.HomeScore;
            victoryInHandNow = homeLead >= 9 || awayLead >= 9;
        }

        if (victoryInHandNow)
        {
            if (_didFireVictoryInHand) return null;
            _didFireVictoryInHand = true;
            return new TriggerEvent
            {
                EventKey = "Offense: Victory in Hand",
                Volume = 100,
                IsEarnedBigEvent = true
            };
        }
        _didFireVictoryInHand = false;

        return null;
    }
}