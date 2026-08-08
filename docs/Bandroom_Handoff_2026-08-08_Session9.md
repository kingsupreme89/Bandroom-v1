# Bandroom Handoff — August 8, 2026 (Session 9)

Picks up right after Session 8. This session ran **concurrently with another Claude Code session
working the same repo/branch at the same time** (confirmed multiple times live — commits `071a943`,
`066eefe`, and `8a926b1` all landed on `master` mid-session, authored as the owner but not run by
this session). If you're picking this up cold: **check `git log` and `git status` before touching
anything** — this doc is a snapshot, and the other session may still be active.

**Current HEAD as of this doc: `201be1b`, on `master`, pushed.** No PR pending — everything landed
straight on `master` (both sessions were committing directly this time, not via PR). By the time
you read this it has almost certainly moved further — `git log` before doing anything.

---

## 1. What this session actually did

- **Song pack: real local zip-import.** `DefaultSongPackService.ExtractExistingZipAsync` +
  `WebBridge.BrowseForSongPackZip`/`ImportDefaultSongPackZip` + `WebMainForm` wiring. The
  "Download Base Sound Pack" button still opens the Google Drive link externally (owner's explicit
  standing decision, see `[[project_songpack_drive_method]]` memory — **do not** wire up the R2
  `cloudflare-defaultsongs` worker instead), but now there's a real "Locate & Import" follow-up
  step that extracts the downloaded zip into `ConfigStore.DownloadedDefaultSongsFolder` and the
  existing index-driven auto-assign (`ConfigStore.ImportDefaultPackForTeam`/`GetDefaultPackTeams`)
  picks it up automatically.
- **Marketplace hub redesigned Spotify-style.** `#bandroom-picker` split into a sidebar (`Your
  Library` team list, searchable) + main pane (Uploads/Top Contributing Teams shelves), widened to
  ~1400px/88vh. `#bandroom-album` and `#my-downloads` widened to match (1100px/85vh).
- **Cursor fixed.** A custom crosshair-reticle SVG cursor was applied to every button/tile
  app-wide (`style.css`, old "CROSSHAIR CURSOR STYLES (gamer UI)" block) — replaced with plain
  `cursor: pointer`/`cursor: text`. Pure CSS, no click-handler logic touched.
- **Black-primary team color fallback.** Only **Appalachian State** and **Army** have a literal
  black *primary* color in `TeamColors.cs` (everyone else's black is a secondary/trim color,
  which is fine). Fixed at the single source (`setActiveTeam()` in `app.js`) — a near-black
  primary now falls back to secondary for the `--team-primary` CSS var, so every current/future
  consumer is covered without per-element checks.
- **Trophy Room removed as a separate destination.** Folded into the team album: `Sound Bank` now
  shows songs *and* background images together in one indexed list (no more tab switcher). The
  `#btn-trophy-room` nav pill, `#tab-trophy-room`, `openTeamTrophyRoom`, and all related copy are
  gone. **My Downloads gained a real "Set as Background" action for images** — it didn't have one
  before (a real functional gap, not just a rename). Important implementation note: My Downloads'
  image URLs (`item.fileUrl`) are WebView2-only virtual-host addresses
  (`https://downloadedimages/...`), **not real network URLs** — the existing
  `DownloadAndSetTeamBackground` bridge method does a server-side `HttpClient` fetch and would
  silently fail (DNS failure on a fake host) if called with one of these. Added
  `TeamBackgroundDownloadService.SetFromLocalFile` + `WebBridge.SetTeamBackgroundFromDownload` /
  `WebMainForm.SetTeamBackgroundFromDownloadFromWeb` as a **local file copy** path instead. If you
  see "Set as Background" silently fail anywhere, check which of these two bridge methods it's
  calling and whether the URL it's passing is actually fetchable over real HTTP.
- **Onboarding (first-run favorite-team picker) converted to cover-flow.** Was a plain grid
  (`#onboarding-grid`); now uses the same cover-flow carousel component as Set Matchup
  (`matchupCoverflowTeams`, `.coverflow-stage`/`.coverflow-track`/`cf-l2`/`cf-l1`/`cf-center`/
  `cf-r1`/`cf-r2` classes — all class-based, not ID-scoped, so reusable anywhere). Unlike Set
  Matchup's "browsing is picking," onboarding has an explicit **Confirm Team** button — first-run
  shouldn't lock in on a stray arrow click. New functions: `renderOnboardingCoverflow`,
  `shiftOnboardingCoverflow`, `_onboardingPicked` state var.
- **Cloudflare workers deployed** ("lehgo"): `bandroom-marketplace` (includes the `"profile"`
  type from Session 8) and `bandroom-usercount` — both live.
