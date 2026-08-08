# Bandroom handoff — 2026-08-04, session 4

Continues from `Bandroom_Handoff_2026-08-04_Session3.md` (same day, same repo:
`D:\Claude\Projects\tools\BandAudioHook`, remote `kingsupreme89/Bandroom-v1`).

## Read this first — cross-session version conflict, still unresolved

**Two Claude Code chats were editing this repo at the same time this same day.** This session
(call it session 4) and a separate "Bandroom feature roadmap" chat (which wrote session 3's
handoff) both had real, uncommitted work sitting in the same working directory simultaneously,
and both tried to run releases independently, with **completely different versioning plans**:

- Session 3's plan: delete ALL existing GitHub releases/tags and relaunch fresh as `v1.0.1`,
  to match a version number already announced on Discord. That session got **blocked** on
  `git`/`gh` not being on PATH in the user's own PowerShell/CMD windows, and **only got as far
  as deleting GitHub releases `v1.0.3`–`v1.0.8`** (confirmed via screenshot) before ending.
  Local git tags `v1.0.0`–`v1.0.9` were NOT deleted (the delete command silently failed).
- This session (4): unaware of that plan at first, ran `release.ps1` directly, which bumped off
  the highest **local** tag and produced `v1.0.9`, `v1.0.10`, `v1.0.11` in sequence (three
  separate releases this session, details below). **The `v1.0.1`-for-Discord plan was never
  completed and is now stale** — GitHub's actual latest release is `v1.0.11`, not `v1.0.1`.

**What this means practically:** because both sessions were editing the same files on the same
disk, `v1.0.9` (the first release built this session) already contains BOTH session 3's work
(Save Profile button, Matchup picker, real possession detection, rail cleanup) AND this
session's work (waveform trimmer) — confirmed by grep for session 3's added symbols
(`SetGameTeamsFromWeb`, `ResolveTeamColor`, `SamplePossession`) all being present in the build.
**Nothing from either session was lost.** But the versioning story is now inconsistent with
whatever was announced on Discord as "v1.0.1" — **next session needs to ask the user directly**:
keep the `v1.0.9`→`v1.0.11` numbering that's actually live and installed, or do a deliberate
reset. Don't unilaterally touch tags/releases again without that answer; both approaches are
destructive/hard-to-reverse in different ways.

**Local git tags right now:** `v1.0.0` through `v1.0.11`, all still present, never cleaned up.
**GitHub releases right now:** `v1.0.9`, `v1.0.10`, `v1.0.11` (v1.0.0–v1.0.8 were deleted by
session 3). **User's installed copy:** force-updated to `v1.0.11` via `Update.exe --update=...`
directly at the end of this session (the in-app checker is unreliable — see Environment notes).

## Urgent — nothing has been committed to git across TWO full sessions of real work

