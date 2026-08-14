# Bandroom Handoff — August 13, 2026 — Session 68

Same idea as always: what happened, explained plain.

## Removed: Scorebug-Layout Switcher on the Matchup Toolbar → Remote Play Toggle in Game Settings

The pill+arrows switcher that used to sit in the matchup screen's header (cycling PC "Kam's CBS"
vs. console OCR-calibration presets) is gone — console presets aren't offered on that screen at
all anymore, since testers are all on PC/remote-play now. The "Game Settings" pill that used to
sit next to it now stands alone, still centered in the header.

In its place: a **Remote Play (console/streaming — skip RAM reader)** toggle inside the Game
Settings island. Checked = BANDroom never even attempts to launch the bundled RAM reader for that
game (moot for console/streaming — no local PC process to read memory from). New
`ConfigStore.RemotePlayModePath`/`LoadRemotePlayModeEnabled`/`SaveRemotePlayModeEnabled`, wired
into `WebMainForm.StartWatchingIfMatchupSet`'s existing RAM-reader-launch guard. Deliberately a
separate flag from the existing RAM-mode opt-in accuracy toggle — unchecking Remote Play never
implicitly turns RAM mode ON, it only ever forces it off when checked.

The old per-preset dropdown in the gear-icon Settings dialog ("Scorebug position") is untouched —
that's a separate advanced-calibration surface, not what this session's request was about.

## Fixed: Left Sidebar Didn't Match Team Theme

`.nav-rack-item`/`.nav-rack-group-label` (The Bandroom, Coffee's Corner, Sound Bank, My Downloads,
Auto-Assign) were flat grey (`var(--text-muted-2)`/`var(--text-muted)`), violating the "no grey
text" rule in the design system. Now follow `var(--team-glow, var(--accent))` like every other
themed element in the app, with a matching hover state (tinted background + border).

## Added: 59 New Team Backgrounds (AAC/C-USA/MAC/Mountain West/Sun Belt/Independents)

You'd dropped these into conference-named subfolders under the live `TeamBackgrounds\` folder that
morning, but `TeamBackdrop.cs`'s `FindImagePath` only ever looks at flat files directly in that
folder by exact team name — subfolders are invisible to it, and the filenames themselves
(`Army_BR_1.png`, `East_Carolina_BR_1.png`, etc.) don't match team names anyway.

Mapped each file to its real roster name (per `TeamColors.cs`), resized to a max 1920px long edge,
and re-saved as JPEG quality 85 (originals were 2–3MB PNGs; now 100–380KB) at the root of both the
live UserData `TeamBackgrounds\` folder and the repo's shipped copy (so new installs get them
too). Skipped the `PAC12\` subfolder entirely (all 8 files in it duplicated teams already covered
at the root) and a stray `7z2602-x64.exe` that had landed in `Sun_Belt\` by accident. The now-empty
conference subfolders were left in place (couldn't get permission to delete them this session) —
harmless, `FindImagePath` never looks inside them.

## Fixed: Scorebug Thumbnails Were Cropped Square Instead of Showing the Full Bug

Both the Game Settings skin-switcher thumbnail and Coffee's Corner's gallery tiles used the shared
`.marketplace-card-thumb` styling — square `aspect-ratio: 1` + `object-fit: cover`, correct for
song/album art but wrong for wide/landscape scorebug crops, which were getting most of their width
cropped off. Both switched to `object-fit: contain` in a taller box scoped to just these two
elements (`#coffees-corner-gallery .marketplace-card-thumb`, `.scorebug-skin-switcher-thumb` →
later restructured, see below).

## Redesigned: Coffee's Corner Gallery Is Now a Large List, Not a Small-Tile Grid

Owner feedback after the first fix: tiles were still too small to actually compare skins. Changed
`#coffees-corner-gallery` from a `repeat(auto-fill, minmax(120px,1fr))` grid to a single-column
list of large rows — 260×110 thumbnail on the left, title/dimensions on the right.

## Redesigned: Game Settings Skin Preview Is Now a Full-Width Strip, Not a 56×74 Box

Same feedback, second location: the switcher's own thumbnail (next to the prev/next arrows) was
also too cramped. Restructured `.scorebug-skin-switcher` to stack the pill+arrows row above a new
full-width 120px-tall preview strip (`.scorebug-skin-switcher-preview`, spans the Game Settings
popover) instead of squeezing a small thumb into the row itself.

## Fixed: Wrong/Mismatched Scorebug Thumbnail Images

`espn2020.png`'s file content turned out to actually be the NBC 2024 screenshot (byte-identical
hash match) — a leftover mixup from an earlier auto-matching pass (see Session 67's own note about
score/clock coincidentally matching the wrong skin). Replaced all four bundled thumbnails
(`fox2021.png`, `fox2025.png`, `espn2020.png`, `nbc2024.png`) with the four Photoroom'd reference
screenshots you supplied, matched by actual visual design this time (helmet/mascot/logo details,
not score-text coincidence).

