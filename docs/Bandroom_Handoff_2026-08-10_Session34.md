# Bandroom Handoff — Session 34 (2026-08-10)

Picks up after Session 33 (Sound Booth plugin-rack redesign + WebView2 cache gotcha, uncommitted).
**A second session was running concurrently with this one**, actively editing `WebMainForm.cs` and
`wwwroot/app.js` (a down/distance event-rename + Big Game gating pass — matches Session 32's
carried-forward work). This session deliberately avoided touching those two files for most of its
back half once that became clear (see "Concurrent session note" below) — some planned work
(volume-settings persistence, a "What's New" popup investigation) was **paused, not done**, because
it needed those exact files.

## Concurrent session note (read this first, then check the one below too)

Confirmed via file mtimes partway through this session that another session was live-editing
`WebMainForm.cs` (mtime 20:30) and `wwwroot/app.js` (mtime 20:43) — neither edited by this session.
`git diff` on `wwwroot/app.js` at that point showed the down/distance event-rename/Big Game gating
work (e.g. `EVENT_FRIENDLY_NAMES` entries like `"Defense: First Down": "1st Down (Post-Kickoff)"`,
new `test-hook-event-a/b` pair-fire UI). **This session's own edits after that point were scoped to
`AudioPlayer.cs` and `wwwroot/style.css` only** — did not touch `WebMainForm.cs`/`app.js` again to
avoid clobbering or being clobbered. If picking this up next, diff those two files first to see
what actually landed from the other session before assuming anything below is still accurate.

There is **no inter-session messaging channel** — "check with the other chat" isn't something a
session can actually do; the only real mitigation is checking file mtimes/git diff before editing
shared files, same approach used here.

## What changed this session

### 1. Fixed: Lead-In Whistle volume slider had no audible effect
Owner reported the whistle volume slider didn't change anything when dragged. Root cause in
`AudioPlayer.cs`'s playback loop (`Play()`): `BuildLeadInProvider` correctly builds the whistle
reader with `volume * WhistleVolume` at ~line 333, but the **live volume-tracking loop** that runs
every 15-30ms while a clip plays was overwriting `leadInReader.Volume = audio.Volume` on every tick
— the main clip's volume, with **no `WhistleVolume` factor at all** — wiping out the slider's effect
almost instantly after playback started. Fixed both overwrite sites (fade-out branch and normal
branch, ~lines 291/297) to multiply by `WhistleVolume` too. Verified via 3089 rebuild+relaunch;
owner has not yet confirmed audibly.

### 2. Restyled the "LOCK IN?" matchup button
Owner: `.matchup-btn`'s unlocked state (`index.html:45`, the header's "LOCK IN?" CTA when no
matchup is picked) looked unstyled/inert compared to the rest of the app's pill language. Restyled
to match `.situation-btn` (the existing reusable actionable-pill pattern): bigger padding/font,
team-color-tinted background/border via `color-mix()`, and the shared `pill-glow-pulse` animation
**persistent** (not just on hover) so it reads as a live call-to-action needing attention. The
`.locked` state (post-matchup-pick, green) explicitly gets `animation: none` since it's a confirmed
state, not a CTA. `wwwroot/style.css` ~line 3755.

### 2b. Diagnosed (not yet fixed): stale "Choose a Team" picker
Owner screenshotted the header team-picker overlay showing an old dense multi-row icon grid with
no dimmed backdrop — didn't match current `#team-picker-overlay` markup at all, which only renders
a 5-tile iTunes-coverflow layout (`renderTeamPickerCoverflow` in `app.js`). Diagnosed as the same
WebView2 disk-cache gotcha from Session 33 recurring (stale cached HTML/JS/CSS surviving a rebuild).
Cleared `WebView2Data` and relaunched; **not yet re-confirmed visually by the owner.**

### 3. Investigated but paused: volume/effects settings don't persist across restart
Owner asked to make volume settings persist "like the lead-in whistle volume," believing that one
already survives a restart. **It doesn't** — confirmed via code read that `AudioPlayer.cs`'s entire
global audio-settings surface (`MasterVolume`, `HomeVolume`, `AwayVolume`, `PaVolume`,
`WhistleVolume`, `FadeStartSeconds`, `CurrentReverb`, `CurrentEq`, `SubBassLevel`,
`TransientShaperEnabled`, `StereoWidenerEnabled`, `LimiterEnabled`, `NoEffectsBypass`,
`LeadInEnabled`) is plain in-memory static state with **zero disk persistence** — all reset to
defaults on every launch. `ConfigStore.cs` has no volume/effects-related persistence at all (grepped
for "Volume", zero hits). Owner's belief it "already works" is just because it hadn't survived an
actual restart yet this session.

Planned fix (not started): a new `audio_settings.json` in `ConfigStore.cs`'s `UserDataRoot`
following the existing `SaveProfile`/`LoadProfile` pattern (local-disk-write-first, synchronous),
loaded once at `WebMainForm` startup and written on every `SetXFromWeb` setter. **Blocked** on
`WebMainForm.cs` being owned by the concurrent session for the rest of this session — every
`SetXVolumeFromWeb`/`SetReverbFromWeb`/etc. setter that would need a save-call added lives there.

