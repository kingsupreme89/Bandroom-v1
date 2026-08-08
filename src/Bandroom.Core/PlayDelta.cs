namespace Bandroom.Core;

public sealed class PlayDelta
{
    public int YardsGained { get; init; }
    public int PreviousYardsToGo { get; init; }
    public bool LostYards { get; init; }
    public bool NewPossession { get; init; }
    public bool WasFirstDown { get; init; }
    public bool WasThirdDownStop { get; init; }
    public bool WasFourthDownStop { get; init; }

    public static PlayDelta Calculate(PlaySnapshot previous, PlaySnapshot current)
    {
        int previousLine = previous.YardLine;
        int currentLine = current.YardLine;
        var yardsGained = previousLine - currentLine;
        var lostYards = yardsGained < 0;
        bool newPossession = previous.PossessionAway != current.PossessionAway;
        bool wasFirstDown = current.Down == 1 && previous.Down > 1;
        bool wasThirdDownStop = previous.Down == 3 && current.Down == 1 && newPossession;
        bool wasFourthDownStop = previous.Down == 4 && current.Down == 1 && newPossession;

        return new PlayDelta
        {
            YardsGained = yardsGained,
            PreviousYardsToGo = previous.YardsToGo,
            LostYards = lostYards,
            NewPossession = newPossession,
            WasFirstDown = wasFirstDown,
            WasThirdDownStop = wasThirdDownStop,
            WasFourthDownStop = wasFourthDownStop
        };
    }
}
