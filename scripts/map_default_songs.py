"""
Maps D:\cfb songs filenames to standardized Bandroom EventKeys.
V2: Full FBS scorebug abbreviation knowledge + apostrophe handling.
"""
import os
import shutil
import json
import re

SOURCE = r"D:\cfb songs"
DEST = r"c:\Bandroom\Songs\Default"
INDEX_PATH = os.path.join(DEST, "index.json")

# Complete FBS scorebug abbreviation -> full team name
# (CFB 27 uses 3-letter scorebug codes in-game)
TEAM_MAP = {
    # ACC
    "BC": "Boston College", "Clem": "Clemson", "DU": "Duke", "FSU": "Florida State",
    "GT": "Georgia Tech", "NCST": "NC State", "PITT": "Pittsburgh", "SU": "Syracuse",
    "UL": "Louisville", "UM": "Miami", "UNC": "North Carolina", "UVA": "Virginia",
    "VT": "Virginia Tech", "WF": "Wake Forest", "CAL": "California", "STAN": "Stanford",
    "SMU": "SMU",
    # SEC
    "ALA": "Alabama", "ARK": "Arkansas", "AUB": "Auburn", "UF": "Florida",
    "UGA": "Georgia", "UK": "Kentucky", "LSU": "LSU", "MSST": "Mississippi State",
    "MSU": "Mississippi State",  # scorebug uses MSU for both -- see CONFERENCE_OVERRIDES below
    "MIZZ": "Missouri", "MIZZOU": "Missouri", "OM": "Ole Miss",
    "OLEMISS": "Ole Miss", "OLE MISS": "Ole Miss",
    "SCAR": "South Carolina", "SOCAR": "South Carolina",
    "TENN": "Tennessee", "TAMU": "Texas A&M", "TEX": "Texas", "VANDY": "Vanderbilt",
    "OU": "Oklahoma", "A&M": "Texas A&M",
    # Big Ten
    "ILL": "Illinois", "IND": "Indiana", "IOWA": "Iowa", "MARY": "Maryland",
    "MRLD": "Maryland", "MICH": "Michigan",
    "MINN": "Minnesota", "NEB": "Nebraska", "NW": "Northwestern",
    "OSU": "Ohio State", "PSU": "Penn State", "PUR": "Purdue",
    "RUT": "Rutgers", "WISC": "Wisconsin", "UW": "Washington",
    "UCLA": "UCLA", "USC": "USC", "ORE": "Oregon", "WASH": "Washington",
    # Big 12
    "ARIZ": "Arizona", "ASU": "Arizona State", "BAY": "Baylor", "BU": "Baylor",
    "BYU": "BYU", "CIN": "Cincinnati", "UC": "Cincinnati",
    "COL": "Colorado", "CU": "Colorado", "HOU": "Houston", "UH": "Houston",
    "ISU": "Iowa State", "KU": "Kansas", "KAN": "Kansas",
    "KSU": "Kansas State", "OKST": "Oklahoma State", "TCU": "TCU",
    "TTU": "Texas Tech", "UCF": "UCF", "UTAH": "Utah",
    "UT": "Texas", "WV": "West Virginia", "WVU": "West Virginia",
    # Pac-12
    "ORST": "Oregon State", "WSU": "Washington State", "WAZZU": "Washington State",
    # Independents
    "ND": "Notre Dame",
}

