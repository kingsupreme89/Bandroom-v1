namespace Bandroom.Core;

public sealed class TriggerEvent
{
    public string EventKey { get; init; } = string.Empty;
    public int Volume { get; init; } = 100;
    public bool IsEarnedBigEvent { get; init; }
}
