using System.Drawing;
using System.Linq;

namespace SupremeStadiumSoundSelector;

public readonly record struct TeamColor(string Name, Color? Primary, Color? Secondary, string Mascot = "")
{
    static readonly Color DefaultAccent = ColorTranslator.FromHtml("#22d3ee");

    /// <summary>Primary color to theme with, falling back to the app's default neon accent
    /// (#22d3ee) for "General" (no team selected). A plain constant, not Theme.CategoryDowns --
    /// that property reads ActiveTeam.Accent, so referencing it here would recurse.</summary>
    public Color Accent => Primary ?? DefaultAccent;
}

/// <summary>The ~140-team FBS color table, ported directly from the PROFILE_LIST constant in
/// the design handoff's "Stadium Sound Selector - UI Redesign.dc.html". Hex pairs are
/// verbatim from that file -- do not "correct" a color without checking the source design
/// file first, some are intentionally unusual (e.g. Colorado's gold-primary).</summary>
internal static class TeamColors
{
    static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    /// <summary>The base ~140-team FBS roster (see class summary). Custom "TeamBuilder" schools
    /// added via AddCustomTeam below are layered on top of this at load time, not mixed into it,
    /// so this table stays a straight port of the design handoff's PROFILE_LIST.</summary>
    static readonly TeamColor[] BaseTeams =
    {
        new("General", null, null),
        new("Air Force", Hex("#003087"), Hex("#8a8d8f")),
        new("Akron", Hex("#041e42"), Hex("#a89968")),
        new("Alabama", Hex("#9e1b32"), Hex("#828a8c")),
        new("Appalachian State", Hex("#000000"), Hex("#ffcc00")),
        new("Arizona", Hex("#ab0520"), Hex("#0c234b")),
        new("Arizona State", Hex("#8c1d40"), Hex("#ffc627")),
        new("Arkansas", Hex("#9d2235"), Hex("#000000")),
        new("Arkansas State", Hex("#cc092f"), Hex("#000000")),
        new("Army", Hex("#000000"), Hex("#d4bf91")),
        new("Auburn", Hex("#0c2340"), Hex("#e87722")),
        new("Ball State", Hex("#ba0c2f"), Hex("#000000")),
        new("Baylor", Hex("#003015"), Hex("#ffb81c")),
        new("Boise State", Hex("#0033a0"), Hex("#d64309")),
        new("Boston College", Hex("#98002e"), Hex("#bc9b6a")),
        new("Bowling Green", Hex("#4f2c1d"), Hex("#fe5000")),
        new("BYU", Hex("#002e5d"), Hex("#ffffff")),
        new("Buffalo", Hex("#005bbb"), Hex("#ffffff")),
        new("California", Hex("#003262"), Hex("#fdb515")),
        new("Central Michigan", Hex("#6a0032"), Hex("#ffc82e")),
        new("Charlotte", Hex("#005035"), Hex("#b3a369")),
        new("Cincinnati", Hex("#e00122"), Hex("#000000")),
        new("Clemson", Hex("#f56600"), Hex("#522d80")),
        new("Coastal Carolina", Hex("#006f71"), Hex("#a27752")),
        new("Colorado", Hex("#cfb87c"), Hex("#000000")),
        new("Colorado State", Hex("#1e4d2b"), Hex("#c8c372")),
        new("Connecticut", Hex("#000e2f"), Hex("#ffffff")),
        new("Delaware", Hex("#00539f"), Hex("#ffd200")),
        new("Duke", Hex("#012169"), Hex("#ffffff")),
        new("East Carolina", Hex("#592a8a"), Hex("#fdc82f")),
        new("Eastern Michigan", Hex("#006633"), Hex("#ffffff")),
        new("FAU", Hex("#003366"), Hex("#cc0000")),
        new("FIU", Hex("#002f5f"), Hex("#b0862e")),
        new("Florida", Hex("#0021a5"), Hex("#fa4616")),
        new("Florida State", Hex("#782f40"), Hex("#ceb888")),
        new("Fresno State", Hex("#db0032"), Hex("#002554")),
        new("Georgia", Hex("#ba0c2f"), Hex("#000000")),
        new("Georgia Southern", Hex("#041e42"), Hex("#b0b7bc")),
        new("Georgia State", Hex("#0039a6"), Hex("#c60c30")),
        new("Georgia Tech", Hex("#b3a369"), Hex("#003057")),
        new("Hawaii", Hex("#024731"), Hex("#ffffff")),
        new("Houston", Hex("#c8102e"), Hex("#ffffff")),
        new("Illinois", Hex("#e84a27"), Hex("#13294b")),
        new("Indiana", Hex("#990000"), Hex("#eeedeb")),
        new("Iowa", Hex("#ffcd00"), Hex("#000000")),
        new("Iowa State", Hex("#c8102e"), Hex("#f1be48")),
        new("Jacksonville State", Hex("#a80532"), Hex("#000000")),
        new("James Madison", Hex("#450084"), Hex("#cbb677")),
        new("Kansas", Hex("#0051ba"), Hex("#e8000d")),
        new("Kansas State", Hex("#512888"), Hex("#ffffff")),
        new("Kennesaw State", Hex("#ffc629"), Hex("#000000")),
        new("Kent State", Hex("#002664"), Hex("#eaaa00")),
        new("Kentucky", Hex("#0033a0"), Hex("#ffffff")),
        new("Liberty", Hex("#002d62"), Hex("#b31942")),
        new("Louisiana", Hex("#ce181e"), Hex("#000000")),
        new("Louisiana Tech", Hex("#00285e"), Hex("#c41230")),
        new("Louisville", Hex("#ad0000"), Hex("#000000")),
        new("LSU", Hex("#461d7c"), Hex("#fdd023")),
        new("Marshall", Hex("#00b140"), Hex("#000000")),
        new("Maryland", Hex("#e03a3e"), Hex("#ffd520")),
        new("Memphis", Hex("#003087"), Hex("#898d8d")),
        new("Miami", Hex("#f47321"), Hex("#005030")),
        new("Miami OH", Hex("#c41230"), Hex("#000000")),
        new("Michigan", Hex("#00274c"), Hex("#ffcb05")),
        new("Michigan State", Hex("#18453b"), Hex("#ffffff")),
        new("Middle Tennessee", Hex("#0066cc"), Hex("#000000")),
        new("Minnesota", Hex("#7a0019"), Hex("#ffcc33")),
        new("Ole Miss", Hex("#14213d"), Hex("#ce1126")),
        new("Mississippi State", Hex("#660000"), Hex("#ffffff")),
        new("Missouri", Hex("#f1b82d"), Hex("#000000")),
        new("Navy", Hex("#00205b"), Hex("#c5b783")),
        new("NC State", Hex("#cc0000"), Hex("#000000")),
        new("Nebraska", Hex("#e41c38"), Hex("#ffffff")),
        new("Nevada", Hex("#003366"), Hex("#807f7f")),
        new("New Mexico", Hex("#ba0c2f"), Hex("#a7a8aa")),
        new("New Mexico State", Hex("#a72036"), Hex("#ffffff")),
        new("North Carolina", Hex("#7bafd4"), Hex("#13294b")),
        new("North Texas", Hex("#00853e"), Hex("#000000")),
        new("Northern Illinois", Hex("#c8102e"), Hex("#000000")),
        new("Northwestern", Hex("#4e2a84"), Hex("#ffffff")),
        new("Notre Dame", Hex("#0c2340"), Hex("#c99700")),
        new("Ohio", Hex("#00694e"), Hex("#ffffff")),
        new("Ohio State", Hex("#bb0000"), Hex("#666666")),
        new("Oklahoma", Hex("#841617"), Hex("#fdf9d8")),
        new("Oklahoma State", Hex("#ff7300"), Hex("#000000")),
        new("Old Dominion", Hex("#003057"), Hex("#8dc8e8")),
        new("Oregon", Hex("#154733"), Hex("#fee123")),
        new("Oregon State", Hex("#dc4405"), Hex("#000000")),
        new("Penn State", Hex("#041e42"), Hex("#ffffff")),
        new("Pittsburgh", Hex("#003594"), Hex("#ffb81c")),
        new("Purdue", Hex("#ceb888"), Hex("#000000")),
        new("Rice", Hex("#00205b"), Hex("#c1c6c8")),
        new("Rutgers", Hex("#cc0033"), Hex("#5f6a72")),
        new("Sam Houston", Hex("#f15a22"), Hex("#000000")),
        new("San Diego State", Hex("#a6192e"), Hex("#000000")),
        new("San Jose State", Hex("#0055a2"), Hex("#ffffff")),
        new("SMU", Hex("#c8102e"), Hex("#0033a0")),
        new("South Alabama", Hex("#00205b"), Hex("#a5acaf")),
        new("South Carolina", Hex("#73000a"), Hex("#000000")),
        new("South Florida", Hex("#006747"), Hex("#cfc493")),
        new("Southern Miss", Hex("#ffab00"), Hex("#000000")),
        new("Stanford", Hex("#8c1515"), Hex("#ffffff")),
        new("Syracuse", Hex("#f76900"), Hex("#000e54")),
        new("TCU", Hex("#4d1979"), Hex("#a3a9ac")),
        new("Temple", Hex("#9d2235"), Hex("#000000")),
        new("Tennessee", Hex("#ff8200"), Hex("#ffffff")),
        new("Texas", Hex("#bf5700"), Hex("#ffffff")),
        new("Texas A&M", Hex("#500000"), Hex("#ffffff")),
        new("Texas State", Hex("#501214"), Hex("#a5acaf")),
        new("Texas Tech", Hex("#cc0000"), Hex("#000000")),
        new("Toledo", Hex("#005837"), Hex("#ffce00")),
        new("Troy", Hex("#b4a369"), Hex("#8d2028")),
        new("Tulane", Hex("#006747"), Hex("#418fde")),
        new("Tulsa", Hex("#002664"), Hex("#c8102e")),
        new("UAB", Hex("#1e6b52"), Hex("#000000")),
        new("UCF", Hex("#ba9b37"), Hex("#000000")),
        new("UCLA", Hex("#2d68c4"), Hex("#f2a900")),
        new("UConn", Hex("#000e2f"), Hex("#ffffff")),
        new("ULM", Hex("#840029"), Hex("#8f8f8f")),
        new("UMass", Hex("#881c1c"), Hex("#000000")),
        new("UNLV", Hex("#cf0a2c"), Hex("#000000")),
        new("USC", Hex("#990000"), Hex("#ffc72c")),
        new("Utah", Hex("#cc0000"), Hex("#ffffff")),
        new("Utah State", Hex("#0f2439"), Hex("#ffffff")),
        new("UTEP", Hex("#ff8200"), Hex("#041e42")),
        new("UTSA", Hex("#0c2340"), Hex("#f15a22")),
        new("Vanderbilt", Hex("#866d4b"), Hex("#000000")),
        new("Virginia", Hex("#232d4b"), Hex("#f84c1e")),
        new("Virginia Tech", Hex("#630031"), Hex("#cf4420")),
        new("Wake Forest", Hex("#9e7e38"), Hex("#000000")),
        new("Washington", Hex("#4b2e83"), Hex("#b7a57a")),
        new("Washington State", Hex("#981e32"), Hex("#5e6a71")),
        new("West Virginia", Hex("#002855"), Hex("#eaaa00")),
        new("Western Kentucky", Hex("#c60c30"), Hex("#000000")),
        new("Western Michigan", Hex("#532a1f"), Hex("#b6862c")),
        new("Wisconsin", Hex("#c5050c"), Hex("#ffffff")),
        new("Wyoming", Hex("#492f24"), Hex("#ffc425")),
    };

