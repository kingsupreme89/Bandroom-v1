"""
BANDroom Audio Intake, Indexing, and Game-Day Metadata Engine.

Converts an uploaded audio file and user-provided details into a standardized,
marketplace-ready audio record for the BANDroom GameWatcher engine.

Usage:
    python intake_engine.py --json '{"original_filename": "..."}'
    python intake_engine.py --batch 'input_list.json'
    python intake_engine.py --file "song.mp3" --team "Alabama" --trigger "TD_HOME"
"""

import json
import os
import re
import sys
import argparse

# ---------------------------------------------------------------------------
# Paths relative to this script
# ---------------------------------------------------------------------------
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_TRIGGER_MAP_PATH = os.path.join(_SCRIPT_DIR, "trigger_event_map.json")
_TEAM_REGISTRY_PATH = os.path.join(_SCRIPT_DIR, "team_registry.json")

# ---------------------------------------------------------------------------
# Lazy-loaded shared data
# ---------------------------------------------------------------------------
_trigger_map = None
_team_registry = None


def _load_trigger_map():
    global _trigger_map
    if _trigger_map is None:
        with open(_TRIGGER_MAP_PATH, "r", encoding="utf-8") as f:
            _trigger_map = json.load(f)
    return _trigger_map


def _load_team_registry():
    global _team_registry
    if _team_registry is None:
        with open(_TEAM_REGISTRY_PATH, "r", encoding="utf-8") as f:
            _team_registry = json.load(f)
    return _team_registry


# ===================================================================
# STAGE A: Title Cleaning
# ===================================================================

# Patterns to strip from filenames/titles
_REMOVE_PATTERNS = [
    # Bitrate / quality tags
    r'\b\d{2,4}\s*kbps\b',
    r'\b\d{2,4}k\b',
    r'\bFLAC\b', r'\bWAV\b', r'\bAIFF\b',
    # Upload/source labels
    r'\[RIP\]', r'\[YT\]', r'\[SC\]', r'\[BC\]',
    r'\(YT\)', r'\(SC\)', r'\(BC\)',
    r'\(Official Audio\)', r'\(Official Video\)',
    r'\(Lyrics?\)', r'\(Audio\)',
    # Versions
    r'\b(final|master|copy|demo|rough|v\d+)\b',
    r'\b(remaster(ed)?)\b',
    # Years (standalone or in parens)
    r'\(\d{4}\)', r'\[\d{4}\]', r'\b(19|20)\d{2}\b',
    # Duplicate extensions
    r'\.mp3\.mp3$', r'\.wav\.wav$',
    # Common noise words at boundaries
    r'\bHD\b', r'\bHQ\b',
    # Leading/trailing garbage
    r'^[\s\-_.,;]+', r'[\s\-_.,;]+$',
    # Trailing numbers after extension removal (leftover auto-numbering)
    r'_\d+$',
]

# Replacements: normalize multi-space, multi-underscore, multi-dash
_CLEANUP_PAIRS = [
    (re.compile(r'[\s_]+'), ' '),       # collapse whitespace/underscores
    (re.compile(r'--+'), '-'),           # collapse dashes
    (re.compile(r'\s*-\s*'), ' - '),     # normalize dash spacing
    (re.compile(r'[\(\[\{].*?[\)\]\}]'), ''),  # strip bracketed content
]


def clean_title(raw: str) -> str:
    """Remove noise from a filename or title string."""
    if not raw:
        return "Unknown"

    s = raw.strip()

    # Strip known extensions
    for ext in (".mp3", ".wav", ".m4a", ".flac", ".aiff", ".ogg", ".wma"):
        if s.lower().endswith(ext):
            s = s[:-len(ext)]

    # Replace underscores with spaces BEFORE regex processing so word
    # boundaries work correctly (underscore is a \w character in regex,
    # which prevents \b from matching at underscore boundaries).
    s = s.replace('_', ' ')

    # Apply regex removals
    for pattern in _REMOVE_PATTERNS:
        s = re.sub(pattern, '', s, flags=re.IGNORECASE)

    # Apply cleanup pairs
    for regex, replacement in _CLEANUP_PAIRS:
        s = regex.sub(replacement, s)

    # Collapse multiple spaces
    s = re.sub(r'\s{2,}', ' ', s).strip().strip('-').strip('_').strip()

    if not s:
        return "Unknown"

    # Title-case-ish: capitalize first letter of words, but preserve acronyms
    words = s.split()
    result = []
    for w in words:
        if w.isupper() and len(w) <= 4:
            result.append(w)  # preserve acronyms like LSU, USC
        else:
            result.append(w[0].upper() + w[1:].lower() if len(w) > 1 else w.upper())
    return ' '.join(result)


