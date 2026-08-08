# Bandroom Handoff — August 8, 2026 (Session 5)

Picks up immediately after Session 4 (same day). Session 4 ended believing "nothing plays" was
fixed by correcting `ConfirmGametimeFromWeb` to auto-start watching. It wasn't — that fix was real
but not sufficient. This session is the one that actually got live sound working in a real game,
through five layered bugs stacked on top of each other. **Owner confirmed live at the end of this
session: "OMG ITS WORKING."** Partial ("...SOME") — see Known Issues at the bottom for what's left.

**⚠️ BUILD STATE:** everything below is built and the running `Bandroom.exe` instance reflects all
of it as of the end of this session. `ocr_debug.log` (next to the exe) was cleared multiple times
during debugging — don't assume its start time reflects app launch time.

---

## The five bugs, in the order they were found (each hid the next one)

### 1. `FindGameWindow()` matched the wrong window by title substring
`GameWatcher.cs:786` (old) matched any visible window whose title contained "College Football 27".
**Frosty Mod Manager** (a mod tool) also has "(College Football 27™)" in its own title bar, and
`EnumWindows` doesn't guarantee it enumerates the real game first — so the watcher could silently
lock onto the mod manager's `hWnd` instead of the game's. Confirmed live: OCR was reading Claude
Code / VS Code text off-screen, not game HUD text, because the crop rect belonged to the wrong
window's screen-space coordinates.

**Fixed:** `FindGameWindow()` now matches by **process name** (`Process.GetProcessesByName
("CollegeFB27")` → `.MainWindowHandle`), confirmed live as the real game's process name via
`Get-Process`. Immune to any other app mentioning the game in its own title.

### 2. Capture method (`PrintWindow`) was silently blocked by anti-cheat
First attempted fix used `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` to capture the game's
content directly regardless of what's on top of it on screen (seemed like the "correct" fix for
window-overlap issues). It came back **blank every time** — EA's anti-cheat
(`EAAntiCheat.GameServiceLauncher.exe`, confirmed running) blocks direct window-content capture
APIs as a standard measure, and `PrintWindow` "succeeds" while rendering nothing, with no error.

**Fixed:** reverted to `Graphics.CopyFromScreen` (plain desktop pixel capture, not a window-content
API — not blocked). Its real constraint: it only captures useful content while the game window is
genuinely the foreground/focused window. Added `Native.GetForegroundWindow()` gating — if the game
isn't focused, the tick is skipped and logged (`"game window isn't focused/foreground..."`) instead
of silently reading garbage. **Practical implication for the owner: glancing at Bandroom's overlay
without clicking into it is fine (both windows visible in borderless fullscreen); clicking into
Bandroom pauses capture until you click back into the game, auto-resuming within ~0.5s.**

Also added (same investigation): `Native.IsIconic` check (minimized windows report a valid-looking
but garbage RECT and blank `PrintWindow`/capture — now explicitly logged and skipped) and a
non-positive rect-size guard, both previously silent.

### 3. `PlaySnapshot.Down` flickered to 0 on every blank OCR frame, breaking `WasFirstDown`
The "down" region's raw OCR crop is the *whole play-by-play ticker line*, which goes blank
constantly between plays (~30% of ticks in real captured logs). `region.Last` for this
un-gated region correctly resets to `null` on blank reads (intentional, for `DownChanged`
re-triggering) — but `RouteEngineTick()` was reading that same `region.Last` directly to build
`PlaySnapshot.Down`. Result: `Down` sequence looked like `3, 3, 0, 0, 0, 1, 0` instead of a clean
`3 → 1`. `PlayDelta.WasFirstDown` requires `current.Down==1` immediately after `previous.Down>1` —
with a `0` almost always landing in between, this edge was essentially never observed. **Proven
without needing a live game session**: replayed the exact OCR sequence from a real captured log
through the actual (unmodified) `Bandroom.Core` classes in a scratch console app — old logic never
produced `WasFirstDown=true` across the whole sequence; new logic did, correctly, on the real
transition. See `C:\Users\Fresh\AppData\Local\Temp\claude\...\scratchpad\downtest\Program.cs` if
this needs re-verifying later (throwaway, not part of the repo).

**Fixed:** added `GameWatcher._lastKnownDown`, updated whenever a non-null down value is read,
**never** nulled on blank frames. `RouteEngineTick()` now reads this instead of `region.Last`.

### 4. `DefenseHelper` fired every tick, not just on the down transition
Found while validating fix #3 live: `Defense: Second Down` fired 4x in under a second in the log.
`DefenseHelper.cs` (unlike `OffenseDownHelper`, which already checked `Current.Down !=
Previous.Down`) had no edge-trigger guard — fired on every tick the defended down stayed on
screen. Harmless while unassigned, would have spammed `AudioPlayer.Play` every ~0.5s once a song
was assigned.

