using System;
using System.Collections.Generic;
using System.Linq;

namespace SupremeStadiumSoundSelector;

/// <summary>
/// Tracks recent fired events for the live feed UI (center column, last N events).
/// </summary>
public sealed class EventHistoryFeed
{
    private readonly List<EventRecord> _records = new();
    private readonly int _maxRecords;

    public event Action<EventRecord>? RecordAdded;

    public EventHistoryFeed(int maxRecords = 50)
    {
        _maxRecords = maxRecords;
    }

    public void RecordEvent(string eventName, string? teamSide, TimeSpan? duration = null)
    {
        var record = new EventRecord
        {
            EventName = eventName,
            TeamSide = teamSide,
            Timestamp = DateTime.Now,
            Duration = duration ?? TimeSpan.Zero,
        };

        _records.Insert(0, record);
        if (_records.Count > _maxRecords)
            _records.RemoveAt(_records.Count - 1);

        RecordAdded?.Invoke(record);
    }

    public List<EventRecord> GetRecent(int count = 20) =>
        _records.Take(count).ToList();

    public void Clear() => _records.Clear();
}

public sealed class EventRecord
{
    public string EventName { get; init; } = "";
    public string? TeamSide { get; init; }
    public DateTime Timestamp { get; init; }
    public TimeSpan Duration { get; init; }
}
