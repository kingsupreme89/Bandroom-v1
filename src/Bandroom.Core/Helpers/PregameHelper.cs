namespace Bandroom.Core.Helpers;

/// <summary>Fires once when CFB27's pregame team-intro/"READY" screen first appears --
/// edge-triggered on the not-ready -> ready transition, same pattern as KickoffHelper's
/// not-kickoff -> kickoff edge, so a sound fires exactly once per game instead of every tick
/// the screen happens to still be up. UNLIKE most other pregame/situational cues, this one is
/// deliberately allowed to re-fire multiple times per app session (owner call 2026-08-12): if
/// the player hits Back on the team-select screen and re-readies, the READY screen genuinely
/// reappears and this should play again -- see the "pregameready" WatchedRegion being
/// deliberately left OUT of GameWatcher.EventGatedRegions for how that re-arming happens at the
/// OCR layer.
///
/// This is a DIFFERENT signal than GameStateEventHelper's "Other: Pregame Take the Field"
/// (which infers pregame from Quarter 0->1 + Down 0->positive, i.e. after the first snap is
/// already readable). PregameHelper instead keys off the READY screen itself, which appears
/// BEFORE kickoff, so it can fire earlier and more reliably as the "game is actually starting"
/// signal -- see PlaySnapshot.IsPregameReady and the "pregameready" WatchedRegion in
/// GameWatcher.cs for how that flag gets set from OCR.
///
/// Detection constraint (do not violate): the READY screen's panel colors change per team
/// matchup (e.g. red/blue for Ohio State/Michigan, different colors for any other pairing), so
/// PlaySnapshot.IsPregameReady must NEVER be derived from color matching -- only from
/// team-neutral signals (fixed screen position of the "READY" text, a center game-name/rivalry
/// badge, ratings-badge layout, etc). This evaluator itself has no opinion on how the flag was
/// derived; it only trusts PlaySnapshot.IsPregameReady already being color-independent.</summary>
public sealed class PregameHelper : IRuleEvaluator
{
    // Cheap early-out: the flag itself is the primary Evaluate() condition.
    public bool CanFire(GameState state) => state.Current.IsPregameReady;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (!state.Current.IsPregameReady || state.Previous.IsPregameReady)
            return null;

        return new TriggerEvent
        {
            EventKey = "Other: Pregame Ready",
            Volume = 90,
            IsEarnedBigEvent = true
        };
    }
}