`git status --short` shows **894 lines of changes**, none committed: the 16 dead-file deletions
from the old WinForms→WebView migration (`MainForm.cs`, `TopBar.cs`, etc. — legitimate, already
replaced by `WebMainForm.cs`, just never `git rm`'d), plus every source edit from session 3
AND session 4. If this working directory were ever lost or reset, all of today's work — Save
Profile, Matchup picker, possession detection, the entire waveform trimmer, home/away volume,
the storage reorg — disappears with it, git history included. **This should be the first thing
next session does**, before any more feature work: `git add` the real source changes (not
`bin/`/`obj/`), commit, push. Both session 3 and session 4 flagged this and neither one did it.

## What happened this session, in order

1. **Built a real audio clip trimmer** (the user's actual ask: "clipping module thats easy,
   loads the waveform and uses a slider to clip it, saves with a button to sounds folder but
   prompts asking what team and name of song it is"):
   - New `WaveformRenderer.cs` — samples an audio file into per-pixel peak values, draws them.
   - Rewrote `TrimmerForm.cs`: waveform display with draggable green(start)/red(end) marker
     lines directly on the waveform, in sync with the existing start/end `TrackBar` sliders.
   - `SaveTrimmed()` now prompts for **team name** then **song name** (via the existing
     `PromptDialog`), saves as `Team-SongName.wav`, handles filename collisions.

2. **Shipped `v1.0.9`** — before discovering session 3's parallel work existed. This is where
   the version-conflict story above starts. Also fixed a real bug in `release.ps1` while doing
   this: `Set-Content -Encoding utf8NoBOM` isn't valid in Windows PowerShell 5.1 (that flag
   needs PS7+) — every release was failing at the final `gh release create` step. Replaced with
   `[System.IO.File]::WriteAllText(..., new UTF8Encoding(false))`, which works on both.
   Also created a **"Bandroom (Dev Preview)" desktop shortcut** pointing directly at
   `bin\Debug\net10.0-windows10.0.19041.0\Bandroom.exe`, so the user can test in-progress work
   without waiting on a release. Note: that build always shows `v1.0.0` in the header (plain
   `dotnet build` never gets the `AssemblyVersion`/`FileVersion` stamp — only `release.ps1`'s
   `dotnet publish` does) and its in-app auto-updater will always fail (no `Update.exe`
   alongside it) — both are expected, not bugs.

3. **`AssignTrackForm.cs` cleanup** (per user request while looking at a live screenshot):
   removed the "Clear Assignment" button (`RequestClear` property left in place, just never
   set — no caller needs to change), and gave all buttons `Font = AppFonts.Get(9)` — they'd
   been silently falling back to the WinForms default font this whole time, every label around
   them was already using `AppFonts` but nobody had set it on the `GlassButton`s themselves.

4. **Fixed a real cross-side audio bug** in `AudioPlayer.cs`, found by walking through the
   home/away event logic with the user: the 20-second fire cooldown was keyed on a single
   global timestamp (`_lastFireUtc`), meaning if the home team scored and the away team forced
   a turnover a few seconds later, the away sound would get **silently swallowed** by the
   cooldown meant to stop the *same* OCR read from double-firing. Changed to
   `Dictionary<string, DateTime> _lastFireByPath` — cooldown is now scoped per audio file, so
   two different teams' clips never block each other; only the exact same clip re-firing too
   fast still gets blocked (the original, correct intent).

5. **Added independent Home/Away volume** — `AudioPlayer.HomeVolume`/`AwayVolume` (both default
   1.0), `Play(path, volumeOverride)` overload, `WebMainForm.FireEventForSide` now passes the
   right one through. New `SetHomeVolumeFromWeb`/`SetAwayVolumeFromWeb`/`GetHomeVolumeFromWeb`/
   `GetAwayVolumeFromWeb` on the host, mirrored on `WebBridge`. New **Away Volume** / **Home
   Volume** sliders in `index.html`'s right panel (`#matchup-volumes`), wired in `app.js`.
   Lets one side be turned down or muted without touching the other.

6. **Shipped `v1.0.10`** with items 3–5, force-updated the user's installed copy directly via
   `Update.exe --update="https://github.com/.../v1.0.10"` (see Environment notes on why).

7. **Investigated "finish the logos" ask, did NOT build it — needs a decision, see below.**
   `TeamColors.cs` already has full color data (primary/secondary hex) for **~140 FBS teams,
   all conferences**, not just SEC/Big Ten — that part's actually done. But:
   - `TeamBackgrounds/` (the big dim backdrop photo behind the whole app) only has real images
     for **14 SEC teams**; everything else either falls back to a shared `_generic/` pool
     (deterministic per-team pick, not a real photo) or nothing.
   - There is **no logo/crest image system at all** — the team grid swatches are just
     `linear-gradient(primary, secondary)` colored tiles with 2-letter initials
     (`WebBridge.Initials()`). A comment already sitting in that method (not written this
     session) explicitly names the gap: `"Placeholder badge text until real logos exist (see
     TeamLogos\ convention in GetTeamBackgroundUrl-style lookup, once logo files are actually
     provided)"`.
   - **I did not build the TeamLogos infrastructure this session** — ran out of time before the
     live-game test, and more importantly: **I can't source actual team crest/logo images
     myself** (real trademarked art, not something to generate or scrape). The mechanical part
     (mirror `TeamBackdrop.FindImagePath`'s exact convention — drop `TeamName.png` into a
     `Logos\` folder, wire a `GetTeamLogoUrl` bridge method, have `renderTeamGridInto` in
     `app.js` try an `<img>` first and fall back to the current colored-initial swatch) is a
     30-60 minute job whenever this is prioritized — it's just waiting on either real image
     assets from the user, or an explicit decision to skip logos and only finish backgrounds.

8. **Trimmer polish, driven by live user feedback while testing** (in this order, each was a
   real complaint from the user watching the actual dialog):
   - **Fixed real lag**: the waveform `Panel` wasn't double-buffered, and every single
     `TrackBar.ValueChanged` tick (fires continuously while dragging) redrew the *entire*
     waveform — hundreds of `DrawLine` calls — from scratch. Fixed by rendering the waveform
     to an offscreen `Bitmap` **once** when it loads, and having `Paint` just blit that bitmap
     plus the two marker lines. Added a small `DoubleBufferedPanel : Panel` subclass (the
     `ControlStyles.UserPaint | AllPaintingInWmPaint | OptimizedDoubleBuffer` trio other custom
     controls in this codebase already use, e.g. `TeamBackdrop`) since plain `Panel` doesn't
     have those set.
   - **Added precise numeric time entry** — a `NumericUpDown` next to each slider (0.1s
     increments, typeable), synced bidirectionally with the `TrackBar`s via a `syncing` guard
     flag to avoid feedback loops.
   - **Matched the button font** — same missing-`AppFonts` bug as `AssignTrackForm` (item 3),
     same fix.
   - **Fixed a real layout bug, caught from a screenshot**: the Save/Cancel button row was
     genuinely getting clipped off the bottom of the dialog. Root cause: the form was sized via
     `Width`/`Height` (450), but `Height` includes the OS title bar, whose actual pixel height
     varies — the content was laid out assuming more client area than `FixedDialog` actually
     left. Switched to `ClientSize = new Size(700, 400)` (exact, title-bar-independent) and
     tightened the vertical spacing between rows to close a dead ~58px gap that existed above
     the button row even before the clipping bug.

9. **Reorganized clip storage** (explicit user ask: don't dump everything flat in `Songs/`,
   separate trimmed clips from raw uploads, and don't duplicate storage on every load):
   - New `ConfigStore.SongsTrimmedFolder` (`Songs/trimmed/`) — `TrimmerForm.SaveTrimmed()`
     writes here now instead of flat in `Songs/`.
   - New `ConfigStore.SongsUploadedFolder` (`Songs/uploaded/`) — `ImportIntoSongsLibrary()`
     (called from `AssignTrackForm.BrowseForFile()`) writes here now instead of flat.
   - Picking an **existing** library track (not importing/trimming) still never copies
     anything — that was already true, just confirmed it stays true with the new folders.
   - `WebMainForm.OpenAssignTrack()`'s file scan changed from `Directory.GetFiles(SongsFolder)`
     (top-level only) to `SearchOption.AllDirectories`, so tracks in the new subfolders still
     show up in the "bank of songs" picker list.

10. **Shipped `v1.0.11`** with items 8–9, force-updated the installed copy again.

11. **Click sound effects — requested, NOT started.** User asked for "very subtle" click sounds
    on button presses app-wide. Planned approach (not implemented): synthesize a short, quiet
    click in `app.js` via the Web Audio API (a ~15-40ms decaying oscillator burst, low gain,
    lazily-created `AudioContext` on first click to satisfy autoplay-gating) rather than sourcing
    an actual audio asset, then a single delegated `click` listener on `document` scoped to
    interactive elements (`button, .team-swatch, .reverb-tile, .rail-item, .icon-btn`) that
    plays it without calling `preventDefault`/`stopPropagation` (so it can't interfere with any
    existing click handler, including the drag-handle guard from session 3). **Zero code written
    for this yet** — next session should start here if the user still wants it.

## Immediate next steps, in priority order

1. **Commit everything** — see "Urgent" section above. Two full sessions of real, tested,
   shipped work is sitting uncommitted. Exclude `bin/`/`obj/` build junk, stage real source
   files by name (not `git add -A`).
2. **Resolve the versioning question with the user directly** — keep `v1.0.9`→`v1.0.11` as the
   real numbering going forward, or do a deliberate, single-session reset to match whatever was
   announced on Discord. Don't let a third session make a third unilateral decision here.
3. **Live-test confirmation still outstanding** — the user was heading into a live game to test
   right as home/away volume + the cooldown fix shipped in `v1.0.10`. No confirmation either way
   was given by end of session on whether possession detection, the dual volume sliders, or the
   `MaxMatchDistance = 90` color-match threshold (flagged as a guess back in session 3) held up
   for a full game. Ask first.
4. **Team logos** — needs a decision, not more investigation: either the user supplies actual
   logo/crest image files (any conference, licensed or their own), or this stays
   colored-initials-only. The code path to wire it up once files exist is small (see item 7).
5. **Click sound effects** — user's explicit ask, queued, zero code written (see item 11).
6. **Cloud sound pack marketplace** — user named this as the next big feature after this
   handoff (from the original 20-feature brainstorm list: users upload/share hype clips,
   browsable by team). Not designed yet. Real questions to answer before writing code: hosting
   (S3/R2/GitHub Releases-as-CDN?), moderation/abuse surface for user-uploaded audio, whether
   this ties into the also-unbuilt `CloudSyncService`/premium-tier ideas from the same
   brainstorm doc, and whether it's free or part of a paid tier.
7. Session 3's still-outstanding items remain outstanding too (not touched this session):
   the `ScorebugProfile` idea (swappable region calibration per broadcast skin), expanding OCR
   auto-detection to the ~34 still-hotkey-only events beyond touchdown/turnover/PAT/kickoff,
   and the user-count Cloudflare Worker deploy (still blocked on Node.js not being installed).

## Environment notes (carried forward, some updated this session)

- `release.ps1`'s encoding bug is **fixed** (see item 2) — `push premo` now runs end-to-end
  without manual intervention, confirmed across two full release runs this session.
- **The in-app auto-updater is unreliable enough that the manual fallback is now the default
  move, not a last resort**: `"C:\Users\Fresh\AppData\Local\Bandroom\Update.exe"
  --update="https://github.com/kingsupreme89/Bandroom-v1/releases/download/vX.X.X"` — used
  after every release this session instead of waiting on/debugging the in-app check.
- `git`/`gh` still not on system PATH — every new terminal window needs
  `$env:Path += ";C:\Program Files\GitHub CLI"` (and `...\Git\cmd` if `git` itself is missing)
  repeated. Session 3 flagged this too. Worth fixing permanently via System PATH one of these
  sessions instead of re-patching per-window forever.
- `bin/`/`obj/` build output is tracked in git (pre-existing, not this session's doing) — don't
  `rm -rf` broadly, don't `git add -A`.
- Node.js still not installed — user-count Cloudflare Worker still un-deployed, unrelated to
  anything touched this session.
- **A "Bandroom (Dev Preview)" shortcut now exists on the user's Desktop**, pointing at the raw
  debug build. Always shows `v1.0.0` and its auto-updater will always fail — both expected.
