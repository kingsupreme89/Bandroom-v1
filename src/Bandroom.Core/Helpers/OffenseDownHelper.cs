namespace Bandroom.Core.Helpers;

/// <summary>REWRITTEN 2026-08-10 ("gameplan" session) -- used to fire one unconditional
/// "Offense: Nth Down" cue per down change, always attributed to whoever currently has the ball,
/// with no distance awareness, and only while UserHasPossession. Real owner request (a real band
/// member describing how two bands' hype cues should trade off during a drive): short yardage
/// keeps the OFFENSE hyped (they're expected to convert), long yardage hands it to the DEFENSE
/// instead (tougher snap for the offense). 4th down is always Defense regardless of distance --
/// a 4th down is inherently a pressure/decision moment (go for it, punt, field goal), not an
/// offensive gimme the way a short 2nd/3rd is.
///
/// No longer gated on UserHasPossession -- doesn't need to be. The EventKey's OWN prefix is what
/// routes it (WebMainForm.OnEngineEventsDetected flips any "Defense:"-prefixed key to the side
/// OPPOSITE current possession, same trick "Penalty: Offense" already relies on), so this fires
/// symmetrically for whichever team's drive the down just changed on -- covers both "our drive"
/// and "their drive" with one helper instead of needing a mirrored pair.
///
/// Reuses the PRE-EXISTING "Defense: Second Down"/"Defense: Third Down"/"Defense: Fourth Down"
/// keys for the long/4th-down cases specifically (not new keys) so any default song pack or
/// existing user assignment on those cards keeps working unchanged -- only the short/offense
/// side needed genuinely new keys, since the old "Offense: Second/Third Down" were already
/// retired (hidden from the UI, kept only for legacy-alias firing) at the owner's own earlier
/// request to reduce clutter.</summary>
public sealed class OffenseDownHelper : IRuleEvaluator
{
    // Buffered edge-detection, same fix as DefenseHelper's (Loss) branch (STATE_MACHINE_ANALYSIS
    // Discrepancy #12): "down" and "yards to go" are two independent OCR reads that don't always
    // land on the same tick. Classifying short-vs-long off Current.YardsToGo on the exact tick
    // Down changes can read a stale (pre-snap) distance -- caused real misfires: a genuine 2nd &
    // long read as still "short" (wrong event key, or silently swallowed if the stale value also
    // looked like a loss), and 3rd/4th & long double-firing once here off the stale short-looking
    // value and again a tick later from DefenseHelper's own (correctly buffered) Loss branch.
    // Now waits up to MaxPendingTicks for YardsToGo to actually move off its pre-transition
    // baseline before classifying, falling back to whatever's read by then on timeout so a genuine
    // OCR gap still fires something rather than going silent.
    readonly DownDistanceBuffer _buffer = new();

    // FIXED 2026-08-19 (live bug: "3rd & Short" fired for a team that had just received a
    // kickoff, before their real first snap of the drive). GameWatcher's down/distance read is
    // sticky -- it holds the last REAL down seen (e.g. "3rd & Short" from the drive that just
    // ended) all the way through a kickoff, since there's no down/distance HUD to read during
    // the kickoff itself. A single flickered OCR tick during/right after the kickoff can look
    // like Down "changing" even though the receiving team hasn't snapped yet, re-firing this
    // helper off that stale value. Same "_awaitingPostKickoffSnap" guard DriveStarterHelper
    // already uses to suppress itself until the real first snap after a kickoff.
    bool _awaitingPostKickoffSnap;

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        bool wasAwaitingPostKickoffSnap = _awaitingPostKickoffSnap;
        if (state.Current.IsKickoff)
            _awaitingPostKickoffSnap = true;
        else if (_awaitingPostKickoffSnap && state.Current.Down is >= 1 and <= 4)
            _awaitingPostKickoffSnap = false;

        if (state.Current.Down != state.Previous.Down)
        {
            if (state.Current.IsKickoff || wasAwaitingPostKickoffSnap)
            {
                _buffer.Clear();
                return null;
            }

            // A turnover also resets Down for the new offense -- that's TurnoverHelper's moment,
            // not a down-and-distance cue. Same NewPossession guard FirstDownHelper/DefenseHelper
            // use.
            if (state.Delta.NewPossession)
            {
                _buffer.Clear();
                return null;
            }

            _buffer.Start(state.Current.Down, state.Previous.YardsToGo);
            return null; // wait for the yards-to-go OCR read to catch up before classifying
        }

