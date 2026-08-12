# Bandroom Handoff — Session 46 (2026-08-11)

Live-fire triage session — the owner was actively testing while this session worked, reporting bugs
in real time off Event Log screenshots. Shipped mid-session as **v1.0.76**, then kept fixing after
that release landed. This doc covers everything, including the post-release round not yet shipped.

## 1. Possession-flip false turnovers/misrouted events (shipped in v1.0.76)

Root cause: `GameWatcher.SamplePossessionByUnderline`/`SamplePossession` committed a possession
flip and fired `PossessionChanged` off a single frame's brightness read, unlike every other
OCR-derived field in the file (down/score/quarter), which already requires the same value twice in
a row. Added `ConfirmPossessionFlip` (2-consecutive-tick confirm, same shape as
`CommitValueIfConfirmed`) plus a post-commit cooldown (`_possessionCooldownUntil`) so a confirmed
flip can't immediately re-flip. Verified via 4 parallel auditor passes per the owner's explicit ask;
findings fixed: `_awaitingPostKickoffSnap` read-before-update ordering, structural-turnover backstop
needing `down == 1` + a 2-tick sustain (single-tick alignment of independently-timed OCR signals was
producing false positives), and `DriveStarterHelper` double-firing on every kickoff (not just the
opening one).

## 2. Sound Start Delay removed entirely (shipped in v1.0.76)

Owner's explicit call — stripped `SoundStartDelayMs` and its staleness-guard machinery
(`_soundFireGeneration`) from `WebMainForm.FireEvent`, `ConfigStore.AudioSettings`, `index.html`,
`app.js`. Back to synchronous `AudioPlayer.Play()` on fire, same as before that feature existed.
**Mac (`src/Bandroom.Mac/MainWindow.axaml.cs`) is left with a dangling reference** to the now-deleted
`ConfigStore.AudioSettings.SoundStartDelayMs` field — owner explicitly said "mac is later," so the
Mac project currently will not compile. Deferred on purpose, not an oversight.

## 3. Game Day fullscreen layout + Last Matchup pill (shipped in v1.0.76)

Per an approved plan: `gameday-mode` body class toggled on GAMETIME, docks Sound Booth to the right
edge (replaces the old centered-modal-with-scrim behavior) and hides the now-redundant
`#adjust-panel`. VS-styled header reuses `#matchup-side-bar` with team logos + switch arrows added.
**Not yet live-verified** — needs the owner to actually press GAMETIME and confirm the layout while
watching. Last Matchup pill: `ConfigStore.SaveLastMatchup`/`LoadLastMatchup` (same shape as
`BigGameSettings`), a pill on the matchup dialog showing "Last: Away @ Home," click re-centers both
coverflows on that pair. Fully wired end to end this session.

## 4. Live bug: "Earned First Down" firing after a punt (2 rounds)

**Round 1** (shipped in v1.0.76): `FirstDownHelper` used a plain `!NewPossession` guard, which
raced GameWatcher's now-2-tick-debounced possession commit — on the tick Down resets to 1 after a
punt, `NewPossession` could still read false. Added a buffered wait
(`_awaitingFourthDownPossessionConfirm`) specifically for the `Previous.Down == 4` ambiguous case
(could be a real conversion or a punt), abandoning the pending fire if `NewPossession` resolves true
within the window.

**Round 2** (post-release, not yet shipped): the bug recurred live. Root cause: the buffer's
timeout was only 3 ticks (~750ms), but GameWatcher's real possession-commit path can take up to
~2.25s worst case (2-tick confirm + a 2-second post-commit cooldown that can still be running from
the *prior* possession event, which a punt's own flip routinely lands inside). Widened to 12 ticks
(3s) to safely outlast it — then the owner said the resulting delay felt too slow, on 2nd downs too
(the *shared* `DownDistanceBuffer` all down-classification evaluators use, not just this one).
**Rebalanced all three related constants together** rather than independently:
- `GameWatcher.Cooldown`: 2.0s → 1.2s.
- `DownDistanceBuffer`'s default `maxPendingTicks`: 3 → 2 (~500ms), affects every buffered
  evaluator (`TflHelper`, `OffenseDownHelper`, `DefenseHelper`, `BigEventHelper`,
  `DefenseThirdDownShortHelper`) uniformly.
- `FirstDownHelper.MaxPendingTicks`: 12 → 7 (~1750ms), recalibrated to the *new*, shorter cooldown's
  worst case, not an independent number — the comment on it says so explicitly so a future session
  doesn't retune one without the other.

Two existing xunit tests (`TflHelper_TimesOut_...`, `BigEventHelper_FourthDownLoss_...`) hard-coded
"4 more ticks" against the old default of 3; updated to 3 ticks against the new default of 2.
47/47 still passing.

## 5. Live bug: touchdown right after a turnover cutting the turnover cue off

Owner report: a fumble, then a score shortly after — the turnover cue got hard-stopped mid-clip by
the touchdown cue's `interruptPrevious: true` → `StopAll()`, even though both are already flagged
`isHighPriority` specifically so OTHER audio should duck out of their way, not get stopped by each
other. Fixed in `WebMainForm.FireEvent`: tracks `_lastHighPriorityFireUtc`; a second high-priority
fire (Touchdown/Turnover/Safety) within a 6-second grace window skips the interrupt and layers/ducks
instead, same trick the same-tick multi-fire case already uses.

Also did a full audit for double-fires per the owner's ask: checked every evaluator's `EventKey`
output for cross-file collisions. Only one (`Defense: Safety`, from `SafetyHelper` and
`FieldGoalPATHelper`) and it's already correctly guarded (`possessingSideGained2` distinguishes a
real 2-point conversion from a safety's score delta). No new double-fire bugs found.

## 6. PA/Home/Away preview volume not tracking its own slider live (shipped in v1.0.76)

Real bug, not a UI glitch: `AudioPlayer.Play`'s preview loop hard-coded `MasterVolume` as the value
it re-read every tick for **any** `isPreview: true` call, regardless of what the clip actually
started at. A PA preview (started at `PaVolume`) silently tracked Master's level the whole time and
ignored the PA slider entirely — explains both "dragging the slider does nothing live" and "volume
is too loud for the level it's set at" (Master is typically higher than PA/Away). Added an optional
`Func<float>? liveVolumeSource` parameter; `PreviewEventFromWeb`'s PA call now passes
`() => AudioPlayer.PaVolume * eventVolumeScale` instead of relying on the old blanket-Master
fallback. Owner confirmed after rebuild: "i see npw that s why i though audio effects werent owkring
... major to peopl."

## 7. Reverb: Dome and Rain removed, remaining presets tightened (shipped in v1.0.76)

Owner call — both read as washy/muddy. Removed `ReverbPreset.Dome`/`StadiumRain` from the enum
entirely (not just hidden from UI); `WebMainForm.SetReverbFromWeb` falls to `Off` for either legacy
saved key so an old profile doesn't crash. Retuned the remaining three (`Stadium`, `NightGame`,
`NightGamePrimeTime`) — smaller room size, roughly half the wet mix — so trigger clips don't wash
out on back-to-back fires.