# Event keywords — expanded with all seen patterns
KEYWORD_MAP = [
    # Quarter transitions
    ("4th qtr", "Other: Start of 4th Quarter"), ("4TH Q", "Other: Start of 4th Quarter"),
    ("4q", "Other: Start of 4th Quarter"), ("4thQTR", "Other: Start of 4th Quarter"),
    ("2nd qtr", "Other: Start of 2nd Quarter"), ("2ndQ", "Other: Start of 2nd Quarter"),
    # Pregame/general
    ("pregame", "Other: Pregame Take the Field"),
    ("on field", "Other: Pregame Take the Field"),
    ("takes field", "Other: Pregame Take the Field"),
    ("take the field", "Other: Pregame Take the Field"),
    ("end", "Offense: Victory in Hand"),
    ("vic", "Offense: Victory in Hand"),
    # Kickoff
    ("kickoff", "Other: Opening Kickoff"),
    ("ko alt", "Other: Opening Kickoff"), ("ko ext", "Other: Opening Kickoff"),
    ("ko reg", "Other: Opening Kickoff"), ("KO", "Other: Opening Kickoff"),
    ("ko", "Other: Opening Kickoff"),
    # Scoring
    ("TD", "Offense: Touchdown Scored"),
    ("td alt", "Offense: Touchdown Scored"),
    ("PAT", "Offense: PAT Made"), ("1pt pat", "Offense: PAT Made"),
    ("2pt", "Offense: 2-Point Conversion Made"),
    ("FG", "Offense: Field Goal Made"),
    ("fg miss", "Defense: Field Goal Missed by Opponent"),
    ("fgmiss", "Defense: Field Goal Missed by Opponent"),
    ("saftey", "Defense: Safety"), ("sfty", "Defense: Safety"),
    ("safety", "Defense: Safety"),
    ("all scores", "Offense: Touchdown Scored"),
    ("nonTD scores", "Offense: Field Goal Made"),
    ("non-td scores", "Offense: Field Goal Made"),
    ("scorenoband", "Offense: Touchdown Scored"),
    # Turnovers
    ("trnvr", "Defense: Turnover Forced"), ("trnv", "Defense: Turnover Forced"),
    ("turnover", "Defense: Turnover Forced"), ("turnvr", "Defense: Turnover Forced"),
    ("int", "Defense: Turnover Forced"), ("fumble", "Defense: Turnover Forced"),
    ("opp to", "Defense: Turnover Forced"), ("OPP TO", "Defense: Turnover Forced"),
    ("opp penalty", "Penalty: Offense"),
    # TFL
    ("tfl", "Defense: Tackle for Loss"), ("sack", "Defense: Tackle for Loss"),
    ("sacks", "Defense: Tackle for Loss"),
    # Defense stops (order: 4th, 3rd, 2nd specific first)
    ("4th conv", "Defense: Fourth Down"), ("4th stp", "Defense: Fourth Down"),
    ("4th dn stop", "Defense: Fourth Down"), ("4th stp", "Defense: Fourth Down"),
    ("d 4th", "Defense: Fourth Down"), ("4th conv", "Defense: Fourth Down"),
    ("def 4th", "Defense: Fourth Down"), ("4th st", "Defense: Fourth Down"),
    ("3rd def", "Offense: Third Down"), ("3rd dwn d", "Offense: Third Down"),
    ("3rd off", "Offense: Third Down"), ("3rd dwn", "Offense: Third Down"),
    ("3rd dn", "Offense: Third Down"), ("3rd down", "Offense: Third Down"),
    ("d 3rd", "Defense: Third Down"), ("d 3rdstp", "Defense: Third Down"),
    ("def 3rd", "Defense: Third Down"), ("3rd def", "Defense: Third Down"),
    ("def stops", "Defense: Third Down"), ("d stop", "Defense: Third Down"),
    ("dstop", "Defense: Third Down"), ("dstp", "Defense: Third Down"),
    ("d stp", "Defense: Third Down"), ("def all", "Defense: Third Down"),
    ("2nd dwn", "Offense: Second Down"), ("2nd dn", "Offense: Second Down"),
    ("off 2nd", "Offense: Second Down"), ("O 2nd", "Offense: Second Down"),
    ("d 2nd", "Defense: Second Down"), ("def 2nd", "Defense: Second Down"),
    # 1st down
    ("1st dn", "Offense: Earned First Down"), ("1st dwn", "Offense: Earned First Down"),
    ("1st down", "Offense: Earned First Down"), ("1st dwn", "Offense: Earned First Down"),
    ("first down", "Offense: Earned First Down"), ("opp 1st", "Defense: Earned First Down"),
    ("opp 1st dn", "Defense: Earned First Down"),
    # Defense after KO
    ("d aft ko", "Defense: Second Down"), ("d after ko", "Defense: Second Down"),
    ("daftko", "Defense: Second Down"), ("d aft KO", "Defense: Second Down"),
    ("d&ko alt", "Defense: Second Down"),
    # Offense after KO
    ("O aft ko", "Offense: Earned First Down"), ("o aft ko", "Offense: Earned First Down"),
    ("0 aft ko", "Other: Opening Kickoff"),
    # Drives/misc
    ("drive start", "Offense: Drive Starter"), ("Drive Starter", "Offense: Drive Starter"),
    ("offense", "Offense: Earned First Down"),
    ("defense", "Defense: Third Down"),
    # Timeouts
    ("time", "Defense: Timeout (3 Remaining)"), ("TO", "Defense: Timeout (3 Remaining)"),
    ("times", "Defense: Timeout (3 Remaining)"), ("TIME", "Defense: Timeout (3 Remaining)"),
    ("timeo", "Defense: Timeout (3 Remaining)"),
    # Misc/fallback patterns
    ("off misc", "Offense: Earned First Down"), ("offmisc", "Offense: Earned First Down"),
    ("o misc", "Offense: Earned First Down"), ("off", "Offense: Earned First Down"),
    ("def misc", "Defense: Third Down"), ("d misc", "Defense: Third Down"),
    ("dmisc", "Defense: Third Down"), ("def cheer", "Defense: Third Down"),
    ("d field", "Defense: Third Down"), ("def", "Defense: Third Down"),
    ("misc", "Other: Pregame Take the Field"),
    # Combined patterns (check AFTER specific ones)
    ("tds, pats", "Offense: Touchdown Scored"),
    ("td, fg", "Offense: Touchdown Scored"),
    ("fg, turnovers", "Offense: Field Goal Made"),
    ("pat, fg", "Offense: PAT Made"),
    ("cheer", "Other: Pregame Take the Field"),
    ("dance", "Other: Pregame Take the Field"),
    ("chant", "Other: Pregame Take the Field"),
    ("spellout", "Other: Pregame Take the Field"),
    ("fight", "Other: Pregame Take the Field"),
    ("thunder", "Other: Pregame Take the Field"),
    ("7na", "Other: Pregame Take the Field"),
    ("avngrs", "Other: Pregame Take the Field"),
    ("hail", "Other: Pregame Take the Field"),
    ("glory", "Other: Pregame Take the Field"),
    ("krypton", "Other: Pregame Take the Field"),
    ("iron man", "Other: Pregame Take the Field"),
    ("requiem", "Other: Pregame Take the Field"),
    ("o fortuna", "Other: Pregame Take the Field"),
    ("gladiator", "Other: Pregame Take the Field"),
    ("atomic", "Other: Pregame Take the Field"),
    ("dixie", "Other: Pregame Take the Field"),
    ("boogie", "Other: Pregame Take the Field"),
    ("rocky top", "Other: Pregame Take the Field"),
    ("gangnam", "Other: Pregame Take the Field"),
    ("tequila", "Other: Pregame Take the Field"),
    ("country roads", "Other: Pregame Take the Field"),
    ("bow down", "Other: Pregame Take the Field"),
    ("fightin", "Other: Pregame Take the Field"),
    ("hypnotoad", "Other: Pregame Take the Field"),
    ("frank", "Other: Pregame Take the Field"),
    ("heartbreaker", "Other: Pregame Take the Field"),
    ("fanfare", "Other: Pregame Take the Field"),
    ("kiffin", "Other: Pregame Take the Field"),
    ("objects", "Other: Pregame Take the Field"),
    ("tiger walk", "Other: Pregame Take the Field"),
    ("pig sooie", "Other: Pregame Take the Field"),
    ("tusk", "Other: Pregame Take the Field"),
    ("neck", "Other: Pregame Take the Field"),
    ("go tigers", "Other: Pregame Take the Field"),
    ("east cst", "Other: Pregame Take the Field"),
    ("socal", "Other: Pregame Take the Field"),
    ("penalty", "Offense: Penalty"),
    ("penalty dragnet", "Penalty: Offense"),
    ("reviews", "Other: Pregame Take the Field"),
    ("cob", "Other: Pregame Take the Field"),
    ("wolf", "Other: Pregame Take the Field"),
    ("shut up", "Other: Pregame Take the Field"),
    ("tail slap", "Other: Pregame Take the Field"),
    ("mo bamba", "Other: Pregame Take the Field"),
    ("explicit", "Other: Pregame Take the Field"),
]