**Fixed:** added the same `if (state.Current.Down == state.Previous.Down) return null;` guard
`OffenseDownHelper` already had.

### 5. Possession detection defaulted to the wrong color-match method, on a mistargeted crop
`ScorebugPreset.KamsCbsScorebug` is the **default active preset** (no persisted override picks the
newer `KamsCbsScorebugV3`'s underline-brightness method instead). It has no underline calibration,
so `GameWatcher.SamplePossessionFromWindow` fell back to the old team-color average-match method
(`PossessionFx*` crop) — landing on the yardline/down-distance **text** area, not reliably on any
team-colored fill, and even where it did, Auburn (navy) vs Tennessee (orange) — fine — but the
crop itself wasn't well-aimed. Owner then supplied a tightly-cropped live scorebug screenshot
showing the actual mechanism: **the rightmost down-and-distance box is filled solid with whichever
team is currently on offense's color** (confirmed across two more screenshots: orange box on
"4th & 6" with Tennessee driving, navy box on the next "1st & 10" after what was almost certainly
a punt to Auburn) — a far more reliable signal than the near-identical-hue underline dashes.

**Fixed:** recalibrated `KamsCbsScorebug.PossessionFx*` (`FxX=0.89, FxY=0.848, FxW=0.095,
FxH=0.104`) to target that box directly, inset from the rounded corners.

**Edge case caught by the owner before it shipped as a live bug**: that same box turns **bright
yellow** for "FLAG" during a penalty review — and Tennessee's own primary color (orange) sits
close enough to yellow in RGB space that `ResolveTeamColor`'s 90-unit match tolerance could have
misread a penalty as "Tennessee has the ball." **Fixed:** possession color-sampling is now skipped
entirely whenever the "flag" region's last read is non-null (one-tick-stale check, negligible at a
250ms poll interval).

---

## Also fixed along the way (smaller, real bugs found during debugging)

- **Test hook dropdown showed duplicate labels.** `EVENT_FRIENDLY_NAMES` collapses several
  distinct raw EventKeys to the same display text (e.g. `"Offense: Second Down"` and `"Defense:
  Second Down"` both show as `"2nd Down"`) — made it impossible to tell which one a test actually
  fired, and repeatedly produced false "unassigned" reports during this session's debugging.
  **Fixed:** `app.js`'s test-hook dropdown now shows raw EventKeys (it's a debug tool; unambiguous
  by design), not the friendly names used everywhere else in the app.
- **No visibility into real (non-test-hook) event firing at all.** `OnLog` (Session 4's fix)
  captured OCR region reads, but nothing ever logged whether `OnEngineEventsDetected` →
  `FireEventForSide` actually succeeded for a real game event. Every debugging step this session
  before this fix was pure guesswork from OCR text alone. **Fixed:** `OnEngineEventsDetected` now
  logs `[engine] {EventKey} -> {side}: {result}` for every real firing attempt (fired / unassigned
  / file-missing / no-profile / blocked), same result vocabulary the test hook already used.

---

## Known issue: away team's own offense songs are structurally unreachable

`OffenseDownHelper`/`FirstDownHelper` gate on `GameState.UserHasPossession`, which is anchored to
`UserIsHome` (hardcoded `true` — the app always treats "home" as the user's own team). This means
`"Offense: *"` events only ever fire for the **home** team, regardless of who actually has the
ball. When Auburn (away) drives, only `"Defense: *"` events fire (for home, reacting to the
opponent) — Auburn's own assigned songs (e.g. their "2nd Down" → `AUDIO 3 (2).mp3`) can currently
**never** fire through the engine. The old `down:1st/2nd/3rd/4th` legacy triggers *could* reach
them via `OnDownChanged`, but that handler is dead code (`if (_useEngineForEvents) return;`, and
`_useEngineForEvents` is always `true`). Not fixed this session — flagged as a real design gap, not
touched since it's a bigger scope decision (does the away team's own offense get its own music at
all, or is this app deliberately "cheer for your team, react to theirs" only?).

---

## What "SOME" still means — untested/unconfirmed as of session end

- Owner confirmed it's working but qualified it ("...SOME") — did not specify what's still
  failing. **Next session: ask what specifically still isn't firing/playing before assuming
  everything above is fully solved.**
- The recalibrated `PossessionFx*` box (bug #5) was fixed via reasoning from screenshots, not
  confirmed via a live `[possession] now: home (...)` log line during an actual Tennessee
  possession — worth grepping `ocr_debug.log` for that early in the next session.
- Away-side testing (bug in "Known issue" above) means only home-team assignments have been
  validated as reachable at all this session.