## 8. Live bug: repeating Turnover/Drive Starter loop while CFB27 was paused

Owner screenshot showed an alternating Home/Away "Turnover Forced" → "Drive Starter" loop, several
cycles in a row — impossible in real football. The screenshot showed CFB27's own pause menu on
screen the whole time. Root cause: `GetForegroundWindow() != hwnd` (the existing "skip capture if
some other app has focus" guard) doesn't help here — CFB27's pause menu keeps the game window itself
focused, so capture kept running against pause-menu pixels. The possession-underline crop in
particular read ambiguous pause-menu content each cycle and flip-flopped left/right; each flip
looked like a real structural turnover (down==1, not a kickoff) to `RouteEngineTick`, which then
fired `DriveStarterHelper`'s cue for the "new" drive too — repeating for as long as the pause stayed
up.

Fixed with a general frozen-frame detector, not text-matching for "PAUSED" specifically (more
robust — catches any paused/menu/loading screen, not just that one word): `UpdateFrozenFrameState`
hashes a coarse 24×14 pixel-sample grid of the *entire* captured frame every tick (not just the
scorebug crop). Real gameplay always has some motion somewhere on screen, even during a pre-snap
huddle; a truly static hash for ~1 second (`FrozenFrameTicksThreshold = 4` ticks) can only mean the
display has actually stopped updating. `RouteEngineTick()` — and therefore all event firing — is
skipped entirely while `_frameIsFrozen`, resuming the instant the frame starts changing again.

## 9. Live bug: "random" playback delays (post-release, not yet shipped)

Owner report was vague ("random events are delayed") until narrowed down: `StartWatchingIfMatchupSet`
only ever calls `AudioCache.Preload()` once, at GAMETIME. Any song assigned or re-assigned to
Home/Away **after** that (exactly what live tweaking mid-game is) never gets warmed into RAM, so its
first real fire falls through to `CachedAudioSource`'s cold synchronous disk-read fallback — a real,
audible stall, but only on whichever specific card was just touched, which is why it read as random
rather than consistent. Fixed in `RefreshHomeAwayConfigIfNeeded` (the method that already re-syncs
`_homeConfig`/`_awayConfig` after every profile save): now also fires a background
`Task.Run(() => AudioCache.Preload(...))` covering the current Home+Away config whenever a save
touches either team currently in the live matchup.

## 10. Investigated, found to already be correct — no change needed

Owner reported "Third Down (Home)" firing on a real 3rd-and-long as if it shouldn't. Traced through:
classification was correct (owner confirmed "it was third and long"), and `Defense: Third Down` is
already in `HomeOnlyAlwaysEventKeys` — home-only, always, Big Game or not, Away can never get it.
Nothing to fix; told the owner plainly rather than guessing at a change to make.

## Build/test status

- `dotnet test src/Bandroom.Core.Tests` — 47/47 passing after every round of changes this session.
- `dotnet build BandAudioHook.csproj -c Debug` — clean throughout; repeatedly blocked by the
  owner's own live-test process holding the exe file lock (expected during live-fire testing, not a
  real error — always confirmed via `AskUserQuestion` before proceeding, never force-killed).
- Section 9's fix (cache pre-warm on mid-game reassignment) has NOT been shipped yet — it's sitting
  in the working tree as of this doc, built and test-verified but not released via `release.ps1`.

## Shipped as v1.0.76

Sections 1–3, 6–8 (first round of fixes) went out via the owner's "ppup"/`release.ps1` flow:
https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.76

Sections 4 (round 2), 5, and 9 landed **after** that release and have only been through local
build+test, not yet shipped. Next session (or later this session) should re-run `release.ps1` to
ship them, or fold them into the next release's notes.

## Not yet confirmed — real next steps

1. Game Day fullscreen layout (section 3) still hasn't been live-verified — needs the owner to
   press GAMETIME and visually confirm the docked Sound Booth + VS header + switch arrows.
2. Sections 4 (round 2)/5/9 need live re-testing against a fresh build, then a release.
3. Mac project still won't compile (`SoundStartDelayMs` reference, section 2) — explicitly deferred
   by the owner, not started.
4. The owner was mid-session, actively testing with real recurring frustration about response speed
   and mystery delays — worth proactively asking early next session whether the rebalanced timing
   constants (section 4) feel right now, rather than waiting for another live bug report.
