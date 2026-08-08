# Bandroom Handoff — 2026-08-05, Session 8

Source: `D:\Claude\Projects\tools\BandAudioHook` (git, remote `origin` =
https://github.com/kingsupreme89/Bandroom-v1). Continued directly from
Session 7's handoff (`D:\Claude\Projects\Bandroom_Handoff_2026-08-05_Session7.md`
— read that first, this doc only covers what changed since).

## What Bandroom is, in one paragraph

Bandroom is a Windows desktop app (WinForms host + a WebView2-rendered
HTML/JS/CSS UI) that plays a marching-band-style sound cue automatically
when it detects a real-time event in a college football video game (via
screen-capture OCR of the on-screen down/distance/score ribbon and the
possession-color bar), or on demand from the UI. The user assigns songs to
"situations" (Touchdown, PAT, Kickoff, Turnover, Down 1st–4th, etc.) per
team profile, sets a Home/Away matchup before kickoff, and the app fires
the right team's cue automatically as the game plays.

## Project layout — where everything lives

- **`WebMainForm.cs`** — the WinForms host window. Owns the WebView2
  control, the `GameWatcher` (OCR loop), the `KeyboardHook` (manual hotkeys),
  the active `_config` (current team's trigger→song mapping), and every
  `...FromWeb` method the JS side calls into via the bridge. This is the
  biggest file and the one you'll touch most.
- **`WebBridge.cs`** — the JS↔C# bridge object, exposed to the page as
  `window.chrome.webview.hostObjects.bandroom` (aliased to `bridge` in
  `app.js`). Every method here is a thin one-liner that calls into
  `WebMainForm`. If JS needs to call new C# logic, add the method here
  first, then implement it in `WebMainForm.cs`.
- **`wwwroot/index.html`** — the actual UI markup (no framework, plain
  HTML). Team picker overlay, matchup dialog, situations panel, settings,
  etc. all live here as hidden/shown sections.
- **`wwwroot/app.js`** — all UI behavior/state. No build step — this file
  is served directly by WebView2 via `SetVirtualHostNameToFolderMapping`
  (`https://appassets/...`). Edit it, rebuild the C# host (or just
  `dotnet build` — WebView2 loads it live from `wwwroot\` at runtime, so
  JS/CSS-only changes don't even need a rebuild if you're running from
  `bin\Debug\...`), reload the app window.
- **`wwwroot/style.css`** — all styling. Dark glass/neon theme, CSS custom
  properties for team colors, hand-rolled animations (no framework).
- **`AudioPlayer.cs`** — the actual playback engine (NAudio). `Play(path,
  volumeOverride, interruptPrevious)` is the one function basically
  everything routes through. Has a 20s per-file-path cooldown (debounces
  duplicate OCR detections) and, as of this session, an `interruptPrevious`
  flag that stops whatever's currently playing before starting the new
  clip.
- **`GameWatcher.cs`** — the OCR polling loop. Screenshots specific
  fractional regions of whatever game window is focused, reads
  text/colors, raises events (`DownChanged`, `PossessionChanged`,
  `TackleForLossDetected`, `RegionChanged`, etc.) that `WebMainForm`
  subscribes to.
- **`ConfigStore.cs`** — per-team profile persistence (JSON on disk) +
  `BuildDefault()` (the trigger list every new team profile starts with).
- **`TeamColors.cs`** — the full 133-team roster (names + primary/secondary
  hex colors). Source of truth for "does this team exist in the app yet."
- **`TeamLogos\`** — logo image files per team, referenced by filename
  match against `TeamColors.cs` names. **28 teams have logos as of this
  session** (11 Big Ten + some SEC from earlier sessions) — the other
  ~105 do not yet.
- **`TeamBackgrounds\`** — per-team stadium/backdrop photos, same
  filename-match convention.
- **`Assets\`** — new this session. Currently just `nfl-draft-chime.mp3`,
  the bundled UI chime (see below). `CopyToOutputDirectory` is wired in
  the `.csproj`; add more files here the same way if needed later.
- **`scripts\slice_logo_sheet.ps1`** (and the older `slice_logos*.ps1`
  variants) — the tool used to cut a multi-team logo sheet (screenshotted
  from wherever the user finds team logo art) into individual per-team
  files with tight, slightly-negative-padding crops. See Session 7's
  handoff for the exact tuning story (`-3.5%` padding value, why it's
  needed).
- **`release.ps1`** — the entire ship pipeline. Run via `powershell
  -NoProfile -ExecutionPolicy Bypass -File release.ps1 -Notes @'...'@`.
  Bumps patch version from the latest git tag, `dotnet publish` (Release,
  win-x64), Squirrel pack (delta+full nupkgs + Setup.exe), git tag + push,
  `gh release create` with all Squirrel assets attached. **Does NOT commit
  source changes for you** — commit and `git push origin main` yourself
  first, then run this. The keyword **"ppup"** (in chat with the user)
  means "run this exact pipeline with real -Notes bullets describing what
  changed." Confirmed working this session (shipped v1.0.19 → v1.0.21).

## Shipped this session (v1.0.19 → v1.0.21)

- **v1.0.19** — Draft chime + cue priority + UI polish:
  - Bundled `Assets\nfl-draft-chime.mp3` (from the user's Downloads
    folder) as the shared "grab attention" chime. Replaced two
    synthesized-tone chimes with it: plays on **app open** (`Load` handler
    in `WebMainForm.cs`), on **GAMETIME button press**
    (`ConfirmGametimeFromWeb`), and on **update detected**
    (`InitAutoUpdater`'s background check) — the update case matters
    because the app is normally left running on one computer at all
    times, so this needs to fire without a restart, not just at launch.
  - `AudioPlayer.Play` gained an `interruptPrevious` parameter. Real
    in-game trigger cues (via `WebMainForm.FireEvent`, i.e. Touchdown,
    PAT, Kickoff, Turnover, Downs, etc.) now pass `true` — explicit user
    call: "second event always takes priority," meaning the newest live
    event cuts off whatever cue was still playing rather than overlapping
    with it. One-off UI chimes (app open/GAMETIME/update) don't use this.
  - New: universal UI click sound. `WebBridge.PlayClickSound()` →
    `WebMainForm.PlayUiClickSoundFromWeb()`, a tiny synthesized tick, fired
    from a single document-level click delegate in `app.js` (matches
    `button, .team-swatch, .rail-item, .category-row`) instead of being
    wired into every individual click handler.
  - Team tiles now flash the **team's own color** on press (`--tile-color`
    CSS custom property, set per-tile in `fillTeamSwatch`) instead of the
    generic cyan accent glow every other button uses.
- **v1.0.20** — Real bug fixes, not just polish:
  - **Fixed the long-standing "squashed team picker tiles" bug** that
    Session 7 theorized was WebView2 disk-cache staleness. It wasn't.
    Root cause: `.team-picker-grid` set `align-content: start` but never
    set `align-items`, which defaults to `stretch` — that fought
    `.team-swatch`'s `aspect-ratio: 1`, stretching tiles to fill their
    grid row's full height while width stayed pinned to the `1fr` column
    track, producing tall skinny bars. Fix: added `align-items: start` to
    `.team-picker-grid`. **This is now confirmed the real root cause** —
    the earlier cache-disable fix from Session 7 was real and worth
    keeping, but wasn't what was causing this particular symptom.
  - UI click sound was originally a pure sine-tone "pop" — user asked for
    something that reads as a mechanical click instead, and quieter.
    Replaced with a short low-pass-filtered noise burst (~10ms, fast
    exponential decay) at lower amplitude.
  - Team logo hover is now a **real macOS-dock-style magnify**: mousemove
    on each grid computes distance from cursor to every tile's center and
    scales each with falloff (not a binary `:hover` pop) — see
    `enableDockMagnify()` in `app.js`, wired to every team grid in the app
    (sidebar `#team-grid`, `#team-picker-grid`, `#matchup-away-grid`,
    `#matchup-home-grid`, `#onboarding-grid`).
