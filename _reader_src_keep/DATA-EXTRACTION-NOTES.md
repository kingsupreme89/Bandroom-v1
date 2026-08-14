
# Automatic data extraction test

This v1.3.49 test snapshot combines the screen pipeline with the confirmed
read-only RAM reader:

- validated scorebug state -> `scoreboard.csv`
- complete accepted state -> `scoreboard.jsonl` and `latest-scoreboard.json`
- score deltas -> `events.jsonl`
- full-window sparse OCR -> `screen-text.jsonl`
- touchdown/penalty/statistics classification and matching screenshots
- live RAM scoreboard state -> `live-game-data.json`
- embedded RAM reader health -> `ram-reader-status.json`

Output is stored under the app's `data-export/<session-id>` directory. The
packaged test uses folder-local `UserData` so it does not share settings or
output with the original installed app.

The packaged Windows app starts `ram-reader/CollegeFB27RamReader.exe` as a
hidden child process. It requests read-only process access, follows game-process
restarts, restarts automatically if the helper exits, and stops when the
scorebug app closes. Its current state is also shown on the Diagnostics page.
Confirmed fields are layered over the renderer scoreboard state before it is
published to the visible bug; the screen reader remains the automatic fallback.

Testers can choose the live scoreboard source from **Reader & live data**:

- **Automatic** prefers RAM and falls back to screen-reader values.
- **RAM reader only** publishes confirmed RAM values only. Screen capture still
  detects whether the native scorebug is visible.
- **Screen reader only** publishes OCR values and turns off the hidden RAM
  reader.

Team rankings are not yet read from RAM and remain a planned follow-up field.

The team-name locator searches the complete active Frostbite team pool and
restricts display-name candidates to the two active matchup tradition slugs.
This supports matchups whose live name buffers sit far from the Team Home
marker, including Oregon vs. Virginia.