## New Feature: Live Scorebug Overlay Using Coffee's Actual Theme Files

The big one this session. You asked why GAMETIME didn't pop up a visible scorebug — turned out
BANDroom only ever absorbed Coffee's **headless** RAM-reader helper (writes score data to a JSON
file, no window at all); the actual on-screen bug rendering lived in Coffee's separate Electron
app, which was never bundled.

First pass wrongly concluded the bundled theme HTML files (FOX 2021/2025, ESPN 2020, NBC 2024,
NBC 2024 Monochrome) had no live-data hook and built a BANDroom-styled replacement instead. You
correctly rejected that — *"was it gonna be coffees if not then we don't want it."* Re-reading the
actual theme source (not just the outer wrapper) found the real answer: **4 of the 5 themes do
have a real live-data bridge**, built by Coffee for exactly this purpose —

- Every bindable field carries a `data-cfb27-bind="away.score"` (etc.) attribute.
- The theme's own script exposes `window.updateScorebug` / `window.CFB27` / `window.scoreboard`
  with `.update(obj)` — very permissively aliased field names (`awayScore`, `away_score`,
  `visitorScore`, `team1Score` all resolve the same way).
- Confirmed present in ESPN 2020, NBC 2024, NBC 2024 Monochrome, and FOX 2025 (the last one buried
  inside its bundler-exported payload, but present).
- **FOX 2021 is the one true exception** — genuinely no live-data hook anywhere in that file, a
  frozen single-file export. Not fixable from BANDroom's side.

Built accordingly:

- **`ScorebugOverlayForm.cs`** (rewritten) — always-on-top, click-through (`WS_EX_TRANSPARENT`),
  no resize/drag chrome, sized/positioned from the theme's own authored canvas dimensions (scaled
  to fit ~45% of screen width, never up). Loads the **real** theme HTML file for the
  currently-saved scorebug skin (served via a `scorebugtheme` virtual host mapping, same pattern
  every other page in the app uses) and pushes live data into `window.updateScorebug` on a 400ms
  timer.
- **`WebMainForm.ResolveActiveScorebugThemeFile()`** — resolves the saved skin name
  (`ConfigStore.LoadSavedScorebugSkin`) to its real file path + canvas size, checking bundled then
  external `library.json` (same precedence `GetScorebugThemeGalleryFromWeb` already uses).
- **`WebMainForm.BuildScorebugOverlayPayloadJson()`** — builds the live payload from the exact same
  data sources already powering the rest of the app (`GameWatcher.CurrentSnapshot`,
  `ConfigStore.LoadLastMatchup`, `TeamColors`/`TeamLogo`). Quarter/down/distance sent as raw
  values (the theme's own `quarterText()`/`downDistanceText()` do the "4TH"/"1ST & 10"
  formatting); clock pre-formatted as `M:SS` since the theme just stringifies whatever it's given.
- Shown on `StartWatchingIfMatchupSet()` (GAMETIME or manual Start Watching), hidden on Stop
  Watching, disposed on app close — same lifecycle the rejected first pass already had right.
- `WebBridge.LogoUrl`/`ColorHex` widened from `private` to `internal static` so the overlay can
  reuse them instead of duplicating team-logo/color lookup logic.
- **FOX 2021** gets a "Coming Soon" badge (owner's call — keep it pickable, just marked) in both
  Coffee's Corner's gallery and the Game Settings skin switcher, since it'll only ever show its
  frozen example numbers.

Build verified clean (0 errors, 0 warnings) after each change; had to close the running Bandroom.exe
each time since its exe/DLL were locked mid-session.

## Known Gaps Carried Forward

- FOX 2021 will never show live data unless Coffee re-exports it with the same bridge the other
  four skins have — not fixable from BANDroom's side.
- FOX 2025's live-data bridge lives inside a bundler-exported payload that takes a moment to
  unpack after navigation; the push loop is guarded (`window.updateScorebug &&`) so early ticks
  before it's ready are silent no-ops rather than errors, but this hasn't been confirmed against a
  live game yet — worth watching the first real test.
- The empty `AAC\`/`C-USA\`/`MAC\`/`Mountain_West\`/`Sun_Belt\`/`Independents\`/`PAC12\`
  subfolders under `TeamBackgrounds\` are still there (permission denied on cleanup this session) —
  harmless, but safe to delete by hand whenever convenient.
- Live overlay's on-screen position/size hasn't been tested against a real running game yet this
  session — worth a live GAMETIME test to confirm placement/scale reads right over actual gameplay.
