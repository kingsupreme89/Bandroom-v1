# Bandroom Handoff — August 8, 2026 (Session 12)

Picks up right after Session 11's doc (`docs/Bandroom_Handoff_2026-08-08_Session11.md`).
**Released as `v1.0.53`, live on GitHub.** Master HEAD at release time: `c152357`.

## 1. What shipped this session

- **Left icon rail height bug** (Session 11 §3, unconfirmed at the time) — root-caused and
  fixed: `#body` is `display:flex` with no `align-items`, defaulting to `stretch`, which forced
  `.rail` (Teams/Save/Help) to fill the whole column height and left a large empty gradient gap
  below Help. Fixed with `align-self: flex-start` on `.rail`.
- **Pregame "READY" screen detection** — confirmed already complete and merged (`f0eb358`),
  properly integrated into the `Bandroom.Core`/`IRuleEvaluator` pattern, text-only detection (no
  color matching, since the pregame panel is tinted per-matchup), build-verified.
- **Marketplace/Sound Bank/My Downloads brief** (`docs/Music_Library_UX_Brief_v2.md`) — the
  team-key-mismatch root cause (§1/§2.2) was already fixed on master; this session added the two
  remaining pieces: My Downloads now prunes manifest entries whose backing file no longer exists
  on disk (self-heals instead of listing dead downloads), and the per-team album grid now
  distinguishes a genuine fetch error ("Couldn't load — Retry") from a real empty team ("Nothing
  uploaded yet") instead of collapsing both into the same message.
- **Full state-machine bug audit** against a fresh owner report and `docs/STATE_MACHINE_ANALYSIS.md`
  — most of the reported list (dead 1st–4th down triggers, safety/2-point-conversion collision,
  duplicate loss-of-yards events, timeout-every-20-seconds, midfield-always-true, sack-fumble
  triple-fire, missing "Offense: Fourth Down", penalty routing) turned out to already be fixed
  earlier in this session (before a context compaction) — verified each one against actual
  current code, not the report's claims. What was genuinely still broken:
  - **`FieldGoalMissedHelper`** never fired — it was gated on `IsPAT` going true→false, but
    `IsPAT` is only ever set from "PAT GOOD" OCR text, which a missed kick never produces. Wired
    the existing "banner" region's "FIELD GOAL" OCR match into a new
    `PlaySnapshot.IsFieldGoalAttempt` flag (fires for both makes and misses) and rewrote the
    evaluator around that + no-score-change + possession-flip. Added a matching guard in
    `BigEventHelper` so a missed FG on 4th down doesn't also fire "Defense: Fourth Down".
  - **`bridge.DuplicateProfile`** didn't exist — the right-click "Duplicate Profile To..." menu
    item threw on every use. Added `DuplicateProfileFromWeb`/`WebBridge.DuplicateProfile` end to
    end.
  - **Mac build** — down to 7 errors, not the reported 78 (most had already been fixed). Root
    cause of the remaining 7: `Window` (Avalonia) has its own inherited `Theme` property of type
    `ControlTheme`, which shadowed this app's `using Theme = SupremeStadiumSoundSelector.Theme`
    alias at every `Theme.ActiveTeam` call site. Fully-qualified all 7 references. Both
    `BandAudioHook.csproj` and `Bandroom.Mac.csproj` now build with 0 errors.
  - **`EventRouter` dedupe backstop** — every duplicate-fire bug found in the audit was the same
    shape (two evaluators independently matching overlapping conditions, both firing the same
    EventKey). Each was fixed at the evaluator level, but that discipline isn't compiler-enforced
    across 16 separate evaluator classes. Added a same-tick EventKey dedupe in `EventRouter.Route`
    as a structural backstop on top of the per-evaluator guards, so a future evaluator can't
    silently reintroduce the bug class.
  - **Race #3** (`STATE_MACHINE_ANALYSIS.md`) — `OnEngineEventsDetected` defaulted to `"home"`
    when possession hadn't been read yet, which could fire a defensive cue for the wrong team.
    Now waits for a real possession read, matching the "never guess" convention already used by
    penalty routing and `OnTackleForLoss` elsewhere in the file.
  - **Discrepancy #9** — the legacy `OnTackleForLoss` handler wasn't gated behind
    `_useEngineForEvents`, so it double-fired "Defense: Tackle for Loss" alongside the engine's
    `TflHelper`, masked only by `FireCooldown`. Gated it like the other legacy handlers.
  - **B16 phantom bridge methods** — `bridge?.PlaySoundboardSlot(...)` and `bridge.ScanDynastySave()`
    were called from `app.js` with no C# counterpart. Neither is reachable today (soundboard bar
    is `[hidden]`, Dynasty scan has no caller), but both would have thrown the instant either
    feature got unhidden. Wired `PlaySoundboardSlotFromWeb` for real; `ScanDynastySaveFromWeb`
    returns `null` honestly (there is no actual CFB27 dynasty save-file parser anywhere in this
    codebase — faking one would be worse than reporting "not found").
