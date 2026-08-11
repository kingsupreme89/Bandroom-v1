# Bandroom Handoff — Session 35 (2026-08-10)

Picks up after Session 34 (whistle-volume fix, LOCK IN? restyle, volume-persistence/conflict-prompt
work paused). This session's scope was entirely different: a deep re-audit of the play-event cue
state machine (`src/Bandroom.Core/Helpers/*.cs` + `GameWatcher.cs`'s OCR/dispatch pipeline), not
UI work. Did not touch `WebMainForm.cs`, `app.js`, or `style.css`.

## Context: three prior audit passes led here

- 2026-08-08 and 2026-08-10 (earlier same day) passes fixed 16 discrepancies total in the evaluator
  pipeline — see `STATE_MACHINE_ANALYSIS_UPDATED_2026-08-10.md` in this same `docs/` folder for the
  full prior history/discrepancy numbering (#1-#16) this session's fixes continue from.
- This session opened by re-verifying issues #11-#14 from that doc against current code: **#11 and
  #14 were already fixed** (stale claims — `BigEventHelper.cs`'s Down==4 Loss branch already checks
  `YardsToGo` directly, not the dead `PlayDelta.LostYards`). **#12 (PenaltyHelper double-fire) and
  #13 (tick-split Loss cue) were still live.**
- User then asked for a much deeper pass ("go 40 levels deep with 3 auditors") — ran 3 parallel
  Explore agents, each independently re-verifying #12/#13 with exact tick-by-tick traces and sweeping
  every remaining Helper class not yet scrutinized in depth (12 files: `DriveStarterHelper`,
  `FieldGoalMissedHelper`, `FieldGoalPATHelper`, `FirstDownHelper`, `GameStateEventHelper`,
  `KickoffHelper`, `NoPuntReturnHelper`, `PregameHelper`, `TouchdownHelper`, `TurnoverHelper`,
  `DefenseFirstDownHelper`, `DefenseThirdDownShortHelper`). That pass surfaced 3 new confirmed bugs
  beyond #12/#13.

## What changed this session — 5 confirmed bugs fixed

### 1. PenaltyHelper double-fire (issue #12)
`GameWatcher.cs`'s `EventGatedRegions` (~line 181, was `{"situation","banner","quarter"}`) didn't
include `"penaltyagainst"`. That region's `region.Last` gets nulled on any blank OCR tick (graphic
flicker), resetting `Previous.IsPenaltyOnOffense/Defense` to false, so the same real-world penalty
could re-trigger `PenaltyHelper`'s edge-trigger a second time once OCR recovered. **Fix:** added
`"penaltyagainst"` to `EventGatedRegions`.

### 2. PregameHelper double-fire (found during the deep pass, not in the prior doc)
Same root cause as #1 — `"pregameready"` was the one status region left off `EventGatedRegions` and
the sticky `_lastKnownX` treatment given to down/scores/quarter. A single missed OCR poll during the
READY screen could double-fire "Pregame Ready". **Fix:** added `"pregameready"` to
`EventGatedRegions`.

### 3. Tick-split Loss cue (issue #13)
`Down` and `YardsToGo` are OCR'd from the same `"down"` crop text but were committed to
`_lastKnownDown`/`_lastDistanceRaw` independently — each only updates when its own regex succeeds
that tick. A garbled/partial OCR read could resolve one field a tick before the other, so
`DefenseHelper`/`TflHelper` (which require both a Down change AND a YardsToGo increase in the *same*
snapshot to fire the "(Loss)" cue) would see a stale pairing and fire the wrong generic cue instead
— with nothing correcting it afterward. **Fix:** added `CommitDownAndDistance` (`GameWatcher.cs`,
near `CheckForLossOfYards`) — stages both fields and only commits them to
`_lastKnownDown`/`_lastDistanceRaw` together, once both have resolved from OCR, with a 750ms fallback
timeout so a field that genuinely never changes doesn't block the other forever. Required decoupling
`CheckForLossOfYards`'s own tackle-for-loss-cue dedup from `_lastDistanceRaw` (now uses its own new
`_lastFiredDistanceRaw` field) since that field's commit is now delayed by the staging logic.

