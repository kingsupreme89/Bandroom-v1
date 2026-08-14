# Bandroom Handoff — August 14, 2026 — Session 72

Same idea as always: what happened, explained plain.

## Real Fix: RAM Reader Was Running the Wrong Binary Entirely

Session 71 swapped in a "v1.4.1" RAM reader exe (`CollegeFB27RamReader.exe`, SHA256 `EF5409AE...`,
283,136 bytes) from Coffee's GitHub releases and it never actually locked onto a live scoreboard —
every session tonight it sat on `"scoreboard missing... waiting to retry"`, cycling through phantom
team-name guesses (`Utah State/Notre Dame` -> `?/?` -> `Wisconsin/North Carolina`) that changed
every poll, never confirming.

**Root cause found by comparing against a working reference.** The owner has TWO separate real
copies of Coffee's overlay app installed locally that were never checked before tonight:
- `D:\Scorbug Overlay App 2 Beta\` (portable build, actually run and used before — its
  `UserData\data-export\` folder had real historical exports, including one from earlier tonight
  showing a **genuine live RAM lock**: Tennessee/Florida, real down/distance "1st & Goal", ranks,
  timeouts, quarter, clock, all tagged `"source":"ram"`).
- `D:\Games\CFB Tools\CFB27-Scoreboard-Overlay-v1.0.26-OPEN-BETA\` (newer, found later this
  session — see below).

Unpacked the first app's `resources\app.asar.unpacked\ram-reader\CollegeFB27RamReader.exe` (via
`npx @electron/asar`) and hashed it: **`bd6fb49f...`, 180,224 bytes** -- a THIRD, completely
different binary from both the "old" one (`c71c32c6`, 280,064 bytes) and the "v1.4.1" one from
Session 71 (`ef5409ae`, 283,136 bytes). Neither of the two exes tested across Sessions 71-72 was
the one that actually worked -- the real working binary was sitting in a completely different app
folder the whole time, never checked.

**Fixed:** copied `bd6fb49f...` into both `Assets\ScoreboardReader\CollegeFB27RamReader.exe`
(source) and the running build's copy
(`bin\Debug\net10.0-windows10.0.19041.0\Assets\ScoreboardReader\...`), killed the stray old
process first. **Confirmed working live, repeatedly, across two different games tonight**
(Wisconsin/North Carolina, then Georgia/Georgia Tech) -- `ram-reader-status.json` showed sustained
`"RAM export LIVE: ..."` messages with real, advancing quarter/clock/down/distance/possession
across multiple ticks, not a one-off fluke.

**Still not resolving most sessions:** `awayTimeouts`/`homeTimeouts` in `live-game-data.json` come
back `"available":false, successfulReads:0"` -- the locator's timeout memory addresses just don't
get found every game launch (they DID resolve to 0/0 in the earlier Tennessee/Florida session, so
it's not structurally broken, just inconsistent per-session address scanning). No code fix for
this -- it's inherent to how the exe's own memory locator works, not something Bandroom's side can
patch.

## Real Bug Found and Fixed: Play Clock Was Reading the Wrong Preset's Coordinates on Every Non-CFB27-Console Skin

Owner tested the ESPN broadcast HUD live tonight and reported score/clock/possession worked but
play clock and timeouts glitched, and the crop looked wrong. Investigation found **two separate,
real bugs**, not glitches:

1. **No ESPN/NBC/Fox preset existed at all.** `ScorebugPreset.AllPresets` only had three entries
   (`KamsCbsScorebugV3`, `CollegeFootball27`, `CollegeFootball26Console`) -- whatever preset was
   active was calibrated against a totally different HUD layout than what was on screen.
2. **`playclock`'s crop was hardcoded** (`GameWatcher.cs`, `FxX=0.70/FxY=0.83/FxW=0.06/FxH=0.14`)
   and never wired into the per-preset crop-reassignment loop that `awayscore`/`homescore`/`clock`/
   `down`/`situation`/`quarter`/`flag` all go through. So play clock was ALWAYS reading the CFB27
   console HUD's play-clock position, regardless of which preset was actually active -- explains
   why it "wasn't working" on ESPN specifically, and would explain the same on NBC/Fox too.

**Fixed play clock's wiring**: added `PlayClockFxX/Y/W/H` to `ScorebugPreset` (defaults match the
old hardcoded constants exactly, so `CollegeFootball27` and every other existing preset are
unaffected), and added a `playclock` branch to `GameWatcher`'s crop-assignment loop so it actually
gets repositioned per active preset like everything else.

**Built a real ESPN preset from someone else's actual production calibration data, not a guess.**
Found a NEWER copy of Coffee's app at `D:\Games\CFB Tools\CFB27-Scoreboard-Overlay-v1.0.26-OPEN-BETA\`
with a real `UserData\settings.json` containing a full `capture.readerCalibration` block (schema
`cfb27-reader-calibration/2`) -- genuine measured ROIs for away/home name/record/timeouts/score,
quarter, clock, play clock, and down/distance. Owner confirmed this corresponds to the **compact
"ESPN 2013" widget** positioned in the screen's bottom-right corner (matches the bundled
`themes/espn-2013/` theme, canvas 371x433 -- portrait-shaped, not the wide horizontal bar every
other preset in this file assumes).

