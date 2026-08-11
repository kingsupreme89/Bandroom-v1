# Bandroom — Session 23 Handoff

## Where this picks up from
The game updated. CFB 27's default scorebug (same HUD on PC and console per the owner, EA's own
skin, not the CBS broadcast overlay `KamsCbsScorebugV3` was calibrated against) changed layout
enough that OCR detection needed re-calibrating from scratch. That work happened first, screenshot
by screenshot. Partway through, the owner — a real band member, describing this from lived
experience running sound for an actual band — pivoted into a much bigger ask: rewrite how the
engine decides which team's cues fire and at what volume, based on down/distance/possession flow.
Both got done this session. **Not independently verified live** — everything below was built
against static screenshots and code-reading, not a running game. See "Real next step."

---

## Part 1: CFB 27 scorebug recalibration

### New preset: `ScorebugPreset.CollegeFootball27`
Overwrote the old `ConsoleScorebugV1`/"College Football 27 Console" preset in place (owner's
explicit call — rename to just "College Football 27", drop the console-specific framing since the
new HUD is confirmed identical on PC and console). Legacy name aliases updated so old saved
preference strings still resolve.

Calibrated from ~15 screenshots across two matchups (Georgia @ LSU, Georgia State @ Georgia
Southern) at 2560x1440, all eyeballed from images not pixel-measured — same caveat every preset in
this file already carries, treat as a strong starting point, not exact:

- **Band position** (`BandFxY`/`BandFxH`) — confirmed across many shots, this skin's down/
  situation/quarter/flag band sits lower and taller than the CBS skin's, to fit the two-row
  center pill (quarter/clock/ball-position on one line, down & distance on the next).
- **Score digits + clock** — these were HARDCODED shared constants in `GameWatcher.cs` before this
  session (one X/W for every preset, assuming the CBS skin's layout). **Promoted to real
  per-preset fields** (`AwayScoreFx*`/`HomeScoreFx*`/`ClockFx*` on `ScorebugPreset`, sourced in
  `GameWatcher.ApplyScorebugPreset`) since this skin's score/clock sit at completely different X
  positions than CBS. Old presets keep the original hardcoded values as their defaults, so nothing
  regressed for them.
- **Possession** — this skin shows a small arrow (▲/▼) next to a yard-line-ish number inside the
  center pill, NOT an underline under the team name like the CBS skin. Confirmed via 3 independent
  screenshots (LSU-has-ball → arrow on home side of the pill; Georgia-kicking → arrow on away
  side; Georgia State 1st-down-after-first-down → arrow on away side) that the SIDE reliably
  tracks whichever team has the ball, regardless of arrow direction (which seems to be cosmetic/
  unrelated — direction flipped between shots while side stayed correct). Reused the *existing*
  `AwayUnderlineFx*`/`HomeUnderlineFx*` fields and `GameWatcher.SamplePossessionByUnderline`'s
  brightness-comparison method as-is, just re-pointed at the arrow's position — no new detection
  code needed.
- **Timeouts** — small rounded pill/dash segments under each team's mascot subtitle text (e.g.
  "TIGERS"), same underlying concept as the CBS skin's tick marks. Calibrated from one direct
  crop of LSU's (home) side; the away-side value (`AwayTimeoutFxX/Y/W/H`, the only side actually
  read — `TimeoutHelper` only ever reads "away," which this app's convention treats as "the
  opponent") is **mirrored from that measurement, not independently confirmed**. Georgia's block
  has logo-then-name-then-score ordering vs. LSU's score-then-name-then-logo, so the mirror is
  shakier than a real measurement would be. **Still open** — get a Georgia-side close-up,
  ideally with 1-2 timeouts already used, to confirm/fix.
- **Situation text** — `KICKOFF` confirmed rendering (red ribbon, same slot as down/distance).
  `TIME OUT` (spelled out, not the dash count) added to the regex after a Panthers/Eagles
  screenshot showed it — confirmed same skin, just unranked teams (no rank number) + different
  team colors, not a different HUD.