# ===================================================================
# STAGE B: Entity Resolution (Team)
# ===================================================================

def resolve_team(name: str):
    """
    Attempt to match a user-provided artist/team/school name to a known
    BANDroom team. Returns (team_name, conference, match_type, band_name_or_unknown).
    match_type is one of: 'exact', 'abbreviation', 'variant', 'fuzzy', 'unknown'.
    """
    if not name or not name.strip():
        return "Unknown", "General", "unknown", "Unknown"

    registry = _load_team_registry()
    teams = registry["teams"]
    alias_index = registry["alias_index"]
    name_clean = name.strip()

    # 1. Exact team name match
    if name_clean in teams:
        t = teams[name_clean]
        return name_clean, t["conference"], "exact", t.get("band_names", ["Unknown"])[0]

    # 2. Alias/abbreviation match (case-insensitive)
    upper = name_clean.upper()
    if upper in alias_index:
        resolved = alias_index[upper]
        t = teams[resolved]
        return resolved, t["conference"], "abbreviation", t.get("band_names", ["Unknown"])[0]

    # 3. Variant match (case-insensitive)
    for team_name, tdata in teams.items():
        for variant in tdata.get("variants", []):
            if variant.lower() == name_clean.lower():
                return team_name, tdata["conference"], "variant", tdata.get("band_names", ["Unknown"])[0]

    # 4. Fuzzy: name appears inside team name or vice versa
    lower = name_clean.lower()
    for team_name, tdata in teams.items():
        if lower in team_name.lower() or team_name.lower() in lower:
            return team_name, tdata["conference"], "fuzzy", tdata.get("band_names", ["Unknown"])[0]

    return "Unknown", "General", "unknown", "Unknown"


# ===================================================================
# STAGE C: Safe Filename Generation
# ===================================================================

def make_safe_filename(team: str, event_or_title: str, cleaned_title: str = "") -> str:
    """
    Generate a filesystem-safe canonical filename.
    Format: TEAM_TRIGGER_CLEANED_TITLE.mp3 (uppercase, underscores only).
    Uses the cleaned_title (with noise removed) rather than raw filename parts.
    """
    parts = [team.upper().replace(' ', '_')]

    # Use the trigger as the event part (e.g., TD_HOME)
    if event_or_title and event_or_title != "Unknown":
        event_part = re.sub(r'[^A-Za-z0-9_]', '', event_or_title.replace(' ', '_').replace('-', '_').upper())
        event_part = re.sub(r'_+', '_', event_part).strip('_')
        if event_part:
            parts.append(event_part)

    # Use the cleaned title (already stripped of noise)
    if cleaned_title and cleaned_title != "Unknown":
        title_part = re.sub(r'[^A-Za-z0-9]', '', cleaned_title.replace(' ', '_').upper())
        title_part = re.sub(r'_+', '_', title_part).strip('_')
        # Avoid duplicating team name already in the prefix
        team_upper = team.upper().replace(' ', '_')
        if title_part and title_part != team_upper and title_part not in parts[-1]:
            parts.append(title_part)

    filename = '_'.join(parts)
    # Remove duplicate .mp3 extension
    if filename.lower().endswith('.mp3'):
        filename = filename[:-4]
    return filename + '.mp3'


# ===================================================================
# STAGE D: Trigger Mapping
# ===================================================================

