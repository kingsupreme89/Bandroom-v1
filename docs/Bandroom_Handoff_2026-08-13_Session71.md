# Bandroom Handoff — August 13, 2026 — Session 71

Same idea as always: what happened, explained plain.

## Real Bug Found and Fixed: Wrong OCR Preset Active

While chasing a "white box / false data" report on the scorebug overlay, found the active OCR
scorebug preset (`C:\Users\Fresh\AppData\Local\Bandroom\UserData\scorebug_preset.txt`) was set to
**"College Football 26 Console"** while the owner is running **College Football 27** on PC
(confirmed via the RAM reader's own status file: process name `CollegeFB27.exe`). OCR was reading
crop regions calibrated for an entirely different game's HUD layout — this alone explains stale/
wrong data independent of anything RAM-related.

**Fixed live** via the Settings preset dropdown (`WebMainForm.SetScorebugPresetFromWeb`,
`WebMainForm.cs:1418`, which updates `_watcher.ActivePreset` immediately, no restart needed) —
switched to "College Football 27". Also overwrote `scorebug_preset.txt` directly as a backstop so
a future restart also boots into the right preset. **Not yet visually re-confirmed live** — next
session (or later tonight) should open with a look at the actual overlay against the real score to
confirm OCR is now accurate.

## Session 70's NBC 2024 Transparency Fix — Still Not Actually Visually Verified

Session 70 claimed "confirmed working live for NBC 2024" based on `crash.log`'s `computedStyle`
diagnostic reporting `rgba(0,0,0,0)`. **Correction: that was never actually verified.** That
diagnostic only proves the page's CSS was transparent — not that the final composited image on
screen looked correct (no white fringe, no stuck-opaque frame, correct positioning). Owner
explicitly caught this ("i didnt verify nbc was fixed") — treat Session 70's transparency claim as
**unconfirmed** until someone actually looks at the overlay on screen. First thing to check next.

## RAM Reader Investigation — Root Cause Chain

Owner opted into RAM mode this session (per Session 70). Investigated why it was stuck showing
`awayScore:0, homeScore:0, quarter:0, clock:"0:00", down:0` (identical, unmoving values) in the
scorebug overlay payload despite the reader process being alive and attached to the right game
process.

**`ram-reader-status.json` showed the actual reason** (the old bundled exe, before today's
replacement — see below):
```
"message":"RAM export: automatic read-only locator is waiting to retry
(scanned 323 MB; scoreboard missing; timeout copies 0; catalog found; teams ?/?; live distance unique)"
```
The reader was attached to the correct process but its internal memory-scanning locator could
never find the live scoreboard structure. Ruled out several hypotheses in order, each with a live
check:
1. **Stale `ModData` (Frosty not rebuilt)** — ruled out; owner confirmed the mod was actually
   active (the game's native HUD scorebug visibly disappeared, which is exactly what the
   "invisible scorebug" mod does).
2. **Pause menu / no live play state** — tested both paused and mid-play with no change in the
   status message, so this wasn't it either (or wasn't the whole story).
3. **Dynasty-mode-only signature scope** — the bundled `ram-live-profile.json` sidecar (profile
   version 3) has `"scope": "automatic-read-only-signatures-v16-dynasty-clone-possession"`,
   suggesting the old exe's signature set may have been built specifically for Dynasty mode. Never
   confirmed either way which mode the owner was actually in — open question if the old exe is
   ever needed again.

