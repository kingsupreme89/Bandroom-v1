# Bandroom Handoff — Session 49 (2026-08-11)

Long session, many separate owner requests threaded together live (owner was watching a real game
and reporting issues as they happened, plus iterating on UI screenshots). Grouped by area below,
roughly chronological within each. Build clean (0 warnings/errors) and 53/53 Core tests passing as
of the last edit; **the final visual-polish thread (item 8) is INCOMPLETE — see "Not done" at the
bottom before doing anything else.**

## 1. Speed toggle: real pitch-preserving time-stretch (SoundTouch.Net)

Owner wanted the per-event "play faster" toggle to speed up WITHOUT changing pitch ("keep the key
the same"). Also tuned the multiplier down four times live (owner listening each round): 2x → 1.5x
→ 1.25x → 1.15x → **1.09x** (final).

- Old `SpeedSampleProvider` (relabeled the sample rate — cheap, but pitch rose with speed) **deleted
  entirely**, replaced by `SoundTouchSpeedSampleProvider` (`AudioPlayer.cs`), backed by the
  `SoundTouch.Net` NuGet package (pure-managed C# port, no native DLL). Bridges NAudio's
  interleaved-float `ISampleProvider.Read` against `SoundTouchProcessor`'s frame-based
  `Put/ReceiveSamples`, buffering + flushing at end-of-stream.
- All doc comments referencing the old sample-rate-relabel trick updated across `AudioPlayer.cs`,
  `TriggerEntry.cs`, `WebMainForm.cs`.
- Speed button icon changed from a text "1.5x" label to a ⏩ fast-forward glyph (matches the other
  icon-only transport buttons on the card).

## 2. Per-event alternate whistle now uses the Clipper, not a native file picker

The per-card alt-whistle button (🎏) used to call `BrowseAndSetEventAltWhistle` — a bare
`OpenFileDialog`. Now opens Clipper Island in a new **"alt-whistle" mode**
(`openClipperAssignForAltWhistle`), same pick-a-song → Trim... → "Set as Alt Whistle for This
Event" flow the GLOBAL whistle button already had.

- New `WebMainForm.SaveTrimAsEventAltWhistleFromWeb(trigger, startSec, endSec)` /
  `WebBridge.SaveTrimAsEventAltWhistle` — mirrors `SaveTrimAsLeadInWhistleFromWeb` but writes to
  `TriggerEntry.AltWhistlePath` (per-event) instead of the single global `LeadInWhistlePath`, same
  `whistles/` subfolder + fixed-per-trigger filename `BrowseAndSetEventAltWhistleFromWeb` already
  used.
- `app.js`: `_clipperAssignMode` gained `"alt-whistle"` alongside `"event"`/`"whistle"` — most of
  the `mode === "whistle"` checks became `isWhistleMode` (whistle OR alt-whistle) since they share
  UI shape; `openInlineTrimmerForWhistle` now keeps `_trimTrigger` set for alt-whistle mode
  specifically (global whistle mode still clears it) so the save button knows which bridge call to
  make.

## 3. Sound Booth: whistle + fade controls added to the Mixer tab

The docked Sound Booth (visible during Game Day mode, when `#adjust-panel` is hidden) had no way to
enable/upload a whistle or see the Fade Delay's plain-language caption — those controls only lived
in the now-hidden sidebar.

- New section in `#sound-booth`'s Mixer tab: "Lead-In Whistle" (enable toggle + "Choose / Replace
  Whistle..." button, mirrors the sidebar's `#leadin-whistle-section`) and "Fade Delay" (own slider
  + "Xs until fade-out — no fade-in" caption, mirrors the sidebar's Fire Sensitivity slider).
- `refreshLeadInWhistleSection()` now syncs BOTH copies (`sb-`-prefixed ids added) off the same
  bridge state. New `syncFadeDelaySlider(value, skipId)` keeps sidebar slider / Sound Booth slider /
  Fade knob-pill all in sync three ways. Also fixed a pre-existing bug: the sidebar's own Fade
  slider never hydrated from the saved value at startup (`refreshVolumeSliders` didn't include it).

## 4. "Copy From..." event-card button removed (was a stuck/dead popover)

Owner reported the "Copy assignment from..." popover wouldn't close and looked orphaned/dead. Root
cause: `openCardPopover()` reparents these to `document.body`; the ONLY cleanup was a sweep in
`openSituations()` on next refresh — if the user navigated away (e.g. into the Clipper/Trim screen,
which doesn't call `openSituations()`) before that refresh happened, the popover stayed orphaned on
`<body>` indefinitely. Owner: "we have a share button now, remove it" — so it's gone, not fixed.

- Removed the button, its popover markup, `wireSituationCopyFromPopover()` entirely, and the
  `.situation-btn-copy`/duplicate CSS. `.situation-copy-*` class names survive (Share's popover
  reuses that styling) — only the actual Copy-From element/function are gone.
- Also fixed the "Share this song to..." popover's own real layout bug while in there: the option
  rows used `justify-content: space-between` on one flex line, so a long event name + a long
  "overwrites <filename>" string collided/wrapped into each other. Now stacked vertically
  (`.situation-copy-option-name` / `-file` on separate lines).

## 5. GAMETIME opens the Band Room Viewer as the Game Day backdrop

Owner: the fullscreen screen that pops up on GAMETIME should be the Band Room Viewer (team photo
gallery, already existed as the "Enter Band Room" pill's fullscreen modal), with the Away/Home bar
as the way to switch sides, and a new checkbox deciding which one loads first.

- New checkbox in the matchup dialog: **"My team is Away (load their band room first)"**
  (`#matchup-my-team-away`) — distinct from the pre-existing `#matchup-screen-side-left` checkbox
  (that one's about scorebug OCR side-reading, unrelated).
- `confirmMatchup()` now calls `selectTeam(...)` for whichever side the checkbox picks, then
  `openBandroomViewer()`, instead of the old `applyVsBackdrop()` two-team split screen (left in
  place, unused from this flow, in case it's wanted again later).
- `.gameday-mode` CSS overrides turn `#bandroom-viewer-overlay` from a blocking fullscreen modal
  into a non-blocking backdrop (z-index -1, since the overlay is AFTER `#body` in the DOM — plain
  `z-index: 0` wasn't enough to sit behind it) behind the docked Situations/Sound Booth panels; its
  own close (X) button is hidden while docked (closed by Stop Watching instead, matching
  `exitGameDayMode`/`closeBandroomViewer()` now being called together in `setWatching`'s "off"
  branch). The existing Away/Home side-bar buttons already called `openBandroomViewer()` on switch
  (from a prior session) — that's the "arrows to switch teams" the owner meant, no new switcher UI
  needed.

## 6. Docked event-cards column: removed glass blur (was hazy over the new photo backdrop)

Once GAMETIME started rendering a real photo behind everything (item 5), the `.glass` panels on the
event-cards side (Away/Home bar, category tabs, event grid, Clip Preview) read as hazy/low-contrast
— their ~6%-opacity blurred glass was designed against a plain color glow, not a real photo.

- `body.gameday-mode` override on `#matchup-side-bar`, `#category-bar`, `#situations-panel`,
  `#clipper-island`: `backdrop-filter: none`, solid `rgba(10, 14, 18, 0.82)` background instead
  (matches the app's existing dark-card convention, e.g. `.situation-row`'s own color-mix). The
  Sound Booth dock on the right keeps its normal blur untouched — owner: "that one's perfect."

## 7. Game-logic fixes (from a live game the owner was watching)

### 7a. 4th down now overrides the generic Tackle for Loss cue

Owner: "a 4th down should always override a tackle for loss event." `TflHelper` fires a generic
"Defense: Tackle for Loss" whenever a down advances 2/3/4 with a yardage loss — including a 3rd
down loss that pushes to 4th, right before `BigEventHelper`'s more specific "Defense: Fourth Down"
cue covers that same moment shortly after. `TflHelper.Evaluate` now returns null when
`state.Current.Down == 4` instead of firing. New test
`TflHelper_SuppressedOnFourthDown_OwnerCall_FourthDownCueOverridesIt`.

### 7b. Home timeouts — were completely untracked (owner report investigated + fixed)

Traced an owner report ("Timeout fired weirdly early / labeled wrong side") down to a real gap:
`PlaySnapshot`/`TimeoutHelper`/`ScorebugPreset` only EVER read `AwayTimeoutsRemaining` — no
`HomeTimeoutsRemaining` existed anywhere, so a Home-team timeout never produced any cue at all.

- Added `PlaySnapshot.HomeTimeoutsRemaining`; `TimeoutHelper` rewritten to check both sides
  symmetrically (each only reacts while that side currently has the ball — same restriction the
  original Away-only code already had, not a redesign). `Defense:` EventKey prefix still handles
  routing to the opposing side automatically via `ResolveEventRouting`.
- `ScorebugPreset` got new `HomeTimeoutFx*` crop fields for all 3 presets. **These are UNVERIFIED
  best-guess placeholders** (owner explicitly chose this option over waiting for a real
  screenshot) — mirrored off each preset's own already-calibrated Away→Home underline-position
  offset, applied to that preset's Away timeout crop. Doc comments flag which preset's guess is
  shakiest (CollegeFootball27 — its Away value was ITSELF already a mirror of a real Home
  measurement, so the new Home value is a double-mirror). **Owner needs to watch a live game and
  confirm/report back** — if a Home timeout doesn't fire correctly, screenshot the moment it's used
  so the crop can be re-measured for real.
- `GameWatcher.cs`: generalized `SampleTimeoutSegments`/`CommitTimeoutsRemainingIfConfirmed` to run
  for both crops every tick (new `_lastHomeTimeoutsRemaining`/`_pendingHomeTimeoutsRemaining`
  fields, mirroring the Away ones exactly).
- 2 new unit tests (`TimeoutHelper_Fires_OnHomeDecrement_WhenHomeHasBall`,
  `..._DoesNotFire_OnHomeDecrement_WhenAwayHasBall`); `GameStateTestHelpers.Snap.With` gained a
  `homeTimeoutsRemaining` param.

### 7c. Turnover-double-fire / away-offense-home-defense reports — investigated, NOT code bugs

Two other owner reports turned out to be correct-as-designed once traced through the actual code,
not bugs:
- "Away offense got a first down but Home defense cue played" — `DefenseFirstDownHelper` fires
  "Defense: After Opening Kick" for the KICKING team's defense the instant the receiving team gets
  their first snap after a kickoff return — intentional, home-only-always. `Offense:`-prefixed
  events (a real Away first-down conversion) structurally cannot route to Home in
  `ResolveEventRouting`, so this wasn't a routing bug.
- Turnover firing twice for opposite sides ~5s apart: traced to OCR possession-read timing/flicker,
  not the same-tick dedupe (`EventRouter.Dedupe` only catches same-TICK duplicates, not two separate
  ticks) and not `TurnoverHelper` itself (correctly edge-triggered). No code change made — flagged
  as an OCR-accuracy issue, not something to blind-fix without more data.

## 8. Live event log file (real-time export)

Owner: the exported event log (used for the "whatsthedeal" review flow, opened in an external
editor) never updated — `ExportEventActivityLogFromWeb` only ever wrote a NEW timestamped file on
manual button click, so re-opening the same file never showed anything new.

- `EventActivityLog.Record()` now also rewrites a FIXED filename (`event_log_live.txt` in
  `ConfigStore.UserDataRoot`) in full on every single call — cheap (200-entry cap). An editor with
  auto-reload-on-change shows new events within ~1s of firing. Best-effort/try-catch — a write
  failure here must never crash the caller (the audio-firing pipeline).
- The in-app Event Log tab (Help & Guide) already polled live every 2s while open — untouched,
  wasn't actually broken, this was specifically about the exported-file workflow.

## 9. Matchup ("Start a Game") dialog: fullscreen takeover

Owner sent reference screenshots: wanted the dialog to go fully edge-to-edge (no visible dark
backdrop/rounded corners around it, like the reference images), reusing the existing split-screen
Away/Home coverflow layout that was already "near-full-viewport."

- `#matchup-dialog`: `width/height: 100vw/100vh`, `border-radius: 0`, `border: none`.
  `#matchup-overlay`'s dimming background removed (`background: none`) since nothing's left around
  the dialog to dim.
- **Follow-up bug (self-caused, fixed same session)**: first pass also set
  `backdrop-filter: none` and tried forcing the OS WINDOW to maximize
  (`EnsureWindowMaximizedFromWeb` — added then fully removed again, see below) to fix an apparent
  "blur" the owner reported. Turned out `.glass`'s blur had been doing double duty as the ONLY
  thing hiding the rest of the app's own UI (nav sidebar, Sound Bank, Sound Booth — still rendered
  behind the overlay in the DOM) through its own ~6%-opacity fill; removing blur with nothing else
  opaque behind it left everything behind showing straight through, unreadably overlapped with the
  dialog's own text (confirmed via owner screenshot).
- **Real fix**: `#matchup-dialog` background is now a SOLID opaque color (`var(--window-bg)`,
  `#161719`) instead of relying on blur or transparency at all. This also satisfies the owner's
  separate ask ("make it so we don't have to have fullscreen in order to have that feature") — the
  dialog now looks correct regardless of whether the app WINDOW itself is maximized, since
  `100vw`/`100vh` are relative to the app's own viewport (the WebView2 control's client area), not
  the OS screen. `EnsureWindowMaximizedFromWeb`/`EnsureWindowMaximized` (host method + bridge
  wrapper) were added then fully removed again once this was understood — confirmed no dangling
  references, build clean.

## Not done — pick up here next session

Owner's last few messages (mid-implementation when "handoff" was requested) asked for, on the
matchup dialog's team-select screen:

1. **Side-grid team icons** (`.matchup-side-grid .team-swatch` — the small vertical icon strip on
   the outer edge of each coverflow column, currently sized via the shared 44px-track default) —
   owner wants them **larger**, with a **team-color glow**, and a **"Mac Dock" style hover
   magnify + smooth transition** (icons scale up as the cursor nears them, tapering off for
   neighbors, like macOS's Dock). **NOT STARTED** — needs new CSS sizing/glow scoped to
   `.matchup-side-grid .team-swatch` plus new JS (mousemove-driven distance-to-scale calculation;
   this is a real interaction, not achievable with hover-only CSS for a true dock feel) wired into
   wherever the side-grid rows are rendered (`renderMatchupSideGrid`/`wireMatchupSideGrid` in
   `app.js`).
2. Main coverflow center logos made bigger (`.matchup-columns .coverflow-track .team-swatch.cf-center`
   clamp bumped from `210px–320px` to `260px–420px`) — **done**, but not visually confirmed against
   the owner's reference screenshot yet (just implemented, app not reloaded/screenshotted since).
3. "Very reflective" logos — `.matchup-columns .team-swatch-reflection` opacity bumped 0.55→0.75,
   mask fade extended 60%→78% (matchup-only override, team-picker/onboarding keep the original
   subtler reflection) — **done**, not visually confirmed.
4. "Smooth album flow" transitions — `.matchup-columns .coverflow-track .team-swatch` transition
   changed from the shared `0.22s ease` to `0.4s cubic-bezier(0.22, 0.61, 0.36, 1)` (matchup-only)
   — **done**, not visually confirmed.
5. Top+bottom vignette on `.matchup-column`'s background — was top-only fade before; added a
   matching bottom `linear-gradient(0deg, ...)` layer so the (now longer/stronger) reflection fades
   into the floor instead of stopping mid-air — **done**, not visually confirmed.
6. **Possible remaining z-index/layering leak**: the owner's screenshots during this thread showed
   `#bandroom-ticker` (the bottom credits bar, `z-index: 50`) text bleeding through the bottom edge
   of the matchup dialog even after the opaque-background fix (item 9) — this may have just been a
   stale pre-reload screenshot (the fix was applied but the app wasn't necessarily reloaded before
   that particular screenshot was taken), or it may be a real remaining leak. **Verify against a
   fresh reload before assuming either way** — if it's still leaking, `#matchup-overlay`'s z-index
   (60) should already be comfortably above the ticker's (50); check whether some ancestor is
   creating a separate stacking context that's defeating the comparison.

None of items 1–6 have been rebuilt/reloaded/screenshotted since the edits — they're pure CSS (no
C# changes needed), so no `dotnet build` is required, just reload the app.

## Build/test status

- `dotnet build BandAudioHook.csproj` — clean, 0 warnings/errors (confirmed after every C# change
  this session, most recently after the `EnsureWindowMaximized` add-then-remove).
- `dotnet test src/Bandroom.Core.Tests` — **53/53 passing** (was 51 at session start: +1 TFL/4th-down
  test, +2 Home-timeout tests).
- `node --check wwwroot/app.js` — passes.
- **App was closed/relaunched repeatedly this session** (build-lock conflicts, PIDs 24908→32212→
  4412→7076→25056→30540→31416→33356→35852 across rounds) — always via `taskkill` on the exact PID
  holding the file lock, then rebuilt clean each time. Not live-tested against a real running game
  since the Home-timeout/TFL-4th-down changes went in — those are unit-tested + build-clean only.

## Real next steps

1. Finish the side-grid Mac Dock icon effect (size + glow + hover magnify/transition) — see "Not
   done" item 1 above.
2. Reload the app and screenshot the matchup team-select screen to confirm items 2–5 above actually
   look right — none have been visually confirmed yet, only implemented.
3. Confirm whether the ticker z-index leak (item 6) is real or just a stale screenshot.
4. Owner needs to watch a live game and confirm the Home-timeout placeholder crop positions
   actually work (7b) — screenshot the exact moment if not, for recalibration.
5. Nothing from this session (or Sessions 46–48) has been released via `release.ps1` yet.
