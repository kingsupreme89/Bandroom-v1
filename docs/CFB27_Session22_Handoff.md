# Bandroom — Session 22 Handoff

## Where this picks up from
Session 21 ended on a decision, not a build: pivot the whole UI layer to WebView2 (HTML/CSS/JS
hosted in a WinForms `WebView2` control) for real glassmorphism blur, because WinForms can't do
backdrop blur. **Session 22 actually built that WebView2 rebuild, end to end, and it's now the
live app** (`Program.cs` launches `WebMainForm`, not the old native `MainForm`). Backend
(audio/OCR/hotkeys/config) is untouched C#, exactly as planned — only the UI layer moved.

**Status right now: code compiles clean (0 warnings/0 errors), app runs, and most of the core
loop (pick team → browse situations → assign/preview/stop → adjust volume/reverb/fade-delay →
toggle watching) works end-to-end in the real running app, verified by launching it repeatedly
this session, not just by reading code.**

---

## What got built this session

### New files
- **`wwwroot/index.html` / `style.css` / `app.js`** — the entire UI. v4 layout regions (chrome
  bar, icon rails, left team panel, center column, right Adjust panel) kept, but re-skinned with
  real `backdrop-filter: blur()` glass panels instead of WinForms flat-panel approximations.
- **`WebMainForm.cs`** — replaces `MainForm.cs` as the app's window. `FormBorderStyle.None`
  (chrome bar is HTML now, no OS titlebar, no URL bar — confirmed as a requirement multiple
  times this session). Hosts the `WebView2` control, wires `GameWatcher`/`KeyboardHook` exactly
  like the old `MainForm` did, exposes methods the JS bridge calls into.
- **`WebBridge.cs`** — the JS↔C# bridge object (`chrome.webview.hostObjects.bandroom` in JS).
  Thin adapter over existing backend classes (`ConfigStore`, `CategoryMap`, `TeamColors`,
  `AudioPlayer`) — no new business logic, just JSON marshalling.
- **`wwwroot/fonts/Outfit-*.ttf`** — copies of the app's existing embedded font, loaded via
  `@font-face` in the web page (not a new font, same one WinForms controls already used).

### Old files, status
- **`MainForm.cs` and the whole native v4 rebuild (`IconRail.cs`, `TeamGridPanel.cs`,
  `AdjustPanel.cs`, `ChromeBar.cs`, `TopBar.cs`, `LeftPanel.cs`, `CategoryMixPanel.cs`,
  `SessionPanel.cs`, `LiveFeedPanel.cs`, `TeamWipeOverlay.cs`, `ConfettiOverlay.cs`,
  `QuickAssignForm.cs`) are now DEAD CODE** — `Program.cs` no longer references `MainForm`, none
  of these are used by `WebMainForm`. They still compile (nothing was deleted), but they're
  unreachable. **Next session should probably delete these outright** rather than keep dragging
  them along — confirm with the user first since I didn't get explicit "yes delete" this session,
  just an implicit "not used anymore."
- **`ShortcutsForm.cs`** — kept and rewritten (see below), still used (Help button).
- **`TrimmerForm.cs`, `AssignTrackForm.cs`, `SettingsForm.cs`** — kept and still used, called
  from `WebMainForm` the same way `MainForm` called them (native modals layered on top of the
  WebView2 window, e.g. file browse, settings, trim). Not ported to HTML this session.

### csproj changes
- Added `Microsoft.Web.WebView2` package reference (1.0.2792.45).
- Added `<Content Include="wwwroot\**\*">` with `CopyToOutputDirectory` so the web assets ship.
- **Fixed a real bug**: added `<Content Include="TeamBackgrounds\**\*">` too — this was MISSING
  before, meaning `TeamBackgrounds\` was never copied to `bin\`, so team selection never changed
  the backdrop image in ANY session including this one until caught. `ConfigStore` only calls
  `Directory.CreateDirectory` on that path, which silently creates an empty folder if the real
  one wasn't copied — no error, just a black backdrop. **If new asset folders get added later
  (e.g. `TeamLogos\`), remember to add them to the csproj too, same pattern.**

---

## UI decisions locked in this session (in the order the user actually asked for them)

1. **No URL bar / no OS window chrome** — confirmed multiple times. `ChromeBar.cs`'s own comment
   already said "no URL bar (explicitly dropped)" from Session 21; ported that same look to HTML
   (`#chrome-bar` div with only traffic lights + decorative glyphs).
