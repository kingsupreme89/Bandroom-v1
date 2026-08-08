# Bandroom Handoff — 2026-08-05, Session 9

Source: `D:\Claude\Projects\tools\BandAudioHook` (git, remote `origin` =
https://github.com/kingsupreme89/Bandroom-v1). Continued directly from Session 8's handoff
(`D:\Claude\Projects\Bandroom_Handoff_2026-08-05_Session8.md` — read that first for the full
project layout, the "ppup"/release.ps1 convention, and everything shipped through v1.0.26).

## Shipped this session (v1.0.26 → v1.0.29)

- **v1.0.27**:
  - Island situation tiles get a pulsing outline glow in the active team's own secondary color
    (`--team-secondary`); global glass blur raised 22px → 26px for visibility.
  - **New "Other: Start of 4th Quarter" trigger**, wired end-to-end (`GameWatcher.cs` new
    "quarter" region → `WebMainForm.cs` → `ConfigStore.BuildDefault`). Calibrated from one
    live kickoff screenshot; **still needs live confirmation of an actual quarter rollover.**
  - **Scorebug OCR made broadcast-skin-independent.** The down/situation/quarter regions used
    to be one tight crop box calibrated against a single CBS Sports overlay skin. The game
    rotates between several skins (CBS/ABC/FOX/ESPN) that shift the scorebug horizontally, so
    all three regions now scan the FULL WIDTH of the bottom score-bug band instead. This
    created a real ambiguity (down "3rd" vs quarter "3rd" are the same OCR text) — solved by
    requiring "&" immediately after the ordinal for down (`3rd & 7`) and explicitly excluding
    it for quarter (`(?!\s*&)`), since down/distance is the only place "&" renders in that bug.
    **Possession-color sampling was deliberately NOT widened** — it's split onto its own
    dedicated tight crop (`SamplePossessionFromWindow`/`ScorebugPreset.PossessionFx*`) still
    calibrated to the CBS skin specifically, so widening the text OCR crop didn't wash out the
    color read. Possession detection itself is NOT yet skin-independent — flagged as a known
    gap, not solved this session.
  - **Named scorebug crop-position presets** (`ScorebugPreset.cs`), picked via a new Settings
    dropdown ("Scorebug position"). Only one exists so far — `KamsCbsScorebug`, the default —
    but a future skin/game just needs a new preset entry, no code digging required.
  - **Start Watching now refuses to start** (`ToggleWatchingFromWeb` returns `"no-matchup"`)
    unless Set Matchup has run first. Before this, the app would silently fall back to
    "single active team" mode (firing one team's cues for both sides) if you started watching
    without ever touching Matchup — explicit user ask to close that gap.
  - **Deployed the long-dormant `cloudflare-usercount` worker** (was scaffolded back in
    Session 7/8 but never actually deployed). Live at
    `https://bandroom-usercount.bandroom.workers.dev`. `UserCountService.Endpoint` now points
    at it. **Note**: `wrangler`/`npm` commands fail in an agent sandbox session on this
    machine — even `mkdir D:\` gets `EPERM`. The user has to run `wrangler login` /
    `wrangler deploy` themselves, in their own terminal, every time. Don't waste time retrying
    those commands from an agent session; ask the user to run them and paste back the output.
  - Bottom bar became a persistent ESPN-style ticker (team-colored "B" logo tile far left,
    scrolling text) — **then repurposed mid-session**: the online-count moved to a small
    subtle pulsing dot in the header toolbar (count in the tooltip), and the ticker itself is
    now meant for scrolling *upload activity* (song/image + school name) once the marketplace
    backend exists. Currently shows a placeholder line since there's no real upload data yet.
  - "The Bandroom" — first pass was a single header button opening a team-picker overlay →
    per-team "album" (Sound Bank 6×5 song grid, Trophy Room 5×5 image grid, dock-magnify
    hover reusing `enableDockMagnify()`/`fillTeamSwatch()` exactly like the existing team
    grids). All upload slots are placeholder "+ Upload" tiles — clicking shows a "coming soon"
    alert. No backend existed yet at this point.
- **v1.0.28**: Reworked the marketplace entry point into **three island pill tabs** in the
  header toolbar (The Bandroom / Sound Bank / Trophy Room) instead of one button — explicit
  user ask ("tabbed islands on the toolbar"). Sound Bank / Trophy Room jump straight into the
  *active* team's album on that specific tab, skipping the team-picker step. Also fixed Escape
  not closing the two new Bandroom overlays (was only wired to team-picker/save-profile/
  matchup).
- **v1.0.29**:
  - **Real bug found and fixed**: the Bandroom/album overlays' `#id` CSS selector (`display:
    flex`) beat the browser's default `[hidden] { display: none }` on specificity, so the
    `hidden` attribute was being silently ignored — the overlay was effectively ALWAYS shown,
    reproduced live as a black screen with static placeholder content ("Team" / empty grids)
    that couldn't be dismissed by clicking the × or pressing Escape. This is the exact same
    bug class already fixed for `#team-picker-overlay` in a past session (see the comment
    right above it in `style.css`) — just hadn't been applied to the two new overlays. Fixed
    with the same `#id[hidden] { display: none }` override pattern. Also hardened
    `setAlbumTab()` to no-op instead of throwing if `albumTeam` is ever null (defense in
    depth, not the actual root cause).
  - User confirmed **Down (1st–4th)** and **PAT** live in a real game — added to
    `WebBridge.ConfirmedTriggers` alongside the pre-existing Touchdown/Turnover. Confirmed set
    is now: touchdown, turnover, PAT, all 4 downs.
  - **Scaffolded `cloudflare-marketplace`** (new worker, NOT deployed): R2 bucket for the
    actual song/image files, KV for metadata (name + school, matching the upload-prompt spec
    below). Endpoints: `POST /upload` (multipart: file, type, name, school), `GET /list?type=`,
    `GET /file/<key>`. See `cloudflare-marketplace/DEPLOY.md` for the exact commands — same
    account as `cloudflare-usercount`, so `wrangler login` shouldn't be needed again, but
    `r2 bucket create` + `kv namespace create` + `wrangler deploy` still need to run in the
    user's own terminal (same sandbox-EPERM issue as above).

## The full marketplace vision (user's spec, only partially built)

Given verbatim across several messages this session — worth preserving exactly since it's the
spec for everything still to build:

- **Three destinations**: "The Bandroom" (browse all teams / search / eventual global feed),
  "Sound Bank" (that team's uploaded songs), "Trophy Room" (that team's uploaded background
  images, shown with a glowing pulsing outline in the team's own color, with an option to
  **set an uploaded image as that team's actual background**).
- **Upload flow**: when a user uploads a song OR an image, a prompt must appear asking for
  **the item's name AND the school/team name** — this metadata is what lets the system
  organize/search/filter uploads correctly across Sound Bank, Trophy Room, and the Bandroom
  marketplace search all at once. (This part IS built into `cloudflare-marketplace/worker.js`
  already — `name`/`school` are required fields on `/upload` — but nothing on the client side
  actually calls it yet; the upload slots still just show an alert.)
- **Ticker** (bottom of app, persistent): scrolls *upload activity* — "someone uploaded X song/
  image, from Y school" — NOT the online user count. (Built structurally, not populated with
  real data yet — no backend calls it.)
- **Online-count "who's here"**: explicitly NOT the big ticker — a small, subtle indicator
  somewhere in the toolbar. (Built: `#presence-dot` in the header, flashing green, count in
  the tooltip.)
- Marketplace UI icons/tiles should look and behave like the rest of the app's team tiles —
  same logos, same dock-magnify hover ("Mac slide album style" per the user's words). (Built —
  reuses `fillTeamSwatch`/`enableDockMagnify`/`renderTeamGridInto` directly, no parallel
  implementation.)
- Marketplace destinations should be **island tabs on the toolbar**, not buried in a menu.
  (Built — three pill tabs in `.marketplace-tabs`.)

## What's NOT built yet (the actual remaining work)

1. **Deploy `cloudflare-marketplace`.** Needs the user to run, in their own terminal (agent
   sandbox can't write to `D:\node_modules`/`D:\` root — confirmed failing this session,
   don't retry it from an agent session):
   ```
   cd D:\Claude\Projects\tools\BandAudioHook\cloudflare-marketplace
   npx wrangler r2 bucket create bandroom-marketplace-files
   npx wrangler kv namespace create MARKETPLACE_META
   ```
   Paste the KV id into `wrangler.toml` (replacing `REPLACE_WITH_KV_NAMESPACE_ID`), then
   `npx wrangler deploy`, then send the resulting `workers.dev` URL back.
2. **Wire the client upload flow.** Once the worker's live: clicking a "+ Upload" slot in
   `renderSoundBankGrid`/`renderTrophyRoomGrid` (`wwwroot/app.js`) needs to open a real file
   picker + a name/school prompt (there's an existing `PromptDialog.cs` pattern used elsewhere
   in the C# host worth checking first, or a simple web `<input type=file>` + two text fields
   is probably simpler since this is a network upload, not a local file assignment like
   `AssignTrackForm`), then `POST` to `/upload` as multipart form data.
3. **Wire the client list/render.** `renderSoundBankGrid`/`renderTrophyRoomGrid` need to
   replace their hardcoded 30/25 empty placeholder loop with a real `GET /list?type=...`
   call, rendering real tiles for whatever comes back, keeping the empty "+ Upload" tiles to
   fill out the remaining grid slots.
4. **Trophy Room "set as team background" action** on a filled image tile — not designed or
   built at all yet, needs a bridge method into whatever currently sets `TeamBackgrounds\`
   (see `TeamBackdrop.cs`).
5. **Ticker real data** — once uploads exist, feed real "X uploaded Y — School" lines into
   `#ticker-text` instead of the current placeholder string.
6. **The Bandroom's own tab** (vs. Sound Bank/Trophy Room jumping to the active team) is still
   just the team-picker → album flow; a true cross-team browse/search/global-feed view was
   described by the user but not built as a distinct experience yet.

## Unresolved as of end of session: auto-update stopped applying

User reported being stuck on an old version and unable to reach v1.0.29 via the in-app
"Up to date" button. Checked `C:\Users\Fresh\AppData\Local\Bandroom` directly (same machine):
only `app-1.0.26`/`app-1.0.27` folders exist, and `Squirrel-Update.log`'s last entry is from
Aug 4 23:16 (an old update to v1.0.11) — **nothing has logged an update attempt since**, which
means even v1.0.28 silently never applied, not just v1.0.29. `highest_version_seen.json` shows
`{"Version":"1.0.27.0"}`, consistent with the last successful update being 1.0.27.

Given this, told the user to close the app fully and run
`D:\Claude\Projects\tools\BandAudioHook\squirrel_releases\BandroomSetup.exe` directly (a full
reinstall to the same `%LocalAppData%\Bandroom` path, bypassing whatever's wrong with the
delta-update path) — **result not yet confirmed, session ended before the user reported back.**

If this comes up again next session:
- Check whether the full-reinstall workaround actually worked (version tag in the header, or
  re-check the `app-*` folders / `Squirrel-Update.log` for a fresh entry).
- If it didn't, actually investigate WHY the auto-updater stopped triggering at all — candidates
  worth checking first: whether `InitAutoUpdater`'s periodic poll (`WebMainForm.cs`, shortened
  to 3 minutes in Session 8) is still actually running/not silently throwing, whether the
  GithubSource update check itself is failing (network, GitHub API rate limit, etc — nothing in
  the log means the check may not even be reaching the point of downloading), and whether the
  "Up to date" button's manual check path (`ShowUpdateDialogFromWeb`) is calling the same code
  as the background poll or a different one that could have its own bug.
- This is a real, currently-unexplained regression, not confirmed fixed — don't assume the
  Setup.exe workaround permanently resolves it without the user confirming AND without finding
  the actual root cause of why the background updater stopped logging anything.

## Known issues / open items carried over from Session 8 (still true)

- **Flag detection**: `GameWatcher.cs` "flag" region still `FxW=0, FxH=0` — never fires, needs
  a live screenshot of a real penalty banner.
- **Full-screen scoring banner** ("TOUCHDOWN"/"FIELD GOAL"/"SAFETY" wide ribbon): same,
  uncalibrated, never fires.
- **Kickoff caveat, still real and still unconfirmed**: the down/distance ribbon reads neutral/
  black when nobody has the ball, which is exactly the state during a kickoff. `SideAwareEvents`
  requires `_possession != null` before firing `Other: Opening Kickoff` — real risk this
  silently never fires. Needs a live test specifically watching for this.
- **Tackle for Loss**: still "not yet confirmed" in a live game (unchanged from Session 7/8).
- **This session's OCR rewrite (full-width band + down/quarter disambiguation) is itself
  unconfirmed live** — the user separately confirmed downs work, but that confirmation may
  predate this session's regex/crop changes. Worth an explicit re-test given how much changed
  under the hood, even though the user says downs are fine.
- **`ConfirmedTriggers` in `WebBridge.cs`** is now: `situation:touchdown`, `situation:turnover`,
  `situation:pat_good`, `down:1st/2nd/3rd/4th`. Everything else (kickoff, tackle-for-loss,
  quarter, flag) is unconfirmed or uncalibrated — see above.
- Logo art (~105/133 teams still missing), Discord "for dummies" PDF, Discord feature-roadmap
  post — all carried over untouched from Session 7/8, no movement this session.

## Starting a fresh session on this project

1. Read this file, then Session 8's handoff for the full project layout / file map / "ppup"
   convention (still 100% accurate, nothing structural changed).
2. `cd D:\Claude\Projects\tools\BandAudioHook` and `git log --oneline -15` — this doc is a
   snapshot as of v1.0.29, treat it as a starting point not living truth.
3. If continuing the marketplace: check whether `cloudflare-marketplace` has actually been
   deployed since this doc was written (`cat cloudflare-marketplace/wrangler.toml` — if the KV
   id is still `REPLACE_WITH_KV_NAMESPACE_ID`, it hasn't been). The 4 numbered "not built yet"
   items above are the real remaining scope, in dependency order.
4. `dotnet build` to confirm a clean starting compile, `node --check wwwroot/app.js` for the
   JS side (no build step, but worth syntax-checking before/after edits since there's no
   TypeScript/bundler to catch mistakes otherwise).
5. **Never run `release.ps1` without the user explicitly saying "ppup"** in the current
   conversation.
6. If you hit any `wrangler`/`npm install` command failing with `EPERM` on `D:\` paths in an
   agent Bash session — this is a real, confirmed environment limitation (not something to
   debug further), not a sandbox restriction you can lift with `dangerouslyDisableSandbox`
   (confirmed doesn't help). Ask the user to run the command in their own terminal instead.
