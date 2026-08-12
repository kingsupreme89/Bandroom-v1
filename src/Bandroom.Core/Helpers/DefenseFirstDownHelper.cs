namespace Bandroom.Core.Helpers;

/// <summary>Fires "Defense: After Opening Kick" for the one specific moment flagged as unbuilt in
/// docs/CFB27_Session23_Handoff.md: the very first offensive snap of a kickoff-started drive
/// (the receiving team's fresh 1st & 10 right after the return), attributed to the DEFENSE side
/// -- the team that just kicked, whose D is about to take the field.
///
/// Owner rule 2026-08-12 (live game, Big Game): both teams should play on this moment, same
/// balanced-dual-fire shape as 2nd/3rd Down Short -- the receiving team (offense, has the ball)
/// is the bigger moment, full 100 (see the new sibling "Offense: After Opening Kick" evaluator,
/// same trigger condition); this Defense side is the ducked counterpart at 60, same balance as
/// "Defense: Second Down Short". No longer home-only-always (that gating is dropped -- ordinary
/// Defense:* routing/Big-Game tiers in WebMainForm.ResolveEventRouting now apply like any other
/// Defense cue).
///
/// Neither FirstDownHelper nor OffenseDownHelper fire anything for this exact moment (both
/// explicitly exclude the opening/kickoff snap), so this is a standalone evaluator, not a
/// branch on either.
///
/// Self-tracked flag instead of comparing Previous.IsKickoff/Current.IsKickoff directly --
/// same fix KickoffHelper already needed (see its own 2026-08-09 comment): "situation" is a
/// gated region whose Last value only updates when "down" changes, so Previous.IsKickoff can
/// stay stuck true across multiple ticks and isn't a reliable one-tick edge on its own. Does
/// not override CanFire (default true) since Evaluate's flag bookkeeping is a side effect that
/// must run every tick, same reasoning KickoffHelper's _didFire tracking relies on.</summary>
public sealed class DefenseFirstDownHelper : IRuleEvaluator
{
    bool _awaitingFirstSnap;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.IsKickoff)
        {
            _awaitingFirstSnap = true;
            return null;
        }

        if (!_awaitingFirstSnap)
            return null;

        // First non-kickoff tick after the kickoff was seen -- if it's already showing a fresh
        // 1st & 10, this is the moment. Any other down here means the exact snap was missed
        // (OCR gap) -- don't fire late/wrong, just drop the flag and move on.
        _awaitingFirstSnap = false;
        if (state.Current.Down != 1)
            return null;

        return new TriggerEvent
        {
            EventKey = "Defense: After Opening Kick",
            Volume = 60,
            IsEarnedBigEvent = false
        };
    }
}
