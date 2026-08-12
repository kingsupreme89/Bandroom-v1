# Bandroom Handoff — Session 54 (2026-08-12)

Continuation of Session 53, still prepping for the v1.1 push. Four matchup-screen UI fixes, a
designer-font swap for the matchup team name, an app-wide icon polish pass, and a new CFB27-only
"field-position volume" system (arrow-based home/away balance). Build clean (0 warnings/errors),
59/59 Core tests passing. Nothing committed yet — all changes are live in the working tree.

## 1. Game Settings popover — text overflow + reposition

Owner screenshots showed the "Game Settings" popover (`#matchup-controls-island`, opened from the
pill next to the scorebug switcher) with toggle labels bleeding past the popover's rounded border
("...the scorebug tt" cut off at the edge), and separately overlapping the away column's search box
and coverflow.

- **Root cause (overflow)**: `.pill-toggle` (`wwwroot/style.css`) had `white-space: nowrap` with no
  wrap fallback -- long labels like "My team is on the LEFT (visiting side) of the scorebug this
  game" rendered as one line wider than the island's 340px, spilling past the border. Fixed: label
  span now wraps (`white-space: normal`, `flex: 1 1 auto; min-width: 0` on the span, checkbox
  pinned `flex: 0 0 auto`).
- **Root cause (position)**: `.matchup-controls-island` was `position: absolute; top: 62px; right:
  clamp(14px, 2vw, 24px)` -- right-anchored, which put it directly on top of the away column's
  `#matchup-away-search` box and coverflow. Owner follow-up mid-session ("center it, same size, not
  overlapping") -- changed to `top: 50%; left: 50%; transform: translate(-50%, -50%)`, same
  width/padding as before, just centered over the dialog instead of pinned to a corner.

## 2. Overlapping subtext moved into the ticker

Owner screenshot: a small crest/logo icon with text overlapping it ("Air Force (away) at Troy
(home) -- each team's own saved profile loads automatically. Hit GAMETIME while you're still on CFB
27's team-select screen.").

- Root cause: `#matchup-subtext` (`.save-profile-subtext`) was still being populated with this full
  team-specific sentence by `updateMatchupSubtext()` (`app.js`) even though Session 51's handoff
  says this content was supposed to have moved to the bottom `.matchup-ticker` marquee already --
  the paragraph element itself was never actually cleared, so it kept rendering (position:absolute,
  sitting near the coverflow logo) alongside the ticker that has near-identical text.
- Fix: `#matchup-ticker-scroll-text` id added to the ticker's `<span>` (`index.html`).
  `updateMatchupSubtext()` now clears `#matchup-subtext` to `""` in the ready state and instead
  writes the team-specific sentence into the ticker span; the two "pick both teams" /
  "can't be the same team" warning states still use `#matchup-subtext` as before (those are short
  and don't overlap anything). `MATCHUP_TICKER_DEFAULT_TEXT` captures the ticker's static default
  once at load so it can be restored if the matchup gets cleared back to an unready state.

## 3. Quicksilver font tried for matchup team name

Session 53 left the matchup-screen team name (`.coverflow-name`) on an Arial-Black-plus-CSS-
transforms approximation, with an open ask for the owner to pick a real font (Anton/Racing Sans
One/Bungee/Alfa Slab One shortlist). Owner instead had a licensed "Quicksilver" font already
installed system-wide (`C:\Users\Fresh\AppData\Local\Microsoft\Windows\Fonts\Quicksilver.ttf`, a
per-user install, not in `C:\Windows\Fonts`).

- Copied into `wwwroot/fonts/Quicksilver.ttf` (same pattern as `Outfit-Variable.ttf`).
- Added `@font-face` in `style.css` (top of file, next to Outfit's).
- `.coverflow-name`'s `font-family` now tries `"Quicksilver"` first, falling back to the existing
  Arial Black / Impact / Outfit stack.
- **Untested visually** -- owner asked to "try it and see how it looks," hasn't confirmed yet.
  Quicksilver's weight/style may not match the existing `font-weight: 900` / `font-style: italic` /
  `skewX(-12deg) scaleY(1.1)` transform stack tuned for Arial Black -- may need adjustment once seen
  live (the transform block exists specifically to fake what Quicksilver might already provide
  natively).

## 4. Icon polish pass -- "all icons rounded/polished/reflective"

Owner asked broadly for this across the app. Found the existing design language already has it:
`.team-swatch` (10px radius, gloss `::before` gradient, inset-highlight box-shadow) and
`.brand-mark`/`.ticker-logo` (9px radius, same gloss treatment, glow pulse) were already fully
polished. The one outlier was `.icon-btn` -- the generic small icon button used everywhere (close
buttons, chevrons, etc.) -- flat, transparent background, no depth, 8px radius.

- `.icon-btn`: bumped to 10px radius, added a glass-pill background (`rgba(255,255,255,0.04)`,
  1px `--glass-border`), the same gloss `::before` gradient overlay and inset-highlight box-shadow
  `.team-swatch` uses, scaled down for a 30px glyph button. Hover state bumped from
  `rgba(255,255,255,0.06)` to `0.1` to stay visible against the new resting background.
- Didn't touch small status/indicator dots (`.team-swatch.configured::after`, `.category-dot`,
  etc.) -- those are state markers, not icons, and glossy-tile treatment would misread as
  clickable.

## 5. New feature: CFB27 field-position volume system

Owner's live-game idea: CFB27's default scorebug shows a ball-position number with an arrow next to
it ("26▲"/"35▼", already noted in `ScorebugPreset.CollegeFootball27`'s doc comment from Session
50/51's calibration) -- up means the ball's on the attacking side past midfield, down means the
opposite. Owner wants this to simulate the two bands' physical seating: home plays full volume when
the arrow's up, quieter (60%) when down, and the reverse for away -- **scoped to Big Game + CFB27
preset only**, and **stacked on top of** (not replacing) the existing Big-Game-away-quiet
multiplier, per the owner's explicit call to keep tonight's already-confirmed routing logic intact.

- **`GameWatcher.cs`**: new public `bool? ArrowUp` property. New `SampleFieldPositionArrowFromWindow`
  (async, called from the same "down"-region tick as `SampleTimeoutsFromWindow`/
  `SamplePossessionFromWindow`) -- OCRs the *same* `AwayUnderlineFx*`/`HomeUnderlineFx*` crops
  already used for brightness-based possession (no new preset fields needed, per the existing
  preset's own comment that this indicator lives in that same slot). Only one side's slot actually
  renders text at a time (whichever side has the ball); `ParseFieldPositionArrow` checks the OCR'd
  text against a generous accepted-character set (`▲ ^ Λ ᐱ` for up, `▼ v V ᐯ` for down) since OCR
  reliably reading an arrow glyph (not a normal font character) is unconfirmed. Null (no-op) until
  a parseable read comes in, wrong preset, or an ambiguous/blank frame -- same "don't guess"
  philosophy as the rest of the file's possession handling. New `OcrCropAsync` helper factors out
  the crop-then-OCR pattern shared with the region loop.
- **`WebMainForm.cs`**: new `FieldPositionVolumeMultiplier(string side)` -- returns 1x unless Big
  Game + CFB27 + `ArrowUp` has a value, in which case home=1x/away=0.6x when up, reversed when
  down. Called from `ResolveEventRouting` (multiplies into the existing `volumeMultiplier`, only
  when `sideAllowed` -- doesn't affect the allow/block decision itself, only volume).

**NOT YET LIVE-CALIBRATED** -- owner explicitly agreed to proceed on a best-effort basis ("yes best
effort ill test that tomorrow... thats also the remote play scorebug"). Next session (or tomorrow's
live game) needs to confirm: (a) Windows OCR actually reads the arrow glyph as one of the accepted
characters rather than silently producing blank/garbage text every tick, (b) the crop box (reused
from the underline possession crop) is tight enough to catch the arrow without also catching the
yard-line digits in a way that confuses the character match, (c) the up/down -> home/away volume
mapping actually matches what's seen live (the owner's rule was inferred from the "26▲"/"35▼"
description, not yet confirmed against a real snap).

## Build/test status

- `dotnet build BandAudioHook.csproj` -- clean, 0 warnings/errors. (First attempt failed on an
  MSB3027 file-copy lock because the previous build's Bandroom.exe was still running; killed and
  rebuilt clean.)
- `dotnet test src/Bandroom.Core.Tests` -- 59/59 passing, unchanged from Session 53 (no test
  coverage added yet for the new `ArrowUp`/`FieldPositionVolumeMultiplier` logic -- worth adding
  once the live-calibration pass above confirms the OCR/character-set assumptions are right).

## Real next steps

1. **Live-calibrate the CFB27 arrow read** (item 5 above) -- the owner's own top priority for
   tomorrow. Watch the log line `[field-position] arrow now: ...` during a real game, confirm the
   accepted-character set actually catches what Windows OCR produces for the arrow glyph, adjust
   `ParseFieldPositionArrow`/the crop box if not.
2. **Confirm the Quicksilver font swap looks right** (item 3) -- owner hasn't seen it live yet;
   may need weight/transform tuning once viewed, or a fallback if Quicksilver doesn't render well
   with the existing skew/glow stack.
3. **Visually confirm the three matchup-screen fixes** (items 1-2) in the running app -- popover
   text wrapping/centering and the ticker text swap were made from code/screenshots, not yet
   re-screenshotted against the live app.
4. Once the owner is done live-testing and confirms, run `release.ps1` -- carried over from every
   prior session's handoff, nothing from Sessions 46-54 has been released yet.
5. Session 53's still-open items remain open (not touched this session): triage the remaining
   `/code-review high` findings, the possession-detection root cause investigation, and the
   `HighPriorityOverlapGrace`/`SamplePossessionFromWindow` freeze-frame gap.