def detect_conference(filepath):
    path_upper = filepath.upper()
    if "\\SEC\\" in path_upper or "\\SEC " in path_upper: return "SEC"
    if "\\ACC\\" in path_upper or "\\ACC " in path_upper: return "ACC"
    if "\\B1G\\" in path_upper or "\\B1G " in path_upper: return "Big Ten"
    if "\\BIG12\\" in path_upper or "\\BIG12 " in path_upper or "\\BIG 12" in path_upper: return "Big 12"
    if "\\PAC12\\" in path_upper or "\\PAC12 " in path_upper: return "Pac-12"
    if "\\IND\\" in path_upper: return "Independent"
    if "\\ND\\" in path_upper: return "Independent"
    if "\\1ST\\" in path_upper: return "General"
    if "PATCH" in path_upper: return "General"
    return "General"

def normalize_filename(filename):
    """Pre-process: split apostrophe-concatenated tokens like `ala'22` -> `ala` + `'22`"""
    return re.sub(r"(\w)'(\d)", r"\1 '\2", filename)

# Abbreviations that mean a different team depending on which conference folder
# they're found in, checked before the global TEAM_MAP so the folder context wins.
# - "UM" is Miami's scorebug code (ACC) everywhere EXCEPT inside a \B1G\ folder, where it's
#   shorthand for Michigan (owner call, 2026-08-12).
# - "MSU" is Mississippi State's scorebug code (SEC) everywhere EXCEPT inside a \B1G\ folder,
#   where it means Michigan State. FIXED 2026-08-12: TEAM_MAP used to have "MSU" as a literal dict
#   key twice (once per school) -- Python dict literals silently let the second one win for EVERY
#   lookup, so every "MSU"-named file resolved to Michigan State regardless of which conference
#   folder it actually came from. Real Mississippi State files sitting under a \SEC\ source folder
#   got their TEAM name resolved to "Michigan State" while their CONFERENCE stayed "SEC" (folder-
#   driven, unaffected by the bug) -- producing a second, wrong "Michigan State" entry tagged SEC
#   in the app's team-browse list, alongside the real Big Ten one, while quietly misfiling every
#   Mississippi State default song under the wrong team.
CONFERENCE_OVERRIDES = {
    "Big Ten": {"UM": "Michigan", "MSU": "Michigan State"},
}

