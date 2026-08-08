# Bandroom Handoff — August 8, 2026 (Session 6)

Picks up right after Session 5 ("OMG ITS WORKING... SOME"). This session: pushed Cline's pending
work as a testable build, chased down the specific bugs live testers reported, added several
owner-requested audio/UI features, and did a full state-machine audit of the event engine.

**⚠️ BUILD STATE:** `Bandroom.Core` builds clean (0 errors/warnings) as of the last check this
session. `BandAudioHook.csproj` (the Windows app, where most of the actual fixes live —
`GameWatcher.cs`, `WebMainForm.cs`) has **not been rebuilt since the state-machine fixes below**
— the locally-running test `Bandroom.exe` was locking the build output and testing was still in
progress at session end. **First thing next session: close that instance, run
`dotnet build BandAudioHook.csproj`, confirm 0 errors, then push.**

---

## Git state

- `273ab8e` — pushed early this session: all of Cline's pending uncommitted work (PA announcer
  layer, 46-event fix, OCR calibration, engine event routing). Deep-audited first (see below).
- `de69a0e` — pushed mid-session: pause-screen/double-audio bug fix, snappier previews, trimmer
  end-preview, lead-in whistle engine, per-event volume.
- **Not yet committed/pushed:** the state-machine fixes (`ConfigStore.cs`, `GameWatcher.cs`,
  `WebMainForm.cs`, and 5 files under `src/Bandroom.Core/Helpers/`) — blocked on the build
  verification above.

---

## 1. Deep-audited Cline's pending push before trusting it (→ `273ab8e`)

Ran a full deep-audit (rebuild + read the actual diff + check one level past every change) rather
than trusting Cline's own claims. Findings:

- **Build status verified directly**: `Bandroom.Core`, `BandAudioHook.csproj`, and
  `Bandroom.Mac.csproj` all built clean. Mac in particular was a real improvement — TASK_BOARD had
  it at 78 errors as of the last update; `PlatformStubs.Mac.cs`'s `TeamBackgroundDownloadService`
  went from a stub to a real implementation and it now builds.
- **Real bug found, not yet fixed**: `bridge.DuplicateProfile(...)` (`app.js:1305`) is wired to a
  real right-click context-menu item but calls a `WebBridge` method that doesn't exist — throws on
  click. `PlaySoundboardSlot`/`ScanDynastySave` are the same class of bug but currently
  unreachable (no UI entry point).
- **Flagged, needs an owner decision**: `wwwroot/ui-bot.js` auto-runs 1.5s after every page load
  in production and can pop a user-visible "UI Bot: N critical, N warnings found" toast — looks
  like a dev diagnostic tool left wired into the shipped build. Gate it behind a debug flag or
  remove it.
- A pile of dead/unwired scope creep (`unlockAchievement`, `renderLeaderboardTable`,
  `followUser`/`unfollowUser`, `generateQRCode`, party system, etc.) — defined, zero callers,
  harmless but bloat.

**None of the above dead-code/broken-button items were fixed this session** — logged to
`TASK_BOARD.md`, still open.

---

## 2. Root-caused the live testers' bug reports

Owner reported from live testing: random song firing on the pause screen, occasional double
audio, "1st down" song firing inconsistently.

**Root cause** (`GameWatcher.cs` `RouteEngineTick()`): `awayscore`/`homescore`/`quarter` OCR
regions fed `PlaySnapshot` straight from the raw region read, which nulls (reads as 0) on ANY
blank OCR tick — not just between plays, but during pause menus/replay overlays/cutscenes where
the scorebug isn't drawn at all. `FieldGoalPATHelper`/`FieldGoalMissedHelper`/`SafetyHelper` all
fire on a single-tick score delta with **no debounce** — a real score (e.g. 14) blanking to 0 on a
pause screen then rebounding to 14 on resume reads as "+14 just happened." Any transient
single-digit OCR misread landing on exactly +1/+2/+3 would false-fire a PAT/2pt/FG cue outright.

