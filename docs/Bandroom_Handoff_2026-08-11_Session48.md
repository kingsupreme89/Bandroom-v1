# Bandroom Handoff — Session 48 (2026-08-11)

Picks up right after Session 47. Three threads this session: (1) a new Clipper cross-team share
feature plus several event-card additions, (2) a real UI bug fix (popover overlap) redesigned into
a slide-out panel per owner request, (3) continuing the trigger-event audit checklist from Session
47 — **still not finished, see below.**

## 1. Clipper song list: cross-team "Share to..." (new feature)

Owner reported no way to send a Clipper song straight onto another TEAM's event — the existing
"Share to..." icon on situation cards (Session 47) only reaches events on the SAME team.

- New backend: `WebMainForm.AssignLibraryFileToTeamEventFromWeb(teamName, trigger, path)` and
  `GetEventsForTeamFromWeb(teamName, category)` — both load/save the TARGET team's profile
  directly via `ConfigStore.LoadProfile`/`SaveProfile` (or the live `_config` if the target happens
  to be the active team), so they can reach any team's roster without switching the active team.
  Exposed via `WebBridge.AssignLibraryFileToTeamEvent`/`GetEventsForTeam`.
- New ↗ icon on every Clipper song row (`buildClipperAssignRow` in app.js) opens a team-select
  dropdown + that team's event list (`openClipperSharePopover`), picking an event assigns the row's
  file there directly.

## 2. Event card additions (transport strip icons)

- **✂ Open Clipper** — jumps straight into the Clipper for that event, same destination as
  "Assign / Edit" just as a compact icon (owner: quicker access on narrower cards).
- **Per-event alternate whistle** (🎏 icon) — `TriggerEntry.AltWhistlePath`, new
  `BrowseAndSetEventAltWhistleFromWeb`/`ClearEventAltWhistleFromWeb` bridge methods. Lets one card
  override the global lead-in whistle clip instead of just toggling it on/off. `AudioPlayer.Play`
  takes a new `whistleOverridePath` param; `BuildLeadInProvider` falls back to the global
  `LeadInClipPath` when unset.
- **Speed toggle** (button reads "1.5x") — `TriggerEntry.PlaybackSpeed2x` (bool; field/param names
  kept as "speed2x" to avoid a rename churn, but the actual multiplier is 1.5, not 2 — owner
  correction mid-session, "2x was too fast"). New `SpeedSampleProvider` in `AudioPlayer.cs` relabels
  the final mixed output's sample rate by ×1.5 (PCM untouched — cheap trick, so pitch shifts up
  too, noted in the code comment as an accepted tradeoff, not a real time-stretch engine). Applies
  to both real in-game firing and Preview, applied after the whistle is sequenced in so both speed
  up together.

## 3. UI bug fix: card popover overlap → slide-out redesign

Owner screenshot showed the "Share to..."/"Copy From..." popover on an event card rendering
clipped and overlapping a NEIGHBORING card, garbled text spilling across two tiles.

- **Root cause**: these popovers were `position: absolute` nested inside the card, which uses a
  glass/`backdrop-filter` background — that creates its own CSS stacking context, which TRAPS any
  `z-index` inside it. A popover popping upward out of one card rendered clipped/overlapping the
  card above instead of floating cleanly on top of everything.
- **Fix**: new `openCardPopover()`/`closeCardPopover()` helpers (app.js) reparent the popover to
  `document.body` and position it with JS-computed `left`/`top` in `position: fixed`, escaping the
  stacking context entirely. `openSituations()` now sweeps any body-reparented popovers on every
  refresh so they don't orphan there across repeated panel rebuilds.
- **Redesign per owner request**: restyled as a slide-out panel from the right edge of the card
  (`.slide-open` opacity/transform transition in style.css) instead of popping up above the button,
  and made draggable by its title bar (`makePopoverDraggable`) so the owner can reposition it
  anywhere on screen.
- Applied to both `wireSituationCopyFromPopover` and `wireSituationShareToPopover`.

## 4. Locked-in mode layout + fullscreen Band Room on team switch

Owner: once a matchup is locked in, only the AM (assignments) modal, the nav-rack sidebar (The
Bandroom/Sound Bank/My Downloads/Auto-Assign/Help), and the Sound Booth should be visible — the
TEAM logo grid panel is clutter once you're only working the two matchup teams.

- New CSS: `#app.locked-in-mode #left-panel { display: none; }` — hides the TEAM grid + Profiles
  column, nav-rack (`#nav-rack`) explicitly untouched (owner wanted that kept).
- `updateMatchupLabel()` now toggles `.locked-in-mode` on `#app` based on `state.matchupLocked`.
  **Caught in this session's own self-audit**: the CSS rule was added earlier in the session but
  the class-toggle JS never got wired before other requests interrupted that thread — fixed before
  handoff, don't assume "I added the CSS" means it's live without checking the JS side too.
- The ◀/▶ Away/Home arrows (`#btn-side-away`/`#btn-side-home`) now also call `openBandroomViewer()`
  after `selectTeam()` — reuses the EXISTING fullscreen Band Room photo viewer (the same one the
  "Enter Band Room" pill already opens) rather than building a second gallery, so switching sides
  shows that team's band room picture fullscreen as its own modal.

## 5. Trigger audit — STILL IN PROGRESS, do not consider finished