### 4. Turnover double-narration (new, found during the deep pass)
`BigEventHelper.cs`'s `Down==3`/`Down==4` `NewPossession` branches had no guard against
`state.Current.IsTurnover` overlapping with `TurnoverHelper`'s own edge-trigger. An
interception/fumble on 3rd or 4th down would fire both "Defense: Turnover Forced" (via
`TurnoverHelper`) and "Defense: Third/Fourth Down" (via `BigEventHelper`) in the same tick, narrating
the same play twice with inconsistent cues. `DefenseHelper` already had the correct guard
(`if (state.Delta.NewPossession) return null;`) deferring to `BigEventHelper` for this exact reason
— `BigEventHelper` itself was missing the analogous guard against `TurnoverHelper`. **Fix:** added
`!state.Current.IsTurnover` to both of `BigEventHelper`'s `NewPossession` branches.

### 5. GameStateEventHelper missing NewPossession guard (new, found during the deep pass)
"Iced Game by First Down" fires on `Delta.WasFirstDown` in the 4th quarter under 2 minutes, but
unlike its sibling `FirstDownHelper` had no guard excluding a turnover-driven down-reset to 1 — a
defensive interception/fumble recovery late in the 4th quarter could satisfy `WasFirstDown` and
incorrectly fire "Offense: Iced Game by First Down" alongside `TurnoverHelper`'s real "Iced Game by
Turnover" cue. **Fix:** added the same `!state.Delta.NewPossession` guard `FirstDownHelper` already
uses.

## Verified this session
- `dotnet build BandAudioHook.csproj -c Debug` — clean, 0 warnings/0 errors for all 3 edited files
  (`GameWatcher.cs`, `BigEventHelper.cs`, `GameStateEventHelper.cs`). Note: `dotnet build
  Bandroom.sln` fails with 10 pre-existing, unrelated errors in `Bandroom.Mac.csproj`
  (`CloudDatabaseService`/`AudioCache`/`IntakeEngine` not found, a delegate-arity mismatch in
  `MainWindow.axaml.cs`) — none of this session's files. Build with `BandAudioHook.csproj` directly
  to avoid noise from that broken project.
- 3089 rebuild+relaunch performed (had to `taskkill` a running `Bandroom.exe` first — it was locking
  the build output), WebView2 cache cleared, app relaunched successfully (confirmed via `tasklist`).
- **Not yet done:** actual runtime verification against a real/recorded broadcast clip. The plan's
  verification section calls out 5 specific scenarios still unconfirmed: (a) a penalty with a graphic
  flicker — confirm single fire; (b) a 3rd-down stuffed run with down/distance OCR updating on split
  frames — confirm correct "(Loss)" cue; (c) a 3rd/4th-down interception — confirm only "Turnover
  Forced" fires, not also "Third/Fourth Down"; (d) a 4th-quarter defensive turnover under 2:00 —
  confirm "Iced Game by Turnover" fires without a duplicate "Iced Game by First Down"; (e) a pregame
  READY screen with a dropped OCR frame — confirm single fire.

## Not yet confirmed — real next steps
1. Owner needs to watch/replay footage covering the 5 scenarios above and confirm each now produces
   exactly one correct cue.
2. No automated test harness exists for these evaluators — each `IRuleEvaluator` is a pure function
   of `GameState`, so unit tests are straightforward to add if this class of regression keeps
   recurring, but that was flagged as a recommendation, not started.
3. Dedupe (`EventRouter.cs`) is string-key-based and order-dependent on Helper construction order in
   `GameWatcher.CreateEventRouter` — traced and confirmed no actual same-tick key collision exists
   today, but this is fragile-by-construction (adding a new Helper with a colliding `EventKey` string
   would silently coalesce rather than error). Flagged for awareness, not fixed.
4. This session's plan file lives at
   `C:\Users\Fresh\.claude\plans\check-ahain-and-go-graceful-gadget.md` if the fix rationale needs
   re-reading in more detail than this doc.

## Carried forward from Session 34 / 33 / 32 / 31 / 30 / 29 (untouched this session)
Everything listed in Session 34's handoff under "Not yet confirmed" items 1-2 and 4-6, and its own
"Carried forward" section (1-8) — none of it touched this session. Notably still open: whistle-volume
audible confirmation, LOCK IN? button visual confirmation, volume-persistence approach decision,
conflict-prompt-before-autosave feature scoping, "What's New" popup root cause, and whatever the
concurrent Session 34 down/distance/Big-Game-gating pass landed as in `WebMainForm.cs`/`app.js` (diff
those fresh before assuming either file's current state).