    /// <summary>The 50 most popular FCS programs, shipped as real first-class roster entries --
    /// NOT routed through the TeamBuilder custom-team path (AddCustomTeam/custom_teams.json).
    /// Kept as its own array rather than merged into BaseTeams so BaseTeams stays a byte-for-byte
    /// port of the design handoff's PROFILE_LIST (see that array's doc comment), but every team
    /// here is layered into TeamColors.All the exact same way at BuildAll() below -- same Team
    /// picker, same Set Matchup eligibility, same ResolveTeamColor OCR possession-color matching
    /// as any of the ~140 FBS teams above. Ships with the app for every user immediately, no
    /// "Add School" step needed.</summary>
    static readonly TeamColor[] FcsTeams =
    {
        new("North Dakota State", Hex("#134a37"), Hex("#ffc72c")),
        new("Montana", Hex("#7c1938"), Hex("#b03a5b")), // secondary set to a lighter tint of the primary burgundy (was silver #c0c0c0) so applyTeamGlowVars' "lighter of primary/secondary" LED-glow pick lands on burgundy instead of silver -- owner request 2026-08-12
        new("Montana State", Hex("#154734"), Hex("#f2a900")),
        new("South Dakota State", Hex("#0033a0"), Hex("#ffc627")),
        new("Villanova", Hex("#00205b"), Hex("#c8102e")),
        new("Eastern Washington", Hex("#a10022"), Hex("#4a4a4a")),
        new("North Dakota", Hex("#009a44"), Hex("#000000")),
        new("Illinois State", Hex("#ce1126"), Hex("#000000")),
        new("Southern Illinois", Hex("#8b1122"), Hex("#a2aaad")),
        new("Youngstown State", Hex("#e6b400"), Hex("#000000")),
        new("South Dakota", Hex("#cc0000"), Hex("#000000")),
        new("Northern Iowa", Hex("#4b116f"), Hex("#a89968")),
        new("Weber State", Hex("#4b116f"), Hex("#a7a9ac")),
        new("Idaho", Hex("#8b2332"), Hex("#a7a9ac")),
        new("Southeastern Louisiana", Hex("#00563f"), Hex("#ffd100")),
        new("Nicholls", Hex("#c39445"), Hex("#003057")),
        new("Incarnate Word", Hex("#c8102e"), Hex("#f2a900")),
        new("Central Arkansas", Hex("#5b0913"), Hex("#a7a9ac")),
        new("Furman", Hex("#4b1869"), Hex("#ffffff")),
        new("Chattanooga", Hex("#00386b"), Hex("#f8b800")),
        new("Wofford", Hex("#8b6f4e"), Hex("#000000")),
        new("Samford", Hex("#0033a0"), Hex("#a7a9ac")),
        new("Mercer", Hex("#f28e1c"), Hex("#000000")),
        new("William & Mary", Hex("#115740"), Hex("#a89968")),
        new("Richmond", Hex("#870020"), Hex("#003057")),
        new("Elon", Hex("#73000a"), Hex("#a7a9ac")),
        new("Stony Brook", Hex("#990000"), Hex("#000000")),
        new("Albany", Hex("#461d7c"), Hex("#f4a900")),
        new("New Hampshire", Hex("#003a70"), Hex("#a7a9ac")),
        new("Maine", Hex("#00285e"), Hex("#a89968")),
        new("Rhode Island", Hex("#00447c"), Hex("#a7a9ac")),
        new("Towson", Hex("#ffc72c"), Hex("#231f20")),
        new("UT Martin", Hex("#f47321"), Hex("#002d5b")),
        new("Tennessee Tech", Hex("#4b116f"), Hex("#eaaa00")),
        new("Eastern Kentucky", Hex("#652d84"), Hex("#a7a9ac")),
        new("Southeast Missouri State", Hex("#a6192e"), Hex("#000000")),
        new("Murray State", Hex("#002144"), Hex("#f7b500")),
        new("Austin Peay", Hex("#b30838"), Hex("#000000")),
        new("Lamar", Hex("#a80532"), Hex("#ffffff")),
        new("McNeese", Hex("#003876"), Hex("#f2a900")),
        new("Northwestern State", Hex("#582c83"), Hex("#a7a9ac")),
        new("Southern University", Hex("#0033a0"), Hex("#f2a900")),
        new("Jackson State", Hex("#001c54"), Hex("#a7a9ac")),
        new("Grambling State", Hex("#000000"), Hex("#f2a900")),
        new("Alcorn State", Hex("#4b116f"), Hex("#eaaa00")),
        new("North Carolina A&T", Hex("#002946"), Hex("#f2a900")),
        new("Southern Utah", Hex("#c8102e"), Hex("#231f20")),
        new("Portland State", Hex("#00693e"), Hex("#000000")),
        new("Idaho State", Hex("#f47321"), Hex("#000000")),
        new("Holy Cross", Hex("#4f2c1d"), Hex("#a89968")),
    };

