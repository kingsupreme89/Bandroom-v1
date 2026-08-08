# Bandroom handoff — 2026-08-04

Repo: `D:\Claude\Projects\tools\BandAudioHook` (git remote: `kingsupreme89/Bandroom-v1`).
Current released version: **v1.0.7** (live on GitHub Releases). User is running it.

## What happened this session, in order

1. **Diagnosed why "push premo" (v1.0.4) silently did nothing.** Found two real bugs:
   - Squirrel's `SquirrelAwareExecutableDetector` only scans `.exe` files for a native
     VERSIONINFO string, embedded manifest, or a `<exe>.squirrel` sidecar file — it never
     reads .NET custom attributes on a `.dll`, and .NET 5+'s generated `apphost.exe` has no
     CLR metadata for `AssemblyMetadataAttribute` to live in anyway. Fixed by shipping a
     `Bandroom.exe.squirrel` sidecar file (see `BandAudioHook.csproj`) instead. Verified via
     reflection against `SquirrelAwareExecutableDetector.GetSquirrelAwareVersion` directly.
   - `WebMainForm.cs`'s `InitAutoUpdater()` ran in parallel with WebView2 init on `Load` —
     if the GitHub update check finished before `CoreWebView2` was ready, the "show update
     button" JS call was silently skipped with no retry (chime still played though, since
     that wasn't gated). Fixed by sequencing: WebView2 init now completes before the
     updater starts.
   - Released as v1.0.5, then v1.0.6 (added the version label described below), then v1.0.7
     (trivial rebuild, confirmed as a live end-to-end test that Update button now lights up
     correctly).
   - **Caught mid-fix**: a user machine stuck on old v1.0.4 (built before either fix) got
     the chime but never the button — a real demonstration of the exact bug. Rescued via
     `Update.exe --update="https://github.com/kingsupreme89/Bandroom-v1/releases/download/vX.X.X"`
     run directly (bypasses the broken in-app UI). Documented as the fallback for anyone else
     stuck: download and run `BandroomSetup.exe` from the latest release, Squirrel updates in
     place over an existing install.
   - Added a small `v1.0.X` version label in the header (`WebBridge.GetAppVersion()`,
     `wwwroot/app.js` `init()`) so this is visually obvious going forward.

