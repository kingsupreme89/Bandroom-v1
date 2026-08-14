🎺 i must not 
- **Search box fixed** — no longer cut off by the header bar
- **Game Settings popover actually works now** — real bug, not confusing UI. Fixed

---

## 🎚️ The Sound Booth — Eight Effects, One Rack

This is the centerpiece of v1.1. The Sound Booth is a floating audio rack — open it from any Clipper Island panel or the nav rack, and you get full control over exactly how your stadium sounds. Every effect has its own (i) button that explains what it does in plain English. No guesswork.

- **Reverb** — Off / Stadium / Night Game / Prime Time. Each one models a real acoustic space. Stadium gives you that open-air tail you hear bouncing off the bowl. Night Game is tighter and warmer, like cooler air. Prime Time is the big-game-under-the-lights version
- **Marching Band EQ** — cleans up muddy band recordings, tames boomy tuba, brings out trumpets and snare. Plus **Megaphone mode** that makes anything sound like it's blasting through an old stadium PA speaker
- **Transient Shaper** — gives drums and cymbals extra crack without turning up the whole song
- **Stereo Widener** — takes narrow, one-dimensional recordings and spreads them across both speakers
- **Sub-Bass Thump** — Off / Subtle / Stadium / Earthquake. Adds a low rumble under big hit plays. Earthquake setting is strong enough to rattle a subwoofer
- **Ducking** — auto-lowers music on big plays so the crowd and announcer cut through, then brings it back up
- **Controller Rumble** — buzzes your Xbox/PlayStation controller in close late-game moments
- **Crowd Bus** — looping crowd noise that gets louder automatically when the game gets close and it's the 4th quarter. You pick your own crowd audio file and Bandroom handles the rest

Preview any song through the full effects chain right in the Sound Booth — this is what it'll actually sound like during a game, not a raw file preview.

---

## ✂️ The Inline Trimmer — Built Right Into the App

No external app. No separate window. Click **Trim** on any song in the Clipper Island and a full waveform editor opens right there — decoded and rendered in real time on an HTML5 canvas.

- Blue waveform showing actual audio peaks
- Drag handles for start and end trim points — labels update in real time
- **Zoom up to 800%** — frame-precise trimming. Click and drag to pan when zoomed in
- **End-tail auto-preview** — release the end handle and it automatically plays the last 4 seconds of your trim range so you don't have to hit play every time you tweak
- **Auto-normalize on save** — every trimmed clip is volume-matched to your library so nothing sounds louder or quieter than everything else
- When you save a trim, the event card you came from auto-scrolls into view and flashes so you know exactly what just got updated

---

## 🛒 The Bandroom Marketplace & Sound Banks

Every team has its own **Sound Bank** — a private folder of songs and backgrounds. **The Bandroom** marketplace lets every user browse, download, and upload to any team's Sound Bank. It's a community library that grows with every user.

- **Browse any team's Sound Bank** — open any school, see everything uploaded for them
- **Download** songs and backgrounds — they go to My Downloads, ready to assign
- **Upload your own** — songs auto-normalize on upload so your volume matches everyone else's
- **Like / Dislike** on every upload — the Popular Songs shelf ranks everything by downloads + likes combined, so the best stuff floats to the top automatically
- **Browse Other Team's Sound Bank** from inside the Clipper Island — assign a song to your away team by pulling from their library without switching your active team
- **Profile Sharing** — share your ENTIRE team setup (all 46 event assignments) as a link. Someone else loads it and their whole team is configured in one click

The marketplace is live, the workers are deployed, and every Bandroom user contributes to a shared library that gets better the more people use it.

---

## 🎵 46 Assignable Events — Every Situation Covered

The event system is fully wired. Six categories, 46 unique moments, every single one assignable:

| Category | Events | What You'd Assign |
|---|---|---|
| **Downs** | 14 | First down (standard/big gain/midfield), 2nd/3rd/4th down with loss/midfield/short variants |
| **Scoring** | 7 | Touchdown, field goal, PAT, 2-point conversion (offense + defense versions of each) |
| **Turnovers** | 2 | Forced turnover, iced game by turnover |
| **Special Teams** | 5 | Opening kickoff, 2nd-half kickoff, regular kickoff, kicking/receiving splits |
| **Penalties** | 2 | Offense/defense — fires for the opposite side (penalty against them = celebration for you) |
| **Hype** | 14 | Take the field, quarter starts, drive starters, after opening kick, victory in hand, all 5 timeout counts |

