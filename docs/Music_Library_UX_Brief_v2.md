# 🎨 Master UX Brief v2: Hybrid "Apple Music × Spotify" Glassmorphism Music Library

## 0. Why this brief exists (read first)
The current Sound Bank / Trophy Room / Marketplace surfaces are functionally broken, not just
visually dated: team tiles' edit controls misfire, per-team modals silently show zero uploads
even when uploads exist (e.g. Georgia), and "My Downloads" has no reliable index of what's
actually on disk. **This redesign must fix the underlying data/indexing bugs as part of the
work — a prettier skin on top of broken team-ID matching is not an acceptable outcome.** Visual
polish and correctness are the same deliverable here, not two phases.

Reference points for the target feel:
- **Apple Music**: generous album-art tiles, instant hover-preview, a persistent Now Playing
  bar, typographic confidence (large title, muted subtitle), buttery scroll physics.
- **Spotify**: dense but scannable rows for library/downloads, left-rail category navigation,
  a search-first mental model, unmistakable "this is playing" state (green pulse → team-color
  pulse for us), one-click add/save affordance on every card.
- Mine both for interaction *patterns* only — final visual language stays BANDroom's own dark
  glassmorphism (see [[project_bandroom_theme]]): pulsing team-color LED outlines, `.glass`
  panels, pill buttons. Do not import Apple/Spotify chrome, iconography, or branding.

---

## 1. Project Goal
Redesign and **repair** the embedded Music Library for BANDroom across three surfaces that must
now behave as one coherent system instead of three disconnected code paths:

1. **Marketplace** — the global browse experience ("The Bandroom" modal): Newest Uploads,
   Top Contributing Teams leaderboard, Find-a-Team grid.
2. **Per-Team Sound Bank / Trophy Room** — the modal opened from a team tile, showing that
   team's songs (Sound Bank tab) and background images (Trophy Room tab), with search and
   Download All.
3. **My Downloads** — the local index of everything the user has actually pulled down, which
   must reflect reality (files present in the `Songs/`/`Trophy Room` folders), not just a log
   of past download actions.

All three must read from **one consistent team-identity key** end to end — tile → click →
modal fetch → results. If a team tile is generated from one identifier (e.g. full name
"Georgia") and the per-team fetch queries by a different one (e.g. abbreviation "GA" or a
slugified variant), that mismatch is the class of bug that produces "opens fine, shows nothing."
Audit and fix this matching before any visual work, then re-verify visually.

---

## 2. Known Defects To Fix (not optional polish — ship blockers for this redesign)
1. **Team tile edit controls are unreliable.** The pencil (rename/re-crop logo) and
   building/trophy (open Trophy Room) icon buttons that overlay each tile on hover do not
   consistently do the right thing — confirm each button is wired to the *specific tile it's
   rendered on* (no shared/stale closure over the last-rendered team), not a sibling or the
   previously hovered tile.
2. **Per-team modal can show zero results for a team with real uploads** (reproduced with
   Georgia: modal opens, tabs render, "Download All" is present, but the list area is empty).
   Root-cause the team-key mismatch described in §1 between however tiles are keyed and however
   the fetch-uploads-for-team call is parameterized. Fix the mismatch, not just the empty state.
3. **"My Downloads" does not reliably reflect what's on disk.** Treat it as an index that can
   drift from the filesystem — decide whether it's rebuilt by scanning local folders on open, or
   updated transactionally on every successful download, and make it self-healing either way
   (a stale entry pointing at a deleted/missing file should not appear as available).
4. **Tile grid sizing bug precedent**: `.team-picker-grid` previously had a class/ID CSS
   selector mismatch that made tiles render as one unsized swatch (fixed once already in the
   matchup picker — see Session 8 handoff §1). Check every other grid in these three surfaces
   (marketplace Find-a-Team grid, Newest Uploads grid, per-team Sound Bank list) for the same
   class-vs-ID selector trap before assuming CSS is fine elsewhere.

Definition of done for this brief is not "looks right" — it's "looks right AND every team's
modal shows that team's real uploads AND My Downloads matches disk."

---

