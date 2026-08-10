namespace Bandroom.Core.Helpers;

/// <summary>Covers the Midfield field-position variants for 2nd down on both offense and
/// defense sides. Loss-of-yards variants ("Second/Third/Fourth Down (Loss)") were REMOVED
/// 2026-08-08 (STATE_MACHINE_ANALYSIS.md Discrepancy #3) -- they exactly duplicated
/// DefenseHelper's own Loss variants (same EventKey, different Volume), so both evaluators
/// fired for the same tick and the second AudioPlayer.Play(interruptPrevious: true) call cut
/// off the first mid-clip, producing an audible start-stop-restart glitch on every loss-of-yards
/// 2nd/3rd/4th down. DefenseHelper remains the single source for those.</summary>
public sealed class DownFieldPositionHelper : IRuleEvaluator
{
    // Cheap early-out: Evaluate's own first guard, before touching YardLine/UserHasPossession.
    public bool CanFire(GameState state) => state.Previous.Down != 0 && state.Current.Down != state.Previous.Down;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Previous.Down == 0 || state.Current.Down == state.Previous.Down)
            return null;

        int down = state.Current.Down;
        // Discrepancy #2: YardLine OCR was never built (always 0), so "<= 50" was always true --
        // this fired "(Midfield)" on literally every 2nd down, duplicating the plain event. Gated
        // behind YardLine > 0 so it stays dormant (matching FirstDownHelper's own Midfield guard)
        // until real yard-line OCR exists, instead of firing on bogus data.
        bool atMidfield = state.Current.YardLine > 0 && state.Current.YardLine <= 50;

        // --- Defense side variants ---
        // Defense = user's team does NOT have possession
        bool defenseSide = !state.UserHasPossession;

        if (defenseSide)
        {
            return down == 2 && atMidfield ? Make("Defense: Second Down (Midfield)", 75, false) : null;
        }

        // --- Offense side variants ---
        if (down == 2 && atMidfield)
        {
            return Make("Offense: Second Down (Midfield)", 75, false);
        }

        return null;
    }

    static TriggerEvent Make(string key, int vol, bool big) => new()
    {
        EventKey = key,
        Volume = vol,
        IsEarnedBigEvent = big
    };
}