# Bandroom Handoff — Session 21 (2026-08-09 night) — v1.0.70 shipped

Picks up right after Session 20 (`docs/Bandroom_Handoff_2026-08-09_Session20.md`, v1.0.69). One
release went out this session: **v1.0.70**.

## What shipped

### 1. Deep audit re-verification + real fixes
The owner pasted a "100-level deep audit" report claiming 18 open bugs. Rather than trust it, ran
a verification pass against actual current source. Result: 4 of the 18 were stale claims already
fixed since the report was written (C1 Delta caching, H2 ui-bot debug gate, H5 PA-copy snapshot,
M7 PA-restart glitch — don't re-fix these), 4 need a product decision or real data outside my
authority (H3 Mac build errors, H4 dead `AudioDuckingController.cs`, M4 uncalibrated pregame OCR
region, M6 own-team timeout tracking — left alone, still open), and 9 were real and got fixed:

- **C2** — `IRuleEvaluator` gained a default `CanFire(GameState)` pre-check; `EventRouter.Route()`
  skips `Evaluate()` when it returns false. Added real overrides only where a genuinely cheap guard
  exists (DefenseHelper, OffenseDownHelper, NoPuntReturnHelper, TouchdownHelper, TurnoverHelper,
  PenaltyHelper, PregameHelper, FieldGoalMissedHelper, FieldGoalPATHelper, DownFieldPositionHelper,
  FirstDownHelper). Deliberately skipped KickoffHelper/GameStateEventHelper (unconditional internal
  state resets inside `Evaluate`) and BigEventHelper (no single cheap guard across its branches).
- **M1** — `EventRouter`'s `List`/`HashSet`/`List` are now pooled instance fields, cleared instead
  of reallocated every 250ms tick.
- **M2/M3** — removed `PlayDelta.LostYards`/`WasThirdDownStop`/`WasFourthDownStop` (computed every
  tick, zero real readers — only comments referencing why callers stopped using them).
- **M5** — `GameWatcher.cs` bitmap-crop sites now clamp X/Y to `>= 0` (width/height were already
  clamped) at all 4 crop call sites.
- **L1** — `PlayUiClickSoundFromWeb` uses `Random.Shared` instead of `new Random()` per click.
- **L3** — `AudioPlayer.Warmup()`'s inner `Task.Run` lambda uses `await Task.Delay(30)` instead of
  `Thread.Sleep(30)`.
- **L4** — `OnLog()` (OCR debug log) now buffers in memory and flushes every 20 calls or 3 seconds
  instead of doing real file I/O + a periodic full-file rewrite on every ~4/sec OCR tick.
- **L5** — added `"Other: Pregame Ready"` to `ConfigStore.AllEngineEventKeys` — `PregameHelper` was
  emitting this EventKey but it had no UI slot, so no song could ever be assigned to it (silent
  orphan, same shape bug as the Auto-Assign overwrite `_config` staleness bug from Session 16).

**Two independent review passes ran against the diff before it shipped** (a "bug-block auditor"
and a "security guard," per the owner's explicit request to lock the state):
- Security review: no findings (checked the new log-buffer file I/O, the crop-clamp math, the
  pooled `EventRouter` fields for thread-safety, and confirmed `Route()` is only ever called from
  one single-threaded OCR poll loop per platform).
- Bug-block audit: caught one **real regression** — the buffered `OnLog()` had no flush wired into
  any crash/close path, so up to ~20 lines / ~3 seconds of the exact OCR data the log exists to
  capture could be silently lost right when it mattered most. Fixed: added a `FlushOcrLog()` method,
  wired into both `WebMainForm.FormClosing` handlers and `Program.cs`'s
  `AppDomain.CurrentDomain.UnhandledException`/`Application.ThreadException` handlers.

**Mid-session correction, worth knowing about**: a later background agent (working on the Event
Log feature below) hit what it believed was external file corruption in `PlayDelta.cs`/
`TflHelper.cs` and "fixed" it by running `git checkout --` on both files — which actually **reverted
the M2/M3 dead-field-removal fix above**, not some external corruption. Caught by re-checking the
diff before shipping; re-applied the M2/M3 fix, re-verified the fields were still genuinely dead,
rebuilt clean. **Lesson for next session**: if an agent claims a file was "externally overwritten
mid-task" again (this has now happened twice this week, once in Session 20/21's lineage with
`FieldGoalMissedHelper.cs` too), don't let it unilaterally `git checkout --` anything — stop and
diff against what the session actually changed first.

### 2. Full event/trigger coverage inventory (owner's "100% checklist" ask)
Built a complete cross-reference of every EventKey against emitted → registered → routable, the
same three-step chain the L5 bug above was hiding in. Result: **8 confirmed working clean, 6
actually broken (silent orphans / retired-with-no-alias), 19 "risky"** (work today but silently
drop if `_possession` hasn't been read yet when they fire — the vast majority of side-specific
events share this one root cause). Also surfaced a real landmine: **"Defense: Tackle for Loss" is
emitted by two different code paths** — the engine path (via `EventRouter`, covered by its
`Dedupe` backstop) and a legacy direct-wired path (`OnTackleForLoss` in `WebMainForm.cs`, bypasses
`EventRouter` entirely, NOT covered by `Dedupe`) — currently prevented from double-firing only by
the `_useEngineForEvents` boolean always being true. Not fixed this session (owner wants to work
through the checklist item by item) — full checklist was handed to the owner in-chat, not written
to a file; regenerate by re-running the same cross-reference (emitted evaluators × `AllEngineEventKeys`
× `WebMainForm` routing gates) if picking this up fresh.

### 3. New feature: Event Log (plain-English "why didn't it fire") — new, shipped
Owner wants to see, live and in plain English, what the app is doing/skipping instead of just not
hearing a song with no explanation.
- **`EventActivityLog.cs`** (new file): static ring buffer, 200 entries, thread-safe (`lock`).
  Records both fires ("Touchdown Scored (Home) — played 'Fight Song.mp3'") and skips ("skipped: we
  haven't figured out which team has the ball yet", "skipped: away-team events are turned off right
  now", "skipped: duplicate of an event we just fired this instant"). Deliberately plain wording —
  no "possession null"/"Dedupe" jargon in anything shown to the user.
- Wired into `WebMainForm.OnEngineEventsDetected`/`FireEventForSide` at every real fire/skip
  decision point, and into `EventRouter.Dedupe` via a new optional `onDuplicateDropped` callback
  (dedup happens in Core before `WebMainForm` ever sees the event, so the callback had to surface
  from there).
- Bridge: `WebBridge.GetEventActivityLog()` / `ExportEventActivityLog()` (→
  `WebMainForm.GetEventActivityLogFromWeb`/`ExportEventActivityLogFromWeb`). Export writes
  `event_log_<yyyyMMdd_HHmmss>.txt` under `ConfigStore.UserDataRoot`, returns the full path.
  Verified: empty-log export doesn't throw, `GetSnapshot()` returns a copy so the UI/exporter never
  race the lock held during `Record`.
- UI: new **"Event Log" pill inside the existing Help & Guide popup** (`#help-guide-overlay`),
  next to Tips & Tricks / Full Guide. Live feed polls `GetEventActivityLog()` every 2s while the
  tab is open; polling starts on tab-select, stops on tab-switch/overlay-close/guide-jump (verified
  no leaked `setInterval`). **"Save Log File"** button exports and toasts the saved path.
- **New trigger phrase for future sessions, saved to memory**: owner says **"whatsthedeal"** to
  mean "pull up the most recent exported event log and go through it together" —
  `~/.claude/projects/C--Bandroom/memory/trigger_whatsthedeal_eventlog.md`.

### 4. Matchup coverflow logos — REAL root cause found and fixed (different bug than Session 20's)
Session 20 claimed the box-reflect/Chromium-paint-culling fix solved "logos don't show in the
matchup coverflow" — confirmed this session that fix **is** still in place, but the owner reported
logos still weren't showing, specifically only on the Set Matchup screen (confirmed working
everywhere else — Sound Bank, team grid, etc.). Traced it in code (no GUI/screenshot tooling
available in this environment to verify visually — same wall Session 20 hit):

**Root cause**: `.matchup-column` (the flex-column wrapper for each half of the matchup screen)
sets `align-items: center` for its own layout needs (centering the title/search box). That switches
its flex children's cross-axis sizing from the default **stretch** to **shrink-to-fit**.
`.coverflow-stage`/`.coverflow-track`'s only content is `position: absolute` tiles, which
contribute **zero intrinsic width** to normal flow — so with shrink-to-fit instead of stretch, the
whole stage collapsed toward ~0px wide, and `overflow-x: hidden` on `.coverflow-track` clipped
every tile inside it, including the center one. Every OTHER place this same coverflow markup is
used (`#team-picker`, onboarding, favorite-team picker) works fine because none of those parent
containers override `align-items` away from the default stretch.

**Fix**: added `width: 100%;` to the existing `.matchup-columns .coverflow-stage` rule
(`wwwroot/style.css`) so it gets a real width regardless of the parent's `align-items`. Scoped
under `.matchup-columns` only — doesn't touch the other 3 coverflow usages sharing the same base
classes. **Not visually confirmed live** (no screenshot/GUI-driving tool available this session) —
owner should confirm on next launch; if still broken, the CSS reasoning above should be re-checked
against DevTools computed styles rather than re-guessed.

### 5. Scorebug presets — "Kam's CBS Scorebug" removed, per owner's explicit request
Owner wants only two presets going forward: **"Kam's CBSv3"** and **"Console/Remote Play v1"**.
Removed the original `KamsCbsScorebug` entry from `ScorebugPreset.cs` entirely (was the first-ever
calibration, superseded by v3). Repointed both hardcoded default-fallback references —
`ConfigStore.LoadScorebugPresetName()`'s no-file-yet fallback and `GameWatcher._activePreset`'s
field initializer — from the removed preset to `KamsCbsScorebugV3`. `SettingsForm.cs`'s dropdown
already builds itself from `ScorebugPreset.AllPresets` dynamically, so no UI code changes needed
there. **Not handled**: if an existing user's machine has `scorebug_preset.txt` on disk containing
the literal old name `"Kam's CBS Scorebug"`, `ScorebugPreset.GetByName` will fail to match it and
silently fall back to `KamsCbsScorebugV3` (its `?? KamsCbsScorebugV3` fallback) rather than error —
confirmed this is graceful, not a crash, but the user's saved preference will silently change to v3
on next launch. Worth a heads-up to the owner if anyone reports "my scorebug position reset."

### 6. Spinner restyle
Only spinner in the whole app (`.btn-signin-loading::after`, the Google Sign-In loading indicator)
restyled from a plain rotating border-ring to a fading dotted ring (conic-gradient + repeating mask
trick), matching a reference image the owner pasted in chat.

## Found, not fixed — flagged for the owner

- **A second, stale full copy of this repo exists at `D:\Bandroom`** (separate from the real,
  actively-worked repo at `C:\Bandroom`). Discovered when a background review agent got confused
  and audited the wrong one, producing a bogus "none of this exists" report. Owner asked to rename
  it to "Bandroom Backup" to reduce confusion — **attempted, failed**: `Rename-Item` hit "Access to
  the path 'D:\Bandroom' is denied" with no process holding a handle on it (`Get-Process` showed
  nothing). Needs the owner to rename it manually (right-click → Rename, or an elevated prompt) —
  don't have permissions to force it from here safely.
