# Bandroom Handoff — Session 14 (2026-08-09)

Picks up right after Session 13 (`docs/Bandroom_Handoff_2026-08-09_Session13.md`).
**Released `v1.0.56` through `v1.0.60` this session, all live on GitHub.** Master HEAD at end
of session: `adcd94b`.

## What shipped, in order

### v1.0.56 — Song pack importer: fix dead Ctrl+K entry, add folder import, surface it in the UI
- Root cause of "nothing happens" on Ctrl+K → "Locate & Import Song Pack": `#songpack-import-overlay`
  was missing from the shared overlay CSS rule (`wwwroot/style.css`) — no `position:fixed`/
  backdrop/centering, so unhiding it did nothing visible. Fixed.
- Added a **folder** import path (`DefaultSongPackService.ImportExistingFolderAsync`) alongside
  the existing zip path, for users who already extracted the pack.
- Added visible **"Import Song Pack"** buttons in the Sound Bank album toolbar and the
  Events/Assign panel — previously Ctrl+K was the *only* way to reach this at all.

### v1.0.57 — Folder import was actually broken, plus a second invisible-overlay bug
- The v1.0.56 folder importer **wiped the entire existing pack folder on every import** and only
  understood the full `Conference\Team\*.mp3` layout — pointing it at one team's folder emptied
  everything else imported before and produced nothing usable. Switched to merge (not replace)
  and added folder-shape auto-detection.