Traced the app's own `src/reader-calibration-file.js` to get the coordinate math exactly right
(each ROI is a fraction WITHIN a `readRegion` sub-box, not the full frame directly -- composed them
by hand: `absX = readRegion.x + roi.x * readRegion.width`, etc) rather than guessing at the
conversion.

**Found and fixed a real architecture gap this surfaced:** every preset until now assumed
away/home scores sit side-by-side at the same Y (one shared `BandFxY`/`BandFxH` band). The ESPN
2013 widget stacks them vertically instead (away row above home row, same X, different Y) -- a
layout shape `ScorebugPreset`/`GameWatcher` had no way to express. Added optional
`AwayScoreFxY/H`, `HomeScoreFxY/H`, `ClockFxY/H` overrides (all null by default, meaning "fall back
to `BandFxY`/`BandFxH` exactly like before") so this works without touching any existing preset's
behavior.

**Result:** new `Espn2013` preset (`ScorebugPreset.cs`) has genuinely measured crops for score,
clock, play clock, AND home timeouts (a real measurement, not the mirrored-guess placeholder every
other preset's `HomeTimeoutFx*` has). Possession and banner/penalty regions are NOT calibrated for
this preset -- his data didn't cover them; left at defaults, flagged in the doc comment, need a
live screenshot showing a possession flip and a penalty/banner overlay on this skin before trusting
those specific signals. Build succeeded, 0 errors -- **not yet re-tested live after this fix, next
session should confirm ESPN preset's play clock/timeouts actually track correctly now.**

## New: Full Team Logo Library Imported From Coffee's App

Found a bundled, pre-cropped library of 130 FBS team logos inside his portable app's
`resources/app.asar` (`assets/team-logo-variants/`, extracted via `@electron/asar` since the
CLI tool's older `asar` package choked on a broken/duplicate directory-node entry for that
specific folder -- `@electron/asar` handled it fine). Confirmed each PNG is genuinely
alpha-transparent (checked corner-pixel alpha = 0 via `sharp`, not just visually white in a
preview). At the owner's explicit instruction ("use his logos for all of the teams, nothing I
gave"), **all 130 were copied into `UserData\TeamLogos`, overwriting everything** -- including
overwriting a smaller set of 6 team logos (Oregon State, Washington State, UCF, Cincinnati,
Houston, Army) that had been cropped from two Mountain West/American-conference reference sprite
sheets the owner sent earlier in the session, before the full 130-logo library was found. Those
sprite-sheet crops are effectively superseded/discarded now.

## New Tool: Crop Calibrator

Built `C:\Bandroom\tools\crop-calibrator.html` -- a self-contained, local HTML tool (canvas-based,
no server/build step) so the owner can calibrate future presets themselves instead of needing a
full screenshot round-tripped through chat every time. Load a full uncropped screenshot, pick a
field name from a dropdown (matches `ScorebugPreset`'s actual field names), drag a box, get
copy-pasteable `FxX/FxY/FxW/FxH` C# output. Not yet used for anything real -- built in response to
repeated "I can't calibrate from a cropped reference image" friction this session (several
reference images sent, like `espn.webp`, turned out to be pre-cropped to just the bug with no way
to recover full-frame fractional coordinates from them).

## Root-Caused: NBC Preset's Overlay Has a White Border/Fringe

Owner reported "NBC bug was cropped fine, we just need to remove the white around the box."
**NOT YET INVESTIGATED** -- flagged but not root-caused or fixed this session. Likely a
chroma-key/anti-aliasing edge issue in the overlay rendering (a white halo where the source PNG's
edge pixels blend against the green matte), not a crop-coordinate problem since the owner
explicitly said the crop itself is fine. Next session should look at the overlay compositing code
(`ScorebugOverlayForm.cs` or wherever the green-screen band gets rendered) for edge/anti-aliasing
handling.

## Dead End Worth Knowing About: `espn-2013` Theme Isn't a Data Source

Investigated `\themes\espn-2013\index.html` inside the app.asar hoping it would reveal exact pixel
layout data for the ESPN widget. It's a single 249KB minified/bundled blob with no meaningful
per-field DOM structure (`grep` for score/clock/timeout ids came back empty except a generic
bundler loader id) -- purely a rendered visual skin his app composites images onto, not a
data-driven template. Don't re-check this path for calibration data; the real calibration data
came from `settings.json`'s `readerCalibration` block instead (see above).

## Options Discussed, Not Started

- Tightening the OCR timeout debounce from 2-consecutive-reads to 3, as a safety margin now that
  ESPN has a real crop -- not done, wait and see if it's still needed after live retest.
- Possession/banner/penalty calibration for the Espn2013 preset -- explicitly not covered by the
  ported calibration data, needs real screenshots.