Second half of the same ask — **prompt before autosaving over a team's songs if they were changed
elsewhere** (implies conflict detection against the Supabase cloud mirror) — was **not scoped or
started at all**. This is a materially bigger feature than local persistence: `ConfigStore.SaveProfile`
today is explicitly local-disk-authoritative with a fire-and-forget best-effort cloud push (see its
own doc comment) specifically so a slow/unreachable cloud never blocks a local save; a pre-save
conflict check would cut against that design intent and needs a real conversation with the owner
about UX (a network round-trip before every autosave, given saves fire on every slider tick per an
existing code comment, would need its own debounce/only-check-on-explicit-save-action design) before
touching architecture. Flagged to owner, not decided yet.

### 4. Investigated but paused: "What's New" popup reappearing on every Save Profile
Owner screenshotted the "What's New in Bandroom" changelog panel appearing stacked with the "Save
Profile" confirmation dialog every time Save is pressed. Read (not edited) the relevant `app.js`
logic: `maybeShowWhatsNew()` gates on `localStorage["bandroom-whatsnew-seen"]` matching the latest
release title (one-time-per-release popup), and `showWhatsNewWhenClear()` already lists
`save-profile-overlay` in `WHATS_NEW_BLOCKING_OVERLAY_IDS` — it's supposed to wait for Save Profile
to close before showing itself, not appear alongside it. Didn't get further than this read — root
cause not confirmed, no fix attempted, since it's an `app.js` change and that file was owned by the
concurrent session at the time.

## Verified this session
- `dotnet build BandAudioHook.csproj -c Debug` — clean, 0 warnings/0 errors after the whistle-volume
  fix.
- Whistle volume fix: rebuilt + relaunched (3089), not yet audibly confirmed by owner.
- LOCK IN? button restyle: rebuilt, WebView2 cache cleared (had to also kill leftover
  `msedgewebview2.exe` subprocesses holding cache files locked from an earlier incomplete
  taskkill — confirmed via `Get-CimInstance Win32_Process` that the *remaining* webview2 processes
  after cleanup belonged to Windows Search/Widgets, not Bandroom), relaunched — not yet visually
  confirmed by owner.
- Team-picker cache-staleness diagnosis: cache cleared, relaunched, not yet re-confirmed.

## Not yet confirmed — real next steps
1. Owner needs to audibly confirm the whistle volume slider now actually changes volume during
   playback/preview.
2. Owner needs to visually confirm the "LOCK IN?" button's new pill/glow styling looks right, and
   that the team-picker overlay now shows the coverflow (not the stale grid) after the cache clear.
3. **Decide the volume-persistence approach** — straightforward once `WebMainForm.cs` is free again
   (new `ConfigStore` JSON, load-on-startup, save-per-setter, mirroring `SaveProfile`'s pattern).
4. **Get owner alignment on the conflict-prompt-before-autosave feature** before starting it — real
   scope/UX questions (when to check: every autosave tick vs. only explicit Save-button presses;
   what "changed elsewhere" means operationally against the existing fire-and-forget Supabase push)
   that only the owner can settle, and it cuts against an explicit existing local-first design
   decision documented in `ConfigStore.SaveProfile`'s own comment.
5. Root-cause the "What's New" popup showing alongside (not after) Save Profile — likely a gap in
   `showWhatsNewWhenClear`'s blocking-overlay check or a race in when `maybeShowWhatsNew` first
   fires vs. when Save Profile can be opened this early in a session, but not actually traced yet.
6. Whatever the concurrent session's down/distance/Big Game-gating pass landed as — diff
   `WebMainForm.cs`/`wwwroot/app.js` fresh, don't assume Session 33's version of either file is
   still current.

## Carried forward from Session 33 / 32 / 31 / 30 / 29 (untouched this session)
1. Sound Booth plugin-rack redesign (Session 33) — reference-fidelity polish (tab icons/tick
   marks/segmented ring) still deferred; meters/Preview still not live-verified against a real game
   event; Reverb-tiles-moved-into-Sound-Booth-only still not confirmed with owner.
2. `voice_poc/.env` — still untracked, uncommitted, not gitignored; likely holds a secret.
3. **Not released** — commits sit on `master` past `v1.0.73` with no version bump/tag/Squirrel pack.
4. 3 deleted `guide/` files (Session 32) — still unexplained, still not touched.
5. `.matchup-vs-badge` `top: 22%` nudge (Session 30) — still not re-verified visually.
6. Coverflow edge-fade mask + `.team-swatch-reflection` DOM wiring — CSS in place, JS side never
   wired up.
7. Player Profile Dashboard public-sharing sync fix still not live-verified against the real worker.
8. Session 27 carryovers: Mac marketplace-sharing multipart fix, trim-preview pill follow-up.
