# The Bandroom Manual
## Everything Your Stadium Can Do

*The complete guide to Bandroom — written for users, streamers, and the most dedicated fans. Version 1.0.0. If something's not clear, that's on us — not you.*

---

# PART 1: WELCOME

---

## 1. What Bandroom Is

Bandroom is a free Windows desktop application that watches your College Football 27 game on screen and automatically plays stadium sounds — fight songs, crowd reactions, PA announcements, band music — synchronized to live gameplay events.

**In plain English:** You play CFB 27 on your PC. Bandroom reads the scoreboard on your screen (the same way a human reads text). When it sees "TOUCHDOWN," it plays your team's actual fight song. When it sees "3rd & 8," it plays a defensive hype chant. No buttons to press. No manual triggers. It just works.

Bandroom is not a mod. It's not a sound pack. It's not something that replaces or modifies any game files. Bandroom is a separate application that runs alongside your game. It looks at your screen and plays audio through your speakers. It doesn't touch the game at all.

Think of it like this: you're watching a football broadcast on TV. The broadcast has a producer who plays music, crowd noise, and sound effects at the right moments. Bandroom is that producer — but for your video game.

---

## 2. What Bandroom Is NOT

- **Not a mod:** Bandroom doesn't modify College Football 27 in any way. It doesn't change game files. It doesn't inject code. It doesn't interact with the game's memory. It just looks at the pixels on your screen.

- **Not a sound pack:** Bandroom doesn't come with pre-loaded audio. It's a platform — you choose what sounds to use, whether from your own collection or from the community marketplace.

- **Not a cheat:** Bandroom doesn't give you any gameplay advantage. It doesn't read the game's internal data. It can't see plays, can't predict outcomes, can't affect the game in any way. It only reads what's already visible on your screen — the scorebug.

- **Not a subscription service:** Bandroom is free. No monthly payments. No locked features behind a paywall. Everything in this manual describes features that are available to every user at no cost.

---

## 3. License & Legal

Bandroom is distributed under the following license:

```
Copyright (c) 2025 Bandroom. All rights reserved.

This software and associated documentation files (the "Software") are the exclusive
intellectual property of the copyright holder. Unauthorized use, reproduction,
modification, distribution, or sale of the Software, in whole or in part, is
strictly prohibited.

NO PERMISSION IS GRANTED to:
- Use the Software for any purpose without prior written consent
- Copy, modify, merge, publish, or distribute the Software
- Sublicense, sell, or commercially exploit the Software
- Use the Software for training machine learning models or AI systems

To request a license or written permission, contact the copyright holder at the
email address listed in the public repository owner's profile.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

**What this means for you:**
- You can download and use Bandroom for free on your personal computer.
- You cannot sell Bandroom or include it in a paid product.
- You cannot modify Bandroom's code and redistribute your modified version.
- You cannot use Bandroom to train AI or machine learning models.
- Bandroom comes with no warranty — if something breaks, we're not legally liable (but we'll try our best to help you anyway).
- You own your audio files. You own your team configurations. Bandroom doesn't claim any rights to content you create or upload.

The source code is publicly visible on GitHub so the community can see how it works, report bugs, and suggest improvements. Public visibility is not a license to copy or redistribute.

---

## 4. A Note from the Developer (One Week In)

Bandroom was built fast. Like, really fast. About a week from idea to the version you're reading about right now. I'm editing it daily — sometimes multiple times a day — as bugs are found, features are tuned, and the community tells me what they need.

Here's what you should know going in:

- **The system WORKS.** The OCR detects game events. The audio engine fires. The streaming connects. The marketplace serves songs. The core functionality is solid.

- **There will be bugs.** A button might not be perfectly aligned. A feature might behave unexpectedly. A rare edge case might not be handled yet. That's the reality of software built in a week.

- **Every day is a step toward perfection.** I ship updates constantly. If something's broken today, it might be fixed tomorrow. Check for updates when you launch Bandroom — it auto-updates through Squirrel.

- **The community makes it better.** Report bugs on GitHub. Suggest features in Discord. Upload songs to the marketplace so other fans can use them. Share your profiles. Bandroom gets better because of the people using it.

Think of this as a living project — not a finished product. It's a stadium audio engine that's being tuned in public, with your help. I built the engine. The community fuels it.

Thank you for being here this early. Bare with me. Report what's broken. Celebrate what works. Let's make College Football sound like Saturday.

---

# PART 2: INSTALLATION & SETUP

---

## 5. System Requirements

Bandroom is a Windows application. Here's what you need:

| Requirement | Minimum | Recommended |
|---|---|---|
| **Operating System** | Windows 10 (64-bit) | Windows 11 (64-bit) |
| **.NET Runtime** | .NET 10 (included in installer) | .NET 10 |
| **CPU** | Any dual-core processor | Quad-core or better |
| **RAM** | 4 GB | 8 GB or more |
| **Disk Space** | ~50 MB for the app + space for your songs | ~50 MB + however many songs you download |
| **Display** | 1920x1080 | 1920x1080 or higher (for accurate OCR) |
| **Internet** | Required for marketplace, cloud sync, and streaming features | Broadband connection |
| **Audio** | Any Windows-compatible speakers or headphones | Stereo speakers or headphones |

**Mac compatibility:** A Mac version of Bandroom is planned but not yet available. Currently, Bandroom is Windows-only.

**College Football 27:** Bandroom is designed to work with CFB 27 running on the same PC. If you play on console, you'll need to capture or mirror your console display to your PC for Bandroom to read the scorebug.

---

## 6. Downloading Bandroom

Bandroom is hosted on GitHub at: `github.com/kingsupreme89/Bandroom-v1`

**Step by step:**

1. Open your web browser.
2. Go to `github.com/kingsupreme89/Bandroom-v1`.
3. On the right side of the page, find the "Releases" section. Click it.
4. You'll see a list of releases. The top one is the latest version.
5. Download the file called `Setup.exe`.

That's it. One file. You don't need to download anything else — the installer handles everything.

**What about Windows SmartScreen?**

When you run `Setup.exe`, Windows might show a blue warning box that says "Windows protected your PC." This appears for any application that isn't signed with an expensive code-signing certificate (which costs hundreds of dollars per year). Bandroom is safe. Click "More info," then click "Run anyway."

---

## 7. The Squirrel Installer

Bandroom uses Squirrel for installation and updates. Squirrel is a fast, lightweight installer for Windows applications. Here's what happens when you run `Setup.exe`:

1. **Installation takes about 2 seconds.** You might not even see a window. Squirrel works silently in the background.
2. **Bandroom launches automatically** when installation is complete.
3. **A shortcut is created** on your desktop and in your Start Menu.
4. **Auto-updates are enabled.** Every time you launch Bandroom, it checks for new versions and applies them automatically (with deltas — only downloading what changed, not the whole app).

If Bandroom doesn't launch automatically after installation, double-click the Bandroom icon on your desktop.

---

## 8. Where Files Land

Bandroom installs to a hidden system folder. Here's exactly where everything lives and what each piece is:

**Main location:** `C:\Users\[Your Name]\AppData\Local\Bandroom\`

To see this folder in File Explorer, you need to show hidden items: click the "View" tab at the top of File Explorer, then check "Hidden items."

| Folder/File | What It Is | Should You Touch It? |
|---|---|---|
| `Bandroom.exe` | The main program. Double-click to run. | No |
| `app-[version]\` | The actual application files. Squirrel manages this for auto-updates. | No |
| `Profiles\` | Contains one JSON file per team with your song assignments and settings. `LSU.json`, `Alabama.json`, etc. | Only if you want to manually edit or share profiles (advanced) |
| `Songs\` | All your imported audio files. Songs you've downloaded from the marketplace or added yourself. | Only to add/remove audio files manually |
| `crash.log` | If something goes wrong, error details are written here. Useful for debugging. | No, but you can look at it if something broke |
| `admin_token.local.txt` | A tiny authentication file. Ignore it. | No |
| `TeamLogos\` | Team logo images used in the UI. | No |
| `TeamBackgrounds\` | Stadium and team backdrop images. | No |

**On your Desktop:** A shortcut to Bandroom.

**In your Start Menu:** Bandroom appears in the list of installed applications.

---

## 9. First Launch — What to Expect

When you launch Bandroom for the first time:

1. **Single-instance check.** Bandroom checks if another copy is already running. If it is, you'll see a dialog asking if you want to open another copy anyway. You almost always want to click "No" — running two copies at once causes duplicate triggers and conflicts.

2. **Audio warmup.** Bandroom initializes its audio engine in the background. This prevents a delay the first time a sound plays. You won't see anything during this — it happens automatically.

3. **WebView2 UI loads.** The main Bandroom window appears. The interface is built with web technology (HTML/CSS/JavaScript) running inside a native Windows app through WebView2 (the same engine as Microsoft Edge). If you don't have WebView2 installed, Windows will prompt you to install it — it's a one-time, automatic process.

4. **Google OAuth login.** Bandroom uses Google Sign-In for user identity. You'll see a "Sign in with Google" button. This is optional for basic use but required for:
   - Cloud sync (your settings follow you across devices)
   - Marketplace uploads and downloads
   - Streamer features (Band Director)
   - Real-time data sync

   **Bandroom never sees your Google password.** OAuth means Google verifies who you are and gives Bandroom a temporary token. That token expires and is useless if stolen.

5. **The main window.** You'll see the Bandroom interface — header bar at top, trigger grid in the center, settings panels on the sides. The app starts in "General" mode (no team selected yet).

---

## 10. Auto-Updates

Bandroom checks for updates every time you launch it. When a new version is available:

1. Squirrel downloads the update in the background. It only downloads the files that changed (delta updates), so it's fast even on slow connections.
2. The update is applied silently.
3. Next time you launch Bandroom, you're on the latest version.

You don't need to manually download new versions from GitHub unless you're doing a fresh install on a new computer. The app keeps itself up to date.

---

# PART 3: THE MAIN INTERFACE — A TOUR

---

## 11. The Header Bar

The header bar runs across the top of the Bandroom window. Here's what each element does, from left to right:

**Left section:**
- **Brand mark ("B") and brand name ("Bandroom"):** Just the app logo and name.
- **Version number:** Shows the current version (e.g., "v1.0.0").

**Center section:**
- **Current team name:** Shows which team you're currently editing. Starts as "General" until you pick a team.
- **LOCK IN? button:** Opens the matchup panel where you set Home and Away teams for your current game.
- **Unlock button (padlock icon):** Appears after you've locked in. Click to unlock the matchup and pick new teams.

**Right section:**
- **Watch status pill:** Shows whether Bandroom is currently watching your game. Green dot = watching. Red dot = not watching. This is status-only — Bandroom starts watching automatically when your game is on screen.
- **Teams pill:** Opens the team picker grid to select which team you're editing.
- **Save pill:** Saves the current team's profile.
- **Sharing Guide pill ("?"):** Opens the Help & Guide overlay, scrolled to the profile sharing section.
- **Shortcuts pill:** Opens a keyboard shortcuts reference.
- **Help & Guide pill ("?"):** Opens the full help overlay.
- **Stop Watching button:** Appears after matchups locked in. Click to end the game and unlock the matchup.

---

## 12. The Marketplace Tabs

Below the header bar, a row of pill-shaped buttons gives you access to the main sections of Bandroom:

- **The Bandroom (rainbow pill):** The community marketplace. Browse songs uploaded by other users, organized by team, trigger type, and category.

- **Sound Bank (marketplace pill):** Your ACTIVE team's song collection. Shows all songs you've downloaded or imported for the team you're currently editing.

- **My Downloads (marketplace pill):** Everything you've downloaded from the marketplace, across all teams. Your personal library.

- **Discord (marketplace pill):** Live Discord chat feed. Connect with the Bandroom community directly from the app.

- **Auto-Assign (auto-assign pill):** One-click button that automatically matches your downloaded songs to the correct game triggers based on their metadata.

---

## 13. The Trigger Grid

The main area of the Bandroom window is the trigger grid. This is where you assign specific songs to specific game events.

The grid is organized by category:

| Category | Events |
|---|---|
| **Offense** | Touchdown, PAT/Extra Point, Field Goal, First Down |
| **Defense** | Defensive Stop on 3rd Down, Defensive Stop on 4th Down, Turnover, Fumble, Interception |
| **Special Teams** | Kickoff, Punt |
| **Situational** | Third Down, Fourth Down, Safety, Penalty, Timeout, Quarter Change, Two-Minute Warning, End of Game |

Each row in the grid represents one game event. Each row shows:
- The event name (e.g., "Offense: Touchdown Scored")
- The currently assigned song (or "None" if unassigned)
- The volume level
- The reverb preset
- Controls to assign, preview, or clear the song

**Home vs. Away:** When you've locked in a matchup, the grid splits into Home and Away sections. Each side has its own song assignments. Your home team's touchdown plays YOUR touchdown song. The away team's touchdown plays THEIR touchdown song. They're completely independent.

---

## 14. Team Selector

Clicking the "Teams" pill or the team badge in the header opens the team picker — a grid of all 134 NCAA Division I football teams.

Click any team to make it your active team. The UI updates to show that team's colors, logo, and background image. All your editing — song assignments, volume settings, reverb presets — now applies to this team.

Switching teams doesn't lose any work. Every team's configuration is saved separately.

---

## 15. Matchup Panel (LOCK IN?)

Clicking "LOCK IN?" opens the matchup panel. Here you select:

- **Home Team:** Pick from all 134 teams.
- **Away Team:** Pick from all 134 teams.
- **Confirm Matchup:** Locks in your selection.

After confirming:
- The header updates to show "Home Team vs. Away Team."
- The trigger grid splits into Home and Away sections.
- Bandroom starts watching for game events (if a game is on screen).
- The "Stop Watching" button appears.

To change the matchup, click "Stop Watching" (or the unlock padlock icon), then "LOCK IN?" again.

**Important:** You should lock in your matchup BEFORE starting a game. Bandroom needs to know who's home and who's away to trigger the right sounds for each team.

---

## 16. The Backdrop

When a matchup is locked in, Bandroom's background changes from the default backdrop to a split-screen VS display. The left half shows the away team's stadium image and logo. The right half shows the home team's stadium image and logo. They're separated by a center emblem with the Bandroom "B" mark.

This is purely visual — no controls live on the backdrop. It creates the atmosphere of a broadcast pregame show while you play.

---

# PART 4: THE GAME WATCHER (OCR)

---

## 17. How It Reads the Scorebug

Bandroom uses OCR (Optical Character Recognition) to read the scorebug on your screen. OCR is the same technology that lets you scan a document and turn it into editable text. Bandroom applies it to a very specific area: the scorebug at the bottom of the CFB 27 screen.

**Here's exactly what happens, 30 times per second:**

1. Bandroom captures a screenshot of the scorebug region.
2. The OCR engine reads the text in that image.
3. The text is parsed to extract: score (home and away), down, distance, quarter, time remaining, possession indicator.
4. These values are compared to the values from the PREVIOUS frame (1/30th of a second ago).
5. If something changed in a way that matches one of the 18 evaluator patterns, a trigger fires.

**Example:** Frame 100 says "LSU 14 - BAMA 10 | 2nd & 7 | Q2 4:32." Frame 101 says "LSU 21 - BAMA 10 | TOUCHDOWN." The score changed by 7 and the text changed to "TOUCHDOWN." The touchdown evaluator fires. The assigned fight song plays.

This all happens in under 50 milliseconds. You won't perceive any delay between the score changing on screen and the sound playing.

---

## 18. The 18 Evaluators — Complete Reference

Each evaluator is an independent detector watching for a specific game event. Here's every single one, what it watches for, and how it works:

### Offense Evaluators

**1. Touchdown**
- **What triggers it:** The score changes by 6 or 7 points (accounting for the PAT that follows immediately in most cases).
- **What it's for:** Fight songs, celebration music, crowd eruptions.
- **Default cooldown:** 15 seconds (prevents double-fire from replay overlays).
- **Priority:** Highest (ducks all other audio).

**2. PAT / Extra Point**
- **What triggers it:** The score changes by exactly 1 point. This typically fires about 5-10 seconds after the touchdown evaluator.
- **What it's for:** Short celebration stings, kick sound effects.
- **Default cooldown:** 10 seconds.

**3. Field Goal**
- **What triggers it:** The score changes by exactly 3 points.
- **What it's for:** Field goal celebration, kicker-specific sounds.
- **Default cooldown:** 20 seconds.

**4. First Down**
- **What triggers it:** The down resets to "1st" and the distance changes (indicating a new set of downs, not just a 1st & goal situation).
- **What it's for:** Short hype stings, "move the chains" sounds.
- **Default cooldown:** 45 seconds (first downs happen frequently — this prevents spam).

### Defense Evaluators

**5. Defensive Stop on 3rd Down**
- **What triggers it:** The down was "3rd," then possession changes without a score (the other team now has the ball on a different down).
- **What it's for:** Defensive chant, stop sounds, crowd roar.
- **Default cooldown:** 30 seconds.

**6. Defensive Stop on 4th Down**
- **What triggers it:** The down was "4th," then possession changes (turnover on downs).
- **What it's for:** Big defensive stand celebration.
- **Default cooldown:** 30 seconds.

**7. Turnover**
- **What triggers it:** The possession arrow flips (home had the ball, now away does) without a score or punt situation detected.
- **What it's for:** Turnover celebration, defensive takeaway sounds.
- **Default cooldown:** 15 seconds.
- **Priority:** High (ducks most other audio).

**8. Fumble**
- **What triggers it:** Detected as a specific type of turnover with characteristic scorebug behavior.
- **What it's for:** Fumble recovery sounds, defensive celebration.
- **Default cooldown:** 15 seconds.

**9. Interception**
- **What triggers it:** Detected as a turnover where possession flips during a passing situation.
- **What it's for:** Interception celebration, "picked off" sounds.
- **Default cooldown:** 15 seconds.

### Special Teams Evaluators

**10. Kickoff**
- **What triggers it:** A special scorebug layout appears — the scorebug may show different information, or the game clock behavior changes.
- **What it's for:** Kickoff-specific music, "here we go" stings.
- **Default cooldown:** 60 seconds (kickoffs only happen a few times per game).

**11. Punt**
- **What triggers it:** Possession changes during a 4th down situation without a score — indicating a punt rather than a turnover on downs.
- **What it's for:** Punt-related sounds, "boot it" effects.
- **Default cooldown:** 45 seconds.

### Situational Evaluators

**12. Third Down**
- **What triggers it:** The down text shows "3rd."
- **What it's for:** Defensive hype chants, "third down" stings, crowd noise escalation.
- **Default cooldown:** 30 seconds.

**13. Fourth Down**
- **What triggers it:** The down text shows "4th."
- **What it's for:** "Go for it" chants, tension-building sounds.
- **Default cooldown:** 30 seconds.

**14. Safety**
- **What triggers it:** The score changes by exactly 2 points.
- **What it's for:** Safety celebration, defensive dominance sounds.
- **Default cooldown:** 20 seconds.
- **Priority:** High.

**15. Penalty**
- **What triggers it:** Penalty-related text appears on the scorebug.
- **What it's for:** Penalty announcement sounds, booing, flag-related effects.
- **Default cooldown:** 30 seconds.

**16. Timeout**
- **What triggers it:** The game clock pauses without the quarter changing.
- **What it's for:** Timeout music, "commercial break" transitions.
- **Default cooldown:** 120 seconds (timeouts are rare).

**17. Quarter Change**
- **What triggers it:** The quarter number changes (1→2, 2→3, 3→4).
- **What it's for:** Quarter transition music, intermission sounds.
- **Default cooldown:** 120 seconds.

**18. End of Game**
- **What triggers it:** The game clock hits 0:00 in the 4th quarter or overtime.
- **What it's for:** Victory music, alma mater, post-game celebration.
- **Default cooldown:** None (only fires once).
- **Priority:** Highest.

---

## 19. Scorebug Presets & Tuning

CFB 27 may have different scorebug layouts for different game modes, broadcast packages, or display settings. Bandroom supports scorebug presets — pre-configured OCR regions and text parsing rules for different layouts.

If OCR isn't detecting events reliably, check the scorebug preset settings. You may need to select a different preset or adjust the capture region.

The OCR engine is designed to handle:
- Different scorebug positions (bottom of screen — most common)
- Different font sizes and styles
- Scorebug animations (fading in/out during replays)
- Overlay graphics that temporarily obscure the scorebug

If you're experiencing issues with OCR detection, the OCR debug log (accessible through settings) shows exactly what text Bandroom is reading from each frame. This is useful for diagnosing detection problems.

---

# PART 5: THE AUDIO ENGINE (SOUND BOOTH / DSP)

---

## 20. How Audio Travels Through Bandroom

When a game event triggers a sound, the audio doesn't just play. It travels through a processing chain — a series of effects that shape the sound before it reaches your speakers. Think of it like an assembly line for audio.

Here's the full chain:

```
Your Audio File → RAM Pre-Cache → LUFS Normalization → Parametric EQ → 
Compressor → Reverb → Stereo Pan → Brickwall Limiter → Your Speakers
```

Each step is optional. Each step is configurable. Each step can be bypassed with a toggle. But when they're all active, they work together to turn a raw audio file into professional-grade stadium sound.

---

## 21. RAM Pre-Caching

**What it does:** When you press LOCK IN? (or load a profile), Bandroom loads all your assigned audio files into your computer's fast memory (RAM). From that point on, no file ever needs to be read from disk during gameplay.

**Why it matters:** Reading from a hard drive takes time. Reading from RAM is instant. This eliminates the most common source of audio stutter and delay.

**Technical detail:** Each audio file is stored as a `byte[]` array in memory. When `Play()` is called, it reads from that memory array instead of opening the file from disk. The cache is cleared when you close Bandroom or reassign songs.

---

## 22. LUFS Normalization — Volume Matching

**What LUFS means:** LUFS stands for "Loudness Units relative to Full Scale." It measures how loud something FEELS to human ears — not just the electrical volume of the audio signal. It's the standard used by Netflix, Spotify, YouTube, and every major streaming platform.

**The problem:** When you collect fight songs from different sources — a live game recording (quiet, distant mic), an album rip (loud, professionally mastered), a YouTube download (compressed, somewhere in between) — they all play at wildly different volumes. You'd have to ride the volume knob constantly.

**How Bandroom fixes it:**

1. When you import a song, Bandroom analyzes the entire file using the EBU R128 standard (the European Broadcasting Union's loudness measurement standard — the same one Netflix uses).
2. It calculates three measurements:
   - **Integrated LUFS:** The overall perceived loudness of the entire track.
   - **Short-Term LUFS:** The loudness over 3-second windows (used to detect unusually loud or quiet sections).
   - **True Peak dBTP:** The absolute maximum level, including between-sample peaks that normal peak meters miss.
3. It calculates a gain value (volume adjustment) needed to reach the target loudness.
4. It applies that gain to a COPY of the file. **Your original file is never modified.**
5. The metadata (LUFS values, applied gain) is stored in the database so the analysis never needs to run again.

**Target loudness levels:**

| Content Type | Target LUFS | Why |
|---|---|---|
| Marching band songs | -14 LUFS | Streaming standard. Punchy and present without being fatiguing. |
| PA announcer clips | -18 LUFS | Speech needs more dynamic range. Quieter target means the speech is clear without being harsh. |
| Lead-in whistles / short stings | -12 LUFS | Short transients need to cut through the mix. Louder target because they're over in a split second. |

**True Peak ceiling:** All normalized audio has a hard ceiling of -1.0 dBFS (1 decibel below the absolute maximum). This prevents clipping even when multiple samples stack up between measurement points.

**The result:** Every single song plays at the same perceived volume. No more reaching for the volume knob. No more getting your ears blown out. No more straining to hear a quiet recording.

---

## 23. Parametric EQ — Marching Band Preset

**What EQ is:** EQ (equalization) adjusts how loud different frequency ranges are. It's like a stereo's bass and treble knobs, but much more precise. You can boost or cut specific frequencies by exact amounts.

**Why marching band recordings need special EQ:** Marching band recordings have characteristic problems:
- Tubas and bass drums overlap in the low frequencies, creating "mud" — a rumbling indistinct low end.
- Trumpets and snares fight in the midrange.
- Cymbals get lost in the upper frequencies.
- Stadium HVAC systems, wind, and crowd rumble add subsonic noise.

**Bandroom's marching band EQ preset:**

| Band Type | Frequency | Q (Width) | Gain | What It Does |
|---|---|---|---|---|
| High-Pass Filter | 80 Hz | 0.71 | — (cuts everything below) | Removes subsonic rumble — stadium HVAC, wind, traffic noise. You feel these frequencies but shouldn't hear them in a recording. |
| Low-Shelf | 200 Hz | 1.0 | -3 dB | Reduces muddy low-mids where tubas and bass drums overlap into indistinct rumble. Clean separation between instruments. |
| Peak (Bell) | 2,500 Hz | 1.4 | +4 dB | Boosts trumpet and mellophone overtones — the "crisp" of the brass section. Brings the melody FORWARD in the mix. |
| High-Shelf | 8,000 Hz | 0.71 | +2 dB | Adds "air" and sparkle to cymbal crashes, piccolo runs, and the general sense of open space. |

**Megaphone / Stadium PA preset:** A separate, special-effect EQ preset:
- Aggressive bandpass filter: cuts everything below 500 Hz and above 4,000 Hz.
- Makes any clip sound like it's coming through old concrete stadium speakers.
- Perfect for PA announcer clips to sound authentically lo-fi.

**Transient Shaper — Drum Enhancement:**
- Applied to percussion-heavy tracks (drumline cadences, snare features).
- **Attack:** +3 dB to +6 dB boost on the first 1-5 milliseconds of every drum hit. This is the "crack" — the sharp initial impact.
- **Sustain:** -2 dB to 0 dB reduction on the ringing tail of each hit. Tightens the sound without killing the body.
- Result: drum hits punch through the mix instead of blending into background rumble.

All EQ settings are adjustable. The presets are starting points — you can tweak any band to fit your specific recordings.

---

## 24. Stadium Reverb — Four Weather-Aware Presets

**What reverb is:** Reverb (short for "reverberation") is echo — the sound bouncing off walls, seats, the roof, and the field before it reaches your ears. It's what makes audio feel like it's in a physical space rather than a small, dead room.

**Bandroom's four reverb presets:**

| Preset | Decay Time | High-Frequency Dampening | What It Feels Like |
|---|---|---|---|
| **Stadium — Clear Night** | 2.8 seconds | 0.3 (bright) | Sharp, crisp outdoor echoes. Sound carries far on a clear, cool night. Classic Saturday night atmosphere. Early reflections are prominent — you hear the first bounce off the nearest seats clearly. |
| **Stadium — Rain** | 1.8 seconds | 0.7 (dark) | Muffled, close-sounding. Rain absorbs high frequencies — everything sounds wetter, closer, more intimate. That November game in the Pacific Northwest. The reverb tail is shorter and darker. |
| **Dome** | 3.2 seconds | 0.5 (neutral) | Long, booming indoor echo. Sound takes forever to die. Heavy late reflections — the distinctive "indoor football" sound. Syracuse Carrier Dome, New Orleans Superdome. |
| **Night Game — Prime Time** | 2.4 seconds | 0.4 (bright-ish) | Wide, cinematic stereo image. Slightly enhanced early reflections for drama. The "big game under the lights" feel. Bigger, wider, more present than Clear Night. |

**How it works technically:** Bandroom uses an algorithmic reverb (not convolution, which is more CPU-intensive). The reverb is applied as an `ISampleProvider` in the DSP chain. Parameters (decay time, HF dampening, early/late reflection mix, stereo width) are adjusted per preset.

**User control:** Each trigger can have its own reverb preset. Your touchdown song could use Night Game Prime Time while your kickoff song uses Clear Night. Or set everything to one preset for consistency.

---

## 25. Brickwall Limiter — Speaker Protection

**What a limiter does:** A limiter puts a HARD CEILING on volume. No matter what happens — no matter how many sounds play at once — the audio output NEVER exceeds the ceiling. Think of it as a safety net that catches audio peaks before they can distort or damage anything.

**Why Bandroom needs one:** In a football game, multiple events can happen simultaneously:
- A touchdown triggers the fight song.
- The crowd roars in response.
- The PA announcer fires.
- A whistle sound effect plays.

Each sound at normal volume is fine. All four at once, summed together, can exceed the maximum digital level and CLIP — causing harsh distortion that sounds terrible and can potentially damage speakers or headphones.

**Bandroom's limiter specifications:**

| Parameter | Value | Why |
|---|---|---|
| Ceiling | -0.3 dBFS | Broadcast-safe standard. 0.0 dBFS would allow inter-sample peaks (audio that clips between measurement points even though each individual sample is fine). The 0.3 dB margin prevents this. |
| Look-ahead | 5 milliseconds | The limiter "looks into the future" by buffering 5ms of audio. If it sees a peak approaching the ceiling, it starts turning down the volume BEFORE the peak arrives. This prevents the "pumping" sound that cheap limiters produce. |
| Release | 50 milliseconds | After a peak passes, the limiter releases its grip over 50ms. Fast enough to not affect the next sound. Slow enough to not create distortion from rapid volume changes. |

**What you'd notice without it:** During chaotic moments (game-winning touchdown in overtime), the audio distorts, crackles, or clips. It sounds like a blown speaker. It physically hurts to listen to. The limiter prevents all of this — clean, controlled audio no matter how much is happening.

---

## 26. Audio Ducking — Priority System

**What ducking is:** Ducking automatically turns down (or "ducks") less important sounds when a more important sound needs to play. It's what radio DJs do when they turn down the music to talk, then turn it back up.

**How Bandroom implements it:**

Events have priority levels. When a high-priority event fires:
- Lower-priority audio is ducked (volume reduced) instantly.
- The high-priority sound plays at full volume.
- After the high-priority sound finishes (or after a set time), the ducked audio smoothly returns to normal volume.

**Priority levels and ducking behavior:**

| Event Priority | Examples | Ducking Behavior |
|---|---|---|
| Highest | Touchdown, Safety, End of Game | All background/ambient audio ducks to 40% volume. Band music ducks by 3 dB. Attack: 20ms (instant). Release: 300ms (smooth return). |
| High | Turnover, Interception, Fumble | Background audio ducks to 50%. Attack: 20ms. Release: 300ms. |
| Medium | Field Goal, Defensive Stop, Kickoff | Band music ducks by 2 dB during PA announcer clips. |
| Low | First Down, Third Down, Timeout | Minimal ducking — these events are common and shouldn't disrupt the overall mix. |

**PA Announcer Ducking:** When a PA announcer clip plays, the band music automatically ducks by 3 dB so the announcement is clearly heard. The music returns smoothly when the clip ends.

The ducking system is fully automatic. You don't need to configure priorities — Bandroom knows which events should take precedence.

---

## 27. Stereo Width Enhancer

**What it does:** Takes narrow or mono recordings and spreads them into immersive stereo — making the band sound like it's spread across a field in front of you instead of coming from a single point.

**Why it matters:** Many marching band recordings are effectively mono (same sound in both speakers) or very narrow stereo. This makes them sound "flat" compared to modern music.

**How it works:** Mid/Side processing. The audio is split into:
- **Mid:** What's identical in both speakers (the center image).
- **Side:** What's different between speakers (the stereo information).

The Side channel is boosted by +3 dB to +6 dB, making the stereo differences more pronounced. A dry/wet mix control lets you dial in how much widening to apply — from subtle enhancement to dramatic spread.

**Mono compatibility:** The result is mono-compatible. If someone listens on a phone speaker, a cheap Bluetooth speaker, or any mono device, the audio still sounds correct — no phase cancellation, no missing instruments. This is critical for stream viewers who may be listening on all kinds of devices.

---

## 28. WASAPI Exclusive Mode

**What it is:** An optional audio output mode that bypasses the Windows audio mixer entirely.

**The difference:**

| Mode | Latency | Compatibility | What Happens |
|---|---|---|---|
| Shared Mode (default) | ~100ms | Any app can play sound simultaneously | Windows mixes all audio from all apps together. Bandroom's audio goes through the Windows mixer, adding ~100ms of delay. Discord, Spotify, and your game can all play sound at the same time. |
| Exclusive Mode | <15ms | Bandroom takes over the audio device | Bandroom talks directly to your sound hardware. No other app can play sound through that device. Lowest possible latency. Professional-grade audio path. |

**When to use Exclusive Mode:**
- You want the absolute lowest delay between game events and sound.
- You're not using other audio apps while playing (or you're okay with them being silent).
- You're streaming and need perfect audio-to-video sync.

**When to use Shared Mode:**
- You need Discord, Spotify, or other audio apps running alongside Bandroom.
- You're not sensitive to the ~100ms delay (most people aren't).
- You want maximum compatibility with no configuration.

**How to switch:** The mode selector is in the audio settings panel. Change it any time — no restart required. The audio device hot-swaps between modes.

---

## 29. Sub-Bass Stadium Thump Enhancer

**What it does:** Synthesizes clean, deep bass (40-60 Hz) and layers it underneath big hits, tackles, and bass drum impacts. You FEEL the impact in your chest instead of just hearing it.

**Why it matters:** In a real stadium, you feel big hits through the structure. The stands shake. Bass drums resonate in your body. Consumer speakers — even good ones — can't reproduce this naturally without help. The sub-bass enhancer creates what's missing.

**How it works:** A sub-harmonic synthesizer:
1. Detects strong low-frequency transients (drum hits, impact sounds).
2. Creates a clean sine wave one octave below the original frequency.
3. Shapes it with a fast attack/slow release envelope so it follows the impact.
4. Mixes it underneath the original sound at a user-adjustable level.

**Uses wavefolding + low-pass filtering** rather than simple pitch shifting. This produces cleaner results with fewer artifacts. Simple pitch shifting creates warbly, unnatural bass. Wavefolding produces tight, musical sub-bass.

**Adjustable intensity:** Off / Subtle / Stadium / Earthquake.

**Applied automatically to:** Big tackle events, field goal blocks, heavy bass drum hits in fight songs.

---

## 30. Crowd Dynamics — Volume Reactive to Game State

**What it does:** The crowd noise volume automatically scales based on what's happening in the game. A real crowd doesn't roar at the same volume all game — and Bandroom's crowd shouldn't either.

**How crowd volume is calculated:**

| Game State Factor | Effect on Crowd Volume |
|---|---|
| **Score Differential** | Close game (<7 points): LOUD. One-score game (>7 but <15 points): moderate. Blowout (>21 points): quieter — fans have checked out. |
| **Quarter** | 1st Quarter: baseline volume. 2nd Quarter: slightly elevated. 3rd Quarter: elevated. 4th Quarter: MAXIMUM. |
| **Time Remaining** | Over 5 minutes: normal. Under 5 minutes: elevated. Under 2 minutes: maximum intensity. Final 30 seconds: DEAFENING. |
| **Down** | 1st & 2nd Down: baseline. 3rd Down: louder — the crowd knows it's a key play. 4th Down: peak tension — everything's on the line. |

The crowd channel is a separate mixing bus. Its gain (volume) is driven continuously by the game state variables read from the OCR engine. The transitions are smooth — you won't hear sudden volume jumps.

---

## 31. Doppler Panning

**What it does:** Sound subtly shifts left or right in the stereo field based on which direction the offense is driving on the field.

**Why it matters:** In a real stadium, the band and crowd sound like they're coming from a specific direction. If the student section is in the left end zone, the band sounds like it's coming from the left. If the team is driving toward the left end zone, that side of the stadium is more engaged.

**How it works:** Field position (yard line) from the OCR engine determines stereo pan:
- Team on the left hash marks → sound pans slightly left.
- Team on the right hash marks → sound pans slightly right.
- Team at midfield → sound is centered.
- Touchdown in left corner → band music hits from that side.

**Very subtle:** Maximum ±15% pan. The goal is subconscious immersion — you feel it more than you consciously hear it. It should never sound like a gimmick.

---

## 32. Tunnel-to-Stadium Pregame Transition

**What it does:** During pregame run-out (when the PregameHelper event fires), the audio starts with a "Tunnel" filter — muffled, echoing, like you're in a concrete tunnel — and crossfades over 3 seconds to wide-open stadium as the team "enters the field."

**Why it matters:** This is the single most cinematic moment in sports. The team running out of the tunnel. The roar building. The burst onto the field. Bandroom sonifies this transition in a way no sports game ever has.

**The Tunnel filter:**
- Heavy reverb: 3.5 second decay (long echoing tunnel).
- Bandpass EQ: 300 Hz to 3,000 Hz (the muffled sound of being in an enclosed concrete space).
- Slight distortion (simulating PA speakers overdriving in the tunnel).

**The transition:** Over exactly 3 seconds, the tunnel filter crossfades to the selected stadium reverb preset (Clear Night, Rain, Dome, or Prime Time). The moment the filter clears is the moment the team hits the field.

---

## 33. Rivalry Tension Drone

**What it does:** In close 4th quarter games, a low, rumbling sub-bass drone (30-50 Hz) slowly fades in underneath all other audio.

**Why it matters:** The best sports moments have tension you can feel. This drone is purely atmospheric — you shouldn't consciously notice it. But your body feels it. It's the same technique horror movies use: sub-bass rumble before the jump scare. It makes close games feel CLOSE.

**When it activates:**
- Score differential under 8 points.
- Time remaining under 4:00.
- 4th quarter or overtime.
- Volume increases as the clock runs down.

**When it cuts:** Instantly when the game ends, the score differential exceeds 8 points, or the game becomes a blowout.

---

## 34. Halftime Show Mode

**What it does:** A toggle that plays through ALL your assigned team songs continuously, with crossfades between tracks. No game events needed.

**When to use it:**
- Streaming breaks between games.
- Halftime or intermission during your stream.
- Tailgating — just put on the playlist and let it run.
- Showing off your song collection to friends.
- Background music while you're setting up.

**How it works:**
- Toggle on → continuous sequential playback of all assigned songs for the active team.
- 3-second crossfade between tracks (one fades out while the next fades in).
- No event triggering — OCR continues watching but its triggers are suppressed during Halftime Mode.
- Optional: display current track name on screen.

---

## 35. Smart Cooldown Gate

**What it does:** Prevents the same event from triggering the same sound over and over in rapid succession — the "wall of sound" spam problem.

**Why it matters:** OCR can be twitchy. During a replay, the scorebug might flash, animate, or display differently. Without cooldowns, the touchdown evaluator might fire three times during one replay — and you'd hear the fight song start three times overlapping. It sounds terrible.

**Cooldown timers per event:**

| Event | Cooldown | Reasoning |
|---|---|---|
| Touchdown | 15 seconds | Events don't repeat often, but replays can trigger double-fires. Short cooldown catches these. |
| PAT | 10 seconds | Follows touchdown closely. Short cooldown prevents double-fire. |
| Field Goal | 20 seconds | Similar to touchdown — replay protection. |
| First Down | 45 seconds | Happens frequently. Longer cooldown prevents spam. |
| Third Down | 30 seconds | Happens frequently. Prevent repetitive trigger every time a team gets to 3rd down. |
| Fourth Down | 30 seconds | Less frequent than 3rd down but similar logic. |
| Defensive Stop | 30 seconds | Prevent double-fire from scorebug transitions. |
| Turnover | 15 seconds | Prevent replay double-fire. |
| Kickoff | 60 seconds | Only happens a few times per game. Long cooldown is fine. |
| Punt | 45 seconds | Similar to kickoff logic. |
| Timeout | 120 seconds | Very rare. Long cooldown is more than sufficient. |
| Quarter Change | 120 seconds | Only happens 3-4 times per game. |

**Separate per side:** Home and Away cooldowns are independent. If the home team scores a touchdown, the away team can still trigger first downs and defensive stops immediately — their cooldowns are separate.

---

## 36. Live Audio Health Monitor

**What it does:** Displays real-time metrics about audio performance, visible in the Bandroom UI.

**What's shown:**

| Metric | What It Means | Healthy Range |
|---|---|---|
| Output Latency | Time between audio being sent to the sound card and it coming out of your speakers | Under 15ms (Exclusive) / Under 100ms (Shared) |
| Buffer Underruns | Number of times the audio buffer ran dry (causes stuttering) | 0 |
| Audio Thread CPU | How much CPU the audio processing is using | Under 10% |
| Peak Level (dBFS) | The loudest recent audio output | Under -0.3 dBFS (the limiter ceiling) |
| Active Clip Count | How many sounds are playing simultaneously | Varies — normal is 1-4 |

**Color coding:** 🟢 Green = healthy. 🟡 Yellow = warning (approaching limits). 🔴 Red = problem detected (dropouts, clipping, excessive latency).

---

## 37. Automatic Crash Recovery

**What it does:** If your audio device disconnects (USB headphones unplugged, Bluetooth drops, driver crashes), Bandroom detects it, finds a new audio device, and resumes playback — all automatically.

**The process:**
1. Watchdog thread monitors the audio output device continuously.
2. Disconnect detected within 500ms.
3. All available audio devices are re-enumerated.
4. Bandroom re-initializes on the new default device.
5. Playback resumes from where it stopped.
6. A brief toast notification appears: "Audio device changed — switched to [device name]."

**No user intervention needed.** No restart. No missed events. If you're mid-game when your headphones die, Bandroom switches to your speakers and keeps going.

---

## 38. Live Audio Device Switcher

**What it does:** A dropdown menu in the Settings panel listing every available audio output device. Switch between speakers, headphones, and any other output without restarting Bandroom.

**How it works:** Hot-swap — the current device is stopped, and a new one is initialized. Target gap: under 200ms of silence during the switch. No restart. No lost game state.

**When you'd use it:** Switching from speakers to headphones mid-game. Bluetooth headphones finally connect after Bandroom launched. You want to test how Bandroom sounds on different output devices.

---

## 39. Mixed Sample Rate Handling

**What it does:** Seamlessly handles mixing audio files recorded at different sample rates (44.1 kHz CD quality, 48 kHz video standard, 96 kHz high-res) in the same session.

**Why it matters:** Audio files from the internet come in all sample rates. You shouldn't have to convert everything to the same rate before using it. Drop in any MP3, WAV, FLAC, AIFF, or OGG file — Bandroom handles the conversion automatically.

**How it works:** NAudio's resampling provider (`WdlResamplingSampleProvider`) automatically converts all source files to the output device's native sample rate. Zero user intervention.

---

## 40. Low-Performance Mode

**What it does:** Detects or manually toggles a "Low CPU" mode that disables visual effects while keeping audio quality intact.

**When to use it:** On a laptop or older PC. When running Bandroom + CFB 27 + OBS streaming simultaneously and the CPU is maxed out.

**What gets scaled back:**
- FFT spectrum analyzer visualizer (the bouncing EQ bars) — disabled.
- UI animation frame rate — reduced.
- Audio thread priority — locked to HIGH (audio never gets starved for CPU time).

**What NEVER gets scaled back:** Audio quality. EQ, reverb, compression, limiting — all DSP processing continues at full quality regardless of performance mode. Only visual effects are reduced.

---

## 41. Detailed Audio Event Log

**What it does:** Records every single audio event to a searchable, structured log.

**What's logged per event:**
- Timestamp (game clock time AND real-world time).
- Event key (e.g., "Offense: Touchdown Scored").
- Side (Home or Away).
- File played (filename and path).
- Input loudness (LUFS before normalization).
- Applied gain (dB of adjustment applied).
- Output peak (dBFS — the loudest moment during playback).
- Play duration (seconds).

**Format:** The log is structured (CSV format initially, migrating to database storage). Rotating log capped at the last 2,000 events — when it reaches the cap, oldest entries are removed.

**Uses:** Troubleshooting ("Why didn't my kickoff song play?"), analytics ("How many touchdown songs played last game?"), bragging rights ("I triggered the fight song 23 times in one season.").

---

## 42. One-Click Diagnostic Zip

**What it does:** A "Help Me" button that packages all diagnostic information into a single zip file for support requests.

**What's included in the zip:**
- Audio event log.
- OCR debug log.
- Crash logs.
- System specifications (CPU, RAM, audio devices, OS version).
- Current Bandroom version.
- Active profile information.

**Where it saves:** To your desktop as `bandroom-diagnostics-[date].zip`.

**When to use it:** When something's wrong and you're asking for help. Instead of "well, there's a log in this folder and another log in that folder...," you send ONE file. Support can see everything at once.

---

# PART 6: THE BAND DIRECTOR (STREAMER DASHBOARD)

---

## 43. What Band Director Is

The Band Director is a dedicated tab in Bandroom that turns the app into a complete live streaming audio control room. It connects to Twitch and YouTube, allowing streamers and their viewers to control game audio in real time through chat commands, Channel Points, Bits, Super Chats, polls, and a remote Guest DJ system.

**For the streamer:** A dashboard showing chat commands as they happen, a queue of viewer-requested songs, live polls, quick-trigger buttons, and an overlay preview.

**For viewers:** The ability to participate in the stadium audio experience — trigger sounds, request songs, vote in polls, and even DJ remotely.

---

## 44. Connecting Twitch

**One-click OAuth — no passwords:**

1. Click "Connect Twitch" in the Band Director tab.
2. Your browser opens to Twitch's authorization page.
3. You log in to Twitch (if not already logged in).
4. Click "Authorize" to grant Bandroom permission.
5. The browser redirects to a local Bandroom URL (`http://localhost:8765`).
6. Bandroom captures the authorization code.
7. Bandroom exchanges the code for an access token and refresh token.
8. The tokens are encrypted and stored.
9. You're connected. That's it.

