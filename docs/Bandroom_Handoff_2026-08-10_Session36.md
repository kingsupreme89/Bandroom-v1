# Bandroom Handoff — Session 36 (2026-08-10)

Picks up after Session 34/35 (whistle-volume fix, LOCK IN? restyle, state-machine audit). This
session's scope was UI/UX polish + bug fixes, running concurrently with Session 35's state-machine
work — did not touch `GameWatcher.cs` or the `src/Bandroom.Core/Helpers/*.cs` evaluators at all,
same file-ownership split Session 34 established. Touched `WebMainForm.cs`, `WebBridge.cs`,
`AudioPlayer.cs`, `ConfigStore.cs`, `BandAudioHook.csproj`, `wwwroot/index.html`, `wwwroot/app.js`,
`wwwroot/style.css`. **Nothing from this session is committed.**

## What changed this session

### 1. Left nav-rack sidebar (owner reference: a Spotify-style music-app mockup)
Replaced the old toolbar pill row (`.marketplace-tabs` + `.help-pill`s: The Bandroom/Sound Bank/My
Downloads/Auto-Assign/Sharing Guide/Shortcuts/Help & Guide) with a vertical icon+label rack
(`#nav-rack` in index.html, `.nav-rack*` classes in style.css, ~line 2980). Every button kept its
exact original `id`, so no `app.js` listener wiring needed to change — only the container/class
moved. Teams/Save/Lock-in/window controls stayed in the header. Discord pill hidden (not deleted —
its click/state listeners in app.js aren't null-guarded) per owner request to declutter for now.

### 2. Sound Booth visual redesign (owner reference: `1774_large@2x.jpg`, a hardware plugin-rack UI)
Rewrote `.sb-knob`/`.sb-meter`/`.soundbooth-tab`/`.sb-tile` CSS (style.css ~3355-3481): dashed
segmented track ring, team-glow drop-shadow on the arc, radial-gradient bezel behind each knob,
LED-style glowing borders/underlines on active tabs and tiles (replacing the old flat-fill active
state), glowing meter fill. Also fixed a real init-ordering gap in `initSoundBoothKnob` (app.js
~3271) — it now calls `sbKnobRender()` synchronously on creation instead of only after the first
async `rebind()` resolves, and `initSoundBoothRack()` is wrapped in try/catch with `console.error`.
**Not yet visually re-confirmed by the owner after the cache-busting fix in item 8 below** — every
screenshot taken of this feature this session was almost certainly showing stale CSS (see item 8).

### 3. Clipper waveform trimmer: zoom + pan (owner request)
`+`/`-`/reset buttons, Ctrl+wheel zoom, click-drag pan on empty canvas space, plain-wheel
horizontal scroll. `setTrimZoom()` scales both `canvas.width` (drawing buffer) and
`canvas.style.width` (CSS/viewport), `#clipper-trim-viewport` gets `overflow-x: auto`. Verified
correct by an independent audit agent (coordinate math via `getBoundingClientRect()` is scroll-safe,
zoom resets to 1 on every new trim session). app.js ~4738-5045, index.html ~490-502,
style.css ~1477-1490.

### 4. Lead-in whistle volume slider (owner request) — **and a real bug in it, fixed**
New `AudioPlayer.WhistleVolume` field, `SetWhistleVolumeFromWeb`/`GetWhistleVolumeFromWeb` bridge
methods, new slider in the Lead-In Whistle panel (index.html), registered in the Sound Booth's
`SB_KNOB_PARAMS` table too. **Session 34 later found and fixed a real bug this session introduced**:
the live per-tick volume-tracking loop in `AudioPlayer.Play()` was overwriting
`leadInReader.Volume = audio.Volume` on every 15-30ms tick with no `WhistleVolume` factor, wiping
out the slider's effect almost instantly. Both overwrite sites (fade-out + normal branch,
AudioPlayer.cs ~291/297) now correctly multiply by `WhistleVolume`. Verified present and correct by
an audit agent this session.

### 5. Volume settings now persist across restart (owner request)
New `ConfigStore.AudioSettings` record + `LoadAudioSettings()`/`SaveAudioSettings()`
(`audio_settings.json` in `UserDataRoot`, same pattern as `BigGameSettings`). Master/Home/Away/PA/
Whistle volumes load once at `WebMainForm` startup and applied to `AudioPlayer`'s static fields;
each `SetXVolumeFromWeb` setter persists back via a 400ms-debounced timer (`PersistAudioSettingsDebounced`)
so dragging a slider doesn't hammer the disk. **Not yet live-verified** (close app, reopen, check
sliders kept their values) — implemented and builds clean, but no confirmation round-tripped yet.

