# Bandroom Handoff — Session 55 (2026-08-12)

Continuation of Session 54, live-fire during the same real game. Locked in Sessions 53-54's
uncommitted event work, fixed the possession-cooldown wrong-side-routing bug that Session 54 had
only diagnosed (not fixed), verified and committed a concurrent set of app.js/style.css fixes, and
added a Big-Game dual-fire pairing for the opening-kickoff moment. Build clean (0 warnings/errors)
throughout, 61/61 Core tests passing by end of session (59 carried over + 2 new).

## 1. Checkpoint — locked in Sessions 53-54's uncommitted event work

Going into this session, 5 files (`GameWatcher.cs`, `WebMainForm.cs`, `wwwroot/app.js`,
`wwwroot/index.html`, `wwwroot/style.css`) had uncommitted changes from Sessions 53-54's live-fire
work (2nd Down Short dual-fire pairing, Timeout gating off the real banner, standalone Kickoff cue,
3rd-down-conversion dedup, matchup font pass), plus Session 54's own handoff doc and two new
untracked files (`HOTKEYS.txt`, `wwwroot/fonts/Quicksilver.ttf`). None of it was committed.

- Added a "Current State" section to the top of `TASK_BOARD.md` (which was stale since 2026-08-08)
  summarizing the event system's status and the possession-cooldown investigation, so the next
  session/Cline doesn't have to re-derive context from chat history.
- Committed everything except the 22MB `Bandroom_dev_share_2026-08-11.zip` build artifact (same
  exclusion rationale as every prior session). Commit `541cd63`.
- Created annotated tag `v1.0.77` on that commit (owner: "lock the state").

## 2. Possession-cooldown wrong-side-routing bug — actually fixed this session

Session 54 had traced this live-reported bug (Tackle for Loss / Fourth Down routing to the wrong
team) to its root cause but explicitly recommended NOT fixing it — the proposed 3-part fix would've
loosened the confirm-gate/cooldown that were themselves fixes for two other separately live-reported
bugs (a single-frame phantom commit, and a repeating false-turnover loop during a CFB27 pause-menu
freeze). That recommendation was cross-checked against the actual code this session (confirmed
accurate, with one correction: the cooldown is 1.2s, not 2s as first estimated) and initially left
un-actioned per the owner's agreement.

Owner then asked to fix it anyway. Implemented the minimal, narrowly-gated version of "Part A" only
(not the margin-relaxation or stability-tiebreaker parts):

- `GameWatcher.cs` `SamplePossessionByUnderline`: a late-cooldown correction is now allowed, gated
  tightly enough to not reopen either prior bug — requires (a) at least half the cooldown (0.6s of
  the 1.2s) to have already elapsed, so an immediate flicker right after the first commit still
  can't undo it, and (b) a much stronger brightness margin (35 points vs the normal 25) than a
  routine read, so an ordinary borderline frame still can't trigger it. Still funnels through the
  exact same single commit path that updates `_lastPossession` and fires `PossessionChanged`
  together, so the 2026-08-11 lockstep-desync fix is unaffected either way.
- The color-match fallback (`SamplePossession`, legacy pre-underline presets — not what CFB27 uses)
  was deliberately left untouched: no equivalent brightness-margin signal exists there to gate a
  safe correction on.
- Confirmed via code trace that "Kam's CBSv3" and "College Football 27" both use the underline
  method (both have real calibrated `AwayUnderlineFxW`/`HomeUnderlineFxW`), so this fix applies
  identically to both — testing tonight on CFB27 is representative.
- Build clean, 59/59 passing after the change. Commit `54ff09f`. **Not yet live-verified** — watch
  for a repeat of wrong-side routing, and separately watch for any flicker/pause-loop regression
  since the correction path is new.

## 2b. Explained: how the routing pipeline actually works (no code change)

