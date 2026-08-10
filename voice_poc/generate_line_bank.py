"""
Generates a full commentary line bank for every Bandroom trigger event:
  1. Asks a local LLM (Ollama/Qwen2.5) to write N tagged variation lines per trigger.
  2. Sends each line to the ElevenLabs API to render audio in your cloned voice.
  3. Saves files into line_bank/<category>/<trigger_slug>/01.mp3, 02.mp3, ...

Setup (one time):
  pip install requests
  set ELEVENLABS_API_KEY=your_key_here      (PowerShell: $env:ELEVENLABS_API_KEY = "...")
  Make sure Ollama is running (it auto-starts) with qwen2.5:7b pulled.

Usage:
  python generate_line_bank.py --dry-run                  # just write scripts, no audio/cost
  python generate_line_bank.py --category scoring          # one category, real audio
  python generate_line_bank.py --lines-per-trigger 10      # all categories, fewer lines each
  python generate_line_bank.py                              # everything, default line count

Cost awareness: this calls the paid ElevenLabs API per line. Use --dry-run first
to review the script text for free, and start with --category scoring before
running all 33 triggers.
"""

import argparse
import json
import os
import re
import sys
import time
import unicodedata

import requests

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
TRIGGERS_FILE = os.path.join(SCRIPT_DIR, "triggers.json")
OUTPUT_DIR = os.path.join(SCRIPT_DIR, "line_bank")

OLLAMA_URL = "http://localhost:11434/api/generate"
OLLAMA_MODEL = "qwen2.5:7b"

ELEVENLABS_API_KEY = os.environ.get("ELEVENLABS_API_KEY")
ELEVENLABS_VOICE_NAME = "Russ Robson"
ELEVENLABS_MODEL_ID = "eleven_v3"

DEFAULT_LINES_PER_TRIGGER = 15


def slugify(name: str) -> str:
    name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    name = re.sub(r"[^\w\s-]", "", name).strip().lower()
    return re.sub(r"[\s_]+", "_", name)


def load_triggers(category_filter=None):
    with open(TRIGGERS_FILE, "r") as f:
        data = json.load(f)
    if category_filter:
        if category_filter not in data:
            raise SystemExit(f"Unknown category '{category_filter}'. Options: {list(data)}")
        return {category_filter: data[category_filter]}
    return data


def build_prompt(trigger_name: str, count: int) -> str:
    return f"""You are writing short football broadcast commentary lines for a single
play-by-play announcer named Russ Robson.

PERSONA (must come through in every line): quirky, off-the-wall, expressive,
cool/hip, and real - not a stiff generic broadcaster. He goes fully UNHINGED
shouting on touchdowns and big plays. He naturally drops hip-hop
references and slang ("no cap," "started from the bottom," "main character
energy," "put some respect on it," etc.) and quirky one-liners nobody
expects, but it always feels genuine, never forced or try-hard.

Trigger event: "{trigger_name}"

Write {count} DIFFERENT short lines (1-2 sentences each) this announcer
could say the instant this event happens in the game. Every line must sound
different from the others in wording, structure, and rhythm - no repeated
templates or copy-pasted phrasing with just one word swapped. Not every
line needs a slang reference - vary it, some lines can be pure excitement,
some quirky, some laid back.

Rules:
- No cliche phrases like "the crowd goes wild" or "ladies and gentlemen".
- Mark delivery using ONLY these three ElevenLabs emotion tags in square
  brackets before the words they apply to: [calm], [excited], [shouting].
  Do not invent other tags. Match tag intensity to how big this event
  actually is (a first down is not a touchdown - save [shouting] for the
  biggest moments).
- Do not write stage directions other than these three bracket tags.
- Output ONLY the {count} lines, one per line, numbered 1. through {count}.
  Nothing else - no intro, no explanation.
"""