- **v1.0.21** — Follow-up bug fix found in this session's own review pass:
  the dock-magnify sets `transform` inline on every mousemove, which beats
  the stylesheet's `.team-swatch:active` press-down rule (inline always
  wins over a class selector) — so clicking a magnified tile silently ate
  the "physical press" feedback. Fixed by tracking the currently-pressed
  tile and folding a small extra shrink into its own scale calculation in
  `enableDockMagnify` instead of relying on `:active` at all.

## Shipped later in this same session (v1.0.22 → v1.0.26)

The above (v1.0.19–v1.0.21) was written mid-session as a checkpoint. The session kept going
much longer than expected, driven by a live back-and-forth with the user testing each release
in real time and reporting bugs immediately. Rest of what shipped, in order:

- **v1.0.22**:
  - **Window is now resizable.** `FormBorderStyle.None` (see `WebMainForm`'s constructor) drops
    the OS-drawn window entirely, including the invisible resize border — `WindowState` toggle
    was the *only* way to change size before this. Fixed by overriding `WndProc` in
    `WebMainForm.cs` to handle `WM_NCHITTEST` directly, returning the standard `HTLEFT`/`HTRIGHT`/
    corner codes near the window edges (6px margin) so `DefWndProc` drives normal OS drag-resize
    without needing `WS_THICKFRAME`. New hit-test constants live in `Native.cs`.
  - **Real fix (attempt #3) for squashed team-picker/matchup tiles.** Two CSS-only fixes
    (`align-content: start`, then `align-items: start`) didn't hold up under further live
    testing — the tiles were still reported broken. Replaced entirely with a JS-measured
    approach: `squareUpTiles()` in `app.js` runs after every grid render, measures the first
    tile's actual rendered width via `getBoundingClientRect`, and sets every tile's `height`
    inline to match — sidesteps whatever CSS grid/`aspect-ratio`/`stretch` interaction was
    actually happening in this WebView2 runtime, rather than guessing at a fourth CSS rule.
    Also fixed the matchup dialog rendering its grids while still `display:none` (width reads
    as 0 then, so the fix silently no-opped) — reordered to unhide before render.
  - Hover magnify simplified: only the exact tile under the cursor scales (2x), no
    neighbor-distance falloff dock-wave sweep like the first version had. Cleaner and cheaper.
  - Nav rail buttons and window control buttons (minimize/maximize/close) enlarged.
  - Auto-update polling made periodic (loops every N minutes for the app's lifetime) instead of
    running once at startup — see the v1.0.19 section above, this was a real live-confirmed bug:
    a release shipped while the user's app was already open never chimed/pulsed until they
    manually clicked Update after being told to.
- **v1.0.23**: `ConfigStore.BuildDefault`'s Down event labels renamed "Down: 1st" → "1st Down"
  (etc) — only affects newly-created profiles, not existing saved ones (baked into each
  profile's JSON on disk). Also added `updatePageZoom()`: scaled the whole page via Chromium's
  `zoom` CSS property against the 1920×1080 default launch size, so resizing the window would
  scale fonts/spacing proportionally instead of just revealing empty space.
- **v1.0.24 — urgent revert.** The v1.0.23 `zoom` scaling **broke click hit-testing app-wide**,
  confirmed live: matchup screen team tiles became completely unclickable. Chromium's `zoom`
  property rescales rendering but pointer-event coordinates don't reliably remap in every
  WebView2 runtime version, especially combined with the per-tile inline `transform: scale()`
  from the hover-magnify feature — the combination is a known source of click-target
  misalignment. **`updatePageZoom` was fully removed, not just disabled** — see the comment
  left in its place in `app.js`. If resize-scaling comes back, it needs a hit-test-safe
  approach (rem-based sizing recalculated on resize, no `zoom`/`transform` involved), and should
  be tested very carefully against real clicks before shipping, not just visually.
- **v1.0.25**:
  - Situations list (the Assign/Preview/Stop panel per event) redesigned from stacked
    full-width rows into wrapped **"island" tiles** with a small LED status dot: green pulse
    (assigned + confirmed), amber pulse (assigned, not yet confirmed live), dim/off
    (unassigned) — status readable at a glance. `.situation-row` class name kept as-is for
    minimal JS churn; only the CSS layout and the new `.situation-led*` markup changed.
  - **Update download progress dialog.** The manual Update button used to silently download
    then instantly relaunch the app the moment it finished — zero on-screen feedback, which
    read as the app randomly closing. Now: `WebMainForm.ShowUpdateDialogFromWeb` dispatches
    `bandroom:updatedownloading` → `bandroom:updateprogress` (real percent via Squirrel's
    `UpdateManager.UpdateApp(Action<int> progress)` overload) → `bandroom:updateready`, and
    `app.js` shows a progress bar + "Restart Now" button (`#update-overlay` in
    `index.html`) instead of auto-restarting. New bridge method:
    `WebBridge.RestartForUpdate` → `WebMainForm.RestartForUpdateFromWeb`.
- **v1.0.26**:
  - **Matchup VS backdrop is now a diagonal split**, not a straight vertical line — explicit
    user ask ("pulsing seam splitting the 2 schools' team colors"). `.backdrop-vs-half`
    changed from 50%-width flex items to absolutely-positioned full-screen boxes with
    `clip-path` polygons cutting the diagonal; a new `#backdrop-vs-seam` element (set via
    `--away-color`/`--home-color` custom properties in `applyVsBackdrop()`) pulses along the
    same cut in both teams' actual colors. The old `.backdrop-vs-underglow` bottom bar was
    retired (would have spanned the full screen width for both halves once they became
    full-width boxes, instead of each half's own 50%). Each half's logo/name content also had
    to be explicitly pushed toward its own side (`padding-left`/`padding-right` +
    `align-items: flex-start`/`flex-end`) since centering on a full-screen box would otherwise
    put both teams' content on top of each other at dead screen-center.
  - Auto-update poll interval shortened from 10 to 3 minutes (was originally added at 10 min in
    v1.0.22; shortened partway through this session's heavy live-testing cadence).
  - Header brand mark ("B") redesigned: fills with the **active team's own color** (gradient +
    glossy sheen, same technique as team tiles) with a pulsing/breathing teal (`--accent`)
    outline, instead of a flat static teal square — explicit ask to make it "extreme"/the
    loudest element in the header. The VS-backdrop's center emblem "B" stays neutral teal
    (`#backdrop-vs-emblem .brand-mark` override) since it sits between both teams and picking
    one side's color there would be confusing.

## User's working style this session (worth knowing before you start)

The user fired off a rapid, sometimes-garbled stream of follow-up instructions while work was
still in progress on the previous one (typos are frequent — read for intent, not literal text).
Explicit instruction, given mid-session and saved to memory
(`feedback_queued_instructions_workflow.md`): treat these as an ordered queue. Finish the
current task fully (including a build + bug-check pass), *then* move to the next one — don't
batch multiple asks into one change, and don't skip ahead. If a queued ask is a real
UI/feature redesign and is ambiguous, one clarifying question is fine (the user has
explicitly asked for this at least once — "let me preview one first before you do all
events") — but don't turn that into stalling on every small tweak.

Also worth knowing: this user tests every release live, immediately, and reports back fast.
Expect rapid ship→bug-report→fix→reship cycles, not long gaps between "ppup" calls. Several
real regressions this session were only caught this way (the zoom/click-hit-testing break in
v1.0.23→v1.0.24 in particular) — don't skip the build+bug-check step before shipping just
because the user seems to be moving fast.

## Known issues / open items going into next session

**Current shipped version as of this doc: v1.0.26.** Always `git log --oneline -10` to check
you're not reading stale info — this session shipped 8 releases in a row.

- **Team-picker/matchup tile squashing: fix #3 (JS-measured `squareUpTiles`, v1.0.22) has not
  been explicitly re-confirmed by the user** as of this doc, though no further complaints came
  in after it shipped, while several other things were being actively tested. Two earlier
  CSS-only attempts (v1.0.20, then a second one) both turned out NOT to fix it despite looking
  correct on paper — don't trust a CSS-reasoning-only fix for this specific bug again. If it's
  reported broken again, `squareUpTiles()` in `app.js` is the actual mechanism now (not CSS) —
  debug by checking whether it's actually running (add a `console.log` or check the tile's
  inline `style.height` in devtools) before touching CSS again.
- **Resize-based UI scaling is unsolved and currently absent.** Tried once (`zoom` CSS
  property, v1.0.23), broke click hit-testing app-wide, fully reverted (v1.0.24). The window is
  resizable (v1.0.22, `WM_NCHITTEST` in `WebMainForm.WndProc`) but nothing scales with it
  anymore — a bigger window just reveals more empty space. If asked to revisit this, do NOT use
  `zoom` or a `transform: scale()` wrapper without first verifying clicks still work correctly
  in the *actual* WebView2 runtime (not just visually) — this exact combination (scaling +
  the hover-magnify's inline `transform`) is what broke it last time.
- **`AudioPlayer`'s 20-second cooldown is per-file-path**, shared across
  *all* callers of `Play()` including the draft chime. If two chime
  triggers fire on the exact same file within 20s of each other (e.g. app
  opens and GAMETIME gets pressed almost immediately after), the second
  one is silently dropped. Flagged to the user, not yet fixed — ask if
  it's actually been a problem before spending time on it.
- **Logo art: still ~105 of 133 teams missing** (28 have logos as of this session — 11 Big Ten
  + earlier SEC). User's established process (locked in Session 7, still in effect): show one
  sheet at a time from their Downloads folder, guess each team by tile position, wait for
  confirm/correction, only THEN slice+crop+commit. `mac.png`, `mw.png`, `american.png`/
  `american 2 i think.png`, `sun belt.png`/`sun belt i think'.png`, and two unreviewed
  `Create_3D_*`/`Make_3D_logo_button_sheet_*.jpeg` files are still sitting there unsliced. User
  asked this session to "go ahead and match the rest to the best of your ability, I'll verify
  after" — a looser mandate than Session 7's strict per-sheet confirm-first process, but this
  wasn't actually acted on yet before the session moved on to other things. Confirm with the
  user which process they actually want before doing a big batch: the looser "just do your
  best" instruction, or the original strict per-sheet confirmation loop.
- Carried over from Session 7, still untouched: Flag OCR region still
  uncalibrated (`FxW=0, FxH=0` in `GameWatcher.cs`), Tackle for Loss
  detection still "not yet confirmed" in a live game, Discord version-reset
  decision undecided.
- The **release.ps1 pipeline pushes the git tag but not the `main` branch
  commit** — you must `git push origin main` yourself before or after
  tagging, or the tagged commit won't be visible on GitHub's default
  branch view (the tag itself is still correct/pointing at the right
  commit either way, this is just a visibility/hygiene thing). Caught and
  fixed within this session (v1.0.19's commit was pushed late) — every
  commit since has been pushed to `main` immediately before tagging.
- Two items the user asked for but weren't reached by the end of this session: a Discord
  "for dummies" instruction PDF (2 images max, was in progress via the `pdf` skill when the
  session's other bug-fix work took priority) and a Discord post listing planned upcoming
  features. Ask the user if these are still wanted.

## Starting a fresh agent session on this project

If you're a new agent picking this up cold:

1. Read this file, then Session 7's handoff
   (`D:\Claude\Projects\Bandroom_Handoff_2026-08-05_Session7.md`) for
   deeper history on the logo-import project and the trigger-logic
   overhaul.
2. `cd D:\Claude\Projects\tools\BandAudioHook` and `git log --oneline -15`
   to see exactly what's landed since this doc was written — treat this
   doc as a snapshot, not living truth.
3. Before touching UI code, skim `wwwroot/index.html` +
   `wwwroot/app.js` together — there's no component framework, so
   "where does X live" is just "which `<div id=...>` and which function
   touches it."
4. Before touching trigger/OCR logic, read `GameWatcher.cs` and
   `WebMainForm.cs`'s `On*Changed` handlers together — the whole pipeline
   is: OCR reads a region → raises a C# event → `WebMainForm` resolves it
   to a `TriggerEntry` in `_config` → `FireEvent` → `AudioPlayer.Play`.
5. `dotnet build` in the project root to confirm you're starting from a
   clean compile before making changes.
6. **Never run `release.ps1` without the user explicitly saying "ppup"**
   (or literally asking for a release) in the current conversation — it's
   a real, irreversible GitHub release + tag push affecting a live app
   already installed on the user's machine.
