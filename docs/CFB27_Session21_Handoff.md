# Bandroom (formerly Supreme's Stadium Sound Selector) — Session 21 Handoff

## Where this picks up from
Session 20 shipped a dark "Stadium.ai" PilePeak-dashboard-style redesign. **This session (21)
churned through a LOT of visual-direction changes** before landing on the real answer at the
end. Read the whole thing before doing anything — the direction changed several times and the
final state matters more than the middle of this doc.

**Status right now: code compiles clean (0 warnings/0 errors), but the UI is a broken mix —
some files are Session 20's PilePeak-dashboard style, others are a half-finished v4
"editor-style" native WinForms rebuild that was deliberately abandoned mid-way. See "The real
next step" below.**

---

## The real next step (do this first)
**We are pivoting the whole UI layer to WebView2 (HTML/CSS/JS hosted in a WinForms WebView2
control), NOT continuing native WinForms.** This was a deliberate, explicit decision at the end
of the session, made for one specific reason: the user wants a **glassmorphism look** — a
full-bleed team stadium background image with UI panels floating on top as blurred/frosted
"islands." **WinForms cannot do real backdrop blur** (no compositor access) — this was
confirmed twice this session (once during general discussion, once explicitly in the v4 design
handoff's own README as a stated non-goal). CSS `backdrop-filter: blur()` does this trivially.

Given that, the user chose, in order:
1. "Move to WebView2 for real blur" (over: ship a flat-panel fake-glass approximation, or a
   WPF/WinUI3 rewrite)
2. "Stop [the in-progress native rebuild] now, go straight to WebView2" (over: let the native
   rebuild finish as a today-only interim v1)

**So: the native WinForms rebuild was intentionally halted mid-way and should NOT be resumed or
finished.** The next session's job is to start the WebView2 rebuild — audio/OCR/trigger backend
logic stays exactly as-is in C#, only the UI layer moves into an HTML/CSS page hosted in a
`WebView2` control, talking to the C# backend via a JS↔C# bridge. This mirrors an architecture
discussion from earlier in the session (see "WebView2 vs WPF vs Blazor Hybrid" below) — that
recommendation is what's now actually being acted on.

**Do NOT start this without asking the user first "what should this actually look like" —** see
"Open design question" below, because the visual spec has been unstable all session and the
last concrete reference image + the v4 README are now BOTH superseded by "glassmorphism, full
background, blurred islands," which has no written spec yet, only a one-line description.

---

## Immediate housekeeping already done this session (safe, keep these)
- **App renamed to "Bandroom"** (`MainForm.cs`: `Text = "Bandroom"`).
- **Window resized to 1920×1080** (`MainForm.cs`, was 1500×900).
- **Watching-toggle discoverability fix** (`SessionPanel.cs`'s `_lblOcrStatus`): was a tiny
  9pt muted-gray text label that didn't read as clickable — now a real pill-button with
  background color that changes by state (off/waiting/watching). This was a genuine bug (user
  couldn't find how to turn watching on) — **whatever UI comes next (WebView2) must keep an
  equally-obvious watching toggle**, don't regress this.
- **Team backgrounds**: `TeamBackgrounds\` now has 14 SEC teams (Alabama, Arkansas, Auburn,
  Georgia, Kentucky, LSU, Mississippi State, Missouri, Ole Miss, South Carolina, Tennessee,
  Texas, Texas A&M, Vanderbilt) — still missing **Florida** and **Oklahoma** to complete the
  16-team SEC. All images were **compressed from ~3MB each to ~60KB each** (960px wide, JPEG
  quality 55) since they render at ~10% opacity behind a dark scrim anyway — full 148-team
  coverage at that size is ~9MB total instead of 450MB+. Full-res originals preserved at
  `TeamBackgrounds_original_fullres\` untouched, don't delete.
- **Generic fallback backdrop pool**: `TeamBackdrop.cs` now falls back to a deterministic pick
  from `TeamBackgrounds\_generic\` (currently empty — the 2 leftover unidentified stadium
  renders got claimed as real teams before the pool was finalized) for any team without a
  dedicated image. This logic is worth keeping conceptually even in a WebView2 rebuild.
- **Sound library reality check**: confirmed by inspecting the actual build output that **no
  base song pack currently ships** — `Songs\` is empty in both `bin/Debug` and `dist/`. If the
  user has a base pack they want bundled, it needs to be added and wired into the publish step
  — not done yet, was flagged back to the user, no answer received before the session moved on.
  **Ask about this again — it affects what "release" means.**

## New feature requests this session — NOT YET BUILT, still open
Two tracked as tasks (#2, #3 in the task list), intentionally not started because they'd touch
files the (now-abandoned) native rebuild agent was mid-editing:
1. **First-run onboarding + favorite-team setup**: a simple first-launch wizard explaining the
   app in plain terms, then asks the user's favorite team (like the actual football game does)
   to set up their initial profile/theme. Gate on a "first run" flag so it only shows once.
2. **Drag-and-drop sound bank import with name normalization**: user wants to drag audio files
   directly onto a "sound bank" area (not just Browse-dialog), AND wants every imported song's
   display name normalized to a consistent format (user said "same font or something like all
   caps... for uniformity" — read as: normalize the display name, e.g. ALL CAPS, not literally
   render a different font per-song). Currently `AssignTrackForm.BrowseForFile` doesn't even
   copy files into `Songs\` — it just references the original path wherever it lives. Real
   drag-and-drop import means actually copying into `Songs\` with a normalized name for the
   first time — this is new behavior, not a tweak.

Both of these are UI-adjacent and should be redesigned as part of the WebView2 rebuild rather
than bolted onto the dying native UI.

## Simplification requests from earlier this session — reconcile these into the WebView2 spec
Before the glassmorphism pivot, the user gave several direct simplification instructions that
should carry forward into whatever gets built next:
- **No stats dashboard, no category-mix tiles.** Explicitly: "we dont need a cues by drive or
  category mix." Don't resurrect Session 20's stat-heavy panels.
- **Core mechanic wanted**: team select + browse all 33 situations + per-situation **Load /
  Edit clip / Preview / Stop buttons grouped together**, "like the first app we made" (i.e.
  Session 19's simpler grid-based UI, not either dashboard redesign). This directly conflicts
  with the v4 handoff's modal-based "Quick Assign" flow (click category → modal → pick event →
  Assign Track modal) — user was asked A (flat buttons) vs B (spec's modal flow) and initially
  said "build it 1:1, we'll configure after" (i.e. build the modal-flow v4 spec first) — **but
  that v4 spec is now abandoned for the glassmorphism pivot, so this flat-button-row preference
  is back in play and should probably win in the new spec.**
- **"Mostly dropdowns"** — user said UI elements should favor dropdown selects over heavier
  custom panels/modals where reasonable.
- **33 events, not 39.** The exact confirmed list (already correct in `CategoryMap.cs`, do not
  change it) — Offense: Earned First Down, Earned First Down (Big Gain), Earned First Down
  (Midfield), Touchdown Scored, Second Down, Second Down (Midfield), Third Down, Field Goal
  Made, Drive Starter, 2-Point Conversion Made, PAT Made, Iced Game by First Down, Victory in
  Hand. Defense: Touchdown Scored, Third Down, Third Down (Loss), Fourth Down, Fourth Down
  (Loss), Second Down, Second Down (Midfield), Second Down (Loss), Field Goal Missed by
  Opponent, Drive Starter, Turnover Forced, Iced Game by Turnover, Safety. Other: Opening
  Kickoff, Second-Half Kickoff, Opening Kickoff on Kick, Kickoff on Kick (Kicking), Kickoff on
  Kick (Receiving), Pregame Take the Field, Start of 2nd Quarter, Start of 4th Quarter. Any
  design reference (old or new) that says "39" is stale — 33 is correct and confirmed twice by
  the user directly.

## Detection-logic groundwork (real, useful, not yet fully implemented)
Worked through with the user how to actually auto-detect events beyond the two currently-wired
OCR regions (`down`, uncalibrated `flag`) in `GameWatcher.cs`. Confirmed facts from the user:
- **Kickoffs**: the game shows a literal **"KICKOFF" banner** for all kickoff situations. Same
  banner region as the planned `flag` detection (OCR-match different words in one region).
  Still need: whether quarter/clock is also OCR-able (to split Opening vs Second-Half vs
  post-score kickoff) and how to tell Kicking vs Receiving — user said "we go from there" after
  agreeing to send an opening-kickoff screenshot, but got sidetracked into UI-direction
  discussion and never sent it. **Still need that screenshot next session.**
- **Turnovers**: confirmed "INTERCEPTED"/"FUMBLE"/"TURNOVER" also appear as banner text (same
  spot, probably), which is much simpler than the down-reset+possession-flip heuristic
  originally proposed — the down-resets-on-penalties-too ambiguity is now moot since we can key
  off the banner text directly instead.
- **Still open**: is the banner region literally the same crop box for FLAG/KICKOFF/
  INTERCEPTED/FUMBLE/TURNOVER (and maybe TOUCHDOWN/FIELD GOAL)? If so, one calibrated region +
  a word list covers most of the 33-event list. Never got a confirmed answer — ask again with
  the promised screenshot.

## WebView2 vs WPF vs Blazor Hybrid — the recommendation already made
Discussed three options for getting a real CSS-driven look (this is the discussion that led to
the end-of-session pivot):
1. **WebView2 embedded in the existing WinForms shell (recommended, and what's now decided)** —
   backend (OCR/audio/triggers/profiles) stays untouched in C#, only the visual layer becomes
   HTML/CSS/JS in a `WebView2` control, talking to C# via a JS↔C# bridge.
2. WPF rewrite — better than WinForms for shadows/animation, but still no real backdrop blur
   without extra work, and hand-coded XAML instead of CSS. Not chosen.
3. Blazor Hybrid (.NET MAUI) — same rendering benefit as #1 but restructures the whole app
   around Blazor instead of just embedding a panel. Bigger lift, not chosen.

## Design references produced/received this session (all superseded except the last one)
1. Two Claude-built HTML mockup artifacts (PilePeak-light-dashboard style, then a dark
   "neon team-color glow" style, then a simplified flat situation-list style) — all superseded,
   don't reference them further, they were exploratory.
2. A real design-tool-generated handoff zip (`Design handoff documentation.zip` →
   `D:\Claude\Projects\Design_handoff_documentation_v2\`) — README describes a v4
   "editor-style" layout (mac chrome bar, icon rails, team grid + categories left panel, center
   canvas hero + transport + timeline, right Adjust panel with sliders/reverb tiles). **This
   was being built natively in WinForms via a background agent, then deliberately abandoned
   when the glassmorphism/WebView2 decision was made.** The README is still useful as a
   reference for layout ideas (measurements, region breakdown) but its explicit "no true
   backdrop blur, flat panel is the ceiling" non-goal is exactly the constraint the user just
   decided to blow past by moving to WebView2 — so treat this spec as a rough idea source, not
   the source of truth anymore.

## Open design question — ASK THE USER FIRST before building anything
There is currently **no written spec for the actual glassmorphism direction** — only one
sentence: "the ui will sit on top of these team backgrounds like islands but blurred over the
menus." Before writing any HTML/CSS:
- Confirm whether the v4 handoff's layout regions (chrome bar, rails, team grid, canvas hero,
  Adjust panel) are still wanted as the *layout*, just re-skinned with real blur/transparency —
  or whether the layout itself should change too (e.g. back to the flat team-select +
  33-situation-list-with-Load/Edit/Preview/Stop-buttons the user asked for earlier).
- Get the promised opening-kickoff screenshot to finish the detection-logic design in parallel
  (unrelated to UI, don't block on it, but don't forget it either).
- Confirm the missing Florida/Oklahoma team backgrounds and the base-song-pack question above.

## File state (end of session 21)
| File/Item | Status |
|---|---|
| All `.cs` files | Compile cleanly, 0 warnings/0 errors (verified as the last action this session) |
| UI code state | **Inconsistent** — mix of Session 20 PilePeak-dashboard files and Session 21's abandoned v4 native rebuild (`IconRail.cs`, `TeamGridPanel.cs`, `AdjustPanel.cs` exist now, partially wired). Don't try to "finish" this — it's being replaced by WebView2. |
| `TeamBackgrounds\` | 14 SEC teams placed, compressed. `_generic\` fallback pool exists but currently empty (both spare images got claimed as real teams). `TeamBackgrounds_original_fullres\` has untouched full-res copies. |
| `dist\` (obfuscated shippable build) | Stale — predates this entire session, needs full regeneration whenever something is actually ready to ship |
| Running app process | Was killed to clear build locks multiple times this session; not relaunched as of end-of-session |
| Tasks #2, #3 in task list | Still pending (onboarding wizard, drag-and-drop sound bank) — intentionally not started, fold into WebView2 rebuild scope |

---

## Working-relationship notes (carried forward + new)
- [[feedback_handoff_at_375k_context]] — this handoff, same pattern as always.
- [[feedback_show_terminal_activity]] / [[feedback_act_autonomously_on_technical_steps]] — held
  all session; every build/file-op shown via real command output.
- **User is in extremely fast, overlapping, multi-topic "vibe coding" mode this session** —
  messages arrived mid-tool-call constantly, sometimes switching topics entirely (UI direction →
  detection logic → UI direction again → sound storage question → team backgrounds → back to
  UI). Pattern held: acknowledge inline, finish current unit of work, address the new thing.
  **This session in particular had a LOT of visual-direction churn** (PilePeak light dashboard →
  dark neon V_DTOR-style → simplified flat list → v4 editor-style spec → glassmorphism/WebView2)
  — each pivot was treated as a real redirect, not ignored, but the sheer number of them means
  a lot of work was built and discarded. **Worth naming this pattern back to the user next
  session** if it continues: consider locking a direction with a quick sketch/mockup check
  *before* building real code each time, to reduce throwaway work.
- **Real bug found and fixed by paying attention to actual usage, not just code review**: the
  watching-toggle discoverability issue was only found because the user said "theres also no
  way to turn watching on that i saw" after actually looking at the running app — a reminder
  that this user tests by using the app, not just reading descriptions of it.
- **Caught a risky command before damage occurred**: a PowerShell batch image-resize script
  produced "Remove-Item on system path '/' is blocked" — the safety guard caught it, no data
  was lost (verified all 14 files intact afterward), but the response was to STOP the risky
  delete-then-rename pattern and redo it with a safer save-to-new-location-then-swap approach
  instead of just retrying. Worth remembering: when a destructive-operation error message
  mentions a suspiciously generic path (root `/`), don't retry blindly — investigate and switch
  to a non-destructive method.
- User actively drops files into watched folders *while I'm mid-task* (team background images
  arrived in `_unsorted`/`_generic` several times mid-conversation, including after I'd already
  renamed/moved the folder) — re-check folder contents rather than assuming they're static.
