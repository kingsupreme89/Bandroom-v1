# Bandroom For Dummies
## The Step-by-Step Installation & Setup Video Script

*Target: ~8-10 minute YouTube video. Written in plain English. Every step spelled out.*

---

## TITLE OPTIONS (pick one):
- "Bandroom For Dummies: Set It Up in 10 Minutes"
- "Bandroom: First-Time Setup — Start to Finish"
- "How to Install Bandroom (Even If You're Not a Computer Person)"

---

## VIDEO SCRIPT

---

### [0:00] INTRO — WHAT IS BANDROOM? (30 seconds)

**You say:**
"Bandroom is a free Windows app that watches your College Football 27 game on screen and automatically plays stadium sounds — fight songs, crowd reactions, PA announcements — the MOMENT stuff happens in the game. Touchdown? Fight song plays instantly. Third down? Defensive hype chant fires. No buttons to press. It just works."

"What I'm going to show you today is how to get Bandroom installed on your computer, step by step, even if you're not a computer person. I'll show you exactly where the files go, what buttons to click, and what to expect. Let's do it."

---

### [0:30] SECTION 1 — WHERE TO GET BANDROOM (1 minute)

**[On screen: Show GitHub page — github.com/kingsupreme89/Bandroom-v1]**

**You say:**
"Bandroom lives on GitHub. GitHub is just a website where programmers store their code — think of it like a public filing cabinet. You don't need an account to download."

"Type this into your browser: `github.com/kingsupreme89/Bandroom-v1`"

**[On screen: Arrow pointing to the Releases section on the right side of the GitHub page]**

"Look on the right side of the page. You'll see a section that says 'Releases.' Click that."

**[On screen: Show the Releases page with the latest version]**

"You'll see a list of versions. The top one is the latest. You're looking for a file called `Setup.exe`. That's the installer. Click it to download."

**[On screen: Browser download bar showing Setup.exe downloading]**

"That's it. One file. Double-click it when it's done."

---

### [1:30] SECTION 2 — INSTALLING BANDROOM (2 minutes)

**[On screen: Double-click Setup.exe. Show the Windows SmartScreen popup if it appears.]**

**You say:**
"Windows might show you a blue box that says 'Windows protected your PC.' This is normal for any app that isn't from the Microsoft Store. Click 'More info,' then click 'Run anyway.' I promise it's safe — thousands of CFB players are already using it."

