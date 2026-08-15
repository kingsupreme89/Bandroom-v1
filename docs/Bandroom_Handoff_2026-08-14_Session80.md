# Bandroom Handoff — August 14, 2026 — Session 80

Same idea as always: what happened, explained plain.

## Researched: Coffee's Own DATA-API.md (RAM Reader Schema)

Coffee published `DATA-API.md` in his `naileditcreativecs/Scorebug-Overlay-App` GitHub repo --
the first time his RAM reader's JSON schema has been documented rather than reverse-engineered
from decompiled source. Pulled and compared against our own `RamReaderValidator.cs`/
`GameStateNormalizer.cs`:

- **Confirmed correct**: `live-game-data.json` was a filename we'd inferred but never verified
  (see `ScoreboardReaderPaths.cs`'s own "never confirmed against a live run" comment) -- the doc
  confirms it outright.
- **New fields we don't use yet**: `ram.recentMessages` (raw banner text -- touchdown
  announcements with scorer names, flags, milestones) and `game.downDistanceKind` (an enum:
  numeric/goal/inches/kickoff/conversion/twoPointConversion/pendingSpecial). Neither wired in this
  session -- `recentMessages` in particular is still being shaped by Coffee (see v1.4.6-1.4.8
  below), not stable yet.
- Also checked v1.4.3 (unrelated in-app editor typing fix) and v1.4.5 (error-banner/diagnostics
  UX) -- neither touches the data feed, nothing for us to act on there.

## Built: RAM Reader's Own Freshness Data Replaces Our Staleness Guess

Coffee shipped v1.4.9, adding `ram.freshness` to the data feed -- for every field, the reader now
reports exactly when its published value last actually changed (`changedAt`/`secondsSinceChange`),
computed from directly re-checking live memory on every publish. His own documented guidance:
treat either clock (`gameClockSeconds`/`playClock`) showing a recent change as proof the *entire*
core memory block (quarter/clocks/scores/down/distance/timeouts, which all share one memory read)
is still live -- a frozen score while the clock ticks is genuinely unchanged, not stale.

This directly replaces the guessing half of Session 78's RAM-staleness fallback (the one that
compared RAM against our own OCR to infer "stuck"). Implemented:

- **`ScoreboardReaderState.cs`** -- new `ScoreboardReaderFreshness` DTO mirroring every field in
  the doc's `ram.freshness` block, plus `CoreBlockRecentlyChanged(utcNow, window)` (true if either
  clock changed within the window). Null-safe by design: a reader that predates v1.4.9 and never
  publishes this block must read as "no freshness data," never as "everything is stale."
- **`RamReaderValidator.cs`** -- new `BuildFreshness`/`BuildFreshnessEntry` parse `ram.freshness`
  off the raw document. Unlike every other field this class validates, a freshness entry that
  fails to parse is just left null rather than rejecting the whole document -- freshness is a
  trust *signal*, not game data.
- **`GameStateNormalizer.cs`** -- `ReaderNumericSnapshot` now carries `Freshness` straight through
  (not sticky-cached like the other fields -- it's the reader's own always-current snapshot of
  itself).
- **`GameWatcher.cs`** -- new `CoreBlockFreshnessWindow` (20s). The down/distance/possession
  stale-RAM-fallback block (Session 78's `RamFieldStaleThreshold`/`OcrFieldCorroborationWindow`
  logic) now only runs its OCR-comparison check when `coreBlockMaybeStale` is true -- i.e. either
  the reader has no freshness data at all (old behavior, unconditional), or both clocks have gone
  quiet past 20s per the reader's own timestamps. When a clock is genuinely ticking, the whole
  block is trusted outright and the OCR-comparison fallback doesn't even run anymore.

## Fixed (Partial): Play Clock Data Was Read, Validated, Then Discarded

Found while auditing "play clock wasn't working": `RamReaderValidator.cs` already validated
`game.playClock` and `ScoreboardReaderState.PlayClock` already carried it -- but nothing
downstream ever used it. `PlaySnapshot.IsPlayClockCounting` (`GameWatcher.cs`) was, and still is,
100% OCR-derived (`playClockRegion?.Last != null`).

- **Done**: `ReaderNumericSnapshot` now carries `PlayClock` (int, -1 sentinel, same convention as
  every other reader field) end-to-end from `GameStateNormalizer` through to `GameWatcher`. Data
  is available.
- **NOT done**: `IsPlayClockCounting` itself still isn't wired to it. `GameStateNormalizer`'s
  sticky-cache discipline (holds the last confirmed value across blank reads, same as every other
  field there) is wrong for this specific flag -- `IsPlayClockCounting` needs the *raw current-tick*
  read to edge-trigger correctly (see the existing comment right above it in `GameWatcher.cs`
  explaining why it deliberately reads `region.Last` directly, not a sticky field).
  `FirstDownOnFirstDownHelper` depends on a real false->true/true->false transition; wiring the
  sticky reader value in naively would latch it `true` forever the first time RAM resolves a play
  clock once, permanently breaking that helper's play-boundary detection. Needs a second,
  non-sticky per-tick capture path in the normalizer -- deliberately not rushed this session to
  avoid trading one bug for a worse one.

## Test Rig: Bundled a Real FOX Scorebug Skin for Local Testing

Owner needed to actually see a scorebug skin rendering to test against, but discovered along the
way that the whole skin-picker UI is currently missing:

