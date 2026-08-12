# Bandroom Handoff — Session 52 (2026-08-11)

Continuation of Session 51, live-fire during the same real game. Two areas of work: (1) a
gameplay-audio rule change, and (2) a full rebuild of the "Start a Game" matchup screen, which
went from "fullscreen backdrop wired in but paused, no code written yet" (Session 51's real next
step #1) all the way to a working redesign, several iterations deep. Build clean (0
warnings/errors), 59/59 Core tests passing.

## 1. 3rd & Long — balanced dual-fire, no longer home-only

Owner (via event-log screenshot mid-game): a real 3rd & long stop is a big enough moment that
away's own band should get it too, not just home's. Reverses a same-day-earlier call ("rare/subtle
enough that even a full away band wouldn't bother").

- `WebMainForm.HomeOnlyAlwaysEventKeys`: removed `"Defense: Third Down"`.
- `DefenseThirdDownHelper.cs`: volume changed from `BigGame ? 100 : 80` to flat **100**.
- `OffenseDownHelper.cs`: the 3rd-down-long branch, which used to return nothing, now returns a new
  `"Offense: Third Down"` key at volume **60** — same balanced dual-fire shape as
  `Offense/Defense: Third Down Short`, Defense at full volume (the bigger moment), Offense ducked
  under it. `EventTagMapping` already had `"Offense: Third Down" = "down:3rd"` from an earlier
  session, so no new UI wiring needed.
- `EvaluatorTests.cs`: `OffenseDownHelper_LongThirdDown_DoesNotFire` renamed to
  `..._FiresOffenseThirdDownAt60` and updated to assert the new firing behavior instead of null.
  59/59 passing after the update.

## 2. Matchup screen — fullscreen photo backdrop + redesign (multi-pass, still live)

Starting point: Session 51 had the CSS/JS plumbing for per-team background photos
(`--team-bg-image`, `GetTeamBackgroundUrl`) sitting on `.matchup-column` already, but confined to
the narrow flex-row strip between the (always-visible) instructions paragraph and the footer —
never actually read as "fullscreen." Owner drove this through many small live rounds; net state at
end of session:

- **True full-bleed backdrop**: `.matchup-columns` is now `position: absolute; inset: 0` on
  `#matchup-dialog` (which got `position: relative`) instead of a normal-flow flex child — the
  team-photo halves now sit behind the ENTIRE dialog (header, footer, everything), not just the
  middle strip. Every other direct child of `#matchup-dialog` got `position: relative; z-index: 1`
  (or higher) to stay above it.
- **Vignette lightened**: the top/bottom black gradients over the photo were dropped from
  0.94/0.85 peak opacity to 0.6/0.5 so the photo actually reads through.
- **Coverflow neighbors removed** (matchup screen only — `renderMatchupCoverflow`'s own local
  `positions` array, not the shared one team-picker/onboarding/favorite-team coverflows use):
  dropped the tilted cf-l1/cf-l2/cf-r1/cf-r2 tiles, center logo only. Reasoning: the side-grid's own
  scroll already covers browsing, the neighbors were redundant.
- **Side-grid (icon scrub list) moved and reworked**:
  - Moved from flanking the coverflow at the outer screen edge into `.matchup-column-main`,
    directly below the big center logo/name, centered under it.
  - `renderMatchupSideGrid` now calls `scrollActiveTileToCenter` on every render so it opens
    centered on the currently-picked team instead of scrolled to the alphabetical top.
  - `wireMatchupSideGridWheel` (new): wheel scrolling is ~2.75x faster than native, and **loops** —
    scrolling past Z wraps to A and vice versa (a `scrollTop` teleport at the boundary, not a
    seamless duplicate-content carousel).
  - CSS: `flex: 1 1 260px` (was a fixed `0 0 90px`) so it grows to fill whatever vertical room is
    left below the logo instead of a tiny fixed strip — "make the scroll longer."
- **Text readability over the photo** ("island" scrims, glass language): header, footer
  subtext/actions, and (at the time) the instructions paragraph each got a translucent
  blurred-glass background so text stays legible over busy photo detail.
- **Controls consolidated into a popover**: Last-Matchup pill, Big Game toggle, and the two
  scorebug-side/away-band toggles (previously scattered — two up top, two down past the coverflow)
  are now one `.matchup-controls-island`, opened via a **"Game Settings"** pill next to the
  scorebug switcher in the header (`wireMatchupGameSettingsPill`, generic
  `wireMatchupPopoverPill(btnId, panelId)` helper shared with instructions-toggling before that
  became a ticker — see below). The three toggle rows were converted from plain checkbox+label to
  `.pill-toggle` (checked state joins the app's standing `pill-glow-pulse` glow language).
- **Instructions moved to a bottom ticker**: the always-visible paragraph (then briefly a
  Help-pill popover) is now `.matchup-ticker` — a bottom-anchored scrolling marquee, same visual
  language as the app's existing `#bandroom-ticker`, reusing its `ticker-scroll-left` keyframe.
  The Help pill was removed since the tip is now always visible.
- **GAMETIME button — real bug, not just cosmetic**: owner reported "GAMETIME does nothing, still
  on the matchup screen." Root cause: once `.matchup-columns` went `position: absolute` (full-bleed
  backdrop, above), it stopped reserving space in `#matchup-dialog`'s flex column, so the footer
  (`save-profile-actions`/`save-profile-subtext`) collapsed straight up to sit right under the
  header — directly underneath the Game Settings popover's own top-anchored position. The
  popover's higher `z-index` meant clicks aimed at GAMETIME were actually landing on the popover
  panel. Fixed by pulling the footer fully out of flow too, pinned to the bottom
  (`position: absolute; bottom: 48px` for actions, `92px` for subtext, both above the ticker) —
  can't overlap the popover regardless of what else collapses. Also restyled as a large flashing
  green pill (`gametime-flash` keyframe) per owner request, matchup-screen-scoped.
- **Team name styling** (font, size, position, color — several iterations):
  - `.coverflow-stage` height capped at `clamp(280px, 36vh, 440px)` (was `min(48vh, 420px)`, often
    taller than the ~420px max logo it centers, leaving a big dead gap above the name below it).
  - `.coverflow-name` pulled up further with a small negative margin.
  - Font: first tried recreating a supplied "Machton" varsity-script reference via
    `Segoe Script`/cursive stack (closest built-in Windows face — can't embed the actual commercial
    font, app only ships Outfit, see `AppFonts.cs`/`wwwroot/fonts`). Owner then supplied a second,
    different reference ("SPORT" — bold condensed all-caps block/slab) and the font was swapped
    again to `Arial Black`/`Arial Narrow Bold`/Impact (same stack the app's own
    `[id$="-title"]` headers already use per the design system), condensed via
    `letter-spacing: -0.01em` + `transform: scaleY(1.12)`, sized up to `clamp(34px, 4vw, 54px)`
    (was `clamp(26px, 3.2vw, 42px)` under the script version). **Current state uses the block font,
    not the script one** — the script-font CSS was fully replaced, not left as a fallback.
  - Color: gradient fill (`--side-secondary` top → `--side-primary` bottom, both already set per
    column in `renderMatchupCoverflow`) via `background-clip: text`, per owner request to use the
    team's own colors instead of plain white.
  - Glow: added a slim neon `-webkit-text-stroke` outline in `--side-secondary` (the color
    opposite the gradient's dominant `--side-primary`) plus a pulsing `drop-shadow` glow
    (`matchup-name-glow` keyframe, 2.4s) — "team-themed glow, opposite of the primary color."

### Open thread — unresolved at session end

Owner's last request ("add the one over the scroll into the ticker") could not be pinned down after
two rounds of clarifying questions — both attempts to identify which element ("team name above the
scroll"? "subtext line"?) got an unreadable/garbled reply. **Nothing was changed for this specific
ask.** Whoever picks this up next should ask the owner directly, in person/voice if possible, what
"the one over the scroll" refers to before touching anything — the matchup screen has already been
through many rapid iterations this session and doesn't need a guessed-wrong change on top.

## Build/test status

- `dotnet build BandAudioHook.csproj` — clean, 0 warnings/errors after every change this session.
  App was killed/relaunched repeatedly for the same `AppContext.BaseDirectory`-served-wwwroot
  stale-build pattern as every prior session (confirmed with the owner before the first kill, then
  just proceeded on later ones since the owner was actively iterating live and expecting
  rebuild+relaunch each round).
- `dotnet test src/Bandroom.Core.Tests` — **59/59 passing** (58 carried over + 1 updated for the
  3rd-down-long behavior change).
- Release (`release.ps1`) not discussed this session — same as Session 51, nothing from Sessions
  46–52 has been released yet.

## Real next steps

1. Resolve the open "one over the scroll into the ticker" ask above — get a plain-language answer
   from the owner first.
2. The matchup screen went through ~6 rapid visual iterations this session (fullscreen backdrop →
   island → popover → ticker → font swap ×2 → glow). Worth a full visual pass end-to-end next
   session to confirm nothing from an earlier iteration was left half-migrated (e.g. stray CSS
   rules for the since-removed Help pill or script-font version) — most were cleaned up as the
   session went, but this was a lot of fast churn in one file.
3. Watch for a repeat of the "-- skipped: we haven't figured out which team has the ball yet"
   burst flagged in Session 51 under the 25-point possession margin — not touched this session,
   still an open question of whether it's a real regression.
4. Once the owner is done live-testing and confirms, run `release.ps1` — nothing from Sessions
   46–52 has been released yet.
