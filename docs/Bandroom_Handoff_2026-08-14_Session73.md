# Bandroom Handoff — August 14, 2026 — Session 73

Same idea as always: what happened, explained plain.

## Real Bug Found and Fixed: RAM Reader Was Zeroing Out Correct Scores and Could Flip Possession Wrong

The owner asked for a general reliability pass on the OCR + RAM reading pipeline ("make everything
stronger... the ocr, the raw everything"). That turned up a real, live bug, not just hardening.

**Root cause:** `GameStateNormalizer` (`src\Bandroom.Core\GameStateNormalizer.cs`) keeps a sticky
per-field cache and defaults `AwayScore`/`HomeScore`/`YardsToGo`/`YardLine` to **0** when a field
has never actually resolved from RAM. `GameWatcher.RouteEngineTick` was applying those fields
**unconditionally** onto the live snapshot -- so the instant the RAM reader connected at all (status
"live"), even if it had only locked team names/clock and NOT score yet (exactly the intermittent
per-field locator behavior Session 72 documented for timeouts), any score/yardage/yard-line field
RAM hadn't separately resolved silently overwrote a correct OCR-read value with a fabricated 0 --
every tick, for the rest of the game. That breaks every score-delta evaluator (Touchdown,
FieldGoalPAT, Safety, FieldGoalMissed all key off `HomeScore`/`AwayScore` deltas).

A second copy of the same bug: `ReaderNumericSnapshot.PossessionAway` is a plain `bool`, defaulting
`false` ("home") when possession had never resolved -- also applied unconditionally, so RAM
connecting at all could silently override GameWatcher's own correctly-sampled OCR possession, even
on games where RAM never actually resolved the possession bit. This directly drives which team's
audio plays as offense/defense.

Timeouts already had the right pattern (`-1` = "not yet resolved," never override on that value) --
that's why only timeouts showed up as broken in the status JSON (`available:false`). The same class
of bug was silently present for score/yardage/possession too, just invisible because it doesn't
report a `false` availability flag the way timeouts do.

**Fixed:** extended the existing `-1 = never resolved` sentinel convention to
`YardsToGo`/`YardLine`/`HomeScore`/`AwayScore`, and added a `HavePossession` bool to
`ReaderNumericSnapshot` so possession can be told apart from "genuinely home" vs "never resolved."
`GameWatcher.cs` now guards all four fields with `>= 0` checks and gates the possession override on
`HavePossession`, matching the pattern already used for Down/Quarter/Timeouts. Added two regression
tests (`Normalize_ScoreNeverResolved_ReturnsSentinelNotZero`,
`Normalize_PossessionNeverResolved_HavePossessionFalse`) plus updated the existing reset test for
the new sentinel value. Applies to both readers (bundled RAM reader and Coffee's screen-JSON
reader) since both funnel through the same normalizer -- a general merge-logic fix, not
RAM-specific.

**Cross-checked against Coffee's own reader-settings UI** (owner sent a screenshot of his "Reader &
live data" tab): his "stable frames: 2" debounce already matches what Bandroom does
(`CommitValueIfConfirmed`/`ConfirmPossessionFlip` already require 2 consecutive matching reads
before committing) -- no change needed there. His "minimum OCR confidence: 20%" slider has no
equivalent here and can't get one without swapping OCR engines -- Windows' `Windows.Media.Ocr`
(what GameWatcher uses) doesn't expose per-word confidence scores at all, unlike Tesseract (which
his app likely uses). Flagged as a known platform limitation, not fixed. His "live clock correction
(seconds)" concept and manual-override fallback form (type the score in by hand when both readers
fail) don't exist in Bandroom -- the manual override is a real gap but it's a UI feature, not a
data-pipeline fix, so it's flagged for a future session rather than started here.

