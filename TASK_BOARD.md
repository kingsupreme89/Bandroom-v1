# Bandroom Task Board — Last Updated: 2026-08-07 (orchestrator pass)

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