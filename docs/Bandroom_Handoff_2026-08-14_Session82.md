# Bandroom Handoff — August 14, 2026 — Session 82

Same idea as always: what happened, explained plain.

## Fixed: Remote Play Toggle Was Silently Dead

The matchup screen's Remote Play checkbox (`#matchup-remote-play` in `app.js`) called
`bridge.GetRemotePlayMode()`/`bridge.SetRemotePlayMode()` -- neither existed on `WebBridge.cs`
(Windows) or `MacWebBridge.cs` (Mac). The backing `ConfigStore.LoadRemotePlayModeEnabled`/
`WebMainForm.GetRemotePlayModeFromWeb` logic was all correct, just never wired to JS. Flipping the
toggle threw silently (console suppressed in this app) and never persisted --
`remote_play_mode.txt` had never even been created on this machine. Added the missing pass-through
methods on both platforms (Mac reads/writes `ConfigStore` directly, matching the pattern
`SetToastsEnabled` already uses for simple toggles, since there's no Mac `MainWindow` method for
this yet).

## Fixed: Ready Screen Had Forced Tunnel Reverb

`WebMainForm.cs`'s `FireEvent` was setting `isPregame = entry.Event.Contains("Pregame Ready", ...)`,
which routes the clip through `AudioPlayer`'s Tunnel bandpass/reverb/saturation treatment instead of
normal playback. Owner request: Ready screen should sound normal. Changed to always `false`.

## Fixed: Clipper Wouldn't Open When Trimming an Existing Pot Song

Clicking "Trim" on a song already sitting in a Team Pot (`openInlineTrimmerForHbcuPot`) set up all
the trim state and started decoding the waveform, but never un-hid the parent `#clipper-assign`
container -- that only happens inside `openClipperAssign` (the "+ Add Song" flow), which this path
never goes through. Added the same header-text/visibility setup `openClipperAssign` does, inline in
`openInlineTrimmerForHbcuPot`.

## HBCU Mode: Kickoff -> Pot Shuffle Timing, Iterated Live

Went through several revisions live against owner feedback:

1. First cut: pot shuffle started immediately on Opening Kickoff, same tick as the assigned kickoff
   song -- raced/overlapped it.
2. Added a fixed 20s delay from the kickoff EVENT firing -- still wrong, since the owner wanted the
   delay measured from the SONG ENDING, not the event firing.
3. `HbcuPlaybackService.Start(TimeSpan kickoffSongDuration)` now takes the kickoff song's own
   duration and waits `kickoffSongDuration + PostKickoffGap (20s)` -- so the first pot track lands
   20s after the kickoff song's real end.
4. **Real bug found here** (owner report, live: "it cut off the opening kickoff and started the
   autoplay"): the duration lookup had two problems --
   - Read `kickoffEntry.AudioFile`'s cached `.meta.json` duration only, ignoring
     `kickoffEntry.BigGameAudioFile` -- `FireEvent` actually plays the BigGame file when Big Game
     mode is active, so the duration being measured could be the wrong file entirely.
   - Fell back to `TimeSpan.Zero` when no cached duration existed, meaning "wait only 20s from the
     event firing" instead of "song length + 20s" -- cut off any kickoff song over 20s that simply
     hadn't been analyzed yet (which is how a `.meta.json` sidecar gets created in the first place).
   Fixed via new `WebMainForm.ResolveKickoffSongDuration`: checks Big Game file first like `FireEvent`
   does, and falls back to reading the real duration straight off the file (NAudio
   `AudioFileReader.TotalTime`, cheap -- no sample decode) instead of guessing zero.
5. Owner then asked for the SAME 20s gap between every pot-song transition, not just the one after
   kickoff -- `AdvanceGap` (previously 3s) is now 20s, matching `PostKickoffGap`.

## Fixed: "Opening Kickoff" Card Invisible in HBCU Mode

Owner reported "still have no opening kickoff event card" after I'd confirmed the EventKey was
correctly registered engine-side (`ConfigStore.AllEngineEventKeys`, `BuildDefault`). Root cause was
purely front-end: `app.js`'s `hbcuRelevantEvents` set (which HBCU mode filters the event-card list
down to) was missing `"Other: Opening Kickoff"`, despite its own comment claiming it matches
`WebMainForm.IsHbcuAllowedEvent` exactly -- that C# set already had it, the JS set didn't. Added it.