def map_trigger(user_trigger: str = None, raw_filename: str = "", description: str = ""):
    """
    Determine primary_trigger and secondary_triggers.
    If user_trigger is provided and valid, use it as primary.
    Otherwise, scan filename and description for keyword hints.
    Returns (primary_trigger, [secondary_triggers], trigger_source).
    trigger_source: 'user', 'filename_keyword', 'description_keyword', 'fallback'.
    """
    tm = _load_trigger_map()
    valid = set(tm["valid_triggers"])

    # 1. User-provided trigger (highest confidence)
    if user_trigger and user_trigger.strip() in valid:
        primary = user_trigger.strip()
        # Find related triggers for secondaries
        secondaries = _find_secondary_triggers(primary)
        return primary, secondaries, "user"

    # 2. Keyword scanning in filename
    text = (raw_filename + " " + description).lower()
    keyword_map = {
        "TD_HOME": ["touchdown", " td ", "td scored", "td home"],
        "TD_AWAY": ["away td", "opponent td", "defensive td"],
        "PAT": ["pat", "extra point", "point after"],
        "FIELD_GOAL": ["field goal", "fg ", "fg made"],
        "OFF_1ST_DOWN": ["first down", "1st down", "1st dn"],
        "OFF_3RD_DOWN": ["third down", "3rd down", "3rd dn", "off 3rd"],
        "DEF_3RD_DOWN": ["def third", "def 3rd", "defense third"],
        "DEF_STOP_3RD_4TH": ["fourth down", "4th down", "tackle for loss", "tfl", "stop"],
        "TURNOVER": ["turnover", "fumble", "interception", "int ", "takeaway"],
        "INTERCEPTION": ["interception", "pick", "picked off"],
        "FUMBLE_RECOVERY": ["fumble", "fumble recovery"],
        "OPENING_KICKOFF": ["kickoff", "opening kick", "ko "],
        "KICKOFF_RETURN": ["kick return", "kickoff return", "return"],
        "TIMEOUT": ["timeout", "time out"],
        "PENALTY": ["penalty", "flag"],
        "BIG_PLAY": ["big play", "big gain", "explosive"],
        "RUNOUT": ["runout", "take the field", "pregame", "entrance"],
        "HALFTIME": ["halftime", "half time", "half-time"],
        "POSTGAME": ["postgame", "victory", "post-game", "after game"],
        "GENERAL_HYPE": ["hype", "crowd", "stadium", "cheer", "chant", "fight song", "pep"],
        "PRE_SNAP": ["pre snap", "pre-snap", "presnap"],
    }

    scores = {}
    for trigger, keywords in keyword_map.items():
        score = 0
        for kw in keywords:
            if kw in text:
                score += 1
        if score > 0:
            scores[trigger] = score

    if scores:
        # Sort by score descending, then alphabetically
        ranked = sorted(scores.items(), key=lambda x: (-x[1], x[0]))
        primary = ranked[0][0]
        remaining = [t for t, _ in ranked[1:4]]
        secondaries = _find_secondary_triggers(primary)
        for t in remaining:
            if t not in secondaries and t != primary:
                secondaries.append(t)
        return primary, secondaries[:3], "filename_keyword"

    # 3. Fallback
    return "GENERAL_HYPE", [], "fallback"


def _find_secondary_triggers(primary: str) -> list:
    """Find related triggers based on category groupings."""
    groups = {
        "scoring": ["TD_HOME", "TD_AWAY", "PAT", "FIELD_GOAL"],
        "turnover": ["TURNOVER", "INTERCEPTION", "FUMBLE_RECOVERY"],
        "downs": ["OFF_1ST_DOWN", "OFF_3RD_DOWN", "DEF_3RD_DOWN", "DEF_STOP_3RD_4TH"],
        "kickoff": ["OPENING_KICKOFF", "KICKOFF_RETURN", "PUNT_RETURN"],
        "hype": ["GENERAL_HYPE", "BIG_PLAY", "RUNOUT", "HALFTIME", "POSTGAME"],
    }
    for group_name, members in groups.items():
        if primary in members:
            return [m for m in members if m != primary][:3]
    return []


# ===================================================================
# STAGE E: Clip Duration Recommendation
# ===================================================================

def recommend_clip(trigger: str, duration_seconds: float = None) -> dict:
    """
    Return a recommended clip range based on trigger type.
    Stings: 6-15s, Full songs/runouts: 15-30s, General: 8-20s.
    If actual duration is provided, clamp the recommendation.
    """
    tm = _load_trigger_map()
    clip_info = tm.get("trigger_clip_duration", {}).get(trigger, {})
    if not clip_info:
        clip_info = {"min_sec": 6, "max_sec": 20, "preferred_sec": 12}

    min_s = clip_info.get("min_sec", 6)
    max_s = clip_info.get("max_sec", 20)
    pref = clip_info.get("preferred_sec", 12)

    if duration_seconds is not None and duration_seconds > 0:
        max_s = min(max_s, duration_seconds)
        pref = min(pref, duration_seconds)

    return {
        "start_sec": 0,
        "end_sec": min(pref, max_s),
        "min_recommended_sec": min_s,
        "max_recommended_sec": max_s
    }


