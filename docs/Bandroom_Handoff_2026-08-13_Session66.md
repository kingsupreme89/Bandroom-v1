# Bandroom Handoff — August 13, 2026 — Session 66

Same idea as always: what happened, explained plain.

## Fixed: Volume Sliders and Marketplace Pill Weren't Team-Tinted

Session 65 tinted the Band Director dashboard's knobs/pills/panels with the active team's
primary color, but explicitly scoped that pass to the Band Director screen only, since that
was the one screen with NO team tint at all. You confirmed tonight the rest of the app should
match too.

Two leftover spots were still hardcoded to the fixed cyan `--accent` instead of following the
"every pill/panel tints with the active team's color" rule adopted 2026-08-09:

- **Volume sliders** (Master/Home/Away/PA/Whistle) — the base `.slider` thumb and its glow were
  hardcoded cyan (`rgba(34, 211, 238, ...)`). Now uses `var(--team-primary, var(--accent))` for
  both fill and glow, same treatment the Band Director master-volume knob already had. Since
  the base `.slider` class now does what `.bd-knob-slider` was a one-off override for, removed
  the now-redundant `.bd-knob-slider` rule.
- **`.pill-marketplace`** (Teams/Save/Discord/Band Director/Share Profile/etc. pills) — had its
  own hardcoded cyan border, overriding the base `.pill` class's team-secondary tint underneath
  it. Switched it to the same `color-mix(... var(--team-secondary, var(--accent)) ...)` token
  the base pill class already uses, so it's no longer a special case.

Everything else checked (base `.pill`, `.situation-btn`, `.matchup-btn`, Sound Booth's rotary
`.sb-knob`) was already following the team-tint rule from earlier sessions — these two were the
only stragglers.

## New Feature: CFB 26 Console Wired to the Field-Position Arrow / Big Game Volume System

Session 64 (2026-08-12) added a field-position arrow read (`GameWatcher.ArrowUp`) and a Big
Game volume multiplier keyed off it, but scoped it to the CFB27 preset only — CFB27 was the
only preset it had been built/tested against. You asked to wire the CFB 26 Console preset
(`ScorebugPreset.CollegeFootball26Console`) into the same system.

Both gates that previously checked `ActivePreset.Name == CollegeFootball27.Name` now also pass
for `CollegeFootball26Console.Name`:

- `GameWatcher.SampleFieldPositionArrowFromWindow` — the OCR read that sets `ArrowUp`.
- `WebMainForm.FieldPositionVolumeMultiplier` — the Big Game volume multiplier that reads it.

**Caveat carried over, not fixed tonight**: `CollegeFootball26Console`'s underline crop
coordinates (`AwayUnderlineFx*`/`HomeUnderlineFx*` — what the arrow read actually OCRs) are
still the unverified placeholder values cloned from `ConsoleScorebugV1`, flagged in that
preset's own doc comment since 2026-08-09. The feature will now *run* for CFB26Console, but
its accuracy on a real console broadcast is unproven until a live CFB 26 console screenshot is
used to re-calibrate those crops — same open item CFB27's penalty overlay has (see below).

Build verified clean (0 errors, 0 warnings) after closing the running app instance (its exe was
locking the build output).

## Explained, No Code Change: What "No Dynasty-Save Parser" Means

You asked what a Known-Gap line from a prior session's audit ("no dynasty-save file parser
exists for CFB27") actually meant. Answer: EA's CFB27 Dynasty (career/franchise) mode writes a
save file to disk with roster/schedule/game-state data. There's a bridge method reference
(`ScanDynastySave`, `WebMainForm.cs:1700`) that something in the UI expects to call, but the
code to actually open and parse that save file was never written — it's an unimplemented
feature stub, not a bug in anything that currently runs. Would be a separate feature (auto-
detecting your next opponent from your dynasty save) if you want to pick it up later.

## What To Test Live

1. **Slider/pill team tint** — open Sound Booth, eyeball the Master/Home/Away/PA/Whistle
   sliders and any `.pill-marketplace` buttons (Teams, Save, Band Director, etc.) match the
   active team's primary color, same as the Band Director dashboard already does.
2. **CFB 26 Console field-position volume** — switch the scorebug preset to "College Football
   26 Console," start a Big Game, and see whether the arrow-driven volume shift does anything
   sensible. Given the underline crop caveat above, treat this as a first live check, not a
   confirmed-working feature yet — screenshot any obviously wrong reads so the crops can get
   re-calibrated.

## Known Gaps / Not Touched Tonight

- CFB27's `PenaltyAgainstFx*` crop is still an unverified clone of CBS's coordinates (from
  Session 65's audit) — needs a live CFB27 penalty-overlay screenshot.
- CFB26Console's underline crops (now load-bearing for the arrow read too, as of tonight) are
  still unverified placeholders — needs a live CFB26 console screenshot.
- Dynasty-save parsing (`ScanDynastySave`) remains an unimplemented stub — explained above, not
  built tonight since it's a distinct, larger feature.

That's everything for tonight!