### 6. Clipper song-list bugs (owner report: "import song list never updates after adding from folder")
Real bug found: `_clipperAssignLibrary` (the cached song list shown in the Assign/Edit picker) was
only ever invalidated on a team switch — **never** after a song-pack import completed, so newly
imported songs never appeared in the list (even after searching) until the user happened to switch
teams and back. Fixed: the `bandroom:songpackready` handler now nulls the cache and, if the Assign
panel happens to already be open, live-refetches and re-renders it in place. Also added a batch
**"Add Songs..."** button (multi-select file picker via new `AddSongsBatchFromWeb`/`AddSongsBatch`
bridge methods) for adding several songs at once instead of repeating "Browse for file..." — added
songs go through the same `ConfigStore.ImportIntoSongsLibrary` copy-in as Browse does. Renamed
"Locate & Import" → "Import from Zip" for clarity (it pairs with the existing Google Drive download
flow). **Not yet live-verified by the owner.**

### 7. Real stacking/z-index bugs found and fixed
- `#resume-session-bar` was `z-index: 80`, above several modal scrims (`#auto-assign-confirm-overlay`
  at 60, `#team-picker-overlay` at 40) — dropped to 35, below every full-screen modal in the app.
- **"What's New" popping up stacked with Save Profile (or any other dialog)**: root cause was a
  one-directional check — `showWhatsNewWhenClear()` (added earlier this session) correctly stopped
  What's New from opening *onto* something else, but did nothing for the reverse case: if What's
  New was already showing (fired on its own 600ms-after-launch timer, before the user had touched
  anything) and the user then opened Save Profile, nothing ever closed What's New. Fixed with a
  `MutationObserver` watching every blocking-overlay id's `hidden` attribute, auto-dismissing
  What's New the instant any of them opens. **Confirmed fixed live** by the owner's own screenshot
  after this landed (Save Profile alone, no stacking) — this is the one item in this whole session
  actually confirmed working end-to-end.

### 8. Two dev-environment "bug blockers" added, one confirmed necessary, one confirmed insufficient
- **Orphaned `msedgewebview2.exe` processes**: force-killing `Bandroom.exe` during the dev
  rebuild-relaunch loop (instead of a clean shutdown) leaves child WebView2 processes running,
  bound to the same `WebView2Data` profile folder, holding cache files. Debug-only
  `KillOrphanedWebView2Processes()` (WebMainForm.cs, needs the new `System.Management` PackageReference,
  Debug-only in the csproj) queries `Win32_Process` via WMI and kills any `msedgewebview2.exe` whose
  command line references Bandroom's own `WebView2Data` path, run at the top of every
  `InitWebViewAsync()`. Confirmed via manual `Get-CimInstance` this session that it correctly leaves
  OTHER apps' WebView2 processes (Windows Search, Widgets) alone.
- **`Network.clearBrowserCache` (added earlier, still Debug-only in `InitWebViewAsync`) turned out
  to NOT be sufficient** for a much stranger caching bug — see "Not yet resolved" below. Left in
  place since it's harmless, but it did not fix the real issue.
- **DevTools access was completely blocked app-wide** (`AreDefaultContextMenusEnabled = false` +
  `AreBrowserAcceleratorKeysEnabled = false` together mean no F12 AND no right-click → Inspect) —
  every rendering mystery this session had to be diagnosed by re-reading source and hoping, with no
  way to check actual computed styles or console errors live. Fixed: Debug builds now re-enable
  `AreDefaultContextMenusEnabled = true` so right-click → Inspect works. **This should be considered
  a standing gap closed, not a one-off fix** — keep it Debug-only going forward, real users still
  shouldn't get a context menu.

## NOT yet resolved — the LOCK IN? button mystery (read this before touching `.matchup-btn` again)

Owner reported repeatedly, across at least 5 separate rebuild+relaunch cycles this session, that
`#btn-matchup` ("LOCK IN?") renders as a plain native `<button>` (light gray background, black
text, sharp corners, no rounding) instead of the intended themed pill
(`.matchup-btn` in style.css, ~3755: `color-mix()` team-tinted background/border, `appearance: none`,
`border-radius: 999px`, `animation: pill-glow-pulse`).

