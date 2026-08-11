# 🎬 THE BANDROOM EVENT CARD & CLIPPER — FULL WALKTHROUGH
## A YouTube Tutorial Script (8+ minutes)
### Tone: Funny, witty, sarcastic — but you'll actually learn something

---

## [0:00–0:45] COLD OPEN — "What Even Is This Thing?"

*(Show Bandroom main screen, lots of glass panels, LED dots pulsing)*

**You:** "Welcome back to Bandroom. If you're new here — Bandroom is the app that listens to your college football game through OCR, detects what's happening — touchdown, turnover, third down — and plays YOUR custom stadium music through YOUR speakers. It's like being the stadium DJ, except you don't have to wear a polo shirt and pretend to like the marching band's arrangement of 'Seven Nation Army.'

Today we're doing a DEEP dive on ONE feature. The event card. The song list. The clipper. The whole pipeline from 'I need a song for this situation' to 'that song is now trimmed, assigned, and ready to fire when Georgia converts a third and short.'

This is the most important screen in the app, and nobody's ever walked through it properly. So here we go."

---

## [0:45–2:00] SECTION 1 — THE BAND ROOM & EVENT CARDS

*(Click through: header → Situations panel opens. Show the category tabs — Offense, Defense, Hype, etc.)*

**You:** "This is the Band Room. It used to be called Situations, then it was called Assignments, then we gave up and called it both. Pick a category — let's say Offense. You're now looking at event cards.

Each card represents ONE thing that can happen in a game. 'Offense: Touchdown Scored.' 'Offense: Earned First Down.' 'Offense: Third Down Short.' There are 46 of these across the whole app. Forty-six. That's not a bug — that's 46 opportunities to play the right song at the right moment.

Now look at the card itself. That little dot on the left — that's your status LED. Green means 'assigned and confirmed.' Amber means 'assigned but never verified.' Dim means 'nothing assigned yet' — also known as 'the app will play silence and your stream will be awkward.' Don't be the dim dot guy.

Below the event name you see the filename. Or 'Unassigned.' If you see 'dies irae 0.wav' — congratulations, you found a legacy default that literally cannot play because the routing code changed six sessions ago. We'll fix that eventually. Moving on."

---

## [2:00–3:15] SECTION 2 — THE BUTTON BAR (aka "Why Are There Eight Buttons")

*(Hover over each button on an event card, one at a time)*

**You:** "Alright, the button bar. There are eight things you can click here and I'm going to explain all of them because nobody reads tooltips.

**Assign / Edit** — opens the song picker. This is the main event. We're gonna spend half this video on what happens after you click this.

**Assign PA** — same thing, but for the PA Announcer layer. Bandroom can play TWO audio files at once per event. Your main hype song AND a separate announcer clip. 'Third and long for the Bulldogs' layered under your stadium banger. It's actually sick when it works.

**Copy From** — new as of last update. Say you already set up 'Offense: Second Down Short' with a perfect song. You click Copy From, pick that event from a dropdown, and BOOM — same song, same PA clip, same whistle setting, copied over. Saves you from doing the same search three times for three different down-and-distance variants. Whoever asked for this — you're a genius.

**Play / Stop** — previews the assigned song. With all effects applied. The actual thing that would fire in a game. Not a watered-down version.

**Volume** — opens a tiny popover slider. Each event card has its OWN volume independent of the master. Because maybe your touchdown song should be louder than your 'start of the second quarter' song. Just maybe.

**Whistle toggle** — that little flag icon. Turns the lead-in whistle on or off for THIS specific event. Some songs start better with a referee whistle before them. Some don't. You decide.

**Track Info** — opens a drawer with metadata. Title, artist, school, energy level, recommended trim points, acoustic fingerprint. It's like Shazam but for your own files and with way more nerd stuff."

---

## [3:15–4:45] SECTION 3 — THE CLIPPER ISLAND (Song Picker)

*(Click Assign / Edit on a card. Clipper Island slides up from the bottom)*

**You:** "When you click Assign or Assign PA, this panel slides up. We call it Clipper Island. It has everything: song library, search, filters, team browser, trimmer, Sound Booth shortcut.

At the top you see what event you're assigning FOR. Below that — the current assignment. 'Current: Touchdown Song.mp3' or 'Current: (none assigned)' — also known as 'your stream is about to be real quiet on this play.'

Below that — **source filters**. This is important because people get confused here.

**'Sound Bank'** is the default — it shows songs from the Bandroom Default Song Pack. These are pre-mapped to your team and your events. Keyword-matched. If you loaded the pack, you'll see suggestions here automatically.

**'Marketplace Downloads'** — songs you downloaded from the community marketplace. Other people's uploads.

**'Trimmed Clips'** — songs YOU trimmed yourself using the built-in trimmer. They get saved separately so you always know which version is the trimmed one.

**'Your Imports'** — songs you dragged in from your own computer. Your personal collection.

**'All Songs'** — everything, combined, no filter. This can be overwhelming. It's listed last for a reason.

