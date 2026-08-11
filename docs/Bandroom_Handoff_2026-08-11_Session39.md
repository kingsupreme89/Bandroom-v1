# Bandroom Handoff — Session 39 (2026-08-11)

Picks up after Session 38 (scorebug preset parity). This was a long, mostly UI-driven session
working directly off live screenshots of the running app: a punch list from an Assignment-screen/
Sound Booth walkthrough, several follow-up bugs found while verifying those fixes, a Matchup-screen
redesign (vertical team scroller + Big Game toggle), and a Sound Bank browsing redesign. Touched
`wwwroot/app.js`, `wwwroot/index.html`, `wwwroot/style.css`, `AudioPlayer.cs`, `TriggerEntry.cs`,
`WebMainForm.cs`, `WebBridge.cs`, `ConfigStore.cs`, `IntakeEngine.cs`, `DefaultSongPackService.cs`,
`BandAudioHook.csproj` (new `TagLibSharp` package), deleted `ShortcutsForm.cs`. **Nothing from this
session is committed** (repo was already 2 commits ahead of `origin/master` from before this
session started).

## 1. Assignment screen / trimmer / Sound Booth punch list

- **Trimmer state leak**: `closeInlineTrimmer()` (`app.js`) now resets `_trimStartSec`/
  `_trimEndSec`/`_trimZoom`/`_trimDragHandle` on close, not just on the next open — a defensive
  fix for anything reading trim state between sessions (e.g. the new Tab-to-preview shortcut).
- **Volume sliders not linked**: they were already calling the right bridge setters — the actual
  bug was nothing ever read the saved value back on launch. Added `refreshVolumeSliders()`,
  called from `init()`, and `syncSoundBoothKnobDisplay()` so the sidebar sliders and Sound Booth's
  knobs (two separate widgets driving the same values) stay in sync with each other.
- **Sound Booth preview**: its Preview button now plays whichever song is highlighted on the
  Assignment screen (`bridge.PreviewLocalFile`, through the real effects chain) instead of a fixed
  "score" test cue; added a direct "🎚 Sound Booth" entry point button on the Assignment toolbar.
- **Tab-to-preview**: Tab now triggers ▶ on the Assignment screen (guarded against text inputs and
  the trimmer).
- **Song indexing**: `.ogg` was missing from `AudioExtensions` in `ConfigStore.cs`,
  `DefaultSongPackService.cs`, `WebMainForm.cs`, and the Mac client — added everywhere for
  consistency; `.ogg` files were silently never indexed before this.
- **Situations↔down/distance linkage and Test Hook's event list**: verified already correct
  (`ConfigStore.AllEngineEventKeys` already is the exact Situations-page list; `RetiredEventKeys`/
  `BlockedEventKeys` already exclude old down cards). No code change needed.
- **Help & Guide close button**: verified already exists and works (`#btn-close-help-guide`). No
  change needed.
- **Per-song lead-in whistle**: added `TriggerEntry.PlayLeadInWhistle` (bool, defaults `true`), a
  per-card toggle button on each Situations row, wired through `WebBridge`/`WebMainForm`
  (`SetEventPlayLeadInWhistle`), and gates the whistle layer in `AudioPlayer.Play()`'s trigger path.

## 2. Follow-up fixes found while verifying the above

- **Default pack team-folder case sensitivity** (`IntakeEngine.cs`): the team dictionary was
  `StringComparer.Ordinal` — a folder named `"uga"` missed the exact-match lookup entirely and fell
  through to slow per-file fuzzy classification. Now `OrdinalIgnoreCase`, and the exact-match branch
  returns the registry's own canonical-cased name instead of the input's casing (was previously
  returning whatever casing the folder happened to have, which could produce inconsistent team-name
  strings downstream).
- **Loudness normalization**: `LoudnessNormalizationService` (LUFS-based, writes a gained copy +
  JSON sidecar cache, never touches the original file) already existed and was already wired into
  the web assign flow (`AssignTrackFileFromWeb` → `NormalizeAssignmentInBackground`) — new songs
  were already being normalized. What was missing: anything already assigned before that wiring
  existed stayed at its original loudness forever. Added `NormalizeExistingLibraryOnce()`
  (`WebMainForm.cs`), a one-time background sweep over every saved team profile, guarded by
  `ConfigStore.LibraryNormalizedMarkerPath` so it only ever runs once per install.