**Important scope correction, made mid-session:** initially told the owner the RAM locator was
unfixable from our side because it's closed-source. Owner correctly pushed back — Bandroom
absorbed Coffee's RAM reader exe + invocation contract this same day (see
`Bandroom_Handoff_2026-08-13_ScoreboardReaderNativeAbsorption.md`), and separately we do have a
decompiled JS *wrapper* (not the RAM reader binary itself) from an earlier inspection session
(`Bandroom_Handoff_2026-08-13_ScoreboardReaderIntegration.md`) — but that extraction folder was
already deleted as scratch, and even that JS wrapper was never the actual memory-locator logic
(that's compiled into the closed-source `.exe`, always was). So the specific claim "we can't patch
this" was and remains accurate, but it was worth the owner's correction — don't assume something
is unfixable without first checking what source access actually exists.

## New: Standalone RAM Reader v1.4.1 Swapped In

Owner supplied a newer standalone build of the reader, sourced directly from Coffee's
`Scorebug-Overlay-App` GitHub releases: `CFB27-Game-Reader-v1.4.1.zip`. **Confirmed genuinely
different binary** (old bundled exe: SHA256 `C71C32C6...`, 280,064 bytes; new: SHA256
`EF5409AE...`, 283,136 bytes — different hash and size, not a repackage of the same file).

Its bundled `README.txt` is a real upgrade in documentation quality over anything seen before:
- **No mention of the "invisible scorebug" mod at all** — states it "needs no install, no
  calibration, and no configuration." The mod requirement flagged as an open gap in Session 70's
  handoff (name/link/instructions never obtained) may simply not apply to this version.
- **No Dynasty-mode restriction mentioned** — unlike the old profile's `...dynasty-clone-
  possession` scope string, this README describes universal operation.
- Explicitly documents needing a few seconds of **live, unpaused gameplay (clock ticking)** before
  it will publish anything — "by design, it proves what it found before it publishes." This may
  fully explain why earlier tests (pause menu, or tests too soon after attach) never produced
  output, independent of the mod/mode questions above.
- Same output contract Bandroom already expects: writes `live-game-data.json`, same `--service
  <seedPath> <statusPath> <ownPid>` invocation (`ScoreboardReaderHost.cs` needs zero code changes
  for this swap).
- Documents a `discovery` field in the JSON explaining in plain English *why* a specific field
  isn't reading yet — worth surfacing in Coffee's Corner or the event log if RAM mode stays on
  long-term, currently unused by our normalizer.
- Documents per-field `*Source` keys (`"ram"` vs `"ram-cached"` vs `"ram-pending"`) distinguishing
  real values from placeholders — `RamReaderValidator`/`GameStateNormalizer` should be checked
  against this if not already handling it (not verified this session).

**Action taken:** killed the old running `CollegeFB27RamReader.exe` process, copied the new exe
over both `Assets\ScoreboardReader\CollegeFB27RamReader.exe` (source) and
`bin\Debug\net10.0-windows10.0.19041.0\Assets\ScoreboardReader\CollegeFB27RamReader.exe` (the
currently-running build's copy) — confirmed by hash match after copy. `bin\Release\...` doesn't
exist yet in this tree (fine, app is running Debug).

**NOT yet done / next steps for whoever picks this up:**
1. Owner was mid-instructed to disable the invisible-scorebug mod (v1.4.1 shouldn't need it) and
   click Stop Watching → Start Watching (or GAMETIME) in BANDroom to relaunch the RAM reader
   fresh against the new exe (`ScoreboardReaderHost.TryStartRamReader` only auto-launches from
   `WebMainForm.cs:1299`/`StartWatchingFromWeb`, not on its own).
2. Get into a real live, unpaused down (clock ticking) and check `live-game-data.json`/
   `reader-status.json` in `C:\Users\Fresh\AppData\Local\Bandroom\UserData\ScoreboardReader\` for
   real (non-null) field values.
3. If it works: confirm `RamReaderValidator.cs`/`GameStateNormalizer.cs` correctly parse this
   version's schema (README's field list/naming looks consistent with `ScoreboardReaderState.cs`
   but wasn't diffed line-by-line this session).
4. If it still says "scoreboard missing": the mod-requirement and mode-requirement hypotheses are
   now both weakened by this README, so the next thing to question is whether the *game version*
   (`gameExeVersion "1, 0, 0, 0"`, `moduleSize 299,429,888`) matches what v1.4.1 was actually built
   against — ask Coffee directly with the exact `reader-status.json` message if so.
5. Re-verify Session 70's NBC 2024 transparency claim visually (see above) — still open.

## Options Discussed, Not Yet Started

Owner asked broader "how do we make this stronger" questions this session. Laid out (not
implemented):
- **Bundle Coffee's own screen/OCR reader mode** (`scoreboard-data-source.js`'s `screen` mode from
  his Electron app — richer than raw pixel-crop OCR: team identity/colors/records, no anti-cheat
  exposure) instead of/alongside the RAM path. Never attempted — would mean bundling a Node/
  Electron process, a bigger lift than the RAM exe was.
- **Building yard-line OCR ourselves** — `PlaySnapshot.YardLine` is hardcoded `0` in
  `GameWatcher.RouteEngineTick`, disabling red-zone/field-position evaluators; only unblocks today
  when a reader (RAM or Coffee's screen mode) is connected. Adding an OCR region for it directly
  in `GameWatcher.cs` would unblock it independent of any reader working. Not attempted.
- Explicitly **not** pursuing a fully homegrown memory reader (same anti-cheat risk profile as
  Coffee's, same per-patch maintenance burden, duplicates work Coffee already did) — recommended
  against, owner did not push back.
