# Bandroom Handoff — August 8, 2026 (Session 7)

Picks up right after Session 6's handoff. This session: verified/pushed Session 6's pending
state-machine fixes, did a live UI bug hunt driven by owner screenshots, shipped a real release
(v1.0.49), fixed the release pipeline's disk-space/bundling problem, and found one more bug
that's still open.

**⚠️ Not committed/pushed yet:** `WebMainForm.cs`, `WebBridge.cs`, `wwwroot/app.js`,
`release.ps1` — see §4 below.

---

## 1. Shipped v1.0.49 (real GitHub release, not just a git push)

Session 6 left the state-machine fixes committed to `master` but never actually released to end
users — `git push` only updates the repo, it doesn't reach an installed copy. Ran the full
`release.ps1` pipeline this session (build → Squirrel pack → tag → `gh release create`):

- **Live:** https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.49
- Existing installs get this as a delta auto-update on next launch; new users get `Setup.exe`.
- Bundles all of Session 6's state-machine fixes + this session's UI fixes (see §2).

**Root-caused two release failures before it worked, both disk-space, different causes:**
1. First failures: `C:` drive was completely full (0 bytes free at one point). Cleared ~9GB of
   safe, regenerable junk (`SquirrelClowdTemp` scratch files, our own stale `publish_temp`
   output, Recycle Bin). This is a recurring risk — **check `df -h /c` before any future
   release**, don't assume there's headroom.
2. Real root cause once disk had room: the release was trying to zip **all 2.8GB of
   `Songs\Default\`** into the installer. The project already has a `/p:BundleDefaultSongs=false`
   MSBuild flag built for exactly this (comment in `BandAudioHook.csproj` says public builds
   should pass it) but `release.ps1` never did. **Fixed** — `release.ps1` now always passes
   `/p:BundleDefaultSongs=false`. Installer is 21.9MB now instead of multiple GB.

## 2. UI bugs found and fixed (owner sent live screenshots, not a spec)

All in `wwwroot/style.css` / `wwwroot/app.js`, all committed in `84c3aae` (already pushed +
released in v1.0.49):

- `#header-team-badge` had no size CSS at all — inherited `.team-swatch`'s `width: 100%` and
  stretched to fill the whole header (showed as a giant cyan block or a stretched team logo
  depending on which team was active). Fixed: capped at 30×30px.
- `.situation-volume-popover` was missing the `[hidden] { display: none; }` pairing that every
  other overlay in this file has — author CSS (`display: flex` on the class) always beats the
  browser's built-in `[hidden]` styling regardless of specificity, so the popover could never
  actually hide/show. Clicking the volume button did nothing visible. Fixed.
- `#my-downloads-grid` and `.situations-list` weren't real CSS grids — `.bandroom-album-grid`
  (My Downloads) had no grid rules at all (single stacked column), and `.situations-list` was a
  flex-wrap with `align-content: flex-end` that left huge dead space and let the glass-blur
  background bleed random colors through each card. Both converted to real
  `grid-template-columns` grids with opaque card backgrounds.
- `wwwroot/fonts/` was completely empty — the `@font-face` rules for "Outfit" (referenced
  everywhere) never resolved, so the whole app was silently falling back to Segoe UI. Downloaded
  the real Outfit variable font from Google's official repo and rewired the `@font-face` to a
  single `font-weight: 100 900` variable-font declaration (Outfit only ships as one variable
  file upstream, not separate per-weight statics).
