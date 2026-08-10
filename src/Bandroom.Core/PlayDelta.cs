namespace Bandroom.Core;

public sealed class PlayDelta
{
    public int YardsGained { get; init; }
    public int PreviousYardsToGo { get; init; }
    public bool NewPossession { get; init; }
    public bool WasFirstDown { get; init; }

    public static PlayDelta Calculate(PlaySnapshot previous, PlaySnapshot current)
    {
        int previousLine = previous.YardLine;
        int currentLine = current.YardLine;
        var yardsGained = previousLine - currentLine;
        bool newPossession = previous.PossessionAway != current.PossessionAway;
        bool wasFirstDown = current.Down == 1 && previous.Down > 1;

        return new PlayDelta
        {
            YardsGained = yardsGained,
            PreviousYardsToGo = previous.YardsToGo,
            NewPossession = newPossession,
            WasFirstDown = wasFirstDown
        };
    }
}