        if (!_buffer.IsPending)
            return null;

        bool timedOut = _buffer.Advance();
        if (!timedOut && state.Current.YardsToGo == _buffer.BaselineYardsToGo)
            return null; // yards-to-go hasn't updated yet -- keep waiting

        int down = _buffer.PendingDown!.Value;
        int baselineYardsToGo = _buffer.BaselineYardsToGo;
        _buffer.Clear();

        if (down < 2 || down > 4)
            return null; // 1st down is FirstDownHelper's/DriveStarterHelper's territory.

        // Defer to DefenseHelper's/TflHelper's Loss branch -- a down that got LONGER (tackle for
        // loss) always reads as "long" here too, which would otherwise double-fire a generic long
        // cue alongside the more specific Loss one on the same snap.
        //
        // EXCEPT during a Big Game (owner call 2026-08-11, live game): a big-game 2nd/3rd & long
        // still deserves the "hand it to the defense" hype cue on top of the loss cue, whether the
        // long distance came from a loss or not -- only OUTSIDE Big Game does this defer entirely
        // to avoid double-firing on an ordinary TFL play. Short yardage (e.g. a small loss that
        // still nets 2nd & 5) is unaffected either way -- falls through to the isShort check below
        // exactly as if this guard weren't here, so the Offense cue still plays for genuinely short
        // situations regardless of BigGame.
        if (state.Current.YardsToGo > baselineYardsToGo && !state.Current.BigGame)
        {
            if (timedOut)
                state.NearMisses.Add($"OffenseDownHelper: buffered wait for down-{down} classification timed out while YardsToGo looked like a Loss, deferring to DefenseHelper");
            return null;
        }

        // "Short" threshold corrected 2026-08-11 (owner audit call): 5 yards to go or less,
        // not 3 -- matches FirstDownHelper's own <= 5 "Short" definition for the fresh-set case,
        // which this was inconsistent with before.
        bool isShort = state.Current.YardsToGo <= 5;

        // down==3's long branch used to fire "Defense: Third Down" here too, but that meant this
        // key ONLY fired on 3rd & long -- corrected 2026-08-11 (owner audit call): "Defense: Third
        // Down" should fire on 3rd down at ANY distance, so it's DefenseThirdDownHelper's own
        // standalone job (fires alongside "Offense: Third Down Short" on short 3rd downs, alongside
        // the new "Offense: Third Down" below on long ones). "Offense: Third Down" added 2026-08-11
        // (owner, live game) so 3rd & long gets the same balanced dual-fire treatment as 3rd &
        // short instead of playing only Defense's cue.
        string eventKey = down switch
        {
            2 => isShort ? "Offense: Second Down Short" : "Defense: Second Down",
            3 => isShort ? "Offense: Third Down Short" : "Offense: Third Down",
            4 => "Defense: Fourth Down",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(eventKey))
            return null;

        // Owner rule 2026-08-11: on 3rd down (short OR long), BOTH the Offense and Defense cues
        // should always play together -- Defense at full volume (the crowd anticipating a stop/
        // sack is the bigger moment), Offense ducked under it rather than competing at equal
        // volume. Flat 60, not BigGame-scaled, since the owner wants this balance every time, not
        // just outside Big Game.
        //
        // 2nd & short is the inverse balance (owner call 2026-08-11): the offense facing a
        // manageable 2nd down is the bigger moment here, not the defense -- whoever HAS the ball
        // plays their 2nd down song at full 100, the other side's "Defense: Second Down Short"
        // (DefenseSecondDownShortHelper, same-tick pairing) gets ducked to 60. Flat, not
        // BigGame-scaled, same reasoning as 3rd down short/long above.
        int volume = (eventKey == "Offense: Third Down Short" || eventKey == "Offense: Third Down")
            ? 60
            : eventKey == "Offense: Second Down Short"
                ? 100
                : (state.Current.BigGame ? 100 : 70);

        return new TriggerEvent
        {
            EventKey = eventKey,
            Volume = volume,
            IsEarnedBigEvent = false
        };
    }
}