- **Security audit** (targeted: path traversal, XSS, worker.js auth, process injection, secrets
  in git — see the dedicated audit sub-task for full scope) — two real, confirmed findings, both
  fixed:
  - `ConfigStore.ImportDefaultPackForTeam` built a filesystem path from an unsanitized
    `teamName` — a crafted name could walk outside `DefaultSongsFolder`. Sanitized.
  - `buildMyDownloadTile` in `app.js` set `innerHTML` with unsanitized marketplace-sourced fields
    (any uploader's name/school string), unlike the parallel marketplace-tile renderer which
    already sanitizes the same fields. Applied `sanitizeHTML()` to match. Also actually installed
    the "STRING XSS SAFETY" blanket `innerHTML` guard (`_safeSetInnerHTML`) — it was fully
    written with a comment claiming it "runs automatically" but the `Object.defineProperty` call
    that would have activated it was missing, so it had never protected anything.
  - Everything else checked out clean: `worker.js` admin endpoints verify `env.ADMIN_TOKEN`
    server-side, the Mac OCR subprocess uses `UseShellExecute=false` with non-attacker-controlled
    arguments, no secret values are actually committed in git history (`.gitignore` already
    excludes `*.local.txt`/`*secret*`/`*token*`, confirmed untracked).
- **TeamBuilder question answered from the code** (see next session's chat log or ask again if
  needed): a custom TeamBuilder school works identically to any built-in team for the live
  engine, because possession/team detection is **never** color- or logo-matched against the game
  screen — it's read purely from a fixed-position underline-brightness signal
  (`SamplePossessionByUnderline`) plus whichever Home/Away team names the user picked manually at
  matchup-confirm time. A custom school also gets its own dedicated profile automatically
  (`ConfigStore.SaveProfile`/`LoadProfile` keyed by team name, same as every built-in team), with
  the existing Generic-profile fallback for anything left unassigned — no special "generic
  profile" routing is needed for TeamBuilder teams.
- **Help & Guide dashboard verified live** — the ~40-tip dashboard + full ELI7 guide
  (`#btn-help-pill` → `#help-guide-overlay`, `initHelpGuide()`/`HELP_TIPS`/`HELP_GUIDE_HTML` in
  `app.js`) was already fully built earlier this session; confirmed by actually clicking through
  it in a browser preview (both "Tips & Tricks" and "Full Guide" tabs render real content). The
  rail's separate "?" button intentionally still opens the native keyboard-shortcuts dialog
  (`bridge.OpenHelp` → `ShortcutsForm`) — this is a deliberate, already-documented split, not an
  oversight.
- **Released `v1.0.53`** via `release.ps1` (owner said "ppup") — tagged, packed with Squirrel
  (delta + full), published to GitHub. Live at
  `https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.53`.

## 2. Not touched this session

- `AudioDuckingController.cs` — still fully dead code (built, never instantiated anywhere). No
  user impact currently; left as-is rather than guessing at activation behavior without a live
  test.
- The Mac app's other pre-existing uncommitted WIP changes (`AudioPlayer.Mac.cs`,
  `Bandroom.Mac.csproj`, `GameWatcher.Mac.cs`, `MacWebBridge.cs`, `PlatformStubs.Mac.cs`) were
  already modified before this session started and were left alone — only
  `MainWindow.axaml.cs`'s `Theme.ActiveTeam` build break was touched.

## 3. Starting a fresh session on this

1. `git log --oneline -15` and `git status` — confirm master HEAD matches or is ahead of
   `c152357` / tag `v1.0.53`.
2. `git worktree list` — should be empty; both in-flight agent worktrees from Session 11 were
   resolved and removed this session.
3. If picking up dead code cleanup: `AudioDuckingController` is the next candidate, but needs a
   live-game decision on ducking behavior before wiring it in, not a blind activation.
4. **Never run `release.ps1`** without the owner saying "ppup" or explicitly asking for a
   release — standing rule, unchanged (confirmed used correctly this session).
