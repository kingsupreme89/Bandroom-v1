# Bandroom Handoff — Session 38 (2026-08-11)

Picks up directly after Session 37 (CSS comment bug fix, state-machine discrepancies #11/#12
fixed). This session answered two owner questions and did one implementation task: (1) what events
still need reference screenshots and are CBS/regional scorebug wired up properly, (2) bring the CBS
and CollegeFootball27 scorebug presets to parity on the "new logic" (per-preset region overrides).
Touched `ScorebugPreset.cs`, `GameWatcher.cs`. **Nothing from this session is committed.**

## 1. Screenshot/CBS/regional research (no code change, informational)

Owner asked what events still need screenshots and whether CBS and "regional" scorebug support are
wired up properly.

- **Screenshots still needed**: `flag` (penalty banner) and `banner` (TOUCHDOWN/FG/SAFETY ribbon)
  OCR regions are flagged in `docs/Bandroom_Trigger_Event_List.md` as uncalibrated. All 33
  assignable sound events otherwise work. `ScorebugPreset.cs` also flags several crops as
  "estimated, needs live tuning" (see item 2 below for the ones fixed this session).
- **CBS**: fully wired, not a stub — `KamsCbsScorebugV3` is the default fallback preset and central
  to the app.
- **"Regional" scorebug**: does not exist anywhere in the codebase (zero grep hits, code or docs).
  The only presets are `KamsCbsScorebugV3`, `CollegeFootball27`, `CollegeFootball26Console`. Flagged
  back to the owner as possibly a different name for one of those, or simply not started — **still
  unresolved, needs owner clarification**.

## 2. CBS vs CollegeFootball27 preset parity — fixed 2 real gaps

Owner asked to make sure both scorebugs are up to date the same way with the "new logic" (the
Session 28 per-preset region override system). Investigation found two real gaps, scoped to CBS +
CollegeFootball27 only (owner explicitly excluded CollegeFootball26Console from this pass):

**Gap A — CBS relied on implicit class defaults.** `KamsCbsScorebugV3` never set
`AwayScoreFxX/W`/`HomeScoreFxX/W`/`ClockFxX/W` itself; it silently inherited them from
`ScorebugPreset`'s field defaults (which happen to equal CBS's numbers, since that's what they were
originally calibrated from). Functionally correct today, but an implicit coupling — if anyone
changes the class defaults later (e.g. to make CFB27 the fallback), CBS breaks silently with no
compiler signal. **Fixed**: `KamsCbsScorebugV3` (`ScorebugPreset.cs`) now sets these fields
explicitly to the same values.

**Gap B — `penaltyagainst` and `banner` were never part of the per-preset system at all.** Unlike
`awayscore`/`homescore`/`clock` (promoted to per-preset fields in Session 28), these two regions'
crop coordinates were hardcoded once in `GameWatcher.cs`'s `_regions` initializer, calibrated only
from a CBS screenshot, and `ApplyScorebugPreset` never touched them. So switching the active preset
to CollegeFootball27 had **zero effect** on penalty-overlay or scoring-banner detection — it kept
reading CBS's screen coordinates regardless of which broadcast skin was actually selected. This was
a real functional gap, not just doc staleness.

Fixed:
- Added `PenaltyAgainstFxX/Y/W/H` and `BannerFxX/Y/W/H` fields to `ScorebugPreset` (`ScorebugPreset.cs`).
- `KamsCbsScorebugV3` set explicitly to the original hardcoded CBS values (no behavior change for CBS).
- `CollegeFootball27` set to the **same** CBS values as an explicit placeholder — no CFB27 screenshot
  showing either overlay has been seen yet, so cloning CBS's numbers with a flagged comment is more
  honest than inventing new coordinates with no basis (same convention `CollegeFootball26Console`
  already uses for its own unverified crops).
- `ApplyScorebugPreset` (`GameWatcher.cs`) extended to reposition `penaltyagainst` and `banner` from
  the active preset's new fields, same pattern as the existing `down`/`situation`/`quarter`/`flag`/
  score/clock handling.

`pregameready` was deliberately left untouched — its crop is `0/0/0/0` (an explicit "uncalibrated,
skip entirely" placeholder per its own doc comment), identical for every preset today, so there's no
CBS-vs-CFB27 asymmetry to fix there; wiring it into `ApplyScorebugPreset` now would just mean
guessing coordinates neither preset actually has data for.

`pregameready` and `EventGatedRegions` were also spot-checked against Session 37's #13 finding (5
entries including `penaltyagainst`, confirmed still correct) — no relation to this session's changes,
just confirming nothing regressed.

## Verified this session
- `dotnet build BandAudioHook.csproj` clean (0 errors; only the expected exe-file-locked-by-running-
  process warnings, since Bandroom.exe was running during the build). `dotnet build Bandroom.sln`
  fails, but only on the unrelated `Bandroom.Mac` project (`CloudDatabaseService`/`AudioCache`/
  `IntakeEngine` not found — pre-existing, not touched this session, not investigated further).
- Confirmed `ActivePreset` is always explicitly set at startup (`WebMainForm.cs:127`) before
  `Start()`, so `ApplyScorebugPreset` always runs and the pre-preset hardcoded defaults baked into
  `_regions`' initializer never actually matter in practice.

## Not yet confirmed — real next steps
1. **Live game verification of Gap B fix** — switching to CollegeFootball27 and confirming a real
   penalty overlay / scoring banner is read from the right screen position has not been done (no CFB
   27 screenshot of either overlay exists yet). CFB27's `PenaltyAgainstFx*`/`BannerFx*` are currently
   just a cloned-from-CBS placeholder, same caveat as its `AwayTimeoutFx*`/underline crops.
2. **"Regional" scorebug** — still unresolved ambiguity, needs the owner to clarify what this refers
   to (see item 1 above).
3. Everything carried forward from Session 37 below is still untouched.

## Carried forward from Session 37/36/35/34/33 (untouched this session)
1. Volume persistence round-trip — not yet confirmed (Session 36 item 5).
2. Clipper song-list fixes + Add Songs button — not yet live-verified (Session 36 item 6).
3. Sound Booth meters/Preview against a real game event — not yet live-verified (Session 33/34).
4. Conflict-prompt-before-autosave feature — not scoped, needs owner alignment (Session 34).
5. `voice_poc/.env` — still untracked, uncommitted, not gitignored; likely holds a secret.
6. **Not released** — commits sit on `master` past `v1.0.73` with no version bump/tag/Squirrel pack.
7. 3 deleted `guide/` files (Session 32) — still unexplained, still not touched.
8. Coverflow edge-fade mask + `.team-swatch-reflection` DOM wiring — CSS in place, JS side never
   wired up.
9. Live-game verification of Session 37's state-machine fixes (#11/#12) and CSS fix — still only
   verified via CDP/synthetic snapshots, not a real captured game.