- Confirmed Coffee's Corner (the skin gallery modal) and its scorebug-SKIN switcher were both
  **removed** on 2026-08-14 per `index.html`'s own comment at the matchup-header scorebug switcher
  -- the backend plumbing (`GetSavedScorebugSkin`/`SaveScorebugSkin`/
  `GetScorebugThemeGalleryFromWeb`/`ResolveActiveScorebugThemeFile` in `WebBridge.cs`/
  `WebMainForm.cs`) still exists but nothing in `app.js` calls any of it anymore.
- Confirmed the bundled `Assets\ScoreboardReader\theme-library\library.json` was empty
  (`"themes": []`) -- the "5 bundled skins" described in the original absorption handoff
  (2026-08-13) were never actually populated, in this repo or any build output.
- Found a real bug in `ScoreboardReaderPaths.ResolveUserDataDirectory()`: its
  `AppDataProductNameCandidates` list checks `"cfb27-scoreboard-overlay-version-1"` (an empty
  Electron `Local State`-only folder on this machine) *before* `"CFB27 Scoreboard Overlay"` --
  first-candidate-that-exists wins, so it would never reach the real installed folder
  (`CFB27 Scoreboard Overlay Version 1.0`, which has the "Version 1.0" suffix the candidate list
  doesn't include) even when a real theme-library sits right there. Confirmed via this machine's
  actual `%APPDATA%` contents; only theme installed locally was "Football Scorebug ESPN 2013" from
  that real folder. **Not fixed this session** -- worked around instead (see below), since the
  test needed a FOX skin, which wasn't in that install anyway.
- Owner supplied a real FOX theme export (`Scorebug-CFB on FoxV6.html`, a self-contained bundled
  page, same shape as Coffee's other theme files). Copied it into
  `Assets\ScoreboardReader\theme-library\themes\fox-v6\index.html`, added a matching entry
  (`name: "FOX"`) to the bundled `library.json`, and seeded
  `%LOCALAPPDATA%\Bandroom\UserData\scorebug_size_choice.txt` = `FOX` directly (bypassing the
  removed picker UI) so `ResolveActiveScorebugThemeFile()` resolves it and `ShowScorebugOverlay()`
  (already wired into the GAMETIME/Start Watching flow, `WebMainForm.cs`) shows it automatically
  on the next GAMETIME press. `canvasWidth`/`canvasHeight` (1200x800) were guessed from the file's
  own loading-thumbnail SVG viewBox, not confirmed against a live render yet.

## Build & Test Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, after both the
  freshness/play-clock wiring and the FOX theme bundling.
- `dotnet test src/Bandroom.Core.Tests` -- all 104 existing tests pass, no regressions.
- Confirmed the bundled FOX theme file and updated `library.json` actually land in build output
  (`bin/Debug/.../Assets/ScoreboardReader/theme-library/...`).
- **Nothing live-tested yet this session** -- no game was played against either the freshness
  fallback change or the FOX skin render. Owner's next live session is the real test for both.

## Git

Not committed -- this session's changes (`ConfigStore.cs`, `GameWatcher.cs`, `WebBridge.cs`,
`WebMainForm.cs`, `src/Bandroom.Core/GameStateNormalizer.cs`,
`src/Bandroom.Core/RamReaderValidator.cs`, `src/Bandroom.Core/ScoreboardReaderState.cs`,
`Assets/ScoreboardReader/theme-library/library.json`, new
`Assets/ScoreboardReader/theme-library/themes/fox-v6/`) are uncommitted local changes, mixed in
the working tree with several other files already modified/untracked from earlier sessions
(`AudioPlayer.cs`, `TeamColors.cs`, `HbcuPlaybackService.cs`, `MarketplaceChatService.cs`,
`cloudflare/cloudflare-marketplace/worker.js`, etc. -- not touched this session, listed here only
so a future `git add` doesn't accidentally bundle unrelated in-progress work into one commit). No
release triggered this session (no "ppup").

## Options Discussed, Not Started

- `IsPlayClockCounting` still needs the non-sticky per-tick RAM play-clock capture described above
  -- data's threaded through as far as `ReaderNumericSnapshot.PlayClock`, just not consumed yet.
- Score/quarter/timeouts don't have active stale-value protection the way down/distance/possession
  now do -- they have the separate/older -1-sentinel "never resolved" protection only. The new
  `ram.freshness` data now flows through for these fields too (`AwayScore`/`HomeScore`/
  `AwayTimeouts`/`HomeTimeouts`/etc. are all in `ScoreboardReaderFreshness`) but nothing reads them
  for this purpose yet.
- `ScoreboardReaderPaths.ResolveUserDataDirectory()`'s candidate-ordering bug (empty
  `cfb27-scoreboard-overlay-version-1` folder shadows the real
  `CFB27 Scoreboard Overlay Version 1.0` install) -- confirmed real on this machine, not fixed.
  Matters for anyone relying on an external Coffee install rather than BANDroom's own bundled
  theme-library.
- Coffee's Corner / scorebug-skin picker UI is gone from the app entirely (removed 2026-08-14,
  backend plumbing still intact) -- this session's FOX-skin test bypassed it by writing the config
  file directly. If skin-switching needs to be user-facing again, that UI needs to be rebuilt, not
  just re-enabled.
- `ram.recentMessages` (touchdown scorer/flag banner text) and `game.downDistanceKind` from
  DATA-API.md -- neither consumed yet. Coffee's own `recentMessages` vocabulary is still being
  mapped from tester data (v1.4.6-1.4.8's `messages-probe.jsonl`/`hudstate-probe.jsonl` asks) --
  worth waiting for that to stabilize before building on it.
- Session 79's open items (untouched again this session): `]`/`[` hotkey collision risk, Mac audio
  engine, Sparkle auto-update, icon-crop batch pass -- see that handoff for detail.
