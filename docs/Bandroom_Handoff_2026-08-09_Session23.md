# Bandroom Handoff — Session 23 (2026-08-09) — RELEASED as v1.0.72

Picks up right after Session 22 (`docs/Bandroom_Handoff_2026-08-09_Session22.md`, uncommitted at
the time). This session's own work is committed and shipped: `git log` shows two commits,
`e194a5d` then `a28a634` (the "ppup" release commit), tagged `v1.0.72` and live on GitHub
(`https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.72`).

**Important context for this session: a second, independent Claude Code session was working in
this same repo concurrently**, building a "Sound Booth" audio-effects overhaul (RAM caching,
sidechain ducking, EQ presets, transient shaper, stereo widener, a real limiter, two new reverb
presets). That work landed in the same commit/release as this session's own work below — see
"The Sound Booth" section for what it is and what was found/fixed in it.

## What this session actually built (Parts A/B/C, commit `e194a5d`)

### Part A — "Load Conference Pack" fixed (was silently finding 0 songs)
Root cause: `Songs\Default\SEC\` (and every real conference pack) stores songs under **per-team
subfolders** (`SEC\Georgia\*.mp3`) with zero loose files at the conference root — confirmed via
`Get-ChildItem -File` (0 results). `PreviewConferencePackForTeam`/`ApplyConferencePackSelections`
(`ConfigStore.cs`) only ever scanned the conference **root** (`TopDirectoryOnly`), so the button
always found nothing for a pack organized this way, despite the owner having the full 594-file SEC
pack downloaded. Fixed by adding `ConferencePackFiles()`, a shared helper that unions the team's
own subfolder with any loose conference-root files (team-specific wins on an EventKey collision).
Also fixed a dead regex bug found along the way: `EventKeyFromFileName`'s `_\d+$` suffix-strip
regex ran AFTER `_` was already replaced with `: `, so it never matched — any `_2`/`_3` alternate
file never resolved to its base event. Consolidated 3 other copies of the same broken inline logic
to call the one fixed helper.

### Part B — "Load All (Overwrite)" button added to the song-pack import wizard
New button next to "Import from Folder" (`songpack-import-overlay`), gated behind an explicit
overwrite-confirm dialog (reuses `#auto-assign-confirm-overlay`'s Yes/Cancel pattern). On confirm:
imports with a new `overwrite: bool` threaded through `DefaultSongPackService.ImportExistingFolderAsync`
→ `CopyFile` (replaces same-named files in place instead of the old `_2`/`_3` alternate-numbering),
then re-runs `ConfigStore.ImportTeamFolderForTeam(...)` (new — see below) for every team the import
found so songs actually land in event slots, not just the library.

