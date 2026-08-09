# Bandroom Handoff — Session 17 (2026-08-09)

Picks up right after Session 16 (`docs/Bandroom_Handoff_2026-08-09_Session16.md`). One release
went out this session: **v1.0.65** (from v1.0.64).

## What shipped

### 1. Guided Auto-Assign wizard
- Old behavior (Session 16): Auto-Assign was a single confirm → overwrite-everything-with-the-
  default-pack action (`runAutoAssignOverwrite`/`ApplyDefaultProfileForTeamOverwrite`). Owner
  wanted a real per-event walkthrough instead: pick a team, search local+market at once, step
  through every event one at a time with a suggested song (or a pick-among-candidates list when
  more than one plausible match exists), confirm each one, warn before overwriting an existing
  assignment, and be able to cancel or skip at any point.
- The confirm dialog (`#auto-assign-confirm-overlay`) now offers three choices instead of two:
  **Cancel**, **Guided Assign...** (new), **Overwrite** (old bulk path, unchanged, still there for
  a fast full-team replace).
- Guided path (`startAutoAssignWizard` → `advanceAutoAssignWizard` → `openWizardEventPicker`, all
  in app.js): builds the full event queue across every category via `GetEventsForCategory`, loads
  a merged candidate library (`GetTrackLibrary()` + `GetDefaultPackSongsForTeam(team)`, deduped by
  path — this is the "local + market at once" search, since GetTrackLibrary already folds in past
  marketplace downloads), then reuses the **existing** `#clipper-assign` panel per event (same
  Play/Browse/Trim/Clear UI everyone already knows) instead of building a new picker from scratch.
  `matchCandidatesForEvent` does simple keyword-overlap scoring against the event's display name to
  auto-select an obvious single match or narrow the search box when there's more than one.
- Per-event overwrite confirm reuses the same `#auto-assign-confirm-overlay` (Cancel here = skip
  just this event, not the whole wizard). A separate always-visible `#auto-assign-wizard-bar` (new,
  sits above the category strip) shows progress ("event 4 of 22") plus **Skip Event** and **Cancel
  Wizard** — Cancel Wizard stops immediately; events already confirmed before that point stay
  assigned, nothing already-applied gets rolled back.
- `afterClipperAssignAction(trigger, assignedThisEvent, songName)` is the hook that makes the
  shared clipper-assign panel wizard-aware: if the wizard owns the currently-open trigger, Select/
  Browse/Trim/Clear advance to the next event instead of just closing the panel. Closing the panel
  manually (X button) while the wizard is active now cancels the wizard too, rather than leaving a
  dangling progress bar with nothing driving it.
- **Completion popup** (`#auto-assign-summary-overlay`, new): instead of a toast, shows a real list
  of every event touched — "Touchdown → Fight Song.mp3" etc, skipped ones marked distinctly — per
  the owner's ask to actually see what changed, not just a count.
- Also reachable from a **new header pill** (`#btn-auto-assign-header`) next to the team badge, so
  it's available on every screen (Sound Bank, My Downloads, etc.), not only after Set Matchup —
  operates on `state.activeTeam` same as the matchup-side-bar button always did.

### 2. Favorite team auto-loads on launch
- `favoriteTeam` (ConfigStore, already existed) previously only drove the profile-dialog label,
  win/loss record, and the manual jump-star button — never actually selected anything at startup.
  `state.activeTeam` came from `GetActiveTeam()`/last session state only.
- `init()` (app.js) now falls back to `userProfile.favoriteTeam` if `GetActiveTeam()` came back
  empty, so a fresh install (or a session that never explicitly picked a team) opens straight into
  the user's favorite instead of "General".

### 3. What's New popup no longer hardcoded
- Was `WHATS_NEW_CHANGELOG` (a manually-maintained array) + `WHATS_NEW_VERSION` (a manually-bumped
  constant) — completely separate from the real GitHub-Releases-backed sidebar changelog panel
  (`loadChangelog`/`GetChangelog`). Forgetting to update either meant the popup either kept showing
  stale text forever or silently stopped appearing for real new releases.
- Now `maybeShowWhatsNew()` (new) fetches the same live `GetChangelog()` feed, compares the latest
  release title against `localStorage["bandroom-whatsnew-seen"]`, and only pops if there's
  something genuinely new. `showWhatsNew()`/`dismissWhatsNew()` render/mark from that fetched data
  instead of the old constants. Called as `init().then(maybeShowWhatsNew)` at the bottom of app.js.

