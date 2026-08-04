namespace SupremeStadiumSoundSelector;

/// <summary>Maps the app's real trigger/event data onto the design handoff's 6 fixed
/// categories (Downs, Scoring, Turnovers, Special Teams, Penalties, Hype). The 39 "Assignable
/// Sound Events" from the game don't carry a category natively, so this is an editorial
/// judgment call, not something pulled from a spec: e.g. "Field Goal Missed by Opponent" and
/// all kickoff events are bucketed under Special Teams, "Drive Starter" and the pregame/
/// quarter-start events under Hype.</summary>
internal static class CategoryMap
{
    static readonly Dictionary<string, string> ByEvent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Offense: Earned First Down"] = "Downs",
        ["Offense: Earned First Down (Big Gain)"] = "Downs",
        ["Offense: Earned First Down (Midfield)"] = "Downs",
        ["Offense: Touchdown Scored"] = "Scoring",
        ["Offense: Second Down"] = "Downs",
        ["Offense: Second Down (Midfield)"] = "Downs",
        ["Offense: Third Down"] = "Downs",
        ["Offense: Field Goal Made"] = "Scoring",
        ["Offense: Drive Starter"] = "Hype",
        ["Offense: 2-Point Conversion Made"] = "Scoring",
        ["Offense: PAT Made"] = "Scoring",
        ["Offense: Iced Game by First Down"] = "Hype",
        ["Offense: Victory in Hand"] = "Hype",

        ["Defense: Touchdown Scored"] = "Scoring",
        ["Defense: Third Down"] = "Downs",
        ["Defense: Third Down (Loss)"] = "Downs",
        ["Defense: Fourth Down"] = "Downs",
        ["Defense: Fourth Down (Loss)"] = "Downs",
        ["Defense: Second Down"] = "Downs",
        ["Defense: Second Down (Midfield)"] = "Downs",
        ["Defense: Second Down (Loss)"] = "Downs",
        ["Defense: Field Goal Missed by Opponent"] = "Special Teams",
        ["Defense: Drive Starter"] = "Hype",
        ["Defense: Turnover Forced"] = "Turnovers",
        ["Defense: Iced Game by Turnover"] = "Turnovers",
        ["Defense: Safety"] = "Scoring",

        ["Other: Opening Kickoff"] = "Special Teams",
        ["Other: Second-Half Kickoff"] = "Special Teams",
        ["Other: Opening Kickoff on Kick"] = "Special Teams",
        ["Other: Kickoff on Kick (Kicking)"] = "Special Teams",
        ["Other: Kickoff on Kick (Receiving)"] = "Special Teams",
        ["Other: Pregame Take the Field"] = "Hype",
        ["Other: Start of 2nd Quarter"] = "Hype",
        ["Other: Start of 4th Quarter"] = "Hype",
    };

    public static string Resolve(TriggerEntry entry)
    {
        if (entry.Trigger.StartsWith("down:", StringComparison.OrdinalIgnoreCase)) return "Downs";
        if (entry.Trigger.StartsWith("flag:", StringComparison.OrdinalIgnoreCase)) return "Penalties";
        return ByEvent.TryGetValue(entry.Event, out var category) ? category : "Hype";
    }
}
