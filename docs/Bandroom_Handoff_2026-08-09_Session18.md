# Bandroom Handoff — August 9, 2026, Session 18

## Summary

This session: found and fixed the real root cause of the "saved logos don't show up" bug (reported repeatedly across several prior sessions and "fixed" the wrong way each time), added a Right-Ctrl global audio-cutoff hotkey, replaced the native song-trimmer popup with an embedded waveform panel, and fixed a hard blocker that made Remote Play/console testing non-functional. Shipped as v1.0.66 and v1.0.67. One more fix (Remote Play window detection) is committed to source but **not yet released** — needs "ppup" from the owner.

---

## ✅ The real logo bug — ROOT CAUSE FOUND AND FIXED (v1.0.67)

**This was never a save-path bug.** Every previous session's fix (cache-busting query params, filename sanitization matching) was real and correct, but none of them touched the actual cause, which is why the report kept coming back.

**Root cause:** `wwwroot/app.js` had two competing "lazy load team logos with IntersectionObserver" implementations added in different sessions (search history showed both `_lazyImageObserver`/`lazyLoadImages` and a `fillTeamSwatch` monkey-patch with `_logoObserver`/`enableLazyLogos`/`observeLogos`). The `fillTeamSwatch` patch **stripped `src` off every logo `<img>` and stored it in `data-src`**, expecting an IntersectionObserver to set `src` back once the tile scrolled into view. **`observeLogos()` was never called from anywhere** — not the team picker, not the matchup coverflow, not the header badge. Every team logo in the entire app — freshly saved or already sitting on disk for months (Alabama, Auburn, etc.) — silently had its `src` deleted and nothing ever restored it. The tile just showed the fallback color gradient forever.

**Verified live**, not just by reading code: reproduced with computer-use + DevTools (had to temporarily force `core.OpenDevToolsWindow()` in `WebMainForm.cs` since `AreBrowserAcceleratorKeysEnabled = false` blocks F12 — reverted after). Confirmed via `chrome.webview.hostObjects.bandroom.GetTeams()` in console that `logoUrl` was correct and fetchable (200, image/png), but `document.querySelector('.cf-center').outerHTML` showed `data-src=` instead of `src=`. Removed both dead lazy-load systems entirely in `app.js` (search "ITEM 8" if you need the exact removed block in git history) — there's no real need for lazy-loading here anyway: at most a few dozen logo tiles are ever in the DOM at once, served from a local virtual host, not a network CDN.

**If a "logo won't show" report ever comes back:** don't assume it's this bug again — it's now impossible for `src` to go missing since nothing writes to `data-src` anymore. Check the save path (`WebBridge.SaveCustomTeamLogo` → `WriteTeamLogoFile` → `TeamLogo.FindImagePath`) fresh, or check for a NEW dead-code lazy-load reintroduction (grep `data-src` in `app.js` — should return zero hits).

---

## ✅ Other fixes this session (shipped in v1.0.66)

