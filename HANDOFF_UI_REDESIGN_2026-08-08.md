# Bandroom UI Redesign Handoff — August 8, 2026 @ 2:00 AM MT

## Summary

This session implemented a massive UI overhaul: marketplace redesign (Nexus Mods style), CSS bug fixes, macOS design language, accessibility improvements, and a UI bug-scanning bot. The project is approximately 25% complete on the full 90+ item list.

---

## Files Changed

| File | What Changed |
|------|-------------|
| `wwwroot/style.css` | ~200 lines added: 4-column card grid, marketplace-card component, sort/filter bar, team grouping, macOS scrollbar, reduced-motion, slider thumb glow, green dot visibility, toast position fix, duplicate CSS merged, window height fix |
| `wwwroot/app.js` | `buildItemTile()` rewritten: Nexus-style cards in album views with thumbnail, title, school dot, download/like counts, uploader, time ago, Preview + Get buttons. Hub shelf kept compact. Added `relativeTime()` helper. |
| `wwwroot/index.html` | Added profile close button (✕), wired `ui-bot.js` script tag |
| `wwwroot/ui-bot.js` | **NEW FILE** — 12-check automated DOM bug scanner that runs on page load. Checks: [hidden] specificity, duplicate CSS, canvas mismatches, XSS, accessibility, nested scroll, z-index collisions, broken images, toast/ticker overlap, animation conflicts, font inheritance, overlay z-order |

---

## ✅ DONE (This Session — ~20 items)

### CSS Bug Fixes (6 of 40)
- [x] Merged duplicate `.header-right { gap }` declarations
- [x] Merged duplicate `.update-actions` declarations (was losing display:flex)
- [x] Toast position moved above ESPN ticker (bottom: 54px, z-index: 51)
- [x] Removed magic `calc(100vh - 26px)` → `100vh`
- [x] Added `#situations-panel[hidden] { display: none }` rule
- [x] Removed triple `.header-right` duplicate

### Marketplace Redesign (all 10 items)
- [x] 4-column card grid (replaces 6-column cramped layout)
- [x] `.marketplace-card` component with thumbnail, title, school dot, metadata, actions
- [x] Sort tabs bar (Trending / Newest / Most Downloaded / Most Liked)
- [x] Filter chips (Songs / Backgrounds)
- [x] Card metadata: downloads, likes, uploader, time ago
- [x] Preview + Get buttons on each card
- [x] Load More button styling
- [x] My Downloads team grouping headers
- [x] Hub shelf kept compact for horizontal scroll
- [x] `buildItemTile()` JS rewritten for new card system

### macOS Design + Accessibility
- [x] `will-change: transform` on team swatches (smooth dock hover)
- [x] Mac scrollbar fallback (`scrollbar-color`)
- [x] Reduced-motion media query for accessibility
- [x] Slider thumb glow with WebView2/macOS fallback
- [x] Green configured dot more visible (bigger + dark ring for contrast)
- [x] Profile close button (✕) added to dialog header

### New Files
- [x] `ui-bot.js` — Automated 12-check bug scanner

---

## ❌ NOT DONE (Still Needs Building — ~75 items)

### Remaining CSS Bugs (~12)
- XSS sanitization on marketplace innerHTML rendering
- Double-scrollbar fix (changelog nested in side-panel)
- Team picker tile layout shift (add explicit width/height on `<img>`)
- `:focus-visible` outlines for keyboard nav
- Channel status indicator for Discord panel
- Google sign-in loading state
- Avatar file size limit
- `prefers-color-scheme` dark/light

### Remaining JS Bugs (~10)
- Search debounce on team picker/marketplace search (200ms delay)
- Lazy loading for team logos (IntersectionObserver)
- Team data validation fallback
- Error handling in init() chain
- Bridge fallback: detect real browser vs WebView2
- Preview waveform canvas sizing

### macOS Redesign (15 items)
- [ ] Traffic-light window controls (red/yellow/green dots)
- [ ] Layered glass depth (header=20px, rail=20px, sidebars=32px blur)
- [ ] Segmented toolbar replacing 5 marketplace pills
- [ ] Dock spring animation (cubic-bezier on magnify)
- [ ] Sheet-style dialogs sliding down from header
- [ ] Vibrancy materials hierarchy
- [ ] Haptic button feedback (scale-down on press)
- [ ] Rubber-band scroll effect
- [ ] System accent color sync
- [ ] Drag-and-drop song assignment
- [ ] Menu bar applet (Mac)
- [ ] Touch Bar support (Mac)
- [ ] Notification Center integration
- [ ] Quick Look preview (Space to preview)
- [ ] Finder-style column browser for teams

