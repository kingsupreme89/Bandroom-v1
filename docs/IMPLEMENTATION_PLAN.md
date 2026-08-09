# Bandroom Implementation Plan — Build-Only, No Fluff

**Date:** August 8, 2026  
**Rule:** Only features that don't exist yet. Every item has exact files to touch.

---

## SPRINT 1 — TONIGHT (3–4 hrs, 8 items)

### 1.1 Crowd Audio Ducking Slider
**What:** Slider that lowers game crowd noise 0-100% when a Bandroom track fires.  
**Files:** `wwwroot/index.html` (add slider to Mixer panel), `wwwroot/app.js` (broadcast ducking level on fire), `AudioPlayer.cs` (apply ducking as volume multiplier on game audio output).  
**How:** AudioPlayer already has MasterVolume/HomeVolume/AwayVolume. Add a `DuckingLevel` float. When a clip fires, store previous volume, set to ducked level, restore after clip ends.

### 1.2 Scorebug Status LED
**What:** Top-corner indicator showing OCR sync state: GREEN=Live Scanning, YELLOW=Calibrating, RED=Scorebug Hidden.  
**Files:** `wwwroot/index.html` (add LED element), `wwwroot/style.css` (pulse animation), `wwwroot/app.js` (listen for watch state), `WebMainForm.cs` / `MainWindow.axaml.cs` (push status to JS).  
**How:** `WebMainForm.OnWindowFoundChanged()` already fires `bandroom:watchstate`. Add color info to that event. In `MainWindow.axaml.cs`, `ToggleWatchingFromWeb` returns "watching"/"waiting"/"off" — map to GREEN/YELLOW/RED.

### 1.3 Multi-Track Randomizer Pool
**What:** Assign 2-5 different songs to one trigger (e.g. "Touchdown") and rotate through them.  
**Files:** `TriggerEntry.cs` (add `List<string> AlternateFiles`), `ConfigStore.cs` (serialize new field), `WebMainForm.cs` / `MainWindow.axaml.cs` (`FireEvent` picks random from pool), `wwwroot/app.js` (multi-file UI on assign card).  
**How:** When `FireEvent` runs, if `entry.AlternateFiles.Count > 0`, pick random index, play that file. Track last-played index to avoid immediate repeats. UI: plus button on assign card adds another file slot.

### 1.4 Test Fire Button on Hover
**What:** Hover any assignment card → "Test Fire" button appears, plays the assigned song with live reverb/volume.  
**Files:** `wwwroot/app.js` (hover handler, call `PreviewEvent`), `wwwroot/style.css` (button visibility on hover).  
**How:** Already have `PreviewEventFromWeb` wired. Just need CSS `:hover` to show a play button on each card, and a click handler calling `bandroom.assignEvent(trigger)` to preview.