**[On screen: Squirrel installer window. It's very quick — show it appearing and disappearing.]**

"The installer uses something called Squirrel. Squirrel is fast. You might not even see a window — it just does its thing in about 2 seconds and then Bandroom launches automatically."

"If it doesn't launch automatically, don't worry. Look on your desktop for the Bandroom icon and double-click it."

---

### [3:30] SECTION 3 — WHERE THE FILES LAND (1 minute)

**[On screen: Open File Explorer. Navigate to the install folder.]**

**You say:**
"Let me show you where Bandroom actually lives on your computer. It installs to a hidden folder, so let's unhide it first."

"Open File Explorer. Click the View tab at the top. Check the box that says 'Hidden items.'"

"Now navigate to: `C:\Users\[Your Name]\AppData\Local\Bandroom\`"

**[On screen: Show the folder contents]**

"Here's what's in this folder and what each thing means to you:"

| Folder/File | What It Is |
|---|---|
| `Bandroom.exe` | The main program. Double-click this to run Bandroom |
| `Profiles\` | Every team's settings live here. LSU.json, Alabama.json, etc. These are YOUR configurations |
| `Songs\` | All your imported fight songs and audio files live here |
| `crash.log` | If something goes wrong, details get written here for debugging |
| `admin_token.local.txt` | Just a tiny file that handles authentication — ignore it |
| `app-[version]\` | The actual app files. Squirrel puts them here and manages updates automatically |

"You will never need to touch these files manually. Bandroom handles everything through its interface. I'm just showing you so you know where stuff is if you ever need to find it."

---

### [4:30] SECTION 4 — FIRST LAUNCH: WHAT YOU SEE (1 minute)

**[On screen: Bandroom launches. Show the main window.]**

**You say:**
"When Bandroom first opens, this is what you'll see. Let me walk you through the main parts."

**[On screen: Mouse over each section as you describe it]**

"At the very top: the header bar. You'll see the Bandroom logo on the left. In the center, it says 'LOCK IN?' — that's for picking your matchup. On the right, you've got buttons for Teams, Save, Shortcuts, and Help."

"Below that, some important buttons: 'The Bandroom' is the marketplace where you download songs. 'Sound Bank' is your current team's songs. 'My Downloads' is everything you've grabbed. 'Auto-Assign' automatically matches songs to the right triggers."

"The main part of the screen — the big area in the middle — is your team's trigger grid. This is where you'll see all the things that can happen in a game (touchdown, kickoff, third down, etc.) and which song is assigned to each one."

---

### [5:30] SECTION 5 — PICKING A TEAM (1 minute)

**[On screen: Click the "Teams" button or the team badge at the top]**

**You say:**
"First thing you want to do: pick a team. Click the team badge at the top, or the Teams button."

**[On screen: Show the team grid popping up]**

"You'll see a grid of all 134 college football teams. Find your team. Click it."

**[On screen: Click LSU (or any team)]**

"The screen changes to show your team's colors, logo, and background. The header updates to show your team's name. You're now editing LSU's sound profile."

"Everything you do from here — every song you download, every trigger you assign — is for THIS team. You can switch teams anytime and your settings for each team are saved separately."

---

### [6:30] SECTION 6 — SETTING A MATCHUP (1 minute)

**[On screen: Click the "LOCK IN?" button]**

**You say:**
"Before you play a game, you need to tell Bandroom who's home and who's away. Click the 'LOCK IN?' button in the top center."

**[On screen: Show the matchup panel popping up — Home and Away selectors]**

"Pick the home team on one side, away team on the other. Click 'Confirm Matchup.'"

"Now Bandroom knows: it needs to play YOUR songs when good things happen for YOUR team, and the opponent's songs when good things happen for THEM. It separates home and away automatically."

**[On screen: Show the "Stop Watching" button that now appears]**

"When you've locked in, you'll see a 'Stop Watching' button appear. That's your way to end the game and unlock the matchup when you're done."

---

### [7:30] SECTION 7 — THE MARKETPLACE: GETTING SONGS (1.5 minutes)

**[On screen: Click "The Bandroom" rainbow button]**

**You say:**
"Now you need songs. Bandroom doesn't come with audio — that's where The Bandroom marketplace comes in. Click the rainbow 'The Bandroom' button at the top."

**[On screen: Show the marketplace browser — team list, trigger categories]**

"This is a community-driven marketplace. Other users have uploaded fight songs, stand tunes, and chants for hundreds of teams. Browse by team name or by what kind of sound you need. Click a song to preview it. Click download to grab it."

**[On screen: Download a song. Show it appearing in My Downloads.]**

"Once you've grabbed some songs, click 'My Downloads' to see everything you've downloaded. Then go to 'Sound Bank' to see your current team's collection."

---

### [9:00] SECTION 8 — AUTO-ASSIGN (30 seconds)

**[On screen: Click the "Auto-Assign" button]**

**You say:**
"Here's the best feature for new users: Auto-Assign. One button. It looks at the songs you've downloaded and automatically matches them to the right game triggers based on their metadata — song title, school, trigger type."

"Click it. Boom. Done. Touchdown songs go to the touchdown slot. Kickoff songs go to the kickoff slot. It's not perfect — you might want to tweak things — but it gets you 90% of the way there instantly."

---

### [9:30] SECTION 9 — GAMETIME: LET IT WATCH (30 seconds)

**[On screen: Show the watch status pill in the header. NOT clicking anything — just explain.]**

**You say:**
"Now here's the part that surprises people: you don't press a GAMETIME button anymore. Bandroom just watches. When your game is on screen and Bandroom can see the scorebug, it starts detecting events automatically."

"The green dot in the header tells you if Bandroom is watching. Green = it sees the game. Red = it doesn't. If it's red, make sure your game window is visible on screen and not minimized."

**[On screen: Show CFB 27 running. Show the scorebug being read.]**

"Bandroom is now reading your screen 30 times per second. When something happens in-game — a touchdown, a kickoff, a turnover — Bandroom detects it and plays the right sound INSTANTLY. Less than 50 milliseconds. You won't even perceive a delay."

---

### [10:00] SECTION 10 — SAVING YOUR PROFILE & GETTING HELP (30 seconds)

**[On screen: Click "Save" button]**

**You say:**
"Don't forget to save. Click the Save button in the header bar to save your current team's profile. Do this after you've set up your songs."

"For help: click the '?' Help & Guide button at the top. It opens a full help panel with keyboard shortcuts, explanations, and a sharing guide."

"If you have questions or run into issues, we're in the CFB Modding Discord. Come ask — the community's active and someone will help you out."

---

### [10:30] OUTRO (30 seconds)

**You say:**
"That's it. You installed Bandroom. You picked a team. You downloaded songs. You auto-assigned them. You locked in a matchup. Bandroom is watching your game. The sounds will fire automatically."

"It really is that simple. One file to download. One double-click to install. A few clicks to set up. Then it just works."

"If this helped you, share it with someone who'd find it useful. Check the description for links to download Bandroom and join the Discord. I'll see you in the next video."

**[End screen: QR code or link to GitHub Releases + Discord invite]**

---

## VIDEO PRODUCTION NOTES

- **Screen recording:** Use OBS to capture the Bandroom window clearly. Zoom in on relevant sections.
- **Cursor:** Use a highlighted cursor so viewers can follow what you're clicking.
- **Text overlays:** Add text for URLs, folder paths, and key terms (like the ones in the tables above).
- **Audio:** Speak clearly. No background music during instructional sections — it competes with your voice.
- **Pacing:** This script is timed at ~10 minutes. Don't rush. Let each step breathe. Pause after clicking things so viewers can see what happened.
- **Timestamps:** Add chapter markers in the YouTube description for each section so viewers can jump around.

---

*Script prepared for Bandroom v1.0.0 — current as of initial public release.*