There's also a **'Browse Other Team's Sound Bank'** button. Opens a team picker. Super useful when you're setting up the away team and thinking 'I bet Alabama's fight song would work here too.'

Each song in the list has its own mini play button, stop button, and a source label so you know where it came from. Click a row to select it. Then hit Assign Selected. Done."

---

## [4:45–6:00] SECTION 4 — THE INLINE TRIMMER

*(Click 'Trim...' on an assigned song, or select a song and click Trim in whistle mode)*

**You:** "Now this part is LEGITIMATELY cool and nobody talks about it enough.

The Trim... button opens a waveform editor. Right here. In the same panel. No separate window. No external app. It loads the actual audio waveform, decoded in JavaScript, rendered on a canvas element.

You see that blue waveform? Those are the actual peaks of your audio file. The shaded region between the two handles — that's your trim range. Everything outside that gets cut off when you save.

Drag the start handle. Drag the end handle. The labels update in real time. You can zoom in — up to 800% — for frame-precise trimming. There's a zoom slider AND zoom in/out buttons. When you zoom, you can click and drag to pan around the waveform like you're in a DAW. It's bananas that this runs in a WebView.

**End tail auto-preview**: release the end handle and it automatically plays the last four seconds of your trimmed range. So you don't have to hit play every time you tweak the end point. TrimmerForm on the C# side has the same behavior, but this one runs IN THE BROWSER.

Hit Preview to hear your trim range. Hit Stop to silence it. Hit Save Trim — the trimmed clip gets saved to your Songs folder, and the event card you came from AUTO-SCROLLS into view and flashes so you know exactly which card just got updated. That last part is new. Before the last update, you saved a trim and just... landed back in the song list with no idea what happened. Now it's obvious.

If you're in whistle mode — opened from the Settings panel instead of an event card — 'Save Trim' is replaced by 'Set as Lead-In Whistle.' Same trimmer, different save destination."

---

## [6:00–7:15] SECTION 5 — SOUND BOOTH, PREVIEW, & THE AUDIO CHAIN

*(Click the Sound Booth button from Clipper Island, or from the rack)*

**You:** "While we're in Clipper Island, notice the Sound Booth button. That opens a floating rack with mixer knobs, EQ presets, transient shaper, stereo widener, ducking — all the audio processing that gets applied to your songs when they fire in-game.

The knobs are literally rendered as SVG arcs with CSS rotation. Someone spent way too long on that and I respect it.

The Preview button in Sound Booth plays your song through the full effects chain. That's different from the little play button on each library row — those play the raw file. Sound Booth preview = what you'll actually hear during a game.

Also worth knowing: the lead-in whistle does NOT play during song-list previews anymore. That was a bug until recently — you'd be browsing your library, hit play on a song, and a referee whistle would blast before every single one. Very authentic. Extremely annoying. Fixed now.

There's also a **firing delay** setting — configurable 0 to 5 seconds between when the game event is detected and when the sound actually starts. This is for streamers who need to sync their audio with broadcast delay. If your stream is 3 seconds behind, set the delay to 3 seconds. Now your touchdown song plays exactly when your viewers SEE the touchdown, not before."

---

## [7:15–8:00] SECTION 6 — AUTO-ASSIGN & QUICK TIPS

*(Show the Auto-Assign button, then show a quick summary of everything)*

**You:** "One last power feature: **Auto-Assign**. If you loaded the Default Song Pack for a team, hit Auto-Assign and Bandroom will keyword-match every song to every event automatically. It walks through each event one at a time, shows you what it picked, and lets you confirm or skip. At the end you get a summary of exactly what changed.

**Quick tips before we wrap:**

One — the Clipper Island and Trimmer are the SAME panel. They swap in and out of the same space. If the trimmer is open and you switch events, it auto-closes so you don't get confused.

Two — your songs, profiles, and settings all auto-save. You don't have to hit Ctrl+S. Assign a song, it's saved. Trim a clip, it's saved. Close the app, everything is still there.

Three — if you ever see an event that says 'Unassigned' and you're SURE you assigned something to it, check which team is active. The Band Room only shows ONE team at a time. That 'All Teams' toggle we're adding soon will fix this.

Four — the Copy From button on each card is your best friend. Set up one event perfectly, then copy it everywhere.

That's the event card. That's the clipper. That's the whole pipeline. If you made it this far, you now know more about this screen than 90% of Bandroom users. Go assign some songs. Make your stadium sound incredible."

*(End on a freeze frame of a fully-assigned event card with the green LED pulsing)*

---

## TIMING BREAKDOWN

| Section | Time |
|---------|------|
| Cold Open | 0:45 |
| The Band Room & Event Cards | 1:15 |
| The Button Bar | 1:15 |
| Clipper Island (Song Picker) | 1:30 |
| The Inline Trimmer | 1:15 |
| Sound Booth, Preview & Audio Chain | 1:15 |
| Auto-Assign & Quick Tips | 0:45 |
| **TOTAL** | **~8:00** |