### Part C — Supabase cloud-sync groundwork (System 1 of `BANDROOM_STREAMER_MASTER_PROMPT.md`)
New `CloudDatabaseService.cs` (raw `HttpClient` against Supabase's PostgREST API, no SDK needed)
and `schema.sql` (both the pragmatic `team_profiles` JSONB-mirror table it actually uses today, and
the full normalized schema from the master prompt for later). `ConfigStore.SaveProfile` fires a
**debounced** (1.5s, per-team, coalesced) best-effort background push when configured — local JSON
write stays synchronous/authoritative and unaffected; the app works fully offline exactly as
before. `WebBridge.GetSupabaseSettings`/`SaveSupabaseSettings` exist so credentials can be wired up,
but **there is no Settings UI panel for entering them yet** — next session's task if this gets used.

The `BANDROOM_*_GUIDE.md` / `BANDROOM_PITCH_DECK.md` / `BANDROOM_STREAMER_MASTER_PROMPT.md` files
are intentionally in `.gitignore` now (owner: personal-machine reference only, not meant to ship in
the repo) — don't `git add -f` them.

## The Sound Booth (the OTHER session's work, audited + bug-fixed by this session)

New files `AudioEngine.cs` (RAM cache + `CachedAudioSource`, biquad EQ, transient shaper, stereo
widener, lookahead limiter, offline LUFS analyzer) and changes to `AudioPlayer.cs`,
`AudioDuckingController.cs` (rewritten from a dead, never-instantiated class into a real wired-in
effect), `ReverbProvider.cs` (+2 weather-variant presets), plus matching UI
(`#soundbooth-section` in `index.html`, wiring in `app.js`, styles in `style.css`) and bridge
methods in `WebBridge.cs`/`WebMainForm.cs`. `PreRollSeconds` dropped from 1.0s to 0.0s on the
premise that RAM caching removes the disk-read stall that pre-roll used to hide.

**This session ran two independent audit passes (manual trace + automated code-review) and found
+ fixed 6 real bugs in that work**, all now shipped in v1.0.72:

1. **PA layer was ducking itself** — `FireEvent`'s PA-clip `Play()` call didn't pass
   `isHighPriorityEvent`, so on a Touchdown/Turnover/Safety the PA announcer got ducked to 40%
   right along with everything else, contradicting the code's own stated goal ("cuts through
   clearly"). Fixed: PA call now passes the same `isHighPriorityEvent` as the main clip.
2. **Cache-preload race reintroduced the exact stall it was meant to remove** —
   `AudioCache.Preload()` was fire-and-forget (`Task.Run`, not awaited) before
   `_hook.Start()`/`_watcher.Start()`; the first game event could fire before preload finished,
   hitting a cold disk read with the new 0.0s pre-roll. Fixed: `Preload()` now runs synchronously
   before watching starts.
3. **`AudioCache.Invalidate` was never called anywhere** — confirmed via full-repo grep. Directly
   interacts with this session's own "Load All (Overwrite)" feature: overwriting a file in place
   wouldn't be reflected in the RAM cache until app restart. Fixed: `DefaultSongPackService.CopyFile`
   now calls `AudioCache.Invalidate(destPath)` after every overwrite.
4. **Duck-release race on overlapping high-priority events** — each `OnHighPriorityEventFired()`
   call scheduled its own independent 2s "un-duck" timer; an earlier event's timer could fire and
   reset the gain to 1.0 while a later, overlapping event's window should still have been active.
   Fixed: rewrote `AudioDuckingController` to track one shared, only-ever-extended deadline that the
   persistent tick loop checks directly — no more independent per-event timers.
5. **Silent-freeze risk in that same tick loop** — the `while(true)` loop had no exception handling;
   an unhandled exception would silently kill it and could leave `_current` frozen mid-duck (every
   clip capped at 40% volume) for the rest of the session with no recovery. Added try/catch +
   `CrashLog`.
6. **`LimiterProvider` O(n) per-sample peak rescan** — the lookahead peak detector rescanned the
   entire ~440-sample window on every single output sample (~3.9M comparisons per ~100ms audio
   buffer callback), on the always-on playback path (limiter is on by default for every clip).
   Replaced with a proper O(1)-amortized monotonic-deque sliding-window max.

**Checked and deliberately NOT changed** (design notes for whoever picks this back up):
- `AudioCache` has no eviction/LRU — RAM usage grows unbounded across a long session that watches
  many different matchups. Not wrong, just unbounded. Worth a cap if this becomes a real issue.
- `StereoWidenerProvider` throws if fed a >2-channel source — theoretical only (real song files are
  mono/stereo), left as-is.
- Sound Booth toggles (EQ/ducking/transient/widener/bypass) aren't persisted across restarts —
  confirmed this matches the existing pattern (`MasterVolume`, `CurrentReverb`, `PreRollSeconds`
  aren't persisted either), so it's consistent behavior, not a regression.

## Two unrelated pre-existing bugs found incidentally (NOT fixed, NOT this session's scope)
Surfaced by the automated audit pass while scanning the broader diff; flagged here rather than
fixed since they're unrelated to either stream of work this session:
- `wwwroot/app.js` `renderProfileActivityFeed()` writes event-log text via `innerHTML` without the
  `sanitizeHTML()` escaping its sibling feed (`refreshEventActivityLog`) uses for the identical
  data. Low real-world risk on Windows (illegal filename chars), higher on the Mac client or for
  marketplace-synced names containing `&`/`"`/`'`.
- `src/Bandroom.Mac/MacWebBridge.cs` is missing 4 achievements present in the Windows
  `WebBridge.cs` (`month_streak`, `marketplace_creator`, `sound_scout`, `team_loyalist`) — Mac users
  meeting those criteria never see them unlock.

## Verification this session
`dotnet build BandAudioHook.csproj` → 0 errors, 0 warnings, checked after every fix round. Release
build (`dotnet publish -c Release`) succeeded as part of `release.ps1`. No live in-app click-through
of "Load Conference Pack" / "Load All" / Sound Booth toggles was done — build-clean and logic-traced
only, same tooling gap as prior sessions (no GUI-driving access).

## Starting a fresh session on this
1. Everything through this handoff is committed, pushed, tagged `v1.0.72`, and live on GitHub —
   `git status` should be clean modulo whatever the concurrent session does next.
2. **Supabase isn't usable yet** — no Settings UI panel to enter the project URL/anon key. The
   bridge methods (`GetSupabaseSettings`/`SaveSupabaseSettings`) and schema (`schema.sql`) are
   ready; wiring a small UI section is the remaining piece if this gets prioritized.
3. **The two pre-existing bugs above are still open** — not urgent, but easy, contained fixes
   whenever there's a slot for them.
4. **AudioCache eviction** is a known gap, not yet a reported problem — watch for it if RAM usage
   complaints come in after long multi-game sessions.
5. Live click-through verification of this session's three main features (Load Conference Pack,
   Load All, Sound Booth toggles) hasn't happened yet — worth doing before assuming they're
   pixel/behavior-perfect, same caveat every recent session has carried.
6. The Session 21 33-event checklist and the `D:\Bandroom` stale-duplicate-repo cleanup (Session 22
   notes) are both still open and untouched.