# ===================================================================
# STAGE F: Confidence Scoring
# ===================================================================

def compute_confidence(cleaned_title: str, team_match_type: str,
                       trigger_source: str, description: str = "",
                       team_name: str = "") -> float:
    """
    Compute a 0.0-1.0 confidence score.
    Title clarity + team match + trigger specificity + description presence.
    """
    score = 0.0

    # Title: 0.0 - 0.3
    if cleaned_title and cleaned_title != "Unknown":
        # Check for noise words still present
        noise_count = len(re.findall(r'[0-9]{3,}|\b\d+k\b|\(|\)|\[|\]', cleaned_title))
        if noise_count == 0:
            score += 0.3
        elif noise_count <= 2:
            score += 0.2
        else:
            score += 0.1

    # Team: 0.0 - 0.3
    team_scores = {"exact": 0.3, "abbreviation": 0.28, "variant": 0.25, "fuzzy": 0.15, "unknown": 0.0}
    score += team_scores.get(team_match_type, 0.0)

    # Trigger: 0.0 - 0.3
    trigger_scores = {"user": 0.3, "filename_keyword": 0.2, "description_keyword": 0.15, "fallback": 0.05}
    score += trigger_scores.get(trigger_source, 0.05)

    # Description: 0.0 - 0.1
    if description and description.strip() and description.strip().lower() != "unknown":
        score += 0.1

    return round(min(score, 1.0), 2)


# ===================================================================
# STAGE G: Review Flags
# ===================================================================

def generate_flags(cleaned_title: str, team_name: str, team_match_type: str,
                   trigger_source: str, description: str = "",
                   duration_seconds: float = None) -> list:
    """Generate human-review flags for low-confidence or missing data."""
    flags = []

    if cleaned_title == "Unknown":
        flags.append("title_unknown")
    if team_name == "Unknown":
        flags.append("team_unknown")
    if team_match_type in ("fuzzy", "unknown"):
        flags.append("team_match_low_confidence")
    if trigger_source == "fallback":
        flags.append("trigger_fallback_assigned")
    if trigger_source == "filename_keyword":
        flags.append("trigger_derived_from_filename")
    if not description or description.strip().lower() == "unknown":
        flags.append("description_missing")
    if duration_seconds is None:
        flags.append("duration_missing")
    if team_match_type == "fuzzy":
        flags.append("team_fuzzy_match_review")

    return flags


# ===================================================================
# CORE ENGINE: Process a single record
# ===================================================================

def process_record(original_filename: str, song_title: str = None,
                   team_name: str = None, artist: str = None,
                   description: str = None, user_event_trigger: str = None,
                   duration_seconds: float = None) -> dict:
    """
    Main entry point. Accepts raw inputs and returns a complete
    bandroom_audio_record as a dict.

    All fields except original_filename are optional. Missing values
    become "Unknown" and generate review flags.
    """
    # --- Title ---
    if not song_title:
        # Derive from filename
        cleaned = clean_title(original_filename)
    else:
        cleaned = clean_title(song_title)

    # --- Team ---
    team_input = team_name or artist or ""
    resolved_team, conference, match_type, band_name = resolve_team(team_input)

    # --- Trigger ---
    primary_trigger, secondary_triggers, trigger_source = map_trigger(
        user_trigger=user_event_trigger,
        raw_filename=original_filename,
        description=description or ""
    )

    # --- Safe filename ---
    safe_name = make_safe_filename(resolved_team, primary_trigger, cleaned)

    # --- Clip recommendation ---
    clip = recommend_clip(primary_trigger, duration_seconds)

    # --- Confidence ---
    confidence = compute_confidence(
        cleaned_title=cleaned,
        team_match_type=match_type,
        trigger_source=trigger_source,
        description=description or "",
        team_name=resolved_team
    )

    # --- Review flags ---
    flags = generate_flags(
        cleaned_title=cleaned,
        team_name=resolved_team,
        team_match_type=match_type,
        trigger_source=trigger_source,
        description=description or "",
        duration_seconds=duration_seconds
    )

    # --- Artist/band ---
    artist_field = artist or band_name if band_name != "Unknown" else "Unknown"
    if artist_field == "Unknown" and resolved_team != "Unknown":
        registry = _load_team_registry()
        t = registry["teams"].get(resolved_team, {})
        artist_field = t.get("band_names", ["Unknown"])[0] if t.get("band_names") else "Unknown"

    return {
        "bandroom_audio_record": {
            "canonical_filename": safe_name,
            "original_filename": original_filename,
            "cleaned_title": cleaned,
            "team_name": resolved_team,
            "conference": conference,
            "artist": artist_field,
            "description": description if description else "Unknown",
            "primary_trigger": primary_trigger,
            "secondary_triggers": secondary_triggers,
            "recommended_clip": clip,
            "confidence": confidence,
            "review_flags": flags
        }
    }


