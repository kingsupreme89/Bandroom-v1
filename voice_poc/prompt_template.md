# Commentary Script Prompt Template (for Qwen2.5 via Ollama)

Fill in the [brackets], paste into `ollama run qwen2.5`, and generate 3-5 times.
Pick the best one (or paste all 5 back to Qwen2.5 and ask it to pick the best).

---

## Base prompt

You are writing a 30-second sports broadcast script for two football announcers,
A (play-by-play, energetic, fast-talking) and B (color commentary, analytical,
slightly deeper energy). Style: Fox Sports / ESPN Saturday night broadcast.

Situation: [describe the game situation, e.g. "4th quarter, 2 minutes left,
offense down by 4, 3rd and long from their own 35"]

Play: [describe the specific play, e.g. "a 62-yard touchdown pass down the
sideline with a diving catch in the end zone"]

Write a natural, in-character exchange:
- First ~15 seconds: banter/setup between A and B about the situation (not the
  play yet — build tension).
- Then the play happens: A calls it live with escalating excitement, B reacts
  with a short, punchy analytical line.
- End on one memorable tagline from A.

Rules:
- Do NOT use cliché broadcast phrases: "and the crowd goes wild", "back and
  forth", "she shoots she scores", "ladies and gentlemen".
- Use natural interruptions, half-sentences, and realistic broadcast pacing —
  real announcers talk over each other and trail off mid-thought sometimes.
- Mark emotional intensity directly in the text using CAPS for shouted words
  and ellipses (...) for pauses/holds, since the voice engine reads inflection
  from punctuation and capitalization, not from stage directions.
- Do NOT write stage directions like "(excited)" — only the words to be spoken.
- Output as alternating labeled lines only, nothing else:
  A: ...
  B: ...
  A: ...

---

## Variation prompts (swap the "Situation"/"Play" lines above)

- Situation: 1st quarter, tied 0-0, opening drive. Play: a trick-play flea
  flicker for 40 yards.
- Situation: 3rd quarter, blowout game, garbage time. Play: a backup RB breaks
  a 70-yard run.
- Situation: overtime, sudden death. Play: a game-winning field goal from 52
  yards in the wind.

---

## Self-rating follow-up prompt (optional, after generating 3-5 versions)

Paste this after pasting all your generated scripts back in:

"Here are 5 versions of the same broadcast script. Rate each 1-10 on how
natural/non-generic it sounds versus a real Saturday night broadcast, and pick
the single best one. Explain in one sentence why."