**Every event card has eight controls:**
- **Assign / Edit** — opens the Clipper Island song picker
- **Assign PA** — a second audio layer (stadium announcer voice) that plays under your main song
- **Copy From** — clone a song + PA clip + whistle from any other event card. The single biggest time-saver in the app
- **Play / Stop** — preview with full effects chain applied
- **Per-event volume** — your touchdown song CAN be louder than your 2nd-quarter-start song
- **Per-event whistle toggle** — referee whistle on or off for each individual situation
- **Track Info drawer** — title, artist, acoustic fingerprint metadata editor
- **Speed toggle** — 1.09x tempo bump

---

## 💾 Team Presets — Four Profiles Per Team

Every team now has **Plain / Home / Away / Big Game** — four completely independent song profiles. Georgia's Big Game touchdown song can be totally different from their everyday Plain touchdown song. Copy any preset → any other with the dropdown. Start with your Home setup, copy it to Big Game, swap out a few songs for rivalry week. Bandroom auto-picks the right preset at GAMETIME.

---

## 🔍 Command Palette (Ctrl+K)

Press Ctrl+K anywhere. Type anything you want to do — "marketplace", "matchup", "streamer", "shortcuts", "song pack", "reset", "relocate", "save", "tips", "help" — and jump straight there. If you learn one power-user trick, make it Ctrl+K.

---

## 🏈 Engine — 6 Detection/Routing Fixes

Diagnosed and fixed during live games:

- **Timeout detection** now reads the real TIME OUT banner instead of guessing from the clock — works any time in the game, not just the 2-minute drill
- **Standalone Kickoff cue** restored — no more silence after a TD because the PAT text didn't read perfectly
- **Possession-cooldown wrong-side-routing fix** — TFL and 4th down no longer fire for the wrong team
- **Take the Field chevron-arrow detection** — catches the team walkout moment sooner by watching for the white arrow on screen
- **New Offense: After Opening Kick** — both teams fire on the opening kickoff (offense full, defense ducked)
- **New 2nd Down Short dual-fire pairing** — offense loud, defense ducked

---

## 📊 Everything Else That's New

- **Guided Assign** — step through every event one at a time, confirm or pick from candidates. Comes with keyword-matching if the pack doesn't have an exact match
- **Event Log** — live feed of what Bandroom played or skipped, and why. Exportable to a file
- **Streamer Mode** — hides personal info for broadcasting
- **Firing Delay** — 0–5 seconds for syncing with broadcast delay
- **Volume Profile Presets** — save and swap different master/home/away/PA balances
- **Config Profile Manager** — save and switch entire Bandroom setups
- **Quick-load by abbreviation** — type "LSU" and it suggests the matching team instantly
- **Auto-Assign** — Quick Overwrite (fill everything at once) or Guided Assign (step through one at a time)
- **Default Song Pack** — ~950 pre-matched songs across 62 teams, free one-time download
- **Universal Profile Override** — same songs no matter which team you're using, for streamers
- **Team Logos & Backgrounds** — upload, crop, and share custom images. 184 built-in teams with real logos
- **In-App Changelog** — see what's new without leaving the app
- **Crash Reporting** — automatic crash logs so bugs are fixable
- **Version Compatibility Guard** — old profiles don't break on new versions
- **Batch logo import** — folder of logos, imported at once
- **Repo now private** — we share builds, not source code

---

## 🏗️ What's Still In Progress

- Mac full UI (engine + audio already run natively)
- Calibration for non-CFB27 scorebug styles
- Font pick for the matchup-screen team name

---

Bandroom v1.1 Early Access. Tested live during real games, fixed as problems were found, and built to make your stadium sound like you're actually there. Check the Event Log if something doesn't fire — it tells you exactly why. Ping us in Discord if you find something we haven't. 🎺