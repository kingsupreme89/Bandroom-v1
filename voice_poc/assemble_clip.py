"""
Stitches individually-rendered announcer line clips into one 30-second broadcast clip.

How to use:
1. Render each script line separately in GPT-SoVITS, saving files named in
   speaking order, e.g.: 01_A.wav, 02_B.wav, 03_A.wav, ...
2. Put them all in the `lines/` folder next to this script.
3. (Optional) put a short cut/stinger sound effect at `sfx/stinger.wav` to play
   right before the big-play line — set STINGER_BEFORE_FILE below to match.
4. Run: python assemble_clip.py
5. Output: output/commentary_clip.mp3

Requires: pip install pydub
Also requires ffmpeg installed and on PATH (pydub uses it under the hood).
"""

import os
from pydub import AudioSegment
from pydub.effects import normalize

LINES_DIR = "lines"
SFX_DIR = "sfx"
OUTPUT_DIR = "output"
OUTPUT_FILE = os.path.join(OUTPUT_DIR, "commentary_clip.mp3")

# Filename (in lines/) of the line right before the big play — the stinger
# sound effect will be inserted right before this file. Set to None to skip.
STINGER_BEFORE_FILE = None  # e.g. "03_A.wav"
STINGER_FILE = os.path.join(SFX_DIR, "stinger.wav")

# Silence gap between lines, in milliseconds. Real broadcast banter overlaps
# slightly rather than pausing, so keep this small (or even 0).
GAP_MS = 120


def load_lines():
    if not os.path.isdir(LINES_DIR):
        raise SystemExit(f"Missing folder: {LINES_DIR}/ — put your rendered wav files there first.")
    files = sorted(f for f in os.listdir(LINES_DIR) if f.lower().endswith((".wav", ".mp3")))
    if not files:
        raise SystemExit(f"No audio files found in {LINES_DIR}/")
    return files


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    files = load_lines()

    gap = AudioSegment.silent(duration=GAP_MS)
    stinger = None
    if STINGER_BEFORE_FILE and os.path.exists(STINGER_FILE):
        stinger = AudioSegment.from_file(STINGER_FILE)

    clip = AudioSegment.empty()
    for fname in files:
        if stinger is not None and fname == STINGER_BEFORE_FILE:
            clip += stinger + gap
        segment = AudioSegment.from_file(os.path.join(LINES_DIR, fname))
        clip += segment + gap

    clip = normalize(clip)  # even out volume across lines rendered separately

    duration_s = len(clip) / 1000
    print(f"Assembled {len(files)} lines -> {duration_s:.1f}s total")
    if duration_s > 32:
        print("Note: clip is longer than ~30s target — consider trimming a line or shortening the script.")

    clip.export(OUTPUT_FILE, format="mp3")
    print(f"Wrote {OUTPUT_FILE}")


if __name__ == "__main__":
    main()