def generate_lines_via_ollama(trigger_name: str, count: int) -> list[str]:
    prompt = build_prompt(trigger_name, count)
    resp = requests.post(
        OLLAMA_URL,
        json={"model": OLLAMA_MODEL, "prompt": prompt, "stream": False},
        timeout=180,
    )
    resp.raise_for_status()
    text = resp.json()["response"]

    lines = []
    for raw_line in text.splitlines():
        raw_line = raw_line.strip()
        if not raw_line:
            continue
        # strip leading "1. " / "1)" numbering
        cleaned = re.sub(r"^\d+[\.\)]\s*", "", raw_line)
        if cleaned:
            lines.append(cleaned)
    return lines


_voice_id_cache = None


def get_voice_id() -> str:
    global _voice_id_cache
    if _voice_id_cache:
        return _voice_id_cache
    resp = requests.get(
        "https://api.elevenlabs.io/v1/voices",
        headers={"xi-api-key": ELEVENLABS_API_KEY},
        timeout=30,
    )
    resp.raise_for_status()
    voices = resp.json().get("voices", [])
    for v in voices:
        if v.get("name", "").strip().lower() == ELEVENLABS_VOICE_NAME.strip().lower():
            _voice_id_cache = v["voice_id"]
            return _voice_id_cache
    names = [v.get("name") for v in voices]
    raise SystemExit(
        f"Could not find a voice named '{ELEVENLABS_VOICE_NAME}'. Available voices: {names}"
    )


def render_line_to_file(line_text: str, out_path: str):
    voice_id = get_voice_id()
    resp = requests.post(
        f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}",
        headers={
            "xi-api-key": ELEVENLABS_API_KEY,
            "Content-Type": "application/json",
        },
        json={
            "text": line_text,
            "model_id": ELEVENLABS_MODEL_ID,
            "voice_settings": {
                "stability": 0.2,
                "similarity_boost": 0.8,
                "style": 0.6,
            },
        },
        timeout=60,
    )
    if resp.status_code != 200:
        raise RuntimeError(f"ElevenLabs error {resp.status_code}: {resp.text[:300]}")
    with open(out_path, "wb") as f:
        f.write(resp.content)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--category", default=None, help="Only generate this category (e.g. scoring)")
    parser.add_argument("--lines-per-trigger", type=int, default=DEFAULT_LINES_PER_TRIGGER)
    parser.add_argument("--dry-run", action="store_true", help="Write scripts only, no audio/API cost")
    args = parser.parse_args()

    if not args.dry_run and not ELEVENLABS_API_KEY:
        raise SystemExit("ELEVENLABS_API_KEY is not set. See the top of this file for how to set it.")

    triggers = load_triggers(args.category)
    total_chars = 0
    total_lines = 0

    for category, trigger_list in triggers.items():
        for trigger_name in trigger_list:
            slug = slugify(trigger_name)
            trigger_dir = os.path.join(OUTPUT_DIR, category, slug)
            os.makedirs(trigger_dir, exist_ok=True)

            print(f"\n=== {category} / {trigger_name} ===")
            try:
                lines = generate_lines_via_ollama(trigger_name, args.lines_per_trigger)
            except Exception as e:
                print(f"  Skipped (script generation failed): {e}")
                continue

            script_path = os.path.join(trigger_dir, "_script.txt")
            with open(script_path, "w", encoding="utf-8") as f:
                f.write("\n".join(lines))

            for line in lines:
                total_chars += len(line)
            total_lines += len(lines)
            print(f"  Wrote {len(lines)} lines to {script_path}")

            if args.dry_run:
                continue

            for i, line in enumerate(lines, start=1):
                out_path = os.path.join(trigger_dir, f"{i:02d}.mp3")
                if os.path.exists(out_path):
                    print(f"  [{i:02d}] already exists, skipping")
                    continue
                try:
                    render_line_to_file(line, out_path)
                    print(f"  [{i:02d}] rendered -> {out_path}")
                except Exception as e:
                    print(f"  [{i:02d}] FAILED: {e}")
                time.sleep(0.3)  # light rate-limit courtesy

    print(f"\nDone. {total_lines} lines generated across all triggers, ~{total_chars} characters total.")
    if args.dry_run:
        print("(dry run - no ElevenLabs API calls were made, no cost incurred)")


if __name__ == "__main__":
    main()