**Exhaustively ruled out this session, with live evidence, not just static reading:**
- The CSS rule itself is correct and present exactly once in `wwwroot/style.css` (no duplicate/
  conflicting block from a concurrent session).
- The deployed `bin/Debug/.../wwwroot/style.css` is byte-identical (`md5sum` match) to source.
- `#btn-matchup`'s `class` attribute in the live DOM is exactly `"matchup-btn"` (confirmed via live
  CDP `Runtime.evaluate`, not just reading the HTML source).
- No inline `style` is ever set on this element by app.js; `updateMatchupLabel()` only uses
  `classList.toggle`, never wipes classes.
- No other selector in style.css targets `#btn-matchup`/`.matchup-btn` with background/border/
  border-radius/appearance properties that could win a specificity fight (`#btn-matchup { flex; }`
  only sets flex/min-width).
- Not a drag-region/`-webkit-app-region` issue — no such CSS exists anywhere in this app; window
  dragging is native (`WM_NCLBUTTONDOWN`/`HTCAPTION` via `BeginWindowDrag()`), and the JS mousedown
  handler on `#drag-handle` already excludes clicks on `<button>` elements.
- Not Windows Forced-Colors/High-Contrast mode — other real `<button>` elements in the SAME window
  (nav-rack items, Sound Booth tabs) render with full custom styling with **no** `appearance: none`
  needed, which would be impossible if the OS were forcibly flattening all buttons.
- Not multiple/stale processes — confirmed via `Get-CimInstance Win32_Process` that the tested PID's
  `ExecutablePath` was genuinely `C:\Bandroom\bin\Debug\net10.0-windows10.0.19041.0\Bandroom.exe`,
  matching the just-rebuilt binary.

