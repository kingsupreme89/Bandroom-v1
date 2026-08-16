# Bandroom Handoff — August 16, 2026 — Session 91

Same idea as always: what happened, explained plain.

## Fixed: Song-Pack Import Leaving Stray Files / Not Indexing

Owner report: re-imported the same song pack zip/folder ~3 times, ended up unsure whether it
"created all kinda files on the computer" and whether songs were actually indexed/loaded.

Root causes (`DefaultSongPackService.cs`):
- `ExtractExistingZipAsync` only deleted its `_songpack_import_tmp` scratch folder on the
  success/no-match paths. A failed or cancelled extraction (bad zip, disk full, locked file) left
  a full partial copy of every extracted file sitting in `UserDataRoot` until the next import
  happened to clear it first.
- That same zip path never wrote `index.json`, so a zip-only import could leave real files on
  disk that the Assign panel's auto-fill (which reads `index.json` via
  `ConfigStore.GetDefaultPackTeams`) had no idea existed.
- The folder-import path's collision handling treated any filename collision as "a different
  alternate clip" and numbered it (`Song_2.mp3`, `Song_3.mp3`...) with no check for "this is
  literally the same file already imported" — re-running the same import repeatedly piled up
  numbered duplicates instead of being a no-op.

Fixed: `ExtractExistingZipAsync` now cleans up its temp folder in a `finally` block regardless of
outcome, and writes/merges `index.json` after moving files in (new `WriteIndexForRoot` helper,
scans the same way folder-import does but without copying). The folder-import path's `CopyFile`
now compares file content (size, then byte-for-byte) before numbering a collision — a genuine
re-import of the same audio is now recognized and skipped instead of duplicated.

Also found the same content-blind collision bug in the *single-song* import path
(`ConfigStore.ImportIntoSongsLibrary`/`PathsPointToSameFile`) via a follow-up audit — fixed the
same way (new `FilesAreIdentical` helper).

## Fixed: Team Abbreviations Silently Mis-Filing Songs to the Wrong School

Owner report (screenshot): marketplace listings tagged with the wrong school — "Jackson State"
song showing as "Florida AM", "FAMU" song showing as "Norfolk State".

Root cause: `IntakeEngine.ResolveTeam`'s alias lookup silently picked `candidates[0]` (whichever
team happens to be declared first in `team_registry.json`) whenever an abbreviation is ambiguous,
even when the conference hint couldn't actually break the tie — e.g. `UT` (Tennessee or Texas,
both SEC), `UW` (Washington or Wisconsin, both Big Ten), `NU` (Nebraska or Northwestern, both Big
Ten). Looked like a confident match, was actually a coin flip.

Fixed: `ResolveTeam` now only returns a confident `"abbreviation"` match when the candidate pool
(after conference-hint narrowing) collapses to exactly one team; otherwise it returns
`"ambiguous"` (new match type, flows into the existing `team_match_low_confidence` flag) so the
caller skips auto-fill and leaves it for manual assignment instead of guessing wrong.

The two live-mistagged marketplace items (Jackson State, FAMU) were corrected directly via the
worker's admin PATCH endpoint. The FAMU fix also surfaced a second bug (next item).

## Fixed: Marketplace School Names Corrupted by `&`, Case, and Whitespace

Found while fixing the FAMU mistag: PATCHing the school to "Florida A&M" silently came back as
"Florida AM" — `worker.js`'s `sanitizeSegment()` stripped `&` from every school name, corrupting
Florida A&M, Alabama A&M, Texas A&M, North Carolina A&T, etc. on every upload/edit. A 3-agent
audit of the worker surfaced two siblings of the same bug: `/leaderboard` grouped counts by the
raw, un-normalized school string (case-sensitive) while `/list`'s filter compared
case-insensitively, so the same school could fragment into separate leaderboard rows whose counts
never matched what `/list` actually returned; and `sanitizeSegment` never collapsed repeated
internal whitespace, so "Ohio State" vs "Ohio  State" were silently treated as different schools
too.

Fixed (`cloudflare/cloudflare-marketplace/worker.js`): `sanitizeSegment` now allows `&` and
collapses whitespace. `/leaderboard` groups by a normalized (trimmed+lowercased) key while still
displaying a real casing. Also hardened `addToIndex`'s existing-index write path (previously
unguarded — a failed KV put there left an item's metadata stored but permanently missing from
`/list`/`/leaderboard`, not "self-healing later" as the old comment claimed) with a retry + log.

Audit also confirmed (separately) that the admin token setup has no live secret-exposure issue —
`admin_token.local.txt` is gitignored, untracked, and points at an absolute out-of-repo path, so
it never ships in a build.

## Fixed: "Load Profile from Others" Failing to Apply Anything

Owner report (screenshot): tried to load a shared "LSU profile 36 songs" — got "None of the 36
songs matched anything in your Songs library," 0 applied.

Root cause: by design, a shared profile only ever carried trigger→filename pairs, never the audio
itself — applying one only worked if the applier happened to already have an identically-named
file. There was no way to actually fetch the referenced songs.

