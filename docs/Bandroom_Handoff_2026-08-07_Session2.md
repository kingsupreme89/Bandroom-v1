# Bandroom Handoff — August 7, 2026 @ 19:28 MT (Session 2)

Picks up from `Bandroom_Handoff_2026-08-07_Session1.md`. That doc covered the initial engine
integration + Mac bootstrap. This one covers a much longer second push: a new feature, a UI
pass, a real security fix, and a full round of live OCR calibration from actual screenshots
the user captured mid-game (Auburn @ Georgia Tech).

## Build Status (verified by direct rebuild, not self-reported)
```
Bandroom.Core.dll    → 0 errors, 0 warnings
Bandroom.dll (Win)   → 0 errors, 0 warnings   (BandAudioHook.csproj, repo root)
Bandroom.Mac.dll     → 🔴 BROKEN as of this session — see "Mac" section below
```

Live source of truth for task-by-task status is `TASK_BOARD.md` — this doc is a narrative
summary, that file has the granular log (Auditor Findings section especially).

---

## 1. New feature: PA Announcer layer

A second, independent audio slot per situation — an announcer clip that plays *alongside* the
main song, not instead of it, with its own volume control. Explicit user request.

- `TriggerEntry.PaAudioFile` — new field, backward-compatible (old saved profiles just get `""`
  on load, no migration needed).
- `AudioPlayer.PaVolume` — independent of Master/Home/Away.
- `AssignTrackForm` was hardcoded to read/write `entry.AudioFile` everywhere — refactored to take
  an explicit `currentPath`/`dialogTitle` so the same dialog now assigns either the main song or
  the PA clip, selected by the caller.
- `WebMainForm.OpenAssignTrack(entry, isPa)` — the `isPa` bool decides which field gets read/
  written (assign, clear, and trim all branch on it).
- New bridge methods: `WebBridge.AssignPaEvent`, `SetPaVolume`/`GetPaVolume`.
- New UI: "Assign PA" button per event card, a "PA: \<filename\>" line, and a new PA Volume
  slider in the Adjust panel (`wwwroot/index.html` + `app.js`).
- `WebMainForm.FireEvent()` fires the PA clip via a second `AudioPlayer.Play()` call, **after**
  the main clip and with `interruptPrevious: false` — order matters here, since the main clip's
  `interruptPrevious: true` calls `StopAll()`, which would kill the PA clip if fired first.

**Not yet live-tested in the running app** — build-verified only. Someone should actually assign
a PA clip to an event and confirm it plays alongside the main song.

## 2. UI decluttering pass

