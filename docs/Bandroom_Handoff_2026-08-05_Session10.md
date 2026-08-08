# Bandroom Handoff — 2026-08-05, Session 10

Source: `D:\Claude\Projects\tools\BandAudioHook` (git, remote `origin` =
https://github.com/kingsupreme89/Bandroom-v1). Continued directly from Session 9's handoff
(`D:\Claude\Projects\Bandroom_Handoff_2026-08-05_Session9.md` — read that first for the full
project layout, the "ppup"/release.ps1 convention, and everything shipped through v1.0.29).

**Explicit instruction for next session: implement everything in "What's left" below without
waiting to test each piece interactively first** — the user asked for this handoff specifically
so the next session can move straight to implementation. Still build carefully (syntax-check,
`dotnet build`, `node --check`) since untested ≠ unverified-to-compile — just don't block on
live in-app confirmation before moving to the next item the way prior sessions did.

## Shipped this session (v1.0.29 → v1.0.30, released via ppup)

- **Marketplace backend deployed and fully wired end-to-end** — this was the big scope of the
  session:
  - `cloudflare-marketplace` worker deployed live at
    `https://bandroom-marketplace.bandroom.workers.dev` (R2 bucket `bandroom-marketplace-files`
    + KV namespace `MARKETPLACE_META`, both created manually by the user in their own terminal
    via `npx.cmd wrangler ...` — `npx` alone was blocked by PowerShell execution policy on this
    machine, `npx.cmd` works around it without changing the policy).
  - **The Bandroom hub** (`wwwroot/index.html`/`app.js`, `openBandroomMarketplace` /
    `renderBandroomHub`) is now a real landing page, not just a team picker: a horizontally
    scrolling "Newest Uploads" shelf (Mac Finder-style, `.bandroom-recent-grid`/
    `.bandroom-item-tile`) shows the newest items across every team/type, clicking one jumps
    straight into that upload's team+tab. Team search grid still sits below it for direct
    team selection. Explicit user ask: **no placeholder content anywhere** — if a team/hub has
    nothing uploaded, it says so in plain text (`.bandroom-empty-state` /
    `.bandroom-recent-empty`) instead of rendering rows of empty "+Upload" tiles.
  - **Sound Bank / Trophy Room album grids** (`renderSoundBankGrid`/`renderTrophyRoomGrid`) now
    fetch real data via `GET /list?type=song|image&school=<team>` and render actual tiles
    (image thumbnail via `<img>`, song via a note-icon tile that plays a preview on click —
    `previewSong`/`_previewAudio`), with exactly one trailing "+Upload" tile, instead of a fixed
    30/25-slot placeholder wall.
  - **Upload flow fully wired** (`openUploadPicker` → hidden `<input type=file>` →
    `#bandroom-upload-overlay` name prompt → `confirmUpload`): school is never re-typed, it's
    always `albumTeam.name` (the album you're already in) — only the item name is asked, since
    re-typing the school the user is already inside of would just be redundant friction.
  - **Client-side compression before upload**, per explicit user ask ("compressed universally so
    all images are the same" / "songs compressed to still give HD quality"):
    - Images: `compressImageFile` — canvas resize, longer edge capped at 1600px, re-encoded as
      JPEG at quality 0.85. Simple, safe, easily testable, no external dependency.
    - Songs: `compressAudioFile` — Web Audio API `decodeAudioData` → `MediaRecorder` capturing a
      `MediaStreamAudioDestinationNode` at 160kbps Opus/WebM. Deliberately **no external encoder
      library** (lamejs etc.) — there's no build step/bundler in this project to vendor one
      through cleanly, and `npm`/`npx` are broken in this environment anyway (see below). This
      runs in **real time** (a 20s clip takes ~20s to "compress") since MediaRecorder has to
      actually play the graph through to capture it — acceptable for the short hype clips this
      app deals with, but **untested against a real audio file** — the browser preview available
      to the agent can't load/decode a real user audio file, so this whole path has only been
      reviewed statically, never run. Flagged as the single highest-risk piece of this session's
      work — see "What's left" below.
    - Both compression paths fall back to uploading the original file on any error
      (`try/catch` in `confirmUpload`) rather than blocking the upload entirely.
  - **Ticker now shows real upload activity** (`pollTickerActivity`, 60s interval) instead of the
    static placeholder string, pulling from the same `fetchRecentUploads` helper as the hub.
  - **Real bug fixed**: `pollUserCount()` was writing the online-user-count string directly into
    `#ticker-text` every 30s — the SAME element meant for upload activity — permanently
    stomping over the "No uploads yet" placeholder and (once wired) any real ticker data. Worse,
    the presence-dot's tooltip (`title="Connecting…"`) was never actually being updated at all,
    stuck forever. Fixed: `pollUserCount` now updates `#presence-dot`'s `title`, ticker owns
    `#ticker-text` exclusively via `pollTickerActivity`.
- **Header/UI polish requested mid-session**:
  - Marketplace tab pills (The Bandroom/Sound Bank/Trophy Room) had their own padding/font-size
    override making them 2-3px taller than Start Watching — confirmed via actual
    `getBoundingClientRect()` measurements in a browser preview, not just eyeballing. Removed
    the override so all header pills share one baseline.
  - Presence dot now glows in `--team-secondary` (same color driving the `.glass` island outline
    pulse) instead of a hardcoded green — and its pulse keyframe (`ticker-dot-pulse`) turned out
    to reference a keyframe name that was **never actually defined**, so the dot never visually
    pulsed at all before this fix (renamed to `presence-dot-pulse` with a real keyframe).
  - Start Watching (`#btn-watch`) is now visibly disabled (dimmed/grayscale/not-allowed cursor)
    until both matchup teams are picked (`updateWatchGate`, called from `updateMatchupLabel`),
    instead of only rejecting the click after a round-trip to the host with a `"no-matchup"`
    alert.
  - Bottom ticker made bigger/bolder/slower per explicit ask: 28px → 44px tall, 11.5px → 15px
    text (weight 500 → 600, color `--text-muted` → `--text-primary`), scroll animation 14s → 32s.
  - Crash-proofing added throughout the marketplace JS (`marketplaceGuard` wrapper) — any error
    in opening/rendering a marketplace overlay now force-closes back to a known-good state and
    toasts the user instead of leaving a frozen/half-broken overlay.
  - Minor pre-existing bug found and fixed (unrelated to this session's main work): `#header-bar`
    had two `id` attributes (`id="header-bar" id="drag-region"`) — invalid HTML, silently
    ignored by the browser, and `drag-region` was never referenced anywhere. Removed the dead
    attribute.
- **Real bug fixed — pause/unpause was re-firing the last sound** (`GameWatcher.cs`): every
  OCR'd region reset itself to blank whenever its text disappeared, including when a pause menu
  covers the whole scorebug. On unpause, the identical text (e.g. "touchdown") reappears and,
  since the tracker had been wiped, read as a brand-new event and refired the sound. Fixed by
  introducing `EventGatedRegions` (`situation`/`banner`/`quarter`) — these no longer reset on
  blank OCR at all; they only re-arm when the `down` region's value actually changes (a real new
  snap happened, tracked via `_downChangedThisTick`). A pause never changes the down, so it can
  no longer cause a refire; a genuine next score still works since there's always at least one
  down change (kickoff/new drive) between any two real scoring events. **Not yet confirmed
  live** — needs the user to actually pause/unpause mid-game and confirm no phantom refire.
- **TrimmerForm ("the clipper") now normalizes volume** — explicit user ask ("a limiter added to
  the clipper so songs all have the same volume"). `NormalizeAndLimit` in `TrimmerForm.cs`: reads
  the full trimmed clip into memory, applies a single RMS-based makeup-gain pass targeting -18
  dBFS (perceived loudness, not just peak — two clips can share a peak level and still sound
  wildly different in loudness), then soft-limits (tanh knee, not a hard clamp) anything that
  would exceed a -1 dBFS ceiling so a boosted transient never produces audible digital clipping.
  Wired into `SaveTrimmed()`. **Builds clean, never tested against a real audio file** — same
  caveat as the audio compression above, flag for live confirmation next session.
- Released as **v1.0.30** via `ppup` (release.ps1), tag/release live at
  https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.30. Full changelog + a 20-item
  "what's still to come" roadmap post were written for Discord — see that message in the
  conversation history if this doc doesn't cover it verbatim; the "What's left" section below is
  the authoritative, updated version of that roadmap now that this session did some of it.

## Unresolved as of end of session: user reported not getting an update notification

User asked "check the update script i didnt get noti" right after v1.0.30 shipped. Diagnosis so
far (checked directly on this machine, not guessed):
- `Get-Process Bandroom` showed the app running since 5:46 AM, ~57 minutes uptime at the time of
  the question — plenty long enough for the 3-minute background poll (`InitAutoUpdater` in
  `WebMainForm.cs`) to have cycled many times.
- `C:\Users\Fresh\AppData\Local\Bandroom\highest_version_seen.json` showed `{"Version":"1.0.29.0"}`
  — matches the currently-running `app-1.0.29` folder, so `VersionGuard.CheckAndRecord` correctly
  found no downgrade on this run (not the cause).
- **No `crash.log` exists anywhere under the Bandroom install folder** — meaning the background
  update-check loop has never caught/logged an exception. Either it's working silently with
  nothing to report yet, or (less likely given the try/catch wraps the whole loop body) something
  is preventing the loop from ever running its check at all.
- Most likely explanation: pure timing — v1.0.30 had only been live on GitHub for a couple of
  minutes when asked, well within one 3-minute poll cycle's worth of lag.
- Told the user to click the **"Up to date"** button directly (`ShowUpdateDialogFromWeb`) to force
  an immediate on-demand check instead of waiting on the timer — **conversation ended before the
  user reported back which of the three outcomes (found it / said already-latest / errored) they
  got.**

**If this comes up again next session, start here**: ask what the manual "Up to date" click
showed.
- If it found v1.0.30 and downloaded fine → was just timing, not a bug, nothing to fix.
- If it said "already on the latest version" (wrong) → this is the same still-unresolved
  auto-updater regression flagged at the end of Session 9 (silently stopped applying updates
  between v1.0.27 and v1.0.29, root cause never found — a full Setup.exe reinstall was the
  workaround that got this machine to v1.0.29, not a fix). Worth checking: is `GithubSource`
  actually resolving the newest GitH001 release correctly (rate limiting? caching?), is the
  `RELEASES` file in the v1.0.30 release assets well-formed, does `mgr.CheckForUpdate()` behave
  differently across `ShowUpdateDialogFromWeb` (manual) vs the `InitAutoUpdater` background loop
  despite using identical `GithubSource`/`UpdateManager` construction in both.
- If it errored → check the MessageBox text shown (network/VPN framing) and `crash.log`, which
  should now actually have an entry to read from that specific failure.

## What's left (supersedes the Session 9 "not built yet" list — items 1-3 from that list are DONE)

Session 9's items "Deploy cloudflare-marketplace", "Wire the client upload flow", and "Wire the
client list/render" are now complete (see "Shipped this session" above). Remaining, in rough
priority order:

1. **Confirm the audio pipeline live** — both `TrimmerForm.cs`'s `NormalizeAndLimit` and
   `app.js`'s `compressAudioFile` (Opus/MediaRecorder) were written and build/syntax-check clean
   but have **never been run against a real audio file** (the agent's browser preview can't
   decode/play a real user file, and `TrimmerForm` is a native WinForms dialog the agent can't
   drive at all). Trim a real song, confirm it saves without error and sounds normalized: not
   ear-shattering loud, not silent, no obvious digital clipping/artifacts. Upload a real song
   through Sound Bank, confirm the "Compressing..." step actually completes and produces a
   playable file (fetch it back via `/file/<key>` and play it) rather than hanging or producing
   a corrupt/empty blob.
2. **Confirm the pause/unpause GameWatcher fix live** — pause CFB 27 mid-play, wait several
   seconds (longer than the old 2s cooldown), unpause, confirm the last sound does NOT refire.
   Also confirm a **genuine second real score still fires correctly** afterward (the down-change
   gate shouldn't accidentally suppress real events).
3. **Set as Team Background** (Trophy Room) — clicking a filled Trophy Room image tile currently
   just does nothing extra beyond the shared `buildItemTile` click handler (which for images in
   the album view does nothing — only hub tiles and song tiles have click behavior currently).
   Needs: a UI affordance on the tile (e.g. a small button/overlay on hover) → a new WebBridge
   method (e.g. `DownloadAndSetTeamBackground(team, url)`) that has the C# side download the R2
   file via `HttpClient` (don't try to pipe the bytes through the JS↔host bridge, that's likely
   size-limited/awkward) and save it into `ConfigStore.TeamBackgroundsFolder\<team>.<ext>` (see
   `TeamBackdrop.cs` for the existing local-background-file convention), then refresh the active
   backdrop if that team is currently showing.
4. **Upload progress feedback** — `confirmUpload` just shows a static "Compressing..." /
   "Uploading..." string with no percentage/spinner. Since audio compression can take as long as
   the clip itself (real-time MediaRecorder capture), a long song with zero visual progress will
   read as a hang. At minimum, add a spinner; ideally surface `MediaRecorder`'s
   `dataavailable`-so-far byte count or a simple elapsed-time counter.
5. **"My uploads" tracking + delete** — no accounts exist (deliberate, per the worker's own
   comment: "No accounts -- anyone can upload... first version"), so there's currently no way for
   someone to take down their own bad/duplicate upload. Simplest version: store uploaded item IDs
   in `localStorage` client-side when *this* browser/app instance uploads something, show a
   delete button only on tiles whose ID is in that local list, backed by a new `DELETE`-style
   endpoint on the worker (needs a matching worker.js change — currently only has
   POST/GET/OPTIONS).
6. **Moderation/reporting** — same "no accounts" tradeoff; worth at least a "report" button
   client-side that POSTs a flag somewhere (even just a KV counter the worker increments) before
   this gets wider exposure.
7. Search/filter within an album's grid (currently browse-only once you're inside Sound
   Bank/Trophy Room — the search bar only exists at the hub level for picking a team).
8. Flag/penalty banner detection — `GameWatcher.cs` "flag" region still `FxW=0, FxH=0`, never
   fires, needs a live penalty screenshot to calibrate (unchanged from Sessions 7-9).
9. Full-screen scoring banner ("TOUCHDOWN"/"FIELD GOAL"/"SAFETY" wide ribbon) — same,
   uncalibrated (unchanged from Sessions 7-9).
10. Opening Kickoff trigger — wired (`Other: Opening Kickoff`) but never confirmed against a real
    kickoff; the down/distance ribbon reads neutral/black with no possession during a kickoff,
    and `SideAwareEvents` requires `_possession != null` to fire, so there's a real risk it
    silently never fires (unchanged caveat from Sessions 8-9).
11. Tackle for Loss — still "not yet confirmed" live (unchanged from Sessions 7-9).
12. More scorebug crop-position presets (`ScorebugPreset.cs`) — only `KamsCbsScorebug` exists;
    ABC/FOX/ESPN skins will need their own entries as they come up live.
13. Auto-updater root-cause investigation — see the "Unresolved" section above; the Session 9
    regression (silent stop between v1.0.27 and v1.0.29) was worked around with a manual
    reinstall, never actually root-caused.
14. Logo art — ~105/133 teams still missing (unchanged for several sessions).
15. Discord "how to use Bandroom" guide/PDF for new users.
16. In-app onboarding tooltip pointing new users at The Bandroom the first time they open it
    (the first-run flow currently only picks a favorite team — `maybeShowOnboarding`/
    `#onboarding-overlay` — doesn't mention the marketplace at all).
17. Likes/favorites on marketplace uploads.
18. "Trending This Week" section on the hub (currently only "Newest").
19. Per-team upload leaderboard/counts.
20. Waveform preview on song tiles before committing to use one (the local `TrimmerForm` already
    has waveform rendering code — `LoadWaveformData`/`_waveformBitmap` — that same rendering
    approach could inform a lightweight web version, though this would need actual audio decode
    in JS, likely via Web Audio's `decodeAudioData` + drawing peaks to a `<canvas>`).
21. Bulk-download a team's whole Sound Bank/Trophy Room as a starter pack.
22. Rate limiting on uploads once there's a real audience (worker currently has zero rate
    limiting — "if abuse becomes a problem later, that's a follow-up," per its own top comment).

## Starting a fresh session on this project

1. Read this file, then Session 9's handoff for anything not superseded above (project layout,
   file map, "ppup" convention — still 100% accurate, nothing structural changed).
2. `cd D:\Claude\Projects\tools\BandAudioHook` and `git log --oneline -15` — this doc is a
   snapshot as of v1.0.30, treat it as a starting point not living truth.
3. Resolve the update-notification question first (see "Unresolved" above) if the user brings it
   back up — it's a quick diagnostic, not a rebuild.
4. Then work top-down through "What's left" — item 1 (confirming the audio pipeline actually
   works on a real file) is the highest-risk untested piece from this session and should be
   verified/fixed before building more on top of it, even though the user asked this handoff be
   written for "implement without testing" going forward for NEW work — the audio pipeline is
   already-written code carrying real unverified risk, not a blank slate.
5. `dotnet build` to confirm a clean starting compile, `node --check wwwroot/app.js` for the JS
   side (no build step/bundler — confirmed again this session that `npx`/`npm` don't work in an
   agent Bash/PowerShell session on this machine, EPERM on `D:\` paths and PowerShell execution
   policy blocking `npx.ps1`; `npx.cmd` works around the policy issue specifically, but real
   package installs into `D:\node_modules` are still a no-go from an agent session — ask the user
   to run those themselves, same as the wrangler commands earlier).
6. **Never run `release.ps1` without the user explicitly saying "ppup"** in the current
   conversation.
7. Marketplace worker source is `cloudflare-marketplace/worker.js`, already deployed and live —
   changes to it need a re-`wrangler deploy` (ask the user to run it, or confirm this session's
   Bash/PowerShell sandbox no longer has the EPERM issue before trying directly).

## Known issues / open items carried over unchanged from Sessions 7-9

- Kickoff caveat (see item 10 above) — still real, still unconfirmed.
- `ConfirmedTriggers` in `WebBridge.cs` is still: `situation:touchdown`, `situation:turnover`,
  `situation:pat_good`, `down:1st/2nd/3rd/4th`. Everything else (kickoff, tackle-for-loss,
  quarter, flag) is unconfirmed or uncalibrated.
- The Session 9 OCR rewrite (full-width band + down/quarter disambiguation) is itself still only
  indirectly confirmed (user confirmed downs work, but that confirmation predates several
  sessions of changes since).
