# 🏈 Bandroom — Master Roadmap
**Last Updated:** 2026-08-09

> 🔴 = Broken / Needs Fixing | 🟡 = In Progress | 🟢 = Done | ⚪ = Not Started

---

## 1. BUILD STATUS

| Assembly | Errors | Warnings | Status |
|---|---|---|---|
| `Bandroom.Core.dll` | 0 | 0 | 🟢 GREEN |
| `Bandroom.dll` (Windows) | 0 | 0 | 🟢 GREEN |
| `Bandroom.Mac.dll` | **78** | — | 🔴 BROKEN (MacWebBridge.cs calls missing MainWindow methods) |

---

## 2. WINDOWS ENGINE — CORE GAME DETECTION (🟢 SHIPPED)

16 evaluators, 42 EventKeys, OCR polling at 250ms.

### Evaluators (all in `src/Bandroom.Core/Helpers/`)

| Evaluator | Purpose | Status |
|---|---|---|
| `TouchdownHelper` | Offensive/defensive TD detection | 🟢 |
| `TurnoverHelper` | Interception/fumble + icing | 🟢 |
| `FieldGoalPATHelper` | FG made, PAT, 2-pt conversion | 🟢 |
| `FieldGoalMissedHelper` | Missed FG (banner+no score change) | 🟢 |
| `SafetyHelper` | Safety (defense +2) | 🟢 |
| `FirstDownHelper` | Earned 1st down + Big Gain variant | 🟢 |
| `OffenseDownHelper` | Offensive 2nd/3rd/4th down | 🟢 |
| `DefenseHelper` | Defensive 2nd/3rd down + loss variants | 🟢 |
| `BigEventHelper` | 3rd/4th down stops, 4th down loss | 🟢 |
| `DownFieldPositionHelper` | Midfield/loss position variants (YardLine-gated) | 🟢 |
| `TflHelper` | Tackle for loss | 🟢 |
| `KickoffHelper` | Opening, 2nd-half, receive/kick variants | 🟢 |
| `PenaltyHelper` | Offense/defense penalty flagged | 🟢 |
| `GameStateEventHelper` | Q2/Q4 start, pregame, iced game, victory | 🟢 |
| `TimeoutHelper` | Timeout with remaining-count variants | 🟢 |
| `DriveStarterHelper` | New possession (non-kickoff, non-turnover) | 🟢 |
| `NoPuntReturnHelper` | Fair catch / no return | 🟢 |

### Event Routing (WebMainForm.cs)
- `EventsDetected` → `OnEngineEventsDetected` → `FireEventForSide`
- `"Defense:*"` → fires for side opposite possession
- `"Penalty: Offense"` → fires for defense (they celebrate)
- Everything else → fires for possession side
- `FireCooldown`: 20s per-audio-file dedup

### Key Architecture
```
OCR Loop (250ms) → RouteEngineTick() → PlaySnapshot rotation
→ GameState(Current, Previous, UserIsHome) → EventRouter.Route()
→ 16 evaluators → List<TriggerEvent> → EventsDetected event
→ WebMainForm.OnEngineEventsDetected() → FireEventForSide() → AudioPlayer.Play()
```

---

## 3. OCR CALIBRATION (🟡 MIXED — MOSTLY CALIBRATED, NEEDS LIVE VERIFICATION)

| OCR Region | Status | Notes |
|---|---|---|
| `down` (down & distance) | 🟢 Working | "1st & 10", "3rd & 7", etc. |
| `quarter` (quarter text) | 🟢 Working | "1st", "2nd", etc. |
| `situation` (event text) | 🟢 Working | Kickoff, touchdown, turnover, PAT, etc. |
| `possession` (color underlay) | 🟢 Working | Pixel-brightness color sampling |
| `awayscore` / `homescore` | 🟡 Calibrated, untested | Tight positional crops, estimated |
| `clock` (time remaining) | 🟡 Calibrated, untested | Estimated crop |
| `flag` (penalty banner) | 🟡 Calibrated, untested | Shares crop with down/situation box |
| `penaltyagainst` (team name) | 🟡 Calibrated, untested | From penalty accept/decline overlay |
| `banner` (TD/FG/SAFETY ribbon) | 🟡 Calibrated, untested | Full-screen scoring banner |
| Timeout dash marks | 🟡 Estimated, untested | Pixel-brightness heuristic (not text OCR) |
| `pregameready` | ⚪ Not calibrated | Pregame team-intro screen |
| Yard line number | ⚪ NOT BUILT | **Blocker**: YardLine always 0, disables Midfield variants |

