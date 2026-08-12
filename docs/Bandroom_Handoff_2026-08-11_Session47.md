# Bandroom Handoff — Session 47 (2026-08-11)

Picks up right after Session 46's live-fire triage (v1.0.76 + unshipped fixes from sections 4/5/9
of that doc — still not shipped as of this session, see "Not yet confirmed" at the bottom).
Two threads this session: (1) calibrating the default CFB27 scoreboard from a screenshot batch
while the owner napped, and (2) a live, in-progress trigger-event audit the owner is walking
through checklist-item-by-checklist-item — **this second thread is NOT finished, see below.**

## 1. CFB27 default scoreboard calibration (from screenshots)

Owner handed off a folder (`OneDrive\Pictures\Screenshots 1\`, ~145 images) and a prior handoff
doc, asked to "get the editing done" on wiring the default CFB27 scoreboard preset while away.

- Confirmed the batch actually mixes TWO scorebug skins: the old "Kam's CBS" broadcast overlay
  and CFB27's real default in-game HUD (hexagon pill, small EA SPORTS wordmark). The
  `ScorebugPreset.CollegeFootball27` preset already targets the second one; its score/clock/
  down-distance/underline crops checked out fine against the fresh screenshots, no changes needed.
- **Real fix**: screenshot #490 was the first-ever live TOUCHDOWN banner seen in the CFB27 skin.
  Turned out this skin doesn't show a separate ribbon like CBS — it replaces the ENTIRE scorebug
  pill in place (logos stay, white text fills the center). The old `BannerFxX/W` for this preset
  were still cloned from CBS's narrow center-only crop and would have missed it entirely.
  Recalibrated to span the full bar width (`ScorebugPreset.cs`, `CollegeFootball27.BannerFx*`).
- Still open: no live CFB27 penalty-overlay screenshot in this batch, so `PenaltyAgainstFx*`
  for that preset is still using CBS's placeholder coordinates. None of this has been live-tested
  against a running game yet either — same caveat every preset in this file already carries.

## 2. Trigger audit — IN PROGRESS, do not consider finished

Built a full checklist of all 45 `AllEngineEventKeys` events in plain English
(`docs/TRIGGER_AUDIT_CHECKLIST_2026-08-11.md`) so the owner could review the whole trigger system
in one place before "finalizing triggers." Owner is going down it top-to-bottom giving live
corrections one at a time ("I'm going down the list, correct as I give and I'll keep going until
we complete") — **we stopped partway through the Defense section**, right after "Defense: Third
Down (Loss)". Next session should resume from **"Defense: Fourth Down"** in the checklist and
keep going down the list with the owner.

### Changes made and shipped this session (build clean, tests passing after each):

- **Renamed** `Offense: Drive Starter` → `Offense: 1st Down After Punt` (clearer name).
- **Renamed** `Defense: Drive Starter` → `Defense: After Punt`, and made it **home-only-always**
  (added to `WebMainForm.HomeOnlyAlwaysEventKeys`) — away never gets this one now.
- **Renamed** `Defense: First Down` → `Defense: After Opening Kick` (clearer name).
- All three renames have old-key fallbacks in a new `WebMainForm.RenamedEventKeyAliases` dict
  (mirrors `ScorebugPreset.LegacyNameAliases`'s pattern) so a profile with a song already assigned
  under the old key doesn't go silently unassigned.
- **Corrected the "Short" yardage threshold** from ≤3 to ≤5 everywhere it's used
  (`OffenseDownHelper.isShort`, `DefenseThirdDownShortHelper`) — now matches
  `FirstDownHelper`'s own ≤5 "Short" definition, which these were inconsistent with before.
- **New event**: `Offense: 3rd Down Conversion` — fires specifically when the offense converts
  FROM 3rd down into a fresh 1st down (distinct from the generic Earned First Down cues, fires
  alongside them on the same snap). New file: `ThirdDownConversionHelper.cs`.
- **Corrected "Defense: Third Down"** — used to only fire on 3rd & LONG (a leftover of
  `OffenseDownHelper`'s short/long split). Owner wanted it to fire on 3rd down at ANY distance
  (offense simply facing 3rd down). Pulled it out into its own new evaluator,
  `DefenseThirdDownHelper.cs`, registered in `GameWatcher.CreateEventRouter`;
  `OffenseDownHelper`'s down==3 long branch now returns nothing (deferred to the new evaluator).
- **Retired `Defense: Third Down (Loss)`** — owner wanted it merged into the existing generic
  `Defense: Tackle for Loss` cue (which already fires on the same snap) instead of being its own
  separate card. Removed the branch from `DefenseHelper.cs`, moved the key from
  `ConfigStore.AllEngineEventKeys` to `RetiredEventKeys`.
- All of the above have matching test updates in `EvaluatorTests.cs` (renamed/retired assertions
  updated, 3 new tests for `DefenseThirdDownHelper`). **50/50 tests passing**, build clean, after
  every round.
- `docs/TRIGGER_AUDIT_CHECKLIST_2026-08-11.md` updated in lockstep with every change above —
  it's the live source of truth for what's confirmed (`[x]`) vs still open (`[ ]`).

### Not yet touched — still open in the checklist

Everything from "Defense: Fourth Down" onward: Fourth Down, Fourth Down (Loss), Second Down,
Second Down (Loss), Field Goal Missed by Opponent, Turnover Forced, Iced Game by Turnover,
Safety, Tackle for Loss, Touchdown Scored (Defense), Timeout ladder (5 events), Penalty ×2, and
the whole Other/Situations group — plus the "known gaps" list at the bottom of the checklist
(per-penalty-type detection, no explosive-play cue, missed/blocked PAT, fumble recovered by own
team, overtime, injuries/reviews/challenges).

## 3. New feature: "Share to..." button (song → other event)

Owner asked for an easy way to assign the same song to multiple events without re-browsing the
file picker each time. Added the mirror image of the existing "Copy From..." button (which pulls
an assignment IN from another event) — this one pushes the CURRENT row's song OUT to another
event.

- Reuses the existing `WebBridge.CopyEventAssignment(sourceTrigger, targetTrigger)` bridge call
  — no C# changes needed, purely `wwwroot/app.js` + `style.css`.
- New `wireSituationShareToPopover()` in `app.js`, mirrors `wireSituationCopyFromPopover()` but
  lists ALL other events on the team (not just already-assigned ones — sharing into an unassigned
  card is the point) and copies OUT instead of IN.
- **UI iteration, two rounds, both from live owner screenshots**: first version put "Share to..."
  as a 4th pill button alongside Assign/Assign PA/Copy From — owner reported it caused a real
  layout bug (row overlap/corruption, too many pills wrapping awkwardly on narrower cards) and
  asked for it moved into the transport icon strip (play/stop/volume/whistle/info) instead, right
  next to Play. Moved it there as a small ↗ icon button, popover now anchors to
  `.situation-transport` instead of the crowded `.situation-actions` pill row. Fixed.
- **Real bug caught this session, not code-related**: after the second edit, the owner's running
  app kept showing the OLD pill-button layout. Root cause: the deployed
  `bin/Debug/.../wwwroot/app.js` was older (17:14) than the source file (17:18) — the build run
  right after the edit didn't actually pick up the latest save before the app was relaunched.
  Rebuilding again (confirmed via `grep -c "share-to"` on the deployed copy, not just re-running
  the build) fixed it. **Worth remembering**: don't trust "I built and relaunched" as proof of a
  fix without checking the actual deployed file's content/timestamp, same lesson Session 22's
  handoff already noted for the TeamBackgrounds csproj bug — this app has now bitten on the
  "looks right in source, stale in the built output" class of bug at least twice.

## Important: a second AI agent (Cline) is concurrently active on this repo

The owner's screenshot this session showed a **Cline agent panel open in VS Code, actively working
on this same Bandroom codebase at the same time** (visible mid-conversation about "song-list
preview Mac app: ported Copy From, sound-start delay, and the whistle-preview fix"). This is a
real collision risk — two agents editing the same files (`wwwroot/app.js` and others) concurrently
can race each other's builds and edits, and may well be part of why the stale-build issue above
happened. **Flagged to the owner directly, not yet resolved** — next session should check whether
Cline is still active before making further edits, and specifically diff `wwwroot/app.js`,
`WebMainForm.cs`, and `ConfigStore.cs` against what this handoff describes in case Cline's session
changed any of the same areas independently.

## Build/test status

- `dotnet build BandAudioHook.csproj -c Debug` — clean (0 warnings/errors) after every round.
- `dotnet test src/Bandroom.Core.Tests` — 50/50 passing after every round.
- App relaunched and confirmed running (verified `wwwroot/app.js`'s deployed copy matches source
  via direct content grep, not just a build log) after the Share-to fix, PID 35904 as of this
  handoff — may still be up.
- Section 9's cache pre-warm fix and sections 4(round 2)/5 from Session 46 are STILL not shipped
  via `release.ps1` as of this handoff either — carried forward, unrelated to this session's work.

## Real next steps

1. **Resume the trigger audit checklist with the owner from "Defense: Fourth Down" onward** —
   this is mid-conversation, not a "come back later" item. Don't re-derive from scratch, just
   keep walking the list in `docs/TRIGGER_AUDIT_CHECKLIST_2026-08-11.md`.
2. Check whether Cline is still active on this repo before editing; reconcile any independent
   changes it may have made to `wwwroot/app.js`/`WebMainForm.cs`/`ConfigStore.cs`.
3. Get a live CFB27 penalty-overlay screenshot to finish calibrating
   `ScorebugPreset.CollegeFootball27.PenaltyAgainstFx*` (still using CBS's placeholder numbers).
4. Once the trigger audit is fully confirmed, ship everything via `release.ps1` (`ppup`) — nothing
   from this session has been released yet, same as Session 46's unshipped tail.
