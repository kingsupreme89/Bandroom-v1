namespace SupremeStadiumSoundSelector;

/// <summary>Sorts events by WHICH SIDE they belong to (Offense/Defense) instead of by type
/// (the old Downs/Scoring/Turnovers/Special Teams/Penalties/Hype scheme) -- explicit owner
/// request: browsing by "is this my offense or my defense" matches how songs actually get
/// picked, browsing by "is this a scoring play" doesn't. Side-agnostic events (kickoffs,
/// quarter starts, pregame, the bare penalty-flag trigger) fall into "Situations" since they
/// aren't offense or defense specific.</summary>
internal static class CategoryMap
{
    static readonly Dictionary<string, string> ByEvent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Penalty: Offense"] = "Offense",
        ["Penalty: Defense"] = "Defense",
    };

    public static string Resolve(TriggerEntry entry)
    {
        if (entry.Trigger.StartsWith("down:", StringComparison.OrdinalIgnoreCase)) return "Offense";
        if (entry.Trigger.StartsWith("flag:", StringComparison.OrdinalIgnoreCase)) return "Situations";
        if (ByEvent.TryGetValue(entry.Event, out var overridden)) return overridden;
        if (entry.Event.StartsWith("Offense:", StringComparison.OrdinalIgnoreCase)) return "Offense";
        if (entry.Event.StartsWith("Defense:", StringComparison.OrdinalIgnoreCase)) return "Defense";
        return "Situations";
    }
}
