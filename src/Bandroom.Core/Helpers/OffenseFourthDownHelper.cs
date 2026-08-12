namespace Bandroom.Core.Helpers;

/// <summary>Added 2026-08-12 (owner call): before this, 4th down had ONLY "Defense: Fourth Down"
/// (see OffenseDownHelper's header comment -- "4th down is always Defense regardless of distance").
/// That meant the team actually driving on 4th down (going for it) got nothing at all -- no card
/// existed for them, home or away. This fires alongside OffenseDownHelper's existing "Defense:
/// Fourth Down" on the same tick, same "separate standalone evaluator" pattern as
/// DefenseThirdDownHelper/DefenseSecondDownShortHelper/OffenseAfterOpeningKickHelper, so it doesn't
/// depend on or alter OffenseDownHelper's own classification.
///
/// Un-gated by prefix already (see WebMainForm.ResolveEventRouting -- any "Offense:"-prefixed cue
/// always plays for whoever's driving, home or away, Big Game or not), so this satisfies the
/// owner's specific ask ("home team in non big games can play 4th downs") automatically, no extra
/// gating code needed. Ducked to 60 under "Defense: Fourth Down"'s own volume (BigGame ? 100 : 70)
/// -- 4th down stays primarily the defense's pressure moment, same balance as the
/// Offense/Defense Third Down pairing, not the inverse "offense is louder" balance 2nd & Short
/// uses.</summary>
public sealed class OffenseFourthDownHelper : IRuleEvaluator
{
    readonly DownDistanceBuffer _buffer = new();

    public bool CanFire(GameState state) => true;

    public TriggerEvent? Evaluate(GameState state)
    {
        if (state.Current.Down != state.Previous.Down)
        {
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

        if (down != 4)
            return null;

        // Same Loss deferral every sibling helper uses -- a down that got LONGER (tackle for loss)
        // reads as long here too, DefenseHelper's/TflHelper's "(Loss)" branch already owns that cue.
        if (state.Current.YardsToGo > baselineYardsToGo)
            return null;

        return new TriggerEvent
        {
            EventKey = "Offense: Fourth Down",
            Volume = 60,
            IsEarnedBigEvent = false
        };
    }
}
