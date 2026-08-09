# Bandroom Handoff — Session 16 (2026-08-09)

Picks up right after Session 15 (`docs/Bandroom_Handoff_2026-08-09_Session15.md`). Two releases
went out this session: v1.0.63 mid-session, and another right after this doc (confirm the exact
tag with `git tag --sort=-v:refname | head -1` on pickup — the second release ran right after this
was written).

## What shipped, in order

### 1. Left icon rail removed, actions moved into the toolbar (v1.0.63)
- Owner had asked repeatedly for the left rail (`#rail-left` — Teams/Save/Help icons) removed; it
  was visually blocking the team grid below it in `#left-panel`. **The team grid itself was
  explicitly NOT touched** — only the icon rail above/beside it.
- Teams / Save / Shortcuts (the rail's native-shortcuts "?" button, `bridge.OpenHelp`) now live as
  pills in `#header-bar`'s `.header-right`, next to the "Not watching" status pill.
- The separate **Help & Guide** pill (`#btn-help-pill`, opens `#help-guide-overlay` — the ~40-tip
  in-app guide, distinct from the native shortcuts button above) moved to the same toolbar row.
  It no longer needs the `position: sticky; bottom: 44px` hack that existed solely to keep it
  visible above the bottom ticker while pinned to the sidebar — that CSS was removed along with
  the rail's own dead `.rail`/`.rail-item` CSS block and the now-redundant `--rail-w` variable.
- `.rail-item` click wiring (`runRailAction(action)`) is now driven by a generic
  `document.querySelectorAll("[data-action]")` listener instead of the old rail-specific one —
  same `data-action` attributes, just on pill buttons instead of vertical rail buttons.

### 2. Auto-Assign: overwrite + confirm + missing-pack prompt (v1.0.63)
- The Events-page Auto-Assign button (`#btn-auto-assign`, matchup-side-bar) used to silently only
  fill *empty* slots. Owner wanted a real "replace what I've got" action instead.
- New flow (`handleAutoAssignClick` in app.js): checks `bridge.HasDefaultSongPack()` first — if
  missing, shows the same `#songpack-prompt-overlay` → `#songpack-import-overlay` flow every other
  "need the pack" entry point uses (Download via Drive link, or Locate & Import a zip/folder), then
  re-runs itself once `bandroom:songpackready` fires. If the pack IS present, shows a new confirm
  dialog (`#auto-assign-confirm-overlay`, Cancel/Overwrite) before calling the new
  `bridge.ApplyDefaultProfileForTeamOverwrite(team)` (→ `WebMainForm.
  ApplyDefaultProfileForTeamOverwriteFromWeb` → `ConfigStore.ImportDefaultPackForTeam(..., overwrite: true)`).
- **Audit-caught bug, fixed same session**: neither the new overwrite method nor the pre-existing
  fill-only `ApplyDefaultProfileForTeamFromWeb` ever refreshed `WebMainForm._config` (the in-memory
  snapshot behind both the visible category counts AND every autosave path) when the target team
  was the currently active one. Auto-Assigning your active team wrote correctly to disk, but the
  next autosave (e.g. tweaking one unrelated song afterward) would silently revert the whole thing
  back to the pre-Auto-Assign state, since the stale `_config` got saved back over it. New
  `RefreshActiveConfigIfNeeded(teamName, config)` helper fixes both methods — mirrors the same
  pattern `DuplicateProfileFromWeb`/`ImportProfileFromWeb` already used elsewhere in the file.
  Harmless for the old fill-only version (re-running was already a no-op either way) but would have
  been real silent data loss now that Auto-Assign is destructive by design. **If you're touching
  any other "write a profile to disk for possibly-the-active-team" bridge method in the future,
  check whether it needs this same `_config`/`PushCategories()` sync.**

### 3. Logo/background "doesn't save" — real root cause found and fixed (v1.0.63)
- `TeamLogo.FindImagePath`/`TeamBackdrop.FindImagePath` looked up the **raw, unsanitized** team
  name (`teamName + ext`), but `WebBridge.SaveCustomTeamLogo`/`SaveCustomTeamBackground` write
  under a **sanitized** filename (regex strips anything outside `[\w\s&-]` — apostrophes, periods,
  etc). Any team whose name contains a stripped character (e.g. an apostrophe) saved its custom
  logo/background successfully to disk, but could never find it again on the next lookup — silently
  falling back to no logo/the old one, with no error anywhere. This was the actual "logo editor
  didn't retain info after saving" bug, not a UI/crop-tool issue.
- Fix: both `FindImagePath`s now check the sanitized name first (matching what the save path
  actually wrote), then fall back to the raw name (so any manually-dropped file with punctuation in
  its filename still works too). Sanitization logic is duplicated inline rather than shared via a
  new public helper — kept the change minimal/localized rather than restructuring WebBridge.cs.

### 4. Team-album back navigation (post-v1.0.63, not yet released as of this doc)
- Owner report: team logos in the market/Sound Bank had no way back to team select except closing
  everything. Clicking the album header's team logo (`#bandroom-album-icon`) now calls
  `backFromTeamAlbum()`, which returns to wherever the album was opened from: the hub's team grid
  (`#bandroom-overlay`) if that's where it came from, or the full team-picker coverflow if it was
  opened via the Sound Bank button's direct-entry shortcut (which has no hub underneath it).
- Also added an explicit `←` back button next to the logo (same handler) for discoverability, and
  a `→` forward button on the hub header that appears once you've gone back, jumping straight back
  into the album you just left (`_lastAlbumTeam`, cleared on a fresh pick or full close).

### 5. FCS school colors "don't match theme" (post-v1.0.63)
- Root cause: several FCS schools' authentic secondary color is literal black (North Dakota,
  Illinois State, Youngstown State, Wofford, Stony Brook, Southeast Missouri State...). `--team-
  secondary` isn't just a decorative glow var — it drives nearly every accent/border/badge/text
  color in the theme (`color-mix(...var(--team-secondary)...)` throughout style.css). A black
  secondary made the ENTIRE accent system go invisible-on-dark for that team, which read as "colors
  don't match the theme."