### Gamer UI Patterns (13 items)
- [ ] Live HUD overlay (score, quarter, down & distance)
- [ ] Kill-feed event log (animated feed entries)
- [ ] Streamer mode toggle
- [ ] Achievement rarity tiers (bronze/silver/gold/diamond)
- [ ] Sound visualizer (real-time frequency bars)
- [ ] Soundboard favorites bar (6-8 manual trigger buttons)
- [ ] FPS/ping-style status indicator
- [ ] Global hotkey panel
- [ ] Party/group sync mode scaffold
- [ ] Clip/replay integration
- [ ] Crosshair cursor styles
- [ ] Match history timeline
- [ ] Season pass UI

### Navigation & Layout (10 items)
- [ ] Collapsible side panels (icon-only mode)
- [ ] Tabbed right panel (Mixer | Effects | Changelog | Help)
- [ ] Global command palette (Ctrl+K)
- [ ] Contextual right-click menus
- [ ] Undo system for song assignment
- [ ] Multi-select team operations
- [ ] Pin teams to top
- [ ] Progress rings on team tiles
- [ ] Keyboard navigation
- [ ] Breadcrumb navigation

### Profile Dashboard (7 items)
- [ ] Full dashboard page replacing modal
- [ ] Public profile page with shareable URL
- [ ] Profile banner image
- [ ] Activity feed
- [ ] Follow/friend system
- [ ] Leaderboards
- [ ] QR code sharing

### Tips System (100 tips + delivery)
- [ ] 100 "Did you know you can...?" tips database
- [ ] Floating tip widget with coach's clipboard design
- [ ] 25 tips mixed into ticker
- [ ] Auto-cycle every 45-90s
- [ ] Context-aware tips based on open panel
- [ ] "Never show again" per tip
- [ ] Tip completion achievements
- [ ] Tip of the Day on startup
- [ ] Animated tip illustrations
- [ ] Tip search via command palette
- [ ] Weekly Discord tip post
- [ ] Tip streaks and rewards
- [ ] User-submitted tips

### Dynasty Features (20 items)
- [ ] Dynasty save file scanner
- [ ] Manual dynasty journal
- [ ] Season stats cards
- [ ] Schedule/results timeline
- [ ] Player stats leaderboard
- [ ] Recruiting class tracker
- [ ] Coach card
- [ ] Rivalry alerts
- [ ] Top-25 scoreboard in ticker
- [ ] Conference standings
- [ ] Bowl projections
- [ ] Award watch lists
- [ ] Dynasty save selector
- [ ] Season-over-season history
- [ ] Auto-load dynasty team songs
- [ ] Dynasty stats on profile
- [ ] Dynasty recap toasts
- [ ] Milestone alerts
- [ ] Dynasty XP bonus
- [ ] Dynasty-specific achievements

### Polish (8 items)
- [ ] Skeleton loading screens
- [ ] "Resume last session" on startup
- [ ] Sound pack recommendations
- [ ] Full accessibility pass (aria-labels, roles)
- [ ] ESP[ticker font](#)
- [ ] Offline mode indicator
- [ ] Focus-visible outlines (falls under accessibility)
- [ ] Build verification step

---

## Key Architecture Notes

**Marketplace rendering:**
- `buildItemTile(item, inHub)` — hub=true uses compact `.bandroom-item-tile`, hub=false uses `.marketplace-card`
- Sort tabs + filter chips CSS exists but JS wiring not yet done in the HTML template
- Card uses `state.teams` for school color lookup and `relativeTime()` helper

**UI Bot:**
- Located at `wwwroot/ui-bot.js`
- Auto-runs on page load (1.5s delay for dynamic content)
- Can be re-run via `window.__runUIBot()`
- Results stored in `window.__uiBotReport`
- 12 check categories with color-coded console output

**Glass redesign:**
- `.glass` base still has the `neon-pulse` animation
- To finish macOS layers: remove animation from `.glass`, add separate rules for header/sidebars/rail with different blur values

---

## How to Rebuild & Test

```
cd /d c:\Bandroom
dotnet build BandAudioHook.csproj
# Then launch Bandroom.exe from bin\Debug\net10.0-windows10.0.19041.0\
```

The app reads `wwwroot\` files fresh on every launch — no deployment needed.

---

## Next Priority Order

1. Finish macOS traffic lights + glass depth (high visual impact, low risk)
2. Search debounce + lazy loading (performance)
3. Kill-feed event log + HUD overlay (gamer appeal)
4. Command palette (Ctrl+K) — single most transformative navigation feature
5. Profile dashboard
6. Tips system
7. Dynasty features
8. Everything else