- Found and fixed `#default-profile-prompt-overlay` (the "No songs assigned yet — use starter
  profile?" dialog shown on **GAMETIME**) — same missing-CSS bug class as the songpack overlay,
  in a core gameplay flow this time, not just the importer.

### v1.0.58 — The real fix: conference folders are flat, filenames carry the team+event
- Owner's actual pack shape: a folder named e.g. `"SEC"` with **268 files sitting directly
  inside it**, no per-team subfolders at all — team + event are baked into each filename
  (`"sec ala '21 1st downs.mp3"`), not folder structure. Rewrote the importer to classify any
  folder whose own name doesn't resolve to a real team **per-file**, via `IntakeEngine.Process()`
  (the same filename-parsing engine already used for local song imports), instead of bulk-copying
  the whole thing as one bogus team. Also fixed the scan to recurse fully instead of stopping at
  the first audio match.
- Import confirmation now reports exactly what happened: team(s), song count, unmatched count.
- Cross-checked a second audit report (from a parallel agent session) against source before
  applying anything — one of its "fixes" would have reintroduced Session 12's Race #3 bug
  (misrouting Offense:/Defense: cues to the wrong team); applied a narrower, correct version
  instead (only side-agnostic `"Other:*"` events bypass the null-possession guard). Also fixed a
  real "Victory in Hand" duplicate-firing bug (no edge guard, could fire ~120x in 30 seconds).
  Two other claims in that report were verified already-fixed/false and left alone.

### v1.0.59 — Imports had nowhere to actually show up
- The Sound Bank album grid only ever showed marketplace/community uploads — default-pack songs
  are local-only and go straight into event slots, so a successful import looked identical to
  nothing happening. Added a distinct "Default Song Pack" section to that album view (with inline
  preview) so an import is actually visible where the owner naturally checked for it.

### v1.0.60 — Verified against a real pack, found and fixed real matching bugs
- Owner had me actually import a real 174-file Big Ten pack (`D:\B1G`, flat files, team+event in
  filename) instead of trusting the code. This surfaced three genuine bugs in `IntakeEngine`:
  1. `team_registry.json`'s `alias_index` (the real lookup table) was out of sync with each
     team's own `abbreviations` list — declared abbreviations that were never copied into
     `alias_index` could never match. Now backfilled automatically at load time.
  2. Abbreviations under 3 characters were blanket-excluded (to avoid false substring hits),
     which silently broke real 2-letter abbreviations like Northwestern's "NW". Replaced with a
     letter-adjacency check that's safe at any length instead of an arbitrary cutoff.
  3. Short abbreviations claimed by multiple teams ("UM" = Michigan or Miami, "OSU" = Ohio
     State/Oklahoma State/Oregon State) always silently resolved to whichever was listed first —
     actively wrong, not just incomplete. `alias_index` is now alias → list of candidates, and
     `ResolveTeam` takes a conference hint parsed from the filename (pack files are conventionally
     prefixed `"b1g"/"sec"/"acc"/"big12"/"pac12"`) to disambiguate. **This is fully general, not
     Big-Ten-specific** — confirmed with the owner mid-session.
  - Added Indiana, Michigan, Ohio State, Minnesota to the registry — all four were completely
    absent (0% match rate for their real files before this).
  - Found a second, more serious bug the same test caught: 110 of 174 files (63%) didn't
    confidently match a specific situation and were falling back to `GENERAL_HYPE`, which itself
    maps to "Start of Quarter"/"Drive Starter" event keys — meaning most of a real pack folder
    would have auto-filled essentially random, wrong slots. Fixed: low-confidence files are now
    filed under the resolved team by name (browsable/manually assignable) instead of auto-assigned
    anywhere.
  - **Result after the fix: 174/174 files matched to the correct team** (up from ~65/174), 64
    auto-filled into real situation slots, 110 filed for manual assignment instead of landing
    somewhere wrong.
  - Verification method: a standalone scratch console harness (not part of the shipped app) that
    calls `IntakeEngine.Process` directly against the real folder — kept in the session scratchpad,
    not committed, since it's a one-off verification tool, not a permanent test suite.

## Known limitation, not fully solved this session

`scripts/team_registry.json` still only covers 65 of the full 187-team roster (`TeamColors.cs`).
The fixes above make matching **self-correcting for any team that already has an entry** (typos/
missing aliases no longer silently fail), but a team with **zero entry** still can't resolve at
all. 121 teams remain unlisted. Options for next session, roughly in order of effort:
- Keep patching teams reactively as real packs surface gaps (what happened this session) — low
  risk, but slow and only as complete as the packs someone happens to test.
- Generate registry entries for the remaining 121 teams algorithmically (initials for
  multi-word names, first-N-letters for single-word names) as a lower-confidence fallback tier,
  distinct from the hand-curated high-confidence entries — bigger lift, needs care around
  collisions (same ambiguity problem "UM"/"OSU" already have, just at 121-team scale).
- Ask the owner for real per-team abbreviation data rather than guessing at scale.

## Known, not yet fixed (carried over from Session 13, still true)

1. Field Goal misfires as "Earned First Down" — `PlayDelta.cs:20`, needs a `!newPossession` guard.
2. Assign PA button + its own volume pill — `app.js:2854` `openClipperAssign`.
3. Clip Preview showing but rest of dashboard not rendering/scrolling — not yet root-caused.
4. Settings menu is still a separate native WinForms window, not merged into the profile popup.
5. `AudioDuckingController.cs` still fully dead code — deliberately left alone, needs a live-game
   ducking-behavior decision before wiring in, not a blind activation. Reconfirmed this session.

## Next up: embedded HD waveform trimmer (paused mid-investigation for the pack-import work)

Owner wants the native WinForms `TrimmerForm` popup replaced with an embedded panel in the web
UI, living in the Events/Assign screen where "Trim..." is pressed today (`wwwroot/index.html`
`#clipper-assign`, `btn-clipper-assign-trim` currently calls `bridge.OpenTrimmer` → native
`TrimmerForm`). Confirmed requirements:

- Move the trim UI into the WebView (HTML/JS), matching the app's dark-glassmorphism styling.
- Keep scrubbing **snappy** — reuse the existing preview-bar pattern (`wwwroot/app.js`
  `previewSong`/`loadPreviewWaveform`/`renderWaveformScrubber`, `<audio>` + Web Audio
  `decodeAudioData`, no round-tripping through C# for playback).
- Preserve the **last-4-seconds-of-end-point preview** behavior on releasing the end handle
  (`TrimmerForm.cs`'s `PreviewEndTail`/`EndTailPreviewSeconds`).
- The web view has **no URL for arbitrary local file paths** today (no virtual-host mapping for
  `SongsFolder`/`SongsTrimmedFolder`/`DownloadedDefaultSongsFolder`, and "Browse for file..." can
  point at literally any path on disk). Planned approach: a small `trimsrc` virtual-host mapping
  to a dedicated temp folder, with a bridge method that copies whatever file is being trimmed
  into it before the panel opens.
- Save flow needs new bridge methods reusing `TrimmerForm`'s existing normalize/limit DSP
  (`NormalizeAndLimit`, RMS gain + soft limiter) — extract into a shared non-UI class so both the
  old dialog (still used by two other call sites — `OpenAssignTrack`'s legacy flow and the local
  song-import pipeline) and the new embedded panel share it without duplicating the DSP code.
- Scope deliberately limited to just the "Trim..." button's flow (Events screen); the other two
  `TrimmerForm` call sites stay native for now — smaller blast radius.

Nothing has been written for this yet beyond the investigation above.

## Starting a fresh session on this

1. `git log --oneline -10` and `git status` — confirm master HEAD is at or ahead of `adcd94b` /
   tag `v1.0.60`.
2. Same pre-existing uncommitted Mac WIP files as prior sessions (`AudioPlayer.Mac.cs`,
   `Bandroom.Mac.csproj`, `GameWatcher.Mac.cs`, `MacWebBridge.cs`, `PlatformStubs.Mac.cs`) are
   still sitting uncommitted — not touched this session, not mine, leave alone unless asked.
3. **Never run `release.ps1`** without the owner saying "ppup" or explicitly asking for a
   release — standing rule, unchanged, used correctly 5x this session.
4. Pick up the embedded waveform trimmer per the section above.
5. If picking up the registry-coverage gap: `scripts/team_registry.json` has 65/187 teams; see
   "Known limitation" above for the tradeoffs on how to close it.