**What Bandroom requests permission to do:**
- Read chat messages (so it can see commands).
- Send chat messages (so it can respond to commands and post poll results).
- Read Channel Point redemptions (so viewers can spend points).
- Read Hype Train events.
- Read subscription events.
- Read Bits/Cheer events.

**Bandroom does NOT:** Post without being triggered, read your private messages, modify your stream settings, or do anything you haven't explicitly allowed.

**Token refresh:** Twitch tokens expire. Bandroom automatically refreshes them using the refresh token. You don't need to re-authorize unless you revoke access from Twitch's settings.

---

## 45. Connecting YouTube

**Same OAuth flow — one click:**

1. Click "Connect YouTube" in the Band Director tab.
2. Browser opens to Google's authorization page.
3. Log in and click "Allow."
4. Redirect back to localhost.
5. Tokens captured, encrypted, stored.
6. Connected.

**What Bandroom requests:**
- Read live chat messages.
- Send live chat messages (for poll results and responses).

**YouTube limitations compared to Twitch:**
- Chat messages are polled (checked every ~1 second) rather than streamed in real time.
- YouTube has no equivalent of Channel Points, Bits, Raids, or Hype Trains.
- Super Chat and Super Stickers are YouTube's monetization equivalents.
- Slow mode is enforced more strictly — Band Director shows a "Chat Delay" indicator.