### Known OCR Limitations
- **Pause menu blanking**: OCR drops to 0 on pause/replay/cutscene screens → fixed with sticky `_lastKnown` pattern for scores/quarter, but `_lastDistanceRaw` is NOT sticky
- **No yard line**: Midfield position variants gated behind `YardLine > 0` — all dormant until YardLine OCR is built
- **No FG attempt text**: `FieldGoalMissedHelper` must infer misses from possession flip + no score change

---

## 4. MAC PORT (🔴 BLOCKED — 78 BUILD ERRORS)

### What Exists
| Component | File | Status |
|---|---|---|
| Shared engine | `src/Bandroom.Core/` | 🟢 Builds clean, shared with Windows |
| Avalonia app scaffold | `src/Bandroom.Mac/Program.cs` | 🟢 Entry point |
| Mac AudioPlayer | `AudioPlayer.Mac.cs` | 🟢 Uses `afplay` CLI, same API surface |
| OCR bridge stub | `bandroom_ocr_bridge.py` | 🟢 Python Vision framework bridge |
| Platform stubs | `PlatformStubs.Mac.cs` | 🟢 |
| KeyboardHook | `KeyboardHook.Mac.cs` | 🟢 Carbon/CGEvent port |
| GameWatcher Mac | `GameWatcher.Mac.cs` | 🟡 Evaluator router built |
| WebView bridge | `MacWebBridge.cs` | 🔴 **78 ERRORS** — calls methods on MainWindow that don't exist yet |
| MainWindow | `MainWindow.axaml` + `.cs` | 🔴 Missing ~15 methods WebBridge expects |
| ChangelogService ref | `MacWebBridge.cs` | 🔴 Not in scope on Mac side |

### Mac Remaining Work (in priority order)
1. 🔴 **Fix Mac build errors** — finish `MainWindow` stubs for all methods `MacWebBridge` calls: window drag/minimize/maximize/close, profile import/export, changelog, PA/UI click sound, matchup lock, copy-to-all-teams
2. ⚪ **WebView embedding** — embed `wwwroot/index.html` with JS↔C# bridge (Avalonia WebView)
3. ⚪ **Vision OCR** — port `GameWatcher` OCR from WinRT to macOS Vision framework + ScreenCaptureKit
4. ⚪ **Full feature parity** — ConfigStore, TeamColors, profiles, Sparkle auto-updater

---

## 5. 🔴 BUGS — NEED FIXING

### Critical (User-Visible)

| # | Bug | Impact | Location | Fix |
|---|---|---|---|---|
| B1 | **Legacy trigger keys (1st/2nd/3rd/4th Down) permanently dead** | `_useEngineForEvents` is always true, so `OnDownChanged` always returns immediately. No evaluator emits bare `"1st Down"`/`"2nd Down"`/`"3rd Down"`/`"4th Down"` keys. Users with assigned songs to these slots hear NOTHING. App ships dead UI slots with default sounds. | `WebMainForm.cs:962` | Either (a) remove legacy Down entries from `BuildDefault` + migrate, or (b) add compatibility alias in `FireEventForSide` |
| B2 | **`bridge.DuplicateProfile(...)` throws on click** | Right-click context menu item calls a `WebBridge` method that doesn't exist — user-reachable crash | `app.js:1305` → `WebBridge.cs` | Implement `DuplicateProfile` or remove the menu item |
| B3 | **`ui-bot.js` auto-runs in production** | Pops "UI Bot: N critical, N warnings" toast 1.5s after every page load — looks like dev diagnostic in shipped build | `wwwroot/ui-bot.js` | Gate behind debug flag or remove before next push |

### High (Silent Failures / Wrong Behavior)

