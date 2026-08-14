 prio # Bandroom — Scoreboard Reader Integration Handoff (2026-08-13)

## TL;DR
This session was a **planning/inspection session**, not a code-writing session. The goal was the
"integrate the CFB27 Scoreboard Reader into BANDroom" task. I inspected **both** codebases to
ground truth (not from the original prompt's assumptions) and produced an integration plan.

**Two findings change the prompt's assumptions:**
1. The "Scoreboard Reader" is **not** a native/shareable codebase. It is an **Electron + Node.js**
   app ("Scorebug Overlay App") whose real data source is a **closed-source native RAM reader**
   (`CollegeFB27RamReader.exe`) that reads the game's process memory, plus a tesseract.js screen
   OCR fallback.
2. It writes **`live-scoreboard.json`**, NOT `live-game-data.json` (the prompt/URL assumed the
   wrong filename). The write is atomic (`.tmp` → `rename`).

**Result:** native import (Option 1) is impossible. The correct approach is **Option 2 + Option 3**
— launch the reader as an internal worker and consume `live-scoreboard.json` locally through a
new normalizer, feeding BANDroom's existing (unchanged) Brain/Trigger/Audio. BANDroom's own
`GameWatcher` OCR is kept as an automatic fallback.

> ⚠️ **BLOCKED — needs owner decisions before any code.** See "Open decisions" at the bottom.
> Nothing was modified in `c:\Bandroom` source this session.

---

## 1. What was actually verified (both codebases, read directly)

### BANDroom (target app — C# / .NET 10)
- UI: WinForms + WebView2 (`WebMainForm.cs`, `wwwroot/`). Backend: native C#.
- Data-acquisition layer already exists: **`GameWatcher.cs` (~1,952 lines)**.
  - Capture: `Graphics.CopyFromScreen` (`WinRT`/`Windows.Media.Ocr` OCR + pixel sampling).
    `PrintWindow` is blocked by EA anti-cheat — this is the documented reason BANDroom is
    screen-based.
  - 13 calibrated OCR regions (down, distance, situation, quarter, scores, clock, play clock,
    flag, penalty-against, banner, pregame-ready, team-runout) + pixel sampling for possession,
    timeouts, and field-position arrow.
- Event brain: `PlaySnapshot` → `GameState(Current, Previous, UserIsHome)` → `EventRouter` →
  **26 evaluators** (`src/Bandroom.Core/Helpers/*.cs`) → `TriggerEvent` → `WebMainForm`.
  `FireEventForSide` → `AudioPlayer` (NAudio: fade, reverb, ducking).
- **Known data gap:** `PlaySnapshot.YardLine` is **hardcoded 0** in `GameWatcher.RouteEngineTick`
  — this deliberately disables several midfield/position evaluators (documented in ROADMAP).

### Scoreboard Reader ("Scorebug Overlay App" — Electron / Node.js)
Source obtained from the owner-provided release URL:
`https://github.com/naileditcreativecs/Scorebug-Overlay-App/releases/download/test-build-4-aug13/CFB27-Scoreboard-Overlay-Test-Build-4-Aug13.zip`
(194.6 MB). Extracted to `c:\Bandroom\_scratch_reader\extracted\`.

Key internals (read from `app.asar`):
- **`src/automatic-data-extractor.js`** — the output writer. Confirms:
  - Output files: `live-scoreboard.json`, `live-screen-scoreboard.json`,
    `latest-scoreboard.json`, `scoreboard.csv`, `scoreboard.jsonl`, `events.jsonl`,
    `screen-text.jsonl` — inside a folder-local `UserData\` dir (because a `portable-data.json`
    marker sits next to the exe), else `%APPDATA%\…`.
  - **Atomic write:** `fs.writeFileSync(tmp)` then `fs.renameSync(tmp, final)`.
  - **Dedup:** writes only when the state key changes (not a fixed 10 Hz poll — better for CPU).
  - Emits `score-change` events with `likelyType` = `touchdown-candidate` / `field-goal-candidate`
    / `conversion-candidate`.
- **`src/transient-json-reader.js`** — the reader side of the bridge: retries + 750 ms grace, so a
  consumer never sees a half-written JSON.
- **`src/scoreboard-data-source.js`** — source modes `auto` / `ram` / `screen`.
- **`src/user-data-location.js`** — portable (`UserData\`) vs app-data path resolution.
- **`src/recognition/ocr-worker.js`** (tesseract.js) + **`recognition/window_probe.py`** (user32
  window enumeration) + **`ram-reader/CollegeFB27RamReader.exe`** (closed-source, 280 KB).
- `TESTER INSTRUCTIONS.txt` states the RAM reader reads the game's process memory, read-only, and:
  **"Use it only in offline/modded play."**

### `live-scoreboard.json` schema (what BANDroom must consume)
```
away / home { rank, name, nickname, record, color, score, timeouts, possession(bool) }
game        { down(int), distance(int|text), downDistance("3rd & 7"), quarter("3rd"|"OT"),
              clock("m:ss"), playClock(int), ballOn, status, possession("away"|"home"|"none") }
meta        { source, visible, confidence, updatedAt, ramUpdatedAt }
```

---

## 2. Recommended architecture (grounded in the two codebases)

```
  Scorebug Reader (worker)                       BANDroom
  ┌────────────────────────┐
  │ RAM reader (memory)    │─┐
  │ tesseract.js (OCR)     │─┼─► live-scoreboard.json ─► GameStateNormalizer
  │ screen classification  │─┘     (atomic)                  │
  └────────────────────────┘                            ▼
                                                    PlaySnapshot (sticky)
                                                            │
  GameWatcher.cs (OCR) ───────────────────────────┐        ▼
      (kept as AUTOMATIC FALLBACK)                ├──► GameState(Current, Previous)
                                                   │         │
                                                   │         ▼
                                                   │   EventRouter (26 evaluators)  ← unchanged
                                                   │         │
                                                   │         ▼
                                                   └──► FireEventForSide → AudioPlayer ← unchanged
```
One normalized `GameState` → one Brain. No duplicate OCR/ram detection; Trigger/Audio never touch
OCR or the reader — they only see `PlaySnapshot` (satisfies "keep Trigger Engine independent").

### Field mapping (Reader → PlaySnapshot)
| Reader field | BANDroom field | New to BANDroom? |
|---|---|---|
| `game.ballOn` | `YardLine` | ✅ **unblocks the currently-dormant yard-line logic** |
| `away/home.name` | `HomeTeamName`/`AwayTeamName` | ✅ identity/rivalry/ranked logic |
| `away/home.rank`, `.record` | new | ✅ auto Big Game / upset / matchup quality |
| `away/home.color` | team color | ✅ one color source of truth |
| `game.down` / `distance` / `downDistance` | `Down` / `YardsToGo` | ✅ more reliable than OCR |
| `away/home.score` | `HomeScore`/`AwayScore` | ✅ exact; kills pause-menu blank-to-0 phantom deltas |
| `game.clock` | `TimeRemainingSeconds` | ✅ continuous |
| `game.playClock` | `IsPlayClockCounting` | ✅ clean snap signal |
| `away/home.timeouts` | timeout counts | ✅ replaces pixel-sampling heuristic |
| `game.possession` / `*.possession` | `PossessionAway` | ✅ replaces color-guess; kills wrong-side routing class |

---

## 3. Phased implementation plan (smallest safe changes)

- **Phase 0 — Baseline:** build `BandAudioHook.csproj` + `src/Bandroom.Core` + `.Tests`; record
  that it's green (board claims 0/0, 64/64). Proves "didn't break it."
- **Phase 1 — pure additive data layer:**
  1. `ScoreboardReaderState.cs` (DTO mirroring `live-scoreboard.json`).
  2. `GameStateNormalizer.cs` → `PlaySnapshot`, with **sticky last-known-valid** caching (same
     discipline `GameWatcher` already uses) so a stale/blank file never fabricates a delta.
  3. `ScoreboardJsonReader.cs` — atomic-safe read, poll ~100–250 ms with rename/grace tolerance,
     resolve the `UserData` path, report `CONNECTED` / `WAITING FOR GAME DATA` / `ERROR`.
- **Phase 2 — one brain, two sources:** feed both normalizer + OCR snapshots into the same
  `RouteEngineTick`/`EventRouter`; OCR is the fallback. No evaluator changes.
- **Phase 3 — UI + lifecycle:** "GAME DATA" status chip in `wwwroot` (+ `WebBridge` method);
  optional `ScoreboardReaderHost.cs` (launch/monitor/stop child, kill on exit — **default off**
  pending safety decision).
- **Phase 4 — events + tests:** verify at runtime if `game.status` carries `KICKOFF`/`TOUCHDOWN`;
  if yes, corroborate scoring via the reader's `score-change` deltas; if not (recommended default),
  use the reader **only** for numeric state + possession + colors and keep BANDroom's OCR
  banner/situation flags. Add unit tests for the normalizer + JSON reader.

---

## 4. Color treatment (from prior owner notes this session)
- BANDroom's brightest weakness is **pixel-color possession sampling**
  (`SamplePossessionFromWindow`/`SamplePossessionByUnderline` → `ResolveTeamColor`), which carries
  many guards for FLAG-yellow ≈ Tennessee-orange, replays, celebration frames, wrong-side routing.
- The Reader is **identity-based** (explicit `possession` + per-team `color`/`name`), so the plan
  makes `game.possession` **primary** and demotes the pixel path to fallback. The Reader's
  `color` becomes the team-color source of truth BANDroom themes off.

---

## 5. What the combination unlocks (mutual value, for the pitch)
- **BANDroom gains:** `YardLine` (red-zone/midfield/field-position logic live), reliable scores,
  exact possession, team identity/ranks/records (auto Big Game, rivalry, upset), play-clock snap
  signal, fewer false triggers.
- **Reader gains:** a real **reaction layer** (brain + trigger + audio + UI + marketplace) it
  doesn't have, turning a reader+overlay into a full in-stadium entertainment product.
- Together = **one pipeline:** Reader (sense) → Normalizer (normalize) → Brain (understand) →
  Trigger (decide) → Audio (react). No duplication.

---

## 6. Tech to explore (correlated to what two codebases actually use)
1. **ONNX Runtime / WinML digit recognition** — tiny CNN for `{score,clock,down,ballOn}`; faster +
   more accurate than OCR for digits; both .NET and Node run ONNX. Biggest future upgrade.
2. **Memory-mapped file + named event IPC** — zero-copy successor to file polling (prompt's
   "shared memory"); supported by .NET and Node.
3. **Local LLM play-by-play (Ollama)** — repo already has `docs/Bandroom_AI_Commentary_Research.md`
   + Roadmap 4.1; the normalized `GameState` + Reader's screen-text classification is the perfect
   structured input.
4. **Semantic Kernel / function-calling** — deterministic, grounded event descriptions before LLM.
5. **Windows.Graphics.Capture** — modern `CopyFromScreen` successor; a fallback lane.
6. **WASAPI/ASIO + unified FFmpeg** — Reader ships `ffmpeg.dll`; shared decode/playback chain.
7. **.NET NativeAOT** — tiny self-contained native bridge binary.
8. **Named pipes** — .NET `System.IO.Pipes` ↔ Node `net` for push events.
9. **Apple Vision / ScreenCaptureKit** — unify the Mac reader path (`bandroom_ocr_bridge.py`).
10. **Code signing / AV strategy** — the RAM reader reads another process; signing matters for
    "trojan" false positives.

---

## 7. OPEN DECISIONS — do not write code until resolved
1. **RAM-reader vs screen-only (SAFETY).** Reader's own instructions: RAM memory read = "offline/
   modded play only." BANDroom deliberately went screen-based to avoid EA anti-cheat. Recommend
   **screen-only primary**, RAM behind a user toggle.
2. **Auto-launch** the Reader from BANDroom, or run it manually?
3. **Reader scope:** numeric state + possession + colors **only** (recommended), or also drive event
   flags from `game.status`/screen classification?

---

## 8. File/artifact state (end of session)
- No source changes in `c:\Bandroom`.
- Inspection artifacts live under `c:\Bandroom\_scratch_reader\`:
  - `extracted\` — full unpacked reader (194 MB), `resources/app.asar`, `app.asar.unpacked`.
  - `extracted\_src\` — decompiled JS of interest (`automatic-data-extractor`, `claude-dc-bridge`,
    `transient-json-reader`, `scoreboard-data-source`, `user-data-location`, `read-region`).
  - `extract_asar.js` — the asar extraction helper (working, auto-corrects the header offset).
- The `_scratch_reader\` folder + `reader.zip` are **scratch** — delete before any real release.

## 9. How to reproduce the inspection
```
curl -L -o r3.zip <release url> && tar -xf r3.zip -C extracted
node extract_asar.js   # dumps wanted JS from app.asar into extracted\_src\