**Still needs live confirmation** (couldn't launch the actual game this session): watch
`live-game-data.json` during a real partial-RAM-lock window (team names resolve before score does)
to confirm score no longer freezes at 0/0.

## Real Bug Root-Caused and Fixed: NBC's White Fringe

Session 72 flagged this ("NBC bug was cropped fine, we just need to remove the white around the
box") but left it uninvestigated. This session found the actual cause and fixed it properly instead
of patching around it.

**Root cause:** `ScorebugOverlayForm.cs` was trying to get real per-pixel alpha out of Chromium's
compositor via CSS `background:transparent` tricks -- the file's own comments describe three prior
rewrites fighting this (WinForms TransparencyKey, then WebView2 DirectComposition visual hosting,
then the current CapturePreviewAsync-based approach). Even with the current approach's
premultiplied-alpha compositing, some themes' rendered edge pixels still weren't reliably
transparent, producing a white halo -- an inherent fragility of depending on each theme's own CSS
actually achieving real transparency inside Chromium's rendering pipeline.

**The real fix, found from the owner's own reference:** the owner shared a screenshot of Coffee's
app's "Green screen" tab -- it doesn't chase CSS transparency at all. It renders the theme onto a
**solid known key color** and applies a post-capture **chroma-key filter** (tolerance + edge
softness sliders, 22%/15.5% in the screenshot) to convert key-colored pixels to transparent, with a
soft falloff band at edges so anti-aliased boundary pixels blend correctly instead of leaving a
halo. Found and confirmed the actual algorithm by locating Coffee's real `chromaKey.js` module
inside both known app installs' `app.asar` (had to byte-scan for it -- the asar header's null bytes
made normal text search miss it):
```
distance = avg(|R-Rk|, |G-Gk|, |B-Bk|) / 255
alpha = clamp((distance - tolerance) / softness, 0, 1)
```
default `color:'#00ff00', tolerance:0.06, softness:0.04` (his UI clamps tolerance 0-0.3, softness
0.005-0.2 -- the screenshot's 22%/15.5% are just his own tuned values within those bounds).

**Implemented the identical math in `ScorebugOverlayForm.cs`:** the theme now renders against a
solid key color (forced via the document-created script, the navigation-completed injection, the
per-tick `ForceTransparentScript`, and `_renderWebView.DefaultBackgroundColor` so even the
first-paint frame is on-key) instead of chasing CSS transparency. Added `ApplyChromaKey(Bitmap)` --
an unsafe per-pixel `LockBits` pass implementing the formula above -- run on the captured bitmap
right after `CapturePreviewAsync`, before the existing premultiply/scale step.

**Key color chosen: magenta (`#FF00FF`), not green.** Green is Coffee's default and what NBC 2024's
own HTML declares near the top (seemingly authored expecting green-screen capture) -- but checking
its actual SVG content found a real peacock logo with a green feather (`#0DB14B`) whose distance
from pure green is ~0.217, right at the edge of Coffee's own 0.22 tolerance. Using green as the key
would have partially kept out NBC's own logo. Magenta doesn't appear in any of the 6 imported
themes' palettes. Tolerance/softness hardcoded to 0.22/0.155 (matching the screenshot), no new
tuning UI added -- this app doesn't have a per-overlay settings surface and the owner didn't ask for
one.

**Still needs live confirmation:** launch the overlay against NBC 2024 live and visually confirm
the fringe is actually gone -- this was verified at the algorithm/build level only, no agent could
launch the actual rendering pipeline on-screen this session.

## Dead End, Confirmed Thoroughly: No NFL Theme, No Second Real ESPN Theme Exist On This Machine

Owner asked to "crop the nfl, the other espn." Two separate passes searched for source material:

- **All three Electron app installs found on disk** (not just the two known from prior sessions --
  found a third, previously unchecked copy: `D:\Scorbug Overlay App 2 Beta 1\`), their bundled
  `app.asar` contents, AND their `UserData\theme-library\themes\` folders (where the 5 themes
  already in Bandroom's library actually trace back to -- the asars themselves only ever bundle one
  theme each, `espn-2013`).
- A full recursive filename search for `*nfl*` (html/zip) across all of C:\ and D:\ -- zero hits.
- A web search for Coffee's Corner / CFB27-Scoreboard-Overlay GitHub for an NFL-branded release --
  none found.
- The only "second ESPN" file that turned up (`ESPN2020-CFB27.html`, 46KB) is just a smaller/earlier
  build of the exact same ESPN 2020 skin already imported -- its own internal JS tags itself
  `version: 'espn-2020'`, not a distinct broadcast look.

**Conclusion: neither exists on this machine or turned up online.** Not fabricated as placeholders,
per this project's own established convention (see every "unverified"/"not pixel-measured" caveat
throughout `ScorebugPreset.cs`). If the owner has a specific source (zip, link, Discord attachment)
in mind, it needs to be supplied directly -- further blind searching won't find something that
isn't there.

## New: ESPN 2013 Theme Imported (Visual Only, No Live Data)

While searching for "the other ESPN," found and imported **"Football Scorebug ESPN 2013"**
(371×433 canvas, portrait) from Coffee's own `UserData\theme-library` into
`Assets\ScoreboardReader\theme-library\themes\bdcf89e766...\`, added to `library.json` (6 themes
now). Two independent checks confirmed it has **zero** `data-cfb27-bind`/`updateScorebug` hooks --
same static/minified render blob Session 72 already ruled out as "not a data source" for the OCR
side. It will display but never update with real scores, same known limitation as FOX 2021.

## Git: Catching Up A Large Uncommitted Backlog

The repo hadn't been committed since `fe20695` ("Fix penalty detection crop, add
black-screen-timed pregame runout trigger") despite roughly a week of work already sitting in the
working tree -- the full scoreboard-reader subsystem absorption (RAM reader, Coffee's JSON reader,
theme-library, `ScorebugOverlayForm.cs`), team background images, and various helper/UI additions
from sessions 65-72, none of it previously committed. Confirmed with the owner before proceeding
(large blast radius, no clean way to separate "just today's fix" from that backlog since there was
no earlier commit boundary to diff against) -- owner chose to commit everything.

**Committed as `e64e5e2`** (141 files, +22121/-390) and **pushed to `origin/master`**. Commit
message calls out the two real fixes (RAM/OCR merge bug, chroma-key compositing) explicitly, rest
described as absorbing prior uncommitted session work. No secrets/credentials found in the diff
(reviewed file list before staging).

## Build & Test Status

- `dotnet build BandAudioHook.csproj -c Release` -- **0 errors, 0 warnings.**
- `dotnet test src/Bandroom.Core.Tests -c Release` -- **104/104 passed** (includes the 2 new
  regression tests for the RAM/OCR merge fix).
- No live game was launched this session by any agent -- everything above marked "still needs live
  confirmation" is exactly that: code-level verified, not screen-verified.

## Options Discussed, Not Started

- OCR confidence-gating equivalent to Coffee's 20% slider -- blocked on Windows OCR API not
  exposing confidence scores; would need a different OCR library, a bigger decision than this pass.
- Manual-override fallback UI (type the score in by hand when both readers fail, like Coffee's
  "Manual/Fallback" panel) -- real gap, flagged for later, not a data-pipeline fix so out of scope
  for this session's hardening pass.
- Live clock correction offset (Coffee's app has a -1s default) -- no equivalent concept in
  GameWatcher; not adding a speculative offset without a measured lag to justify a specific value.
- NFL and "other ESPN" theme cropping -- blocked entirely on the owner supplying a real source file;
  see dead-end section above.
