---
title: "Bandroom Full Handbook"
subtitle: "Everything you need to know to run stadium sound like a pro"
author: "Bandroom"
date: "2026-08-12"
version: "v1.1 Early Access"
toc: true
toc-depth: 3
---

# Bandroom Full Handbook

Welcome. This is the complete guide to Bandroom — what it does, how it works, how to set it up, and how to get the most out of it. No jargon, no assumptions. If something works a certain way, this book explains why. If there's a trick that saves you time, it's in here. If something confused you, it's probably answered in the FAQ at the end.

Bandroom was built so one person can make their stadium sound incredible from one computer. Whether you're streaming, playing solo, or running sound for a watch party, this handbook covers every feature, every button, and every situation.

---

## Part 1 — Getting Started

### What Is Bandroom?

Bandroom is a program that watches your college football game on your screen and plays the right song at the right moment — automatically. When your team scores a touchdown, Bandroom notices and plays your touchdown song. When the defense forces a third-down stop, it plays your stop song. You don't have to click anything during the game. Bandroom does it for you.

Think of it like having a stadium DJ who can see the scoreboard, know every situation, and hit the right cue every time — except it's software running on your PC, and you control exactly which song plays for which moment.

### How It Actually Works (The Simple Version)

1. You tell Bandroom which teams are playing (Set Matchup).
2. You assign songs to game situations — touchdown, third down, turnover, kickoff, and dozens of others.
3. During the game, Bandroom watches your screen. It reads the scoreboard — the down, the distance, the quarter, the score, the clock, penalty banners, scoring banners — using optical character recognition (OCR), the same kind of technology that turns a scanned document into text.
4. When it sees something change — for example, the down changes from 2nd to 1st — it figures out what just happened and plays the song you assigned for that moment.

You never have to click a "play" button mid-game. Bandroom reads the screen, figures out what happened, and fires the right sound — all by itself.

### System Requirements

Bandroom runs on **Windows 10 or Windows 11**. It needs:

- A display running at 1920×1080 (1080p) or higher — Bandroom reads pixels off your screen, so it needs to see the scoreboard clearly
- The game you're watching must display a visible scoreboard on screen (which every college football video game does)
- An internet connection for marketplace features and profile sync (optional — Bandroom works offline too)
- A Google account if you want to use the marketplace, profile sync, or stat tracking (optional)

Bandroom also has an in-progress **Mac version**. The engine and audio playback already work natively on macOS. The full Mac app is still being built.

### Installing Bandroom

1. Go to the GitHub releases page: **https://github.com/kingsupreme89/Bandroom-v1/releases/latest**
2. Download the file called `BandroomSetup.exe`
3. Run it — it installs automatically, no admin rights needed, no complicated setup wizard

That's it. One download, one click.

#### Important install rules:

- Only run `BandroomSetup.exe` **once**. It creates a desktop shortcut. Always use that shortcut after installation.
- **Delete `BandroomSetup.exe`** from your Downloads folder after you run it. Old installers don't update — if you double-click one months later, you'll reinstall an old version and break things.
- Updates happen **inside the app**. When the update button lights up in the header, click it to get the latest version. You never need to download a new installer from the website unless you're moving to a brand-new computer.

### Where Bandroom Lives on Your PC

Bandroom installs into a folder called **Bandroom** inside your Local AppData folder. You can jump straight there by pasting this into the Windows File Explorer address bar:

```
%LocalAppData%\Bandroom
```

Inside that folder you'll find:

- The app itself (`Bandroom.exe`)
- Your saved songs and profiles
- All your settings and team data
- The `Songs` folder — where your imported songs live
- The `TeamBackgrounds` and `TeamLogos` folders — where your custom images live

You don't need to go in there for normal use — it's just where everything lives if you ever need to find or back up your stuff.

### First Launch

The very first time you open Bandroom, it asks you to pick your favorite team. You'll see team logos slide by like a carousel — click the arrows on the left and right, or click a logo directly to move it to the center. When your team is in the center spot, press **Confirm Team**.

Right after that, Bandroom points out **The Bandroom** button — that's the community marketplace where other users' songs and backgrounds live. We'll cover that in depth later.

You don't need to do anything else to start using Bandroom. It already has some default songs it can use, and you can download a much bigger free pack (see "Default Song Pack" in Part 5).

### Sign In with Google

In the top corner of the app, click **Sign in with Google**. Signing in gives you:

- Access to **The Bandroom marketplace** — browse and download songs and backgrounds other users have shared
- **Profile sync** — your entire setup follows you to any computer you sign in on
- **Stats tracking** — games watched, streaks, top uploads, and more

You can use Bandroom without signing in. The marketplace, profile sync, and stat tracking all require it, but the core engine — game watching, song triggering, and audio playback — works offline with no account at all.

### Your Profile

Open your **Profile** from the navigation rack (the sidebar). Here you can:

- Set a **Favorite Team** — gets a quick-jump star in the header so you can always get back to it fast
- Set a **Rival Team** — for flavor and stat comparisons
- Upload an **Avatar** — a profile picture that shows up in the marketplace and Discord
- See your **Stats** — games watched, songs triggered, uploads, downloads, and more
- Toggle **Public Profile** — when on, your favorite team, level, achievements, and lifetime stats can be viewed by others via a share link, and you'll appear on the marketplace's Players leaderboard. Your bio and rival team are never shared.

---

## Part 2 — The UI Layout

Bandroom's interface is built with a custom glassmorphism theme — panels have transparent frosted-glass backgrounds with subtle borders and glows. The whole app changes color to match whichever team you have active.

### The Header Bar

At the very top of the app:

- **Team Badge** (left) — shows your active team's logo. Click it to jump back to that team at any time.
- **The Bandroom** button — opens the community marketplace. This button gets a spotlight animation on your first launch so you know it's there.
- **Sign In / Profile** (right) — your Google sign-in status and profile access.
- **Update indicator** — a button that lights up when a new version is available. Click to update inside the app.

### The Navigation Rack (Left Sidebar)

The vertical sidebar on the left is your main navigation. It's organized like a Spotify-style icon + label column. From top to bottom:

- **The Bandroom** — community marketplace
- **Sound Bank** — your current active team's songs and backgrounds
- **My Downloads** — everything you've downloaded from the marketplace
- **Discord Chat** — community chat panel
- **Set Matchup** — pick your Home and Away teams before watching a game
- **Save Profile** — save your current setup
- **Streamer Mode** — hide personal info for broadcasting
- **Keyboard Shortcuts** — see and customize hotkeys
- **Tips** — show a rotating tip
- **Profile** — your stats and settings
- **Settings** — audio, preferences, and more
- **Help** — the built-in help and guide overlay

Every button shows a tooltip when you hover it, so you don't have to memorize what each icon means.

### The Center Panel

The main area of the app is where you'll spend most of your time:

- **Situations Panel** — shows all 46 game events organized by category (Offense, Defense, Hype, Special Teams, etc.). Each event is a card where you assign songs.
- **Matchup Sidebar** — once you've set a matchup (Home and Away teams), a small sidebar appears in the center area that lets you toggle between editing the Home or Away team's song assignments with one click.
- **Team Preset Bar** — switch between Plain, Home, Away, and Big Game presets for the active team.

### The Right Panel (Adjust Panel)

The panel on the right holds audio and visual controls:

- **Master Volume** slider
- **Home Volume** and **Away Volume** sliders (active once a matchup is set)
- **Lead-In Whistle** setup — upload and toggle a short referee-whistle sound
- **Firing Delay** — set a delay (0–5 seconds) for streamers syncing with broadcast delay
- **Help** button — opens the in-app guide

### The Command Palette

Press **Ctrl+K** anywhere in Bandroom to open the command palette — a search box that lets you type what you want to do and jump straight there. It searches every action in the app:

- "marketplace" → open The Bandroom
- "matchup" → set a matchup
- "streamer" → toggle streamer mode
- "shortcuts" → open hotkey panel
- "song pack" → manage your default song pack
- "reset" → reset team profile
- And a whole lot more

If you only learn one power-user trick, make it Ctrl+K. It's the fastest way to get anywhere in the app without hunting through panels.

### The Matchup Screen (Coverflow)

When you open the full team picker — for picking a favorite team, setting a matchup, or browsing teams — you'll see a large coverflow-style display:

- **Center**: the currently selected team's logo, large and prominent, with the team name displayed underneath in a bold, sporty font with a neon glow effect
- **Side scroll list**: a vertical strip of smaller team logos on the side, sorted A through Z (all teams, FCS schools mixed in alphabetically)
- **Search box**: type to filter teams by name
- **Add School**: create a custom team (see Part 3 — TeamBuilder)

The coverflow and scroll list stay in sync — whatever's highlighted in the center also highlights in the side list, and vice versa.

### The Team Preset Bar

Above the Situations panel, you'll see four small pills: **Plain**, **Home**, **Away**, and **Big Game**. These let you switch which of a team's four song profiles you're currently editing. The active pill glows in the team's color. Pills marked with a green dot have already been configured (songs assigned); blank pills are empty and ready for you to fill.

Between the pills, a small **copy dropdown** lets you copy songs from one preset to another — for example, copy everything from Home → Big Game as a starting point, then tweak the Big Game version with different songs for rivalry matchups.

### The Header Bar in Detail

Each element in the header serves a specific purpose:

- **Team Badge** — clicking this jumps your active team back to your favorite team instantly. It also shows a small colored dot indicating whether the current team has a saved profile (green = yes, dim = no)
- **The Bandroom** — the marketplace entry point. On first launch, this button gets a spotlight animation with a tooltip: "New here? Check out The Bandroom — a community library of songs and backgrounds other bands have shared"
- **Google Sign-In** — your avatar appears here once signed in. Clicking it opens your Profile
- **Update Pill** — when lit, a new version is available. Click to update in-place
- **Presence Dot** — a small pulsing dot next to the sign-in area showing how many other Bandroom users are currently online (only visible when signed in, hidden in Streamer Mode)

### The Right Panel (Adjust Panel) in Detail

The right panel (sometimes called the Adjust panel) contains more than just volume sliders:

- **Scorebug Preset selector** — tells Bandroom which game/display style you're using so it knows where on screen to look for each piece of text. The default is "College Football 27" (CFB27). If you're using a different game or the CBS-style scorebug, change this. Wrong preset = the engine can't read the scoreboard = no songs fire
- **Big Game toggle** — when enabled, defensive events fire for BOTH sides at different volumes (full for the team whose defense made the play, ducked for the other side), and field-position-based volume layering activates. This is why rivalry games feel different from regular-season games
- **Lead-In Whistle section** — upload/set a whistle clip, toggle it on/off globally. Individual event cards can still override this per-event
- **Firing Delay** — 0 to 5 seconds, synced to stream broadcast delay
- **Auto-Apply to New Teams** — when enabled, any new team you switch to automatically gets your Universal Profile's songs assigned (if a Universal Profile is saved)

---

## Part 3 — Teams & Matchups

### Active Team vs. Matchup Teams

This is one of the most important concepts in Bandroom, and it trips people up. There are two different ideas of "which team am I using":

**Active Team** — whichever team is currently selected in the left-side Team panel. This controls:

- The app's colors (everything glows in this team's colors)
- The background image
- Which team's song assignments you're currently VIEWING and EDITING
- Which team's Sound Bank you're browsing

**Matchup Teams** — set via the **Set Matchup** button. This tells the game-watching engine:

- Which two teams are actually playing
- Which team is Home and which is Away
- Who has the ball (Bandroom reads this off the screen)

The active team and the matchup teams can be different. They often will be. For example:

- You're playing as Georgia against Florida. You set Set Matchup to Georgia (Home) vs. Florida (Away).
- You switch your active team to Georgia to edit Georgia's song assignments — the app turns red and black.
- Then you switch your active team to Florida to edit Florida's song assignments — the app turns orange and blue.
- During the game, Bandroom watches the scoreboard, knows that Georgia is Home and Florida is Away, and fires the right team's songs based on who has the ball — regardless of which team is "active" in the sidebar.

Think of it this way: **Set Matchup tells the engine what to watch for. Your active team tells Bandroom what to look like and which team's songs you're editing.**

### Picking Teams

Every team picker in Bandroom uses the same coverflow interface:

1. The center shows the currently selected team's logo at full size
2. Click the left/right arrows, or click a team logo in the side scroll list, to navigate
3. The side scroll list is sorted A through Z, with all 134 FBS teams plus 50 popular FCS schools
4. Type in the search box at the top to filter by name
5. Press **Confirm Team** (or the equivalent button for the picker you're in) to lock in your choice

### Set Matchup

Before you start watching a game, use **Set Matchup** in the navigation rack:

1. Click **Set Matchup**
2. Pick an **Away** team
3. Pick a **Home** team
4. Press **GAMETIME** (plays a tackle hit sound to confirm)
5. Press **Watching** — the engine starts monitoring your screen

**Important:** once you press **Watching**, the Away/Home matchup is locked. You can't change it unless you press **Watching** again to unlock it first.

### Switching Sides Mid-Game

While watching, you can switch between which team's song assignments are active by using the **Matchup Sidebar** in the center panel — click Away or Home to flip which side you're editing. This lets one person control both teams' band cues from one seat.

The match-up sidebar also controls which team's Preset (Plain/Home/Away/Big Game) is currently being edited.

### Team Presets: Plain, Home, Away, and Big Game

Every team can have up to four different song profiles — different sets of assignments for different contexts:

- **Plain** — your everyday setup, the default
- **Home** — a set of songs specifically for home games
- **Away** — a set of songs specifically for away games
- **Big Game** — a special set for rivalry games, bowl games, or playoff matchups

These are four completely independent profiles per team. Georgia's "Big Game" touchdown song can be totally different from Georgia's "Plain" touchdown song. You switch between them with the preset pills above the situations panel, and you can copy any preset to any other (e.g., copy Home → Big Game to use your home setup as a starting point for your rivalry setup).

When you press **GAMETIME**, Bandroom automatically picks the right preset based on the matchup context — so you don't have to remember to switch to "Big Game" manually before a rivalry game.

### TeamBuilder — Adding Your Own School

If your school isn't in Bandroom's built-in team list, TeamBuilder lets you add it yourself in about 30 seconds.

#### Creating a TeamBuilder Team

1. Open the full Team picker and click **Add School**
2. Type your school's name
3. Pick a **primary color** and a **secondary color** — the two main colors of your team. Bandroom uses these to color the whole app to match, the same way it does for every built-in school
4. If your scoreboard shows the mascot instead of the school name (e.g., "Bengals" instead of "Idaho State"), add it in the mascot field so penalty calls match to the right team
5. Click the button to save it. Your new school appears immediately in the Team picker — no restart needed
6. Give it a logo: find your new school's tile in the Team picker and click the small pencil icon on it. This opens the logo tool, where you can upload and crop a picture to use as its logo

That's it — your school now behaves like any other team in the picker, Set Matchup, and the rest of the app.

#### How a TeamBuilder Team Actually Works With the Game Engine

This is the part that trips people up, so read it carefully.

**A TeamBuilder team is a skin and a sound bank — not a source of automatic detection.**

When Bandroom watches your game, it isn't guessing what's happening — it's reading text and colors directly off your screen (the scoreboard, the penalty banner, the down-and-distance display, and so on). That only works for schools the video game itself actually knows about and displays. A school you typed into TeamBuilder doesn't exist inside the video game, so there's nothing on screen for Bandroom to read for that school specifically.

**What you DO get automatically, just by creating a TeamBuilder team:**

- The app's colors switch to match your school the moment it's active
- Its own private **Sound Bank** — a folder of songs and pictures that belongs only to this team
- A spot in the Team picker and in **Set Matchup**, exactly like a built-in school

**What you do NOT get automatically:**

- No touchdown, score, or possession detection tied to your custom school by name
- No penalty-side reading, no field-position reads — none of the screen-reading features fire because your school said so
- Bandroom cannot tell when "your team" scored, because your team doesn't exist in the game's own data

**So how do you actually use a TeamBuilder team in a real game?**

The trick is: you separate *what the engine watches* from *what plays and what it looks like*.

1. In the video game itself, you're always playing as one of the game's real, built-in schools (whatever roster the game actually ships with) — that part never changes. TeamBuilder doesn't add your school into the video game.
2. In Bandroom's **Set Matchup**, pick that same real, built-in school as your side. This is the school Bandroom's engine will actually watch the screen for — scores, downs, possession, penalties, all of it.
3. Switch your **active team** in the Team panel to your TeamBuilder school. This is what makes the app's colors and Sound Bank match *your* school, regardless of which real school Set Matchup is watching.
4. Assign songs to situations under your TeamBuilder team as normal. When the engine detects a real event off the real school in Set Matchup — say, a touchdown — Bandroom plays whatever song *you* assigned under your TeamBuilder team for "Touchdown."

**In one sentence:** Set Matchup tells the engine what to watch for; your active TeamBuilder team tells Bandroom what to look like and what to play. They don't have to be the same school, and for a custom school, they never will be.

#### Quick Reference: Built-in vs. TeamBuilder

| | Built-in school | TeamBuilder (custom) school |
|---|---|---|
| Shows up in Team picker / Set Matchup | Yes | Yes |
| App colors match it | Yes | Yes |
| Has its own Sound Bank | Yes | Yes |
| Engine auto-detects scores/plays for it | Yes | No — it isn't in the video game |
| How to still trigger its songs during a game | Just play as it | Set Matchup uses your real in-game school; keep your TeamBuilder school as the *active* team so its songs/colors are what plays |

### Built-In FCS Schools

Bandroom ships with all 134 FBS teams plus 50 popular FCS schools built in — full logo, colors, and automatic detection for every single one. Schools like Montana, North Dakota State, South Dakota State, and dozens of others are already in the app. If you're an FCS school, check the team list before assuming you need TeamBuilder — you might already be fully wired in.

---

## Part 4 — The Event System

### How Bandroom "Sees" the Game

Bandroom uses OCR (optical character recognition) to read your game screen in real time. It looks at specific regions of the screen — the spots where the scoreboard, down-and-distance display, and banner graphics appear — and turns the pixels into text it can understand.

There are six specific regions the engine watches:

| Region | What it reads | Used for |
|---|---|---|
| **Down** | Current down & distance (e.g., "3rd & 7") | Down changes, first downs, tackles for loss |
| **Situation** | Game state banners (KICKOFF, PAT GOOD, TOUCHDOWN, INTERCEPTED, FUMBLE, TURNOVER, TIME OUT) | Scoring plays, turnovers, kickoffs, timeouts |
| **Quarter** | Quarter number (1st, 2nd, 3rd, 4th) | Quarter changes, game state transitions |
| **Flag** | PENALTY / FLAG banner | Penalty detection and side routing |
| **Banner** | Full-screen scoring banners (TOUCHDOWN, FIELD GOAL, SAFETY) | Big scoring play confirmation |
| **Possession Color** | Team color of the down-and-distance ribbon | Who has the ball (no text needed — just the color) |

The engine also reads:
- **Score and clock** — for "iced game," 2-minute warning, and "victory in hand" moments
- **Timeout dash marks** — the little indicator marks on the scorebug that show how many timeouts each team has left (read via pixel brightness, since there's no text there)
- **Possession underline** — a colored underline or indicator bar on certain scorebug styles that confirms possession

### The Smart Trigger Brain

Once per tick (fractions of a second), the engine reads the screen and compares this snapshot to the previous one. It runs through **16 different evaluators** — each one is a specialist that watches for a specific kind of game event. For example:

- **OffenseDownHelper** — watches the down/distance text and fires when the offense earns a new set of downs (1st down, 2nd down, 3rd down)
- **TouchdownHelper** — watches for the TOUCHDOWN banner or score change
- **TurnoverHelper** — watches for INTERCEPTED, FUMBLE, or TURNOVER banners
- **TimeoutHelper** — watches for the TIME OUT banner and reads how many timeouts remain
- **KickoffHelper** — watches for the KICKOFF banner and fires appropriate kickoff cues
- **FirstDownHelper** — fires when the offense earns a first down, with variants for big gains and midfield
- **TflHelper** — catches plays where the ball carrier was tackled behind the line (negative yardage)
- **GameStateEventHelper** — fires for quarter changes, pregame, victory-in-hand, and iced-game moments
- **DriveStarterHelper** — fires at the start of each new offensive drive
- **PenaltyHelper** — reads penalty banners and routes to the correct side

These evaluators run simultaneously every tick. When one of them determines that an event just happened (comparing this tick to the previous tick), it returns an **EventKey** — a string like `"Offense: Touchdown Scored"` or `"Defense: Third Down"` — which Bandroom then looks up in your song assignments to find the right audio file to play.

### The Complete List of Assignable Events (46 Total)

Every one of these events can have a song assigned to it. They're organized into six categories in the UI:

#### DOWNS (14 events)

| Event | When it fires |
|---|---|
| Offense: Earned First Down | Any time the offense gains a first down |
| Offense: Earned First Down (Big Gain) | First down earned on a play that gained significant yardage |
| Offense: Earned First Down (Midfield) | First down earned that crosses the 50-yard line |
| Offense: Second Down | The offense faces 2nd down |
| Offense: Second Down (Midfield) | 2nd down at or past midfield |
| Offense: Second Down Short | 2nd down with short yardage to go |
| Offense: Third Down | The offense faces 3rd down |
| Defense: Second Down | The defense faces 2nd down |
| Defense: Second Down (Loss) | 2nd down after the offense lost yards on the previous play |
| Defense: Second Down (Midfield) | 2nd down at or past midfield |
| Defense: Second Down Short | 2nd down with short yardage — defense needs a stop |
| Defense: Third Down | The defense faces 3rd down |
| Defense: Third Down (Loss) | 3rd down after the offense lost yards on the previous play |
| Defense: Fourth Down | The defense faces 4th down |
| Defense: Fourth Down (Loss) | 4th down after a loss |
| Defense: Tackle for Loss | Ball carrier tackled behind the line of scrimmage |

#### SCORING (7 events)

| Event | When it fires |
|---|---|
| Offense: Touchdown Scored | Your offense reaches the end zone |
| Offense: Field Goal Made | Your kicker splits the uprights |
| Offense: PAT Made | Extra point is good |
| Offense: 2-Point Conversion Made | Two-point try succeeds |
| Defense: Touchdown Scored | Your defense or special teams scores |
| Defense: Field Goal Missed by Opponent | The other team misses a field goal |
| Defense: Safety | Your defense traps the ball carrier in their own end zone |

#### TURNOVERS (2 events)

| Event | When it fires |
|---|---|
| Defense: Turnover Forced | Interception or fumble recovered by your defense |
| Defense: Iced Game by Turnover | A turnover that effectively clinches the win |

#### SPECIAL TEAMS (5 events)

| Event | When it fires |
|---|---|
| Other: Opening Kickoff | The very first kickoff of the game |
| Other: Second-Half Kickoff | The kickoff that opens the 3rd quarter |
| Other: Kickoff | Any regular kickoff during the game |
| Other: Kickoff on Kick (Kicking) | Your team is kicking off |
| Other: Kickoff on Kick (Receiving) | Your team is receiving the kickoff |

#### PENALTIES (2 events)

| Event | When it fires |
|---|---|
| Penalty: Offense | Penalty called on the offense (fires for the OPPOSITE team — i.e., the defense's celebration) |
| Penalty: Defense | Penalty called on the defense (fires for the OPPOSITE team — i.e., the offense benefits) |

Note: Penalty events fire for the team opposite the penalty — so when the offense gets flagged, the defense's penalty song plays (since it's good news for the defense), and vice versa.

#### HYPE (12 events)

| Event | When it fires |
|---|---|
| Other: Pregame Take the Field | Team runs onto the field before kickoff |
| Other: Start of 2nd Quarter | Second quarter begins |
| Other: Start of 4th Quarter | Fourth quarter begins |
| Offense: Drive Starter | Beginning of your offensive possession |
| Defense: Drive Starter | Your defense takes the field |
| Defense: After Opening Kick | First defensive snap after the opening kickoff |
| Offense: After Opening Kick | First offensive snap after receiving the opening kickoff |
| Offense: Iced Game by First Down | Offense converts a first down that runs out the clock |
| Offense: Victory in Hand | Score and clock make a comeback practically impossible |
| Defense: Timeout (4 Remaining) | Opponent calls timeout, 4 left |
| Defense: Timeout (3 Remaining) | Opponent calls timeout, 3 left |
| Defense: Timeout (2 Remaining) | Opponent calls timeout, 2 left |
| Defense: Timeout (1 Remaining) | Opponent calls timeout, 1 left |
| Defense: Timeout (0 Remaining) | Opponent calls timeout, none left |

### How Side Routing Works

When an event fires, Bandroom needs to decide WHICH team's song to play — the home team's or the away team's. This is called side routing, and it follows a simple set of rules:

1. If the EventKey starts with `"Defense:"`, the song fires for the team OPPOSITE the one that has the ball. (Because a defensive stop is good for the other side's defense.)
2. For everything else, the song fires for whichever team HAS the ball. (Because scoring, first downs, and kickoffs are about the offense.)
3. Penalties fire for the OPPOSITE side of whoever was flagged — so an offensive penalty plays the defense's song, and vice versa.
4. A handful of "home only" events only fire for the home team regardless of possession — these are mostly pregame and hype moments.

During a **Big Game**, the routing adjusts: defensive events can fire for BOTH sides at different volumes — the team whose defense made the play gets full volume, and the other team gets a ducked (lowered) version so both sides' band cues are audible in a big-game atmosphere.

---

## Part 5 — Assigning Songs (The Clipper)

Every game situation needs a song assigned to it before Bandroom can play it. This is done through the **Situations Panel** — the main center area of the app where all 46 event cards live.

### The Event Card

Each of the 46 events shows as a card. Every card has:

- **Status LED** (left dot):
  - Green = a song is assigned and confirmed
  - Amber = assigned but never verified
  - Dim/gray = nothing assigned yet — this event will play silence
- **Event name** — e.g., "Offense: Touchdown Scored"
- **Current assignment** — the filename of the assigned song, or "Unassigned"
- **Button bar** — eight actions you can take on this event

### The Button Bar (Eight Actions Per Card)

1. **Assign / Edit** — opens the Clipper Island song picker to choose or change the assigned song
2. **Assign PA** — same as Assign/Edit, but for the PA Announcer layer (a second audio clip that plays simultaneously)
3. **Copy From** — copies the song, PA clip, and whistle setting from another event card to this one. Saves you from doing the same search three times for different down-and-distance variants
4. **Play / Stop** — previews the assigned song with all effects applied, exactly as it would sound in-game
5. **Volume** — opens a tiny popover slider. Each event card has its OWN volume independent of the master — your touchdown song can be louder than your "start of the 2nd quarter" song
6. **Whistle Toggle** — turns the lead-in whistle on or off for this specific event. Some songs sound better with a referee whistle before them; some don't
7. **Track Info** — opens a drawer with metadata: title, artist, school, energy level, acoustic fingerprint. You can edit this data and save it to the audio file
8. **Speed Toggle** — plays the song at 1.09x speed (both in-game and in preview). Subtle bump in tempo for certain situations

### The Clipper Island (Song Picker)

When you click **Assign / Edit** or **Assign PA** on an event card, a panel called **Clipper Island** slides up from the bottom. It contains everything you need to find and assign a song.

At the top, you'll see what event you're assigning FOR and what's currently assigned to it.

**Source Filters** — these tabs narrow your song list to a specific source:

- **Sound Bank** — shows songs from the Bandroom Default Song Pack, pre-matched to your team and events. If you loaded the pack, suggestions appear here automatically
- **Marketplace Downloads** — songs you've downloaded from the community marketplace
- **Trimmed Clips** — songs you trimmed yourself using the built-in trimmer. Saved separately so you always know which version is the trimmed one
- **Your Imports** — songs you dragged in or browsed from your own computer
- **All Songs** — everything combined. This can be overwhelming; it's listed last for a reason

There's also a **Browse Other Team's Sound Bank** button — opens a team picker so you can borrow songs from any other team's library. Very useful when setting up the away team and thinking "I bet Alabama's fight song would work here too."

Each song in the list has its own mini play button, stop button, and a source label so you know where it came from. Click a row to select it, then hit **Assign Selected** to lock it in.

The **team sidebar** next to the song list narrows everything to one team's songs instead of scrolling through everything.

### Copy From

The **Copy From** button on each event card is one of the biggest time-savers in Bandroom. Say you already set up "Offense: Second Down Short" with a perfect song, PA clip, and whistle setting. Click Copy From, pick that event from a dropdown, and everything — the main song, the PA announcer clip, the whistle toggle — gets copied to the current event. No re-searching, no re-trimming, no re-assigning.

This is especially useful for down-and-distance variants where you want the same song across 1st/2nd/3rd down situations.

### PA Announcer Layer

Bandroom can play TWO audio files simultaneously for each event — your main hype song, and a separate PA Announcer clip. The PA clip is meant to be a short voice recording — like a real stadium announcer calling the play under your band hit. It has its own volume slider so you can balance it against the music.

This is a newer feature and still being tested against live games — if you have PA clips, assign them using the **Assign PA** button next to the main Assign/Edit button.

### Auto-Assign

If you've loaded the **Default Song Pack** for a team (see next section), you can use Auto-Assign to fill in your events automatically. There are two modes:

**Quick Overwrite** — replaces EVERY assigned song for the active team with the default pack's suggestions. This is destructive — any hand-tuned assignments get wiped. Use this when you're starting fresh and want Bandroom to do all the work.

**Guided Assign** — walks through each event one at a time, showing you what it picked and letting you confirm or skip. You can also pick from multiple candidates if the pack has several good matches for one event. At the end, you get a summary of exactly what changed.

Guided Assign also does keyword matching: if the default pack doesn't have an exact match for an event, it scores all available songs by how many significant words they share with the event name and suggests the best candidates.

### Default Song Pack

Instead of finding and assigning every single song yourself, Bandroom offers a big, free, one-time download called the **Default Song Pack** — thousands of songs already sorted by team and situation, covering roughly 950 pre-matched songs across 62 teams.

When you import it, Bandroom fills in any situations you haven't already assigned a song for. It will **never replace a song you chose yourself** unless you specifically use Quick Overwrite mode.

Because the pack is large (a few gigabytes), you download it from a link that opens in your web browser, then come back to Bandroom and use **Locate & Import** to point Bandroom at the file you downloaded.

You can see where the pack is saved on your computer, or move it to a different folder or drive (useful if your main drive is small), from the command palette — press Ctrl+K and search "song pack."

### Universal Profile Override

You can save a **Universal Profile** — a set of song assignments that isn't tied to any one team. When active, it overrides whatever songs are assigned to individual team profiles, so you hear the same set of songs no matter which team you're using.

Turn it on or off from your profile screen. Turning it off goes back to each team's own assigned songs. This is useful for streamers or content creators who want a consistent audio experience regardless of the matchup.

---

## Part 6 — The Inline Trimmer

The **Trim** button in the Clipper Island opens a full waveform editor — right inside Bandroom, no separate window, no external app needed. It loads the actual audio waveform, decoded and rendered on a canvas element.

### Using the Trimmer

1. The blue waveform shows the actual peaks of your audio file
2. The shaded region between the two handles is your trim range — everything outside that gets cut off when you save
3. **Drag the start handle** (left) to cut from the beginning
4. **Drag the end handle** (right) to cut from the end — labels update in real time as you drag
5. **Zoom**: use the zoom slider or buttons to go up to 800% for frame-precise trimming. When zoomed in, click and drag to pan around the waveform like a DAW
6. **End tail auto-preview**: release the end handle and it automatically plays the last four seconds of your trimmed range — so you don't have to hit play every time you tweak the end point
7. **Preview**: hear your trim range with effects applied
8. **Stop**: silence the preview
9. **Save Trim**: the trimmed clip saves to your Songs folder, and the event card you came from auto-scrolls into view and flashes so you know exactly which card just got updated

The Trimmer also normalizes the volume when you save, so trimmed clips are consistent in loudness with the rest of your library — no jarring volume jumps between songs.

If you opened the Trimmer from the **Settings panel** (not from an event card), the Save button changes to **Set as Lead-In Whistle** — the same trimmer, but the result gets assigned as your referee whistle instead of a song.

---

## Part 7 — Audio Features

### Volume Controls

Bandroom gives you multiple levels of volume control:

- **Master Volume** — the overall output level for everything Bandroom plays. This is what you drag in the right panel
- **Home Volume** — once a matchup is set, home-team songs use this volume (as a percentage of Master)
- **Away Volume** — same for the away team
- **Per-Event Volume** — each individual event card has its own volume (relative to the team volume). Your touchdown song can be louder than your first-down song
- **PA Announcer Volume** — separate slider for the PA announcer layer

The master volume also controls previews — drag it while a preview is playing and you'll hear the change immediately.

### Volume Profile Presets

You can save different volume balance setups as presets. For example:

- "Normal" — everything at 100%, equal balance
- "Home Crowd" — home team louder, away team quieter
- "Stream" — lower overall output with more PA announcer presence

Swap between them instantly from the Settings panel. These are separate from team song profiles — volume presets only affect volume, not which songs are assigned.

### Lead-In Whistle

A short referee-whistle sound that can play right before certain songs start — like the real whistle that blows before a play. It's optional, and you can toggle it on or off for each individual event card.

To set up a whistle:

1. Go to the right panel → Lead-In Whistle section
2. Upload or select a short sound file
3. Toggle the enable switch on
4. For each event card, use the whistle toggle button to turn the whistle on or off for that specific event

If you haven't set a whistle clip yet, the toggle rows are hidden — there's nothing to enable. Once you upload one, the controls appear everywhere.

### Firing Delay

A configurable delay (0 to 5 seconds) between when the game event is detected and when the sound actually starts. This is for streamers who need to sync their audio with broadcast delay.

If your stream is 3 seconds behind, set the delay to 3 seconds. Now your touchdown song plays exactly when your viewers see the touchdown, not before.

### Sound Booth

The **Sound Booth** is a floating rack with all of Bandroom's audio effects. Access it from the Clipper Island (🎚 Sound Booth button) or from the navigation rack. It previews your songs through the full effects chain — this is different from the little play button on each library row (which plays the raw file). Sound Booth preview = what you'll actually hear during a game.

#### Reverb

Adds room sound to your songs. Four presets:

- **Off** — dry, no room sound added
- **Stadium** — a tight, open-air tail with some high end absorbed, like a real crowd soaking up sound outdoors
- **Night Game** — tighter and warmer than Stadium; more high end damped down, simulating cooler night air
- **Prime Time** — Night Game's warmth with a wider stereo image; the big-game-under-the-lights version

#### Marching Band EQ

Two modes:

- **Marching Band** — cleans up marching band recordings so they sound less muddy. Cuts out some rumble, tames boomy tuba/bass drum, and brings out trumpets and snare
- **Megaphone** — makes anything sound like it's blasting through an old stadium PA speaker, on purpose. For that lo-fi, old-school stadium announcement vibe

#### Transient Shaper

Makes drum and cymbal hits punch harder without turning up the whole song — like giving the snare a little extra crack right when it hits. On/off only.

#### Stereo Widener

Takes a recording that sounds narrow or one-note (like it's coming from one spot) and spreads it out so it sounds bigger and fuller through two speakers. On/off only.

#### Sub-Bass Thump

Adds a low rumbly "thump" under the sound on big tackle-for-loss plays — like feeling a hit in your chest, not just hearing it. Four levels:

- **Off** — no added low-end thump
- **Subtle** — a light thump, felt more than heard
- **Stadium** — a noticeably bigger thump. Reads as "that was a real hit" without overpowering the song
- **Earthquake** — the heaviest setting, strong enough to rattle a subwoofer. Can overpower quieter songs — try Stadium first if unsure

Off by default since it's a newer effect.

#### Ducking on Big Plays

When something big happens (touchdown, turnover), this quietly turns the music down for a second so the crowd sound and announcer can be heard clearly, then brings the music back up on its own. On/off only.

#### Controller Rumble

If you have an Xbox or PlayStation-style controller plugged into your PC, this gives it a light buzz when the game is close and the clock is running out — specifically, the last 2 minutes of the 4th quarter or overtime, with the score within a touchdown either way. Needs a controller connected to do anything.

#### Crowd Bus

Plays a looping crowd-noise sound in the background that gets louder automatically when:

- The game is close (small score margin)
- It's the 4th quarter
- Time is running out

It stays quieter the rest of the time. You have to pick your own crowd-noise sound file first (use the **Set Crowd Clip** button in the Sound Booth) since Bandroom doesn't come with one built in.

---

## Part 8 — Marketplace & Community

### The Bandroom Marketplace

**The Bandroom** is the shared community marketplace — accessible from the header button or the navigation rack. Every Bandroom user in the world can upload and download songs and backgrounds from each other's team Sound Banks.

It works on a simple principle: each team has its own Sound Bank. You can browse any team's Sound Bank, download anything you like, and upload your own songs and backgrounds for others to use.

### Downloading

1. Open a team's Sound Bank from the marketplace
2. Find a song or background you like
3. Press the download button

The download goes to **My Downloads** on your computer. It doesn't automatically become one of your assigned songs — you still have to assign it through the Clipper Island (Part 5). This way you can collect a library first and assign everything later.

### Uploading

1. Open a team's Sound Bank
2. Press **+ Upload**
3. Pick a song or background from your computer
4. Give it a clear name — team name + what situation it's for, like "UGA 3rd Down Stop". This helps other users find it
5. Bandroom automatically trims and normalizes the volume so your upload sounds consistent with everyone else's

### Like / Dislike

Every upload has a heart (like) and a thumbs-down (dislike) button. This is just feedback — it doesn't delete anything. It helps good uploads rise to the top and lets other users know which songs are worth downloading.

### Popular Songs Shelf

On the marketplace's front page, songs are ranked by downloads + likes combined. The best stuff floats to the top automatically, based on real community feedback.

### Profile Sharing

This is different from uploading a single song — **Profile Sharing** lets you share your WHOLE team's setup (which song plays for which situation) so someone else can copy it in one click, instead of assigning 30+ songs by hand.

To share your profile: go to your Profile and use the sharing option to generate a link. Anyone with that link can load your entire assignment setup for that team into their own Bandroom.

To load someone else's profile: use the link they shared with you. Bandroom imports their assignments but doesn't overwrite your existing songs — you choose which events to replace.

### Team Logos and Backgrounds (Public)

Every custom team logo you save is shared to the community by default, so other users with the same team see it too. The same goes for team backgrounds. This is automatic — no extra step needed.

### Discord Chat

Bandroom has a built-in Discord chat panel, accessible from the navigation rack. It connects to the Bandroom community server so you can share tips, ask questions, and coordinate with other users without leaving the app.

---

## Part 9 — Settings, Profiles & Power Features

### Config Profile Manager

You can save and switch between multiple complete Bandroom setups — not just which songs are assigned, but everything: volume presets, active team, audio settings, Sound Booth configuration, whistle on/off, and more.

This is useful if:
- You share a computer with someone else who also uses Bandroom
- You want one profile for streaming and one for personal play
- You want to experiment with a completely different setup without losing your current one

Manage profiles from the Settings panel or the command palette.

### Cross-Device Profile Sync

If you're signed in with Google, your entire setup — team assignments, profiles, settings, and stats — follows you to any computer you sign in on. Install Bandroom on a new PC, sign in, and everything is exactly how you left it.

### Streamer Mode

Toggle **Streamer Mode** from the navigation rack. When on:
- Personal info (your name, avatar, profile link) is hidden from the UI
- The presence dot is hidden
- UI sounds are muted
- The app is safe to have on screen while broadcasting

The Streamer Mode indicator appears in the UI when active so you never forget it's on.

### Keyboard Shortcuts & Global Hotkeys

Open the **Keyboard Shortcuts** panel from the navigation rack to see every hotkey. You can customize global hotkeys — key combinations that trigger actions even when Bandroom isn't the focused window, so you can trigger actions while the game has your full screen.

The command palette (Ctrl+K) also shows shortcuts and lets you search them.

### Team Backgrounds

The big picture behind the whole app is called a **Team Background**. Every team can have its own. You can:
- Pick one from a team's Sound Bank (marketplace)
- Download one someone else uploaded and set it from My Downloads
- Upload your own custom picture — open a team's Sound Bank, click upload, and choose a background image

Backgrounds are separate from logos — the background fills the app, the logo is the team's icon.

### Team Logos

If your team is missing its logo, or you want to use a custom one:
1. Find the team's tile in the Team picker
2. Click the small pencil icon on it
3. Upload any image, drag it into place, zoom with a slider, and save

Your custom logo is instantly applied and shared with the community so other users of the same team see it too.

### Batch Logo Import

If you have a folder full of team logos, you can import them all at once. This is an advanced feature not surfaced in the regular UI — if you need it, ask in the Discord or check the documentation for the batch logo folder import workflow.

### In-App Changelog

See what changed in the latest update without leaving the app. Open from the Settings panel → Changelog tab.

### Crash Reporting

If Bandroom crashes, it automatically captures details about what happened and saves a crash log. This makes bugs fixable instead of a mystery — the crash log tells the developer exactly what went wrong without you having to describe it.

### Version Compatibility Guard

When Bandroom updates, your saved profiles might be in an older format. The version guard checks your saved profiles against the running version before loading them, so a new update never corrupts your old setup.

### Event Log

The **Event Log** (accessible from the Help & Guide overlay → Event Log tab) is a live feed of everything Bandroom just played or skipped, and why. It shows:

- Which event fired
- Which side it fired for (Home/Away)
- Which song was played (or "skipped — no song assigned")
- The timestamp of each event
- Whether Big Game mode was active

You can **export the event log** as a file (Save Log File button) — useful for:
- Debugging "why didn't my song play?"
- Sharing with support when something doesn't seem right
- Reviewing your game after it's over

---

## Part 10 — Troubleshooting & FAQ

### No Sound Is Playing? Try This, In Order

1. **Check the master volume slider** — is it turned up?
2. **Check that songs are actually assigned** — open the Situations panel and look at the event cards. If most dots are dim gray, you haven't assigned songs yet. Use the Default Song Pack or assign songs manually through the Clipper Island
3. **Check that Watching is active** — Bandroom only fires events when the Watching toggle is on. Press it again if needed
4. **Check that a matchup is set** — events won't fire without a Home and Away team configured
5. **Check your audio output device** — is Windows sending audio to the right speakers or headphones?
6. **Check the Event Log** — open Help & Guide → Event Log tab and watch for "skipped" messages. The log tells you exactly why something didn't play
7. **Restart Bandroom** — sometimes the simplest fix works

### "Why Didn't My Song Play?"

Use the **Event Log** (Help & Guide → Event Log). It shows a live feed of exactly what Bandroom detected and what it did about it. If you see "skipped: no song assigned," you need to assign a song to that event. If you see an event fire but the wrong song played, check which team is active and which songs are assigned to that specific event for that specific team.

### "My Custom Team Doesn't Auto-Detect Any Plays"

This is expected. A TeamBuilder team (one you added yourself) doesn't exist inside the video game, so there's nothing on screen for Bandroom to read. See Part 3 — TeamBuilder for the full explanation and the correct workflow.

### "Not All the Teams Are Showing"

The team list is sorted A through Z with all teams mixed together — FBS and FCS schools are not grouped separately. If you can't find a team, use the search box at the top of the team picker. If it's still not there, you may need to add it via TeamBuilder.

### "An Update Broke Something — How Do I Go Back?"

Bandroom keeps a version history. If a new update causes problems, you can roll back to the previous version. Check the Bandroom install folder (`%LocalAppData%\Bandroom`) for previous version directories, or reach out on Discord for help.

### "How Do I Share My Whole Setup With Someone?"

Use **Profile Sharing** (Part 8). From your Profile, generate a share link. Anyone with that link can load your entire assignment setup for that team.

### "Can I Use Bandroom While Streaming?"

Yes — use **Streamer Mode** to hide personal info, and set the **Firing Delay** to match your stream's broadcast delay so songs play in sync with what viewers see.

### "Do I Need to Be Signed In?"

No. Signing in gives you marketplace access, profile sync, and stat tracking. The core engine — game watching and song playback — works offline with no account.

### "Can I Use My Own Songs?"

Yes. Drag and drop audio files into Bandroom, or use the import/browse buttons in the Clipper Island. Imported songs appear in the "Your Imports" filter. You can also download songs from the marketplace.

### "Is Any of This Required?"

No. Everything — marketplace, default pack, PA announcer, whistle, backgrounds, profiles — is optional. Bandroom works with just you selecting a matchup and assigning a few songs to the events you care about.

### "How Do I Use Bandroom on a Mac?"

The Mac version is in progress. The engine and audio playback already run natively on macOS with identical detection logic. The full Mac app with UI is still being built. Check the Bandroom Discord or GitHub for the latest status.

### "How Do I Know If My Custom Logo Actually Shared?"

Custom team logos are shared automatically when you save them. If you saved a logo and other users can't see it, try re-saving it. If the issue persists, check the Discord — the sharing service may need attention.

### Tips & Tricks (40 Real, Verified Tips)

These are the same tips that appear in Bandroom's built-in Help & Guide panel, collected here for reference:

1. You can upload your own songs to a team's Sound Bank from that team's album view
2. Every upload gets a Like and a Dislike button — your feedback helps good uploads rise to the top
3. The Popular Songs shelf in the marketplace hub is ranked by downloads + likes combined
4. Apply to All Teams copies your current team's song setup to every other team at once
5. Streamer Mode hides your personal info so it's safe to have on screen while broadcasting
6. The Discord panel lets you chat without leaving Bandroom
7. Soundboard favorites let you trigger any sound with one click
8. The event log shows live game events as they happen
9. Offline mode keeps Bandroom working when you lose connection
10. The green dot on team tiles means that team has a profile
11. Toast notifications tell you what just happened
12. The onboarding screen helps new users pick their first team
13. You can choose between stadium, dome, and night game reverb
14. Song assignments auto-save — you never have to hit Ctrl+S
15. Keyboard arrow keys navigate most lists and grids
16. Context menus appear on right-click throughout the app
17. Join the Discord to share tips and songs with the community
18. Bandroom is built by one developer — feedback is always welcome
19. The Copy From button on each event card is your best friend. Set up one event perfectly, then copy it everywhere
20. Your songs, profiles, and settings all auto-save. Assign a song, it's saved. Trim a clip, it's saved. Close the app, everything is still there
21. If you ever see an event that says "Unassigned" and you're SURE you assigned something to it, check which team is active. The Band Room only shows one team at a time
22. The Clipper Island and Trimmer are the SAME panel. They swap in and out of the same space
23. The Trimmer saves trimmed clips to your Songs folder, and the event card you came from auto-scrolls into view and flashes so you know exactly which card just got updated
24. The firing delay setting syncs your audio with broadcast delay — if your stream is 3 seconds behind, set the delay to 3 seconds
25. The lead-in whistle does NOT play during song-list previews — only in-game
26. Each event card has its own volume independent of the master
27. Press Ctrl+K to open the command palette — type anything you want to do and jump straight there
28. The matchup sidebar lets you flip between editing the Home and Away team's song assignments with one click
29. Team presets (Plain/Home/Away/Big Game) give you four completely different song profiles per team
30. Copy any preset to any other with the copy dropdown
31. Auto-Assign with Guided Assign walks through every event one at a time so you can confirm each song
32. The Track Info drawer lets you edit title, artist, and acoustic fingerprint metadata on any song
33. The event log is exportable — use "Save Log File" to save a record of your game
34. You can browse another team's Sound Bank from inside the Clipper Island without switching your active team
35. The Sound Booth preview plays through the full effects chain — different from the little play button on library rows
36. The Sub-Bass Thump effect works on tackle-for-loss plays specifically
37. Crowd Bus needs you to pick your own crowd-noise file first — Bandroom doesn't come with one built in
38. Controller Rumble only works with a controller plugged in, and only in close late-game situations
39. Firing delay is per-event configurable — set it once in Settings and it applies to everything
40. If the Default Song Pack doesn't have a match for a particular event, Guided Assign scores all available songs by keyword overlap and suggests the best candidates

### Things People Commonly Miss

- **Per-event volume** — most people set the master volume and stop there. Every event card has its own slider. Your touchdown song SHOULD be louder than your second-down song
- **Copy From** — the biggest time-saver in the app. Most people assign 30 songs manually before noticing they could've copied their best three assignments everywhere
- **Guided Assign** — the Auto-Assign popup has TWO buttons: Quick Overwrite (destructive) and Guided Assign (step through one at a time). Guided Assign is slower but safer
- **The command palette (Ctrl+K)** — type "relocate" to move your song pack folder, "reset" to wipe a team's profile, "save" to save your config, and a dozen other actions. Most power users navigate exclusively through Ctrl+K
- **Quick-load by abbreviation** — on the Assign page's matchup sidebar, type a team's abbreviation (like "LSU") into the quick-load box and it suggests the matching team. Hit confirm to switch your active team instantly
- **The Event Log** — if a song didn't play and you don't know why, the event log tells you. It's under Help & Guide → Event Log
- **Track Info** — the little (i) button on each event card opens a metadata editor. You can set a title, artist, and acoustic fingerprint that helps the marketplace search find your songs
- **Big Game mode** — when Big Game is toggled on in Game Settings, the routing rules change: defensive events fire for BOTH sides at different volumes, and field-position volume layering kicks in

---

## Part 11 — Real-World Walkthroughs & Recipes

This section walks through common setups step by step. If you're not sure where to start, find the scenario that matches what you want to do and follow it exactly.

### Recipe 1 — First-Time Setup (Everything From Scratch)

You just installed Bandroom, picked your favorite team, and want to be ready for a game tonight.

1. **Sign in with Google** (top-right) — optional but recommended so your setup syncs
2. **Load the Default Song Pack** — from the command palette (Ctrl+K), type "song pack" and select "Download / Import Default Song Pack." This gives you ~950 pre-matched songs across 62 teams. Download the big zip file in your browser, then come back to Bandroom and use "Locate & Import" to point at the file
3. **Wait for the import** — it takes a minute or two. Bandroom processes each file and organizes them by team and situation
4. **Open the Situations panel** — pick a category (like "Offense" or "Hype") and look at the event cards. The status LEDs should be green for events that got auto-filled from the pack
5. **Preview a few songs** — click Play on some cards to make sure audio is working and you like what was assigned
6. **Set your matchup** — click Set Matchup in the navigation rack, pick Away and Home teams, press GAMETIME
7. **Press Watching** — the engine is now live. Start your game
8. **Monitor the Event Log** — open Help & Guide → Event Log tab and watch for "skipped" messages during the game. These tell you which events still need songs assigned
9. **After the game, fill in the gaps** — go back to the Situations panel and assign songs to any events that showed as skipped

**Estimated time:** 10 minutes for setup + however long the pack download takes on your connection.

### Recipe 2 — Setting Up for a Stream

You're broadcasting on Twitch or YouTube and want Bandroom audio to sync with your viewers' experience.

1. **First, do everything in Recipe 1** — get your songs assigned and your matchup set
2. **Find your stream's broadcast delay** — most streams are 2–5 seconds behind real time. Check your streaming software's settings or do a test stream where you clap on camera and listen for the delay
3. **Set the Firing Delay** — in the right panel, drag the Firing Delay slider to match your broadcast delay. If your stream is 3 seconds behind, set it to 3
4. **Turn on Streamer Mode** — click Streamer Mode in the navigation rack. This hides your name, avatar, and profile link from the UI so it's safe to have Bandroom visible on stream
5. **Check your volume balance** — during a test run, make sure game audio and Bandroom audio are balanced. Use the Master Volume slider in Bandroom and your streaming software's audio mixer
6. **Save a Volume Preset** — once the balance is right, save it as a preset so you can recall it instantly next time
7. **Optional: set up a Universal Profile** — if you stream different teams regularly and want the same songs every time regardless of matchup, set up a Universal Profile from your Profile screen. Toggle it on before going live

**Streamer pro tip:** The Event Log is exportable. If a viewer says "I didn't hear the touchdown song," you can check the log after the game to see exactly what fired and when.
ir
### Recipe 3 — Setting Up Both Teams (Running Sound for a Watch Party)

You're running audio for a group watching the game, and you want both teams' band cues to fire.

1. **Do Recipe 1 for BOTH teams** — switch your active team to the Home team, assign songs. Switch to the Away team, assign songs. Use "Browse Other Team's Sound Bank" in the Clipper Island to borrow songs between teams
2. **Balance the volumes** — in the right panel, set Home Volume and Away Volume to appropriate levels. If you're at a party where most people are rooting for the home team, maybe set Home at 100% and Away at 70%
3. **Use Copy From heavily** — set up one team's offense events perfectly, then switch to the other team and use Copy From on each event card, selecting the first team as the source. This copies the song structure (you still pick different songs, but the PA clips and whistle toggles transfer)
4. **Consider Big Game mode** — if it's a rivalry or championship game, toggle Big Game on in Game Settings. This makes defensive events fire for BOTH sides (one at full volume, one ducked) so both bands' reactions are audible
5. **Use the Matchup Sidebar during the game** — click Home or Away in the center panel to quickly check or tweak either team's assignments mid-game without losing your place
6. **Save both teams as a Config Profile** — after everything is dialed in, save your Config Profile so you can reload this exact two-team setup any time these same teams play again

### Recipe 4 — Adding Your Own Custom School (TeamBuilder Workflow)

Your school isn't in Bandroom, but you play as them through a roster mod or created team in the game.

1. **Add the school in TeamBuilder** (see Part 3 — TeamBuilder for full steps): name, colors, mascot, logo
2. **Assign songs to your new school** — switch your active team to your new school and set up all the events you care about. Use the Default Song Pack if you want a starting point (pick a built-in team with similar fight songs as the source)
3. **In the game, note which real school your created team replaces** — e.g., if you created "Northwest State" and replaced "UTSA" in the game's roster, UTSA is your real in-game school
4. **In Bandroom, Set Matchup using the REAL school** — Away: [opponent], Home: UTSA (or whichever real school you replaced)
5. **Keep your active team on your custom school** — the app shows your colors and uses your Sound Bank
6. **Verify it works** — start a game, watch the Event Log. You should see events fire based on the real school (UTSA) but hear songs from your custom school's assignments

**Important:** If your custom school plays against another custom school (both created in the game), you'll need BOTH sides to map to real built-in schools in Set Matchup. The engine needs at least one real school name to read from the scorebug.

### Recipe 5 — Creating a "Big Game" Set Without Starting Over

You already have your everyday (Plain) songs assigned. Now you want a special set for rivalry week.

1. **Make sure your Plain preset is fully configured** — all the events you want should have green status LEDs
2. **Copy Plain → Big Game** — use the copy dropdown between the preset pills. This duplicates everything from Plain into Big Game
3. **Switch to Big Game** — click the Big Game pill. All your assignments are there, identical to Plain
4. **Swap out the special songs** — for events where you want a different song in big games (touchdown, 3rd down stop, take the field), click Assign/Edit and pick a different song. The rest stay the same as Plain
5. **Turn on Big Game mode** — in the right panel, toggle Big Game on. This activates the dual-fire routing so both teams' defensive events play
6. **Save your profile** — hit Save Profile in the navigation rack to preserve the Big Game assignments
7. **Before the rivalry game, just toggle Big Game on** — Bandroom auto-picks the Big Game preset when Big Game mode is active, so you don't have to remember to switch presets manually

### Recipe 6 — Setting Up Crowd Noise (Crowd Bus)

You want background crowd noise that gets louder when the game gets tense.

1. **Find a crowd-noise audio file** — a looping stadium ambiance track. Bandroom doesn't come with one
2. **Open the Sound Booth** — from the Clipper Island (🎚 button) or the navigation rack
3. **Click "Set Crowd Clip"** — browse to your crowd-noise file and select it
4. **Enable Crowd Bus** — toggle the Crowd Bus switch on in the Sound Booth
5. **Adjust the other Sound Booth settings** — you probably want Ducking ON (so the music ducks under the crowd on big plays) and Reverb set to Stadium for outdoor ambiance
6. **Test during a game** — the crowd noise plays at a low level normally, and automatically gets louder when the score is close, it's the 4th quarter, or time is running out

### Recipe 7 — Diagnosing "That One Song That Never Played"

A specific event isn't firing and you want to know why.

1. **Open the Event Log** — Help & Guide → Event Log tab. Keep it open during a game
2. **Watch for the event name** — when the situation you're expecting should happen, look at the log
3. **If you see the event with "skipped"** — the song wasn't assigned. Go to the Situations panel, find that event card, assign a song
4. **If you see the event name with a song filename but didn't hear it** — check volume: master, home/away balance, and the per-event volume slider on that specific card
5. **If you don't see the event at all** — Bandroom didn't detect the situation. Possible causes: wrong Scorebug Preset (change it in the right panel), the scorebug text wasn't clear enough (try adjusting the game's display settings), or the event doesn't match what Bandroom watches for (e.g., a custom animation that doesn't use standard scorebug text)
6. **Export the log** — click "Save Log File" to save a record for support or your own review

### Recipe 8 — Backing Up and Restoring Your Setup

You're moving to a new computer or want a safety copy.

**Option A — Google Sign-In (automatic)**
- If you're signed in with Google, your profile syncs automatically. On the new computer, just install Bandroom and sign in. Everything appears

**Option B — Manual backup**
1. Open File Explorer and paste `%LocalAppData%\Bandroom` into the address bar
2. Copy these folders to a backup location (USB drive, cloud storage):
   - `Songs` — all your imported songs and trimmed clips
   - `TeamLogos` — custom team logos
   - `TeamBackgrounds` — custom backgrounds
   - The profile `.json` files in the main folder — your song assignments and settings
3. On the new computer, install Bandroom, then copy these folders into the new `%LocalAppData%\Bandroom` folder
4. Restart Bandroom

**Note:** Your Default Song Pack might have a different location if you relocated it. Check with Ctrl+K → "Move Default Song Pack Folder" to see where it lives.

---

## Part 12 — Advanced Knowledge & Under the Hood

This section is for power users who want to understand exactly how things work. Nothing here is required reading — Bandroom works fine without knowing any of this — but if you've ever wondered "why did it do that?" or "how does it actually decide?", these explanations will help.

### How Possession Detection Actually Works

Bandroom doesn't guess who has the ball — it reads it directly off the screen, using two different methods depending on which scorebug style you're using:

**Underline method (CFB27 and CBS-style):** The down-and-distance ribbon on the scorebug has a colored underline or indicator bar. Bandroom samples the brightness of specific pixels at the "away underline" and "home underline" positions. Whichever side's pixels are brighter (more color present) is the team that has the ball. This requires a brightness margin of at least 25 points between the two sides to confirm a read — so borderline lighting conditions don't produce a false positive.

**Legacy color-sampling method (older scorebug presets):** Samples the overall team color of the down ribbon directly. Less precise than the underline method, but works for scorebug styles that don't have a distinct underline indicator.

**Possession confirmation cooldown:** Once possession is confirmed, Bandroom enforces a 1.2-second cooldown before it can change again. This prevents single-frame flickers from causing a false read — for example, a brief pause-menu freeze or a transitional animation frame. However, a late-cooldown correction path exists: if at least 0.6 seconds of the cooldown have elapsed AND a new read is much stronger (35-point brightness margin instead of the usual 25), Bandroom will correct a stale possession read. This was added to fix a real bug where fourth-down and tackle-for-loss events were routing to the wrong team because possession had been locked in too early.

**Frozen-frame guard:** The engine's main event loop has a guard that detects when the screen hasn't changed (e.g., the game is paused, or a menu is up) and skips processing during those frames. This prevents stale data from being treated as a new event.

### How Timeout Detection Works

Timeout detection went through a major rewrite in v1.1. The old method:

1. Waited for the score to not change for a while
2. Checked if there was still plenty of time on the clock (more than 240 seconds)
3. Guessed that a timeout had been called

This missed almost every timeout before the 2-minute drill, and fired false positives when the clock simply ran during a long drive.

The new method:

1. Reads the actual "TIME OUT" text from the scorebug's situation region using OCR
2. Samples the timeout dash marks (the little indicators showing how many timeouts each team has left) using pixel brightness — there's no text there to read, so this is a pure color/brightness comparison
3. Fires the appropriate timeout event with the correct remaining count (4, 3, 2, 1, or 0)

The timeout-segment sampling runs every tick, independent of the game-state guard that protects possession-color sampling during banner frames — timeout reads don't have the same failure mode as color reads, so they're safe to run unconditionally.

### How the Engine Avoids Double-Fires

Several safeguards prevent the same event from firing twice in a row:

- **High-priority overlap guard:** A global timestamp (`HighPriorityOverlapGrace`) prevents any high-priority event (touchdown, turnover, safety) from re-firing within 6 seconds of the last high-priority event — regardless of which side it was for. This prevents a bounce in the scoring banner read from triggering "Touchdown" twice.
- **Per-event state tracking:** Each evaluator tracks its own "last known state" and only fires when the state *changes*. For example, `OffenseDownHelper` remembers that the last tick was "2nd & 7" and only fires when it becomes "1st & 10" — not every tick that it reads "1st & 10."
- **Dual-fire pairing with shared state:** For events like 2nd Down Short (offense loud, defense ducked), both evaluators fire on the same tick using a buffered pairing pattern — the defense helper checks whether the offense helper is about to fire for that same tick and, if so, fires its own ducked version simultaneously rather than on the next tick. This prevents a one-tick delay that would sound like an echo.

### How Kickoff Detection Chains Work

Kickoff detection is surprisingly complex because multiple things need to happen in the right sequence:

1. A scoring play happens (touchdown, field goal, PAT)
2. The game transitions through the scoring screen
3. The "KICKOFF" text appears on the scorebug's situation region
4. Bandroom must fire the kickoff cue BEFORE the first snap (which would trigger a drive starter or first-down event instead)

The pre-v1.1 code had a problem: if the PAT text didn't read perfectly (e.g., "PAT GOOD" was partially obscured or OCR'd as "PAT GO0D"), the entire kickoff chain would break — no PAT song, AND no kickoff song, leaving total silence for the entire possession transition. The fix was restoring a standalone "Other: Kickoff" cue that fires on any kickoff transition that isn't the opening or second-half special case, independent of whether the PAT was read correctly.

The opening kickoff and second-half kickoff are handled separately because they have different game-state signals (quarter == 1 and situation == "KICKOFF" for the opener; quarter changes from 2 to 3 for the second-half kickoff).

### How Big Game Mode Changes Routing

Big Game mode (the toggle in Game Settings) changes three things:

1. **Defensive events become dual-fire:** In normal mode, a defensive stop only fires for the team whose defense made the play. In Big Game mode, it fires for BOTH sides — the defense's team at full volume, the offense's team at a ducked (60%) volume. This means both bands' reactions are audible.

2. **Field-position volume layering:** In Big Game mode, an additional volume multiplier is applied based on where the ball is on the field — the team whose end zone the ball is closer to gets proportionally louder volume, simulating the crowd getting louder as the ball approaches.

3. **Preset auto-selection:** At GAMETIME, if Big Game mode is on, Bandroom automatically picks the "Big Game" profile for each team rather than their Plain/Home/Away presets.

### How the Trimmer's 800% Zoom Works

The waveform in the Trimmer is rendered on an HTML5 canvas element. The raw audio samples are decoded in JavaScript (not sent to the C# side), and drawn as a series of vertical bars representing the amplitude at each sample point. When you zoom in, the canvas only draws the portion of the waveform that's within the zoomed viewport — it recalculates which samples fall inside the visible range and redraws only those, at higher resolution (fewer samples per pixel). The zoom slider adjusts the viewport width as a fraction of the total duration. Panning (click and drag) shifts the viewport start position.

The end-tail auto-preview works by setting a temporary play range of (endHandle - 4 seconds) to (endHandle) and playing only that segment. It doesn't create a new audio buffer — it just seeks to the calculated position and plays for 4 seconds or until the end handle, whichever comes first.

### How the Cloudflare Backend Works

Bandroom's online services run on Cloudflare Workers — small serverless functions that run at Cloudflare's edge locations worldwide. There are two main workers:

- **Marketplace Worker** (`cloudflare-marketplace/worker.js`): Handles song and background uploads (stored in Cloudflare R2, an object storage service) and listings (stored in Cloudflare KV, a key-value database). The `/upload` endpoint accepts a file plus metadata (name, school, situation, uploader); the `/list` endpoint returns all songs for a given team, sorted by downloads + likes.
- **Usercount Worker** (`cloudflare-usercount/worker.js`): Tracks how many users are currently online and relays the Discord chat between Bandroom instances.

Both workers were deployed and are actively monitored. A previous issue where the logo-sharing portion of the marketplace worker had been "coded but never deployed" was fixed in August 2026 — newly saved logos will now actually reach other users, though any logos saved before that fix would need to be re-saved to be shared.

### How the Song Intake Engine Sorts Files

When you import a folder of songs (e.g., the Default Song Pack), the `IntakeEngine` processes each file:

1. **Clean the filename** — strips track numbers, bitrate tags, and extra whitespace
2. **Guess the team** — matches filename tokens against a dictionary of team names and aliases (e.g., "UGA" → "Georgia", "Bama" → "Alabama")
3. **Conference disambiguation** — if a token could match multiple teams (e.g., "UM" could be Miami, Michigan, or Montana depending on context), the engine checks for conference hints in the filename or folder path. Inside a folder containing "B1G" or "Big Ten", "UM" resolves to Michigan; elsewhere, it resolves to Miami. This was added specifically because the old code always picked Miami, dumping Michigan songs into the wrong folder when importing Big Ten packs
4. **Guess the trigger** — matches against event names and keywords (e.g., a file with "3rd Down" in its name gets mapped to `Defense: Third Down`)
5. **Assign to the team's Sound Bank** — copies the file to the appropriate team folder with normalized naming

### How Song Metadata and the Track Info Drawer Work

Every song file Bandroom knows about can carry metadata — title, artist, school, energy level, and an "acoustic fingerprint" (a short description like "punchy brass hit with snare roll build" that helps marketplace search find it). This metadata is saved inside the song file itself (as a small JSON blob appended to the audio data), not in a separate database — so it travels with the file when you copy or share it.

The **Track Info drawer** (the (i) button on each event card) lets you:

- View the current metadata for the assigned song
- Edit any field
- Click **Suggest** to auto-fill metadata from the file (reads filename, duration, and any existing embedded tags)
- Click **Save** to write the metadata back to the file

When you rename a file through Track Info, Bandroom automatically updates the filename reference in every event card that uses that song — so renaming "touchdown_v3_final_mixdown_2.wav" to "Georgia Touchdown.wav" doesn't break your assignments.

### How Profile Sharing Actually Works

When you share your profile, Bandroom exports a list of all your assigned EventKeys and their corresponding song filenames, PA clips, and whistle settings. This list is encoded as a shareable link. When someone else loads your profile:

1. Bandroom downloads the assignment list
2. For each event, it tries to match the filename against the recipient's own song library (including marketplace downloads and the default pack). If it finds a match, it assigns that song. If not, it leaves the event unassigned — it NEVER copies actual audio files, just the assignment map
3. The recipient's existing assignments are preserved unless they explicitly choose to overwrite

This means profile sharing works best when both people have the same Default Song Pack loaded — most filenames will match and the whole setup transfers in seconds. If the recipient doesn't have the same songs, they'll get a partial transfer (only the events where filenames matched).

### Common Confusions & Misunderstood Features

**"The app changed colors but my songs didn't change"**
This happens when you switch your active team but the matchup teams are different. Remember: active team controls colors and which team's assignments you SEE. Matchup controls which team's songs actually PLAY during a game. If you're watching Georgia vs. Florida, the active team could be either one — the engine routes songs based on the matchup, not the active team.

**"I changed the master volume but previews sound the same"**
The master volume controls in-game playback and what you hear through the Sound Booth preview (which runs through the full effects chain). The little play button on each library row in the Clipper Island plays the RAW file directly — it bypasses the master volume and effects chain entirely. This is by design so you can hear what the source file actually sounds like before effects.

**"My songs play but they're the wrong songs"**
Check which team's profile you're editing vs. which team is active vs. which team the matchup says has the ball. Three different things. Also check if a Universal Profile Override is on — it overrides individual team assignments.

**"The Event Log shows events firing but I don't hear anything"**
Check: Master Volume, Home/Away balance, per-event volume on that specific card, your Windows audio output device, and whether the Windows volume mixer has Bandroom muted separately from other apps.

**"I copied my Plain preset to Big Game but now changing things in Plain also changes Big Game"**
This shouldn't happen — presets are completely independent once copied. However, if you're LOOKING at the Big Game preset while you think you're editing Plain (or vice versa), it's easy to edit the wrong one. The active pill above the situations panel tells you which preset you're editing. The pill that glows in team color is the one you're changing.

**"The trimmer won't let me zoom past 800%"**
That's the maximum. 800% gives you frame-precise control for audio at standard sample rates — any more zoom and you'd be looking at individual audio samples, which isn't useful for trimming song clips.

**"I uploaded a logo but nobody else can see it"**
Logo sharing was broken until August 2026 — the sharing service existed in code but hadn't been deployed. It's fixed now. If you saved a logo before that date, re-save it (open the logo tool and save again) to trigger sharing. New saves work immediately.

**"The timeout song fires in the 1st quarter — that's wrong, right?"**
No. The old timeout detection (pre-v1.1) only worked late in games. The new system fires whenever a TIME OUT banner appears, regardless of quarter. If you don't want timeout songs in early quarters, assign silent audio files to those events, or use the per-event whistle toggle (with no whistle clip) to effectively mute them.

### Appendix F — Known Limitations (v1.1 Early Access)

These are things that are known to need work. None of them are bugs — they're just not built yet or still being tuned.

- **Flag and Banner OCR regions are not yet calibrated.** The penalty banner and full-screen scoring banner detection regions exist in code but haven't had their exact screen coordinates set. Penalty events can still fire through the down/situation regions in some cases. Full fix requires calibration screenshots.
- **Mac UI is not yet released.** The engine and audio playback run natively on macOS. The full graphical interface is still being built.
- **Only CFB27 and CBS scorebug presets are calibrated.** If you're using a different game or display mode, the engine may not find the right text regions. Additional presets need calibration screenshots.
- **High-priority overlap is not per-channel.** The 6-second guard against double-fires applies globally (across all sides), not per-team. In theory, two different teams scoring rapid-fire (a pick-six followed by a kickoff return touchdown) could have the second event suppressed. This hasn't been observed in real-world use but is a known design limitation.
- **The match-up screen team name font is a temporary approximation.** It uses Arial Black with CSS transforms (skew, scale) rather than a proper sports block font. A permanent font selection is pending.
- **Crowd Bus requires your own audio file.** Bandroom doesn't ship with built-in crowd noise. You need to find and load your own looping stadium ambiance track.
- **Logo sharing was broken until August 2026.** Any logos saved before the fix won't appear for other users until re-saved.

---

## Appendices

### Appendix A — File Locations

| What | Where |
|---|---|
| App install folder | `%LocalAppData%\Bandroom` |
| Your songs | `%LocalAppData%\Bandroom\Songs` |
| Team backgrounds | `%LocalAppData%\Bandroom\TeamBackgrounds` |
| Team logos | `%LocalAppData%\Bandroom\TeamLogos` |
| Default song pack | Configurable — use Ctrl+K → "Move Default Song Pack Folder" to see or change |
| Crash logs | `%LocalAppData%\Bandroom\CrashLogs` |
| Exported event logs | Saved wherever you choose when you click "Save Log File" |

### Appendix B — All Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+K | Open command palette |
| *(Customizable)* | Global hotkeys for triggering actions from anywhere — configure in Settings → Hotkeys |

Additional shortcuts are available in the Keyboard Shortcuts panel (navigation rack).

### Appendix C — Supported Scorebug Styles

Bandroom supports multiple scorebug display styles from different video games:

- **College Football 27 (CFB27)** — the default, actively calibrated and tested
- **CBS-style (v3)** — also calibrated and supported
- Additional presets are available in the Settings panel (Scorebug Preset selector)

If you're using a different game or display mode that shows the scoreboard differently, check the Scorebug Preset settings. The engine needs to know WHERE on the screen to look for each piece of text.

### Appendix D — Getting Help

- **In-app help**: Click the ❓ Help & Guide button in the navigation rack — includes tips, full walkthrough, and the event log
- **Discord**: Join the Bandroom community Discord server from the Discord Chat panel in the app
- **GitHub**: Issues and release notes at **https://github.com/kingsupreme89/Bandroom-v1**
- **YouTube tutorial**: A full video walkthrough of the event card and clipper system is available on the Bandroom YouTube channel

### Appendix E — Glossary

| Term | What it means |
|---|---|
| **Clipper Island** | The slide-up panel where you assign songs to events. Contains the song library, trimmer, and Sound Booth access |
| **Event Card** | One of the 46 game situations you can assign a song to. Shown as a card in the Situations panel |
| **EventKey** | The internal name for a game situation, like `"Offense: Touchdown Scored"` |
| **Evaluator** | One of the 16 specialist pieces of code that watches for a specific type of game event |
| **GAMETIME** | The button you press to lock in a matchup and start or reset the game-watching engine |
| **OCR** | Optical Character Recognition — the technology Bandroom uses to read text off your game screen |
| **Scorebug** | The on-screen scoreboard graphic that shows the score, down, distance, quarter, clock, and timeouts |
| **Scorebug Preset** | A saved set of screen coordinates that tells Bandroom where to look for each piece of scorebug text. Different games and display modes may need different presets |
| **Set Matchup** | The action of picking which two teams are playing (Home and Away) so the engine knows whose songs to fire |
| **Sound Bank** | A team's private collection of songs and backgrounds |
| **The Bandroom** | The community marketplace where users share songs and backgrounds |
| **Watching** | The toggle that turns the game-watching engine on or off. When Watching is active, Bandroom is scanning your screen |

---

*This handbook covers Bandroom v1.1 (Early Access) as of August 2026. Features, locations, and behavior may change in future updates. Check the in-app changelog or the Bandroom Discord for the latest information.*