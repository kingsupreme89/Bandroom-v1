"""One-off: render 10 hand-picked lines to sanity-check quality before bulk rendering."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# load .env
env_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
with open(env_path) as f:
    for line in f:
        line = line.strip()
        if line and "=" in line:
            k, v = line.split("=", 1)
            os.environ[k] = v

import generate_line_bank as glb

glb.ELEVENLABS_API_KEY = os.environ["ELEVENLABS_API_KEY"]

lines = [
    "[excited] Yo, check it out! The offense just dropped a touchdown right on the heads of those defenders. No cap!",
    "[calm] Just like that, they've got themselves a touchdown. Respect.",
    "[excited] Oh yeah, they just turned up the heat and cooked the defense for a big score. Started from the bottom...",
    "[shouting] HEY YEAH! THAT'S HOW IT'S DONE! TOUCHDOWN TIME!",
    "[shouting] YOU GUYS! THAT'S THE MOMENT WE'VE BEEN WAITING FOR! TOUCHDOWN!!!",
    "[calm] Alright folks, let's see what happens next! The clock is ticking, and we're not out of this yet!",
    "[excited] Started from the bottom, now they're giving it all they've got! What a game!",
    "[shouting] No cap, they're still in this thing! Let's see how long they can keep up this pace!",
    "[shouting] Main character energy in full swing here, my friends! They've got this in the bag!",
    "[shouting] Respect on the line of scrimmage! They're not backing down from any challenge!",
]

out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "test_render_10")
os.makedirs(out_dir, exist_ok=True)

for i, line in enumerate(lines, start=1):
    out_path = os.path.join(out_dir, f"{i:02d}.mp3")
    print(f"[{i:02d}] rendering: {line[:60]}...")
    try:
        glb.render_line_to_file(line, out_path)
        print(f"  -> {out_path}")
    except Exception as e:
        print(f"  FAILED: {e}")

print("Done.")
