# Bandroom Handoff — Session 26 (2026-08-09 late night) — NOT released yet

Picks up after Session 24 (`docs/Bandroom_Handoff_2026-08-09_Session24.md`) and in parallel with
Session 25 (`docs/Bandroom_Handoff_2026-08-09_Session25.md`, a separate concurrent session that ran
a bug-audit pass over the same recent changes — read that one too, it covers real fixes to files
this session also touches). This session did the actual "Sound Booth" audio engine overhaul work
that Session 24 only documented from the outside (commit messages + diff stats, not read in full).

## What this session built

Scope was the full `BANDROOM_AUDIO_MASTER_PROMPT.md` 25-item list, narrowed down live with the
owner to: everything except WASAPI/device-switcher/health-monitor UI (kept backend-only, no
toggle), Doppler panning (item #13, on hold), and halftime mode (item #16, dropped outright, owner
call). "No fade-ins anywhere" was also an explicit owner constraint — the existing fade-OUT-only
design stays that way; nothing new fades in.

**New files:** `AudioEngine.cs` (the bulk of the new DSP: `AudioCache`, `CachedAudioSource`,
`BiQuadFilter`, `ParametricEqProvider`/`MegaphoneEqProvider`, `TransientShaperProvider`,
`StereoWidenerProvider`, `LimiterProvider`, `LoudnessAnalyzer`, `LoudnessNormalizationService`,
`SubBassEnhancerProvider`, `TunnelFilterProvider`, `SystemVolumeService`), `ControllerRumbleService.cs`,
`CrowdBusService.cs`.

**Modified:** `AudioPlayer.cs` (RAM cache wired into `Play()`, pre-roll dropped 1.0s→0.0s, new
Sound Booth toggles, new `isHighPriorityEvent`/`isBigHitEvent`/`isPregameEvent` params on `Play()`),
`AudioDuckingController.cs` (was 100% dead code per every prior handoff — now a real gain
calculator, actually wired into `Play()`'s poll loop), `ReverbProvider.cs` (two new weather
presets), `GameWatcher.cs` (`LastSnapshot` property added so services can poll live game state),
`WebMainForm.cs`/`WebBridge.cs` (bridge methods for every new toggle), `wwwroot/{index.html,app.js,
style.css}` (the Sound Booth panel UI, living inside the existing Mixer/Adjust side panel for now).

Note: several DSP fixes visible in the files as they stand now (ducking's single-shared-deadline
fix, the limiter's monotonic-deque sliding-max, crowd bus rebuilding its pipeline on `ClipPath`
reassignment) landed mid-session from a concurrent process while this session was still working —
they're described below as already-present, not as this session's own contribution.

### Item-by-item

1. **Pre-roll delay removed** (`AudioPlayer.PreRollSeconds` 1.0 → 0.0).
2. **RAM pre-caching** (`AudioCache`) — populated before `_watcher.Start()` in
   `StartWatchingIfMatchupSet` (blocking on purpose — see that method's own comment for why a
   fire-and-forget `Task.Run` here would race the first real trigger).
