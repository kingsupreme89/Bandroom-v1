# Bandroom Handoff — August 8, 2026 (Session 11)

Picks up right after Session 10. **Current `master` HEAD: `7429b5e`.** All queued work from this
session has landed and builds clean. **Note: a concurrent Claude Code session was also committing
directly to this same `master` during this session** (`7e00006`, `7429b5e` were not made by this
session's agent) — if picking this up fresh, check `git log` for anything newer than `7429b5e`
before assuming this doc is fully current, and be aware two sessions working the same repo at once
is possible for this owner.

## 0. Final state (added after §1-5 below were written mid-session)

Both background agents referenced in the original §2 below completed and were merged:
- Marketplace team-key mismatch + My Downloads self-heal → `f60a33f`, then further extended by
  the concurrent session's `7429b5e` (disk-pruning + fetch-error-vs-empty distinction — the more
  complete version; `f60a33f`'s flag-don't-prune approach is now largely superseded, harmless
  overlap, not a bug).
- Pregame READY-screen detection → `f0eb358` (plumbing complete, OCR region intentionally
  uncalibrated — needs a live screenshot before it can ever fire).
- Also landed: CFB27-style matchup screen redesign (`0414be1`), direct Locate & Import entry
  point (`f3cf4a4`), and (via the concurrent session) the left-rail height bug (`7e00006`).

Nothing is currently in flight. `dotnet build BandAudioHook.csproj -c Release` and
`node --check wwwroot/app.js` both pass clean as of `7429b5e`.

**Known environment gotcha this session**: every agent worktree spawned with `isolation:
"worktree"` checks out at the same stale commit `5813cc1` (v1.0.47), 15+ commits behind whatever
`master` actually is at spawn time — NOT a fresh branch off current master like you'd expect. Two
consequences: (1) agents cannot `dotnet build`/`git commit` meaningfully against current code, they
can only reason about it via absolute-path `Read`, and their own "build verified" claims may
actually be against the stale tree, not current master -- treat every agent completion report as
unverified until you personally check which base it actually built against; (2) never cherry-pick
an agent's worktree commits directly — read the diff, then hand-port the equivalent edit onto real
`master` at the current line numbers, same as done repeatedly this session. One exception:
worktree `agent-a04134f98c3cbae1e` (matchup redesign) *did* branch from current master and cherry-
picked cleanly — seems to depend on timing/some other factor, don't assume either way, always
check `git worktree list`'s SHA column against current master's HEAD before trusting a cherry-pick.

## 1. What shipped this session (on `master`, all individually audited + build-verified)

- **`2dfb8b1`** — Merged Session 10's unaudited worktree (6 commits + 1 finished uncommitted
  change): fixed the clipper-island dead-space gap under the situations/events grid (the
  `#situations-panel` was `flex: 1 1 auto`, force-filling leftover space instead of sizing to
  content — changed to `flex: 0 1 auto`), real Big Game detection (was hardcoded `false`), a real
  upload/replace flow for the lead-in whistle clip, an explicit default-profile prompt after
  matchup lock, Play/Stop/Download buttons on Suggested-for-You rows, removed the clipper-assign
  team-filter sidebar (dead-space complaint), restyled clipper-assign row buttons to match the
  glass-pill treatment used elsewhere.
