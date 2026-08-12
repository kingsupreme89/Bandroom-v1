# Bandroom Handoff — Session 50 (2026-08-11)

Continuation of Session 49, live-fire during a real game the owner was watching. Two real bugs
diagnosed and fixed from live reports; a long punch list of further owner asks was captured but
**not yet started** — see "Not done" at the bottom. Build clean (0 warnings/errors), 58/58 Core
tests passing as of the last edit.

## 1. Audio channel isolation: Home cue no longer cuts off Away cue

Owner report (live): "the home bg fired but it cut off the away bg def." Root cause: `AudioPlayer`
has one shared `ActiveOutputs` list with no concept of side — `Play(interruptPrevious: true)` always
called a global `StopAll()`. The existing same-tick multi-fire fix (`firedYet`/`otherFiredYet` in
`WebMainForm.OnEngineEventsDetected`) only protects events landing in the same batch; two
side-specific events landing on **separate** engine ticks (e.g. OCR flicker, or genuinely
sequential plays) each still called the old global `StopAll()` on interrupt, so whichever side
fired second always silenced the other regardless of side.

- `AudioPlayer.cs`: `ActiveOutputs` changed from `List<WaveOutEvent>` to
  `List<(WaveOutEvent Output, string? Channel)>`. New `StopChannel(string? channel)` — stops only
  outputs tagged with the matching channel; `channel: null` falls back to the old global `StopAll()`
  behavior (previews/UI chimes that never set a channel are unaffected). `Play()` gained a
  `channel` param, threaded through to `StopChannel` on `interruptPrevious`.
- `WebMainForm.cs`: `FireEvent`/`FireEventForSide` now pass `side` ("home"/"away") as the channel,
  so a same-side re-fire still cuts off that side's own leftover audio (unchanged behavior) but no
  longer touches the other side's currently-playing cue.
