namespace Bandroom.Core.Helpers;

/// <summary>Added 2026-08-11 (owner audit call): "Defense: Third Down" should fire for the
/// offense facing 3rd down at ANY distance, not just 3rd & long -- corrects OffenseDownHelper's
/// prior behavior, which only emitted this key on the long branch (short 3rd downs only got
/// "Offense: Third Down Short"/"Defense: Third Down Short", never this one). Standalone evaluator
/// (not a branch inside OffenseDownHelper) so it can fire on EVERY 3rd down regardless of what
/// OffenseDownHelper itself classifies that same snap as -- mirrors DefenseThirdDownShortHelper's
/// own "separate file, same buffered-edge shape, dedupe-safe by EventKey" pattern.
///
/// No longer home-only-always (owner reversed that 2026-08-11, live game, same session) -- now
/// plays for whichever side is actually on defense, paired with OffenseDownHelper's new
/// "Offense: Third Down" on the same tick (60%) the same balanced way
/// "Defense/Offense: Third Down Short" already pair up. Flat 100 volume (was BigGame ? 100 : 80)
/// to match that pairing's flat Defense volume.</summary>
public sealed class DefenseThirdDownHelper : IRuleEvaluator
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

        if (down != 3)
            return null;

        // A loss (yards-to-go went UP) is DefenseHelper's/TflHelper's territory, not this cue --
        // same deferral OffenseDownHelper/DefenseThirdDownShortHelper already use.
        if (state.Current.YardsToGo > baselineYardsToGo)
            return null;

        return new TriggerEvent
        {
            EventKey = "Defense: Third Down",
            Volume = 100,
            IsEarnedBigEvent = state.Current.BigGame
        };
    }
}