- **`f52489d`** — Three queued Session 10 items:
  - **Load Profile now prompts which team** a profile file is for (`WebMainForm.
    ImportProfileFromWeb(targetTeamName)`, new team-picker dialog `#import-target-team-overlay` —
    deliberately NOT `#load-profile-overlay`, which was already taken by the separate "Load
    Profile from Others" marketplace feature; watch for this collision if resuming related work).
  - **TeamBuilder "Add School" v1**: name + primary/secondary color only, persisted to
    `custom_teams.json` via `ConfigStore`, layered onto `TeamColors.All` at load time
    (`TeamColors.AddCustomTeam`). No in-game OCR/matching, by design — logo is set afterward via
    the existing per-team crop tool, which already works for any team name.
  - **Root-caused the "crop tool doesn't work" complaint**: `WebBridge.LogoUrl()`/
    `GetTeamBackgroundUrl()` always returned the identical URL string for a given team (files
    save back to a stable filename), so a saved crop's on-screen result silently never refreshed
    for any element that mutates an existing DOM node in place (`applyBackground`,
    `applyVsBackdrop`) rather than rebuilding it (`fillTeamSwatch` happened to work by accident,
    since it rebuilds via `innerHTML`). Fixed with a last-write-time cache-busting query param on
    both URLs.
- **`b6e1c8f`** — Deep audit of the event-trigger/OCR pipeline (owner: "80 levels deep"), three
  real findings:
  - **Root cause, not OCR flakiness**: `PlaySnapshot.YardLine` is hardcoded to `0` in
    `GameWatcher.cs` (no OCR region ever reads yard line) — `PlayDelta.LostYards` was therefore
    always `false`, which silently dead-ended `TflHelper`, both `DefenseHelper` "(Loss)"
    branches, and `BigEventHelper`'s Fourth-Down-Loss branch. Fixed to use Down-advanced +
    YardsToGo-increased instead (both reliably OCR'd every tick).
  - Kickoff regex was the one phrase in the `situation` region's pattern missing `\s*` tolerance
    for OCR word-splitting (every other multi-word phrase already had it) — fixed, plus the
    matching `NormalizeMatch` case.
  - Possession-color sampling already skipped itself during a FLAG (penalty) frame but had no
    equivalent guard for the "situation" region also being active (KICKOFF/TOUCHDOWN/TURNOVER
    banners repaint the same band to a non-team color) — generalized the guard. Plausible cause
    of "touchdown attributed to wrong team," not yet live-confirmed.
  - **Still needs a live game rep to fully confirm**: the kickoff-regex and possession-guard
    fixes are code-verified gaps, not confirmed against real OCR output yet. The TFL/Defense/
    BigEvent fix is a straightforward dead-signal correction, high confidence.
- **`0414be1`** — Redesigned the "Start a Game" matchup picker to CFB27's own proportions per an
  owner-provided reference screenshot: full-bleed split screen, giant center team logo per half
  (clamp 210-320px, was a fixed 128px), each half tinted toward its own centered team's color
  (`--side-color`, same `color-mix` pattern as `applyVsBackdrop`), pulsing "VS" badge dividing the
  two sides. Scoped to `.matchup-columns` only — the shared coverflow classes used by onboarding/
  favorite-team pickers are untouched. Same underlying picker mechanism (search, arrow/click
  cycling, GAMETIME gating), this is layout/visual restructuring around it.
- **`f3cf4a4`** — Added a second Command Palette entry, "Locate & Import Song Pack (I already
  have the .zip)", directly opening `#songpack-import-overlay`. Previously the ONLY way to reach
  the "Locate & Import" button was clicking "Download" first (which re-opens the Google Drive
  page) even for someone who already had the file downloaded.

Also this session: **disk filled to 0 bytes free** mid-session (same failure mode as a prior
session per Session 10's doc — recurring issue, root cause still not diagnosed). Freed ~19GB by
deleting `bin/` (5.8GB, regenerable build output), `test_build_2/` (3GB, explicitly flagged
scratch in Session 10's doc), `test_build/`, `obj/`, and removing 4 already-merged/unusable agent
worktrees + their branches. **If disk fills again, check for accumulated `.claude/worktrees/*`
directories and stale `bin`/`obj` first** — this is now a two-time recurrence, worth a permanent
fix (e.g. a repo-root `.gitignore`-adjacent cleanup script, or investigating why builds/worktrees
accumulate rather than getting cleaned up automatically).

## 2. In flight — NOT on master, do not assume complete

Two agents launched near the end of this session, both in isolated worktrees (both stale-checkout
at `5813cc1` per the gotcha above — expect to hand-port, not cherry-pick):

1. **`agent-af80fbba63347d942`** — Marketplace/Sound Bank/My Downloads fixes per
   `docs/Music_Library_UX_Brief_v2.md` (full spec doc, read it first). Scoped to fix the
   team-key-mismatch ship-blocker (a team's per-team modal can show zero uploads despite real
   ones existing — brief's own reproduced example is Georgia), the "My Downloads" disk-drift
   issue, and a possible class/ID CSS selector trap in the marketplace grids, THEN do the visual
   glass-tile pass on top. This is the owner's current top priority ("make sure new spotify
   market/dl folder and sound bank... are first to complete").
2. **`agent-a5e2d93264c754cfc`** — Pregame "READY" screen detection (last queued item from
   Session 10 §3). Explicit constraint: must NEVER use color matching for detection (CFB27's
   pregame panel colors vary per matchup) — anchor on fixed/team-neutral screen elements only.
   Expected to add real evaluator/plumbing (`PregameHelper.cs` following `TflHelper.cs`'s
   pattern) but leave the actual OCR region coordinates as an explicitly-flagged uncalibrated
   placeholder, since no agent can run the live game to calibrate pixel positions. A prior attempt
   at this exact task failed silently (worktree was corrupted/nonsensically stale, produced
   nothing usable) — this is the retry.

**When these land**: read the actual diff (not the agent's summary), check what commit/SHA the
worktree is actually based on vs. current master, hand-port onto real `master` at current line
numbers, verify `dotnet build BandAudioHook.csproj -c Release` + `node --check wwwroot/app.js`,
then commit for real. Same audit discipline used all session — see every commit message above for
the expected tone/rigor.

## 3. Unresolved from this session — needs a screenshot or live check

- **Left icon rail rendering abnormally tall**: owner sent a screenshot showing `#rail-left`
  (Teams/Save/Help pill buttons) stretched to fill the entire window height with a large empty
  gradient gap below the Help button, instead of hugging its content height. Not yet
  investigated — asked the owner for clarification on whether this is the "island" they wanted
  removed vs. a separate layout bug; no reply received in this session. Check `wwwroot/style.css`
  `#rail-left`/`.rail-item` flex/height rules for a `flex: 1` or `height: 100%` that shouldn't be
  there, similar in spirit to the `#situations-panel` gap bug fixed in `2dfb8b1`.
- Owner also asked "how do users import the base pack and it work flawlessly" — answered inline
  (see this session's chat transcript) but flagged two real gaps, only one of which was fixed:
  - ✅ Fixed (`f3cf4a4`): no direct entry point to "Locate & Import" without re-triggering Download.
  - ❌ NOT fixed: the download itself is still a manual hop through the system browser to Google
    Drive with no retry/resume if interrupted, and the prior session's root cause for the pack
    once extracting *inside* the user's own Songs folder (2.8GB, 2,241 files) was patched but
    never actually root-caused — still flagged as needing verification the import path can't
    recreate it.

## 4. Queue state vs. Session 10's original list

- ~~Load Profile school prompt~~ — done (`f52489d`).
- ~~Custom/team-builder "Add School"~~ — done, v1 scope (`f52489d`).
- ~~Logo/background crop tool bug~~ — root-caused and fixed (`f52489d`).
- ~~Consolidate the two Set Matchup screens~~ — done (`0414be1`); note there was actually only
  ever one `#matchup-overlay` by the time this session started (already coverflow-based from an
  earlier session), so this became "make the existing one match CFB27's proportions" rather than
  a literal two-screen merge — re-read `0414be1`'s commit message if this distinction matters for
  future work.
- ~~Assign Track song rows glass styling~~ — done as part of `2dfb8b1`
  (`clipper-assign-row-btn` restyle).
- **Pregame "READY" screen detection** — in flight, see §2.
- **Marketplace/Sound Bank/My Downloads full brief** — in flight, see §2, top priority per owner.
- New from this session: left-rail height bug (§3, unconfirmed), song-pack download friction
  (§3, partially fixed).

## 5. Starting a fresh session on this

1. `git log --oneline -15` and `git status` first — confirm `master` HEAD matches or is ahead of
   `f3cf4a4`.
2. `git worktree list` — check whether the two in-flight agents (§2) have landed; if their
   worktrees still exist and have uncommitted/committed diffs, that's real work to review, not
   noise. If a worktree's SHA doesn't match current master, expect to hand-port per §2's method.
3. Read this doc's §3 for what still needs a screenshot/live confirmation from the owner before
   any code gets written for it — don't guess at the left-rail bug's fix without seeing the
   actual CSS first.
4. Watch disk space (`df -h /c` equivalent) proactively if doing any build-heavy work — this is a
   two-time recurrence this project's history, not a one-off.
5. **Never run `release.ps1`** without the owner saying "ppup" or explicitly asking for a
   release — standing rule, unchanged.
