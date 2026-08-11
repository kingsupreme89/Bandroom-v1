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

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.Down != state.Previous.Down)
        {
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

        // Defer to DefenseHelper's "(Loss)" branch -- a down that got LONGER (tackle for loss)
        // always reads as "long" here too, which would otherwise double-fire a generic long cue
        // alongside the more specific Loss one on the same snap.
        if (state.Current.YardsToGo > baselineYardsToGo)
        {
            if (timedOut)
                state.NearMisses.Add($"OffenseDownHelper: buffered wait for down-{down} classification timed out while YardsToGo looked like a Loss, deferring to DefenseHelper");
            return null;
        }

        bool isShort = state.Current.YardsToGo <= 3;

        string eventKey = down switch
        {
            2 => isShort ? "Offense: Second Down Short" : "Defense: Second Down",
            3 => isShort ? "Offense: Third Down Short" : "Defense: Third Down",
            4 => "Defense: Fourth Down",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(eventKey))
            return null;

        return new TriggerEvent
        {
            EventKey = eventKey,
            Volume = state.Current.BigGame ? 100 : 70,
            IsEarnedBigEvent = false
        };
    }
}