- **Sound Bank pill on the Assignment screen**: added a pill-filter row (`#clipper-assign-filters`,
  reusing the same `.bandroom-filter-pill` component My Downloads/Band Room album already use) —
  defaults to that team's Default Song Pack ("Sound Bank"), with All Songs/Marketplace/Trimmed/Your
  Imports/Imported Files one click away instead of everything dumped in one long list.
- **Matchup/team-picker mouse-wheel scroll**: coverflow pickers are index-based (5-tile window, not
  a real scroll container) — added a throttled wheel handler (`wireCoverflowWheel`) that steps the
  index one team per notch, paced to the tiles' own 0.22s CSS transition.

## 3. Sound Booth theming + cleanup

- **Sound Booth had no team-color glow**: its inner knobs already used
  `var(--team-glow, var(--accent))`, but the modal's own outer chrome (border, title LED, active
  tab, active Reverb tile, preview button, info popover/close button, launcher pill) was hardcoded
  to the fixed generic `--accent` — so the frame never matched whichever team was active even
  though the knobs inside it did. All of those now follow the same fallback pattern.
- **Sound Booth info-tooltip mispositioned**: `#soundbooth-info-popover` is one shared element for
  every `(i)` button across every tab, but it only ever got `hidden = false` — never actually
  repositioned per button. Its `position: absolute` resolved against `#sound-booth-overlay` (the
  fixed, full-viewport backdrop, since `#sound-booth` itself never set `position`), so every info
  button popped the box in the same wrong spot. Fixed: `#sound-booth` is now the real positioning
  anchor, and `refreshSoundBoothInfoPopover` computes position from the actual clicked button,
  clamped inside the panel; also closes automatically on tab switch now.
- **Native "How to Use Bandroom" popup removed**: it was a plain, unthemed WinForms dialog
  (`ShortcutsForm.cs`, opened via `bridge.OpenHelp()`/`ShowHelp()`) duplicating what the in-app,
  properly-themed Help & Guide overlay already covers more thoroughly. Deleted `ShortcutsForm.cs`
  and the now-dead `OpenHelpFromWeb`/`OpenHelp`/`ShowHelp` bridge methods; both the `?` Help button
  and the command palette's Help entry now open the themed overlay (`#btn-help-pill`'s handler)
  instead.

## 4. Matchup screen redesign

- **Vertical team-scroller** (`.matchup-side-grid`): a fast, click-to-select single-column icon
  strip on the outer edge of each side (Away far-left, Home far-right), same look as the sidebar
  team grid, added *alongside* the existing coverflow (not replacing it) per owner's explicit
  choice. Confirmed live by the owner as the right feature — but the first pass had a dimension bug
  (icons cut off at the edges): the grid's own `1fr` track was 40px (44px container minus 2px×2
  padding) while `.team-swatch` was force-set to a literal 44px, overflowing its track by 4px on
  every tile. Fixed by removing the hardcoded width and letting it fall through to `.team-swatch`'s
  own `width: 100%` (height already derives from `aspect-ratio: 1`), plus locking the container to
  a hard `width/min-width/max-width: 44px` with `overflow-x: hidden`.