Continued `docs/TRIGGER_AUDIT_CHECKLIST_2026-08-11.md` with the owner, live corrections one at a
time. Picked up from "Defense: Fourth Down" (where Session 47 left off) through "Defense: No Punt
Return," then got a naming complaint on the Timeout ladder that's **not yet resolved**.

### Confirmed/corrected/retired this session (build clean, 50/50 tests passing after each):

- **Defense: Second Down** — confirmed, no code change. Already home-always / away-only-during-
  Big-Game via `OffenseDownHelper`'s `IsEarnedBigEvent = false` + `ResolveEventRouting`.
- **Defense: Fourth Down (Loss)** — retired (same pattern as Third Down (Loss) in Session 47).
  Redundant with generic "Tackle for Loss" + plain "Defense: Fourth Down" stop cue, both already
  fire on the same snap. `BigEventHelper`'s buffered down==4 Loss branch (and its now-unused
  `DownDistanceBuffer`) removed entirely. Test replaced with a retirement-style assertion.
- **Defense: Field Goal Missed by Opponent** — confirmed, no code change. Walked the owner through
  `FieldGoalMissedHelper.cs`'s by-elimination detection (FIELD GOAL banner + possession flip + no
  score change = must be a miss, since the banner text can't tell makes from misses on its own).
- **Defense: Iced Game by Turnover** — corrected. Old logic fired on ANY real turnover late in Q4
  regardless of who was ahead; owner wanted it to mean the TRAILING team's possession flipping to
  the LEADING team, whether via a real turnover or just a punt/turnover-on-downs. `TurnoverHelper.cs`
  rewritten: fires when `NewPossession` + Q4/<2:00 + the new possessor is ahead on the scoreboard.
- **Defense: Touchdown Scored** — corrected. Owner: the TOUCHDOWN banner doesn't stay up long
  enough for OCR to reliably catch a defensive score (goes straight to kickoff). `TouchdownHelper.cs`
  rewritten to detect defense TDs purely from a +6 score delta for the side that didn't have the
  ball, independent of the banner flag — same technique `SafetyHelper`/`FieldGoalPATHelper` already
  use. Dedupe fields (`_lastDefenseTdHomeScore`/`_lastDefenseTdAwayScore`) stop a late banner from
  double-firing the offense cue for the same points.
- **Defense: No Punt Return** — retired outright, no replacement requested. `NoPuntReturnHelper.cs`
  deleted; unregistered from all three evaluator lists (`GameWatcher.cs`, `GameWatcher.Mac.cs`,
  `MainWindow.axaml.cs`); key moved to `RetiredEventKeys`; its two tests replaced with a one-line
  retirement comment (same pattern as NoPuntReturnHelper's removal elsewhere this session).

### Raised but NOT YET implemented — pick this up first next session

- **Defense: Timeout (4/3/2/1/0 Remaining)** — owner: "we need a cleaner way for these. they're not
  defense or off, just a timeout." The `Defense:` prefix is misleading (these aren't routed via the
  offense/defense side-flip the way real Defense:* cues are) and the 5-separate-keys shape is
  clunky. **No design has been agreed yet** — needs a conversation with the owner about what the
  cleaner shape actually looks like (single "Timeout" event with a count parameter? Drop the
  `Defense:` prefix but keep 5 keys? Something else?) before touching `TimeoutHelper.cs`,
  `ConfigStore.AllEngineEventKeys`, or the friendly-name map. Don't guess at this one blind.
- Checklist still open from there: Turnover Forced, Second Down (Loss), Safety, Tackle for Loss,
  the Timeout ladder (blocked above), Penalty ×2, and the whole Other/Situations group.

## 6. Dev share build

Built and sent the owner a framework-dependent zip (`--self-contained false`, matches
`release.ps1`'s own publish flags) — 21.7MB, no bundled .NET runtime, requires .NET 10 already
installed on the target machine. One-off dev share, NOT a real release (no git tag, no Squirrel
pack, nothing pushed to GitHub) — `release.ps1` still needs to run separately when this is ready to
actually ship.

## Build/test status

- `dotnet build BandAudioHook.csproj -c Debug` — clean (0 warnings/errors) after every round.
- `dotnet test src/Bandroom.Core.Tests` — 50/50 passing after every round (2 obsolete Fourth-Down-
  Loss tests replaced with 1 retirement assertion; 2 NoPuntReturn tests removed; 2 new Iced-Game
  tests + 2 new Touchdown score-delta tests added).
- App relaunched after the AudioPlayer/whistle/speed changes (PID 4412 at that point) and again
  after the popover/locked-in-mode changes (PID 24908) — confirmed running, deployed `app.js`
  content-checked against source (not just build-log-trusted) per Session 47's stale-build lesson.
- **Not yet live-tested against a real running game**: the Iced-by-Turnover/Touchdown score-delta
  corrections, the locked-in-mode layout, and the fullscreen-band-room arrow transition are all
  unit-tested/build-clean but haven't been watched fire during an actual CFB27 session yet.

## Real next steps

1. **Agree on the Defense: Timeout redesign with the owner FIRST**, then implement — do not guess
   at the shape.
2. Resume the trigger audit checklist from **Defense: Second Down (Loss)** (or Timeout once
   resolved) onward — still mid-conversation, not a "come back later" item.
3. Live-test this session's corrections (Iced Game by Turnover, Touchdown Scored, locked-in-mode
   layout, fullscreen band-room arrows) against a real game before trusting them fully.
4. Once the trigger audit is fully confirmed, ship everything via `release.ps1` (`ppup`) — nothing
   from Session 46, 47, or this session has been released yet.