### 4. Big Game Rules editor (new feature, was backend-only before)
- `GameWatcher.cs`'s `isBigGame` was a hardcoded `quarter == 4 && Math.Abs(homeScore - awayScore)
  <= 8` constant with zero UI, zero config — the owner wanted to see and edit "current Big Game
  rules." There was no rivalry/ranking OCR signal to build a smarter rule from (checked
  `TeamColors.cs`/`scripts/team_registry.json` again this session, still nothing), so the editable
  version is the same close-score heuristic, just parameterized.
- New `ConfigStore.BigGameSettings` record `(Enabled, QuarterThreshold, ScoreMargin)`, persisted to
  `big_game_settings.json` under UserDataRoot, cached in memory (`_bigGameSettingsCache`) since
  `GameWatcher`'s OCR loop reads it every frame during a live game — don't remove that cache
  without replacing it with something else cheap, re-reading JSON off disk that often is wasteful.
- `WebBridge.GetBigGameSettings()`/`SaveBigGameSettings(enabled, quarterThreshold, scoreMargin)` —
  thin wrappers, no new business logic.
- New "Big Game Rules" panel in the Adjust sidebar (between Lead-In Whistle and Reverb): enable
  toggle, quarter-threshold dropdown, score-margin number input, Save button
  (`refreshBigGameSection`/`wireBigGameSection` in app.js).
- Separate **"Show Big Game badge on the matchup screen"** toggle — this is purely cosmetic/local
  (`localStorage["bandroom-biggame-banner"]`), not tied to the live in-game rule (there's no score
  yet on the pre-game matchup screen, so it can't be "live-linked" there). When on, an original,
  app-themed pulsing "BIG GAME" pill (`#matchup-big-game-badge`) shows just below the VS circle on
  `#matchup-vs-badge`. **Deliberately not** a reproduction of ESPN's trademarked College GameDay
  logo (owner pasted that as a style reference) — built our own glass/glow badge instead to avoid
  shipping someone else's trademark. Fixed box + no image scaling, so it can't stretch.
- If a real logo asset is ever wanted here instead of the text pill, it needs to be an original
  graphic (or explicitly licensed), not the ESPN asset from the reference image.

## Not done this session (still queued)

Per the owner's explicit priority order (auto-assign → bugs → global polish) plus items added
mid-session:
1. **Marketplace / My Downloads visual rebuild** — owner wants it rebuilt (not just restyled) using
   Bandroom's existing color-rule/LED-pulsing/glass system, referencing a dark music-library mockup
   (sidebar, playlist cards, filter pills, data table, bottom player bar) for layout ideas only, not
   literal reproduction. This is the single biggest remaining item. Not started.
2. Original Session 16 carryover bug list: team backgrounds bugged from saving, dead "Share
   Profile" link, album-flow arrow scroll speed.
3. Global polish batch: universal font-size bump (~10px → 12–14px), secondary-color glow should use
   the lighter of primary/secondary (currently sometimes renders as dark/black for teams like
   Arkansas/Georgia), HD waveform zoom, logo-delete restricted to owner only (others can still set
   their own), master Volume slider should override home/away event volumes, functional profile
   sharing + user-facing instructions, and a first-load-after-update "Getting Started" glass popup.
4. Verified this session: `dotnet build` clean (0 errors/warnings) and `node --check` clean on
   app.js, both before and after every change. **Not** click-tested live in a running window this
   session — computer-use tooling could only get an allowlist grant for the installed AppData
   Bandroom.exe, not the freshly-built dev exe, and the user denied elevating that further. Worth a
   manual run-through next session before trusting the wizard/Big Game panel further, especially
   the wizard's Cancel-mid-loop and per-event overwrite-confirm paths, which have the most new
   control flow.

## Starting a fresh session on this

1. `git log --oneline -5` and `git tag --sort=-v:refname | head -3` — confirm v1.0.65 is the latest
   and matches what's described above.
2. Working tree was **not committed** this session (matches the established pattern here — builds
   for release.ps1 come straight from the working directory, not a git commit; several prior
   sessions' changes are still sitting uncommitted too). If you want a clean git history at some
   point, that's a separate ask — don't commit unprompted.
3. Same pre-existing uncommitted Mac WIP files as every prior session (`src/Bandroom.Mac/*`,
   `AGENT_NOTES`, etc.) — still untouched, not mine, leave alone unless asked.
4. **"ppup" is the owner's trigger phrase for running `release.ps1`** — confirmed again this
   session (auto-mode's own classifier blocked the direct run and required an explicit yes/no even
   after the trigger phrase, which is correct: it's a real publish action, git tag + push + public
   GitHub release). Don't run it without that phrase or an explicit ask.
5. Next priority is the Marketplace/My Downloads rebuild (item 1 above) — biggest scope, needs its
   own look at the reference mockup plus a read of the current `#my-downloads-overlay`/
   `#bandroom-overlay`/`#bandroom-album-overlay` structure before touching anything.