## New: HBCU Event Test Hook

Added an "HBCU mode" section to the existing Ctrl+Shift+T Event Test Hook: pick a possession side,
then "Fire Opening Kickoff" or "Fire Touchdown" -- both route through the REAL
`OnEngineEventsDetected` path (new `WebMainForm.FireTestEventHbcuFromWeb`), not a direct
`FireEventForSide` bypass like every other test-hook button, so `HbcuPlaybackService`'s
Start/Pause/Resume/OnTouchdown wiring is actually exercised without a live game.

**Bug caught immediately via live use**: the Touchdown test button fired `"Offense: Touchdown"`,
which isn't a real EventKey (`"Offense: Touchdown Scored"` is) -- so `ResolveEntryForEvent` always
returned null even with a real song assigned, logging "no song assigned" every time. Fixed the
button's EventKey string.

## New: HBCU Pot Dashboard ("\" Hotkey)

Owner request: a way to see the shuffle actually working without listening for it blind. New panel,
toggled with the `\` key (guarded against firing while typing in a text field), polls
`HbcuPlaybackService.GetStatus()` once a second while open:
- Status: Shuffling / Paused / "Waiting for kickoff song to end..." / Stopped
- Currently playing song + team
- Countdown to the next scheduled event (kickoff-delay or track-advance timer)
- Each side's queued-song count
- **Start** (fresh restart -- new `HbcuPlaybackService.Restart()` clears both queues so nothing
  already-played this game carries into the new shuffle, starts immediately with no kickoff-delay
  gate), **Pause**, **Resume**, **Stop** buttons

`HbcuPlaybackService.Status` is a new `readonly record struct` exposed via
`WebMainForm.GetHbcuPlaybackStatusFromWeb`/`PauseHbcuPlaybackFromWeb`/etc. and their `WebBridge.cs`
pass-throughs.

## New: Pot Songs Register in My Downloads

Owner request: a song added to a Team Pot via the Clipper's "+ Add Song" modal should also show up
in My Downloads, so it can be reused (assigned to a normal event card, shared to marketplace)
without re-importing. `ConfigStore.AddToHbcuPot` now also calls `RecordLocalTrack` for the file,
skipped if it's already a marketplace download or already has its own My Downloads entry (avoids
clobbering that entry's `Shared`/`Type`/`CreatedAt` fields -- see `LocalTrackEntry`'s own doc comment
on never mixing the two sources).

## Investigated, Not Resolved: "Nothing on the Scorebug Is Triggering"

Owner reported the CFB27 pill scorebug (native PC game, `CollegeFB27.exe` -- not a console/Remote
Play capture, confirmed by owner: "cfb exe") isn't triggering ANY scorebug-dependent event -- not
chevrons, not the fight song, nothing that reads the bug itself (side-agnostic events like Pregame
Ready still fire fine).

Ruled out this session:
- **Wrong app/window**: `CollegeFB27` is already in `GameWatcher.GameProcessNames`, confirmed
  running (PID 28424).
- **DPI scaling**: `Program.cs:98` already calls
  `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)`, so `GetWindowRect` returns true physical
  pixels, not virtualized/scaled ones.
- **Wrong crop coordinates for `CollegeFootball27` preset**: hand-measured the owner's own live
  screenshots (2560x1440, Bethune-Cookman @ Florida A&M, Cheez-It Citrus Bowl) against the active
  preset's fractional crops -- band Y, away/home score X, and clock X all measured within the
  currently-configured crop boxes. Doesn't rule out a few-px miss, but no gross mismatch found.
- **Focus loss**: owner says the game does stay focused; not what's happening here.

**Real culprit, caught by checking `ocr_debug.log` directly against `Get-Process`**: the currently
running `Bandroom.exe` (this session's latest rebuild, PID 24432, started 7:20 PM) had written
NOTHING to `ocr_debug.log` since launch -- last entry was 7:18 PM, from a PREVIOUS instance, before
this one even started. `CollegeFB27.exe` has been running since 6:04 PM. Since this session rebuilt
and relaunched Bandroom.exe roughly a dozen times (once per fix above), and each relaunch is a fresh
process needing GAMETIME/Start Watching pressed again for `GameWatcher` to start capturing at all --
it's very likely the owner was playing through/after one of those rebuild cycles without re-arming
Start Watching on the current instance, which would produce exactly this symptom (nothing fires,
across every region at once) with ZERO connection to crop calibration.

**Not yet confirmed**: asked the owner to check Bandroom is showing LIVE/actively watching right
now, re-press GAMETIME if not, play one live down, and I'll re-pull `ocr_debug.log` for real
`[down]`/`[situation]`/`[quarter]`/`[awayscore]`/`[homescore]`/`[clock]` OCR reads. If detection
still doesn't fire with Start Watching confirmed active, the crop coordinates become the next real
suspect and will need a fresh live screenshot batch (walkout/chevron, live down, KICKOFF text,
FLAG/penalty, TOUCHDOWN banner) to recalibrate against.

## Build & Test Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, after every change this
  session (rebuilt and relaunched Bandroom.exe roughly a dozen times across the session).
- `dotnet build src/Bandroom.Mac/Bandroom.Mac.csproj -c Debug` -- clean, checked once after the
  Remote Play bridge fix.
- `dotnet test src/Bandroom.Core.Tests` -- 104/104 passing, checked once early in the session.
- Live-tested by the owner throughout: Clipper trim-open fix, HBCU test hook (caught 2 real bugs --
  wrong EventKey, missing bridge wrappers), kickoff-timing revisions (caught 1 real bug -- BigGame
  file / zero-duration fallback). Pot dashboard and Start/My-Downloads-linking built but not yet
  live-confirmed by the owner as of this handoff.

## Git

Not committed. Working tree has this session's changes layered on top of Session 80/81's own
already-uncommitted state:
`AudioPlayer.cs`, `ConfigStore.cs`, `GameWatcher.cs`, `TeamColors.cs`, `WebBridge.cs`,
`WebMainForm.cs`, `cloudflare/cloudflare-marketplace/worker.js`, `dashboard_watchdog.log`,
`src/Bandroom.Core/GameStateNormalizer.cs`, `src/Bandroom.Core/RamReaderValidator.cs`,
`src/Bandroom.Core/ScoreboardReaderState.cs`, `src/Bandroom.Mac/Bandroom.Mac.csproj`,
`src/Bandroom.Mac/MacWebBridge.cs`, `wwwroot/app.js`, `wwwroot/index.html`, `wwwroot/style.css`
(modified) plus `HbcuPlaybackService.cs`, `MarketplaceChatService.cs` (untracked, pre-existing) and
three previous sessions' handoff docs (79/80/81, also untracked). No release triggered this session
(no "ppup").

## Options Discussed, Not Started

- **Waiting on**: owner confirming Start Watching is active on the CURRENT Bandroom instance, then a
  fresh live down, so `ocr_debug.log` can show real OCR reads instead of "window not focused"/stale
  noise -- this is the actual next step for the scorebug-triggering investigation.
- If detection still fails with watching confirmed active: recalibrate `ScorebugPreset.
  CollegeFootball27` from a fresh live screenshot batch (a normal down, KICKOFF text, FLAG/penalty,
  TOUCHDOWN banner, a possession change) -- explicitly requested by the owner as the fallback plan.
- `ChevronMarkerFx*` is still only ever calibrated from ONE Rose Bowl screenshot (2026-08-12) -- the
  owner's Cheez-It Citrus Bowl screenshot this session showed a visually similar but not
  pixel-identical chevron/logo layout (different bowl branding). Worth a dedicated recalibration
  pass once/if the bigger Start-Watching question above is resolved and this turns out to still be a
  real gap.
- Session 81's still-open items (Coffee scorebug overlay work, RAM reader address-locking
  unreliability) untouched again this session -- see that handoff.