| # | Bug | Impact | Location | Fix |
|---|---|---|---|---|
| B4 | **Legacy `SideAwareEvents` permanently dead** | `OnRegionChanged`'s touchdown/turnover/pat_good/kickoff fallback gated on `_useEngineForEvents` — permanently dead. No regression in practice (engine covers these exactly), but dead code. | `WebMainForm.cs` | Clean up or remove dead paths |
| B5 | **`_lastDistanceRaw` not sticky** | Can go null during pause menus. Post-resume, TFL/first-down evaluators get incorrect YardsToGo. | `GameWatcher.cs` | Apply sticky pattern: only update on valid parse |
| B6 | **`PlaySoundboardSlot` / `ScanDynastySave` don't exist** | Soundboard UI hidden (unreachable), Dynasty scan has no caller (unreachable). Same class of bug as B2 but not user-triggerable. | `app.js` → `WebBridge.cs` | Implement or remove |

### Medium (Edge Cases)

| # | Bug | Impact | Location | Fix |
|---|---|---|---|---|
| B7 | **`_possession` can be null during event routing** | Defaults to "home" — defensive events for away fire for wrong team temporarily | `WebMainForm.cs:992` | Guard with explicit check |
| B8 | **`OnTackleForLoss` dual fire path** | Legacy + engine both fire TFL — masked by FireCooldown but double-processing | `WebMainForm.cs:844-847` | Gate legacy path (owner explicitly wanted it ungated) |
| B9 | **No `.gitignore` existed** | Two files with live secrets (`admin_token.local.txt`, `google_client_secret.local.txt`) in plaintext at repo root | Root | `.gitignore` added covering `*.local.txt`, `*secret*`, `*token*` — but project isn't a git repo yet |

### Problems Found But NOT Yet Fixed (from STATE_MACHINE_ANALYSIS.md)

| # | Issue | Severity | Status |
|---|---|---|---|
| D1 | TimeoutHelper was level-triggered (fired every tick) | HIGH | 🟢 FIXED (edge-triggered on decrement) |
| D2 | DownFieldPositionHelper Midfield always true | MEDIUM | 🟢 FIXED (gated behind YardLine > 0) |
| D3 | Duplicate DefenseHelper + DownFieldPositionHelper Loss events | HIGH | 🟢 FIXED (Loss variants removed from DownFieldPositionHelper) |
| D4 | BigEventHelper + DefenseHelper 3rd-down ambiguity | MEDIUM | 🟢 FIXED (DefenseHelper skips when NewPossession) |
| D5 | Safety + 2-pt conversion score delta overlap | HIGH | 🟢 FIXED (FieldGoalPATHelper checks possession-side delta) |
| D6 | FieldGoalMissed may never fire | MEDIUM | 🟢 FIXED (uses banner region + possession flip + no score change) |
| D7 | OCR blanking race on non-sticky fields | LOW | 🔴 NOT FIXED |
| D8 | NoPuntReturn comment clarity | COSMETIC | ⚪ Not a bug |
| D9 | Dual TFL fire path | LOW | 🔴 NOT FIXED (owner request) |
| D10 | No "Offense: Fourth Down" event | MEDIUM | 🟢 FIXED (added to OffenseDownHelper) |

---

## 6. 🟢 RECENTLY COMPLETED (Last 48 Hours)

### Audio Features
- **PA Announcer Layer**: Independent second audio track per event (TriggerEntry.PaAudioFile, PaVolume slider, Assign PA button, concurrent playback)
- **Audio Preview skip**: `isPreview` path skips 1s pre-roll + 20s FireCooldown for manual previews
- **Trimmer auto-preview**: Releasing End slider auto-previews last 4 seconds
- **Lead-in whistle**: Gapless playback path (`LeadInEnabled`/`LeadInClipPath`) — off by default, needs owner whistle clip + UI toggle

