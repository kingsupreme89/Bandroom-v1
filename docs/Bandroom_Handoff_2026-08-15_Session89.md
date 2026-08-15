# Bandroom Handoff — August 15, 2026 — Session 89

Same idea as always: what happened, explained plain.

## Fixed: Music Playing In Menus (Orphaned Song Preview)

Owner report: some users were hearing music playing that didn't seem tied to anything -- sounded
like it was coming from menus.

Root cause: `wwwroot/app.js` has one shared song-preview player (`_previewAudio`, a plain
`<audio>` element) behind every "click a song to preview it" surface -- The Market, Sound
Bank/team albums, My Downloads, Auto-Assign. Most of the overlay-close functions already pause it
on the way out (`closeMyDownloads`, `closeTeamAlbum`, `backFromTeamAlbum`), but two didn't:

- **`closeBandroomMarketplace()`** -- the actual "X" that closes the main Market hub. Preview a
  song there, close the hub, and it just kept playing indefinitely with no visible player left
  anywhere -- exactly what "random music in the menus" would sound like.
- **`closeClipperAssign()`** -- closing the "Assign Track"/"Add to Team Pot" picker. It stopped
  the native local-file preview pathway (`bridge.StopPreview()`) but not the JS `<audio>` pathway,
  so a marketplace preview left running survived closing that panel too.

Fixed: both now call `_previewAudio?.pause()` on close, matching every other overlay-close
function that already did this.

## Shipped

`ppup` -- committed, pushed, tagged, built, packaged with Squirrel, and published as **v1.1.13**:
https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.13

## Verification

- `node --check wwwroot/app.js` -- clean syntax.
- `dotnet build BandAudioHook.csproj -c Debug` -- 0 warnings, 0 errors.
- NOT independently live-tested: didn't click through an actual preview-then-close-hub sequence
  in the running app to hear the fix land. Straightforward one-line addition in two spots,
  consistent with the working pattern already used everywhere else -- low risk, but flagging per
  the usual "log/build only" caveat.

## Options Discussed, Not Started

- Nothing new this session -- see Session 88's carryover list (Mac's HBCU Team Pot bridge
  support, CollegeFB27.exe live-game report, `ChevronMarkerFx*` recalibration, Coffee scorebug
  overlay work / RAM reader address-locking unreliability, RAM/OCR watchdog score-mismatch log
  suppression, live-fire verification of Session 88's HBCU Mode timing changes, Session 87's
  duplicate-helpers cleanup and title-based team search fallback).
