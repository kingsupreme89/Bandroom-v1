# Bandroom Handoff — August 8, 2026 (Session 4)

Picks up from Session 3 (PA Announcer, 46-event fix, OCR calibration, download counter, slim
installer, deep audit of Cline's concurrent event-firing fixes). This session covered: a live
test hook for firing events without a real game, the actual root cause of "nothing plays" (not
what Session 3 fixed), a real bug in the What's New popup that silently blocked the entire app,
an Offense/Defense/Situations UI reorg, event-list simplification, live OCR diagnostics, and
several live bugs found from screenshots during real gameplay testing.

**⚠️ BUILD STATE WARNING:** the currently-running dev instance (`Bandroom.exe`, launched during
this session) was built at 23:54 from a `ConfigStore.cs` that was then edited again at 00:01 (the
Midfield-variant-removal / "Got 1st Down" simplification pass, see section 5). **That last edit
was never rebuilt or relaunched.** Anyone continuing this session should `dotnet build` and
relaunch before testing anything — what's currently running on screen is one step behind what's
in the source files.

---

## 1. The REAL root cause of "nothing plays for any down" (this session's big find)

Session 3 fixed the `_useEngineForEvents`/`OnDownChanged` dead-code bug. That fix was real and
correct, but it wasn't why the owner's live game had silence. The actual cause, found this
session by tracing `ToggleWatchingFromWeb`/`ConfirmGametimeFromWeb`:

**Pressing GAMETIME never started the OCR watcher.** `ConfirmGametimeFromWeb` only called
`SetGameTeamsFromWeb` + set `_matchupLocked = true` — starting the actual screen-capture/OCR loop
(`_hook.Start()`/`_watcher.Start()`) was a **separate, manual** "Start Watching" button press that
had to happen afterward. If that second click never happened (easy to miss — GAMETIME *feels*
like "we're live now"), the watcher never ran at all, meaning literally nothing was ever being
read off the screen. Every "why didn't down N fire" report this session traced back to this,
not to down-specific logic (2nd and 3rd down run through identical code — no reason one would work
and not the other except watching not actually being on when it mattered).

**Fixed:** `WebMainForm.cs` — `ConfirmGametimeFromWeb` now calls a new shared
`StartWatchingIfMatchupSet()` helper (factored out of `ToggleWatchingFromWeb`) right after locking
the matchup. GAMETIME now locks the matchup **and** starts watching in the same press. Verified by
rebuild (0 errors) — **the auto-start itself was live-tested this session and confirmed to flip
the status pill to "Waiting for window…" immediately after GAMETIME**, but whether down-triggers
are now 100% reliable in a full live game wasn't confirmed before the session ended (see section
6, "Waiting for window" issue, which surfaced during this same testing pass).

**Follow-on UI change:** the old clickable "Start Watching" pill was removed entirely (it's
automatic now) and replaced with a non-interactive status badge (`#watch-status`) plus a separate,
explicit **"Stop Watching"** button (`#btn-stop-watching`, hidden until watching is active). This
also closes a real accidental-footgun: with the old single-button toggle, a habitual second click
after GAMETIME (thinking "let me start watching too") would have **silently stopped** watching
instead, since it was a raw toggle. That's no longer possible — there's no "start" action left to
misclick.

---

## 2. Real bug: What's New popup was silently blocking the entire app

`style.css` had `#whats-new-overlay { display: flex; ... }` with **no**
`#whats-new-overlay[hidden] { display: none; }` override. An ID selector beats a bare `[hidden]`
attribute selector on CSS specificity, so `overlay.hidden = true` (called by both the "Got it!"
and "×" buttons) did nothing visually — the full-screen overlay stayed rendered and **absorbed
every click**, including the custom window-control buttons underneath it (this app runs
`FormBorderStyle.None`, so those buttons are the *only* way to minimize/close — there's no OS
titlebar fallback). This is the exact bug class Cline's new `ui-bot.js` (section 4) is built to
catch, and it's a documented recurring pattern in this codebase — `#bandroom-overlay`,
`#matchup-overlay`, `#save-profile-overlay`, etc. all needed the same explicit fix historically.

**Fixed:** added the missing `[hidden]` override. Also proactively added the same override for
the new `#test-hook-panel` (section 3) so it doesn't hit the same bug later.