Explicit user feedback: the "island tabs" event-card grid felt cluttered. Real, scoped fix (not
a full redesign, which would need live visual iteration this session can't do):

- Every one of the (now 46) event cards used to pulse its outline glow constantly, forever
  (`situation-row-outline-pulse`, infinite animation on every card simultaneously). Now static/
  quiet at rest, glow only on hover.
- Tightened card padding/gap/max-width, shrank button padding/font so the new 4th button
  (Assign PA) fits without overflowing.
- Fixed a real regression this surfaced: once ~35 more cards started showing the "Coming Soon"
  badge (see ConfigStore fix below), the event name text was rendering completely blank on many
  cards — the raw text node had no `min-width: 0` to shrink against the badge. Wrapped it in its
  own `.situation-name-text` flex item with proper ellipsis truncation.

## 3. Bug: ~24 of ~41 events had no assignable UI slot at all

The biggest one found this session. `ConfigStore.BuildDefault()` only ever created
`TriggerEntry` rows for 6 of the ~41 EventKeys the 16 evaluators can actually emit (the
`autoDetected` dict), plus 4 legacy down-entries whose `Event` names didn't even match the new
"Category: Name" format. `GetEvents`/`GetEventsForCategory` only ever show what's in `_config`
— no merge against a canonical list, and `LoadProfile`/`LoadOrCreate` just deserialize whatever's
on disk with no reconciliation either.

**Net effect (before the fix): ~24 events the engine could detect — Second/Third/Fourth Down
variants, Field Goal Made, Safety, both Penalty events, all 5 Timeout variants, Victory in Hand,
Iced Game, Drive Starter, 2nd Quarter start, most Kickoff variants, 2-Point Conversion — had no
row in any profile, never showed in the assignment UI, and could never have a song assigned.**
This was independent of and arguably bigger than the known OCR-calibration gap, since it broke
events whose detection logic already worked.

**Fix:** `ConfigStore.AllEngineEventKeys` (the canonical list) + `EnsureAllEvents()`, wired into
`BuildDefault()`, `LoadOrCreate()`, and `LoadProfile()` so every load path backfills missing
event slots without touching existing assignments. Also fixed `CategoryMap.cs`: removed a dead
entry, added Penalty/Timeout category mappings (they'd have shown as "Hype" otherwise).

**Confirmed live in-app** (user checked): category tab counts went from ~11 events to 46 total
(23 Downs / 6 Scoring / 2 Turnovers / 5 Special Teams / 2 Penalties / 7 Hype — sums to 45, the
extra one being a legacy entry), matching the canonical list.

## 4. Security: no `.gitignore`, two files with live secrets in plaintext

`admin_token.local.txt` and `google_client_secret.local.txt` (a real Google OAuth client secret)
sit at repo root with **zero** `.gitignore` protection. This project isn't a git repo yet, but
it's been discussed as something to set up properly, and other docs in this repo reference it
already being on GitHub elsewhere. Added `.gitignore` (`*.local.txt`, `*secret*`, `*token*`,
plus bin/obj/WebView2Data/crash.log) before this becomes a real leak. **Do not `git init` this
project without confirming `.gitignore` is in place and actually working first.**

## 5. Mac build — currently broken, not by anything in this session

Two separate issues surfaced:

1. `ReverbProvider.cs` (NAudio-dependent, Windows-only) had been added to
   `Bandroom.Mac.csproj`'s "shared, no Windows deps" compile list under a comment that didn't
   hold for this specific file. **Fixed** — pulled it back out.
2. After that fix, a much bigger wave of errors (78) surfaced in `MacWebBridge.cs` — it calls
   ~15 methods on `MainWindow` (window drag/minimize/maximize/close, profile import/export,
   changelog, matchup lock, copy-to-all-teams) that `MainWindow` doesn't implement yet. This is
   very likely Cline's in-progress WebView bridge work (matches the board's Priority 2 item #1),
   caught mid-edit, not a regression from anything in this session. **Not fixed** — this needs
   `MainWindow`'s missing methods finished by whoever's actively driving that file; blind-
   implementing 15 methods without a Mac to test against isn't responsible.

Also worth knowing: **the Bandroom repo has never been copied onto an actual Mac.** All Mac work
so far has been cross-compiled from Windows. Real testing needs the code physically on a Mac.

## 6. OCR calibration — the big one, from live screenshots

The user played a live game (Auburn @ Georgia Tech, CBS broadcast skin, 1920x1080) and sent
screenshots covering most of the situations needed. Every region below is now calibrated and
build-verified, but **all coordinates are visually estimated from screenshots, not pixel-
measured** — treat every one as a real starting point that will likely need small live-tuning,
not as exact.

| Region | Status | How |
|---|---|---|
| `awayscore` / `homescore` | ✅ Calibrated | Tight positional crop (bare digits have no unique regex signature) |
| `clock` | ✅ Calibrated | Same approach, feeds `TimeRemainingSeconds` |
| `flag` | ✅ Calibrated | Turned out to share the exact same crop as down/situation/quarter |
| `penaltyagainst` + side resolution | ✅ Calibrated + wired | New region reads "Against \<Team\>" text from the penalty decision overlay; new `GameWatcher.HomeTeamName`/`AwayTeamName` (set in `SetGameTeamsFromWeb`) let `RouteEngineTick` resolve `IsPenaltyOnOffense`/`IsPenaltyOnDefense` |
| Timeouts remaining | ✅ Calibrated + wired | **Not OCR** — `Windows.Media.Ocr` can't read graphical dash marks. New `GameWatcher.SampleTimeoutSegments()` does pixel-brightness sampling instead (3 segments, luminance ≥128 = "lit"), same technique family as the existing possession-color sampler. User confirmed ground truth (away=3 lit, home=0 lit) from a screenshot. Higher uncertainty than the text regions — untested threshold. |
| `banner` (TOUCHDOWN/FIELD GOAL/SAFETY) | ✅ Calibrated | From a live TOUCHDOWN screenshot |
| YardLine | ⬜ Still 0 | Noted: the "KICKER RANGE" overlay shows literal "TARGET LINE: 40 YARD LINE" text — a much better OCR source than the tiny persistent-bug number, but only appears situationally (4th down/FG decisions), not every play. Worth building as a supplemental source later. |

**Bug caught by this work, not something that existed before:** `WebMainForm.cs` routed events
by `EventKey.StartsWith("Defense:")`, but `PenaltyHelper`'s own comments say an offense penalty
should fire for the *defense* side (celebrating the opponent's mistake). Since `"Penalty:
Offense"` doesn't start with `"Defense:"`, it was silently routing to the wrong side. Moot
before this session (nothing ever populated the penalty flags), live-relevant now. **Fixed.**

## What's next (in priority order)

1. **Live-test everything from tonight** — nothing in this doc has been run in the actual game
   yet, only build-verified. PA Announcer, the 46-event fix, and all the new OCR regions need a
   real play session to confirm they work as intended, especially the timeout pixel-sampling
   (highest uncertainty) and the score/clock/penalty/banner crop boxes (likely need minor
   tightening).
2. **Finish `MainWindow`'s missing methods** so `Bandroom.Mac.dll` builds again — currently
   broken, blocking any further Mac work.
3. **Get the repo onto an actual Mac** — still hasn't happened; all Mac work has been blind
   cross-compilation from Windows so far.
4. **YardLine** — still hardcoded to 0. The "KICKER RANGE" overlay is a promising lead for a
   supplemental source.
5. Lower priority, noted but not started: unit tests for `PlayDelta`/evaluators, `AudioDuckingController`
   is fully built but never instantiated anywhere (dead code, needs a product decision on when
   ducking should trigger before it's worth wiring in).

## Conventions (unchanged from Session 1)
- Everything lives in `C:\Bandroom` — not `D:\AGY\Bandroom`.
- Read `TASK_BOARD.md` before starting anything; it has the live, granular log.
- The "Standing process" section on the board (added this session) is the audit checklist:
  rebuild directly, read the actual diff, check one level past the change, check for
  regressions, record mismatches. There's also now a `.claude/skills/deep-audit/SKILL.md` that
  encodes the same checklist as an invokable skill.
