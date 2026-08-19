# Bandroom Handoff — August 19, 2026 — Session 95

Owner was live in a real game the entire session (same as Session 94) — every fix here was found
and pressure-tested against real, in-progress gameplay. This session had two threads: building a
brand-new custom scorebug overlay system from scratch, and a long chain of live event-attribution
bugs discovered while testing it. **Supersedes Session 94's possession-inversion fix** — see below.

## New Feature: Custom HTML Scorebug Overlays

Owner wanted to bring in their own scorebug HTML files (not just Coffee's bundled themes) and see
them render with live game data. Built out:

- **`ScorebugOverlayForm`'s theme-library system now actually has content.** It existed in code
  (`ResolveActiveScorebugThemeFile`, `GetScorebugThemeGalleryFromWeb`) but `Assets\ScoreboardReader\
  theme-library\library.json` shipped with `"themes": []` — nothing was ever in it. Installed 11
  themes total: the owner's own `ESPN 25 V3` (custom-authored HTML they provided), 4 more from a
  batch zip they sent (`CFB27 ESPN 2007 V2`, `ESPN 2020 V6`, `FOX V7`, `CW Scorebug`, `ESPN 2013`),
  and the 5 *real* canonical Coffee's Corner themes (`ESPN 2020`, `NBC 2024`, `NBC 2024 Monochrome`,
  `FOX 2021`, `FOX 2025`) pulled directly from a `D:\CFB27-Scoreboard-Overlay-v1.4.60` install the
  owner had on disk — these replace whatever was bundled before (which was empty). **3 of the 11
  (`CW Scorebug`, `ESPN 2013`, `FOX 2021`) have no live-data bridge at all** (no `window.
  updateScorebug` or equivalent) and are labeled "(static preview only, no live data)" in the
  gallery name so that's visible without guessing. 2 of those 3 also load React from `unpkg.com` —
  need live internet to render at all.
- **Chroma-key spill suppression** (`ScorebugOverlayForm.ApplyChromaKey`): the existing magenta
  chroma-key only adjusted alpha, never removed the magenta tint baked into antialiased edge
  pixels' RGB by the browser's own rendering — produced a visible pink/magenta halo around every
  bug. Since the backdrop color is a known constant, the blend is exactly invertible (`real =
  (observed - (1-alpha)*magenta) / alpha`). Fixed for every theme at the pixel level, not per-file
  CSS surgery.
- **Numeric `PlayClock`** added to `PlaySnapshot`/`ReaderNumericSnapshot` — existed validated in
  `RamReaderValidator` but was discarded before ever reaching a snapshot; overlay themes' play-clock
  field was permanently stuck on its static default. Wired through and into
  `BuildScorebugOverlayPayloadJson`.
- **Scorebug skin picker UI restored** — Coffee's Corner's gallery UI was fully removed 2026-08-14
  (only backend plumbing survived), so there was no way to pick a skin at all. Added a second pill
  (`#scorebug-skin-switcher`, `loadScorebugSkinSwitcher`/`cycleScorebugSkin` in `app.js`) on the
  matchup screen, next to the existing OCR-layout switcher pill. Takes effect on next Start
  Watching (`RefreshForCurrentSkin` runs right before `Show()`), not live mid-game.
- **Scorebug overlay disabled in dev builds** (`WebMainForm.ShowScorebugOverlay`, `#if DEBUG` early
  return) — owner didn't want it popping up while testing other things; Release builds unaffected.

## Root Cause Found (and Session 94's Fix Reverted): RAM Reader's Own Possession Bit Was Trustworthy — OCR Wasn't

Session 94 added a one-time orientation check: on first disagreement between RAM and OCR
possession, assume RAM is inverted and flip it for the rest of the game, treating OCR as the
trusted tie-breaker. **This was backwards.** Confirmed live, repeatedly, all session: every single
time RAM and OCR disagreed on possession, RAM was right and OCR was wrong — first downs, tackles
for loss, penalties, and a fumble-recovery TD were all misattributed by trusting OCR's correction.
`ScorebugPreset.CollegeFootball27`'s own doc comments already flagged this preset's possession
color-crop as unconfirmed ("only 2 data points... confirm this still flips correctly").