**Also found and fixed in the same investigation:** `AudioPlayer.cs` has a 20-second
per-audio-file cooldown (`FireCooldown`, global `_lastFireByPath` dict) meant to stop live OCR
flicker from double-firing the same cue. It's shared by **every** caller of `AudioPlayer.Play` —
including the Preview button and the new test hook — so clicking Preview then testing the same
clip within 20 seconds silently no-ops with zero UI feedback. Added
`AudioPlayer.ClearCooldown(path)` and wired the test hook to bypass it
(`FireEventForSide(..., bypassCooldown: true)`).

---

## 3. New: Event Test Hook (fires events without a live game)

Ctrl+Shift+T opens a small panel (`#test-hook-panel`) — pick a Side (home/away) and any EventKey
from a dropdown (populated from `ConfigStore.AllEngineEventKeys` via `WebBridge.GetAllEventKeys`),
hit **Fire Event**. Calls `WebMainForm.FireTestEventFromWeb` → `FireEventForSide(side, eventKey,
bypassCooldown: true)` — the exact same code path real engine events use, so it validates the
`LegacyDownEventAlias` fallback and side-routing volume too, not a separate mocked path.

- `FireEventForSide` was changed from `void` to `string`, returning what actually happened
  (`"fired:<filename>"` / `"unassigned"` / `"file-missing"` / `"no-profile"`) so the test hook can
  show a real toast instead of "did it work? who knows." Real engine/legacy callers ignore the
  return value, unaffected.
- **Stop** button (calls `AudioPlayer.StopAll()` via the existing `bridge.StopPreview`).
- Dropdown shows friendly names (section 5), not raw EventKeys.
- **Verified working** via direct testing this session, including the cooldown-bypass fix and the
  fire-result toast.

---

## 4. Cline's task this session: `ui-bot.js` (DOM bug scanner)

Found as an untracked file, initially **not wired in** (`ui-bot.js` existed but no `<script>` tag
referenced it — dead code). By end of session Cline had wired it into `index.html`. It's a 12-check
automated scanner (missing `[hidden]` overrides — literally the same bug class as section 2, canvas
size mismatches, z-index collisions, accessibility gaps, duplicate CSS properties, etc.), exposed
as `window.__runUIBot()`, auto-runs 1.5s after DOM ready.

**Verified live** (loaded the real `index.html` in an isolated browser and ran it): reported 6
critical + 11 warnings + 7 passes. Spot-checked the top findings by hand:
- **3 "missing `[hidden]` override" criticals** (`#left-panel`, `#adjust-panel`,
  `#discord-chat-panel`) — checked whether JS ever actually sets `.hidden` on these three elements
  at runtime (grepped `app.js`). **It doesn't, for any of them.** These are **false positives** —
  real latent risk if someone adds `.hidden` toggling to them later, but not an active bug today.
  This is a real limitation of the bot's check: it flags the CSS *pattern*, not whether it's ever
  actually exercised.
- **3 "canvas size mismatch" criticals** (`logo-crop-canvas`, `bg-crop-canvas`,
  `preview-waveform`) — **not yet verified**. Was mid-way through checking whether these are real
  bugs or intentional thumbnail downscaling (a big backing canvas rendered small via CSS is a
  normal, deliberate pattern for crop tools) when the session moved on. **Next session: finish
  this check** — read the CSS rules at `style.css:379` (`#preview-waveform`), `:1656`
  (`#logo-crop-canvas`), `:1709` (`#bg-crop-canvas`) and confirm whether the JS drawing code
  assumes the HTML attribute size or the CSS-rendered size for its coordinate math.
- Didn't get to verifying the 11 warnings (z-index collisions, accessibility gaps, etc.) at all.

**Bottom line for the owner:** Cline's tool is real and useful, and it already caught the same bug
class we fixed by hand in section 2 — but its "critical" label isn't proof of an active bug by
itself; every finding needs a runtime-usage check before acting on it, same as always.

---

## 5. UI simplification pass (several rounds of explicit owner feedback)

### 5a. Category reorg: Offense / Defense / Situations
Replaced the old 6-category scheme (Downs/Scoring/Turnovers/Special Teams/Penalties/Hype) —
explicit owner ask: "sorted thru that way not hype scoring etc." `CategoryMap.cs` now buckets by
EventKey prefix (`Offense:`/`Defense:` → matching tab; legacy `down:` triggers → Offense;
`flag:`/`Other:` → Situations). `WebMainForm.CategoryOrder` and `app.js`'s `categoryColors`/
fallback list updated to match. **Verified live** — screenshots this session show the new tabs
rendering correctly with real counts.

