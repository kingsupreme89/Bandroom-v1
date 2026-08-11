# Bandroom Handoff — Session 33 (2026-08-10)

Picks up after Session 32 (down/distance redesign + Big Game gating rewrite, uncommitted).
This session redesigned The Sound Booth into a plugin-rack-style UI (owner shared a reference
screenshot of a dark hardware-effects-rack plugin) and fixed a real WebView2 caching gotcha
discovered along the way. **Nothing from this session is committed** — same shared, uncommitted
working tree as Session 32; see that doc's own concurrent-session warning, still applicable.

## Concurrent session note (read this first)

`git status` shows this session's changes sitting alongside **uncommitted** Session 32 work
(`ConfigStore.cs`, `GameWatcher.cs`, `TriggerEntry.cs`, `src/Bandroom.Core/Helpers/
FirstDownHelper.cs`, plus 2 new evaluator files, plus the 3 deleted `guide/` files — none of that
is this session's doing, don't touch without checking). `WebBridge.cs`, `WebMainForm.cs`,
`AudioEngine.cs`, `AudioPlayer.cs` were touched by **both** this session and Session 32's work —
this session's diff in those files is scoped to the Sound Booth additions described below (new
getters + a metering tap); Session 32's event-routing rewrite is the rest of the diff in
`WebMainForm.cs` (225 changed lines total, most of it Session 32's).

## What changed this session

### 1. Sound Booth → plugin-rack redesign
Owner wanted The Sound Booth (`#sound-booth-overlay`) restyled to resemble a hardware effects-rack
plugin UI (tab strip, big rotary knob, IN/OUT meters) they showed via screenshot — but using
Bandroom's own theme, and wired only to real backend params. Investigation found the audio engine
has no continuous filter/cutoff/resonance/LFO like the reference — those were **not** built as
decoration. What's real and now exposed:

- **Rack head** (persistent across every tab): live IN/OUT level meter bars + a big Master Volume
  knob that's always visible regardless of tab, plus the existing "Reset All" no-effects-bypass
  button.
- **3-tab strip**: Mixer (Home/Away/PA volume + Fade Delay via a context knob that rebinds through
  4 pills), Reverb & EQ (reverb/EQ/sub-bass preset tiles — Reverb tiles **moved in** from the
  Adjust side panel, no longer duplicated there), Toggles (existing boolean effects, unchanged).
- **New custom rotary knob component** (`wwwroot/app.js`, `initSoundBoothKnob`/`SB_KNOB_PARAMS`) —
  SVG-based, drag-to-adjust (vertical drag, ~160px = full range), keyboard-accessible
  (arrows/PageUp-Down/Home/End), debounced bridge calls (~70ms) so a fast drag doesn't spam
  WebView2 host-object calls. Styled entirely off existing `--accent`/`--team-*`/`--glass-*` CSS
  vars, not the reference's pink/teal.
- **Live meters** — new `PeakMeterProvider` tap (`AudioEngine.cs`) wired into `AudioPlayer.Play`'s
  chain (dry tap + post-effects tap), backing a new `GetCurrentLevels()` bridge method
  (`WebBridge.cs` → `WebMainForm.GetCurrentLevelsFromWeb`) that also decays the levels ~40% per
  poll so the bars fall back toward 0 between clips. Polled every 100ms while the modal's open
  (`startSoundBoothMeters`/`stopSoundBoothMeters` in `app.js`); degrades to a dimmed "no live
  signal" state if the bridge call throws, so UI and metering can ship independently.
- **Preview button** per tab reuses the existing `bridge.PreviewEvent`/`StopPreview` calls — no new
  playback plumbing needed.
- **New getters** so the modal shows true engine state instead of stale defaults on reopen:
  `GetVolume`/`GetVolumeFromWeb`, `GetFadeDelay`/`GetFadeDelayFromWeb`,
  `GetReverb`/`GetReverbFromWeb` (mirrors the existing Home/Away/PA getter pattern).
- **Real per-effect explanations** — added "i" info buttons (reusing the existing
  `SB_INFO_TEXT`/`.sb-info` popover pattern) for Reverb, Sub-Bass, and each knob param, with actual
  technical detail pulled from the DSP code (e.g. Reverb's room-size/damp/wet/width per preset from
  `ReverbProvider.cs`, not guessed copy). The knob's info button updates automatically as the
  context knob rebinds between Home/Away/PA/Fade.

### 2. WebView2 disk-cache bug (real gotcha, not code)
Verifying the knob visually kept showing a giant unstyled black circle even after rebuild +
relaunch. Root cause: `WebMainForm.InitWebViewAsync` points WebView2's user-data folder at
`bin/<config>/net10.0-windows.../WebView2Data`, and Chromium/WebView2's disk cache for the
`https://appassets/...` virtual host **persists across full process kill + relaunch** — a rebuild
alone doesn't invalidate it. Fix: delete that `WebView2Data` folder before relaunching whenever
verifying a wwwroot-only (CSS/JS/HTML) change. Saved to memory (`bandroom_webview2_cache.md`) and
folded into the "3089" shorthand definition so this doesn't get re-discovered next session.

### 3. Dev-share push
Copied the freshly built `Bandroom.exe`/`.dll`/`.pdb`, `Bandroom.Core.dll`/`.pdb`, and the full
`wwwroot/` folder into `publish-dev-share/` (owner's "8888" shorthand, now saved to memory).
Left `Songs/`, `TeamBackgrounds/`, `TeamLogos/`, `Assets/`, and the Google secret file in that
folder untouched — those are user/runtime data, not build output.

## Verified this session
- `dotnet build BandAudioHook.csproj -c Debug` — clean, 0 warnings/0 errors, confirmed after every
  change (matches the project's standing convention).
- Knob rendering bug (WebView2 cache) diagnosed and fixed; owner asked to re-check visually after
  the cache-clear relaunch — **not yet confirmed fixed by the owner** as of this handoff.

## Not yet confirmed — real next steps
1. **Owner needs to re-verify the Sound Booth visually** now that the WebView2 cache issue is
   fixed — knob should render as a proper dial/arc, rack head should lay out horizontally
   (meter/knob/meter), tabs should switch panels correctly.
2. Owner asked for closer visual fidelity to the reference screenshot ("polish current layout" was
   chosen over "full reference clone" — add tab icons / tick marks / a more segmented ring, no new
   decorative-only controls) — not yet done, was paused for the render-bug fix and hasn't been
   resumed.
3. Owner explicitly said Sound Booth should match the *other* modals' existing look, not the other
   way around — no app-wide modal changes were made or requested; Sound Booth already reuses the
   same `.team-picker-header`/`.soundbooth-led` pattern as other overlays.
4. Live-verify the meters actually move during a real game event or Preview, and that the Preview
   button's playback genuinely passes through the current effect chain (assumed true since it's
   the same `AudioPlayer.Play` path as live fires, but not independently confirmed this session).
5. Reverb tiles were moved out of the Adjust side panel into the Sound Booth modal — confirm the
   owner is fine with that being single-location now rather than also quick-accessible from Adjust.

## Carried forward from Session 32 / 31 / 30 / 29 (untouched this session)
1. `voice_poc/.env` — still untracked, uncommitted, not gitignored; likely holds a secret.
2. **Not released** — commits sit on `master` past `v1.0.73` with no version bump/tag/Squirrel pack.
3. Session 32's event-routing rewrite (down/distance redesign, Big Game gating) — still
   uncommitted, still needs the live-verification steps listed in that session's own handoff.
4. 3 deleted `guide/` files noted in Session 32 — still unexplained, still not touched.
5. `.matchup-vs-badge` `top` value (nudged to `22%` in Session 30, still not re-verified visually).
6. Coverflow edge-fade mask + `.team-swatch-reflection` DOM wiring — CSS in place, JS side never
   wired up.
7. Player Profile Dashboard public-sharing sync fix still not live-verified against the real worker.
8. Session 27 carryovers: Mac marketplace-sharing multipart fix, trim-preview pill follow-up.
