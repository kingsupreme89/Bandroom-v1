namespace Bandroom.Core.Helpers;

/// <summary>Fires once when CFB27's "EA SPORTS COLLEGE FOOTBALL 27" flag/title card first
/// appears -- edge-triggered on the not-shown -> shown transition, same pattern as
/// PregameHelper's not-ready -> ready edge, so a sound fires exactly once per game instead of
/// every tick the screen happens to still be up.
///
/// This is a DIFFERENT signal than the chevron tunnel-walk marker (IsPregameEntranceMarker,
/// which stays dedicated to GameStateEventHelper's "Other: Pregame Take the Field" -- this
/// helper does NOT touch or replace that). The flag card appears earliest in the pregame
/// broadcast sequence, before the chevron walkout and before the READY screen, so this is the
/// first team-neutral signal that "a game is about to start" -- see PlaySnapshot.IsTeamRunOut
/// and the "teamrunout" WatchedRegion in GameWatcher.cs for how that flag gets set from OCR.</summary>
public sealed class RunOutHelper : IRuleEvaluator
{
    // Cheap early-out: the flag itself is the primary Evaluate() condition.
    public bool CanFire(GameState state) => state.Current.IsTeamRunOut;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsTeamRunOut || state.Previous.IsTeamRunOut)
            return null;

        return new TriggerEvent
        {
            EventKey = "Other: Pregame Tunnel",
            Volume = 90,
            IsEarnedBigEvent = true
        };
    }
}