# ===================================================================
# CLI INTERFACE
# ===================================================================

def main():
    parser = argparse.ArgumentParser(
        description="BANDroom Audio Intake & Metadata Engine"
    )
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--json", type=str, help="Single JSON record as a string")
    group.add_argument("--batch", type=str, help="Path to JSON file containing an array of records")
    group.add_argument("--file", type=str, help="Original filename (use with --team, --trigger, etc.)")

    parser.add_argument("--title", type=str, default=None, help="Song title")
    parser.add_argument("--team", type=str, default=None, help="Team or school name")
    parser.add_argument("--artist", type=str, default=None, help="Artist or band name")
    parser.add_argument("--description", type=str, default=None, help="Audio description or lyrics")
    parser.add_argument("--trigger", type=str, default=None, help="User-selected game event trigger")
    parser.add_argument("--duration", type=float, default=None, help="Clip duration in seconds")
    parser.add_argument("--pretty", action="store_true", help="Pretty-print JSON output")

    args = parser.parse_args()
    indent = 2 if args.pretty else None

    if args.json:
        # Single JSON input
        try:
            record = json.loads(args.json)
        except json.JSONDecodeError as e:
            print(json.dumps({"error": f"Invalid JSON: {e}"}))
            sys.exit(1)
        result = process_record(
            original_filename=record.get("original_filename", "unknown.mp3"),
            song_title=record.get("song_title"),
            team_name=record.get("team_name"),
            artist=record.get("artist"),
            description=record.get("description"),
            user_event_trigger=record.get("user_event_trigger"),
            duration_seconds=record.get("duration_seconds")
        )
        print(json.dumps(result, indent=indent))

    elif args.batch:
        # Batch processing
        if not os.path.exists(args.batch):
            print(json.dumps({"error": f"File not found: {args.batch}"}))
            sys.exit(1)
        with open(args.batch, "r", encoding="utf-8") as f:
            try:
                records = json.load(f)
            except json.JSONDecodeError as e:
                print(json.dumps({"error": f"Invalid JSON in batch file: {e}"}))
                sys.exit(1)
        if not isinstance(records, list):
            print(json.dumps({"error": "Batch file must contain a JSON array"}))
            sys.exit(1)

        results = []
        for i, rec in enumerate(records):
            try:
                result = process_record(
                    original_filename=rec.get("original_filename", f"unknown_{i}.mp3"),
                    song_title=rec.get("song_title"),
                    team_name=rec.get("team_name"),
                    artist=rec.get("artist"),
                    description=rec.get("description"),
                    user_event_trigger=rec.get("user_event_trigger"),
                    duration_seconds=rec.get("duration_seconds")
                )
                results.append(result)
            except Exception as e:
                results.append({
                    "error": str(e),
                    "original_filename": rec.get("original_filename", f"unknown_{i}.mp3")
                })
        print(json.dumps(results, indent=indent))

    elif args.file:
        # CLI flags mode
        result = process_record(
            original_filename=args.file,
            song_title=args.title,
            team_name=args.team,
            artist=args.artist,
            description=args.description,
            user_event_trigger=args.trigger,
            duration_seconds=args.duration
        )
        print(json.dumps(result, indent=indent))


if __name__ == "__main__":
    main()