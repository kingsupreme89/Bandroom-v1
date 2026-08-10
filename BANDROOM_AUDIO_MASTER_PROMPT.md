# Bandroom Audio Engine — Master Prompt
**Use this as a complete spec to hand to any AI coding assistant (Claude, Cline, etc.)**

---

## PROJECT CONTEXT

Bandroom is a Windows desktop app (C# .NET 10, WinForms + WebView2 UI) that watches College Football 27 via OCR and automatically triggers stadium audio (fight songs, crowd chants, PA announcer clips) based on live game events. It has 18 evaluators that detect touchdowns, turnovers, first downs, kickoffs, penalties, timeouts, etc. from the on-screen scorebug. Audio plays through NAudio `WaveOutEvent`.

**Project location:** `c:\Bandroom\`
**Audio engine file:** `c:\Bandroom\AudioPlayer.cs` (283 lines, static class)
**DSP files:** `c:\Bandroom\ReverbProvider.cs`, `c:\Bandroom\AudioDuckingController.cs` (dead code, never wired)
**Web UI:** `c:\Bandroom\wwwroot\index.html`, `app.js`, `style.css`
**Bridge:** `c:\Bandroom\WebBridge.cs` (Windows), `c:\Bandroom\src\Bandroom.Mac\MacWebBridge.cs`
**Main form:** `c:\Bandroom\WebMainForm.cs` (2037 lines)

---

## CURRENT STATE — WHAT EXISTS AND WHAT'S WRONG

### AudioPlayer.cs Current Behavior
- Static class, all playback goes through `Play(path, volumeOverride, interruptPrevious, isPreview)`
- **1-second pre-roll delay** before every real trigger (PreRollSeconds = 1.0)
- Opens a NEW `WaveOutEvent` per clip (shared mode, ~100ms Windows mixer latency)
- Reads audio files FROM DISK every single play call
- Basic algorithmic reverb (3 presets: Stadium, Dome, NightGame)
- Mono-to-stereo conversion
- Lead-in whistle support (gapless SequencedSampleProvider)
- `FireCooldown` = 20s per-file-path dedup
- Fade-out over last 4.5 seconds
- Independent volume floats: Master, Home, Away, Pa
- `StopAll()` stops all active `WaveOutEvent` instances
- `Warmup()` one-time cold-start mitigation
- **Zero EQ, zero compression, zero limiting, zero loudness normalization**

### AudioDuckingController.cs
- Fully built duck/fade state machine with presets
- **NEVER INSTANTIATED ANYWHERE** — 100% dead code
- Needs to be wired into the audio pipeline

### Key Problems
1. **1-second delay** between game event and sound = immersion killer
2. **Inconsistent volume** across user-uploaded songs (some whisper quiet, some ear-splitting)
3. **No speaker protection** — multiple simultaneous events can clip/distort
4. **No EQ** — marching band recordings sound muddy without EQ cleanup
5. **N×WaveOutEvent model** — each clip opens its own output device, Windows sums them
6. **Disk I/O on hot path** — reads from disk every play instead of RAM cache

---

## TASK

Refactor and expand the Bandroom audio engine to be a professional-grade, low-latency, broadcast-quality audio pipeline. Build the following features in priority order.

---

## PHASE 1: FOUNDATIONAL IMPROVEMENTS (Build First)

### 1. Instant Response Time (Remove Pre-Roll Delay)
- Change `PreRollSeconds` default from 1.0 to 0.0
- Make it configurable per event type (touchdown = 0ms, first down = 0ms, etc.)
- Target: trigger-to-speaker latency under 50ms (currently ~1200ms)

### 2. RAM Audio Pre-Caching
- On profile load or GAMETIME press, pre-load all assigned audio files into `byte[]` buffers
- `Play()` reads from `MemoryStream` wrapping these buffers instead of `new AudioFileReader(path)`
- Cache key = file path, invalidate when user re-assigns songs
- Eliminates disk I/O from the hot playback path

### 3. Automatic Volume Balancing (LUFS Normalization)
- On file import/upload, analyze each audio file using EBU R128 / ITU-R BS.1770 standard
- Calculate Integrated LUFS, Short-Term LUFS, True Peak dBTP
- Normalize to target loudness:
  - Marching band songs: **-14 LUFS** (streaming standard, punchy for live playback)
  - PA Announcer clips: **-18 LUFS** (speech clarity with headroom)
  - Lead-in whistle: **-12 LUFS** (short transient, needs to cut through)
- True Peak ceiling: **-1.0 dBFS** (never clip, even after normalization gain)
- Apply gain to a COPY of the file (never modify the user's original)
- Store LUFS metadata so re-analysis is skipped on subsequent loads
- Result: all songs play at the same perceived volume regardless of source

### 4. Master Bus Brickwall Limiter
- Final stage before audio output
- Look-ahead: 5ms
- Ceiling: -0.3 dBFS (broadcast-safe, prevents inter-sample peaks)
- Release: 50ms
- Protects speakers/headphones from multi-trigger clipping chaos
- BONUS: Add a soft clipper option (analog-style saturation instead of hard digital clip)

---

## PHASE 2: DSP & SOUND QUALITY (Build Second)

### 5. Modular DSP Processing Chain
Refactor from monolithic `Play()` method to composable `ISampleProvider` chain:
```
Source → Pre-Gain (LUFS) → EQ → Compressor → Reverb → Pan → Limiter → Output
```
Each processor is an independent `ISampleProvider` wrapper — testable in isolation, bypassable with a flag.

### 6. Marching Band Parametric EQ Presets
Three-band parametric EQ optimized for marching band recordings:

| Band | Frequency | Q | Gain | Purpose |
|---|---|---|---|---|
| High-pass | 80 Hz | 0.71 | — | Cut subsonic rumble and stadium noise floor |
| Low-shelf | 200 Hz | 1.0 | -3dB | Clean up muddy tuba/bass drum overlap |
| Peak | 2.5 kHz | 1.4 | +4dB | Bring trumpet overtones and snare crack forward |
| High-shelf | 8 kHz | 0.71 | +2dB | Add air and sparkle to cymbal crashes |

Also add a **"Megaphone / Stadium PA" preset**: aggressive bandpass (500Hz–4kHz) that makes any clip sound like it's booming through old concrete stadium speakers.

### 7. Transient Shaper for Marching Percussion
- Attack: +3dB to +6dB, fast attack time (1-5ms) — makes snare/quad hits crack harder
- Sustain: -2dB to 0dB — tightens drum resonance without killing body
- Apply only to percussion-heavy tracks or as an optional toggle per event

### 8. Stereo Width Enhancer
- Takes narrow or mono recordings and spreads into immersive stereo
- Mid/Side processing: boost Side channel by +3dB to +6dB
- Dry/Wet mix control so users can dial in the amount
- Mono-compatible (sums back to mono without phase cancellation)

### 9. AudioDuckingController Integration (Wire the Dead Code)
- Wire the existing `AudioDuckingController` into the mixer
- When a high-priority event fires (Touchdown, Turnover, Safety):
  - Duck background/ambient tracks to 40% volume
  - Attack: 20ms (fast, feels immediate)
  - Release: 300ms (smooth return, not jarring)
- Duck band music by 3dB during peak real-game crowd roars

### 10. Enhanced Reverb with Weather Presets
Extend the existing 3 reverb presets with weather-aware variants:
- **Stadium (Clear Night):** Decay 2.8s, HF damp 0.3, early reflections prominent — sharp, crisp outdoor echoes
- **Stadium (Rain):** Decay 1.8s, HF damp 0.7 (rain absorbs high frequencies), muffled and close — the "wet November game" sound
- **Dome:** Decay 3.2s, HF damp 0.5, heavy late reflections — that distinctive indoor boom and long tail
- **Night Game (Prime Time):** Decay 2.4s, HF damp 0.4, wide stereo image — the "big game under the lights" feel

Weather state can be input from a manual toggle in the UI, or detected from the game if weather OCR is ever built (rain/snow text on scorebug).

---

## PHASE 3: STADIUM IMMERSION (Build Third)

### 11. Crowd Volume Reacts to Game Situation
- Crowd roar volume scales automatically based on:
  - **Score differential:** Close game (<7pts) = loud. Blowout (>21pts) = quieter.
  - **Quarter:** 4th quarter = louder. 1st quarter = baseline.
  - **Time remaining:** Under 2 minutes = maximum intensity.
- Crowd channel is a separate mixing bus with its own gain envelope, driven by game state from the OCR engine

### 12. Sub-Bass Stadium Thump Enhancer
- Sub-harmonic synthesizer that adds clean 40-60Hz weight on:
  - Big tackles (Tackle for Loss events)
  - Field goal blocks
  - Heavy bass drum hits in fight songs
- Uses wavefolding + low-pass rather than simple pitch shifting (cleaner, less artifacts)
- Adjustable intensity: Off / Subtle / Stadium / Earthquake

### 13. Doppler Panning for Running Plays
- Subtle stereo panning based on field position:
  - Team driving LEFT across screen → sound pans slightly left
  - Team driving RIGHT → sound pans slightly right
  - Touchdown in left corner → band music hits from the left
- Field position sourced from YardLine OCR (when built) or estimated from down/distance text
- Very subtle (±15% pan max) — should be felt, not obviously heard

### 14. Tunnel / Locker Room Pregame Sound
- During pregame run-out (PregameHelper event fires):
  - Apply "Tunnel" filter: heavy reverb (3.5s decay) + bandpass (300Hz–3kHz) + slight distortion
  - Crossfade over 3 seconds from Tunnel → Wide Open Stadium as the team enters the field
- This is the single most cinematic moment in sports audio — the "bursting onto the field" transition

### 15. Rivalry Tension Drone (4th Quarter / Overtime)
- In close 4th quarter games (<8pt differential, under 4:00):
  - Low, rumbling sub-bass drone (~30-50Hz) slowly fades in underneath everything
  - Volume follows game tension: louder as clock runs down
  - Cuts instantly when the game ends or goes to blowout margin
- Purely atmospheric — shouldn't be consciously noticeable, just felt

### 16. Halftime Show Mode
- Toggle mode that plays through ALL assigned team songs continuously
- Crossfade between tracks (3-second overlap)
- No event triggering — pure playlist mode
- Optional: display current track name on screen
- Useful for streaming, tailgating, or just showing off your song collection

---

## PHASE 4: PERFORMANCE & RELIABILITY (Build Alongside Others)

### 17. WASAPI Exclusive Mode Toggle
- UI toggle: Shared Mode (default, compatible) vs Exclusive Mode (lowest latency, bypasses Windows mixer)
- When enabled: `WasapiOut(AudioClientShareMode.Exclusive, desiredLatency: 10)`
- When disabled: keep existing `WaveOutEvent` or switch to `WasapiOut` shared mode
- Target: <15ms round-trip latency in Exclusive mode

### 18. Live Audio Health Monitor
- Real-time metrics exposed to the web UI via WebBridge:
  - Current output latency (ms)
  - Buffer underrun count
  - CPU usage of audio thread
  - Current peak level (dBFS)
  - Active clip count
- Color-coded status: Green (healthy) / Yellow (warning) / Red (dropouts detected)

### 19. Automatic Crash Recovery
- Watchdog thread that monitors the audio output device
- If device disconnects (USB unplug, Bluetooth drop, driver crash):
  - Detect within 500ms
  - Re-enumerate audio devices
  - Re-initialize on the new/default device
  - Resume playback from where it left off
- User sees a brief toast: "Audio device changed — switched to [device name]"

### 20. Live Audio Device Switcher
- Dropdown in Settings/Mixer panel listing all available output devices
- Switch devices without restarting the app or losing game state
- Hot-swap: stop current device, re-init on new device, <200ms gap

### 21. Mixed Sample Rate Handling
- Seamlessly handle mixing 44.1kHz, 48kHz, and 96kHz source files in one session
- Use NAudio's `WdlResamplingSampleProvider` or `MediaFoundationResampler` to resample to the output device's native rate
- Zero user intervention needed — just drop in any file

### 22. Smart Cooldown Gate (Already Partially Built)
- Upgrade from per-file-path 20s cooldown to per-EventKey with adaptive windows:
  - Touchdown: 15 seconds (they don't repeat often, but prevent double-fire from replay overlays)
  - First Down: 45 seconds (happens frequently, don't spam)
  - Kickoff: 60 seconds (only happens a few times per game)
  - Timeout: 120 seconds
- Separate cooldown per side (home/away) so both teams' events can fire independently

### 23. Low-Performance Mode
- Detect or manually toggle a "Low CPU" mode that:
  - Disables FFT spectrum analyzer visualizer
  - Switches from convolution reverb to algorithmic (if convolution is ever added)
  - Reduces animation frame rate in the UI
  - Locks audio thread to HIGH priority
- Audio quality is never sacrificed — only visual effects are scaled back

### 24. Detailed Audio Event Log
- CSV or text log recording every triggered event:
  - Timestamp (game clock + real time)
  - Event key (e.g., "Offense: Touchdown Scored")
  - Side (home/away)
  - File played
  - Input loudness (LUFS), applied gain, output peak
  - Play duration
- Rotating log, capped at last 2000 events

### 25. One-Click Diagnostic Zip
- Button in Settings that packages:
  - Audio event log
  - OCR debug log
  - Crash logs
  - System specs (CPU, RAM, audio devices)
  - Current Bandroom version + profile info
- Saves to desktop as `bandroom-diagnostics-[date].zip`

---

## IMPLEMENTATION RULES

1. **Language:** C# (.NET 10), NAudio for audio, WebView2 for UI bridge
2. **Thread safety:** Audio render callback must be lock-free. Use `Interlocked.Exchange` for parameter updates from UI thread. Use `ConcurrentQueue` for OCR→audio event handoff.
3. **No allocations in the render callback.** All DSP processors should be constructed once and reused.
4. **All processors implement `ISampleProvider`** from NAudio. Chain them, don't branch. Each processor wraps the next one.
5. **Backward compatible.** Don't delete `AudioPlayer.cs` immediately. Build new `AudioEngine` class, make `AudioPlayer.Play()` delegate to it as a thin wrapper. Migrate callers over time.
6. **Normalization is offline.** LUFS analysis + gain application happens at file import time in a `Task.Run` background thread. Never in the audio render callback.
7. **Every feature has an on/off toggle.** Users can opt into any feature. Nothing is forced. All toggles exposed through WebBridge → `app.js` → Settings/Mixer UI panels.
8. **Testable.** Each DSP processor should be a standalone class with a single `ISampleProvider Read()` method. Test with known input → verify output.
9. **Mac-compatible where possible.** Audio on Mac uses `afplay` CLI currently. DSP processing (EQ, compression, limiting) can work identically on both platforms if built as `ISampleProvider` wrappers. Output device code is platform-specific.
10. **Document everything.** Every DSP processor gets a doc comment explaining what it does in plain English, what the parameters mean, and when you'd use it.

---

## PRIORITY ORDER SUMMARY

Build in this exact order — each one enables or simplifies the ones after it:

1. **RAM Audio Pre-Caching** (#2) — eliminates disk I/O, makes everything else feel faster
2. **Remove Pre-Roll Delay** (#1) — immediate win, 3 characters changed in code
3. **Modular DSP Chain** (#5) — the architecture everything else plugs into
4. **Brickwall Limiter** (#4) — protects speakers before we start making things louder
5. **Automatic Volume Balancing** (#3) — solves the #1 user complaint
6. **Ducking Controller Integration** (#9) — wires existing dead code, prevents overlapping audio chaos
7. **Marching Band EQ Presets** (#6) — one-click fix for muddy recordings
8. **Smart Cooldown Gate** (#22) — prevents the "wall of sound" spam bug
9. **Enhanced Weather Reverb** (#10) — builds on existing reverb, huge demo value
10. **Everything else** — in any order, based on what excites you most

---

## HOW TO GIVE THIS TO AN AI

Copy everything above and paste it as a single prompt. The AI will have full context of:
- What Bandroom is
- What the current audio pipeline looks like
- What's broken and why
- What to build and in what order
- Implementation rules and constraints

The AI can then implement features one at a time, starting from the top of the priority list.