---
title: "Bandroom Handbook"
subtitle: "The complete, in-depth guide to Bandroom"
author: "Bandroom"
date: "2026-08-12"
toc: true
toc-depth: 2
---

# TeamBuilder — Adding Your Own School

If your school isn't in Bandroom's built-in team list, TeamBuilder lets you add it yourself in about 30 seconds. This section covers how to create one, and — just as important — what a custom TeamBuilder team can and can't do compared to a built-in team.

## Creating a TeamBuilder Team

1. Open the full **Team picker** and click **Add School**.
2. Type your school's name.
3. Pick a **primary color** and a **secondary color** — the two main colors of your team. Bandroom uses these to color the whole app to match, the same way it does for every built-in school.
4. Click the button to save it. Your new school appears immediately in the Team picker — no restart needed.
5. Give it a logo: find your new school's tile in the Team picker and click the small pencil icon on it. This opens the logo tool, where you can upload and crop a picture to use as its logo.

That's it — your school now behaves like any other team in the picker, Set Matchup, and the rest of the app.

## How a TeamBuilder Team Actually Works With the Game Engine

This is the part that trips people up, so read it carefully.

**A TeamBuilder team is a skin and a sound bank — not a source of automatic detection.**

When Bandroom watches your game, it isn't guessing what's happening — it's reading text and colors directly off your screen (the scoreboard, the penalty banner, the down-and-distance display, and so on). That only works for schools the video game itself actually knows about and displays. A school you typed into TeamBuilder doesn't exist inside the video game, so there's nothing on screen for Bandroom to read for that school specifically.

**What you DO get automatically, just by creating a TeamBuilder team:**

- The app's colors switch to match your school the moment it's active
- Its own private **Sound Bank** — a folder of songs and pictures that belongs only to this team
- A spot in the Team picker and in **Set Matchup**, exactly like a built-in school

**What you do NOT get automatically:**

- No touchdown, score, or possession detection tied to your custom school by name
- No penalty-side reading, no field-position reads — none of the screen-reading ("OCR") features fire because your school said so
- Bandroom cannot tell when "your team" scored, because your team doesn't exist in the game's own data

**So how do you actually use a TeamBuilder team in a real game?**

The trick is: you separate *what the engine watches* from *what plays and what it looks like*.

1. In the video game itself, you're always playing as one of the game's real, built-in schools (whatever roster the game actually ships with) — that part never changes, TeamBuilder doesn't add your school into the video game.
2. In Bandroom's **Set Matchup**, pick that same real, built-in school as your side. This is the school Bandroom's engine will actually watch the screen for — scores, downs, possession, penalties, all of it.
3. Switch your **active team** in the Team panel to your TeamBuilder school. This is what makes the app's colors and Sound Bank match *your* school, regardless of which real school Set Matchup is watching.
4. Assign songs to situations under your TeamBuilder team as normal (see the Clipper / song-assigning section). When the engine detects a real event off the real school in Set Matchup — say, a touchdown — Bandroom plays whatever song *you* assigned under your TeamBuilder team for "Touchdown."

**In one sentence:** Set Matchup tells the engine what to watch for; your active TeamBuilder team tells Bandroom what to look like and what to play. They don't have to be the same school, and for a custom school, they never will be.

### Quick Reference

| | Built-in school | TeamBuilder (custom) school |
|---|---|---|
| Shows up in Team picker / Set Matchup | Yes | Yes |
| App colors match it | Yes | Yes |
| Has its own Sound Bank | Yes | Yes |
| Engine auto-detects scores/plays for it | Yes | No — it isn't in the video game |
| How to still trigger its songs during a game | Just play as it | Set Matchup uses your real in-game school; keep your TeamBuilder school as the *active* team so its songs/colors are what plays |
