# Bandroom Handoff — August 14, 2026 — Session 81

Same idea as always: what happened, explained plain.

## Attempted: Coffee Scorebug Overlay, Live-Tested Against a Real Game — Reverted

Picked up Session 80's uncommitted freshness/FOX-skin work and pushed it further with a real
live game (Air Force @ Arkansas) running the whole session. Ended in a full revert at owner
request ("remove all that coffee stuff") once the RAM reader itself proved unreliable -- see
below. None of this survives in the working tree; it's documented here only so a future session
doesn't re-discover the same dead ends from scratch.

**What got fixed along the way (all now reverted, but worth knowing if this is picked up again):**

- **Canvas-size guess was wrong**: the owner-supplied FOX theme file's own embedded metadata
  (`<meta name="canvas-width" content="2315">` etc.) gave the real authored size -- 2315x534, not
  the 1200x800 Session 80 guessed from a loading-thumbnail SVG viewBox.
- **Chroma key was hardcoded to magenta everywhere**, but the FOX file's own background is a
  genuine `#00B140` green (its embedded design-tool props literally declare
  `"greenScreen":true, "keyColor":"#00b140"`) -- magenta never matched anything on that page, so
  it rendered fully opaque. Made the key color per-theme (library.json's `chromaKeyHex`).
- **Real alpha-channel bug found in `ApplyChromaKey`** (this is the one fix worth remembering
  regardless of the Coffee-specific stuff): `CapturePreviewAsync`'s PNG of a fully-opaque page
  decodes into a GDI+ `Bitmap` with **no native alpha channel**. `LockBits(..., Format32bppArgb)`
  gives a temporary 32-bit view either way, but `UnlockBits` converts back to the bitmap's real
  underlying (alpha-less) format and silently discards whatever alpha the chroma-key pass wrote --
  confirmed live: a pixel exactly on the key color computed alpha=0 correctly, then read back as
  alpha=255 after Unlock. Fix is to force a real `Format32bppArgb` copy (`Graphics.DrawImageUnscaled`
  onto a fresh bitmap of that format) before locking/writing alpha. **This bug would silently
  break chroma-keying for ANY theme**, not just FOX -- worth re-applying if this feature comes
  back.
- **`window.updateScorebug` truthiness is not proof of a working binder.** Confirmed live: for a
  custom/HBCU matchup (Bethune-Cookman @ Florida A&M), the theme's own binder reported
  `{"bridgePresent":true,"applyResult":true}` on every push, yet the home team name stayed frozen
  on a static placeholder ("ILLINOIS"). Root-caused to (most likely) an internal roster/ALIAS
  lookup table inside Coffee's own theme code that doesn't recognize non-FBS school names and
  silently no-ops instead of erroring.
- **Extracted Coffee's real `theme-bridge.js`** from his shipped `app.asar` (via `npx
  @electron/asar extract-file`, found at `D:\COFFEE\resources\app.asar` and three other local
  installs) -- this is the actual generic DOM-binding mechanism his real app uses for every
  theme: auto-detects the scorebug root via `[data-cfb27-scorebug]` or a geometric heuristic
  (hardcoded reference dimensions of 371x433 -- literally tuned against the bundled ESPN 2013
  theme), maps `[data-cfb27-bind]` elements, and writes `.textContent`/image `src` directly with
  no name validation at all. Ported it in as an opt-in per-theme (`useGenericBridge`) fallback.
  It worked -- confirmed live, correct names/scores/clock/logos once the alpha bug above was also
  fixed -- but the underlying data feeding it (see next section) was the real blocker.
- **Found the canonical 5-theme set** (matching Session 78's "absorption" description, never
  actually populated locally) sitting in `D:\COFFEE\UserData\theme-library\`: ESPN 2020, FOX 2021,
  FOX 2025, NBC 2024, NBC 2024 Monochrome -- all real production exports (not raw design-tool
  files like the FOX one the owner had hand-supplied), all using `#00FF00` as their native chroma
  key, 3 of the 5 with genuine `data-cfb27-bind`/`updateScorebug` baked in.

**Why it got abandoned**: with the real ESPN 2020 theme live-tested in-game, the scorebug pipeline
itself worked (transparent, correct rendering) but showed frozen `0-0`/`0:00` while the actual
game was at Air Force 2-0, Q1 5:38. Traced to the RAM reader's own status message:
`"RAM export: automatic read-only locator is waiting to retry (cached scoreboard readable;
waiting for it to move (paused games stay here))"` -- the reader found candidate memory addresses
but was stuck waiting to confirm one of them was really the live value. A Stop/Start Watching
cycle (killing and relaunching the reader) didn't clear it; deleting the reader's own
`ram-live-profile-cache.json` to force a from-scratch scan also didn't get tried to conclusion
before the owner called it. **This is inside Coffee's closed-source `CollegeFB27RamReader.exe` --
not fixable from our side beyond retry/cache-clear tricks.**

Also updated (then reverted with everything else): swapped the bundled
`CollegeFB27RamReader.exe` for a newer copy (180KB -> 288KB) found alongside the owner's
`D:\Start Game Reader.cmd` -- same `--service seedPath statusPath pid` invocation contract, so
it was a safe drop-in if this is revisited.

**Currently in the working tree**: none of the above. `git status` shows only Session 80's
already-uncommitted files (`GameWatcher.cs`, `WebBridge.cs`, `ConfigStore.cs`,
`GameStateNormalizer.cs`, `RamReaderValidator.cs`, `ScoreboardReaderState.cs`, etc. -- the
freshness/play-clock work, still not live-tested) plus the separate pre-existing HBCU mode files
below. `Assets\ScoreboardReader\theme-library\library.json` is back to `"themes": []`.
`Assets\ScoreboardReader\CollegeFB27RamReader.exe` is back to the original bundled copy.

## Status: HBCU Mode -- Mid-Test, Not Owner's First Session On This

This wasn't started tonight -- `HbcuPlaybackService.cs` and `MarketplaceChatService.cs` were
already sitting untracked/uncommitted from an earlier, undocumented session (no prior handoff
mentions it by name). Picked up at the owner's request after abandoning the Coffee scorebug work,
with the owner testing it live against a real game with the native CFB27 scorebug/OCR path (not
the RAM reader) so both HBCU playback and something else could be watched at once.

**What's already wired** (confirmed by reading the code, not yet confirmed by a live play):
- `HbcuPlaybackService` -- continuous alternating-turn shuffle between two Team Pot queues (falls
  back to a school's downloaded pack, normalized on the way in, if the pot is empty), Fisher-Yates
  shuffle per refill, ~3s gap between tracks.
- `WebMainForm` wiring: built (not started) on `MarkGameStarted`/GAMETIME when
  `ConfigStore.LoadPlaybackMode() == PlaybackMode.Hbcu`; `.Start()` fires on kickoff;
  `.Pause()`/`.Resume()` (8s delayed resume) wrap Runout/Ready/Kickoff events so the shuffle
  doesn't race an assigned cue; `.OnTouchdown()` fades the non-scoring side's track while leaving
  the scoring side's track playing uninterrupted.
- `IsHbcuAllowedEvent` gates HBCU mode down to just Pregame Ready/Take the Field/Kickoff/Touchdown
  -- everything else is suppressed in favor of the continuous shuffle.
- UI already built in `index.html`/`app.js`: `#pill-hbcu-mode` global toggle (narrows team picker
  to SWAC/MEAC, matchup screen still shows every team), per-trigger "Is this for an HBCU?"
  checkbox (retargets the file into `ConfigStore.HbcuSchoolFolder`), and a Team Pot panel
  (`#hbcu-pot-panel`/`#hbcu-pot-list`) for unlimited add/remove songs per school, separate from the
  normal per-trigger assignment slots.

**Not yet done tonight**: an actual live play-through. Owner was mid-setup (HBCU Mode pill,
picking two schools to test) with the native CFB27 scorebug OCR path already running in parallel
when this session ended.

## Build & Test Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, confirmed after the
  full Coffee-work revert.
- No test suite run this session.
- Bandroom.exe relaunched multiple times during live Coffee-overlay testing; final relaunch (PID
  30660) is running with none of that work present, native OCR scorebug path active.

## Git

Not committed. Working tree is Session 80's already-uncommitted state
(`GameWatcher.cs`/`WebBridge.cs`/`ConfigStore.cs`/`GameStateNormalizer.cs`/
`RamReaderValidator.cs`/`ScoreboardReaderState.cs` -- freshness/play-clock fallback, still not
live-tested) plus the pre-existing untracked HBCU files (`HbcuPlaybackService.cs`,
`MarketplaceChatService.cs`) plus other untouched in-progress files noted in Session 80's own
handoff (`AudioPlayer.cs`, `TeamColors.cs`, `cloudflare/cloudflare-marketplace/worker.js`, etc.).
No release triggered this session.

## Options Discussed, Not Started

- Everything in Session 80's own "Options Discussed, Not Started" list is still true and
  untouched (`IsPlayClockCounting` non-sticky capture, score/quarter/timeouts freshness
  protection, `ResolveUserDataDirectory` candidate-ordering bug, Coffee's Corner skin-picker UI
  being gone, `ram.recentMessages`/`downDistanceKind`).
- If the Coffee scorebug overlay work is ever picked back up: the alpha-channel `ApplyChromaKey`
  bug, the per-theme `chromaKeyHex`, and the ported `theme-bridge.js` approach are all worth
  re-applying -- they worked. The blocker was purely the RAM reader's own address-locking getting
  stuck, which needs either patience (let it keep retrying through more live plays), a full game
  restart (not just Stop/Start Watching in BANDroom), or accepting it as a known unreliability of
  that closed-source reader.
- HBCU mode needs its first real live play-through -- nothing about the wiring above has been
  confirmed against an actual game yet, just read from the code.