- Fix: `applyTeamGlowVars` (app.js) now applies the same `isNearBlack()` fallback `--team-primary`
  already had, to `--team-secondary` too — falls back to primary (if that isn't also near-black),
  then to the app's default accent (`#22d3ee`) as a last resort. `applySchoolGlow` (marketplace
  tile school-name glow) got the same double-fallback for consistency.
- The actual RGB hex values in `TeamColors.cs`'s `FcsTeams` array were NOT wrong/changed — they're
  correct school colors. The fix is purely in how the theme falls back when a real color happens to
  be black, not in the roster data.

### 6. My Downloads redesign (post-v1.0.63)
- Owner supplied a reference screenshot (a Spotify/mooshic-style music library UI: sidebar filters,
  sortable table, search) and asked for that layout adapted to Bandroom's own logic/theme, plus "10
  suggestions for a My Downloads folder, implement them."
- Rebuilt from a plain unsorted 4-column tile grid into a searchable/filterable/sortable list:
  search box, 5 filter pills (All/Songs/Backgrounds/Your Uploads/Missing), 3-mode sort (Newest/Name
  A-Z/By School), optional "Group by school" toggle (collapsible-style headers), item count in the
  header, type badges per row, download date per row, and a `←` Back button to The Bandroom hub
  (same "easy way back" gap as item 4 above). Reused every existing action (preview-on-click,
  Share to Marketplace, Set as Background, Remove, the missing-file badge) rather than inventing
  new ones — this was a layout change, not a feature-logic rewrite.
- Container renamed `#my-downloads-grid` → `#my-downloads-list`, tile builder renamed
  `buildMyDownloadTile` → `buildMyDownloadRow`, grid function `renderMyDownloadsGrid` split into
  `loadMyDownloads()` (fetch) + `renderMyDownloadsList()` (filter/sort/render, callable standalone
  when the toolbar changes without refetching). Checked `ui-bot.js` and the Mac app for stale
  references to the old names — none found.

## Pre-release audit (this session)

Ran the `deep-audit` skill against everything above before release. Rebuilt both
`src/Bandroom.Core/Bandroom.Core.csproj` and `BandAudioHook.csproj` fresh (not trusting prior
"build succeeded" output) — 0 errors/warnings both times, before and after the fixes below.

**Found and fixed:**
- The `_config` staleness bug in Auto-Assign overwrite (item 2 above) — the one real behavioral bug
  found. Would have caused silent data loss (an Auto-Assign getting quietly undone by the next
  autosave) if shipped as-is.
- Dead CSS/JS leftovers from the rail removal: `.rail-item`/`--rail-w` tokens in a few combined
  selectors (`:focus-visible`, `:active`, cursor rules) that no longer match anything in the DOM —
  harmless (selectors just never match), cleaned up anyway. Stale `buildMyDownloadTile` mentions in
  three comments updated to the new `buildMyDownloadRow` name.

**Checked, no issue found:**
- `RefreshHomeAwayConfigIfNeeded` (live home/away game-state sync) already correctly handles the
  overwrite path the same as the fill-only path — no separate fix needed there.
- Escape-key and backdrop-click handlers for the team album route through `closeTeamAlbum()` (plain
  close), not `backFromTeamAlbum()` — the two close paths don't conflict; both are safe no-ops on
  an already-hidden overlay.
- `_albumOpenedFrom`/`_lastAlbumTeam` get freshly recomputed on every `openTeamAlbum()` call, so
  there's no staleness risk from skipping `backFromTeamAlbum()` via the X button or Escape.
- The FCS secondary-color fallback only touches the CSS theme vars (`--team-secondary`), not the
  raw `state.teams[].secondary` data `fillTeamSwatch` uses for gradient fills — team swatches still
  show each school's true (possibly black) secondary color; only the glow/accent system falls back.
- `ui-bot.js` and the Mac app (`src/Bandroom.Mac/`) have no references to any of the renamed/removed
  My Downloads or rail identifiers.

**Known gap, not fixed this session (out of scope, Mac isn't built/shipped currently):**
Mac's `MacWebBridge.cs` likely has the same sanitize-mismatch pattern item 3 fixed on Windows
(`MacWebBridge.cs:547` uses its own `safeTeam` sanitization for writes; unclear if its logo lookup
path matches). Not touched this session since the Mac app isn't in active use/release — worth a
look if/when Mac work resumes.

## Starting a fresh session on this

1. `git log --oneline -10` and `git tag --sort=-v:refname | head -3` — confirm both this session's
   releases landed (v1.0.63 mid-session, one more right after this doc).
2. Same pre-existing uncommitted Mac WIP files as every prior session — still untouched, not mine,
   leave alone unless asked.
3. **Never run `release.ps1`** without the owner saying "ppup" or explicitly asking — used correctly
   this session (twice, both explicitly requested).
4. Next priorities, per what's still open from Session 15's carryover (untouched again this
   session): avatar upload cropper (still just raw-compress-and-upload, no crop step), the embedded
   HD waveform trimmer (queued since Session 14, redirected again this session for owner's live
   UI-bug requests), Field Goal misfire / Assign PA button / Clip Preview scrolling / dead
   `AudioDuckingController.cs` from even further back.
5. The Mac `MacWebBridge.cs` logo/background lookup parity gap noted above, if Mac work resumes.