- **v1.0.51 shipped** (`gh release` + Squirrel) covering everything through the first PR merge
  (song pack import, Spotify layout, cursor fix, black-primary fallback, plus the other session's
  grid/CSS/trim/XSS fixes from PR #1). **Nothing after v1.0.51 has been released yet** — the
  Trophy Room removal, onboarding cover-flow, and whatever the other session did in commits
  `066eefe`/`8a926b1` are on `master` but not yet in a shipped build. Do NOT run `release.ps1`
  without the owner saying "ppup" or explicitly asking for a release.

## 2. The other session's work (commits I didn't make, landed on `master` mid-session)

- **`071a943`** (before this session properly started, but discovered mid-session): fixed
  `#bandroom-songs-grid`/`#bandroom-images-grid` being targeted as CSS *classes* when they're only
  ever IDs (empty Sound Bank bug), dead CSS on logo/bg-edit buttons, zero-CSS crop/profile/update/
  songpack dialogs, marketplace uploads skipping the trim step, a stored-XSS gap in
  `buildItemTile` (`sanitizeHTML()` existed but was never called), and 3 regressions in an earlier
  "done" report (duplicate `pushKillFeedEntry`/`getDynastyRecord` declarations, a dead debounce
  listener). Also added `IntakeEngine.cs` (C# port of `scripts/intake_engine.py`) and
  `ImportAndUploadSongToMarketplace`. This landed via PR #1, merged by this session.
- **`066eefe`**: song-upload naming-convention hint (shows a concrete example like "UGA 3rd Down
  Stop" since filename-based auto-assign/profile-matching keys on it) + picked up this session's
  in-progress `WebBridge.cs`/`WebMainForm.cs`/`TeamBackgroundDownloadService.cs` changes from the
  shared working tree (see §1's Set-as-Background fix — their commit message correctly credits
  this as "already in the working tree").
- **`8a926b1`**: "Add per-row Play/Stop/DL buttons to the clipping island's song picker" — **not
  reviewed by this session**, landed literally while writing this handoff. Read the actual diff
  before building on top of it, don't trust this summary.

**If you're a fresh session reading this**: the fact that uncommitted working-tree edits from
*this* session kept ending up inside *their* commits (twice, confirmed via `git show <sha> --stat`
each time) means direct file edits are genuinely shared in real time when both sessions run
locally against the same checkout. If you're running standalone, this isn't a concern — but if the
owner says another Claude/Cline session is active, expect the same thing and verify before
committing (diff your intended change against what's already in `HEAD` — don't assume "not shown
in `git status`" means "not saved," it may mean "someone else already committed it").

## 3. Security agent — ran, found 2 real issues + 1 false positive

`scripts/security_agent.py` crashed on a `UnicodeEncodeError` the first run (emoji output vs. the
Windows console's cp1252 codepage — a bug in the script itself, unrelated to app code). Re-ran
with `PYTHONIOENCODING=utf-8 python scripts/security_agent.py` and it completed. Results, triaged:

- **False positive — ignore**: flagged `cloudflare/cloudflare-marketplace/worker.js` for a
  "Google OAuth credential." It's `GOOGLE_CLIENT_ID` (line 121) — a Client ID, not a Client
  *Secret*. Client IDs are public-by-design (embedded in frontend/mobile apps everywhere); the
  checker's regex just matches the string `client_id` too broadly. No action needed, but worth
  tightening the regex someday so this stops crying wolf.
- **Real bug, not yet fixed — 4 dead bridge calls.** `app.js` calls four `bridge.*` methods that
  don't exist anywhere in `WebBridge.cs` (verified directly, not just trusting the checker — 5 of
  its other 9 flagged methods turned out to be real/present, so the checker itself has false
  positives too; these 4 do not):
  - `bridge?.ShowHelp()` (`app.js:4045`, command palette's Help entry) — silently throws, Help
    does nothing from the palette.
  - `bridge.ScanDynastySave()` (`app.js:4434`) — throws, breaks whatever dynasty-save-scan flow
    calls this.
  - `bridge.DuplicateProfile()` (`app.js:4791`) — throws, "Duplicate Profile To..." context-menu
    action is broken.
  - `bridge?.PlaySoundboardSlot()` (`app.js:4190`) — throws, the soundboard favorites bar
    (`#soundboard-*` buttons in index.html) doesn't play anything.
  
  `ShowHelp` was trivial to fix (commit `201be1b`) — `WebMainForm.OpenHelpFromWeb()` already
  existed, just needed the one-line `WebBridge` wrapper every other `*FromWeb` method gets.
  **The other three (`ScanDynastySave`, `DuplicateProfile`, `PlaySoundboardSlot`) are NOT
  fixed** — checked `WebMainForm.cs` and there's no Dynasty/Duplicate/Soundboard-related method
  anywhere at all, not even under a different name. These are real unimplemented backends behind
  UI that already shipped, not just missing wrappers. Scope each one before starting: Dynasty-save
  scanning and profile duplication both sound like they need real file-system/parsing work, not
  one-liners.
- **Minor, informational**: two EventKey orphans (`Offense: Fourth Down`, `Defense: No Punt
  Return` exist in evaluators but aren't documented in `EVENT_KEY_MAP.md` — doc drift, not a bug)
  and one duplicate-EventKey warning (`Defense: Safety` fires from `SafetyHelper.cs` twice at the
  same volume — likely intentional dual-fire, same pattern as the Tackle-for-Loss exception
  documented elsewhere, but worth a glance).

## 4. Task queue (tracked via TaskCreate this session — re-create if your harness doesn't share it)

Done this session: song-pack zip-import, Spotify-style marketplace layout, cursor fix,
black-primary fallback, Trophy Room removal, onboarding cover-flow.

**Still open, roughly in the order the owner asked for them:**
1. **Popular Songs + Top Team Backgrounds rotating shelves** — replace the marketplace hub's
   static shelves with a rotating "Popular Songs" carousel (ranked by downloads+likes, per the
   owner's Circle-app reference screenshot) + a "Top Team Background Uploads" shelf below it,
   seeded from the existing TeamBackgrounds pack for now.
2. **Marketplace like/dislike** — add a dislike action alongside the existing like.
3. **Unified upload dashboard modal with flair selector** — one modal (owner's Circle-app
   reference) replacing the separate song/image/PA upload flows. Per the owner's own answer this
   session: flair pills are the existing "short sort" (school + song title) plus an event-flair
   pill — picking "event" should prompt for which event using the real trigger list, then hand off
   to an auto-placement pass (**reuse `IntakeEngine.cs`**, already added by the other session, C#
   port of `scripts/intake_engine.py`'s title-cleaning/team-resolution/trigger-mapping logic) to
   file it correctly. Also: **embed the Clipper (trim/preview) inside this same dashboard**
   instead of a separate flow.
4. **Pill-shaped bottom player island** — redesign `#clipper-island`/`#preview-bar` into a large
   rounded pill instead of the current horizontal rectangle, per the owner's Circle-app reference.
5. **Team-colored header wordmark** — style the "Bandroom" header wordmark using the active
   team's primary color (with the black-primary fallback from this session already covering the
   edge case) consistently across every screen.

Also mentioned but not scoped into a task yet: **"we're always gonna use the drive method"** for
the song pack (saved to memory, see `[[project_songpack_drive_method]]` — don't deploy
`cloudflare-defaultsongs`), a passing comment about **quarter length / kickoff logic** possibly
needing to ask the user how many minutes they're playing (OCR-related, not investigated), and
**Remote Play OCR calibration for console/PS5 users** (owner sent a 1920×1080 PS5 screenshot this
session for exactly this — Texas @ Oklahoma, 1st & 10, KICKOFF — save it if the owner sends it
again, it's useful for the still-uncalibrated Flag OCR region and the Session 7 Remote Play
scorebug preset).

## 5. Known-good verification state

- `dotnet build BandAudioHook.csproj -c Release` succeeds cleanly as of `8a926b1`.
- **Use `-c Release` when checking builds if the app might be running** — plain `dotnet build`
  (Debug) will fail with a file-lock error (`MSB3027`) if `Bandroom.exe` is currently running from
  a Debug build output, which happened mid-session. Not a code problem, just an environment one.
- The core event pipeline (`GameWatcher.cs` → `WebMainForm`'s `On*Changed` handlers →
  `EventRouter`/`FireEventForSide` → `AudioPlayer.Play`) was explicitly verified untouched by any
  of this session's UI work — confirmed by reading the actual wiring
  (`WebMainForm.cs:69-72`,`:385`,`:1067`), not just by diffing.

## 6. Starting a fresh session on this project

1. `git log --oneline -15` and `git status` first — this doc is a snapshot, not living truth, and
   there may be another session actively committing.
2. Read this doc, then Session 8's (`docs/Bandroom_Handoff_2026-08-08_Session8.md`) for the fuller
   project-layout primer if you need it.
3. `dotnet build BandAudioHook.csproj -c Release` to confirm a clean starting point.
4. Check whether the security_agent.py background run from this session ever finished
   successfully; re-run it if not (`PYTHONIOENCODING=utf-8 python scripts/security_agent.py`).
5. Pick up the task queue in §4, in order, unless the owner redirects.
6. **Never run `release.ps1`** without the owner saying "ppup" or explicitly asking for a release
   — real, live, irreversible GitHub release + tag push.
