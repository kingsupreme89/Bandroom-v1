# Bandroom Handoff — Session 32 (2026-08-10)

Picks up after Session 31 (window sizing fix, committed `dc32a93`). This session did a full
cross-check of the uncommitted "gameplan" work from earlier today, cleaned up disk space, then
implemented a real redesign of the down/distance event system and Big Game gating based on
back-and-forth with the owner. **Nothing from this session is committed yet.**

## What changed this session

### 1. Cross-checked and committed the earlier "gameplan" diff
Read every diff from the concurrent session that redefined Big Game and reworked down-and-distance
cues (see `docs/CFB27_Session23_Handoff.md` for that session's own writeup). Confirmed it was
internally consistent and built clean, then committed it as `a644c29`. Found and fixed one real
bug during review: `ConfigStore.RetiredEventKeys` had the two retired kickoff cues added, but they
were never removed from `AllEngineEventKeys`, so they kept reappearing in the UI every load instead
of staying hidden. That fix (plus a new "Fire Routed" test-hook control) landed separately.

### 2. Disk cleanup
C: drive was at 0 bytes free at one point this session. Removed (with explicit confirmation each
time): a stale, fully-merged git worktree (`profile-dashboard-metadata`, 5.6GB), `Bandroom/bin`
build output, and broken Ollama/local-model files (`C:\Models\blobs`, `AppData\Local\Ollama`).
**Not fully cleaned** — user mentioned another concurrent session may have done more; worth a fresh
`df -h /c` check before assuming there's headroom for a full Release+Squirrel pack.

### 3. Event system redesign (the main work)
Owner walked through a simplified down/distance naming scheme and, through several rounds of
clarification, revealed the real underlying gating model is different from what was built earlier
today. Implemented:

- **`src/Bandroom.Core/Helpers/FirstDownHelper.cs`** — dropped the old yards-gained "Big Gain"
  branch (retired to `ConfigStore.RetiredEventKeys`), replaced with a `YardsToGo <= 5` check on
  the resulting first down → new `Offense: Earned First Down Short` key. Plain case relabeled
  "1st Down (1st & 10)".
- **`src/Bandroom.Core/Helpers/DefenseFirstDownHelper.cs`** (new file) — fires `Defense: First
  Down` once, on the first offensive snap of a kickoff-started drive (the "Bama gets to play 1st
  down" moment Session 23's handoff flagged as unbuilt). Self-tracked flag pattern, same fix
  `KickoffHelper` already needed for gated-region staleness.
- **`src/Bandroom.Core/Helpers/DefenseThirdDownShortHelper.cs`** (new file) — fires `Defense:
  Third Down Short` on the exact same tick as `OffenseDownHelper`'s short-3rd-down branch, routed
  to the opposite side. Confirmed safe with `EventRouter.Route` (dedupes only identical
  EventKeys, runs every evaluator every tick) — but see the double-fire fix below, this was not
  safe without it.
- Both new evaluators registered in `GameWatcher.CreateEventRouter()`.

### 4. Big Game gating rewrite — real behavior change
`WebMainForm.ResolveEventRouting` went from two tiers to three:
1. **Home-only-always** (new `HomeOnlyAlwaysEventKeys` set) — `Defense: Third Down`,
   `Defense: First Down`. Never play for away, even during a Big Game.
2. **Un-gated** — any `Offense:`-prefixed key now always plays full volume for whoever's
   driving, home or away, Big Game irrelevant. **This is the actual fix for a live dev-build
   report** ("I'm on offense and a defensive song played, wrong side of the ball") — the old code
   gated *any* event routed to "away" regardless of prefix, so an away team's own offense cues
   were being wrongly throttled. Scoped to just the down/distance cards discussed this session;
   `Offense: Touchdown/PAT/Field Goal/etc.` keep their existing `IsEarnedBigEvent` 25%/100%
   treatment untouched.
3. **Ordinary Defense** (unchanged) — home always; away only during Big Game.

### 5. Same-tick double-fire fix
Firing two events on one tick (e.g. `Offense: Third Down Short` + `Defense: Third Down Short`,
routed to opposite sides) would have silently only played the *last* one — this app has one
shared audio pipeline (`AudioPlayer.StopAll`), not separate home/away channels, and
`interruptPrevious: true` was hardcoded on every fire. `FireEventForSide`/`FireEvent` now take an
`interruptPrevious` param; `OnEngineEventsDetected`'s loop only lets the *first* actually-fired
event in a tick's batch interrupt, everything after layers instead (same trick already used for
the PA announcer layer).

### 6. Big Game conditional song (new feature, backend only)
`TriggerEntry.BigGameAudioFile` — a full alternate clip (not layered like `PaAudioFile`) used
instead of `AudioFile` whenever `IsBigGame` is true and a variant is assigned. `FireEvent` picks
it automatically. Bridge methods added (`AssignBigGameTrackFile`/`ClearBigGameTrackAssignment`)
but **no UI pill wired up yet** — owner wanted this for the redesigned Defense cards specifically;
scoped out of this session for time. Bridge is ready whenever someone builds the card UI.

### 7. Test hook (Ctrl+Shift+T) kept current
All 3 new EventKeys auto-populate both dropdowns (pull live from `GetAllEventKeys`). Added a new
**Fire Together** control specifically to verify the same-tick layering fix without a live game —
picks two EventKeys + a possession side, fires both through the real routing path.

### 8. Dev-share builds + handoff RAR
Rebuilt and republished to `publish-dev-share/` twice this session (once after the retired-key
fix, once after the full redesign). Built `Bandroom_dev_build_2026-08-10.rar` (~1MB) at
`C:\Bandroom\` twice as well — exe/dlls/wwwroot only, **not standalone**, meant to extract over an
existing install. Most recent RAR has everything through item 7 above.

## Build status
`dotnet build BandAudioHook.csproj -c Debug` — clean, 0 warnings/0 errors, confirmed after every
change. `Bandroom.sln` (which also builds the Mac project) still fails — pre-existing, unrelated
missing-type errors (`CloudDatabaseService`, `AudioCache`, `IntakeEngine`), not touched this
session. New evaluators were **only** registered in `GameWatcher.CreateEventRouter()` (the
Windows path) — `src/Bandroom.Mac/GameWatcher.Mac.cs` and `MainWindow.axaml.cs` have their own
separate evaluator lists and were deliberately left alone since the Mac project doesn't build
anyway.

## Noticed but NOT touched this session
`git status` shows 3 deleted files this session didn't delete:
`guide/BANDROOM_COMPLETE_GUIDE.md`, `guide/BANDROOM_FEATURE_SHOWCASE_VIDEO_SCRIPT.md`,
`guide/BANDROOM_FOR_DUMMIES_VIDEO_SCRIPT.md` — likely another concurrent session or the owner
directly. Don't assume these are safe to re-add or finalize the deletion of without checking who
did it and why.

## Not yet confirmed — real next steps
1. **Live-verify the two new Defense evaluators** in an actual game: does `Defense: First Down`
   fire exactly once, right after a kickoff return's first snap? Does `Defense: Third Down Short`
   fire alongside `Offense: Third Down Short` and are both actually audible (not one cutting the
   other off)?
2. **Live-verify the un-gated Offense tier** fixes the original "wrong side of the ball" report —
   away's own offense cues should now always play regardless of Big Game.
3. **Big Game song UI pill** — bridge methods exist (`AssignBigGameTrackFile`/
   `ClearBigGameTrackAssignment`), no card UI built yet.
4. Everything in this session is **uncommitted** — working tree has `ConfigStore.cs`,
   `GameWatcher.cs`, `TriggerEntry.cs`, `WebBridge.cs`, `WebMainForm.cs`,
   `src/Bandroom.Core/Helpers/FirstDownHelper.cs` modified, plus 2 new evaluator files, plus the
   3 deleted guide files noted above (not this session's doing).

## Carried forward from Session 31 / 30 / 29 (untouched this session)
1. `voice_poc/.env` — still untracked, uncommitted, not gitignored; likely holds a secret.
2. **Not released** — commits sit on `master` past `v1.0.73` with no version bump/tag/Squirrel
   pack.
3. `.matchup-vs-badge` `top` value (nudged to `22%` in Session 30, still not re-verified visually).
4. Coverflow edge-fade mask + `.team-swatch-reflection` DOM wiring — CSS in place, JS side
   (`fillTeamSwatch()`/`renderMatchupCoverflow()`) never wired up.
5. Player Profile Dashboard public-sharing sync fix still not live-verified against the real
   worker.
6. Session 27 carryovers: Mac marketplace-sharing multipart fix, trim-preview pill follow-up.