- Full event-checklist breakdown (8 confirmed / 6 broken / 19 risky, see item 2 above) — owner
  wants to work through this list item by item next. Start with the 6 truly broken ones (Drive
  Starter home/away — never registered in the UI at all; the down-field-position "Midfield" events —
  blocked pending real yard-line OCR; the legacy-alias-only down/timeout events) since those are
  "can never work at all" vs. the 19 risky ones which mostly share one root cause
  (`_possession == null` drop) and might be one fix instead of 19.
- The Tackle-for-Loss double-fire-path landmine (item 2 above) — not touched, flagged only.

## Starting a fresh session on this

1. `git log --oneline -5` and `git tag --sort=-v:refname | head -3` — confirm v1.0.70 is latest.
2. Working tree **was committed this session** (unlike most prior sessions) — `release.ps1`'s new
   Step 0 (added Session 20) commits pending work automatically as part of "ppup" now. Don't assume
   uncommitted-forever is still the pattern; check `git status` fresh each time.
3. `D:\Bandroom` is a stale duplicate, not the real repo — always confirm you're operating in
   `C:\Bandroom` before trusting any agent's file-existence claims, especially if a report says
   something "doesn't exist" that you're confident was just added.
4. If a background agent claims a file was "externally overwritten" mid-task again, don't trust a
   unilateral `git checkout --` fix — verify against `git diff` from the start of the session first.
5. Next priorities per the owner: (a) confirm the matchup coverflow logo fix actually works live —
   nobody could visually verify it this session; (b) work through the 6 broken + 19 risky event
   checklist items; (c) the Tackle-for-Loss double-fire landmine; (d) `D:\Bandroom` folder rename,
   if the owner wants help troubleshooting the access-denied error.