## 3. Visual Aesthetic: Glassmorphism & Modern Gaming HUD
- **Glass Materials (`backdrop-filter`):** Semi-transparent dark panels
  (`rgba(18, 18, 24, 0.75)` with `backdrop-filter: blur(16px)`), subtle `1px` inner borders
  (`rgba(255, 255, 255, 0.08)`), soft layered drop shadows for depth over the background.
- **Dynamic Accent Color Engine:** Accent highlights (active tabs, waveforms, glowing trigger
  badges, hover states, the "now playing" pulse) dynamically adapt to the selected team's
  primary/secondary palette (e.g., LSU Gold, Alabama Crimson, Florida Blue) — this is the same
  pulsing-LED language already standing in the Clip Preview island; reuse it, don't reinvent it.
- **Typography & Structure:** Modern, highly legible sans-serif (Inter, Geist, or SF Pro).
  Tight, scannable vertical spacing, clear hierarchy. Respect the outstanding font-readability
  complaint (10–11px text is too small throughout the app) — nothing new in this redesign should
  ship below a 13px floor for body text, 11px absolute floor for meta/caption text.

---

## 4. Layout Architecture (The Hybrid View)

### A. Persistent Top Glass Bar (Navigation & Global Controls)
- **Primary View Switcher** — glass pill tabs:
  `🎵 Sound Bank` · `🏆 Trophy Room` · `📥 My Downloads` · `🌐 Marketplace`
- **Live Search & Filter Hub:** real-time filtering by team abbreviation, situation/trigger
  name, or track title — search must query the *same normalized team key* used everywhere else
  (§1), so searching "Georgia," "UGA," or "GA" resolves to the same team consistently.
- **Density Control & View Toggles:** Compact Rows / Expanded Cards / Hybrid Table, plus a
  zoom/scale slider for tile and row size.

### B. Split-Pane Content Region
- **Left Sidebar (Category Nav & Team Switcher):** quick-jump anchors; team switcher dropdown
  shows team color swatch + logo thumbnail, keyed by the canonical team ID, never by display
  name string-matching.
- **Main Dynamic Panel:**
  - **Tile View (Marketplace / Sound Bank grid):** glass tiles with album/stadium art, title,
    team badge, likes/downloads count, inline play/preview with mini waveform, and the two
    hover-overlay icon buttons (edit, open-team) each scoped to their own tile's data.
  - **List/Slot View (My Downloads, per-team Sound Bank list):** row-based, each row shows
    filename, source team, file-exists status (so a broken/missing local file is visibly
    flagged, not silently absent), inline play, and a clear "remove from downloads" action that
    only ever touches that row's file.

---

## 5. Interaction, Performance & Motion Design
- **Silky Smooth Scrolling:** virtualized scrolling for hundreds of rows/cards at 60+ FPS.
- **Fluid Micro-Animations:** `scale(1.02)` + elevated glass glow on hover
  (`box-shadow: 0 8px 32px rgba(0,0,0,0.4)`); animated glowing EQ bars on the actively playing
  row/card; drag-and-drop from marketplace grid onto an event trigger card highlights the drop
  zone with a glowing border.
- **Instant Response, No False Empty States:** zero layout shift during preview/assign. An empty
  list must be distinguishable at a glance from "still loading" and from "the fetch actually
  failed" — no more silent zero-result modals; a real empty team gets an explicit "no uploads
  yet for this team" state, a fetch error gets an explicit retry state, and these look nothing
  alike.

---

## 6. Key Deliverables
1. **Desktop Main View (1920×1080 & 1440×900):** hybrid glass library populated with real team
   data (LSU/UF/Georgia), including a per-team modal that actually shows that team's uploads.
2. **Compact Mode Overlay View:** minimized floating glass HUD widget for live gameplay.
3. **Design System Tokens:** Glass Blur, Border Opacity, Team Accents, Surface Elevators,
   Waveform Canvas Colors — plus the 13px/11px type floors from §3.
4. **Bug-fix diff summary**: a short writeup (can live in the next session handoff doc) of what
   the team-key mismatch actually was and where it was fixed, so it doesn't regress the way the
   `.team-picker-grid` class/ID bug nearly did twice.
