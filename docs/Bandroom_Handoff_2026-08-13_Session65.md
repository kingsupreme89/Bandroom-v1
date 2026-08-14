# Bandroom Handoff — August 13, 2026 — Session 65

Same idea as always: what happened, explained plain.

## New Feature: Adjustable Pregame Runout Timer

Session 64 hardcoded the black-screen-timed pregame runout to 13 seconds (owner-confirmed live
that night). You asked for it to be user-adjustable instead, 15-45 seconds, since 13s was only
confirmed on one matchup and load times vary. There's no other way to catch "team ran out of the
tunnel" reliably, so getting this dialed in matters.

Added `PregameRunoutDelaySeconds` to the existing Audio Timing settings (same JSON file as
Pre-roll/Fade-start/Fade-duration/Cooldown, under Profile → Settings). Default is 15s (the floor
of the new range — the old 13s value doesn't fit inside the requested 15-45 window, so it got
bumped up to the nearest valid value rather than left out of range). `GameWatcher` now reads this
live off `ConfigStore` instead of a hardcoded constant, clamped to 15-45s as a safety net even if
something writes a bad value to the settings file directly.

## Fixed: Rectangular Box Glow Behind the LIVE Pill

You sent a screenshot showing a harsh rectangular pink glow bleeding around the "LIVE" status
pill in the header. Root cause: `#header-bar` has its top/left/right borders explicitly turned off
(it's meant to show only a bottom underline, since it spans the full window edge-to-edge), but it
was still using the shared `neon-pulse` animation, which glows on **all four sides** via
box-shadow. That put a flat rectangular halo along edges that have no border at all, clashing with
the pill's own soft rounded glow sitting right next to it.

Fixed by giving the header its own `header-bar-glow-pulse` keyframe, scoped to a bottom-edge-only
box-shadow (positive Y offset, no spread) that actually matches its bottom-only border. Nothing
else uses `neon-pulse` in a way that has this same border/box-shadow mismatch, so this was header-
bar-specific.

## Fixed: Bandroom Arrows Only Cycled One Team's Photos

The prev/next arrows on the fullscreen Band Room viewer always cycled through the CURRENT team's
own background images, everywhere. You wanted that scoped: once a matchup is locked in (Game Day
mode), the arrows should stay on that team's gallery (switching teams there is the Away/Home bar's
job) — but outside locked-in mode, with no matchup constraining anything, the arrows should walk
the full team roster instead, one team's bandroom at a time.

`shiftBandroomViewer` now checks `state.matchupLocked`: if unlocked and there's more than one team,
it calls `selectTeam()` + reopens the viewer on the next/previous team in the roster instead of
just cycling images.

## Dashboard Tinting: Band Director Panel

The Band Director streamer dashboard (Twitch/YouTube chat commands, live log, queue, polls) was
plain grey — it never got updated when the "every pill/panel tints with the active team's primary
color" design rule was adopted back on 2026-08-09. Tinted `.bd-panel` (the four islands), the
`.bd-conn-pill` connection status pills, and the master-volume slider's thumb/glow, all with
`--team-primary`, matching the rest of the app.

## New Feature: Did You Know Close Button

The floating tips widget only had "Never show" (permanently disables) and "Next tip" — no plain
dismiss. Added a small X button in the corner that just closes the current tip without touching
the "never show again" preference.

## Confirmed Already Working: Remove PA

You asked for a way to remove an assigned PA clip. Turned out it already exists — "Assign PA" →
the assign dialog's "Clear" button already calls `ClearTrackAssignment(trigger, isPa: true)`. No
code change needed, just confirmed the path works.

## New Feature: Per-Event Settings Pill + Individual Fade Overrides

Biggest piece this session. Two parts:

**Consolidation**: the event card transport strip had 4 separate icon buttons (lead-in whistle
on/off, whistle speed cycle, 1.09x playback speed toggle, Stadium PA speaker effect toggle) plus
Track Info, all crowded into one row. Replaced the 4 toggles with a single gear (⚙) button that
opens a settings popover containing all of them as labeled rows, same open/close/drag popover
pattern the existing Share-to popover already uses. The gear button itself lights up (same active-
glow style the old individual buttons had) if ANY setting inside is non-default, so you can still
tell at a glance a card has custom settings without opening the popover.

**New feature — per-event fade override**: previously fade-out timing (when a clip starts fading,
how long the fade takes) was ONLY a global Sound Booth setting, applied to every clip the same way.
Added `FadeStartSecondsOverride`/`FadeOutDurationOverride` to `TriggerEntry` (nullable — null means
"follow the global setting," same as before this existed). Threaded through
`AudioPlayer.Play`'s fade loop (`fadeStartOverride`/`fadeOutDurationOverride` params, defaulting to
the global `FadeStartSeconds`/`FadeOutDuration` when null) at both real-game-fire and Preview call
sites. New "Override fade for this event" checkbox in the settings popover reveals two number
inputs (fade start / fade duration in seconds) when checked, saved via a new
`SetEventFadeOverrideFromWeb` bridge method.

Build verified clean (0 errors, 0 warnings) and launched for live UI verification.

## What To Test Live

1. **Pregame runout timer** — confirm the new 15-45s slider actually changes when the pregame
   trigger fires, across a couple of games (the old 13s was only ever confirmed on one matchup).
2. **Event settings gear pill** — open it on a few different cards, confirm whistle speed cycling,
   2x toggle, and PA effect toggle all still work exactly as before now that they're inside the
   popover instead of standalone buttons.
3. **Per-event fade override** — set a custom fade on one event, fire it for real in a game, confirm
   it fades on ITS OWN schedule and doesn't get overridden by the Sound Booth's global fade setting.
4. **Header glow / bandroom arrows / dashboard tint** — all pure visual changes, just eyeball them
   next time the app is open.

## Known Gaps / Not Touched Tonight

- Per-event fade override has no UI validation beyond the browser's own number-input min/max
  (0-120 for start, 0-30 for duration) — a wildly nonsensical combination (e.g. duration 0) will
  just skip straight to full-volume cutoff, same as the global setting already behaves in that case.
- Didn't touch the rest of the app's cyan-accent `.slider` thumbs (Master/Home/Away/PA/Whistle
  volume) to also tint with team color — this session's tinting pass was scoped to the Band
  Director dashboard specifically, since that was the one screen that had NO team tint at all.
  Worth asking whether the main sliders should match too.

That's everything for tonight!