### Event System
- **ConfigStore.BuildDefault()** now creates all 42+ event slots (was 6)
- **CategoryMap.cs** fixed — Penalty/Timeout categories now mapped
- **42 EventKeys across 16 evaluators** all wired
- **Penalty side routing fixed** — `"Penalty: Offense"` → defense side (was routing wrong due to doesn't-start-with-"Defense" bug)

### UI
- **UI decluttering**: 46 event cards no longer pulse constantly (glow on hover only), tighter padding, PA button fits
- **Marketplace redesign**: 4-column card grid, sort tabs, filter chips, Nexus Mods style
- **Situation cards**: Title ellipsis fix for ~35 more cards showing "Coming Soon" badges

### Infrastructure
- **Dashboard**: `TASK_BOARD.md` + `dashboard.html` + `serve_dashboard.py` (served at `http://localhost:8765`)
- **Cloudflare Workers**: Paid Plan active ($5/mo), unlimited KV writes, marketplace deployed
- **Default song mapper**: 950/1056 files mapped, 62 teams

---

## 7. ⚪ PLANNED — SPRINT 1 (3–4 HRS, 8 ITEMS)

*From `docs/IMPLEMENTATION_PLAN.md` — ordered by owner priority*

| # | Feature | Files to Touch | Status |
|---|---|---|---|
| 1.1 | **Crowd Audio Ducking Slider** — lower game crowd noise when Bandroom track fires | `index.html`, `app.js`, `AudioPlayer.cs` | ⚪ |
| 1.2 | **Test Fire Button on Hover** — hover card → play button previews assigned song | `app.js`, `style.css` | ⚪ |
| 1.3 | **Scorebug Status LED** — GREEN/YELLOW/RED OCR sync indicator | `index.html`, `app.js`, `WebMainForm.cs` | ⚪ |
| 1.4 | **Multi-Track Randomizer Pool** — assign 2-5 songs per trigger, rotate | `TriggerEntry.cs`, `ConfigStore.cs`, `WebMainForm.cs`, `app.js` | ⚪ |
| 1.5 | **Profile Cloning (Home → Away)** — one-click copy song map | `app.js`, `WebBridge.cs`, `MainWindow.axaml.cs` | ⚪ |
| 1.6 | **Global Mute / Panic Button** — big red [STOP ALL] with Escape hotkey | `index.html`, `app.js` | ⚪ |
| 1.7 | **Red Zone Evaluator** — new evaluator for inside-the-20 | NEW `RedZoneHelper.cs`, register in `GameWatcher.cs` | ⚪ |
| 1.8 | **Pick-Six Split** — separate INT vs pick-6 trigger cards | `TurnoverHelper.cs` | ⚪ |

---

## 8. ⚪ PLANNED — SPRINT 2 (6–8 HRS, 11 ITEMS)

| # | Feature | Status |
|---|---|---|
| 2.1 | **Rivalry "Big Game" Multiplier** — auto +3dB for known rivalries | ⚪ |
| 2.2 | **3rd & Short vs 3rd & Long Split** — different songs per yardage | ⚪ |
| 2.3 | **Field Goal Miss / Blocked Kick Stings** — sad brass for misses | ⚪ |
| 2.4 | **4th Quarter Clutch Mode** — auto override when within 7pts <2min | ⚪ |
| 2.5 | **Compact Overlay "HUD Mode"** — floating glass bar over streams | ⚪ |
| 2.6 | **Batch MP3 Drag-and-Drop with Auto-Assign** | ⚪ |
| 2.7 | **DMCA-Safe Filter Toggle** — marketplace filter for royalty-free only | ⚪ |
| 2.8 | **Auto-Sync Marketplace Updates** — notify on updated packs | ⚪ |
| 2.9 | **Super Sim Auto-Mute** — detect super sim in dynasty, silence triggers | ⚪ |
| 2.10 | **First Down Chain Gang Stings** — 3-second brass for short-gain 1st downs | ⚪ |
| 2.11 | **Trending Sounds Row** — "Most Used This Weekend" in marketplace | ⚪ |

---

## 9. ⚪ PLANNED — SPRINT 3 (10–15 HRS, 10 ITEMS)

| # | Feature | Status |
|---|---|---|
| 3.1 | **Dynamic Stadium Size Reverb** — reverb scaled to real stadium capacity | ⚪ |
| 3.2 | **Post-Event Smart Crossfade** — hard-cut crossfade back to game audio | ⚪ |
| 3.3 | **EQ Profiles** — Headset / Home Theater / Bass toggle | ⚪ |
| 3.4 | **Subwoofer / Bass Boost** — low-end EQ punch | ⚪ |
| 3.5 | **WASAPI / ASIO Low-Latency** — Windows-only native audio engine | ⚪ |
| 3.6 | **Stream Deck Plugin** — Elgato integration | ⚪ |
| 3.7 | **Export / Import .BAND File Packs** — one-click bundle sharing | ⚪ |
| 3.8 | **Discord Rich Presence** — live game status in Discord | ⚪ |
| 3.9 | **Multi-Monitor Window Pinning** — always-on-top on secondary displays | ⚪ |
| 3.10 | **System Tray Minimization** — minimize to tray with background execution | ⚪ |

---

## 10. ⚪ PLANNED — SPRINT 4 (MONTH+, 10 BIG FEATURES)

| # | Feature | Status |
|---|---|---|
| 4.1 | **AI Commentary Voice Clone** — record voice → clone via F5-TTS → play-by-play via Ollama LLM | ⚪ |
| 4.2 | **Mobile Remote Controller** — QR code → phone web app triggers sounds | ⚪ |
| 4.3 | **OBS Studio Dock Panel** — embedded dock for streamers | ⚪ |
| 4.4 | **Twitch Chat Trigger Integration** — channel point rewards / !commands fire stings | ⚪ |
| 4.5 | **Discord Bot Relay** — stream stadium music into Discord voice channels | ⚪ |
| 4.6 | **Community Leaderboard** — top downloaded creators | ⚪ |
| 4.7 | **Daily Featured School Showcase** — day-of-year spotlight on university band | ⚪ |
| 4.8 | **Share to TikTok / X Clip Generator** — last 15s clip exporter | ⚪ |
| 4.9 | **Cross-Device Cloud Profile Sync** — auto-sync profiles via marketplace worker | ⚪ |
| 4.10 | **Gamepad Vibration Feedback** — controller vibrates on big plays | ⚪ |

---

## 11. UI REMAINING WORK (~75 ITEMS FROM REDESIGN HANDOFF)

### macOS Design (15 items) — ALL ⚪
Traffic-light window controls, layered glass depth, segmented toolbar, dock spring animation, sheet-style dialogs, vibrancy materials, haptic button feedback, rubber-band scroll, system accent color sync, drag-and-drop song assignment, menu bar applet, Touch Bar, Notification Center, Quick Look preview, Finder-style column browser

### Gamer UI Patterns (13 items) — ALL ⚪
Live HUD overlay, kill-feed event log, streamer mode toggle, achievement rarity tiers, sound visualizer, soundboard favorites bar, FPS/ping status indicator, global hotkey panel, party/group sync mode, clip/replay integration, crosshair cursor styles, match history timeline, season pass UI

### Navigation & Layout (10 items) — ALL ⚪
Collapsible side panels, tabbed right panel, command palette (Ctrl+K), right-click menus, undo system, multi-select teams, pin teams, progress rings, keyboard nav, breadcrumbs

### Profile Dashboard (7 items) — ALL ⚪
Full dashboard page, public profile page, profile banner, activity feed, follow/friend system, leaderboards, QR code sharing

### Tips System (100 tips + delivery) — ALL ⚪

### Dynasty Features (20 items) — ALL ⚪
Dynasty save scanner, journal, season stats cards, schedule timeline, player stats leaderboard, recruiting tracker, coach card, rivalry alerts, top-25 scoreboard in ticker, conference standings, bowl projections, award watch lists, save selector, season-over-season history, auto-load dynasty songs, dynasty stats on profile, recap toasts, milestone alerts, XP bonus, dynasty achievements

### CSS Bugs Remaining (~12) — ALL ⚪
XSS sanitization, double-scrollbar, team picker layout shift, focus-visible outlines, channel status indicator, Google sign-in loading, avatar file size limit, prefers-color-scheme dark/light

### JS Bugs Remaining (~10) — ALL ⚪
Search debounce, lazy loading, team data validation fallback, init() error handling, bridge fallback detection, preview waveform sizing

---

## 12. INFRASTRUCTURE & DEVOPS

| Item | Status |
|---|---|
| Cloudflare Workers Paid Plan | 🟢 Active ($5/mo) |
| Marketplace worker deployed | 🟢 `https://bandroom-marketplace.bandroom.workers.dev` |
| Dashboard server (`serve_dashboard.py`) | 🟡 Running but NOT persistent — dies and needs manual restart |
| Scheduled health check | 🟢 File-based (TASK_BOARD freshness, file presence, build timestamps) |
| `.gitignore` | 🟢 Added (covers secrets, bin/obj, WebView2Data) |
| Git repo | ⚪ Not initialized yet — DO NOT `git init` without `.gitignore` in place |
| Default song pack download | ⚪ 1 GB — download-on-first-launch needed |
| `AudioDuckingController.cs` | 🟡 Fully built but NEVER instantiated — 100% dead code |
| `ReverbProvider.cs` | 🟢 Built, see Sprint 3.1 for dynamic scaling |
| `ProfileSyncService.cs` | 🟢 Built, extendable to team profiles (Sprint 4.9) |

---

## 13. IMMEDIATE NEXT ACTIONS (This Session / Next Session Priority)

### 🔴 BLOCKING — Do First
1. **Rebuild `BandAudioHook.csproj`** — verify 0 errors before trusting any fix is live (`.exe` was running, locking the output DLL at last check)
2. **Fix Mac 78 build errors** — finish `MainWindow` stubs for `MacWebBridge` methods
3. **Fix B1 (dead legacy trigger keys)** — product decision needed: migrate or add compatibility alias

### 🟡 HIGH — Do Next
4. **Verify OCR calibrations live** — scores, clock, flag, penaltyagainst, banner, timeout dashes
5. **Fix B2 (DuplicateProfile crash)** — user-reachable
6. **Fix B3 (ui-bot.js in production)** — gate or remove
7. **Fix B5 (sticky `_lastDistanceRaw`)** — prevent spurious events after pause/resume
8. **Dashboard durability** — auto-restart `serve_dashboard.py` on crash

### 🟢 SPRINT 1 — Start Building
9. **Crowd Audio Ducking** (#1 owner priority)
10. **Test Fire Button** (#2 — makes assigning songs 10x faster)
11. **Scorebug LED** (#3 — instant visual feedback)

---

## 14. KEY FILES REFERENCE

| File | Purpose |
|---|---|
| `c:\Bandroom\TASK_BOARD.md` | **LIVE task board** — always more current than this roadmap |
| `c:\Bandroom\AGENT_NOTES.md` | Persistent agent memory (conventions, known constraints) |
| `c:\Bandroom\docs\IMPLEMENTATION_PLAN.md` | Sprint plans with exact files and lines to touch |
| `c:\Bandroom\docs\STATE_MACHINE_ANALYSIS.md` | Complete state machine audit (771 lines of analysis) |
| `c:\Bandroom\docs\EVENT_KEY_MAP.md` | All 42 EventKeys with evaluator, category, and side routing |
| `c:\Bandroom\docs\Roadmap_A_Committed_Work.md` | What was explicitly asked for (committed, not suggested) |
| `c:\Bandroom\docs\Roadmap_B_75_Suggestions.md` | 75+ suggestion items for future consideration |
| `c:\Bandroom\HANDOFF_UI_REDESIGN_2026-08-08.md` | UI redesign session summary (~20 done, ~75 remaining) |
| `c:\Bandroom\FINAL_HANDOFF_2026-08-07.md` | Engine integration final handoff (all green, audit complete) |
| `c:\Bandroom\dashboard.html` | Orchestrator dashboard → `http://localhost:8765/dashboard.html` |

---

## 15. HOW TO VERIFY

- **Build Windows:** `dotnet build c:\Bandroom\BandAudioHook.csproj`
- **Build Mac:** `dotnet build c:\Bandroom\src\Bandroom.Mac\Bandroom.Mac.csproj`
- **Build Core:** `dotnet build c:\Bandroom\src\Bandroom.Core\Bandroom.Core.csproj`
- **Launch App:** Run `Bandroom.exe` from `bin\Debug\net10.0-windows10.0.19041.0\`
- **Dashboard:** `python c:\Bandroom\serve_dashboard.py` → `http://localhost:8765/dashboard.html`
- **Deploy Marketplace:** `npx wrangler deploy` (from `cloudflare-marketplace/`)

---

> ⚠️ **IMPORTANT**: Everything lives in `C:\Bandroom` — NOT `D:\AGY\Bandroom`. Read `TASK_BOARD.md` before starting any task. Mark 🔴 IN PROGRESS / ✅ DONE with timestamp.