### 1.5 Red Zone Evaluator
**What:** New evaluator that fires "Offense: Red Zone" / "Defense: Red Zone Stop" when offense enters the 20-yard line.  
**Files:** NEW `src/Bandroom.Core/Helpers/RedZoneHelper.cs`, register in `GameWatcher.cs` line ~158 and `MainWindow.axaml.cs` line ~215.  
**How:** Copy pattern from `BigEventHelper.cs`. Check `state.Current.YardLine` (need OCR to populate this — for now, manual trigger key or estimate from OCR'd down-distance text). Add EventKeys to `EVENT_KEY_MAP.md`.

### 1.6 Pick-Six Split (INT vs Pick-6)
**What:** Separate trigger cards for regular interception vs interception returned for touchdown.  
**Files:** `src/Bandroom.Core/Helpers/TurnoverHelper.cs` (split into two EventKeys), `docs/EVENT_KEY_MAP.md` (add new keys).  
**How:** TurnoverHelper currently fires `Defense: Turnover Forced`. Add logic: if `IsTouchdown` is true on same snapshot, fire `Defense: Pick Six` instead. Needs OCR to detect both simultaneously, or chain detection.

### 1.7 Global Mute / Panic Button
**What:** Big red [STOP ALL AUDIO] button, hotkey Escape or Spacebar, on every screen.  
**Files:** `wwwroot/index.html` (add panic button), `wwwroot/style.css` (big red prominent), `wwwroot/app.js` (call `StopPreview`), `WebMainForm.cs` / `MainWindow.axaml.cs` (already has `StopAll` wired).  
**How:** `AudioPlayer.StopAll()` already exists. Just need a prominent UI button + keyboard shortcut. In app.js, add `document.addEventListener('keydown', e => { if (e.key === 'Escape') bandroom.stopPreview(); })`.

### 1.8 Profile Cloning (Home → Away)
**What:** One-click button copies Home team's entire song assignment map to Away team.  
**Files:** `wwwroot/app.js` (add Clone button in matchup panel), `MacWebBridge.cs` / `WebBridge.cs` (add `CloneProfileToAway` method), `MainWindow.axaml.cs` / `WebMainForm.cs` (copy `_homeConfig` to `_awayConfig`, save).  
**How:** `ConfigStore.SaveProfile(awayName, homeConfigSnapshot)`, reload `_awayConfig`. Trivial.

---

## SPRINT 2 — THIS WEEKEND (6-8 hrs, 11 items)

### 2.1 Rivalry "Big Game" Multiplier
**What:** Flag a matchup as a rivalry → auto-boost volume +3dB, activate aggressive crowd chants category.  
**Files:** `TriggerEntry.cs` (add `RivalryBoost` bool), `wwwroot/app.js` (rivalry checkbox in matchup panel), `WebMainForm.cs` / `MainWindow.axaml.cs` (apply +3dB in `FireEvent`), NEW `TeamColors.cs` rival pairs list.  
**How:** Hardcode known rivalries (Alabama-Auburn, Michigan-Ohio State, etc.) in TeamColors. When both teams match a known pair, auto-set boost flag.

### 2.2 3rd & Short vs 3rd & Long Split
**What:** Different songs for 3rd & 1-3 (tense drumline) vs 3rd & 8+ (full hype horn).  
**Files:** `src/Bandroom.Core/Helpers/DefenseHelper.cs` (check YardsToGo, emit different EventKeys), `docs/EVENT_KEY_MAP.md`.  
**How:** DefenseHelper already has `Defense: Third Down (Loss)`. Add `Defense: Third Down (Short)` for ≤3 yards, `Defense: Third Down (Long)` for 8+ yards. Needs `PlaySnapshot.YardsToGo` populated by OCR.

### 2.3 Field Goal Miss / Blocked Kick Stings
**What:** Sad brass / crowd groan triggers when a FG is missed or blocked.  
**Files:** `src/Bandroom.Core/Helpers/FieldGoalMissedHelper.cs` (split into Missed and Blocked), `docs/EVENT_KEY_MAP.md`.  
**How:** Existing `Defense: Field Goal Missed by Opponent` already covers the miss case. Add `Defense: Field Goal Blocked` as separate key. OCR needs to distinguish, or manual hotkey.

### 2.4 4th Quarter Clutch Mode
**What:** Auto audio override when score within 7 points under 2:00 in 4th quarter.  
**Files:** NEW `src/Bandroom.Core/Helpers/ClutchModeHelper.cs`, register in evaluator list.  
**How:** Check `Quarter == 4 && TimeRemainingSeconds < 120 && Math.Abs(HomeScore - AwayScore) <= 7`. Fire `Other: Clutch Mode Activated`. Volume boost all subsequent triggers. Needs OCR score/time reads.

### 2.5 Compact Overlay "HUD Mode"
**What:** Shrink the app to a floating glass bar fitting over game streams.  
**Files:** `wwwroot/index.html` (HUD mode layout), `wwwroot/style.css` (compact styles via CSS class toggle), `wwwroot/app.js` (toggle HUD mode).  
**How:** Add `--hud` class to body. Compact card layout, hide sidebar, minimal chrome. Hotkey Ctrl+H toggles. Window resize to ~400x80.

### 2.6 Batch MP3 Drag-and-Drop with Auto-Assign
**What:** Drag 10 files into window → AI/intake naming engine auto-maps to triggers.  
**Files:** `WebMainForm.cs` (`OnSongDragDrop` already exists), `IntakeEngine.cs` (already has filename parsing), `wwwroot/app.js` (show auto-assign suggestions modal).  
**How:** Existing `OnSongDragDrop` calls `ConfigStore.ImportIntoSongsLibrary`. Extend to run `IntakeEngine.Process()` on each filename, suggest trigger mapping, show confirmation dialog.

### 2.7 DMCA-Safe Filter Toggle
**What:** Setting that hides copyrighted commercial audio in marketplace, showing only royalty-free marching band.  
**Files:** `wwwroot/app.js` (filter toggle in marketplace panel), marketplace worker (add `contentType` field: "marching_band" vs "commercial").  
**How:** Add `contentType` field to marketplace items. Filter UI toggle calls API with `?type=marching_band`.

### 2.8 Auto-Sync Marketplace Updates
**What:** Prompt when a creator updates a downloaded sound pack with higher-quality audio.  
**Files:** `MarketplaceDownloadService.cs` (version check on packs), `wwwroot/app.js` (notification UI).  
**How:** Marketplace worker returns `version` field per item. On app launch, compare downloaded version with current server version. Show "Update Available" badge in My Downloads.

### 2.9 Super Sim Auto-Mute
**What:** Detects when player enters Super Sim in Dynasty mode and silences triggers.  
**Files:** `GameWatcher.cs` (detect super sim OCR pattern — screen dims/speeds up), `WebMainForm.cs` (mute flag).  
**How:** OCR detects "Super Sim" or "Simulating" text in game UI, sets mute flag. Or: detect rapid score changes as signal (Super Sim skips plays).

### 2.10 First Down Chain Gang Stings
**What:** Quick 3-second brass stings firing the exact frame 1st down yardage is gained.  
**Files:** `src/Bandroom.Core/Helpers/FirstDownHelper.cs` (add EventKey for "chain gang sting" variant).  
**How:** Already fires `Offense: Earned First Down`. Add `Offense: First Down (Short Gain)` for ≤3 yard gains — short stinger, not full fight song.

### 2.11 Trending Sounds Row
**What:** "Most Used This Weekend" row in marketplace UI.  
**Files:** `wwwroot/app.js` (trending section), marketplace worker (track download counts with weekend window).  
**How:** Worker returns items sorted by download count in last 72 hours. UI shows top 8 items in a horizontal scroll row above categories.

---

## SPRINT 3 — NEXT 1-2 WEEKS (10-15 hrs, 10 items)

### 3.1 Dynamic Stadium Size Reverb
**What:** Reverb scales based on real stadium capacity. Michigan 107k = massive decay, Boise State 36k = tight.  
**Files:** `TeamColors.cs` (add `StadiumCapacity` field per team), `AudioPlayer.cs` (calculate reverb params from capacity), existing reverb presets.  
**How:** Add capacity data to team registry. When home team selected, calculate `ReverbPreset` params: roomSize = capacity/110000, damp = 0.3 to 0.7 inversely.

### 3.2 Post-Event Smart Crossfade
**What:** Hard-cut crossfade out of fight song back into game audio at clip end.  
**Files:** `AudioPlayer.cs` (already has `FadeStartSeconds`/`FadeOutDuration` for hard stop), no fade-in needed.  
**How:** Already implemented as hard stop. Just tune `FadeStartSeconds` to match typical clip length minus 1-2 seconds.

### 3.3 EQ Profiles (Headset vs Home Theater)
**What:** Quick toggle optimizing frequencies for SteelSeries/Astro headsets vs 5.1 surround.  
**Files:** `AudioPlayer.cs` (EQ param struct), `wwwroot/index.html` (toggle in Mixer), `wwwroot/app.js` (send EQ selection).  
**How:** Add `EqProfile` enum (Flat, Headset, HomeTheater, Bass). Apply simple bass/treble gain multipliers in `Play()` before setting volume. Headset: +3dB treble, -2dB bass. HomeTheater: flat. Bass: +6dB low end.

### 3.4 Subwoofer / Bass Boost
**What:** Low-end EQ punch for stadium bass drums, tubas, touchdown cannons.  
**Files:** `AudioPlayer.cs` (bass multiplier), `wwwroot/index.html` (slider).  
**How:** Add `BassBoost` float (0.0 to 1.0). In NAudio path (Windows): apply low-shelf filter. On Mac (afplay): pass through `-v` only — EQ limited.

### 3.5 WASAPI / ASIO Low-Latency Audio (Windows only)
**What:** Native low-latency audio engine option eliminating buffer delays.  
**Files:** `AudioPlayer.cs` (switch from `WaveOutEvent` to `WasapiOut`), NAudio API.  
**How:** NAudio supports `WasapiOut` with `AudioClientShareMode.Shared`. Add toggle, use `WasapiOut` instead of `WaveOutEvent` when enabled. Reduces latency from ~100ms to ~10ms.

### 3.6 Stream Deck Plugin
**What:** Native integration mapping categories and hotkeys to Elgato Stream Deck buttons.  
**Files:** NEW `streamdeck/` folder with plugin manifest + JS code.  
**How:** Stream Deck plugins are simple: manifest.json + JS that sends HTTP requests to Bandroom's localhost server. Add `/api/trigger/{eventKey}` endpoint to startWebServer in `MainWindow.axaml.cs`.

### 3.7 Export / Import .BAND File Packs
**What:** One-click bundle sharing: export entire team configuration as .band JSON file, import on another machine.  
**Files:** `wwwroot/app.js` (Export/Import buttons), `WebBridge.cs` / `MacWebBridge.cs` (already has `ExportProfile`/`ImportProfile` — rename and extend to bundle audio + config).  
**How:** Export packs trigger config + list of filenames + checksums. Import matches by filename/checksum against downloaded marketplace items. New file extension `.band` (really a zip containing JSON + optional audio references).

### 3.8 Discord Rich Presence
**What:** Show live status in Discord ("Playing as LSU vs Alabama — Q3 14-10").  
**Files:** NEW Discord RPC integration via `DiscordRichPresence` NuGet or direct IPC.  
**How:** Discord Game SDK or simple named pipe IPC. Update presence on game state change: `{home} vs {away} | Q{quarter} {homeScore}-{awayScore}`.

### 3.9 Multi-Monitor Window Pinning
**What:** Keep Bandroom always-on-top on secondary displays.  
**Files:** `WebMainForm.cs` (`TopMost = true` already exists via Settings), `MainWindow.axaml.cs` (Avalonia `Topmost` property).  
**How:** Expose `SetAlwaysOnTop(bool)` from WebBridge → Settings UI → `Window.Topmost` on Avalonia, `Form.TopMost` on WinForms. Already half-built in `OpenSettingsFromWeb`.

### 3.10 System Tray Minimization
**What:** Minimize to system tray with background execution.  
**Files:** `WebMainForm.cs` (WinForms NotifyIcon), `MainWindow.axaml.cs` (Avalonia TrayIcon).  
**How:** On minimize, hide window, show tray icon. Tray icon has "Show Bandroom" + "Exit" context menu.

---

## SPRINT 4 — MONTH+ (30+ hrs, big features)

### 4.1 AI Commentary Voice Clone System
**What:** Full pipeline: record 2-5 min voice sample → clone via local F5-TTS model → generate play-by-play lines via Ollama LLM → play cloned voice alongside songs.  
**Files:** NEW `scripts/announcer_server.py` (FastAPI), `src/Bandroom.Core/Services/AnnouncerService.cs`, `wwwroot/app.js` (announcer settings panel).  
**How:** See `docs/Bandroom_AI_Commentary_Research.md` for full architecture. Pipeline: GameWatcher event → Ollama (Llama 3.1 8B) generates line → F5-TTS synthesizes in cloned voice → AudioPlayer plays concurrently at PaVolume. No training needed (zero-shot cloning).

### 4.2 Mobile Remote Controller
**What:** QR code on screen opens mobile web app to trigger hotkey sounds from phone.  
**Files:** NEW `wwwroot/mobile.html` (lightweight mobile UI), `wwwroot/app.js` (host API endpoints).  
**How:** Serve mobile page from same localhost:18765. Mobile UI shows game situation buttons. Tap fires HTTP POST to `/api/trigger/{eventKey}`. QR code generation in main UI pointing to `http://{local-ip}:18765/mobile.html`.

### 4.3 OBS Studio Dock Panel
**What:** Embedded dock view fitting inside OBS Studio's browser dock.  
**Files:** NEW `wwwroot/obs-dock.html` (compact dock layout).  
**How:** OBS dock is just a browser source pointed at a URL. Serve a compact version at `http://localhost:18765/obs-dock.html` with current song + trigger log + mute button.

### 4.4 Twitch Chat Trigger Integration
**What:** Channel point rewards or chat commands (!td, !neck) manually fire stings.  
**Files:** NEW `src/Bandroom.Core/Services/TwitchChatService.cs`, `wwwroot/app.js` (Twitch config panel).  
**How:** Connect to Twitch IRC (irc.chat.twitch.tv) with OAuth token. Listen for `!commands` in chat. Map commands to EventKeys. Fire through same pipeline as game events.

### 4.5 Discord Bot Relay
**What:** Stream stadium music directly into Discord voice channel for Dynasty league buddies.  
**Files:** NEW Discord bot (separate process or integrated via Discord.Net).  
**How:** Virtual audio cable routing: Bandroom output → Virtual Cable → Discord input. Or: separate bot process that joins voice channel and streams audio via ffmpeg.

### 4.6 Community Leaderboard
**What:** Display top downloaded team creators on app home screen.  
**Files:** `wwwroot/app.js` (leaderboard section), marketplace worker (aggregate download counts by creator).  
**How:** Worker returns `/leaderboard` endpoint sorted by total downloads. UI shows top 10 creators with avatar, school, download count.

### 4.7 Daily Featured School Showcase
**What:** Daily spotlight on a specific university's band history and community audio pack.  
**Files:** `wwwroot/app.js` (featured section), `ChangelogService.cs` or new endpoint.  
**How:** Curated list of 365 schools. Day-of-year modulo to pick featured school. Show band history blurb, top community pack, "Load This Setup" one-click button.

### 4.8 Share to TikTok / X Clip Generator
**What:** One-click clip exporter: last 15 seconds of gameplay + Bandroom audio overlaid.  
**Files:** NEW screen recorder module (Windows: SharpDX / Mac: AVFoundation capture).  
**How:** Ring buffer of last 15 seconds of triggered audio events. On "Share Clip" button, render buffer to video file with game audio + Bandroom track mix.

### 4.9 Cross-Device Cloud Profile Sync
**What:** Auto-sync all custom profiles across multiple PCs via the existing marketplace worker.  
**Files:** `ProfileSyncService.cs` (already exists — extend to sync team profiles not just user stats).  
**How:** On sign-in, pull all team profiles from cloud. On save, push to cloud. Conflict resolution: newest `SavedAt` timestamp wins.

### 4.10 Gamepad Vibration Feedback
**What:** Controller vibrates gently on stadium bass hits or touchdown triggers.  
**Files:** Windows: `Windows.Gaming.Input` API. Mac: `CoreHaptics` via P/Invoke.  
**How:** On `FireEvent`, send short vibration burst to connected gamepad. Intensity based on event type (Touchdown = strong, First Down = light).

---

## QUICK REFERENCE: EXISTING ARCHITECTURE HOOKS

| Hook Point | File | Line(s) |
|-----------|------|---------|
| Register new evaluator (Windows) | `GameWatcher.cs` | ~158 `CreateRouter()` |
| Register new evaluator (Mac) | `GameWatcher.Mac.cs` / `MainWindow.axaml.cs` | `CreateRouter()` / `AllEvaluators()` |
| Add EventKey | `docs/EVENT_KEY_MAP.md` | Append to table |
| Add JS-callable method (Windows) | `WebBridge.cs` | ~class body |
| Add JS-callable method (Mac) | `MacWebBridge.cs` | ~class body |
| Add public method (Windows) | `WebMainForm.cs` | ~"Public methods" region |
| Add public method (Mac) | `MainWindow.axaml.cs` | ~"Public methods called from MacWebBridge" |
| Add audio setting | `AudioPlayer.cs` | ~static field + apply in `Play()` |
| Add UI element | `wwwroot/index.html` | Within panel sections |
| Add UI logic | `wwwroot/app.js` | Within relevant init function |
| Add UI style | `wwwroot/style.css` | Within category section |
| Add team data | `TeamColors.cs` | `All` array |

---

## PRIORITY ORDER (If You Can Only Do Some)

1. **Crowd Ducking** — biggest audio quality win, one slider
2. **Test Fire Button** — makes assigning songs 10x faster
3. **Scorebug LED** — instant visual feedback, 10 lines of code
4. **Multi-Track Randomizer** — stops repetition, high user demand
5. **Profile Cloning** — saves hours of manual assignment
6. **Panic Button** — essential for streamers
7. **3rd & Short vs Long** — adds real tactical depth
8. **Rivalry Multiplier** — "wow" factor on big games
9. **Red Zone Evaluator** — new game situation coverage
10. **Compact HUD Mode** — streamer-focused, small build