3. **LUFS normalization** (`LoudnessNormalizationService`) — real ITU-R BS.1770-style K-weighted
   analysis, offline, gain-matched copy written to `SongsNormalized/`, JSON sidecar cache keyed by
   source mtime. Wired into `OpenAssignTrack`, `AssignTrackFileFromWeb`, and the lead-in whistle
   browse flow, all via `NormalizeAssignmentInBackground` (fire-and-forget, re-points the
   `TriggerEntry` at the normalized copy once ready). **System volume linking**: `SystemVolumeService`
   reads (never writes) the Windows default output device's volume/mute via
   `NAudio.CoreAudioApi.MMDeviceEnumerator`, exposed read-only through `GetSystemVolumeInfoFromWeb` —
   informational only (so the UI can eventually say "your songs are balanced, but Windows itself is
   muted" instead of that looking like a Bandroom bug), does NOT double-apply gain.
4. **Master limiter** (`LimiterProvider`) — lookahead brickwall, on by default.
5. **Modular DSP chain** — `Play()`'s provider chain: cache → mono-to-stereo → (reverb OR tunnel) →
   EQ preset → transient shaper → stereo widener → sub-bass → lead-in sequencing → limiter. Each
   stage is a no-op skip when its toggle is off; `NoEffectsBypass` skips the whole creative chain
   (limiter still applies — it's a safety stage, not an effect).
6. **EQ presets** — `ParametricEqProvider` (Marching Band: HPF 80Hz, low-shelf 200Hz -3dB, peak
   2.5kHz +4dB, high-shelf 8kHz +2dB) and `MegaphoneEqProvider` (500Hz-4kHz bandpass).
7. **Transient shaper** — dual envelope follower (~3ms fast / ~80ms slow), toggle only, no
   per-track override yet.
8. **Stereo widener** — Mid/Side, toggle + fixed 0.5 amount (no slider in UI yet, field exists).
9. **Ducking** — real now, not dead code. Applies to every OTHER currently-playing clip's poll
   loop on Touchdown/Turnover/Safety, not the triggering clip itself.
10. **Weather reverb** — `StadiumRain`, `NightGamePrimeTime` added to `ReverbPreset`/`ReverbPresets`.
11. **Crowd bus** (`CrowdBusService`) — persistent looping `WaveOutEvent`, volume driven by
    `GameWatcher.LastSnapshot` (score margin / quarter / time remaining) polled every 500ms.
    **Fully wired but inert until the owner supplies a crowd-ambience audio file** — no such asset
    ships in this repo; UI shows "(needs a clip first)" until one's set via the Sound Booth's
    "Set Crowd Clip..." button.
12. **Sub-bass thump** — Off/Subtle/Stadium/Earthquake, wavefold+lowpass, wired to "Tackle for
    Loss" only (this codebase has no Field Goal Block detection). **Ships OFF by default per
    explicit owner instruction** ("only do 12 if you can do it smoothly") — this session could not
    listen to it, so smoothness is unverified. Needs an owner listening pass before it's trusted.
13. **Doppler panning — NOT built.** Owner said they have a specific on-screen trigger in mind
    ("the first screen") but didn't specify it before this session ended. No `PanProvider`, no
    hook point — genuinely not started, nothing to break.
14. **Tunnel/pregame effect** (`TunnelFilterProvider`) — bandpass 300Hz-4kHz + long tight reverb +
    soft-clip saturation, replaces (not stacks with) the normal reverb preset for the "Other:
    Pregame Ready" trigger only. No crossfade to a separate "open stadium" sound — collapsed per
    the owner's "no fade-ins anywhere" rule; the whole clip just gets the tunnel treatment.
15. **Controller rumble** (`ControllerRumbleService`) — XInput vibration pulse (~350ms, moderate
    strength, one pulse per ~4s while the condition holds) when Quarter≥4 & clock≤2:00, OR
    Quarter≥5 (inferred overtime — **`PlaySnapshot` has no real OT flag**, this is a guess at how
    CFB27's OT quarter-read behaves and is the first place to fix if that's wrong), AND score
    margin ≤7. Windows-only; every P/Invoke call is wrapped so a missing controller/XInput DLL is
    silently a no-op, never a crash.
16. **Halftime mode — dropped**, explicit owner call mid-session.

## What's genuinely unverified

This session had no way to build-and-run the WinForms/WebView2 app or listen to any audio output.
Everything below compiled clean (`dotnet build BandAudioHook.csproj` → 0 errors, checked after every
major addition) but was never heard or click-tested:
- Sub-bass smoothness (item 12) — could sound bad, owner needs to judge.
- Crowd bus's actual mix balance once a real ambience file is assigned.
- Tunnel effect's character (item 14) — untested by ear.
- Controller rumble's actual strength/feel on real hardware, and whether the Quarter≥5-means-OT
  assumption holds against real CFB27 OT scorebug text.
- The Sound Booth UI's placement inside the Adjust/Mixer panel — the owner separately asked for a
  bigger "Game Page" redesign (see below) that will likely relocate all of this.

## Open item: the "Game Page" redesign (blocked on the owner)

Mid-session the owner asked for something bigger than "add a Sound Booth tab": after GAMETIME, the
app should open a smaller, movable/resizable **Game Page** window — team-background-themed,
containing a compact Sound Booth "island" and a situations/events selector in BOTH list and
grid/tab form. The owner said they'd share a picture of the layout before this gets built, to avoid
guessing wrong. **That picture was never sent before this session ended.** The Sound Booth UI
currently lives where it was originally built (inside the existing Adjust/Mixer side panel) as a
placeholder. Do not relocate it without that reference, per the owner's own instruction.

## Starting a fresh session on this

1. **Read Session 25's handoff too** — it covers a real bug-audit pass over overlapping files
   (`AudioPlayer.cs`, `AudioDuckingController.cs`, `CrowdBusService.cs`, etc.) that landed
   concurrently with this session's own work. Reconcile both before assuming either alone is the
   full picture.
2. **Ask the owner for the Game Page picture/reference** before touching Sound Booth placement —
   this is the single biggest open item and was explicitly gated on that input.
3. **Get a listening pass on items 3/9/11/12/14/15** — none of this session's audio work has been
   heard. Sub-bass in particular ships off by default until confirmed smooth.
4. **Get a crowd-ambience audio file from the owner** (or decide to source/bundle one) — item #11
   is fully wired but silent without it.
5. **Nail down the Doppler panning trigger** (item #13) — owner mentioned "the first screen" but
   the session ended before getting specifics. Nothing built yet, no risk of it being wrong, just
   not started.
6. **Verify `PlaySnapshot.Quarter >= 5` actually means overtime** in real CFB27 OCR output before
   trusting `ControllerRumbleService`'s rumble-during-OT behavior.
7. **Confirm what's actually committed** — this session never ran `git add`/`git commit` itself (no
   commit instruction was given by the owner). Run `git status`/`git log` fresh rather than trusting
   this handoff's file list against what's really on disk/staged by the time you read this, since at
   least one other concurrent session (25) was committing in parallel.
8. Everything still open from Session 24's own "starting fresh" list (gameday-logo `top:` value
   reconciliation, `ReverbProvider.AllPass` feedback-parameter fix verification, Supabase Settings
   UI, `AudioCache` eviction, Session 21's 33-event checklist, `D:\Bandroom` stale-duplicate-repo
   cleanup) remains open and was not touched this session.