### 5b. Friendly display names
Raw EventKeys ("Offense: Earned First Down", "Other: Kickoff on Kick (Receiving)") are internal
jargon nobody should have to read to assign a song. Added `EVENT_FRIENDLY_NAMES` map in `app.js`
+ `friendlyEventName()`, applied to both the test hook dropdown and the profile screen's card
titles (`ev.eventName` in `openSituations()`). EventKey itself is untouched (zero risk to saved
profiles) — this is a display-only lookup.

Per later feedback ("Got 1st Down" convention, then "some of these are the same... make it
simpler"), the "Earned" phrasing was changed to "Got X Down" and then the whole duplicate-card
problem this uncovered (see 5c) got fixed instead of just renamed.

**⚠️ Incomplete:** `EVENT_FRIENDLY_NAMES["Offense: Earned First Down (Midfield)"] =
"Got 1st Down - Past Midfield"` is now a **stale, unused map entry** — that EventKey was removed
from `AllEngineEventKeys` in 5c (see below), so this label can never actually render anywhere.
Harmless (dead map entry, not a bug) but should be deleted for cleanliness. Also was mid-rename of
`"Got 1st Down - Big Gain"` → `"1st Down - Big Gain"` (drop the "Got" prefix inconsistency between
this card and the plain "1st Down" legacy card that sits next to it) when the session ended —
**this edit was NOT made**, still needs doing.

### 5c. Removed duplicate/dead event cards
Owner, looking at the actual rendered Offense tab, correctly identified real confusion: the
legacy `"1st Down"`/`"2nd Down"`/`"3rd Down"` cards (real songs already assigned, working via
`LegacyDownEventAlias`) were showing up **alongside** duplicate empty placeholder cards for the
canonical engine keys (`"Offense: Earned First Down"` etc, rendered as "Got 1st Down") — same
concept, two cards, one always empty.

`ConfigStore.cs` — new `RetiredEventKeys` set (`"Offense: Earned First Down"`, `"Offense: Second
Down"`, `"Offense: Third Down"`, `"Offense: Drive Starter"`, `"Defense: Drive Starter"`), removed
from `AllEngineEventKeys` and actively **pruned from already-persisted profiles** in
`EnsureAllEvents` — but only when the entry's `AudioFile` is empty, so no real user assignment is
ever silently deleted. This is safe at the firing level: removing a UI card doesn't touch runtime
firing at all, since `FirstDownHelper`/`OffenseDownHelper`/`DriveStarterHelper` still emit these
exact EventKeys — `LegacyDownEventAlias` still catches them and routes to the legacy card's file,
completely unchanged.

Second round (owner: "still confused... 1st down is fine and got 1st on 1st") — same treatment
for the **Midfield variants**, but for a different reason: these can't just be renamed, they
**structurally can never fire**. `YardLine` is hardcoded to `0` everywhere (never OCR'd, tracked
since Session 2), so any `<= 50 = midfield` check is either always-true or always-false garbage.
New `BlockedEventKeys` set (`"Offense: Earned First Down (Midfield)"`, `"Offense: Second Down
(Midfield)"`, `"Defense: Second Down (Midfield)"`), same prune treatment. Re-add to
`AllEngineEventKeys` once real YardLine data exists.

**⚠️ Not yet rebuilt/relaunched** — see the warning banner at the top of this doc.

### 5d. AssignTrackForm redesign (preview-on-top)
Owner: "hard to understand how to select a song." `AssignTrackForm.cs` reordered — a preview
panel (`_lblPreviewing`, `_btnPlay`/`_btnStop`) now sits above the song list instead of the list
being the only thing on screen; selecting a list item enables Play/Stop and shows the filename;
`AudioPlayer.Play(item.Path)` previews directly (no `FireEvent`/PA-layer/interruptPrevious
involved, so it can't accidentally trigger a PA clip or fight the cooldown). Cancel now also stops
any playing preview. Build-verified (0 warnings after fixing the initial unused-field warnings
from a half-finished first pass) — **NOT live-tested**, someone should open Assign/Edit on a real
event and confirm the new layout looks/behaves as intended.

**Not started:** the bigger ask this fed into — embedding the whole assign/trim flow directly
into the profile screen (CapCut-style: list on the left, big preview/clip-editor canvas on the
right, no separate popup dialog at all) plus circular trim-knob sliders and macOS-style theming.
Owner gave 20 feature suggestions for this (see prior session transcript) — scoped as a real
multi-pass redesign, not started beyond the preview-panel piece above.

---

## 6. Live bugs found from real gameplay screenshots (this session)

### 6a. Windows "Activate Windows" watermark overlapping the OCR crop region
A live screenshot showed the "Activate Windows / Go to Settings to activate Windows" watermark
sitting directly over the down/distance score-bug text, in the exact region OCR crops
(`FxY=0.83, FxH=0.14`, full width). Plausible (not confirmed) contributor to intermittent
misreads. Owner removed the watermark via the legitimate `PaintDesktopVersion` registry value
(`HKCU\Control Panel\Desktop`) rather than actually activating Windows. **Unconfirmed whether this
was actually a real contributing factor** — owner explicitly asked "are you sure that was the
issue?" and the honest answer given was no, it's plausible but unverified; needs a live retest to
know for sure.

### 6b. "Waiting for window…" stuck even after pressing GAMETIME — RESOLVED
Reported after the section 1 fix was already live — i.e., watching *was* starting automatically,
but the OCR watcher's window-finder (`GameWatcher.FindGameWindow`, `GameWatcher.cs:786`) wasn't
locating the game window. That function enumerates all visible windows and checks whether the
title bar text contains a hardcoded substring.

**First hypothesis (wrong, corrected later same session):** initially assumed the owner's game was
College Football 25/26 (per this doc's other references) and the literal `"College Football 27"`
substring just didn't match a 25/26 window title, so the match was loosened to bare `"College
Football"`. This was reverted — the owner is actually on **College Football 27**, confirmed live
via `Get-Process | Where MainWindowTitle -like "*College Football*"`, which returned the real
window title as `"EA SPORTS™ College Football 27"`. That string already contained the original
literal substring, so the original match was never actually broken this way.

**Actual root cause, found via the new `ocr_debug.log` (section 7):** the watcher loop *was*
running and *was* finding a window match, but `EnumWindows` + title-substring matching is
ambiguous — **other running apps can have "College Football 27" in their own title too.** Live
`Get-Process` on the owner's machine turned up a second match: `"Frosty Mod Manager: MMC Edition -
1.1.0.2 (College Football 27™)"` (process `MMCModManager`), a mod-manager tool with the game name
in parentheses. `FindGameWindow()` returns whichever matching window `EnumWindows` happens to
enumerate first — not necessarily the real game (`CollegeFB27` process, confirmed via
`Get-Process`) — so it can silently lock onto the mod manager's `hWnd` instead. The OCR log proved
this happened live: every "down"/"flag"/"situation" region read back **VS Code / Claude Code chat
text**, not game HUD text, because the crop rect for the (wrong) matched window's screen
coordinates happened to be covered by another app's window at the time.

**Real fix:** rewrote `FindGameWindow()` to stop matching by window-title substring entirely.
It now uses `Process.GetProcessesByName("CollegeFB27")` (the actual game's process name, confirmed
live) and reads `MainWindowHandle` directly — immune to any other app's window title containing
the game's name. Rebuilt (0 errors), stale `ocr_debug.log` cleared, relaunched.
**Owner should retest**: confirm the pill flips to "Watching" after GAMETIME with only the real
game running, and check `ocr_debug.log` shows real HUD text (down/distance/score), not chat/editor
text, to be sure this is fully resolved.

### 6c. Real bug fixed: possession defaulting to "home" before ever detected
`GameWatcher.cs:694` — `bool possessionIsHomeNow = _lastPossession != "away";`. Since
`_lastPossession` starts `null` and stays `null` until OCR successfully reads it once,
`null != "away"` evaluates `true` — silently assumed home has the ball before possession was ever
actually detected, contradicting this file's own stated "ambiguous frames deliberately do nothing"
philosophy. Affected penalty-side routing (`isPenaltyOnOffense`/`isPenaltyOnDefense`) during the
window right after Set Matchup, before the first real possession read.

**Fixed:** changed to nullable (`bool?`), both penalty flags now also require
`possessionIsHomeNow.HasValue`, so penalty routing correctly waits for a real read instead of
guessing. Rebuilt and confirmed 0 errors.

**⚠️ NOT fixed (flagged, bigger scope):** the exact same bug pattern exists at
`GameWatcher.cs:712` — `PossessionAway = _lastPossession == "away"` — which feeds
`GameState.UserHasPossession` (`GameState.cs:15`), gating **every** Offense-side evaluator
(`OffenseDownHelper`, `DriveStarterHelper`, `KickoffHelper`, `NoPuntReturnHelper`,
`TimeoutHelper`). Because `UserIsHome` is hardcoded `true`, this particular default actually
means "assume the user's team has the ball" before detection — which, unlike the penalty bug,
mostly just causes over-firing on offense events rather than blocking them, but it's the same
underlying design flaw and should get the same nullable treatment for consistency. Deferred
because it requires changing `PlaySnapshot.PossessionAway` from `bool` to `bool?` and touching
every helper that reads it — a much wider-reaching change than the contained penalty fix.

### 6d. Confirmed correct: penalty ("FLAG") detection
A live screenshot during an actual penalty showed the game's on-screen text is literally
**"FLAG"** in a yellow box — exactly matching the already-calibrated `flag` region regex
(`\b(FLAG|PENALTY)\b`) from Session 3. Detection side confirmed working. The "penalty doesn't
trigger" report traced to `"Penalty: Offense"`/`"Penalty: Defense"` having **no song assigned at
all** in the Tennessee profile (verified by reading the profile JSON directly) — not a firing bug,
an empty slot.

---

## 7. New: live OCR diagnostic logging

`WebMainForm.OnLog` was a **no-op** (`void OnLog(string message) { }`) — every OCR region read
that `GameWatcher` already reports via `Log?.Invoke(...)` (down/situation/flag/possession/loss,
see call sites at `GameWatcher.cs:312,333,387,511,515,570,575,598,602`) went straight to nowhere.
This is why every live-bug investigation this session required screenshots and guesswork instead
of just reading a log.

**Fixed:** `OnLog` now appends timestamped lines to `ocr_debug.log` next to the exe (same folder
as `Bandroom.exe`), capped to the last ~2000 lines so a long game session can't grow it unbounded.
Rebuilt and launched — **not yet exercised with a real game session** to confirm the log actually
captures something useful; next live test should produce a log worth reading.

---

## 8. Open decisions carried over from Session 3, still unresolved

1. **"4th Down" legacy alias** — still undecided (option a/b/c from Session 3 unchanged).
2. **Kickoff variant fallback** — still undecided.
3. **Away-team volume: 25% on non-Big-Game, 100% on Big-Game** — discussed extensively this
   session (owner's exact spec: "away teams play 25% volume on non big games 100/100 for big
   games... must have the big game option to check") but **not implemented**. `BigGame` is still
   hardcoded `false` in `GameWatcher.cs:725`, never OCR'd, no UI checkbox exists. Also identified
   the current volume logic (`BigEventHelper`/`DefenseHelper`/etc, `Volume = BigGame ? 100 : N`)
   only scales *which* volume number gets used, it doesn't gate *whether* the away side fires at
   all — doesn't match the owner's "away band in big games play if they stop on 4th" rule (implying
   away should NOT fire on non-big-game 4th-down stops at all, not just fire quieter). **Needs
   actual implementation next session.**
4. **Automatic Big Game detection via the "EA SPORTS CFB" splash screen** — owner shared a
   screenshot of a pregame screen with readable "EA SPORTS CFB" text + split team helmets.
   Proposed as a real alternative to a manual checkbox: OCR-calibrate that text, auto-set
   `BigGame = true` if it appears. **Blocked on one open question for the owner:** does that
   screen show before every game, or only marquee/rivalry ones? If universal, this idea doesn't
   work and a manual checkbox is needed instead.
5. **"Run Out" tunnel cutscene** — determined this session to not be OCR-feasible with the
   current text-based approach; no scorebug/readable text exists during that cutscene. Would need
   image-matching, a different technique than everything else in this app. Not built.
6. **Generic-profile override toggle** ("use my generic pack instead of home/away, but only
   choosable after matchup lock") — discussed, not built.
7. **CapCut-style profile screen redesign** — 20 feature ideas given, only the AssignTrackForm
   preview-panel piece (5d) actually built.
8. **Cline's ui-bot.js findings** — 3 canvas-size-mismatch criticals and 11 warnings never
   verified (section 4).

---

## Conventions (unchanged from Session 3)
- Everything lives in `C:\Bandroom`, a real git repo (still not committed — all changes described
  above are uncommitted working-tree state as of this doc).
- `.claude/skills/deep-audit/SKILL.md` is the audit checklist — keep using it before trusting any
  "done"/"fixed" self-report, including this document's own claims. Every "Verified" label above
  means "rebuilt + traced the code path"; every "NOT verified" / "unconfirmed" label is an honest
  gap, not a formality — check it before relying on it.
- **Rebuild and relaunch before doing anything else** — see the warning at the top of this file.