- Public `AudioPlayer.StopAll()` (used by the UI's "Stop All" button) is untouched — still stops
  everything, on purpose.

## 2. "1st Down After Punt" merged back into the regular first-down cue

Owner reports (live, multiple messages): a "1st Down After Punt (Home BG)" card played the WRONG
file (`Other_Opening Kickoff_4_norm_Song.wav` — an Opening Kickoff clip, not a first-down clip),
another instance showed "no song assigned, nothing played," and separately "the correct trigger
took forever to play which was supposed to be 1st on 1st." Owner's own diagnosis, confirmed
correct: "it was a home bg first down so that should just be a regular first down on 1st down" —
the split itself was the bug, not just a bad file assignment.

- Root cause: `DriveStarterHelper` (fires on the first snap of a brand-new possession, e.g. after a
  punt) was renamed to its own distinct card, `"Offense: 1st Down After Punt"` / `"Defense: After
  Punt"`, in a Session 49 audit pass — deliberately kept separate from `FirstDownHelper`'s
  `"Offense: Earned First Down"` (a mid-drive conversion) because the offense didn't technically
  "earn" a fresh-possession 1st down. In practice this just meant a second, easily-orphaned card
  that silently went unassigned or got a stray file dropped on it by Auto-Assign, while the
  drive's real "1st down" cue (whatever's assigned to "Offense: Earned First Down") never played at
  all for a fresh drive — explains both "wrong file" and "correct trigger took forever" (it wasn't
  slow, it just weirdly wasn't the event that fired).
- `DriveStarterHelper.cs`: offense branch now returns the SAME event key as an earned first down —
  `"Offense: Earned First Down Short"` (YardsToGo ≤ 5) or `"Offense: Earned First Down"` — instead
  of its own `"Offense: 1st Down After Punt"` key. Defense branch (`"Defense: After Punt"`)
  untouched — owner only flagged the offense/home side.
- `WebMainForm.RenamedEventKeyAliases`: dropped the now-retired `"Offense: 1st Down After Punt"` →
  `"Offense: Drive Starter"` mapping; added `"Offense: Earned First Down"` → `"Offense: 1st Down
  After Punt"` so if the regular first-down card has no song assigned, it falls back to whatever
  was already assigned under the old (now-retired) "1st Down After Punt" key rather than going
  silent. (Note: this is a single-hop fallback — anyone whose assignment survived from TWO renames
  back, under the original "Drive Starter" name, won't be picked up. Very unlikely to matter in
  practice.)

## Build/test status

- `dotnet build BandAudioHook.csproj` — clean, 0 warnings/errors. App was relaunched/killed twice
  mid-session for build-lock conflicts (PIDs 35004, 23812), same pattern as Session 49 — always via
  `taskkill` on the exact PID holding the file lock, then rebuilt clean.
- `dotnet test src/Bandroom.Core.Tests` — **58/58 passing** (same count as end of Session 49 — no
  new tests added yet for either fix; see "Real next steps").
- Not yet re-verified against a live game since these two fixes landed — both were diagnosed from
  live event-log screenshots, not reproduced+confirmed-fixed live.

## Not done — big punch list from this session, NONE started yet

Owner fired off a long rapid list of further asks (screenshots of the Assignment screen, Offense
card grid, a cut-off Share button, and more) that got captured in the todo list but not yet
investigated or coded:

1. **Blur/haze around event cards + pills on the Assignment ("Assigning songs for...") screen** —
   owner: "this isn't supposed to be blurred." This is the NON-Game-Day assignment/Sound-Bank
   screen — Session 49 item 6 only stripped `.glass` blur in `body.gameday-mode`, this is a
   different (undocked) screen entirely. Needs its own investigation into which `.glass`-class
   panels are in play here (`#situations-panel` etc. outside gameday-mode) and whether the fix is
   the same "solid dark background instead of blur" pattern or something narrower.
2. **Event naming**: "Stopped Them on 4th" card — owner said "this should also be named Stop On 3rd
   Down," implying a matching 3rd-down "stop" card should exist/be named consistently. Needs
   clarifying which EventKey(s) this refers to and whether a 3rd-down equivalent card already
   exists under a different name.
3. **Kickoff-after-PAT misclassification**: owner reported a kickoff that followed a PAT (extra
   point) got logged/played as "Opening Kickoff" instead of a regular kickoff. `KickoffHelper.cs`
   needs a look — likely missing a "was this actually the FIRST kickoff of the game" check versus
   any kickoff following a score.
4. **"Why do we have doublers of some downs?"** — Offense screenshot showed what look like
   duplicate-ish down cards (e.g. both "1st Down" and "1st Down (1st & 10)", "2nd Down" appearing
   near "2-Point Conversion Good" etc. in a way owner read as duplicated). Needs a pass over the
   full Offense category's EventKey list to check for genuine duplicates vs. just similarly-named
   distinct cards (short/long split, big-gain variants, etc.) that read as redundant in the UI.
5. **Autosave**: app needs to save all profiles/assignments automatically on every change instead
   of relying on a manual save action (if one currently exists/is required — needs verifying
   current save-trigger behav8ior first).
6. **Speed toggle should also affect the lead-in whistle** — per `AudioPlayer.Play`'s own doc
   comment the SoundTouch speed-up (Session 49 item 1) is already applied "after the whistle is
   sequenced in so both speed up together" — this ask may already be satisfied; needs verifying
   against actual behavior (owner may be seeing a case where it doesn't, e.g. AltWhistlePath /
   per-event override path) before assuming it's a real gap.
7. **Sound settings (volumes etc.) must persist globally across relaunch** — needs checking
   whether `AudioPlayer.MasterVolume`/`HomeVolume`/`AwayVolume`/`PaVolume`/etc. are currently
   written to `ConfigStore` on change or only held in memory.
8. **Universal/master volume up 20%** — straightforward default-value bump once the right constant
   is located (`AudioPlayer.MasterVolume` default or wherever it's initialized from
   `ConfigStore`).
9. **Share button unreachable when a song is the TOP row** of the list (screenshot shows the
   button clipped off above the visible scroll area) — owner: convert to "a quick popup modal with
   the same feature because that was working" — i.e. reuse whatever modal pattern the Session 48
   cross-team Share feature already uses instead of the current inline/positioned popover that
   clips at the list edge.

## Real next steps

1. Work the 9-item punch list above in order (owner didn't rank them, but #1/#3/#4/#9 read as
   active annoyances mid-game; #5/#7/#8 are straightforward once located; #2/#6 need a clarifying
   look before coding anything).
2. Verify both Session 50 fixes (channel isolation, first-down merge) against a real live game —
   neither has been confirmed fixed in actual play yet, only diagnosed from event-log screenshots
   and unit-tested indirectly (no NEW tests were added for either).
3. Consider adding a real test for the channel-isolation fix (e.g. fire two events on different
   "ticks" for opposite sides via the existing `FireTestEventPairFromWeb`-style hook and assert
   both remain audible) and for the first-down merge (assert `DriveStarterHelper` now returns
   `"Offense: Earned First Down"`/`"...Short"` instead of the retired key).
4. Nothing from Sessions 46–50 has been released via `release.ps1` yet.