1. **Right Ctrl = global "band cutoff" hotkey.** `KeyboardHook.cs` now has a `Cutoff` event firing on Right Ctrl (uses `VK_RCONTROL`, distinct from generic `VK_CONTROL` which can't tell left/right apart) — global via the existing `WH_KEYBOARD_LL` hook, works even when Bandroom isn't focused. Wired in `WebMainForm.OnCutoff()` → `AudioPlayer.StopAll()` + a confirmation toast (`bandroom:cutoff` event in `app.js`).
2. **Crop-tool zoom slider fixed.** `logo-crop-zoom`/`bg-crop-zoom` had `min="100"` — could zoom in but never back out. Changed to `min="50"`.
3. **Embedded waveform trimmer** (replaces the native `TrimmerForm` popup for the Events/Assign screen's "Trim..." button only — the other two `TrimmerForm` call sites, `AssignTrackForm`'s legacy flow and local-song-import, are untouched, smaller blast radius per the Session 14 investigation notes).
   - New: `AudioNormalizer.cs` — extracted the RMS-normalize-and-limit DSP out of `TrimmerForm` so both the old dialog and the new panel share one implementation.
   - New: `ConfigStore.TrimSourceFolder` + a `trimsrc://` virtual host mapping (`WebMainForm.cs`) — single-slot scratch folder the panel's waveform fetches from.
   - New bridge methods: `PrepareTrim`, `SaveTrim`, `SaveTrimAsLeadInWhistle` (`WebBridge.cs` → `WebMainForm.cs`).
   - New UI: `#clipper-trim-panel` in `index.html`, swaps in for the song list inside `.clipper-assign-main`; pill actions (Preview/Stop/Save Trim/Set as Lead-In Whistle/Cancel) swap in for the default action row the same way. Drag-handle + end-tail-preview behavior mirrors `TrimmerForm` exactly.
   - **Not yet fully redesigned visually** — it's functional and embedded (no more popup), but hasn't had a dedicated pass to match the CFB-reference aesthetic the owner wants elsewhere. Worth revisiting alongside the matchup screen work below.

---

## ⚠️ Remote Play / console testing — fixed a hard blocker, NOT YET RELEASED

**What was broken:** `GameWatcher.FindGameWindow()` only ever looked for a process literally named `CollegeFB27` (the PC game's own exe). A console tester runs Sony's **PS Remote Play** client instead, which is a completely different process (`RemotePlay.exe`) showing the same in-game UI in its own window. Bandroom could never find a window to watch at all for a console tester — **the scorebug preset dropdown existed but was unreachable in practice**, since window-detection failed before OCR calibration ever mattered.

**Fix (committed, not released):** `GameWatcher.cs` now checks both `CollegeFB27` and `RemotePlay` process names.

**What's still genuinely unverified for console:** per the Session 9 handoff, the scorebug/down-distance OCR regions were calibrated from one real PS5 Remote Play screenshot (`ScorebugPreset.ConsoleScorebugV1`), but the **penalty/flag OCR region was only ever tested against PC captures** — it may or may not line up correctly on a Remote Play capture. Nobody has confirmed this live yet.

### Exact steps for a console tester (once this ships)

1. Install/update Bandroom to whatever version includes this fix (check the version number in the top-left of the app against the release notes — this fix isn't in v1.0.67 yet).
2. Start **PS Remote Play** on the PC, connect to the PS5, and get College Football 27 running full-screen or windowed inside the Remote Play client.
3. In Bandroom, open **Settings** (gear icon, top-right of the header) → **Scorebug position** dropdown → select **"Console/Remote Play v1"**.
4. Set the matchup (home/away teams) as normal, then hit **Start Watching**.
5. Bandroom should detect the Remote Play window and start reading down/distance/quarter/possession the same as a PC capture.
6. **What to actually test and report back on:** does a penalty/flag get detected correctly? That's the one region nobody has confirmed works on a console capture. If the owner can send a screenshot of a flag thrown while testing (1920×1080, full Remote Play window), that's exactly what's needed to calibrate `PenaltyHelper`'s OCR region for console if it turns out to be off.

---

## Next up: UI design work (not started this session, notes for whoever picks this up)

### 1. Matchup ("Set Matchup") screen redesign
Owner wants this to visually resemble the CFB 27 in-game matchup/rivalry screen (reference screenshot: two big team-branded panels side by side — helmet/logo, team ratings OVR/OFF/DEF in top corners, name + mascot name, a center "VS"/rivalry badge, controller-icon hints, bottom action bar). Current implementation is `#matchup-overlay` in `index.html` (~line 804) with a horizontal coverflow picker per side (`renderMatchupCoverflow` in `app.js`) — browsing IS picking, center tile commits. That interaction model is fine and doesn't need to change; this is a **visual** redesign, not a functional one. Replace the plain team-swatch tiles in the committed home/away slots with something closer to the reference's big branded panel look (logo prominent, team colors as the panel's dominant treatment, not just a small swatch). Keep dark glassmorphism (`.glass`, pulsing team-color LED outlines) — the reference's exact style (torn-paper texture, hard rivalry-red) is NOT the target, just its *layout/information density* per standing style guidance.

### 2. My Downloads + Marketplace UI
Per `HANDOFF_UI_REDESIGN_2026-08-08.md`: Marketplace got a real redesign (Nexus-Mods-style 4-column cards) that session; **My Downloads did not** and is still on the original cramped layout. ~75 other items from that session's punch list are also still open (XSS sanitization on marketplace innerHTML, search debounce, macOS traffic-light window controls, sheet-style dialogs, etc. — full list in that doc under "NOT DONE").

### 3. Event wording — verified fine, no action needed
Checked this session: TFL (`TflHelper.cs`), 4th quarter (`GameStateEventHelper.cs`), and penalty (`PenaltyHelper.cs` + the `"Penalty: Offense"` → routes-as-defense special case in `WebMainForm.cs:1497`) are all wired correctly and intentionally. Don't re-investigate these without a specific new symptom.

---

## Starting a fresh session on this

1. `git log --oneline -10` and `git status` — confirm master HEAD is at/ahead of `v1.0.67`, and that `GameWatcher.cs`'s `RemotePlay` process-name fix (uncommitted as of this handoff) is either committed or still present in the working tree.
2. Same pre-existing uncommitted Mac WIP files as every prior session (`AudioPlayer.Mac.cs`, `Bandroom.Mac.csproj`, `GameWatcher.Mac.cs`, `MacWebBridge.cs`, `PlatformStubs.Mac.cs`) — not touched, not mine, leave alone unless asked.
3. **Never run `release.ps1` without the owner saying "ppup"** or explicitly asking for a release — standing rule.
4. If picking up remote play: get the owner to actually test with a real PS5 + Remote Play session and report whether penalty detection works; that's the one open unknown.
5. If picking up UI work: start with the matchup screen (owner explicitly asked for it, has a reference image), then My Downloads.