---

## 46. Multi-Platform Mode

When both Twitch and YouTube are connected, you can enable Multi-Platform Mode. This merges everything:

- Chat commands from BOTH platforms trigger sounds.
- Viewer song requests from BOTH platforms appear in the SAME queue, labeled with their source (🟣 Twitch or 🔴 YouTube).
- Polls aggregate votes from both platforms.
- The overlay displays combined stats: "Requests: 3 (2 Twitch, 1 YouTube)."

One merged queue. One unified poll system. One overlay. Two platforms. No conflicts.

---

## 47. All Twitch Features (Complete Reference)

### Chat Commands
Viewers type commands prefixed with `!`. Bandroom reads them and triggers the corresponding sound. Available commands (configurable — you choose which are active):

| Command | Triggers | Notes |
|---|---|---|
| `!td` | Touchdown fight song | |
| `!kickoff` | Kickoff music | |
| `!defense` | Defensive stop chant | |
| `!hype` | Hype sting | |
| `!boo` | Boo / jeer sound | |
| `!fight` | Fight song (any trigger) | |
| `!timeout` | Timeout music | |
| `!punt` | Punt sound | |
| `!fg` | Field goal celebration | |
| `!safety` | Safety celebration | |
| `!ot` | Overtime hype | |

- **Case insensitive:** `!TD`, `!td`, `!Td` all work.
- **Rate limited:** Maximum one sound trigger per 2 seconds from chat to prevent spam.
- **Configurable:** You can enable/disable any command. Create custom command mappings.
- **Cooldowns apply:** Existing event cooldowns (e.g., 15s for touchdown) prevent spam regardless of how many viewers type `!td`.

