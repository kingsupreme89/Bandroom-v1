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
    public bool CanFire(GameState state) => state.Current.Down != state.Previous.Down;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.Down == state.Previous.Down)
            return null;

        // A turnover also resets Down for the new offense -- that's TurnoverHelper's moment, not
        // a down-and-distance cue. Same NewPossession guard FirstDownHelper/DefenseHelper use.
        if (state.Delta.NewPossession)
            return null;

        // Defer to DefenseHelper's "(Loss)" branch -- a down that got LONGER (tackle for loss)
        // always reads as "long" here too, which would otherwise double-fire a generic long cue
        // alongside the more specific Loss one on the same snap.
        if (state.Current.YardsToGo > state.Previous.YardsToGo)
            return null;

        int down = state.Current.Down;
        if (down < 2 || down > 4)
            return null; // 1st down is FirstDownHelper's/DriveStarterHelper's territory.

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