Tried tightening the confirmation window first (`RamPossessionOrientationConfirmTicks = 3`,
requiring 3 consecutive agreeing comparisons instead of 1) — didn't help, because OCR was wrong
*the entire session*, so 3 consecutive comparisons against consistently-wrong OCR just reached the
same wrong conclusion 3 ticks later.

**Final fix** (`GameWatcher.cs`): removed the entire OCR-based orientation-inversion mechanism.
RAM's raw possession bit is now trusted directly and unconditionally whenever it resolves —
`_ramPossessionInverted`/`_ramPossessionOrientationChecked`/-`Streak*` fields left in place as dead
code (harmless, nothing sets them anymore) rather than torn out mid-session. Also removed the
separate OCR-overrides-stable-RAM fallback specifically for possession (the stale-field watchdog
block still applies to down/distance/scores, just not possession — that path is now log-only).

### Follow-up bug this created, also fixed: false "After Punt" mid-drive

Removing the OCR correction also removed the only debounce that happened to sit on the possession
path — RAM's raw bit started flowing into `PlaySnapshot.PossessionAway` completely unsmoothed, and
`PlayDelta.NewPossession` is a bare `Previous != Current` comparison with zero debounce of its own.
A single stray RAM tick read as an instant turnover, firing `NewPossession`-gated events like
"Defense: After Punt" seconds after a real, ordinary earned first down within the same drive. Added
a 2-consecutive-tick confirmation requirement mirroring the existing `ConfirmPossessionFlip` OCR
debounce, but purely RAM-vs-its-own-previous-tick (`_lastConfirmedRamPossessionAway`/
`_pendingRamPossessionAway`/`_pendingRamPossessionTicks`) — can't reintroduce the OCR-corruption bug
since OCR isn't involved.

## Fixed: RAM Reader Executable Was Stale

`Assets\ScoreboardReader\CollegeFB27RamReader.exe` was a 180KB build from Aug 14. Swapped in the
324KB current build from the owner's `D:\CFB27-Scoreboard-Overlay-v1.4.60` install. That install's
own `DATA-API.txt` explicitly warns stale readers break rank/record/**timeout**/possession reading
after a game patch while scores/clock/downs keep working — matches the exact symptom chased most of
the session (timeouts stuck at 0 all game while everything else updated).

## Fixed: Several Events With No Flicker/Debounce Guard At All

Backgrounded a full audit of all 26 `Helpers/*.cs` files for this exact bug class (level-triggered
or edge-triggered-off-raw-OCR-flag conditions with no persistent guard) after finding the first
instance live. Confirmed and fixed:

- **`GameStateEventHelper.cs`** — "Start of 2nd/4th Quarter" fired dozens of times over ~5-7s
  intervals for a whole quarter (Quarter flickering 1↔2 on screen, no fire-once guard unlike the
  file's own `_didFirePregame`/`_didFireVictoryInHand`). Added `_didFireStart2ndQuarter`/
  `_didFireStart4thQuarter`.
- **`TouchdownHelper.cs`** — offense-TD banner edge (`IsTouchdown`, raw OCR flag) could re-fire if a
  single false OCR tick interrupted the real banner. Added `_lastOffenseTdHomeScore`/
  `_lastOffenseTdAwayScore` (mirrors the existing defense-TD guard) so a repeat edge for an
  already-credited score is a no-op.
- **`RunOutHelper.cs`** — "Other: Pregame Tunnel" (flag/title-card edge) had no fire-once guard at
  all. Added `_didFire`.
- **`PenaltyHelper.cs`** — offense/defense penalty edges had no debounce; a single false OCR tick
  mid-flag-display would re-fire. Added a 2-consecutive-tick "not shown" streak requirement before
  clearing the already-fired guard (`NotShownStreakToClear`), same shape as `KickoffHelper`'s own
  not-shown streak.
- Audited and confirmed safe (already properly guarded or edge-triggered off a protected field):
  `KickoffHelper`, `TimeoutHelper`, `SafetyHelper`, `FieldGoalPATHelper`, `FieldGoalMissedHelper`,
  `DriveStarterHelper`, `OffenseAfterPuntHelper`, `OffenseAfterOpeningKickHelper`,
  `DefenseFirstDownHelper`, `TflHelper`, `DefenseHelper`, `DefenseThirdDownHelper`,
  `DefenseSecondDownShortHelper`, `OffenseDownHelper`, `OffenseFourthDownHelper`, `FirstDownHelper`,
  `ThirdDownConversionHelper`, `FirstDownOnFirstDownHelper`, `TurnoverHelper`, `BigEventHelper`,
  `PregameHelper`.

**Known side effect, not a bug**: restarting Watching mid-game resets every one-shot flag above
(needed for the possession fix to take effect), which also re-arms "Opening Kickoff"/"Pregame Take
the Field" — the next kickoff after any restart will look like a fresh opening kickoff once. Owner
hit this twice tonight after being asked to restart watching to pick up fixes. One-time artifact per
restart, not persistent. True "is this a restart or a real new game" detection would be a separate,
larger feature if wanted.

## Fixed: RAM Play-Clock Never Reached the Overlay/Helpers

`PlaySnapshot.IsPlayClockCounting` was 100% OCR-derived (`playClockRegion?.Last != null`) with no
RAM backing — `FirstDownOnFirstDownHelper` is entirely dependent on that one flag toggling to find
its play-boundary edges, so on a preset whose OCR play-clock crop isn't reliable, that helper
silently never fires. Added a RAM-derived signal using the reader's own freshness data (`ram.
playClock`'s `changedAt` — a live countdown ticks ~1/sec, so no change for 1.5s+ means frozen/dead
ball), OR'd with the OCR flag (`PlayClockCountingRecencyWindow`).

## Fixed: Goal-to-Go Situations Broke Down/Distance Tracking

RAM's raw `distance` field is documented `null` during goal-to-go specials — only the composed
`downDistance` display string (e.g. `"2nd & Goal"`) reliably carries it. `YardsToGo` was going
stale (holding whatever it was before the goal-to-go snap) instead of resolving to 0, breaking
every YardsToGo-dependent down helper and buffer near the goal line. `GameStateNormalizer.Normalize`
now falls back to parsing `"goal"` out of `game.DownDistance` when `game.Distance` itself doesn't
resolve. Per owner's explicit scoping call: goal situations route through the *existing* down
events (1st/2nd/3rd/4th down cues), not a new separate event category.

## New Event: "Offense: Second Down" (2nd & Long)

2nd & Long previously only fired "Defense: Second Down" (by original design — long yardage "hands
it to the defense") with zero offense-side counterpart, unlike 3rd down which already dual-fires
both `Offense: Third Down`/`Defense: Third Down`. Added `OffenseSecondDownHelper.cs` (mirrors
`DefenseSecondDownShortHelper`'s proven buffered-edge pattern exactly) firing `Offense: Second Down`
at volume 60 (ducked) alongside the existing `Defense: Second Down` — same balance convention as the
3rd-down-long pair. Registered in `GameWatcher.CreateEventRouter` and `ConfigStore.
AllEngineEventKeys` so it's assignable in the Market.

## Deliberately Not Changed

- **`DownDistanceBuffer`'s 500ms confirmation window** — owner explicitly declined widening it back
  toward 750-1000ms despite it causing some near-miss drops during noisy RAM/OCR sessions; they'd
  specifically asked for it shortened before for feeling slow, and preferred keeping that.
- **True new-game vs. restart detection** for the Pregame/Opening-Kickoff re-arm side effect above —
  flagged as a separate ask if wanted, not attempted this session.

## First Thing Next Session

Confirm the possession-debounce fix (last change of the night) actually holds across a full game —
it was rebuilt and relaunched but not yet pressure-tested against extended live play before this
handoff was written. Watch specifically for: any lingering false "After Punt"/turnover-adjacent
fires, and whether 2nd & Long's new dual-fire pairing sounds right in practice (volumes/timing).