2. **v4 layout regions kept**, but re-skinned for real blur (user chose this over reverting to
   the flat situation-list layout when explicitly asked A/B).
3. **No "0 Cues Fired" stat hero in the center** — user was explicit: center column should just
   show the team stadium background, nothing else. Implemented as an empty div, background shows
   through the transparent center-column.
4. **Categories: horizontal, not vertical** — after a clarifying question (rails vs categories
   list), user meant the category list specifically. First pass wrapped chips in the narrow
   240px sidebar (looked no different since chips wrapped to one-per-line anyway); user's
   correction ("buttons on the side aren't doing anything... need descriptions") plus follow-up
   feedback made clear the categories needed to be pulled OUT of the sidebar into their own
   full-width horizontal strip above the center column — that's the final `#category-bar`.
5. **Clicking a category should open an inline dropdown of that category's situations**, not a
   native modal — this was a real UX correction (`QuickAssignForm` modal was the old flow,
   removed). Also explicitly asked for an **"All"** chip to browse all 33 situations at once.
   Implemented as `#situations-panel`, populated via `WebBridge.GetEventsForCategory`.
6. **Rail buttons need visible text labels, not icon-only with tooltips** — and every rail button
   needs to actually DO something (several were no-ops in the native version's `SetupRails`,
   ported as no-ops in the first HTML pass too — fixed to wire real actions: Teams → full picker,
   Categories → open "All" situations, Feed → toggle live feed *(not yet ported, see below)*,
   Assign → open "All" situations, Help → real instructions modal, Adjust → focus right panel,
   Effects → test-fire a cue).
7. **"Teams" rail button → full team picker overlay**, not just highlighting the small always-
   visible sidebar grid. Built `#team-picker-overlay`: centered glass modal, search box, grid of
   all teams, click to select and close. This came after two rounds of "doesn't do anything" /
   "just has a team selected" feedback — the fix that actually landed was building a real
   dedicated "choose a team" screen, not tweaking the highlight-and-scroll behavior.