**Fix**: same "sticky last-known value, never nulled on blank" pattern already proven for
`_lastKnownDown` (Session 5 bug #3) — added `_lastKnownAwayScore`/`_lastKnownHomeScore`/
`_lastKnownQuarter`, updated only on a real parsed OCR value. `RouteEngineTick` reads these
instead of the raw region `.Last`.

**Still needs live verification**: grep `ocr_debug.log` during an actual pause menu for a
`HomeScore`/`AwayScore` snapshot value that no longer drops to 0.

---

## 3. Owner-requested audio/UI features

- **Preview snappiness** (`AudioPlayer.cs`, `WebMainForm.PreviewEventFromWeb`): manual Preview
  clicks on assign cards now skip the 1s pre-roll delay and the 20s same-clip `FireCooldown` gate
  — both existed for real in-game triggers and were making previews feel broken/laggy (clicking
  Preview twice on the same clip within 20s silently played nothing).
- **Trimmer end-preview** (`TrimmerForm.cs`): releasing the End slider/handle (drag or arrow keys)
  now auto-plays the last 4 seconds up to the new end point.
- **Gapless lead-in whistle**: new `SequencedSampleProvider` (zero-gap concatenation, not a
  sleep-then-play hack) plays an optional short clip immediately before every real triggered clip
  AND every manual preview. Clipped via a new "Set as Lead-In Whistle" button in `TrimmerForm`
  (writes to `ConfigStore.LeadInWhistlePath`, a single fixed file). On/off toggle persists across
  restarts (`ConfigStore.LoadLeadInWhistleEnabled`/`SaveLeadInWhistleEnabled`), surfaced in the
  Mixer panel — only shown once a whistle clip actually exists.
- **Per-event volume**: `TriggerEntry.Volume` (0-100, default 100, backward-compatible on old
  JSON). Small volume-icon button on each event card pops a slider + close/X, matching the
  interaction pattern the owner described wanting for PA sounds. Applied as a multiplier on top
  of Master/Home/Away/PA in both `FireEvent` and `PreviewEventFromWeb`.
- **Deployed both Cloudflare workers** (`bandroom-marketplace`, `bandroom-usercount`) on request
  ("lehgo").

## 3a. Explicitly reverted this session

- **Font swap attempt**: owner asked for a font matching a "MICROSPORT"-style image, went through
  Anton → Helvetica Neue (proprietary, can't bundle) → "closest to Mac" (downloaded Inter) →
  finally clarified they meant the app's own existing native font. **Fully reverted** — `git
  checkout` on `style.css`/`ui-bot.js`, downloaded font files deleted. App is back on `Outfit`
  (self-hosted in `wwwroot/fonts/` — note that directory was actually EMPTY before this session's
  font work started, meaning the `@font-face` rules referencing `fonts/Outfit-*.ttf` may have
  never resolved in the web UI at all; the native WinForms side has its own separately-embedded
  copy in `Fonts/` via `AppFonts.cs` and is unaffected. **Worth checking**: does the web UI
  actually render in Outfit today, or has it been silently falling back to Segoe UI this whole
  time? Not investigated further this session.)

---

## 4. State-machine audit (`docs/STATE_MACHINE_ANALYSIS.md`)

Owner supplied a full state-machine audit doc (written by a separate audit pass, not this
session) listing 10 numbered discrepancies. **Cross-checked every one against the actual current
code** before touching anything (per the deep-audit skill's rule: a report is a claim, not a
fact) — all 10 checked out as accurately describing the real code, not phantom findings.

**Fixed:**
| # | Bug | Fix |
|---|---|---|
| 1 | `TimeoutHelper` fired every ~250ms tick (level-triggered, no edge detection) | Now only fires on an actual `AwayTimeoutsRemaining` decrement |
| 2 | `DownFieldPositionHelper`'s "Midfield" check was always-true (`YardLine` OCR never built, hardcoded 0) | Gated behind `YardLine > 0`, same pattern `FirstDownHelper` already used |
| 3 | `DefenseHelper` + `DownFieldPositionHelper` both fired the same "Second/Third Down (Loss)" cues → audible stop-start glitch | Removed the duplicate branches from `DownFieldPositionHelper` |
| 4 | 3rd-down sack-fumble could fire 2-3 different cues from `DefenseHelper`/`BigEventHelper`/`DownFieldPositionHelper` simultaneously | Added a `NewPossession` guard to `DefenseHelper` so `BigEventHelper` owns that case |
| 5 | A safety and a 2-point conversion both produce a `+2` total-score delta — safety was ALSO firing "Offense: 2-Point Conversion Made" | `FieldGoalPATHelper` now checks which side's own score actually moved |
| 7 | `_lastDistanceRaw` (YardsToGo) wasn't sticky — could read as 0 right after a pause/resume | Same sticky-value fix as the score/quarter bug above |
| 10 | No `"Offense: Fourth Down"` event existed anywhere — legacy `down:4th` assignments were permanently silent | Added to `OffenseDownHelper` + `LegacyDownEventAlias`; kept off the UI (added to `RetiredEventKeys`, same bucket as 2nd/3rd Down) since it fires automatically |

**Deliberately NOT applied — #9**: the doc recommended gating `OnTackleForLoss` behind
`_useEngineForEvents`. Checked one level past the recommendation and found an existing owner
comment in `WebMainForm.cs` explicitly asking for TFL to stay ungated as an intentional
exception. Applying the doc's fix would have silently broken that decision — flagged instead of
applied, consistent with the deep-audit skill's own worked example (this is almost the identical
scenario).

**Left open — #6**: `FieldGoalMissedHelper` likely never fires in practice — the "situation" OCR
region only ever captures success text ("PAT GOOD"/"TOUCHDOWN"), nothing for a missed attempt.
Needs new OCR capability (or a heuristic: possession flip + 4th down + no score change + time
elapsed), not a quick code fix. Not attempted this session.

**#8** was cosmetic (a confusing comment describing correct logic) — not touched.

---

## 5. Default song pack — status, not finished

`DefaultSongPackService.cs` (client download/extract logic) and the `cloudflare-defaultsongs`
worker (deployed, R2 bucket `bandroom-default-songs` exists) are both in place. **Not confirmed
this session whether `pack.zip` has actually been uploaded to that R2 bucket** — was mid-check
when the owner redirected to other priorities, then to the handoff. `Songs_Default_Pack.zip` sits
untracked at the repo root (large binary, not pushed to git). **Next session: verify with
`npx wrangler r2 object get bandroom-default-songs/pack.zip` (or list bucket contents) whether the
pack needs uploading, and if so, upload it** — the client-side download flow can't work until it
does.

---

## What "next session" should do, in order

1. Close the locally-running test `Bandroom.exe`, rebuild `BandAudioHook.csproj`, confirm 0
   errors, commit + push the state-machine fixes (§4 above).
2. Verify the pause-screen/score-blanking fix live — grep `ocr_debug.log` during an actual pause.
3. Finish the default song pack: confirm/upload `pack.zip` to R2 (§5).
4. Fix or remove the broken `DuplicateProfile` context-menu item (§1) and decide on `ui-bot.js`'s
   production toast.
5. Check whether the web UI's `Outfit` font is actually loading (the `wwwroot/fonts/` gap noted
   in §3a) — separate from, and probably predates, this session's font-swap detour.
6. Still-open architectural questions from earlier sessions, unchanged: away-team-offense-songs
   structurally unreachable (`UserIsHome` hardcoded true), Team Builder/custom teams get no color
   or possession auto-detection (no product decision made on either yet).