Fixed: `MarketplaceDownloadEntry` gained a `Url` field (the original `/file/<key>` link), recorded
at download time. `ShareCurrentProfileToMarketplace` now embeds that URL/name/school/type
alongside the filename for any assigned song that's itself a marketplace download on the sharer's
machine. `ApplyMarketplaceProfile` now falls back to downloading that URL directly when no local
filename match is found, instead of reporting the event unmatched. Mirrored on the Mac side
(`MacWebBridge.cs`) to avoid the drift a follow-up audit flagged as a recurring pattern in this
codebase. Purely local/trimmed songs with no marketplace copy still can't be summoned — that
limit is real, not a bug.

**Caveat for the owner:** the specific "LSU profile 36 songs" listing already on the marketplace
was uploaded under the *old* format (no source URLs baked in) — it won't benefit from this fix
until whoever shared it re-shares it from an updated client. Only profiles shared *after* v1.1.17
carry the new data.

## Fixed: Release-Build Startup Exception

Found while chasing an unrelated "why won't the version update" report: `crash.log` on the
(as it turned out, mis-packaged) v1.1.15 install showed `KillOrphanedWebView2Processes` throwing
`FileNotFoundException` on `System.Management` every single launch — that package is
Debug-only (`BandAudioHook.csproj` line 40) and the call site is correctly `#if DEBUG`-gated, so
a true Release build should never hit this. The v1.1.15 install had also been silently
mis-stamped as `AssemblyVersion 1.0.0.0` (confirmed via reflection) despite its Squirrel folder
being correctly named `app-1.1.15` — strong evidence that specific release was accidentally
packaged from a Debug build rather than Release at some point before this session. Verified
today's actual Release build stamps correctly (`1.1.16.0`, then `1.1.17.0`); no code change was
needed here, just confirmation the current release pipeline is sound.

## Also This Session

- Found and relabeled a Desktop shortcut ("Bandroom (test build)" → "Bandroom - DEV BUILD (do not
  use for real games)") that was pointing at `c:\Bandroom\bin\Debug\...` — this is very likely
  what the owner was actually launching when reporting version/behavior confusion.
- Built a "Bandroom Media Pack" artifact (TikTok cover / YouTube thumbnail / reel border overlay
  templates in the app's dark-glass/pulsing-LED style) plus matching GPT image-gen prompts and a
  new SVG wordmark/badge logo, at the owner's request — pure asset work, no app code touched.
- Extracted `D:\NCAA Sounds.zip` (1,057 files, ACC/B1G/BIG12/ND/PAC12/SEC + a patch folder) to
  `D:\NCAA Sounds Extracted` for the owner to run through "Import from Folder" — **result not yet
  confirmed**, see Open Items.

## Shipped

Two `ppup` releases this session:
- **v1.1.16** — song-pack import fixes, marketplace worker fixes, ambiguous-abbreviation fix.
- **v1.1.17** — profile-sharing auto-download fix, Release-build startup exception note.

## Verification

- `dotnet build BandAudioHook.csproj -c Debug` — 0 warnings, 0 errors, after every C# change.
- `node --check cloudflare/cloudflare-marketplace/worker.js` — clean syntax after worker edits.
- Confirmed via reflection that the actual shipped v1.1.16/v1.1.17 nupkgs stamp `AssemblyVersion`
  correctly (`1.1.16.0`, `1.1.17.0`), ruling that out as an ongoing risk.
- Mac-side mirror of the profile auto-download fix (`MacWebBridge.cs`) was **not** build-verified
  this session (user interrupted the Mac build call) — code is a direct structural mirror of the
  Windows fix, reviewed but not compiled. Flag for a follow-up Mac build check.
- Live-verified on the owner's own machine: confirmed a stale `%LOCALAPPDATA%\Bandroom\app-1.1.15`
  install was actually running (not the Desktop dev-build shortcut, as first suspected), confirmed
  it auto-updated to `app-1.1.16` after the in-app Update button was clicked, confirmed empty
  `Songs`/`DefaultSongs` folders before the NCAA Sounds extraction.

## Open Items For Next Session

- **Did `D:\NCAA Sounds Extracted` import cleanly?** Not yet confirmed — the owner's UI showed
  "LSU All 5/47" in a screenshot taken around the same time, but that may be unrelated (could be
  from the marketplace profile partially applying via old-format filename matches, not the folder
  import). Check in on this first.
- The still-open question of *why* the owner's first 3 import attempts left zero files on disk is
  most likely explained by those attempts running against the mis-packaged v1.1.15 Debug-as-Release
  build (see the startup-exception item above) rather than a code bug in the import path itself —
  but this is inferred, not directly confirmed by a captured error from those specific attempts.
- Consider auditing how `app-1.1.15` came to be mis-packaged in the first place (manual
  `dotnet publish` without `-c Release`? An older `release.ps1` without the version-stamp args?) so
  it doesn't happen again silently.