- Removed the "Coming Soon" badge from the Situations panel per owner request (was the only
  occurrence in the codebase, `app.js`'s `openSituations()`).

## 3. Default song pack — still not live via the in-app button

- `DefaultSongPackService.cs` (client) + `cloudflare-defaultsongs` worker + R2 bucket
  (`bandroom-default-songs`) are all correctly wired end-to-end already, including real
  byte-progress reporting (`fraction, downloaded, total`) — this already IS the "% + time
  remaining" download UI the owner wanted, it just needs the actual file uploaded.
- **Blocked:** `wrangler r2 object put` hard-caps single-file uploads at 300MB. The pack is
  2.73GB. Tried; failed immediately with `Error: Wrangler only supports uploading files up to
  300 MiB in size`. No `aws-cli` or `rclone` installed on this machine, and no R2 S3-API
  credentials (Access Key ID/Secret) configured anywhere — wrangler only has OAuth, which can't
  do the S3-compatible multipart upload R2 needs for files this size.
- **Owner's call:** for now, skip the R2 upload. Provided a Google Drive link instead
  (`https://drive.google.com/file/d/1kZKcqfOSfMv9k2sppduTE9hWpaVrPerN/view`). Changed the
  in-app "Download Base Sound Pack" button to open that Drive link in the system browser
  (`bridge.OpenExternalUrl`, new method) instead of calling the broken
  `bridge.DownloadDefaultSongPack()` pipeline. Marked clearly in `app.js` as temporary, with a
  comment pointing back at switching to the real pipeline once `pack.zip` is actually on R2.
- **Next session, if this gets prioritized:** either (a) get real R2 API credentials (Cloudflare
  dashboard → R2 → Manage API tokens) and use `aws-cli`/`rclone`/a small S3 multipart-upload
  script, or (b) split/compress the pack smaller, or (c) leave the Drive-link workaround as the
  permanent answer if that's fine long-term.

## 4. NOT YET committed or pushed

- `WebMainForm.cs` / `WebBridge.cs`: new `OpenExternalUrlFromWeb`/`OpenExternalUrl` bridge method
  (just `Process.Start(..., UseShellExecute = true)` — opens any URL in the system default
  browser). Builds clean, 0 errors.
- `wwwroot/app.js`: the "Download Base Sound Pack" button now calls `OpenExternalUrl` with the
  Drive link (see §3).
- `release.ps1`: the `/p:BundleDefaultSongs=false` fix (see §1) — **this one already shipped**
  in v1.0.49 since it was fixed before that release ran, but the file itself wasn't committed to
  git yet, so a fresh checkout wouldn't have the fix. Should commit this regardless of what
  happens with the rest.
- Owner said "hold off" before these were committed/released — **waiting on go-ahead**, don't
  assume these are live. `v1.0.49` does NOT include the Drive-link button change.

## 5. New bug found, not fixed (owner screenshot caught it)

`ConfigStore.cs:689` — the default seed config for the newly-added `down:4th` trigger:

```csharp
new() { Trigger = "down:4th", Event = "4th Down", AudioFile = @"C:\Games\Mod Folder\CFB Mods\MMC_Editor_v1.1.0.2\dies irie 0.wav" },
```

Every other default trigger in this same list seeds `AudioFile = ""` (shows "Unassigned" in the
UI, matches actual state for a fresh install). This one line has a leftover personal dev-machine
absolute path baked in instead — shows as "dies irie 0" in the Offense panel, looking assigned,
but the file won't exist on any real user's machine so playback will silently fail. Almost
certainly an accidental commit from whoever locally tested the new 4th-down feature. **Not
fixed this session** (owner said hold off) — the fix is a one-line change to
`AudioFile = ""` to match every other entry.

## 6. Other open items, unchanged from Session 6 or newly discussed

- **`main`/`master` unrelated git histories**: `origin/main` (single commit, v1.0.47 snapshot)
  and `origin/master` (this session's real 4-commit history) share no common ancestor
  (`no merge base`). A normal PR between them isn't meaningful — would show almost every file as
  changed. Owner chose to skip opening a PR for now; `master` is the branch actually being
  worked on and is fully pushed. Worth untangling eventually but not blocking anything.
- **Disk space is genuinely tight**: `C:` was at 13GB free / 223GB total as of this session's
  end (was briefly at 0 free). Owner asked about moving the whole `C:\Bandroom` project to `D:`
  (which has 187GB free) — **deliberately not done this session**, since `C:\Bandroom` is the
  active session's pinned working directory and relocating it mid-session would break every
  subsequent file operation. Worth doing as its own dedicated step, not mid-task.
- **Remote Play / different scoreboard layout**: owner plays on console via Remote Play, which
  has a different scoreboard than the calibrated "Kam's CBS Scorebug" presets. Good news: the
  existing `ScorebugPreset.cs` system already supports swappable named presets via fractional
  (not pixel) crop coordinates, so laptop vs. widescreen resolution is already a non-issue (only
  aspect ratio matters). Owner was going to send a 1080p console screenshot so a new preset could
  be calibrated the same way the existing ones were (eyeballed from a live screenshot, not
  pixel-measured) — **screenshot not yet sent/received as of end of session**.
- Everything else from Session 6's "next session" list (§6 area of that doc) — pause-screen OCR
  live verification, `DuplicateProfile` broken button, `ui-bot.js` production toast, Outfit font
  loading (now actually fixed, see §2 above), away-team-offense structural issue — still
  unaddressed, not touched this session.

---

## What "next session" should do, in order

1. Get owner's go-ahead, then commit + push `WebMainForm.cs`/`WebBridge.cs`/`wwwroot/app.js`
   (Drive-link button) and `release.ps1` (BundleDefaultSongs fix) — the release fix in
   particular should be in git regardless, since right now a fresh clone wouldn't have it.
2. Fix `ConfigStore.cs:689` — change the `down:4th` default `AudioFile` from the stray dev path
   to `""` to match every other entry (§5).
3. If the Drive-link screenshot arrives: calibrate a new `ScorebugPreset` for console/Remote
   Play from it (§6).
4. If default-song-pack R2 upload becomes a priority again: sort out real R2 API credentials
   (not wrangler OAuth) for a proper multipart upload, or accept the Drive-link workaround as
   permanent (§3).
5. Keep an eye on `C:` free space before any future `release.ps1` run — it's been the cause of
   every release failure this session.
