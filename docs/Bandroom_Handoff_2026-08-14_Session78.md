# Bandroom Handoff — August 14, 2026 — Session 78

Same idea as always: what happened, explained plain.

## New: Home/Away Take the Field Hotkeys + Per-Event No Fade

Owner request: a manual way to fire the pregame "Take the Field" song per side (on top of the
existing automatic READY/black-screen timer), plus a per-song "never fade" option.

- **Hotkeys**: `]` manually fires "Other: Pregame Take the Field" for Home, `[` for Away.
  OS-global (work even if Bandroom isn't focused), same low-level keyboard hook mechanism as the
  existing Right-Ctrl cutoff key. New in `Native.cs` (`VK_OEM_4`/`VK_OEM_6`), `KeyboardHook.cs`
  (plain, unmodified key handling -- no Ctrl/Shift/Alt needed), `WebMainForm.cs`'s `OnKeyCombo`/
  `ManualFireTakeTheField`. Firing one calls `GameWatcher.MarkPregameTakeFieldFiredManually()` so
  the automatic timer (armed or not) can't also fire and double the song. Bypasses
  `AudioPlayer.FireCooldown` (`bypassCooldown: true`) -- owner explicitly wants to be able to
  replay the hotkey song as many times as they want, no throttling. Documented in the in-app
  hotkey panel (`app.js`'s `HOTKEYS` array), display-only entries since these are OS-global not a
  JS keydown.
  - Caveat flagged to owner, not yet acted on: `]`/`[` are unmodified and OS-global, so typing a
    literal bracket into any other app (Discord, etc.) while Bandroom is running will also fire
    the hotkey. Owner hasn't asked for a modifier key yet.
- **No Fade**: new `TriggerEntry.NoFade` bool (default false). When true, that card's clip always
  plays straight through with no fade, overriding both its own `FadeStartSecondsOverride`/
  `FadeOutDurationOverride` and the global Audio Timing fade settings. Threaded through
  `AudioPlayer.Play`'s new `noFade` param (short-circuits by setting `effectiveFadeStart =
  double.MaxValue`), both `FireEvent`/`PreviewEventFromWeb` call sites, `WebBridge`
  serialization/setter (`SetEventNoFade`), and a new checkbox in the event card's settings
  popover (`app.js`, folded into the gear "active" state check alongside the other overrides).

## Fixed: Two Real Bugs Found While Testing the Above

**Bug 1 -- Audio Timing "Pregame runout delay" silently discarded on save.** The Sound Booth's
Audio Timing panel read `PregameRunoutDelaySeconds` into its input on load but never included it
in the object it POSTs back on "Apply Timing" (`app.js`) -- every click silently reset the saved
delay back to whatever it already was, so an owner edit to that field never actually persisted.
Fixed by adding it to the save payload, clamped 15-45 same as the input's own min/max.

**Bug 2 -- "no song assigned" for a song that WAS assigned.** Owner reported (with a screenshot)
that Home kept logging "Pregame Take the Field (Home BG) -- no song assigned, nothing played"
despite a song visibly assigned via Assign/Edit. Traced by reading the actual profile JSON files
on disk (`LSU · Home.json` vs `LSU.json`): the song was correctly assigned, but it landed in the
team's **plain base profile** (`LSU.json`, saved while no Home/Away/BigGame preset tab was
selected), not in `LSU · Home.json` -- and `GameplayProfileKey`/`LoadGameplayProfile` always
prefers a preset file over the base profile once that preset file exists at all, so gameplay never
looked at the base profile's copy. Same class of bug as an earlier session's home/away routing
fix, one layer up: the assignment landed in the wrong **file**, not the wrong **side**. Fixed in
`ResolveEntryForEvent` (`WebMainForm.cs`) -- now falls back to the team's base profile (if one
exists) before the cross-team Generic pack, when the active preset has nothing for that event.

## Fixed: RAM Reader Feeding Stale Down/Distance/Possession Into Live Routing

Owner report, live, with a screenshot of the Event Log: repeated `(RAM/OCR watchdog) RAM is
primary but disagrees with OCR` entries -- `down RAM=1 OCR=2`, `distance RAM=0 OCR=7`, `possession
RAM=home OCR=away` -- while the owner confirmed the real game state was "2nd & 7" (matching OCR,
not RAM). Since 2026-08-13 RAM is treated as authoritative for real event routing whenever
"connected"; this watchdog log was diagnostic-only and never corrected anything, so the game was
actually being routed off stale RAM data -- wrong down/distance evaluators, and Offense/Defense
audio potentially firing for the wrong side since possession was flipped.

Root cause: `down`/`quarter` only fall back to OCR when RAM reports exactly `0`
(`GameWatcher.cs`). That distinguishes "RAM never resolved this field" from "RAM resolved it once,
then correctly moved on" -- but not from "RAM resolved it once, its locator then silently broke
mid-game, and it's now frozen re-reporting that same stale value forever." A narrower version of
the exact staleness class the 2026-08-14 `-1`/`HavePossession` sentinel fixes already covered for
score/yardsToGo/timeouts/first-touch possession; down/quarter just never got the same treatment.

Fix (`GameWatcher.cs`) -- a two-sided staleness check, not a flat timer, specifically to avoid
false positives: a real down/distance legitimately holds the same value for 20-40s between plays,
so "RAM unchanged for N seconds" alone would misfire on a perfectly healthy game constantly.
Requires **both**:
- RAM frozen on one exact value for >= 12s (`RamFieldStaleThreshold`), AND
- OCR independently **settled** on a different value for >= 3s straight (`OcrFieldCorroborationWindow`)
  -- a single noisy OCR digit misread resets this clock the instant the value changes again, so
  flickering/inconsistent OCR can never trigger a fallback on its own.

Only when both hold does that one field (down, distance, or possession -- scoped to exactly the
three fields observed stuck live; score/timeouts/yard line already have their own protection and
weren't reported broken) fall back to OCR for that tick, same as if RAM had never resolved it.
Self-heals the instant RAM's value changes again. New `IsFieldStableFor<T>` generic helper backs
both the RAM-frozen and OCR-settled checks; new tracking fields reset in `Start()` alongside the
other per-game sticky state so nothing bleeds into the next game.

## Build & Test Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, throughout the session
  (multiple rebuilds; each required killing the previously-launched `Bandroom.exe` first since the
  live-testing loop kept it running and locking the exe).
- `dotnet test src/Bandroom.Core.Tests` -- all 104 existing tests pass, no regressions from the RAM
  staleness fallback or any other change this session.
- App relaunched live for the owner after each fix; owner confirmed the profile-fallback fix
  directly by testing (screenshot showed the mismatch), RAM staleness fix built and shipped in
  response to a live in-game report but not yet separately confirmed fixed live as of this
  handoff (the game was still in progress).

## Git

Not yet committed -- all changes (`AudioPlayer.cs`, `GameWatcher.cs`, `KeyboardHook.cs`,
`Native.cs`, `TriggerEntry.cs`, `WebBridge.cs`, `WebMainForm.cs`, `wwwroot/app.js`) are uncommitted
local changes. No release triggered this session (no "ppup").

## Options Discussed, Not Started

- `]`/`[` hotkey collision risk (unmodified, OS-global, could fire while typing a bracket in
  another app) -- flagged to owner, no modifier-key change requested yet.
- Owner hadn't yet confirmed the RAM staleness fallback resolved the live game's routing as of
  this handoff -- the fix shipped and rebuilt/relaunched mid-game, worth a live confirmation next
  session.
- Session 77's open items (Mac audio engine, Sparkle auto-update, icon-crop batch pass) weren't
  touched this session -- still open, see that handoff for detail.