### Channel Points — Trigger Song
Custom reward: "Play Fight Song." You set the point cost. A viewer redeems the reward → the assigned trigger fires. Simple, direct, effective.

### Channel Points — Pick Song
Custom reward: "Choose Next Song." The viewer who redeems gets a list of available songs (from your team's sound bank). They pick one. It goes into the queue. The overlay shows "Requested by: [viewer name]."

### Channel Points — Change Reverb
Custom rewards: "Set Stadium Reverb" / "Set Dome Reverb" / "Set Night Game Reverb." Viewer spends points to change the DSP reverb preset live. Changes take effect immediately — the next sound that plays uses the new preset.

### Channel Points — Duck Audio
Custom reward: "Mute Band for 30 Seconds." Viewer spends points to trigger audio ducking for 30 seconds — all music is ducked to 40% volume. Perfect for when the streamer needs to talk.

### Channel Points — Queue Jump
Custom reward: "Skip to Front of Queue." Viewer's song request jumps to position #1 in the queue, ahead of everyone else waiting.

### Bits — Sound Effects
- **100 bits:** Generic hype/horn sound effect.
- **500 bits:** Custom celebratory sound (team-specific if available).
- **1,000 bits:** Viewer picks ANY available song to play immediately — bypasses the queue entirely.
- **5,000 bits:** Stadium Takeover — viewer controls ALL audio for 60 seconds. They can trigger any sound, change reverb, adjust volume. Complete control.

### Bits — Queue Priority
Every bit cheered on a queued song request adds +1 priority point. Higher priority songs play sooner. Viewers can invest bits to get their song heard faster.

### New Subscriber
New subscriber (any tier) → Team fight song plays automatically. Subscriber's name appears on the overlay with a welcome message.

### Resubscriber
Resub (continuing subscription) → Celebration music plays. Number of months shown on overlay. Higher months = bigger celebration.

### Gifted Subscriptions
- **5 gifted subs:** Stadium crowd roar effect.
- **10+ gifted subs:** Escalating celebration — bigger, louder, more hype.
- The gifter's name appears on the overlay.

### Incoming Raid
Raid detected → Welcome sound plays + team chant. The raider's channel name appears on overlay. **Volume scales with raid size** — a 200-person raid is audibly bigger than a 10-person raid.

### Hype Train
Each Hype Train level plays increasingly intense audio:
- **Level 1:** Subtle hype — barely noticeable, just a bit more energy.
- **Level 2:** Noticeable hype — the crowd picks up.
- **Level 3:** Big hype — the band gets louder.
- **Level 4:** Massive hype — stadium is rocking.
- **Level 5:** FULL STADIUM ERUPTION — everything at maximum intensity.

### Predictions
Streamer creates a prediction (e.g., "Will they convert this 3rd down?"). When the prediction resolves:
- Correct voters: celebration sound.
- Incorrect voters: groans / disappointment sound.
- Results posted in chat automatically.

---

## 48. All YouTube Features (Complete Reference)

### Live Chat Commands
Same `!td`, `!kickoff`, `!defense` commands as Twitch. Messages are read via the YouTube Live Chat API. Commands are queued (not dropped) to handle YouTube's rate limiting.

### Super Chat — Trigger Sound
- **$5 Super Chat:** Mapped to a trigger (streamer configures which one).
- **$10 Super Chat:** Viewer picks a specific song from the available list.
- **$20 Super Chat:** Full queue control for 2 minutes — viewer can add, remove, and reorder songs.

### Super Stickers
Specific Super Sticker IDs are mapped to specific sound effects:
- Star sticker → Touchdown horn.
- Heart sticker → Celebration cheer.
- Other stickers can be custom-mapped by the streamer.

### Membership Milestones
- **New member:** Team fight song plays.
- **Member milestones (1 month, 6 months, 1 year, etc.):** Escalating celebration sounds. Longer membership = bigger celebration.

### Live Polls
YouTube's built-in polling is limited. Bandroom reads poll results from chat and plays the winning song. Alternatively, streamers can use Bandroom's built-in poll system (which aggregates votes from both YouTube and Twitch).

### Chat Slow Mode Awareness
YouTube enforces slow mode more strictly than Twitch. Band Director shows a "Chat Delay: ~2s" indicator so streamers know there's a lag. Commands are queued internally rather than dropped.

---

## 49. The Song Queue & Viewer Requests

The song queue is a central list of viewer-requested songs waiting to be played. It's visible in the Band Director dashboard and on the stream overlay.

**How songs get into the queue:**
- Viewer chat commands.
- Channel Points redemptions ("Choose Next Song").
- Bits triggers (viewer picks a song).
- Super Chat triggers (viewer picks a song).
- Guest DJ requests.
- Poll winners.

**Queue display shows:**
- Song title and artist.
- Who requested it (viewer name).
- How it was requested (chat, channel points, bits, super chat, guest DJ, poll).
- Priority (higher = plays sooner).
- Status: pending, playing, played, skipped.

**Queue controls for the streamer:**
- Reorder songs (drag and drop).
- Remove songs.
- Skip the current song.
- Clear the entire queue.
- Boost a song's priority.
- Force-play a specific song immediately (bypassing the queue).

---

## 50. Guest DJ System

**What it is:** A system that lets viewers connect DIRECTLY to the streamer's Bandroom instance and control audio — with the streamer's permission.

**How it works:**

1. Streamer clicks "Generate Guest Code" in Band Director.
2. A random 6-character code is generated (e.g., `X7K2MP`). Uppercase only. No confusing characters (0/O, 1/I/L are excluded).
3. The code is displayed in the dashboard and (optionally) on the overlay.
4. A viewer downloads Bandroom (free), opens the "Join Stream" tab, enters the 6-digit code.
5. The streamer receives a notification: "[ViewerName] wants to join your Bandroom."
6. Streamer approves (or denies).
7. Streamer sets permissions for that viewer.

**Permission levels:**

| Level | What the viewer can do |
|---|---|
| **View Only** | See the active playlist. See the event log. See what's playing. No control. |
| **Request** | Request songs — they go into the queue for streamer approval. The streamer decides whether to play them. |
| **Queue** | Add songs directly to the queue (no approval needed). Reorder their own requests. |
| **Full Control** | Change team profiles. Trigger sounds directly. Change reverb and effects. Full audio control — like they're sitting at the streamer's computer. |

**Security:**
- Codes are 6 characters from a set of ~30 unambiguous characters = ~729 million possible combinations, but rate limiting prevents brute force.
- 5 wrong code attempts → code invalidated.
- Codes expire when the stream ends (or manually by the streamer).
- Maximum 50 concurrent guest DJs.
- All viewer actions are logged.
- Streamer can revoke any viewer's access instantly.

---

## 51. The Stream Overlay

**What it is:** A web page you add as a Browser Source in OBS (or any streaming software that supports browser sources). It displays what's happening in Bandroom on your stream.

**What the overlay shows:**
- **Now Playing:** Song title, artist, team logo.
- **Game Situation:** Down & distance, quarter, time remaining, score.
- **Requested By:** Viewer name (if the song was requested via chat/points/bits).
- **Up Next:** The next 2 songs in the queue.
- **Poll Results:** Live vote counts during active polls.

**Overlay URL:** `http://localhost:8765/overlay?streamer=YOUR_USER_ID`

**Setup in OBS:**
1. Add a new Source → Browser.
2. Paste the URL.
3. Set width and height (recommended: 400x200 for a corner overlay, or full width for a bottom bar).
4. Done.

**Customization:**
- Team colors auto-applied from the active matchup.
- Font size, position, animation speed — adjustable.
- Show/hide each element independently (hide "Requested By," show only "Now Playing," etc.).
- Multiple layout presets: Bottom Bar (horizontal strip), Top Bar, Corner Box, Center Banner.
- Overlay updates in real time — no manual refresh needed.

---

## 52. Viewer Polls

**What it is:** A poll system built into Band Director. The streamer creates a question with options, viewers vote, and the winning option plays automatically.

**Creating a poll:**
1. Click "Create Poll" in Band Director.
2. Type a question: "What song on 3rd & Long?"
3. Options are auto-populated from the active team's assigned songs. You can edit them.
4. Set duration: 30 seconds, 60 seconds, 90 seconds, or "until manually closed."
5. Click "Start Poll."

**Where the poll appears:**
- Band Director UI (streamer's view).
- Stream overlay (viewers see it on stream).
- Twitch chat (as a bot message).
- YouTube chat (as a bot message).
- Guest DJ viewer apps.

**How votes are collected:**
- Twitch chat keywords (viewers type "1", "2", "3", or option names).
- YouTube chat keywords.
- Guest DJ app (viewers click their choice).
- Channel Points: a custom reward "Vote in Poll" can be weighted higher than a regular vote.

**When the poll closes:**
- The winning song plays automatically.
- The overlay shows "Winner: [Song Name] with [X] votes."
- Poll results are archived in the database.
- The overlay returns to normal display.

---

# PART 7: THE MARKETPLACE

---

## 53. Browsing the Marketplace

The Bandroom marketplace is built directly into the app. Click the rainbow "The Bandroom" pill to open it.

**Browse by:**
- **Team:** All 134 NCAA Division I football teams. Click a team to see all songs available for that school.
- **Trigger Type:** Touchdown, Kickoff, Defensive Stop, Third Down, Fight Song, Punt, Field Goal, etc.
- **Category:** Fight Songs, Stand Tunes, Hype Stings, PA / Defense Chants.
- **Search:** Type a school name, song title, or keyword to find specific songs.

**Each song in the marketplace shows:**
- Title and artist.
- School abbreviation and logo.
- Trigger type and category.
- Duration.
- Download count.
- Energy level (High, Mid, Low).
- Instrumentation description.

**Previewing:** Click a song to hear a preview before downloading.

---

## 54. Downloading & Auto-Assign

**Downloading a song:**

1. Find a song you want in the marketplace.
2. Click "Download."
3. The song downloads to your local Songs folder and appears in "My Downloads."
4. If you're currently editing a team, the song also appears in that team's Sound Bank.

**My Downloads:** Shows EVERY song you've downloaded across all teams. Your personal library. From here, you can assign songs to teams and triggers.

**Auto-Assign:**

The single most time-saving feature. After downloading songs for a team:

1. Make sure the correct team is active (check the header).
2. Click "Auto-Assign."

Bandroom reads the metadata on every downloaded song (school, trigger type, category) and automatically assigns each one to the correct trigger for the active team. Touchdown songs go to the Touchdown slot. Kickoff songs go to the Kickoff slot. Defensive chants go to the Defensive Stop slot.

It's not perfect — you might want to tweak assignments afterward — but it gets you 90% of the way there in one click.

---

## 55. Uploading Songs

**The import flow:**

1. **Drag and drop** an audio file into Bandroom, or click "Import Song."
2. **The Trimmer opens.** You can trim the start and end of the song (keep the best 10-60 seconds). Use the waveform display to find the perfect cut points.
3. **LUFS analysis runs automatically.** Bandroom measures the loudness and calculates the normalization gain. This happens on a COPY of the file — your original is untouched.
4. **Metadata is auto-detected:**
   - Filename is parsed for school name and trigger hints.
   - Frequency analysis determines instrumentation (Heavy Brass, Drumline, Full Band, etc.).
   - Energy level is calculated from LUFS measurements.
   - A standardized filename is generated: `[ABBREV]_[TRIGGER]_[CLEAN_NAME].mp3`
5. **The prompt dialog appears** with all fields pre-filled. You can edit anything before confirming:
   - Title, artist
   - School
   - Primary trigger
   - Category
   - Trim points
   - Reverb preset
   - Energy level
   - Instrumentation
   - Description
6. **Confirm.** The processed file is saved to your Songs folder. Metadata is written to the database. The song appears in My Downloads and (if applicable) in the marketplace.

**Supported formats:** MP3, WAV, FLAC, AIFF, OGG.

---

## 56. The 11-Field Metadata Engine

Every song in Bandroom is tagged with 11 standardized metadata fields. This powers marketplace search, Auto-Assign, and the "similar songs" feature. Here's each field, what it means, and how it's determined:

| # | Field | Example | How Determined |
|---|---|---|---|
| 1 | **Standard Title** | `Tiger Rag` | Cleaned from filename — track numbers, years, rip tags, and weird characters are stripped. Proper capitalization applied. |
| 2 | **Standard Artist** | `LSU Golden Band from Tigerland` | Mapped to known marching band names database. Fallback: user enters manually. |
| 3 | **School Abbreviation** | `LSU` | 2-4 letter code. Auto-detected from filename/path if it contains a known school name. Otherwise prompted at import. |
| 4 | **Bandroom Standardized Filename** | `LSU_EVT_TD_TigerRag.mp3` | Auto-generated: `[ABBREV]_[TRIGGER]_[CLEAN_NAME].extension`. Never manually typed — ensures consistency. |
| 5 | **Primary Trigger** | `Touchdown` | User selects at import time from the 18 evaluator events. Auto-suggested from filename hints. |
| 6 | **Marketplace Category** | `Audio - Fight Song` | One of: Fight Song, Stand Tune, Hype Sting, PA / Defense Chant. User selects; auto-suggested by trigger type. |
| 7 | **Recommended Trim** | `00:05–00:15 (10s)` | Auto-analyzed: finds the loudest segment closest to the start of the file. User can adjust at import. |
| 8 | **Reverb Preset** | `Stadium` | Suggested by trigger type + energy level. Options: Stadium, Dome, Night Game, None. User can override. |
| 9 | **Energy Level** | `High (Big Game)` | Calculated from LUFS short-term measurements: High (above -12 LUFS), Mid (-12 to -18), Low (below -18). |
| 10 | **Instrumentation** | `Heavy Brass` | Frequency analysis: which range has the highest average amplitude? Options: Heavy Brass, Low Brass/Tuba, Marching Snare Drums, Stadium PA Horn, Full Band, Drumline, Electric Guitar Synth. |
| 11 | **Acoustic Description** | `Roaring brass explosion with snare cadence, instant stadium eruption trigger` | Auto-generated 15-word summary based on energy level + instrumentation + trigger type. User can write custom description. |

---

## 57. Instrumentation Detection (Frequency Analysis)

When you import a song, Bandroom performs a simple frequency analysis (FFT — Fast Fourier Transform) to determine the dominant instrumentation. No machine learning, no AI — just basic signal processing:

| Dominant Frequency Range | Classification |
|---|---|
| 60–250 Hz | Low Brass / Tuba |
| 250–500 Hz | Marching Snare Drums |
| 500–2,000 Hz | Heavy Brass (trumpets, trombones, mellophones) |
| 2,000–4,000 Hz | Full Band (balanced spectrum across all ranges) |
| Sharp transients (sudden loud spikes) | Drumline |
| Narrow band 500–4,000 Hz | Stadium PA Horn |

The classification is a rule-based system. It's not perfect — if the frequency analysis gets it wrong, you can manually select the correct instrumentation at import.

---

## 58. Profile Sharing

**What it is:** Share your entire team configuration (all song assignments, volume settings, reverb presets) with other Bandroom users.

**How to share:**
1. Set up a team exactly how you want it.
2. Click Save to save the profile.
3. Go to Help & Guide → Sharing Guide for instructions.
4. Your profile is stored as a JSON file in the Profiles folder. Share the file.
5. The recipient places it in their Profiles folder and loads it in Bandroom.

**How to load someone else's profile:**
1. Get their profile JSON file.
2. Place it in your `Profiles\` folder.
3. In Bandroom, select that team — the configuration loads.
4. Note: you'll need the SAME audio files (matching filenames) for the songs to play. The profile only stores references to filenames, not the audio itself.

**Profile sharing is filename-based.** If you share a profile that references `LSU_EVT_TD_TigerRag.mp3`, the recipient needs a file with that exact name in their Songs folder (or needs to manually reassign songs).

---

## 59. Content Policy (DMCA)

Bandroom's marketplace is a community-driven platform. By uploading a song, you confirm:
- You have the right to share the audio (you recorded it, you own it, or it's in the public domain).
- You're not uploading copyrighted commercial music.
- You understand that Bandroom operates under DMCA safe harbor provisions.

**DMCA takedowns:** If you believe content in the marketplace infringes your copyright, contact the repository owner. Bandroom will process valid DMCA takedown requests promptly.

---

# PART 8: THE CLOUD (SUPABASE)

---

## 60. What the Cloud Database Does

Bandroom uses Supabase — a cloud PostgreSQL database — to store and sync your data across devices.

**Before the cloud database (the old way):**
- All data stored in individual JSON files on your PC (`Profiles\LSU.json`, `Profiles\Alabama.json`, etc.).
- 134+ separate files.
- Only accessible on that one PC.
- No way to search across teams ("show me all SEC teams with a touchdown song assigned").
- If your hard drive died, everything was lost.

**With the cloud database (the new way):**
- All data stored in structured database tables.
- Accessible from PC, Mac (coming soon), and phone (via web dashboard).
- Changes on one device sync to all others within 1 second.
- Full search capability — "show me all touchdown songs for SEC teams" is an instant query.
- Your data is backed up in the cloud.
- Offline fallback: if the internet goes down, Bandroom works from local files. Syncs when internet returns.

---

## 61. Row-Level Security

Supabase uses Row-Level Security (RLS) — a database rule that ensures you can only see YOUR own data.

**What RLS means in practice:**
- When you look up your team configs, the database automatically filters to only show rows where `user_id` matches your Google account.
- You cannot see other users' private configurations.
- You cannot modify other users' data.
- Public data (songs marked `is_public = true`) is readable by everyone.
- Private data (your personal team configs, activity logs) is only readable by you.

This is enforced at the database level — not in the app code. Even if someone bypassed Bandroom and tried to query the database directly, RLS would block them from seeing data that doesn't belong to them.

---

## 62. What Syncs and What Doesn't

**What syncs to the cloud:**
- Team configurations (song assignments, volume settings, reverb presets).
- Song metadata (title, artist, school, trigger, category, LUFS data).
- Marketplace listings (public songs you've uploaded).
- Activity logs (optional — you can disable this).
- Streamer profiles (Twitch/YouTube connection data — tokens are encrypted).
- Polls and queue data (active during a stream).

**What stays local (does NOT sync):**
- Audio files themselves. Songs stay on your PC (cloud storage for audio is planned but not yet implemented — currently, the marketplace stores metadata and file paths; actual audio downloads come from other users via the marketplace).
- OCR debug logs.
- Crash logs.
- Scorebug presets (these are machine-specific since they depend on your display configuration).

**Offline mode:** If Supabase is unreachable (no internet), Bandroom falls back to local JSON files automatically. All core game-watching and audio playback works without internet. Data syncs when the connection returns.

---

# PART 9: REFERENCE

---

## 63. Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl + S` | Save current team profile |
| `Ctrl + T` | Open team picker |
| `Ctrl + M` | Open matchup panel (LOCK IN?) |
| `Ctrl + D` | Open My Downloads |
| `Ctrl + B` | Open The Bandroom marketplace |
| `Ctrl + A` | Auto-Assign songs for active team |
| `Ctrl + H` | Open Help & Guide |
| `Ctrl + ,` | Open Settings |
| `Ctrl + Shift + S` | Open Sound Bank for active team |
| `F1` | Help & Guide |
| `F5` | Refresh marketplace data |
| `Esc` | Close current panel/overlay |

---

## 64. Troubleshooting Common Problems

### "Bandroom isn't detecting events (the watch dot is red)"

**Check:**
- Is CFB 27 visible on screen and not minimized?
- Is the scorebug visible (not covered by another window)?
- Is your display at 1920x1080 resolution? Lower resolutions may affect OCR accuracy.
- Try a different scorebug preset in Settings.
- Check the OCR debug log to see what text Bandroom is reading.

### "Sounds are delayed"

**Check:**
- Are you in Shared Mode? Switch to WASAPI Exclusive Mode in audio settings for lower latency.
- Are your audio files on a slow hard drive? Move Bandroom to an SSD if possible.
- Is your CPU overloaded? Enable Low-Performance Mode in settings.

### "Some songs are too quiet / too loud"

**Check:**
- Has the song been LUFS normalized? Check the song's metadata in Sound Bank. If it shows "Not analyzed," re-import the song.
- Are the master volume and individual trigger volumes set correctly?
- Check if the limiter is engaged (it should be — if it's not, enable it in audio settings).

### "Bandroom won't launch"

**Check:**
- Is another copy already running? Check Task Manager for `Bandroom.exe`. End any existing processes.
- Run `Setup.exe` again to repair the installation.
- Check `crash.log` in the Bandroom install folder for error details.
- Make sure .NET 10 is installed (the installer should handle this automatically).

### "The marketplace isn't loading"

**Check:**
- Do you have internet access?
- Is the Cloudflare Worker responding? The marketplace API is at `bandroom-marketplace.bandroom.workers.dev`. If it's down, check the Bandroom Discord for status updates.
- Try refreshing (F5).

### "Twitch/YouTube won't connect"

**Check:**
- Have you authorized Bandroom in your Twitch/YouTube settings? Go to your account's connected apps page and look for Bandroom.
- Try disconnecting and reconnecting.
- Tokens may have expired — Bandroom auto-refreshes them, but if something went wrong, re-authorize.

### "The overlay isn't showing in OBS"

**Check:**
- Is the URL correct? Should be `http://localhost:8765/overlay?streamer=YOUR_USER_ID`.
- Is Bandroom running? The local server only runs while Bandroom is open.
- Is the Browser Source sized correctly? Try 400x200 or full width.

---

## 65. Glossary

| Term | What It Means in Plain English |
|---|---|
| **API** | A set of digital doors that let programs talk to each other. Bandroom uses APIs to talk to Twitch, YouTube, Supabase, and Cloudflare. |
| **Audio Ducking** | Automatically turning down background sounds when something important plays. Like a DJ turning down music to talk. |
| **Band Director** | Bandroom's streamer dashboard — connects to Twitch and YouTube, lets viewers control audio. |
| **Bits** | Twitch's virtual currency. Viewers spend money to cheer Bits; Bandroom triggers sounds based on Bit amounts. |
| **Brickwall Limiter** | A hard ceiling on volume. No matter what happens, audio never exceeds the limit. Protects speakers and ears. |
| **Channel Points** | Twitch's free currency viewers earn by watching. They can spend points to trigger sounds in Bandroom. |
| **Cloudflare Worker** | A tiny program running on the internet that acts as the middleman for the marketplace — receives uploads, serves downloads, handles search. |
| **Compression (Audio)** | Making quiet parts louder and loud parts quieter so everything sounds even and controlled. |
| **Cooldown** | A waiting period that prevents the same sound from playing too often. Stops spam and OCR double-fires. |
| **dBFS** | Decibels relative to Full Scale — how digital audio measures loudness. 0 dBFS is the absolute maximum; anything above clips. |
| **DSP** | Digital Signal Processing. Math applied to audio to change how it sounds. EQ, reverb, compression, and limiting are all DSP. |
| **EQ (Equalization)** | Adjusting how loud different frequencies are. Like bass and treble knobs, but more precise. |
| **Evaluator** | One of Bandroom's 18 game event detectors. Each evaluator watches for a specific pattern (touchdown, first down, etc.) in the OCR data. |
| **EventSub** | Twitch's system for sending real-time notifications when things happen (subscriptions, Bits, raids, Hype Trains). |
| **FFT (Fast Fourier Transform)** | A math trick that breaks audio into its component frequencies. Used for instrumentation detection and the spectrum visualizer. |
| **Guest DJ** | A system that lets viewers connect to a streamer's Bandroom and control audio remotely with permission. |
| **Hype Train** | A Twitch event where rapid support (subs, Bits) triggers escalating celebrations. Level 1 through 5. |
| **IRC** | Internet Relay Chat — the old-school chat protocol Twitch uses under the hood. Bandroom connects to Twitch chat via IRC. |
| **JSON** | A simple text format for storing structured data. Bandroom's local profiles are JSON files. |
| **JWT** | JSON Web Token. A digital ID card that proves who you are without needing a password. Used for Supabase authentication. |
| **LUFS** | Loudness Units relative to Full Scale. Measures how loud something FEELS to human ears (not just its electrical volume). Used by Netflix, Spotify, and Bandroom. |
| **Metadata** | Data about data. A song's title, artist, school, and trigger type are metadata — they describe the song without being the audio itself. |
| **NAudio** | A free, open-source audio library for C#. Handles playback, DSP processing, and audio file reading in Bandroom. |
| **OAuth2** | A login system where you sign in with one service's account (like Google or Twitch) without giving your password to the app. |
| **OCR** | Optical Character Recognition. Looking at an image and reading the text in it. Bandroom uses OCR to read the scorebug. |
| **PostgreSQL** | A powerful, reliable database system. Supabase runs PostgreSQL. |
| **Prediction (Twitch)** | A feature where viewers bet Channel Points on outcomes. Bandroom plays celebration or groan sounds based on results. |
| **Reverb** | Echo. The way sound bounces around a room or stadium before it fades away. |
| **RLS (Row-Level Security)** | A database rule: "you can only see your own rows." Prevents users from accessing each other's data. |
| **Scorebug** | The on-screen graphic showing score, down, distance, quarter, and time during a football broadcast. |
| **Squirrel** | The installer and auto-update system Bandroom uses. Fast, handles deltas, creates shortcuts. |
| **Supabase** | A cloud database service built on PostgreSQL. Bandroom's data lives in Supabase. |
| **Trigger** | A game event that causes Bandroom to play a sound. "Touchdown" is a trigger. "First Down" is a trigger. There are 18 triggers total. |
| **True Peak (dBTP)** | The absolute maximum level of audio, including peaks that fall between digital samples. More accurate than regular peak measurement. |
| **WebView2** | A mini web browser (Microsoft Edge engine) embedded inside Bandroom. Powers the user interface. |
| **WebSocket** | A permanent open connection between two programs for instant communication. Used for real-time sync and Guest DJ connections. |

---

## 66. Version History & Auto-Update System

Bandroom uses Squirrel for auto-updates. Every time you launch the app, it checks for a new version. Updates are delta-based — you only download the files that changed, not the entire app.

**Current version:** 1.0.0 (initial public release)

**How versioning works:**
- Versions follow the format `vMajor.Minor.Patch` (e.g., `v1.0.0`).
- Patch increases: bug fixes, small improvements.
- Minor increases: new features.
- Major increases: significant changes, rewrites, or breaking changes.

**Changelog:** Available in the app under Settings → Updates, and on the GitHub Releases page.

---

## 67. Community & Support

**GitHub:** `github.com/kingsupreme89/Bandroom-v1`
- Report bugs (Issues tab).
- Suggest features (Issues tab → Feature Request).
- See the source code.
- Download the latest release.

**Discord:** CFB Modding Discord
- Get help from the community.
- Share profiles and song recommendations.
- Report bugs and suggest features in real time.
- Stay updated on development progress.

**In-app help:** Click the "?" Help & Guide pill in the header bar for:
- Keyboard shortcuts reference.
- Profile sharing guide.
- Quick troubleshooting tips.

**Diagnostic zip:** Settings → "Create Diagnostic Zip." Packages all logs and system info into one file for support requests.

---

## 68. Thank You

Bandroom was built in about a week. It's being edited daily. It has bugs. It has rough edges. But it WORKS — the OCR detects events, the audio fires, the streaming connects, and the marketplace serves songs.

Every day is a step toward perfection. The community makes it better — your bug reports, your feature suggestions, your song uploads, your profile shares.

If something's broken: report it. If something's great: tell someone. If you made an awesome profile: share it.

Thank you for being here this early. Let's make College Football sound like Saturday.

---

*The Bandroom Manual — Version 1.0.0 — Current as of initial public release.*