- **Big Game toggle pill added directly on the Matchup dialog** (`#toggle-matchup-big-game`) so it's
  flippable right before GAMETIME instead of only reachable from the Adjust sidebar. **Bug found via
  owner screenshot and fixed same session**: this pill (and the sidebar's own checkbox) only ever
  wrote the real gating flag (`ConfigStore.BigGameSettings`) — a *separate*, purely-cosmetic flag
  (`_bigGameBannerEnabled`, controls whether the Gameday-logo badge glows) could silently disagree
  with it, so unchecking Big Game left the logo still glowing. All three controls (sidebar checkbox,
  sidebar banner checkbox, matchup pill) now go through one function, `applyBigGameEnabled()`, that
  keeps all three UI states *and* the logo glow *and* the persisted flag in sync on every toggle —
  no more independently-driftable states.

## 5. Sound Bank per-team browsing redesign

Owner's complaint: the Default Song Pack ships ~68 teams, but assigning a song only ever showed the
active team's own pack, and the files are named after the EventKey slot they auto-filled ("Defense_
Third Down_5"), not anything describing the song. Owner then showed a screenshot proving the MP3s
carry real ID3 Title tags (e.g. "sec socar '21 def stops") that Explorer already displays but the
app never read.

- **Added `TagLibSharp`** (NuGet) and a `ReadAudioTitleTag` helper (`WebMainForm.cs`) — both
  `GetDefaultPackSongsForTeamFromWeb` and `GetConferencePackSongsForTeamFromWeb` now return a
  `title` field (the ID3 tag, or `null` on any read failure/missing tag — never throws). The
  Assignment-screen row label now prefers `title` over the filename-derived name everywhere pack
  songs appear, with the real filename still in the tooltip and still what search matches against.
- **New `ConfigStore.GetDefaultPackTeamsWithConference()`**: a live two-level folder scan
  (Conference → Team), deliberately NOT relying on the existing `GetDefaultPackTeams()`'s
  `index.json` read — that file is only written by the download flow, so a build shipping the pack
  *bundled* instead (which `DefaultSongsFolder` prefers when present) would silently report zero
  teams even with real per-team folders on disk. Exposed via a new `GetDefaultPackTeamsFromWeb`/
  `GetDefaultPackTeams` bridge method.
- **New "🔀 Browse Team..." button + popover** next to the Sound Bank pill row
  (`#btn-clipper-browse-other-team`/`#clipper-browse-team-popover` in `index.html`,
  `wireBrowseOtherTeamSoundBank` in `app.js`) — searchable list of all ~68 pack teams. Picking one
  fetches that team's songs via the *same* `GetDefaultPackSongsForTeam` call the default pill uses
  and merges them into the currently-rendered Assign list as their own section ("Default Song Pack
  -- {Team} (N)", with a × to clear it), reusing `buildClipperAssignRow` unchanged — so Preview/
  Play/Stop/Assign Selected/Trim all keep working exactly as before, no special-casing needed in the
  assign/trim code paths. Only one browsed team stacks at a time. Explicitly additive, not a
  replacement for the Sound Bank pill's default (owner confirmed this scope via AskUserQuestion
  mid-session).

## Verified this session
- `dotnet build BandAudioHook.csproj` clean (0 warnings/errors) after every change, including after
  adding the `TagLibSharp` package (confirms it actually restores/compiles, not just references).
- Vertical team-scroller confirmed by the owner as the right feature via a live screenshot (the
  dimension bug in that same screenshot is what's fixed in item 4 above).
- Big Game/Gameday-logo desync confirmed by the owner via a live screenshot, root-caused, and fixed
  same session.

## Not yet confirmed — real next steps
1. **Nothing in this session has a post-fix confirmation screenshot from the owner yet** except the
   two items explicitly called out above (scroller concept, Big Game bug) — everything else (Sound
   Booth theming/tooltip fix, ShortcutsForm removal, side-grid dimension fix, the entire Sound Bank
   browsing redesign, ID3 title tags, normalization sweep) is verified by clean build + source
   reading only, not by eyeballing the running app. Given this session's own pattern (a "fixed" CSS
   glow claim was still stale-cache-suspect until re-screenshotted), treat all of it as
   needs-a-look, especially the new Browse Team popover (its layout/positioning has never been seen
   rendered).
2. **WebView2 cache gotcha remains real and was hit repeatedly this session** — wwwroot edits
   silently don't show up after rebuild+relaunch unless `bin\...\WebView2Data` is cleared first
   (own project memory note, re-confirmed multiple times this session).
3. Carried forward, still untouched: `voice_poc/.env` uncommitted/ungitignored (likely holds a
   secret, flagged since Session 36); not released (commits sit ahead of `origin/master` — now
   quite a few more after this session, still no version bump/tag/Squirrel pack); 3 deleted
   `guide/` files (Session 32) still unexplained.
4. The Mac client (`src/Bandroom.Mac`) was NOT touched for any of this session's work (Sound Bank
   browsing, Big Game sync, whistle-per-song, ID3 tags are all Windows/WebView2-side only) — flagged
   but out of scope unless a Mac build ships alongside these.