    /// <summary>Base roster plus any user-created custom schools (TeamBuilder "add school" v1),
    /// loaded once from ConfigStore's custom_teams.json manifest and kept in sync in-memory by
    /// AddCustomTeam so a newly-added team shows up everywhere immediately, no restart needed.</summary>
    static readonly List<TeamColor> _all = BuildAll();

    // BUG FIX (audit finding): AddCustomTeam used to read/mutate _all with no synchronization.
    // WebView2 host-object calls aren't serialized onto one thread (same reasoning as every lock
    // in ConfigStore.cs), so two near-simultaneous "add school" calls -- or an add racing All's
    // enumeration via ToArray() below -- could interleave a List<T>.Add with another thread's
    // read/resize, which is undefined behavior on a plain List<T> (can throw, or silently produce
    // a duplicate/corrupt roster). Guards the whole check-then-add in AddCustomTeam.
    static readonly object AllLock = new();

    static List<TeamColor> BuildAll()
    {
        var list = new List<TeamColor>(BaseTeams);
        list.AddRange(FcsTeams);
        foreach (var custom in ConfigStore.LoadCustomTeams())
        {
            if (list.Any(t => t.Name.Equals(custom.Name, StringComparison.OrdinalIgnoreCase))) continue;
            try { list.Add(new TeamColor(custom.Name, Hex(custom.PrimaryHex), Hex(custom.SecondaryHex), custom.Mascot)); }
            catch { /* corrupt hex in manifest -- skip this one custom team, don't break the roster */ }
        }
        return list;
    }