Walked through the full chain for the owner: `UserIsHome` is set once at matchup selection →
`GameWatcher.PossessionChanged` updates `WebMainForm._possession` live → `ResolveEventRouting`
applies three tiers (home-only-always hardcoded set → un-gated `Offense:*` always full volume for
whoever's driving → ordinary `Defense:*` home-always/away-during-Big-Game) → `FieldPositionVolumeMultiplier`
layers a CFB27-Big-Game-only ball-position volume balance on top → `FireEventForSide` plays the
assigned song. No code changed, just documented for context ahead of the fixes below.

## 3. Verified and committed a concurrent set of app.js/style.css fixes ("Claude working")

A separate set of uncommitted changes appeared in `wwwroot/app.js`/`style.css` mid-session (owner
flagged as other in-progress work, not from this session's own edits). Read and verified both
diffs before committing rather than blind-trusting them:

- `app.js`: matchup side-grid wheel-scroll wrap was triggering off the *projected* scroll position
  instead of whether the grid was actually resting at an edge — a single fast wheel/trackpad tick
  mid-list could overshoot past 0/max on its own and yank the whole list to the opposite end (owner
  report: "skipping over teams" / "teams aren't even present"). Rewritten to only wrap from a
  confirmed edge, with motion eased via `requestAnimationFrame` instead of an instant jump.
- `style.css`: `.matchup-controls-island` (the "Game Settings" popover) had no `[hidden]` →
  `display: none` override, so its unconditional `display: flex` rule always won over the browser's
  built-in `[hidden]` rule regardless of the JS correctly toggling the attribute — the popover
  silently never opened or closed. Fixed with an explicit override, same pattern
  `#team-picker-overlay[hidden]` already had. Also a cosmetic glossy "old iPhone icon" pass on
  `.team-swatch` (owner reference image) — rounder corners, stronger highlight/reflection layers.
- Syntax-checked `app.js` (`node --check`) before committing; no build needed (static assets only).
  Commit `bab7725`.

## 4. Pushed to GitHub

All of the above (3 commits: `541cd63`, `54ff09f`, `bab7725`) pushed to `origin/master` on request
— `ac0ff7b..bab7725`.

## 5. New: `Offense: After Opening Kick` — Big Game dual-fire with the Defense side

Live report: Home kicked off in a Big Game, `Defense: After Opening Kick` fired for Home (the
kicking team) with no song assigned — owner initially read this as wrong-side routing. Traced and
explained: this is actually correct-as-designed (already confirmed once before, Session 49) —
`DefenseFirstDownHelper`'s cue is intentionally attributed to the KICKING team's defense ("our D is
about to take the field"), not the receiving team. It's also `HomeOnlyAlwaysEventKeys`, so away
never got anything here at all. Owner's real ask, once clarified: in a Big Game both teams should
play this moment — receiving team (offense, has the ball) at full volume, kicking team (defense)
ducked under it, same balance as the existing `Offense/Defense: Second Down Short` pairing.

- New `OffenseAfterOpeningKickHelper.cs`: mirrors `DefenseFirstDownHelper`'s exact trigger condition
  (first snap after a kickoff-started drive, tracked independently rather than sharing state, same
  standalone-evaluator shape every other paired helper in this codebase uses) and fires
  `"Offense: After Opening Kick"` at flat 100 on the same tick.
- `DefenseFirstDownHelper.cs`: volume dropped from 85 to flat 60 (ducked counterpart).
- `WebMainForm.HomeOnlyAlwaysEventKeys`: `"Defense: After Opening Kick"` removed — now routes
  through ordinary `Defense:*` tiers (home always; away during Big Game at full volume via this new
  pairing; 25%/earned-only outside Big Game) instead of never playing for away at all.
- Registered `"Offense: After Opening Kick"` in `ConfigStore.AllEngineEventKeys` and the new
  evaluator in `GameWatcher.CreateEventRouter`'s rules array (Windows only — Mac's evaluator list is
  already missing several other evaluators from prior sessions, pre-existing drift not touched here,
  same note as Session 53's `Defense: Second Down Short` addition).
- 2 new tests (`OffenseAfterOpeningKickHelper_Fires_OnFirstSnapAfterKickoff`,
  `..._DoesNotFire_WhenSnapMissed`); existing `DefenseFirstDownHelper_Fires_OnFirstSnapAfterKickoff`
  updated to assert the new 60 volume. Build clean, 61/61 passing. Commit `fd3cc83`, **not yet
  pushed** (session ended before a push request came in for this one).

**Owner still needs to assign a song to the new `"Offense: After Opening Kick"` card before testing
tonight** — it's a fresh event key, currently empty, same as the Defense one was.

## Build/test status

- `dotnet build BandAudioHook.csproj` — clean, 0 warnings/errors after every change this session.
  App (`Bandroom.exe`) was locking the build output twice mid-session (PIDs 15128, then 14840) —
  killed after explicit owner confirmation each time.
- `dotnet test src/Bandroom.Core.Tests` — **61/61 passing** by end of session (59 carried over + 2
  new for `OffenseAfterOpeningKickHelper`).

## Real next steps

1. **Live-verify tonight**: the possession-cooldown correction (item 2) and the new
   `Offense: After Opening Kick` pairing (item 5) are both build-verified only, not yet confirmed
   against a real game. Watch for: wrong-side routing actually resolving, no new flicker/pause-loop
   regression from the cooldown correction, and both sides of the opening-kick pairing firing
   correctly once a song is assigned to the new Offense card.
2. **Push commit `fd3cc83`** (the After Opening Kick change) once the owner confirms it's ready —
   everything before it is already on `origin/master`.
3. **Assign a song** to the new `"Offense: After Opening Kick"` card.
4. Carried over from every prior session: v1.1 release (`release.ps1`) still not run — nothing from
   Sessions 46-55 has been released yet. Font pick for the matchup-screen team name (Anton/Racing
   Sans One/Bungee/Alfa Slab One shortlist) still open (Session 53).
5. Carried over from Session 53's code-review triage (still not acted on): `HighPriorityOverlapGrace`
   per-channel scoping, `SamplePossessionFromWindow`'s freeze-frame gap (separate from this
   session's cooldown fix), `WebMainForm`'s duplicated profile-fallback logic, `ScorebugPreset`'s
   hand-computed timeout-mirror offsets.