**Live CDP investigation (this session enabled `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`
and drove the running instance directly over the DevTools Protocol via a raw WebSocket from
PowerShell — a real, repeatable technique worth reusing for any future "why doesn't my CSS/JS show
up" mystery in this app, since in-app DevTools access was blocked until item 8 above landed):**
- `getComputedStyle(#btn-matchup)` returned `appearance: "auto"` (not `"none"`), `borderRadius: "0px"`,
  `backgroundColor: "rgb(240, 240, 240)"` — i.e. **none** of `.matchup-btn`'s declarations were
  computing, despite the class match.
- `document.styleSheets[0].cssRules` (the loaded, parsed stylesheet) had a rule count that looked
  truncated relative to the real file, ending at a rule that appears mid-file in source — **but this
  specific piece of evidence is suspect**, since the rule-count comparison used a crude
  `grep -c "^\."` line count as the "expected" baseline, which is not equivalent to actual parsed
  top-level CSSOM rule count (doesn't account for rules nested in `@media`, multi-line selectors,
  etc.) — **don't trust the "598 vs 653 rules" framing on its own**, it was never rigorously confirmed
  the file *should* parse to exactly 653 top-level rules.
- A raw in-page `fetch('style.css', {cache:'no-store'})` DID return the full, correct, untruncated
  171,627-byte file content (confirmed the real tail: `.whats-new-card-text { ... }`, matching the
  actual end of the source file) — so the underlying resource IS being served correctly over HTTP at
  the network layer.
- `Page.reload({ignoreCache: true})` + `Network.setCacheDisabled(true)` did NOT change the computed
  style result.
- A full cross-navigation (`Page.navigate` to `about:blank`, then back to `index.html`) did NOT
  change the computed style result either.
- A genuinely fresh, never-before-requested cache-busting query string
  (`style.css?v=<Date.now()>`, added to index.html's `<link>` tag via a `document.write` shim,
  same treatment given to `app.js`/`ui-bot.js`) **also did NOT change the computed style result** in
  the one clean test completed before the live Bandroom process got replaced out from under the
  debugging session (see below) — this is the most important negative result: it rules out *every*
  caching theory (browser HTTP cache, WebView2-internal virtual-host cache, stale on-disk file),
  since a URL that has never been requested before cannot be served from any cache.

**Session ended without a resolution.** The investigation was cut short because the live
`Bandroom.exe` process kept getting replaced (new PID) out from under the debugging session — most
likely the owner manually relaunching the app in parallel while this session was mid-diagnosis,
which killed the WebSocket connection to the DevTools Protocol repeatedly. The cache-busting
`<link>`/`<script>` change (index.html, see item 8's sibling changes) is still a reasonable
defensive fix to keep regardless — it can only help, and closes off an entire class of future
"my edit isn't showing up" reports — but it should **not** be assumed to be the fix for this
specific bug until re-verified with a stable connection.

**Real next steps for whoever picks this up:**
1. Re-run the exact live CDP technique above (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`,
   connect via `System.Net.WebSockets.ClientWebSocket` from PowerShell, `Runtime.evaluate` against
   `http://localhost:9222/json`'s `webSocketDebuggerUrl`) against a *stable* single instance — make
   sure nothing else (the owner, another session) is relaunching Bandroom.exe mid-investigation.
2. With DevTools access now unblocked (item 8), the fastest actual next step is almost certainly
   just: right-click the LOCK IN? button → Inspect → Elements → check the Styles pane directly.
   This alone would have resolved the ambiguity days ago; nobody actually did it this session
   because DevTools access was blocked until very late.
3. If `.matchup-btn`'s rule genuinely is missing/inert in the Styles pane despite being present and
   unique in source, the next things to check: whether `getMatchedCSSRules`-equivalent shows it
   filtered out for a `@supports`/`@media` reason not visible from a flat text read of the file;
   whether there's a Trusted Types / CSP-style content restriction silently dropping the stylesheet
   parse partway through (would explain a "truncated-looking" parse without it being a caching
   issue at all); or whether this element is somehow a *different* DOM node than the one `app.js`
   and `style.css` were both written assuming (e.g. two `#btn-matchup` elements existing briefly
   during some dynamic re-render, one styled and one not, with the unstyled one currently painted
   on top) — not yet checked.

## Verified this session
- `dotnet build BandAudioHook.csproj -c Debug` clean (0 warnings/errors) after every change,
  including a rebuild done concurrently with Session 35's own `GameWatcher.cs` edits (one build
  attempt caught them mid-save and failed on an unrelated `GameWatcher.cs` error that resolved
  itself on retry seconds later — not this session's bug, did not touch that file).
- 4 independent audit agents (C# build/bridge consistency, JS/HTML dead-link/duplicate-ID/CSS-conflict
  audit, per-fix correctness verification, clipper-zoom/nav-rack verification) all came back clean
  with no defects found, across the full diff as of mid-session.
- What's New/Save Profile stacking fix — confirmed live by the owner's own screenshot.
- Nav-rack sidebar and (at the time, before the caching mystery was understood) the `.matchup-btn`
  CSS — both **appeared** correct in one screenshot mid-session, before later screenshots showed
  the LOCK IN? button broken again. Given the caching investigation above, take any "confirmed
  working" screenshot of this app from this session with a grain of salt unless it was captured
  immediately after a rebuild+relaunch with no other Bandroom.exe process having been running
  beforehand.

## Not yet confirmed — real next steps
1. **LOCK IN? button** — see the dedicated section above, this is the main unresolved item.
2. Sound Booth visual redesign — not re-confirmed after the cache-busting fix landed; likely
   suffered from the exact same stale-render issue as the LOCK IN? button all session.
3. Ctrl+Shift+T test-hook panel — owner reported it still not opening across multiple rebuild
   cycles. The `openTestHook()` try/catch fix (Session 33/this session) is confirmed correct in
   source by an audit agent. Given everything else this session turned out to be a rendering/
   caching mystery rather than a logic bug, **do not assume this is fixed either** — re-verify with
   a stable DevTools connection (Console tab, check for a thrown error, or manually dispatch the
   keydown event via `document.dispatchEvent(new KeyboardEvent('keydown', {key:'t', ctrlKey:true, shiftKey:true}))`
   in the Console) before doing anything else to it.
4. Volume persistence (item 5) — implemented, builds clean, not yet round-tripped (close app,
   reopen, check sliders kept their values).
5. Clipper song-list fixes + new Add Songs button (item 6) — not yet live-verified.
6. Sound Booth meters/Preview — still not live-verified against a real game event (carried forward
   from Session 33/34).

## Carried forward from Session 35/34/33 (untouched this session)
1. State-machine audit findings (#12/#13 and beyond) — see Session 35's own handoff, entirely
   separate file scope from this session.
2. Conflict-prompt-before-autosave feature — not scoped, needs owner alignment (Session 34).
3. `voice_poc/.env` — still untracked, uncommitted, not gitignored; likely holds a secret.
4. **Not released** — commits sit on `master` past `v1.0.73` with no version bump/tag/Squirrel pack.
5. 3 deleted `guide/` files (Session 32) — still unexplained, still not touched.
6. Coverflow edge-fade mask + `.team-swatch-reflection` DOM wiring — CSS in place, JS side never
   wired up.