def detect_team(filename, conference=None):
    """Extract team abbreviation from normalized filename"""
    parts = filename.split()

    # Skip conference/section prefixes and year tokens
    skip = {"SEC", "ACC", "B1G", "BIG12", "PAC12", "IND", "ND", "1ST", "PATCH", "TEST", "TTU"}
    start = 0
    for i, p in enumerate(parts):
        pu = p.upper().strip("'")
        if pu not in skip and not re.match(r"^'?\d{2}$", pu) and pu not in ("SAMPLES", "ENDS"):
            start = i
            break

    # Build uppercase lookup for case-insensitive matching
    upper_map = {k.upper(): v for k, v in TEAM_MAP.items()}
    override_map = CONFERENCE_OVERRIDES.get(conference, {})

    for length in [2, 1]:
        for offset in range(min(3, len(parts) - start - length + 1)):
            candidate = " ".join(parts[start+offset:start+offset+length])
            cu = candidate.upper()
            if cu in override_map:
                return override_map[cu]
            if cu in upper_map:
                return upper_map[cu]

    for p in parts[start:start+3]:
        pu = p.upper()
        if pu in override_map:
            return override_map[pu]
        if pu in upper_map:
            return upper_map[pu]

    return None

def match_event(filename):
    fname_lower = filename.lower()
    for keyword, event_key in KEYWORD_MAP:
        if keyword.lower() in fname_lower:
            return event_key
    return "Other: Pregame Take the Field"

def safe_filename(s):
    return s.replace(": ", "_").replace(":", "_").replace("/", "_")

def main():
    print("Bandroom Default Songs Mapper v2")
    print(f"Source: {SOURCE}")
    
    if not os.path.exists(SOURCE):
        print(f"ERROR: Source not found: {SOURCE}")
        return
    
    stats = {"total": 0, "mapped": 0, "unmapped": 0, "teams": set(), "events": set()}
    index = {"teams": [], "conferences": {}, "files": {}}
    
    for root, dirs, files in os.walk(SOURCE):
        for f in files:
            if not f.lower().endswith(('.mp3', '.wav', '.m4a', '.flac', '.aiff')):
                continue
            
            fullpath = os.path.join(root, f)
            stats["total"] += 1
            
            normalized = normalize_filename(f)
            conference = detect_conference(fullpath)
            team = detect_team(normalized, conference)
            event_key = match_event(normalized)
            
            if team and conference:
                stats["mapped"] += 1
                stats["teams"].add(team)
                stats["events"].add(event_key)
                
                safe_event = safe_filename(event_key)
                ext = os.path.splitext(f)[1]
                
                dest_dir = os.path.join(DEST, conference, team)
                dest_file = os.path.join(dest_dir, f"{safe_event}{ext}")
                
                os.makedirs(dest_dir, exist_ok=True)
                
                if not os.path.exists(dest_file):
                    shutil.copy2(fullpath, dest_file)
                else:
                    base = safe_event
                    counter = 2
                    while os.path.exists(dest_file):
                        dest_file = os.path.join(dest_dir, f"{base}_{counter}{ext}")
                        counter += 1
                    shutil.copy2(fullpath, dest_file)
                
                if team not in index["files"]:
                    index["files"][team] = {}
                index["files"][team][event_key] = dest_file
                
                if conference not in index["conferences"]:
                    index["conferences"][conference] = []
                if team not in index["conferences"][conference]:
                    index["conferences"][conference].append(team)
            else:
                stats["unmapped"] += 1
                if stats["unmapped"] <= 20:
                    print(f"  SKIP: {f} (team={team} conf={conference})")
    
    index["teams"] = sorted(stats["teams"])
    
    os.makedirs(DEST, exist_ok=True)
    with open(INDEX_PATH, 'w', encoding='utf-8') as idx:
        json.dump(index, idx, indent=2, ensure_ascii=False)
    
    pct = stats['mapped'] / max(stats['total'], 1) * 100
    print(f"\n===== RESULTS =====")
    print(f"Total: {stats['total']}  Mapped: {stats['mapped']} ({pct:.0f}%)  Skipped: {stats['unmapped']}")
    print(f"Teams: {len(stats['teams'])}  Events: {len(stats['events'])}")
    print(f"Index: {INDEX_PATH}")

if __name__ == "__main__":
    main()