# Bandroom Task Board — Last Updated: 2026-08-12 (Session 54 checkpoint)

## Current State — 2026-08-12 (read this first)
Event system is in good shape: all 38 active event keys (`ConfigStore.AllEngineEventKeys`) are
wired, assignable, and routing correctly as of this checkpoint. Full list of active keys is in
this session's chat log; the retired/blocked ones (bare "1st/2nd/3rd/4th Down", "(Midfield)"
variants, a few others) are intentionally left out — no real yard-line data yet, or superseded by
a newer key, see `ConfigStore.RetiredEventKeys`/`BlockedEventKeys` for the full reasoning per key.

**This session's event fixes (Sessions 53-54, uncommitted going into this checkpoint):**
- New dual-fire pairing `Defense: Second Down Short` (mirrors the existing 3rd-down-short pairing,
  inverse balance — offense loud/defense ducked here, opposite of 3rd short).
- `TimeoutHelper` now gates on the actual scorebug "Time Out" banner (`IsTimeout`) instead of a
  `TimeRemainingSeconds > 240` heuristic that silently dropped early-game timeouts. Follow-up fix
  same session: extracted `SampleTimeoutsFromWindow` so timeout-segment counts get sampled every
  tick, independent of the `situationActive` guard that was blocking it from ever seeing the count
  decrement while the banner was up.
- `Other: Kickoff` cue restored as a standalone cue independent of `Offense: PAT Made` — previously
  a missed PAT OCR read meant total silence after a score, no kickoff cue at all.
- 3rd-down conversions now fire only `Offense: 3rd Down Conversion`, no longer double up with the
  generic `Offense: Earned First Down` on the same snap (owner wanted just the specific cue).
- Matchup-screen team-name font pass (SPORTY/TROY reference iterations) — cosmetic, still open:
  owner asked for a real embedded font (Anton/Racing Sans One/Bungee/Alfa Slab One shortlist) to
  replace the current Arial-Black-plus-CSS-transform approximation; not picked yet.