### Still uncalibrated / no screenshot yet
PAT GOOD, FAIR CATCH, NO RETURN, INTERCEPTED, FUMBLE, TURNOVER situation text; the full-screen
TOUCHDOWN/FIELD GOAL/SAFETY banner; the FLAG/PENALTY ribbon + the "Against <Team>" penalty
decision overlay; the pregame READY screen. None of these have been seen in a screenshot yet this
session. Note: the structural turnover backstop added in Part 2 means turnover detection already
works today even without the INTERCEPTED/FUMBLE text calibrated — but the OCR text path should
still get filled in eventually as the more precise/faster-firing signal.

### Also confirmed, no calibration needed
- The play-calling menu's condensed mini-scorebug renders in the exact same screen position as
  the main HUD bug — one band calibration covers both contexts.
- A branded "EASPORTS / CFB" full-screen splash plays at the true opening kickoff (confirmed by
  filename, not by content — never got a definitive answer on whether it ALSO shows at other
  kickoffs, so it wasn't wired as a trigger signal). **Still open** if the owner wants to revisit.

---

## Part 2: The "gameplan" logic rewrite

The owner described a full down-by-down flow for how two bands (home's own, and an away band when
one is actually present, e.g. Bama @ LSU) should trade off hype cues during a drive, plus a
volume/gating rule for when the away side should play at all. This wasn't a request to bolt on new
triggers — several explicit corrections during the conversation made clear existing architecture
needed to change, not just extend. Session ended without a live playtest confirming any of this
fires correctly in a running game.

### BigGame — redefined, not extended
This was the biggest risk of the session and got an explicit go/no-go question before touching it,
since it's read by 7 different helpers. **Old meaning:** auto-detected from live score/quarter
("4th quarter, score within 8 points"), boosted cue volume 80→100. **New meaning (owner's
explicit choice: full replacement, not a parallel toggle):** a manual per-matchup flag for
"both bands are physically here" (a real Bama @ LSU-type game) vs. an ordinary game where the away
team only sends a small travel pep band.

- `ConfigStore.BigGameSettings.Enabled` is now literally "is this currently a Big Game" — the user
  flips it on before kickoff of a real one. `QuarterThreshold`/`ScoreMargin` are now **dead
  fields**, kept only so old saved `big_game_settings.json` files still deserialize without a
  migration step. Default changed `true`→`false` (the old default was harmless; defaulting the
  new manual flag to true would silently full-volume every away event on every ordinary game).
- `GameWatcher.cs`'s `isBigGame` computation is now just `bigGameSettings.Enabled`, nothing else.
  Added `GameWatcher.IsBigGame` (public property) so `WebMainForm` can read live state.
- UI panel (`index.html` "Big Game" section) simplified from quarter/score-margin inputs down to
  one checkbox. `WebBridge.SaveBigGameSettings` signature simplified to a single `bool`.

### Away-side volume/gating rule (`WebMainForm.OnEngineEventsDetected`)
Home is **completely unaffected** by Big Game — always plays every routed event at full
`HomeVolume`. Away's behavior now branches on `_watcher.IsBigGame`:
- **Big Game on:** away plays everything, full `AwayVolume`, same as home.
- **Big Game off:** away is blocked entirely unless `evt.IsEarnedBigEvent` is true (the "big
  moment" flag several helpers already set — touchdowns, turnovers, etc.), and even those play at
  25% of `AwayVolume`. `FireEventForSide` gained a `volumeMultiplier` parameter (default 1, so
  every other caller — home, previews, test-fire — is unaffected) to carry this.

### 2nd/3rd down short vs. long
`OffenseDownHelper` was completely rewritten. **Old behavior:** fired one distance-blind
"Offense: Nth Down" cue per down change, only while the user's own team had the ball. **New
behavior:** fires for **either team's drive** (no longer gated on `UserHasPossession` — doesn't
need to be, since the `Offense:`/`Defense:` prefix on the EventKey is what routes it to the
correct side already, the same trick `Penalty: Offense` already relied on to route to defense).

- 2nd/3rd down, ≤3 yards to go ("short") → new keys `Offense: Second Down Short` /
  `Offense: Third Down Short`.
- 2nd/3rd down, ≥4 yards to go ("long") → reuses the **pre-existing** `Defense: Second Down` /
  `Defense: Third Down` keys on purpose, so any default song pack or existing user assignment on
  those cards keeps working unchanged. `DefenseHelper.cs` had its own plain (distance-blind)
  branches for these same two keys removed to avoid double-firing — it now only owns the more
  specific `(Loss)` variants (a down that got LONGER, i.e. a stuffed/negative play), which
  `OffenseDownHelper` explicitly defers to by skipping whenever `YardsToGo` increased.
- 4th down → unchanged, still always `Defense: Fourth Down`, no split (a 4th down is inherently a
  pressure/decision moment regardless of distance, per the owner).
- New keys added to `ConfigStore.AllEngineEventKeys` so they're assignable in the UI. Legacy alias
  map (`WebMainForm.LegacyDownEventAlias`) extended so a returning user's old `down:2nd`/`down:3rd`
  legacy song assignment surfaces under the new Short cards automatically (short felt like the more
  intuitive landing spot for an old undifferentiated assignment).

### Structural turnover backstop
Owner's own rule, stated plainly: "if the possession switches on any down besides 4th, that's a
turnover." Added in `GameWatcher.RouteEngineTick` as an OR alongside the existing OCR-text check
(`situation == "turnover"`, which only catches literal INTERCEPTED/FUMBLE/TURNOVER text) — doesn't
replace it, since the text path is more precise when it works. Guards: excludes 4th-down
possession changes (punts, turnover on downs — not turnovers by the owner's own definition),
excludes the pregame/not-yet-read state, excludes any kickoff-adjacent tick (an ordinary
receiving-team handoff after a score isn't a turnover). Useful right now specifically because
INTERCEPTED/FUMBLE text isn't calibrated for the new HUD yet (see Part 1) — this gives turnover
detection a working path today regardless.

### Kickoff/PAT collision
Owner's diagnosis: the per-play `Other: Kickoff on Kick (Receiving/Kicking)` events (fired via
OCR text on literally every kickoff, not just the opening one) were colliding with PAT GOOD
detection, since both render in the scorebug's shared situation slot back to back right after a
score. Removed both from `KickoffHelper` entirely — `Other: Opening Kickoff` and
`Other: Second-Half Kickoff` still fire once each, exactly as before, but every other kickoff
during the game now has no dedicated cue at all; `Offense: PAT Made` (already firing right before
it) is considered sufficient signal that a kickoff is coming. Both retired keys added to
`ConfigStore.RetiredEventKeys` so they don't leave dead/unreachable cards in the UI.

**One inference, not confirmed:** the owner's walkthrough said "Bama gets to play 1st down" right
after describing the opening kickoff. Nothing new was built for that specific moment — read as
already covered by `Other: Opening Kickoff` itself (same moment), since neither `FirstDownHelper`
nor the rewritten `OffenseDownHelper` fire anything for the literal first snap of a kickoff-started
drive (both explicitly exclude it, pre-existing behavior, untouched this session). Flag if a
distinct second cue was actually wanted there.

---

## Verification status
Build is clean (`dotnet build BandAudioHook.csproj` — 0 warnings, 0 errors, confirmed multiple
times through the session, most recently after all Part 2 changes). **Nothing in Part 2 has been
tested against a live/running game** — it's all been reasoned through against the existing
codebase and the owner's description, not observed firing correctly end-to-end. Part 1's
calibrations are similarly unverified live, same caveat every preset in this file already carries.

---

## Working-relationship notes
- Owner sends real screenshots constantly, often several in one message, sometimes with
  descriptive filenames (e.g. "home 3rd and long," "away 1st down after first down") once asked —
  **read filenames carefully when given, they're often the actual answer to an open question**,
  don't guess at screen content when a plain-language label is sitting right there.
- Screenshots can be read directly from disk by path (confirmed working this session via
  `C:\Users\Fresh\OneDrive\Pictures\...`) — more reliable than inline paste, which failed at least
  once this session (dimension error). Prefer asking for a saved path over relying on paste for
  anything that needs to be gotten right.
- Owner corrects fast and expects the correction to stick immediately — e.g. flatly said "no im
  sorry i meant read the filenames" after a misread, and separately scoped down a request
  ("we wont do team timeout triggers yet, just simply X remaining... like we always have") to stop
  overbuilding. Don't re-litigate a scope-narrowing correction once given.
- Owner is a real band member describing lived experience, not a hypothetical spec — the BigGame
  redefinition, the short/long down logic, all of it came from "I know how the flow goes," not
  from guessing at football rules. Trust the domain description over independently re-deriving
  football logic from scratch; ask for clarification on the ENGINEERING/architecture risk (like
  the BigGame replace-vs-parallel-toggle question), not the football itself.
- When a request is this large and touches many files with real behavior-change risk (the BigGame
  redefinition affecting 7 helpers), one crisp go/no-go question before touching code was the
  right call and got a fast, clear answer — don't skip that checkpoint on similarly broad asks,
  but don't over-ask either; only the genuinely irreversible/high-blast-radius piece needed it,
  everything else in Part 2 was built directly off the owner's stated rules without further
  check-ins.

---

## File state (end of Session 23)

| File/Item | Status |
|---|---|
| `ScorebugPreset.cs` | `CollegeFootball27` preset added/calibrated (band, score/clock, possession, timeouts); score/clock promoted to real per-preset fields |
| `GameWatcher.cs` | Score/clock region sourcing now per-preset; TIME OUT added to situation regex; BigGame computation simplified to manual read; structural turnover backstop added; `IsBigGame` public property added |
| `ConfigStore.cs` | `BigGameSettings` doc/defaults redefined; 2 new EventKeys added (`Offense: Second/Third Down Short`); 2 EventKeys retired (`Other: Kickoff on Kick (Receiving/Kicking)`) |
| `WebBridge.cs` | `SaveBigGameSettings` signature simplified to one bool |
| `WebMainForm.cs` | Away-side Big Game volume/gating rule added to `OnEngineEventsDetected`; `FireEventForSide` gained `volumeMultiplier` param; `LegacyDownEventAlias` extended for the new Short keys |
| `wwwroot/index.html` | Big Game panel simplified to a single manual toggle |
| `wwwroot/app.js` | Big Game panel JS simplified to match; friendly names added for the 2 new EventKeys, updated for the redefined `Defense: Second/Third Down` (now "2nd/3rd & Long") |
| `src/Bandroom.Core/Helpers/OffenseDownHelper.cs` | Fully rewritten — symmetric both-sides, short/long split |
| `src/Bandroom.Core/Helpers/DefenseHelper.cs` | Plain distance-blind branches removed, `(Loss)` branches kept |
| `src/Bandroom.Core/Helpers/KickoffHelper.cs` | Per-kickoff Receiving/Kicking events removed, Opening/Second-Half kept |
| Build | Clean, 0 warnings/0 errors, last verified end of session |

---

## The real next step
**Live-test the whole thing in a running game** — nothing in either part of this session has been
confirmed against actual gameplay, only screenshots and code-reading. Priority order once a game
is available:
1. Confirm the recalibrated `CollegeFootball27` preset actually reads down/quarter/clock/score/
   possession/timeouts correctly tick-by-tick (watch the Event Log).
2. Confirm the short/long down split fires the right EventKey at the right moment, both directions
   (home driving, away driving).
3. Confirm the structural turnover backstop fires (and doesn't false-positive on a normal punt or
   kickoff-driven possession change).
4. Confirm the Big Game toggle actually gates/scales away-side volume as designed — flip it on/off
   mid-test if possible.
5. Then keep filling the still-open situation-text gaps (PAT GOOD, FAIR CATCH/NO RETURN,
   INTERCEPTED/FUMBLE/TURNOVER text, TD/FG/SAFETY banner, FLAG/PENALTY + overlay, pregame READY)
   and the Georgia-side timeout crop, same screenshot-driven process as Part 1.
