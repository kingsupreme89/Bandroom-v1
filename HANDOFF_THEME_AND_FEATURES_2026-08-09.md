# Bandroom Theme Overhaul + New Features — August 9, 2026 (evening session)

## What was requested

A large batch of asks in one message: universal color/glow/font rules, several specific screen
restyles, three new features (quick-load-by-abbreviation, second-instance prompt, LIVE status +
LOCK IN rename), and a fix for broken matchup-screen logos — with an explicit instruction to
self-audit and fix anything else found broken along the way.

## Design system (now a standing memory, not just this session)

Saved to `~/.claude/projects/C--Bandroom/memory/project_bandroom_design_system.md` — applies by
default to all future Bandroom UI work:
- No grey text anywhere (`--text-muted`/`--text-muted-2` now resolve to white).
- Glow/LED pulses always use the *lighter* of a team's primary/secondary color, never the dark
  one (`--team-glow`, computed live in `applyTeamGlowVars()`, app.js).
- Pills default to secondary-team-color tint + glowing pulse, white text always.
- Section headers get a bold block "sports vector" wordmark treatment (outline + team-color glow)
  — applies to actual titles only, never to individual event/track names.
- Text sizing bumps mean a modest bump (~14px→16px), not literal 2x.

## What changed

**CSS/JS theme pass** (`wwwroot/style.css`, `wwwroot/app.js`, `wwwroot/index.html`):
- `--team-glow` CSS var added, computed as whichever of primary/secondary is perceptually lighter
  (`relativeLuminance()`), driving every glow pulse app-wide.
- All 184 `font-size` declarations in style.css mechanically scaled ×1.14 (14px→~16px equivalent)
  via a one-off script — font-size only, no layout dimensions touched, so this couldn't break
  positioning/overflow the way scaling padding/width would.
- `[id$="-title"]:not(button), [id$="-header"]:not(button)` now get the block header font
  (bold, outlined, team-color glow pulse) — covers every dialog/panel title in the app in one
  rule instead of hand-editing ~20 elements.
- Situations panel: event names (e.g. "Penalty Flag") recolor to `--team-glow`; "Unassigned"/
  "PA: none" text goes white with a slim secondary-color outline; Assign/Assign PA pills get a
  primary-color glow pulse, white text.
- Category tab bar (Offense/Defense/Situations) tints with the active team's primary color
  instead of staying flat black.
- Clip Preview label and track name enlarged and set to solid white.
- **Bug found and fixed in the same pass:** `.pill-marketplace`/`.pill-auto-assign`/`.pill-rainbow`
  were overriding pill text to cyan/gold, contradicting the "pills always keep white text" rule
  that was just established — fixed to `#fff`.
- **Pre-existing bug found and fixed:** the "LIVE"/watching status dot never lit up green — CSS
  only ever styled a `.pill-on` class, but the JS applies `.pill-watching`/`.pill-waiting`.
  `.pill-on` was dead CSS. Fixed to target the real classes.

**"LIVE" status + LOCK IN flow:**
- `#watch-status` now shows "LIVE" (not "Waiting for window…") once the matchup is locked in,
  flashing twice (blue-white) on the transition, then holding a steady glow until Stop Watching
  clears it back to "Not watching" — see `setWatching()` in app.js.
- `#btn-matchup` renamed "Set Matchup" → "LOCK IN?" — **caught in self-audit**: `updateMatchupLabel()`
  was overwriting the button's text back to the old "Set Matchup" string on every load/matchup
  change, which would have silently undone the rename. Fixed both the HTML default and the JS
  fallback string.
- The "matchup pill turns green after GAMETIME" behavior the user described was already correct
  and working (`.matchup-btn.locked`) — no change needed there, verified only.

**Matchup screen logos (reported broken):**
- Root cause: last session's lazy-loading fix (`loading="lazy"` on team logo `<img>`s) collided
  with the matchup coverflow, which tears down and rebuilds its 5 tiles on every arrow-click/
  keystroke — an `<img>` could get destroyed before the browser ever resolved whether it was
  visible, so the logo never painted. Fixed by adding an `eager` flag to `fillTeamSwatch()`;
  the 4 coverflow-style pickers (team picker, matchup home/away, onboarding) now load eagerly
  since they're small (≤5 tiles) and churn constantly, while the 100+ team static grids keep lazy
  loading.

**New: Load Profile by abbreviation** (matchup side bar, "assign page"):
- Type a team's initials or partial name into the new input; a live hint shows the best match
  (`findTeamByAbbreviation()`). Pressing Load/Enter opens a confirm dialog ("Is \<team\> the team
  you found — the right team?") before actually switching the active profile via the existing
  `selectTeam()`.

**New: second-instance prompt** (`Program.cs`):
- Named Mutex detects an already-running instance. If found, shows a native MessageBox titled
  "Walk Into Another Room While We're Already At Practice?" asking whether to open a second copy
  anyway (defaults to No).
- **Caught in self-audit before this shipped:** the check was originally placed *before*
  `SquirrelAwareApp.HandleEvents(...)`. Squirrel launches this same exe with special flags during
  unattended install/update, and that handler exits the process itself once it's done — a
  blocking MessageBox ahead of it would have stalled an unattended installer waiting on a dialog
  nobody's there to click. Moved the check to run after Squirrel's own event handling.

## Environment note (worth knowing for next session)

Discovered mid-session: Git Bash's `/d/Bandroom` path resolves to a **separate, diverged**
`D:\Bandroom` directory, not the real project at `C:\Bandroom`. Several early sanity-check
commands in this session ran against that stale copy before this was caught — they were
re-verified against the correct `/c/Bandroom` path afterward, and everything checked out (784/784
balanced braces, clean `node -c`, clean `dotnet build`). Going forward, Bash commands in this repo
should use `/c/Bandroom` or no `cd` at all (the tool already starts there), never `/d/Bandroom`.

## What's NOT done / deferred

- The "opposite team color" instruction for pill glow is implemented as a two-color-pair
  approximation (secondary tint + primary glow) since the app only tracks one active team's
  colors as CSS vars at a time, not simultaneous home+away vars. A true home-glows-away-color
  system would need new CSS plumbing (`--away-team-primary` etc.) — flagged, not built this
  session.
- The "sports vector" header font is approximated with `Arial Black`/`Impact` + text-stroke +
  glow, not an actual custom font file (none was provided/downloadable).
- No visual QA was possible this session — no way to launch the actual WebView2 desktop app in
  this environment. Everything was verified via `dotnet build` (clean), `node -c` (clean), and a
  brace-balance check on the CSS. **Please launch the app and eyeball the Situations/matchup/
  header screens before treating this as fully verified** — a syntax-clean stylesheet can still
  look wrong.

## How to rebuild & test

```
cd /d c:\Bandroom
dotnet build BandAudioHook.csproj
# Then launch Bandroom.exe from bin\Debug\net10.0-windows10.0.19041.0\
```
