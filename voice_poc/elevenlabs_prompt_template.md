# Copy/Paste Script Prompt (ElevenLabs version)

## How to use this
1. Copy the block below labeled "PROMPT TO PASTE".
2. Fill in the 2 blanks (marked ___) with your situation — examples given below.
3. Paste the whole thing into Qwen2.5 (`ollama run qwen2.5`) or DeepSeek, whichever you're using to write the script.
4. It will output the finished script, already tagged for ElevenLabs — just copy that output straight into ElevenLabs, one line at a time, and generate.

You never have to write commentary yourself. You only fill in the two blanks.

---

## PROMPT TO PASTE

```
You are writing a 30-second sports broadcast script for two football
announcers, A (play-by-play, energetic, fast-talking) and B (color
commentary, analytical, calmer energy). Style: Fox Sports / ESPN Saturday
night broadcast.

Situation: ___
Play: ___

Write a natural exchange:
- First ~15 seconds: banter between A and B about the situation (not the
  play yet).
- Then the play happens: A calls it live with escalating excitement, B
  reacts with a short punchy line.
- End on one memorable tagline from A.

Rules:
- No cliche phrases like "the crowd goes wild" or "ladies and gentlemen."
- Use natural half-sentences and realistic pacing, like real announcers
  talking over each other.
- Mark delivery style using ElevenLabs emotion tags in square brackets
  before the part of the line they apply to, e.g. [excited], [shouting],
  [calm], [laughs], [pause]. Start neutral/calm and escalate the tags as
  the play develops, peaking at [shouting] for the score.
- Do not write any stage directions other than these bracket tags.
- Output ONLY alternating labeled lines, nothing else:
  A: ...
  B: ...
  A: ...
```

---

## Examples of what to type in the two blanks

Just pick one pair and paste it into the blanks above — you don't need to write your own.

**Example 1**
- Situation: 4th quarter, 2 minutes left, offense down by 4, 3rd and long from their own 35
- Play: a 62-yard touchdown pass down the sideline with a diving catch in the end zone

**Example 2**
- Situation: 1st quarter, tied 0-0, opening drive
- Play: a trick-play flea flicker for 40 yards

**Example 3**
- Situation: overtime, sudden death
- Play: a game-winning 52-yard field goal in the wind

**Example 4**
- Situation: 3rd quarter, blowout game, garbage time
- Play: a backup running back breaks a 70-yard run

---

## After you get the output

The model will hand you back something like:

```
A: [calm] Alright, third and long here, everything on the line for this offense...
B: [calm] They've gotta convert or this game's basically over.
A: [excited] Snap's away, quarterback drops back, he's got time... he's looking deep...
A: [shouting] HE THROWS IT UP... CAUGHT! TOUCHDOWN! HE DOVE INTO THE END ZONE!
B: [excited] That is an UNBELIEVABLE catch under pressure!
A: [calm] Ballgame. That's how you do it in the fourth quarter.
```

Copy each `A:`/`B:` line (including the bracket tags) straight into ElevenLabs
as separate generations, using your cloned voice with Stability ~25-35% and
Style Exaggeration ~30-50% (from earlier setup). Render each line, then use
the `assemble_clip.py` script in this folder to stitch them into one clip.
