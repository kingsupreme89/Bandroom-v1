# Supreme's Stadium Sound Selector — Session 20 Handoff

## Where this picks up from
Session 19 shipped a working GUI (down detection, trigger grid, profiles, reverb,
trimmer). **This session (20) did two big things:** (1) shipped a YouTube script for a
feature-showcase video, obfuscation, and a licensing experiment; (2) executed a **complete
visual redesign** of the app from a design handoff doc, replacing the old grid-based UI with
a dark "Stadium.ai" dashboard look. Project location, unchanged:
`D:\Claude\Projects\tools\BandAudioHook\`

**Status right now: code compiles clean (0 warnings/0 errors), but the redesigned UI has NOT
been visually confirmed working after the last fix.** See "Immediate next step" below — this
is the single most important thing to do first.

---

## Immediate next step (do this first)
A flicker was reported when the new UI first ran. I applied a fix (`WS_EX_COMPOSITED` on
`MainForm` + slowed the glow-border timer from 40ms→60ms) but **the verification build after
that fix hit a file-lock error** (old process still running) and the session ended before a
clean rebuild+relaunch+user-confirmation happened. The lock has since been cleared and a
`dotnet build` after that succeeded (0/0), but **nobody has looked at the running app since
the flicker fix landed.**

Next session should immediately:
```bash
cd "D:/Claude/Projects/tools/BandAudioHook"
dotnet build
cd "bin/Debug/net10.0-windows10.0.19041.0"
"./Supreme's Stadium Sound Selector.exe"
```
then ask the user to confirm the flicker is actually gone and the redesign looks right
end-to-end (team switching, tiles, breakdown table, modals, live feed, toasts).

---

## The redesign (Session 20's main work)

### Source of truth
A design handoff was delivered as `Design handoff documentation.zip`, extracted to
`D:\Claude\Projects\Design_handoff_documentation\design_handoff_stadium_sound_selector_ui\`.
The `README.md` in there is the **complete spec** (colors, spacing, screens, interactions) —
re-read it if anything about the design's intent is unclear. The `.dc.html` in the same
folder is a React prototype that **does not render correctly as a static file** (raw
`{{ }}` template placeholders, stuck-open modals) — don't rely on opening it, work from the
README's exact hex/px values instead.

The user approved **full scope, no time pressure** ("everything, take the time it takes") —
this is not a rushed reskin, all 11 planned pieces were built.

### New files added this session
| File | Purpose |
|---|---|
| `TeamColors.cs` | ~140-team FBS color table, ported verbatim from the design's `PROFILE_LIST` |
| `CategoryMap.cs` | Maps the 39 real game events onto the design's 6 fixed categories (Downs/Scoring/Turnovers/Special Teams/Penalties/Hype) — **this mapping is an editorial judgment call I made, not from a spec**, see the file's doc comment for the full list if it needs adjusting |
| `AppFonts.cs` | Loads embedded Outfit font (Regular+Bold only — GDI+ `PrivateFontCollection` can't reliably distinguish more than 2 weights per family; Medium/SemiBold `.ttf` files are embedded but unused, kept for a possible future WPF migration) |
| `Fonts/*.ttf` | Outfit font files (SIL Open Font License), downloaded from `Outfitio/Outfit-Fonts` GitHub releases, embedded as `EmbeddedResource` in the `.csproj` |
| `RoundedPanel.cs` | Reusable rounded-rect bordered panel, optional drop shadow + pulsing team-color glow (used for main panel + Live Feed) |
| `TeamBackdrop.cs` | Team background image layer behind the header (~10% opacity + dark scrim) — see "Team background images" section below |
| `TopBar.cs` | Wordmark, Console/Track Library nav, Saved/Shortcuts links, gear icon, New Cue+, bell (Live Feed), avatar+team name (Team Picker) |
| `SessionPanel.cs` | Left column: cues-this-drive counter, assigned/reverb pills, OCR status (now **clickable — toggles watching**, see below), decorative per-quarter chart, Total Fires/Avg-per-Category/Top Category/Coverage/Detection/Fire Delay stats |
| `CategoryMixPanel.cs` | 3×2 category tile grid, click filters the Breakdown table |
| `BreakdownPanel.cs` | The event table — wraps the existing `DataGridView` (kept for reliability) with category tabs, search box, 7-per-page pagination, pencil(reassign)/play(test-fire) actions |
| `TeamPickerForm.cs` | Team select + range slider + live event search, opens via avatar or Ctrl/Cmd+K |
| `AssignTrackForm.cs` | Replaces the raw `OpenFileDialog`-only flow — library list + Browse/Trim/Clear all still present |
| `ShortcutsForm.cs` | Small reference card (Ctrl/Cmd+K, ?, Esc) |
| `LiveFeedPanel.cs` | Right-anchored slide-over, recent fires, capped at 8, category-tinted |
| `ToastManager.cs` | Top-right toast stack, 3s auto-dismiss |
| `ConfettiOverlay.cs` | 24-particle, ~1.5s burst on Scoring-category fires |

### Files rewritten
- **`Theme.cs`** — full token rebuild to the design's exact hex values (`#0b0c0e` page bg,
  `#17181c` panel, `#e7ecf1`/`#8b95a1` text, category colors). `Theme.ActiveTeam` now drives
  `Accent`/`AccentBright` everywhere. Old field names kept as aliases so nothing else broke.
- **`SettingsForm.cs`** — expanded. The new Console design has no screen space for
  Volume/Reverb-select/Always-on-top/Stop-Playback/Songs-folder/Clear-All/Compact-mode/
  Profile-reset, so **all of that moved into Settings** (opened via the top bar's gear icon).
  Nothing was deleted, just relocated — flag this to the user if they go looking for the old
  inline controls and don't find them.
- **`MainForm.cs`** — completely rewritten as the orchestrator wiring all of the above
  together. Old `DataGridView`-only layout is gone.

### Key product decision: Team = Profile (unified)
The old app had a separate "Profile" concept (manual Save/Save As/Load/Delete, team-specific
trigger configs) alongside the new design's "Team Picker" (which only changes theme colors in
the mock). **I unified these**: selecting a team in the Team Picker now also loads that
team's saved trigger assignments (via the existing `ConfigStore.SaveProfile`/`LoadProfile`
plumbing, keyed by team name), auto-saving on every change — no more manual Save/Save
As/Delete buttons. "Reset This Team's Assignments" in Settings replaces the old Delete-profile
button. **This is a real behavior change from Session 19, not just a reskin** — confirm the
user is happy with it; it was a judgment call to resolve a genuine gap between the old and
new designs, not something explicitly spec'd.

### Other adaptations made (flag these, they're judgment calls not spec)
- The old bottom "EVENT LOG" scrolling panel is **gone** — no slot for it in the new design.
  Cue fires now show via the Live Feed slide-over + toasts instead. Diagnostic/error messages
  from `GameWatcher` still surface as toasts if they start with "Error".
- **OCR Locked status chip is now clickable** and toggles watching on/off — the design didn't
  reserve a UI slot for the old "Start Watching" button, so I repurposed the read-only status
  indicator into the control. Text reads "click to start"/"click to stop".
- "New Cue +" in the top bar opens a **minimal test-fire picker** (search list, double-click
  to test-fire) — the design's spec for this button was vague about exact behavior beyond
  "fires a test cue."
- Priority stars (★☆×5) in the Breakdown table are **deterministic-per-event decoration**, not
  a real field — matches the design README's explicit allowance ("replace with a real
  priority field if the product gets one").
- Detection %/Fire Delay stats in the Session panel are **static placeholder values** (96%,
  0.32s) — the app doesn't instrument real OCR confidence or per-fire latency. Also explicitly
  allowed by the README. Total Fires/Coverage/Top Category/Assigned-count ARE real, computed
  live from `_config`.
- Removed the "Peak drive · Q3" text callout from the decorative session chart per user
  request mid-session — chart line + Q1–Q4 axis labels remain, purely decorative (no real
  per-quarter data tracked).

---

## Team background images (new feature this session)
User wants to drop in real stadium/team art. Convention implemented in `TeamBackdrop.cs`:
drop an image named **exactly** like the team (e.g. `Florida.jpg`, `Texas A&M.jpg`) into
`D:\Claude\Projects\tools\BandAudioHook\TeamBackgrounds\` — no subfolders, flat file list,
`.jpg`/`.jpeg`/`.png`/`.bmp` all work. Picked up automatically on team switch, ~10% opacity
with a dark scrim per the design spec. Missing image = no crash, just flat panel background.

**A batch just arrived this session** (`SEC-20260804T095215Z-1-001.zip` from Downloads, 14
AI-generated stadium-tunnel images) and was extracted:
- `TeamBackgrounds\Texas A&M.jpg` — **matched and renamed automatically**, filename made it
  unambiguous.
- The other 13 are sitting in `TeamBackgrounds\_unsorted\` with generic AI-generation
  filenames ("Football_stadium_tunnel_entrance..._2K_202608040347 (1).jpeg" etc.) — **these
  need the user to say which team each one is for** before they can be renamed/moved into
  place. I did not guess by looking at image content since these are generic AI renders, not
  actual team-branded photos — guessing wrong would put the wrong stadium under a team's name
  silently. Ask the user directly, or view each image and confirm one at a time.

---

## Obfuscation / licensing (earlier in Session 20, before the redesign)
- **Obfuscar** (`obfuscar.globaltool`, installed as a global dotnet tool) is wired up via
  `obfuscar.xml` in the project root. Workflow to re-obfuscate after code changes:
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
  obfuscar.console obfuscar.xml
  ```
  then copy the obfuscated DLL over the one in `dist/` (see prior session's exact copy
  commands if needed — the `dist/` folder is the shippable build, NOT `bin/Debug`).
  **`dist/` has not been regenerated since the redesign** — it still reflects pre-redesign
  code. Regenerate before shipping/showing anyone the obfuscated build.
- A license-key activation gate (`ActivationGate.cs`) was built, tested working, then
  **explicitly removed** at the user's request ("no key remove that"). Don't re-add it unless
  asked again.
- `obfuscar_private/Mapping.txt` de-obfuscates the shipped build — never share it.

## YouTube script (earlier in Session 20)
A ~9-minute video script in King Supreme's voice was written and iterated (beta/bugs
disclaimer added, fade-in mention corrected to fade-out). Final version is in the
conversation history, not saved to a file — ask the user if they still need it written out,
since a lot has happened since and priorities may have shifted to the redesign instead.

## Fade-in/fade-out (earlier in Session 20)
Net result after a mid-session correction: **fade-out is present and working, fade-in was
removed**, both in `AudioPlayer.cs` and `SettingsForm.cs`'s exposed controls (now folded into
the expanded Settings dialog, see above).

## Window sizing
`MainForm` is 1500×900 by default (`MinimumSize` 1200×650) — was widened earlier in the
session per user request ("don't like square"), then the redesign changed the layout
entirely on top of that sizing. Confirm the current size still looks right with the new UI
once flicker is confirmed fixed.

---

## Working-relationship notes (carried forward + new)
- [[feedback_handoff_at_375k_context]] — this handoff, same pattern as always.
- [[feedback_show_terminal_activity]] / [[feedback_act_autonomously_on_technical_steps]] —
  held all session; every build shown via real `dotnet build` output, obfuscation/font
  downloads/zip extraction all done without pausing for permission on the technical mechanics.
- User is in fast, overlapping-request "vibe coding" mode — messages arrive mid-tool-call
  constantly (e.g. "remove peak drive" landed while I was mid-edit on something else, the SEC
  zip landed while mid-flicker-fix). Pattern held: acknowledge inline, finish current unit of
  work, address the new thing, keep moving. Don't stop to over-confirm minor asks.
- **Genuine scope confirmation happened for the redesign** (not just assumed) — I asked
  before starting whether to scope down for the video deadline or do the full spec; user
  chose "everything, take the time it takes." That answer is why the whole redesign got built
  in one sitting instead of a cut-down version.
- Font substitution (Outfit vs. Segoe UI vs. embed) was explicitly asked rather than
  decided silently, per the design README's own instruction to confirm with the user —
  user chose full embed.
- When the user's message is garbled/typo-heavy under fast typing (e.g. "handf", "no sorrt
  remove fade in not fade out" reversing an instruction), re-read carefully rather than
  guessing — a misread here (fade-in vs fade-out) already cost a wasted build cycle this
  session before being corrected.

---

## File state (end of session 20)
| File/Item | Status |
|---|---|
| All `.cs` files | Compile cleanly, 0 warnings/0 errors as of last build this session |
| Running app process | Was killed to clear a build lock; **not relaunched/confirmed since the flicker fix** — see "Immediate next step" |
| `dist/` (obfuscated shippable build) | Stale — predates the redesign, needs regenerating before use |
| `TeamBackgrounds/Texas A&M.jpg` | Ready to use |
| `TeamBackgrounds/_unsorted/` (13 images) | Waiting on user to identify which team each is for |
| `obfuscar_private/Mapping.txt` | Unchanged, still private |