**Possession-detection wrong-side-routing bug — root cause confirmed, minimal fix now applied:**
Live reports (Tackle for Loss, Fourth Down) routed to the wrong team's audio. Traced to
`GameWatcher.cs`: `SamplePossessionByUnderline`/`SamplePossession` require 2 consecutive matching
ticks to commit a flip (`ConfirmPossessionFlip`), then lock a `Cooldown` (1.2s, NOT 2s as first
estimated) during which no further possession updates were accepted at all — including correct
ones. If a replay overlay or camera cut produced 2 bad consecutive ticks that committed a phantom
flip right as the cooldown started, `_lastPossession` stayed wrong for the full 1.2s, and any
routed event (`Defense:`/`Offense:` prefix) firing in that window went to the wrong side.
A broader 3-part fix was proposed and considered too risky (loosening the margin or the
confirm-gate itself, which exist to fix two OTHER separately live-reported bugs — a single-frame
phantom commit, and a repeating false-turnover loop during a CFB27 pause-menu freeze, both
documented inline in `GameWatcher.cs` around `_pendingPossession`'s declaration).
**Fix actually applied (2026-08-12, `SamplePossessionByUnderline` only):** a late-cooldown
correction is now allowed, but gated tightly enough not to touch either of those two prior fixes —
requires (a) at least half the cooldown (0.6s) to have already elapsed, so an immediate flicker
right after the first commit still can't undo it, and (b) a much stronger brightness margin (35
points vs the normal 25) than a routine read, so an ordinary borderline frame still can't trigger
it. Still funnels through the same single commit path that updates `_lastPossession` and fires
`PossessionChanged` together, so the lockstep guarantee from the 2026-08-11 desync fix is
unchanged. The color-match fallback (`SamplePossession`, legacy pre-underline presets, not what
CFB27 uses) was deliberately left untouched — no equivalent brightness-margin signal exists there
to gate a safe correction on. Build clean, 59/59 Core tests passing after the change — **not yet
live-verified in an actual game**, watch for wrong-side-routing reports to confirm this resolves
it (and doesn't reintroduce a flicker/pause-loop regression).

No manual possession override exists (discussed as a fallback in Session 51, still not built).

## Claude → Orchestrator (2026-08-08)
- [FIXED, NEEDS LIVE VERIFICATION] Live testers reported: random song firing on the pause
  screen, occasional double audio, "1st down" song firing inconsistently. Root cause found in
  `GameWatcher.cs` `RouteEngineTick()`: `awayscore`/`homescore`/`quarter` regions fed
  `PlaySnapshot` straight from `region.Last`, which nulls (reads as 0) on ANY blank OCR tick --
  not just between plays, but during pause menus/replay overlays/cutscenes where the scorebug
  isn't drawn at all. `FieldGoalPATHelper`/`FieldGoalMissedHelper`/`SafetyHelper` all fire on a
  single-tick `HomeScore+AwayScore` delta with no debounce -- a real score (e.g. 14) blanking to
  0 on a pause screen then rebounding to 14 on resume reads as "+14 just happened," and any
  transient single-digit OCR misread landing on exactly +1/+2/+3 would false-fire a PAT/2pt/FG
  cue outright. Fixed using the same "sticky last-known value, never nulled on blank" pattern
  already proven for `_lastKnownDown` (Session5 bug #3): added `_lastKnownAwayScore`/
  `_lastKnownHomeScore`/`_lastKnownQuarter`, updated only on a real parsed OCR value,
  `RouteEngineTick` now reads these instead of the raw region `.Last`. `Bandroom.Core` rebuilds
  clean; `BandAudioHook.csproj` (where `GameWatcher.cs` actually lives) could NOT be rebuilt to
  confirm at time of writing -- `Bandroom.exe` was running locally and locking the output DLL.
  **Next session/whoever picks this up: rebuild `BandAudioHook.csproj` first thing and confirm
  0 errors before trusting this fix is live**, then watch `ocr_debug.log` during an actual pause
  menu for a `HomeScore`/`AwayScore` snapshot value that no longer drops to 0.
- [KNOWN, NOT YET FIXED] Deep-audit pass (background agent, full report given to owner) on
  Cline's last uncommitted push found: `bridge.DuplicateProfile(...)` (app.js:1305, wired to a
  real right-click context-menu item) calls a `WebBridge` method that doesn't exist -- throws on
  click, user-reachable. `bridge?.PlaySoundboardSlot(...)` and `bridge.ScanDynastySave()` are
  the same class of bug but currently unreachable (soundboard UI permanently hidden; Dynasty
  scan has no caller at all). Also: `wwwroot/ui-bot.js` auto-runs 1.5s after every page load in
  production and can pop a user-visible "UI Bot: N critical, N warnings found" toast -- looks
  like a dev diagnostic left wired into the shipped build rather than gated behind a debug flag,
  needs an owner decision on whether to gate it or remove it before the next push.
- [IN PROGRESS] Owner-requested audio engine changes, both build-verified in isolation (not yet
  confirmed against a locked `BandAudioHook.csproj` build, see above): (1) `AudioPlayer.Play` /
  `WebMainForm.PreviewEventFromWeb` now take an `isPreview` path that skips the 1s pre-roll delay
  and skips the 20s same-file `FireCooldown` for manual Preview-button clicks only -- real
  in-game triggers keep both. (2) `TrimmerForm.cs`: releasing the End slider/handle (drag or
  arrow keys) now auto-previews the last 4 seconds up to the new end point. (3) New optional
  gapless "lead-in whistle" playback path (`AudioPlayer.LeadInEnabled`/`LeadInClipPath`,
  `SequencedSampleProvider`) -- off by default, needs the owner's actual whistle clip and a UI
  toggle wired in before it's usable end-to-end.

## Cline → Orchestrator
- [20:5x] CONFIRMED ROOT CAUSE — "event trigger system hasn't fired on both pushes" (user report,
  Auburn @ Georgia Tech matchup, Watching on, real songs assigned, nothing played):
  `WebMainForm.cs:78` now sets `_useEngineForEvents = true` unconditionally in the constructor
  (Cline's fix, comment says "Fixed 2026-08-07", intent was to stop events dying before Set
  Matchup was pressed). But `OnDownChanged` (`WebMainForm.cs:962`) — the ONLY code path that ever
  fires the `Trigger`-keyed `"1st Down"/"2nd Down"/"3rd Down"/"4th Down"` entries — opens with
  `if (_useEngineForEvents) return;`. Since that flag is now permanently true, `OnDownChanged`
  always bails immediately: these four entries can never fire again, full stop, regardless of OCR
  correctness.
  The engine's replacement evaluators do NOT emit those literal keys — `OffenseDownHelper.cs`
  emits `"Offense: Second Down"` / `"Offense: Third Down"`, `BigEventHelper.cs`/`DefenseHelper.cs`
  emit `"Defense: Third Down"` / `"Defense: Fourth Down"` (opponent-facing "they're in a tough
  spot" cues, not "it is now Nth down for you"). There is no evaluator anywhere that emits a bare
  `"1st Down"`/`"2nd Down"`/`"3rd Down"`/`"4th Down"` key — confirmed via full grep of
  `src/Bandroom.Core/Helpers/*.cs`. So even a perfect fix to the routing gate would not bring
  these back; the engine has no equivalent event to route.
  Worse: `ConfigStore.BuildDefault()` (`ConfigStore.cs:669-672`) still manufactures these same 4
  dead `Trigger`-keyed entries — including a pre-filled default 4th-down file
  (`dies irie 0.wav`) — for every new/existing profile via `EnsureAllEvents`. So this isn't just
  "user assigned to stale slots": the app itself ships dead UI slots with a real default sound
  that can never play, for every team, out of the box.
  This matches the Georgia Tech profile exactly (`UserData/Profiles/Georgia Tech.json`): real,
  existing files assigned to `"1st Down"`/`"2nd Down"`/`"3rd Down"`/`"4th Down"`, all orphaned.
  **Not yet fixed** — needs a decision, not a blind patch: either (a) remove/hide the 4 legacy
  Down entries from BuildDefault + UI and have OffenseDownHelper/BigEventHelper/DefenseHelper
  cover 1st down explicitly too (currently only 2nd/3rd are covered on offense, nothing fires on
  a *fresh* 1st down at all — FirstDownHelper only fires on *earning* a first down via a play, a
  different concept), migrating any already-assigned legacy files across; or (b) add a
  compatibility alias in `FireEventForSide`/`ConfigStore` mapping the legacy keys to their nearest
  canonical engine key so already-assigned user files keep working without a data migration.
  Adjacent behavior checked and NOT affected by the same class of bug: `OnRegionChanged`'s
  legacy `SideAwareEvents` fallback (touchdown/turnover/pat_good/kickoff) is also gated on
  `_useEngineForEvents` and also permanently dead now, but the engine's `TouchdownHelper`/
  `TurnoverHelper`/`FieldGoalPATHelper`/`KickoffHelper` emit the exact same EventKey strings
  those mapped to, so no regression there — those events fire fine through the new path.

## Build Status (verified directly, not self-reported)
```
Bandroom.Core.dll   → 0 errors, 0 warnings
Bandroom.dll (Win)  → 0 errors, 0 warnings (all 5 pre-existing warnings cleaned earlier tonight)
Bandroom.Mac.dll    → 🔴 BROKEN — 78 errors in MacWebBridge.cs (see below)
```

## 🚨 Mac build is currently broken — Cline mid-flight on WebView bridge
`MacWebBridge.cs` calls ~15 methods on `MainWindow` (window drag/minimize/maximize/close,
profile import/export, changelog, PA/UI click sound, matchup lock, copy-to-all-teams) that
`MainWindow` doesn't implement yet, plus references `ChangelogService` without it being
in scope. This is very likely just mid-edit WIP (Cline actively building the WebView bridge,
Priority 2 item #1), not a regression I introduced — but flagging clearly since "Mac: 0 errors"
was true earlier tonight and is NOT true right now. Whoever's driving Mac work next should
either finish `MainWindow`'s missing methods or treat this file as not-yet-integrated.

## OCR calibration pass (from live screenshots the user provided, 1920x1080 CBS skin)
- ✅ CALIBRATED — `awayscore`/`homescore`/`clock` regions added (tight positional crops, since
  bare digits have no unique regex signature the way "KICKOFF" does). Now feeding
  `PlaySnapshot.HomeScore/AwayScore/TimeRemainingSeconds` instead of hardcoded 0. Coordinates
  are visually estimated from screenshots, not pixel-measured -- a real starting point, expect
  minor tightening needed on first live run.
- ✅ CALIBRATED — `flag` region enabled: turned out to share the exact same crop box as
  down/situation/quarter (yellow "FLAG" text in the same rightmost box), not a separate banner.
- ✅ CALIBRATED + WIRED — penalty SIDE detection. The scorebug's flag ribbon alone can't say
  which team was penalized (fixed yellow, not team-colored). Found the real signal: the game's
  penalty accept/decline overlay shows "Against &lt;Team Name&gt;" text. Added `penaltyagainst`
  region + `GameWatcher.HomeTeamName`/`AwayTeamName` (set from `SetGameTeamsFromWeb`) to compare
  against, resolving `IsPenaltyOnOffense`/`IsPenaltyOnDefense` from (who was flagged) x (who has
  the ball right now). Estimated crop from a single screenshot -- may need widening for other
  penalty types/overlay variants.
- 🔴 FOUND + FIXED (real bug, now live-relevant) — `WebMainForm.cs` routed events by
  `EventKey.StartsWith("Defense:")`, but `PenaltyHelper`'s own comments state "Offense penalty →
  fire for the defense side." Since "Penalty: Offense" doesn't start with "Defense:", it was
  silently routing to the WRONG side (offense's own fans instead of defense celebrating the
  opponent's mistake). "Penalty: Defense" happened to route correctly by coincidence. This was
  moot until penalty-side OCR existed to ever populate the flag -- fixed now that it does.
- ✅ CALIBRATED + WIRED — timeouts remaining. User confirmed ground truth from a screenshot
  (away=3 lit, home=0 lit), which validated the dash marks ARE the timeout indicator. Since
  `Windows.Media.Ocr` reads text, not graphical tick marks, this is solved via pixel-brightness
  sampling instead -- new `GameWatcher.SampleTimeoutSegments()`, same crop+screenshot technique
  as `SamplePossessionFromWindow` but averaged per-segment (3 assumed segments) instead of across
  the whole box, counting how many are "lit" (luminance >= 128) vs dark. New
  `ScorebugPreset.AwayTimeoutFx*` fields hold the crop (away only -- `TimeoutHelper` only ever
  reads `AwayTimeoutsRemaining`, and this app always treats the user's own team as home, so
  "away" already means "the opponent" by existing design convention). `_lastAwayTimeoutsRemaining`
  defaults to -1 ("not sampled yet") so `TimeoutHelper`'s own range check correctly just won't
  fire until real data comes in, rather than risking a wrong guess. **Estimated crop + untested
  threshold** -- higher uncertainty than the OCR-text regions since this is a brightness
  heuristic, not exact text matching; needs real live verification before trusting it.
- ✅ CALIBRATED — the full-screen scoring banner (TOUCHDOWN/FIELD GOAL/SAFETY ribbon). User
  provided a live TOUCHDOWN screenshot; `banner` region now has real FxX/FxY/FxW/FxH instead of
  0/0/0/0. Estimated crop, not pixel-measured.
- 🟡 NOTED, not implemented — the "KICKER RANGE" overlay (4th down / FG decision moments) shows
  literal text like "TARGET LINE: 40 YARD LINE" -- a much more OCR-friendly YardLine source than
  trying to read a tiny number off the persistent scorebug, but only appears situationally, not
  every play. Worth building as a supplemental YardLine source later, not attempted tonight.

## New bugs found + fixed this pass (orchestrator, direct code read + rebuild each time)
- ✅ FIXED — `ConfigStore.BuildDefault()` only created rows for 6 of ~41 event types the engine
  can fire; the other ~24 (Second/Third/Fourth Down variants, Field Goal Made, Safety, both
  Penalty events, all 5 Timeout variants, Victory in Hand, Iced Game, Drive Starter, etc.) had
  no assignable slot in the UI at all, ever, regardless of OCR correctness. Fixed via
  `ConfigStore.AllEngineEventKeys` + `EnsureAllEvents()`, wired into every load path. Confirmed
  live in-app: went from ~11 events to 46 across all category tabs.
- ✅ FIXED — `CategoryMap.cs` had a dead entry and was missing Penalty/Timeout category mappings
  (would've shown as "Hype" once the above was fixed).
- ✅ FIXED — `situation-name` card titles were rendering blank once ~35 more cards started
  showing the "Coming Soon" badge (regression from the fix above stressing an untested layout) —
  raw text node had nowhere to shrink; now wrapped in its own flex item with proper ellipsis.
- ✅ FIXED — Windows app had 5 pre-existing build warnings (nullable annotations, one dead
  field) — all cleaned, 0 warnings now.
- 🔴 FOUND, NOT FIXED (real risk, cheap to close) — no `.gitignore` existed at all; two files
  with LIVE secrets (`admin_token.local.txt`, `google_client_secret.local.txt` — a real Google
  OAuth client secret) sit in plaintext at repo root. Added `.gitignore` covering `*.local.txt`,
  `*secret*`, `*token*`, plus bin/obj/WebView2Data. This project isn't a git repo yet, but
  Roadmap B docs reference it already being on GitHub elsewhere — do NOT `git init && git add .`
  without confirming this `.gitignore` is in place first.
- 🟡 FOUND, NOT FIXED (needs a product decision, not a blind fix) — `AudioDuckingController.cs`
  is fully built (duck/fade state machine, presets) but is never instantiated anywhere in the
  codebase — 100% dead code, not "wired to a subset" as Roadmap B #12 guessed. Needs a decision
  on when ducking should actually trigger before wiring it in.
- ⚠️ MISMATCH (earlier this session, still valid) — Cline's own audit recommended gating
  `OnTackleForLoss`; WebMainForm.cs:844-847 has an explicit owner comment saying keep it
  ungated intentionally. Not applied.

## New feature built this pass: PA Announcer layer
Per explicit request: a second, independent audio layer per situation (announcer clip playing
alongside the main song), its own volume slider, reusing the existing song library/upload/trim
pipeline rather than building a new one.
- `TriggerEntry.PaAudioFile` (new field, backward-compatible — old profiles deserialize it as "").
- `AudioPlayer.PaVolume` (independent of Master/Home/Away).
- `AssignTrackForm` refactored to be field-agnostic (was hardcoded to `entry.AudioFile`
  throughout) so the same dialog now assigns either the main song or the PA clip.
- New bridge methods: `AssignPaEvent`, `SetPaVolume`/`GetPaVolume`.
- New UI: "Assign PA" button per event card, "PA: <filename>" line, new PA Volume slider.
- **Verified by**: clean rebuild (0 errors/warnings) + direct code trace of the full call chain
  (FireEvent → both AudioFile and PaAudioFile play concurrently via independent Task.Run calls,
  confirmed interruptPrevious ordering doesn't let the PA call's StopAll() kill the main clip).
  **NOT yet live-tested in the running app** — build-verified only, someone should assign a PA
  clip and confirm it actually plays alongside the main song in a real session.

## UI decluttering (explicit request: "island tabs" felt cluttered)
- Every one of the 46 event cards used to pulse its outline glow constantly, all at once,
  forever (`situation-row-outline-pulse`, infinite animation) — noisy at scale. Now static/quiet
  at rest, glow appears only on hover.
- Tightened card padding/gap, smaller max-width, smaller button padding/font so the new 4th
  button (Assign PA) fits without overflowing the card.
- Not a full visual redesign — a real but scoped decluttering pass. Full redesign would need
  live visual iteration I can't do without a way to render this native WinForms app myself.

## Marketplace
| # | Task | Status |
|---|------|--------|
| 1 | 15-level audit (worker, download service, WebBridge, UI) | ✅ |
| 2 | Identified KV write bottleneck (counter endpoints) | ✅ |
| 3 | Cloudflare Workers Paid Plan — $5/mo | ✅ ACTIVE |
| 4 | KV write cap removed — unlimited writes | ✅ ACTIVE |
| 5 | R2 storage OK — pay-as-you-go | ✅ |

## Cloudflare Links
- Billing/Plans: https://dash.cloudflare.com/?to=/:account/workers/plans
- Worker deploy: `npx wrangler deploy` (from cloudflare-marketplace/ folder)
- Worker URL: https://bandroom-marketplace.bandroom.workers.dev

## Completed Today
| # | Task | Status |
|---|------|--------|
| | Engine integration (GameWatcher + csproj + WebMainForm) | ✅ |
| | EventsDetected → FireEventForSide wiring | ✅ |
| | Bug fixes: UserIsHome, first-tick storm, duplicate handlers | ✅ |
| | TimeoutHelper EventKey format fixed | ✅ |
| | Deep audit (15 levels) — 3 bugs found, 2 fixed | ✅ |
| | Default song mapper: 950/1056 files, 62 teams | ✅ |
| | ImportDefaultPackForTeam() in ConfigStore | ✅ |
| | Mac app bootstrapped (Avalonia, AudioPlayer, EventRouter) | ✅ |
| | Marketplace audit (15 levels) — KV bottleneck identified | ✅ |
| | Cloudflare Workers Paid Plan activated | ✅ |

## For Claude / Next Session
- Default pack download-on-first-launch (1 GB)
- Marketplace front-end in app.js (upload/download UI if missing)
- Mac: WebView embedding, GameWatcher (Vision OCR), KeyboardHook (Carbon)