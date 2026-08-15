# Bandroom Handoff — August 15, 2026 — Session 90

Same idea as always: what happened, explained plain.

## Fixed: HBCU Songs Stuck Showing in FBS Mode

Owner report: still seeing HBCU songs/Team Pot content while in FBS mode.

Root cause: the Team Pot/Generic Pack panel's visibility (`wwwroot/app.js:969`) was only ever
set when the Situations panel was first opened. Toggling HBCU Mode off (or on) while that panel
was already open left it showing stale content until you closed and reopened it.

Fixed: `refreshHbcuMode()` now also re-syncs the pot panel's visibility live, the same way it
already re-filters the team grid on every toggle.

## Fixed: Events Silently Skipped or Firing for the Wrong Side

Owner report (event log screenshot): several events either didn't trigger at all, or triggered
for the wrong team -- e.g. `Other: Start of 2nd Quarter (Away)` logged as "skipped: not a Big
Game -- away only plays big/earned events."

Root cause: side-agnostic `Other:*` events (quarter starts, kickoff, pregame) are supposed to
fire for BOTH teams. That both-sides logic only ran in a narrow "possession not read yet"
fallback that then returned immediately. Once real possession was known (true for almost the
entire game), `Other:*` events fell through to the main per-event loop instead, which routes by
CURRENT possession like a `Defense:*` cue -- so they'd only ever fire for one side, and once
routed to "away" they were incorrectly subjected to the Big Game away-volume-gate meant only for
defensive cues.

Fixed (`WebMainForm.cs OnEngineEventsDetected`): pulled the both-sides fire for `Other:*` events
out to run unconditionally near the top of the method, stripped from the event list before
anything else runs. Also fixed a same-tick audio-cutoff bug this surfaced: the main loop's own
"first fire interrupts, rest layer" flag now starts seeded from whether the `Other:*` loop already
fired something this tick, instead of always starting fresh -- previously a quarter-start cue and
a down-change event landing in the same tick could have the second one cut off the first's audio
immediately after it started.

Verified by 4 independent review passes (routing-fix correctness, broader sweep for other
side-routing bugs, and two verification passes on follow-up patches) -- no other live instance of
this bug class found.

## Fixed: "Share to..." Popover UI

Owner report (screenshot): the "Share this song to..." popover was cramped and overlapping/
cut off against the right-side mixer panel.

Fixed: it's now a centered modal dialog with a dim click-outside-to-close backdrop instead of a
280px anchored sliver next to whichever card opened it. Also fixed the backdrop staying stuck
visible if the Settings popover was opened while Share-to was still open, and removed the
now-unused drag-to-reposition code from the old anchored version.

## Changed: Event Card Labels

- `Offense: Earned First Down` now shows as **"1st Down"** (was "1st Down (1st & 10)") -- this
  card doesn't actually appear in the assign screen (retired in favor of the split short/long
  cards), but the label was cleaned up anyway since it's still used in fallback/legacy contexts.
- `Offense: First Down on First Down` now shows as **"First Down"** (was "Big Gain - Fresh 1st
  Down"). Event key itself is unchanged.

## Shipped

`ppup` -- committed, pushed, tagged, built, packaged with Squirrel, and published. See the
GitHub releases page for the version number.

## Verification

- `node --check wwwroot/app.js` -- clean syntax after every JS change.
- `dotnet build BandAudioHook.csproj -c Debug` -- 0 warnings, 0 errors, after every C# change.
- 4 independent auditor passes on the event-routing fix and its two follow-up patches -- all
  passed, no additional bugs found.
- NOT independently live-tested: the routing fix (Other:* both-sides fire, interruptPrevious
  seeding) is verified by code reading + build only, not clicked/played through in a running game.
  Same "log/build only" caveat prior sessions have flagged for gameplay-timing-dependent changes.