8. **Fire Sensitivity → real fade-delay control, not decorative.** User guessed correctly that
   the old "Fire Sensitivity" slider (which had literally zero backing logic, per the native
   `AdjustPanel`'s own code comment) was supposed to be a delay. Wired it to
   `AudioPlayer.FadeStartSeconds` — seconds before a fired cue starts fading out. Default changed
   from 9.0 to 10.0 seconds. **Explicit requirement: no fade-in, only fade-out** — confirmed
   `AudioPlayer.Play` never had a fade-in ramp to begin with (volume jumps straight to full), so
   only the default/label needed fixing, not new playback logic.
2. **Team backgrounds should be dimmer with a pulsing neon border in the team's secondary color**
   — last thing built this session. Scrim opacity raised slightly (was almost invisible-light
   before), and every `.glass` panel now has a `neon-pulse` CSS animation using a
   `--team-secondary` custom property that JS updates on team select
   (`document.documentElement.style.setProperty(...)`). Uses `color-mix()` — confirm this renders
   correctly in the actual WebView2 Chromium runtime version next session; wasn't independently
   verified against the shipped WebView2 Evergreen runtime, only assumed compatible (Chromium
   111+, which any current WebView2 install should be well past, but double-check by eye).
3. **Bandroom brand wordmark made bigger/more exciting** — 15px → 24px Outfit Bold, white→cyan
   gradient text fill, soft glow via `filter: drop-shadow`. User said "keep the other fonts the
   same" — only the brand wordmark changed, nothing else re-fonted.

---

## Real bugs found and fixed this session (not just feature requests)

1. **TeamBackgrounds never shipped to `bin\`** (see csproj section above) — the actual root
   cause of "team backgrounds don't seem to work," found by checking the build output directory
   directly rather than just re-reading the C# code, which looked correct in isolation.
2. **CSS specificity bug on the team picker overlay** — `#team-picker-overlay { display: flex }`
   (ID selector, specificity 1-0-0) was silently beating the browser's built-in
   `[hidden] { display: none }` rule (attribute selector, lower specificity), so toggling the
   `hidden` attribute from JS did nothing visually. Both "the × doesn't close it" and "can't
   click off to close it" were the same root cause. Fixed with an explicit
   `#team-picker-overlay[hidden] { display: none }` override. **Worth remembering as a general
   pattern**: any other place that toggles `hidden` on an element with its own ID-selector CSS
   rule needs the same explicit `[hidden]` override, or the same bug repeats. Didn't audit every
   other `hidden`-toggled element for this same bug class this session (situations-panel appears
   fine since it has no conflicting ID-level `display` rule, but wasn't stress-tested).
3. **Replay double-fire bug**: user reported a song fired, a replay overlay appeared, and the
   same song fired again when the live feed reappeared — almost certainly `GameWatcher`'s OCR
   re-detecting the same on-screen state (e.g. "First Down") after the replay clears. Fixed with
   a blunt but effective 20-second global fire cooldown in `AudioPlayer.Play` (static
   `_lastFireUtc` + `FireCooldown = TimeSpan.FromSeconds(20)`), rather than trying to fix OCR
   debouncing directly. **This is a band-aid, not a root-cause OCR fix** — if the user wants true
   per-trigger debouncing (e.g. still allow a DIFFERENT cue to fire during another one's cooldown)
   instead of a global lock, that's a design choice to revisit, not implemented that way now.
4. **Team grid clipping/cutoff** — `.team-grid` had its own `max-height: 220px; overflow-y: auto`
   nested inside `.side-panel`'s own `overflow-y: auto`, so the last row got clipped mid-tile by
   the inner scrollbox before the outer one ever kicked in. Removed the inner scroll box entirely
   — the side panel's own scroll now handles it.
5. **`ShortcutsForm` (Help) was stale and unhelpful** — described the old abandoned Ctrl+K
   quick-assign modal flow, which doesn't exist anymore. Rewrote as a real 6-step how-to-use
   guide matching the actual current UI (pick team → browse situations → assign → preview/stop →
   watching toggle → adjust panel).
6. **Trim dialog used NumericUpDown spinners, user wanted sliders** ("like the volume slider").
   `TrimmerForm.cs` converted from `NumericUpDown` start/end fields to `TrackBar` sliders at
   0.1s resolution, with live-updating "X.Xs" labels. Save/Preview/Stop logic untouched, it was
   already correct — this was purely a control-type swap.

---

## Feature requests raised but explicitly NOT built yet — still open

- **Team logos.** Long thread this session:
  - User has a CFB27 game save file (`ROSTER-Official`, ~12MB). Checked its header —
    it's the proprietary `FBCHUNKS` compressed save format (zlib chunks), same family of format
    other sessions have been reverse-engineering (see `CFB27 kernel bytecode analysis`, `Ghidra
    Phase 2`, `MMC Editor decompilation` in memory/project history). **Roster saves hold
    player/team stat data, not logo textures** — confirmed by inspecting the raw bytes, not
    guessed. Team logo art (if extractable at all) would live in the base game's asset bundles,
    which is what Frosty Editor is for, not this file. **I don't have a confirmed exact path
    inside Frosty for where team logos live in this build's asset layout — didn't want to send
    the user on a guessed hunt, so this is still an open question for next session or in-app
    Frosty exploration.**
  - User then supplied a single collage image with ~156 team-logo-shaped tiles in a 12×13 grid.
    **Rejected as an unreliable source**: many teams repeat multiple times (Penn State, Texas
    A&M, Notre Dame, West Virginia each appear 2-4+ times with sometimes-different art), there's
    no legend, and I have no reliable way to map "cell 47" to a specific one of the 148 teams in
    `TeamColors.All` without guessing. Asked the user how to proceed (slice-and-let-them-
    manually-rename, vs. skip and keep placeholders) — **got dismissed, no answer chosen yet.**
    Don't re-attempt auto-mapping that sheet without either a legend or explicit "just slice it,
    I'll sort it" confirmation.
  - **Current state: placeholder monogram badges** (2-letter initials, e.g. "AL" for Alabama,
    computed in `WebBridge.Initials()`) render on every team swatch — sidebar grid, team picker
    grid, everywhere. Explicitly commented in the CSS/JS as swappable once real logos exist. This
    is a reasonable holding pattern, not meant to be the final look.
  - If the user does get a clean, individually-labeled logo pack (a folder of `<TeamName>.png`
    files, or a zip with clear names) — that's the easy path, same drop-in convention as
    `TeamBackgrounds\`: create `TeamLogos\`, add it to the csproj's `Content` items (don't forget
    this step, see the TeamBackgrounds bug above), add a `GetTeamLogoUrl` bridge method mirroring
    `GetTeamBackgroundUrl`, swap the `<div class="team-swatch">` monogram for an `<img>` when a
    logo exists, monogram as fallback otherwise.

- **Live Feed panel not ported.** `ToggleLiveFeedFromWeb()` in `WebMainForm.cs` is currently a
  no-op stub with a `/* TODO */` comment. The native `LiveFeedPanel.cs` still exists but isn't
  wired into the WebView2 shell at all. The "Live Feed" header button and rail items call it,
  but nothing visibly happens yet. **This is a known gap, not a bug** — just wasn't built this
  session, ran out of runway on higher-priority fixes.

- **Onboarding wizard + drag-and-drop sound import** (carried over from Session 21's task list,
  tasks #2/#3) — still not started. Was explicitly deferred in Session 21 pending the UI
  direction settling, which it now has (WebView2 confirmed and mostly built) — **these are
  reasonable candidates to pick up next session** now that the shell exists to build them into.

- **Confetti overlay / toasts** (`ConfettiOverlay.cs`, `ToastManager.cs`) — native, not ported.
  `TriggerEffectsTestFromWeb()` fires a test cue but doesn't trigger any visual confetti/toast in
  the new HTML shell (the old `_confetti.Burst()` / `_toasts.Show(...)` calls from `MainForm`
  weren't carried over — `WebMainForm.FireEvent` is now much thinner than the old one).

---

## Things that need re-verification next session (not fully proven, just assumed)

- **`color-mix()` CSS function** used in the new neon-pulse border animation — should work on any
  current WebView2 Evergreen runtime (Chromium 111+), but wasn't explicitly confirmed rendering
  correctly against this machine's actual installed WebView2 runtime version. If borders look
  flat/wrong (no pulse, or a hard color swap instead of a mix), check `--team-secondary` is
  actually being set (open devtools if possible) before assuming the animation itself is broken.
- **`-webkit-app-region: drag`** is set on `#chrome-bar` in CSS but **does nothing in WebView2**
  (that's an Electron-only feature). The actual drag mechanism is `WebBridge.BeginDrag()` →
  `WebMainForm.BeginWindowDrag()` → classic Win32 `ReleaseCapture` + `WM_NCLBUTTONDOWN`/
  `HTCAPTION` trick, wired via `onmousedown` on the chrome bar div. This was tested to compile
  but window-dragging-by-chrome-bar wasn't explicitly confirmed working by the user this session
  — worth a quick sanity check next time (click-drag the top bar, does the window move).
- **Situations panel `hidden` toggling** — works via the default UA `[hidden]` rule since
  `#situations-panel` has no conflicting ID-level `display` CSS rule (unlike the team picker bug
  above), but this wasn't explicitly stress-tested the same way the picker was. If it turns out
  broken too, same fix pattern applies.

---

## Working-relationship notes (carried forward + new)

- [[feedback_handoff_at_375k_context]] — this handoff, same pattern as always.
- [[feedback_show_terminal_activity]] / [[feedback_act_autonomously_on_technical_steps]] — held
  all session; every build was shown via real `dotnet build` output, every launch was a real
  `start` command, not just claimed.
- **User works in the same fast, overlapping, multi-topic style as Session 21** — messages
  arrived mid-tool-call constantly (e.g. "fire sensitivity needs a delay label" arrived while
  `WebMainForm.cs` was still being written; "team still isn't taking me to a choose team
  selection" arrived while investigating a background-copy csproj bug). Pattern held: acknowledge
  inline, finish the current unit of work, address the new thing next. **This session had far
  LESS visual-direction churn than Session 21** — once WebView2 was chosen, the user stayed on
  that path and gave a long, coherent stream of concrete UI bugs/requests rather than re-deciding
  the whole direction again. Worth noting the "lock a direction before building" idea from
  Session 21's handoff seems to have actually helped.
- **User tests by actually using the running app, consistently** — nearly every bug report this
  session (team background not linking, teams button not working, × not closing, categories not
  opening events, help leading nowhere) came from screenshots of the real running app, not code
  review. Several of these (the csproj copy bug, the CSS specificity bug) were NOT visible from
  reading the C#/CSS in isolation — they only showed up by actually building and clicking through
  the app. **Reinforces: always rebuild and relaunch after UI changes, don't just eyeball the
  diff.** This was done consistently this session (rebuilt and relaunched probably 8+ times).
- **User sends real screenshots of the actual running app, sometimes cropped to just the relevant
  region** — read these carefully for exact pixel-level detail (e.g. the team-picker screenshot
  clearly showed populated tiles + a working blurred background, which contradicted an earlier
  assumption of "the grid renders empty" from a browser-preview debugging attempt that turned
  out to be an artifact of the browser preview tool itself, not the real app). **When browser-
  preview debugging conflicts with a real app screenshot, trust the real app screenshot.**
- **User gave a logo image and expects either exact accurate results or an honest "I can't do
  this reliably" — not a best-effort guess dressed up as done.** The 12×13 collage was correctly
  flagged as unusable for auto-mapping rather than just slicing it and assigning names by
  guesswork, which would have shipped wrong team logos. This restraint (asking before guessing on
  something hard to verify/undo) matches the general safety instinct to have here.
- **Real bug-hunting pattern that worked well twice this session**: when a UI feature "doesn't
  work" and the code reads correctly, check the ACTUAL BUILD OUTPUT / RUNTIME STATE directly
  (`ls bin/.../TeamBackgrounds/`, CSS specificity via first-principles cascade reasoning) rather
  than re-reading the same source files again. Both real bugs this session were invisible from
  source-reading alone.

---

## File state (end of session 22)

| File/Item | Status |
|---|---|
| All `.cs` files | Compile cleanly, 0 warnings/0 errors (verified repeatedly, last as final action) |
| `Program.cs` | Launches `WebMainForm`, not `MainForm` — the native UI is now dead code |
| `wwwroot/` | New this session: `index.html`, `style.css`, `app.js`, `fonts/Outfit-*.ttf` |
| `WebMainForm.cs`, `WebBridge.cs` | New this session, the actual WebView2 host + bridge |
| `MainForm.cs` + native v4 UI files | Dead code, still present, not deleted, not referenced |
| `BandAudioHook.csproj` | WebView2 package added; `wwwroot\` and `TeamBackgrounds\` now copy to output (the latter was a real missing-before bug) |
| `TeamBackgrounds\` | Same 14 SEC teams as Session 21 end state — still missing Florida, Oklahoma. Now actually reaches `bin\` on build (bug fixed) |
| `TeamLogos\` | Does not exist yet — blocked on user decision (see Open Design Question section above) |
| Running app process | Was running (WebMainForm) as of end of session — not explicitly killed before this handoff was written, may still be up |

---

## Loose threads from the very end of this session (came in as the handoff was being written)
- **"Push the borders"** — unclear what this means (thicker neon pulse border? panels pushed
  closer to screen edges/less padding? something else?). Did NOT guess and implement this —
  ask the user to clarify before touching border/spacing CSS again.
- **Logo sheet, "use the ones you can identify for now"** — user wants me to slice the 12×13
  collage and use whichever tiles I can confidently identify, skipping ambiguous/duplicate ones,
  rather than rejecting the whole sheet. **Not done yet** — the image only exists as a pasted
  chat screenshot, not a file on disk, so it can't be cropped/processed. Ask the user to save it
  to a real path (e.g. `D:\Claude\Projects\team_logos_sheet.png`) next session, then do the
  identify-and-slice pass on the ones that are unambiguous.
- **"How do I push this for people to use?"** — answered conversationally, not yet executed.
  `dist\` exists but is stale (predates the WebView2 rebuild) and is NOT actually obfuscated
  despite Session 21's handoff calling it that — no obfuscation tooling is wired into the repo,
  that description appears to have been aspirational rather than real. Real path for next
  session: `dotnet publish` self-contained + single-file (so end users don't need .NET
  installed), bundle `Songs\`/`Profiles\`/`TeamBackgrounds\`/`wwwroot\` alongside the exe, zip it
  up. Consider whether "obfuscated" was ever actually wanted/needed or if that's stale language
  to just drop.

## The real next step
Pick a lane from the "still open" list above based on what the user wants to tackle first:
1. Team logos (needs user decision: Frosty export, or slice-and-manually-rename the collage, or wait)
2. Live Feed panel port into the WebView2 shell (currently a dead stub)
3. Onboarding wizard + drag-and-drop sound import (Session 21 tasks #2/#3, now unblocked by the UI settling)
4. Delete the now-dead native v4 UI files (confirm with user first — implicit but not explicit "yes, delete")
5. Confetti/toast visual feedback in the new shell (currently silent — `FireEvent` doesn't trigger either anymore)

Don't guess which one — ask the user which is highest priority before starting.