2. **Detection-logic wiring** (the CFB27 game-state auto-trigger backlog from
   `CFB27_Session21_Handoff.md`, which is otherwise now largely superseded by this doc —
   its UI/WebView2-decision content is stale, but its "Detection-logic groundwork" section
   led directly to this):
   - User sent 4 live gameplay screenshots. Confirmed: the scorebug's rightmost segment
     (already calibrated as `GameWatcher.cs`'s `"down"` region, `FxX=0.65, FxY=0.85,
     FxW=0.14, FxH=0.09`) also carries **KICKOFF** (blue bg) and **PAT GOOD** (green bg,
     italic) banner text — same crop box, different background color/text per state. Also
     confirmed **TOUCHDOWN** is a *separate*, much wider full-screen white ribbon banner,
     not part of the small scorebug.
   - Extended that proven crop box into a new `"situation"` region (widened regex/word-list:
     KICKOFF, PAT GOOD, TOUCHDOWN, INTERCEPTED, FUMBLE, TURNOVER) rather than guessing new
     pixel coordinates from inconsistently-cropped screenshots. Added `NormalizeMatch()` to
     collapse OCR noise into stable trigger keys (`situation:kickoff`, `situation:pat_good`,
     `situation:touchdown`, `situation:turnover`).
   - Added an uncalibrated `"banner"` region for the big TOUCHDOWN/FIELD GOAL/SAFETY ribbon
     (`FxW=FxH=0`, same "not calibrated yet" convention as the pre-existing `"flag"` region).
     **Needs a clean, full-resolution screenshot at the exact moment it appears** to fill in
     real coordinates — the 4 screenshots sent this session were inconsistently cropped/sized
     and not trustworthy enough to hardcode blindly.
   - `ConfigStore.BuildDefault()` now auto-wires 4 of the 33 events to these states instead
     of a hotkey: Offense: Touchdown Scored, Offense: PAT Made, Other: Opening Kickoff,
     Defense: Turnover Forced. Everything else still needs a manual Numpad press.
   - **Not yet live-tested against an actual running game** — needs verification.
   - User also asked about resolution/scorebug-position independence for future broadcast
     styles. Recommended (not yet built): anchor-logo template matching (e.g. locate the
     CBS icon first) so region coordinates are relative to an anchor instead of absolute
     screen fractions, rather than full-frame OCR scanning every tick (expensive).

3. **UI/feature backlog from `CFB27_Session21_Handoff.md`** — implemented all of it this
   session:
   - No stats dashboard / category-mix tiles, flat Load/Edit/Preview/Stop buttons per
     situation — **turned out already built**, nothing to change.
   - **First-run onboarding wizard**: `ConfigStore.IsFirstRun()` / `MarkFirstRunDone()`
     (gated on a `.firstrun_done` marker file), `WebBridge.IsFirstRun()` /
     `CompleteFirstRun(team)`, new `#onboarding-overlay` in `index.html` reusing the
     existing team-picker grid styling. Shows once, asks favorite team.
   - **Sound-bank import + name normalization**: `ConfigStore.ImportIntoSongsLibrary()` now
     actually copies dropped/browsed files into `Songs\` with the filename normalized to ALL
     CAPS (previously `AssignTrackForm.BrowseForFile` just referenced the original path
     wherever it lived — a real bug, now fixed). Wired to both the existing Browse dialog and
     new whole-window native WinForms drag-and-drop (`WebMainForm.OnSongDragEnter/DragDrop`).
     **Caveat**: couldn't confirm WebView2's internal Chromium drop handling won't intercept
     the file drop before it reaches the WinForms handler — the `AllowExternalDrop` API I
     expected to disable that isn't exposed the way documented in this WebView2 SDK version
     (1.0.2792.45), so that line was removed. **Needs a live drag-and-drop test** to confirm
     it actually works, not just compiles.

4. **Live user-count ticker** (new ask, not from the old handoff doc): user wants to see how
   many people are running Bandroom right now, shown subtly in the header.
   - Design: Cloudflare Workers + KV, free tier. Client sends an anonymous per-install GUID
     heartbeat every 60s; KV entries have a 120s TTL so closed/crashed instances drop off
     automatically with no explicit disconnect needed. No IP/hostname/PII stored beyond the
     GUID itself.
   - Built and committed: `cloudflare-usercount/worker.js` + `wrangler.toml` (Worker code,
     ready to deploy), `UserCountService.cs` (heartbeat sender + cached count fetcher,
     `Endpoint` left blank so the whole feature is a safe no-op until deployed),
     `WebBridge.GetActiveUserCount()`, header ticker in `wwwroot/index.html`/`app.js`/
     `style.css` ("· N watching now" next to "Live Session", hidden if endpoint unreachable).
   - **Blocked on the user**: needs Node.js installed (not present on this machine), then
     `npx wrangler login` (interactive OAuth, can't be done for them), then
     `npx wrangler kv namespace create USERCOUNT`, paste the printed id into `wrangler.toml`,
     then `npx wrangler deploy`. Full steps in `cloudflare-usercount/DEPLOY.md`. **Once they
     send back the resulting workers.dev URL, paste it into `UserCountService.cs:15`
     (`const string Endpoint = "";`) and ship — that's the only remaining step.**

## Environment notes
- `gh` CLI is installed but not on PATH: `C:\Program Files\GitHub CLI\gh.exe`. Already
  authenticated as `kingsupreme89`. Add to PATH per-session in PowerShell:
  `$env:Path += ";C:\Program Files\GitHub CLI"`.
- Release pipeline: `powershell -File .\release.ps1` from the repo root — auto-bumps patch
  version from the latest git tag, builds, packs with Squirrel, tags, pushes, creates the
  GitHub release with all 3 assets. This is what "push premo" means (per user's standing
  memory instruction) — full pipeline, no confirmation needed when they say that phrase.
- The repo has `bin/`/`obj/` build output tracked in git (pre-existing, not something to
  "fix" unprompted) — be careful with broad `rm -rf bin obj` or `git add -A`, always add
  specific source files by name.
- Node.js is NOT installed on this machine (checked this session). If the user asks for
  anything else that needs npm/node, that's a prerequisite to flag first.

## Immediate next steps, in priority order
1. **User-count worker deploy** — waiting on the user to install Node, run the wrangler
   steps, and send back the URL.
2. **Live-test detection wiring** — needs the user to actually run the game with v1.0.7+
   watching, and confirm `situation:kickoff` / `situation:pat_good` / `situation:touchdown` /
   `situation:turnover` fire correctly (check `crash.log` next to the running exe, or the
   in-app log, for `[situation] OCR read: "..."` lines).
3. **Calibrate the TOUCHDOWN banner region** — needs one clean, full-resolution (ideally
   1920x1080, uncropped) screenshot at the exact moment the banner appears, then fill in
   `GameWatcher.cs`'s `"banner"` region's `FxX/FxY/FxW/FxH`.
4. Anchor-logo detection for multi-scorebug-style support (Fox, ABC, etc.) — design only,
   not started.