    public static TeamColor[] All { get { lock (AllLock) return _all.ToArray(); } }

    public static TeamColor ByName(string name)
    {
        lock (AllLock)
        {
            foreach (var t in _all)
                if (t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return t;
            return _all[0];
        }
    }

    /// <summary>Adds a new user-created custom school (name + primary/secondary color + optional
    /// mascot), persists it, and makes it available immediately via TeamColors.All. Mascot is an
    /// OCR-matching alias only (e.g. the game's scorebug/penalty banner often shows "Bengals"
    /// where the school picker shows "Idaho State") -- it plays no part in theming/colors, see
    /// GameWatcher.HomeTeamMascot/AwayTeamMascot. Returns the existing team unchanged if the name
    /// (case-insensitive) is already taken, rather than creating a duplicate/shadow entry.</summary>
    public static TeamColor AddCustomTeam(string name, Color primary, Color secondary, string mascot = "")
    {
        name = name.Trim();
        mascot = mascot.Trim();
        lock (AllLock)
        {
            var existing = _all.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing.Name != null) return existing;

            var team = new TeamColor(name, primary, secondary, mascot);
            _all.Add(team);
            // ConfigStore.SaveCustomTeam has its own file-level lock (CustomTeamsLock) guarding
            // custom_teams.json itself; nesting it inside AllLock here is fine (SaveCustomTeam
            // never calls back into TeamColors) and keeps the in-memory add and the on-disk
            // persist from interleaving with a second concurrent AddCustomTeam call.
            ConfigStore.SaveCustomTeam(name, ColorTranslator.ToHtml(primary), ColorTranslator.ToHtml(secondary), mascot);
            return team;
        }
    }
}
