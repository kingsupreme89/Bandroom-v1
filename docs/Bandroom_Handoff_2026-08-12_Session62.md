# Bandroom Handoff — August 12, 2026 — Session 62

Continuation of tonight's Session 61 work. Same idea as always: what happened, explained plain.

## Bug Fix: Events Went Completely Silent After Pregame Ready

You reported that after Pregame Ready fired once, nothing else fired for the rest of the game --
no touchdowns, no downs, nothing.

Root cause: a "frozen frame" detector (added a while back to stop the app from re-firing the same
cue when you pause the game) suspends ALL event detection whenever the captured screen looks
static for just 2 ticks (about half a second). It's supposed to resume the instant the picture
starts moving again -- but there was no upper bound on how long it could stay stuck. If it ever
got wedged "frozen" for any reason (a held loading screen, a stats overlay, an unlucky pixel
sample), detection just stayed off for the rest of the game with no way to recover.

Fixed: added a safety valve. If the frozen state ever persists more than 10 seconds -- far longer
than any real gameplay pause without an actual pause menu -- it forces detection back on and logs
a warning so you can see it happened. This is the most likely fix for what you saw, though it
wasn't caught live firing in a real game session tonight (the bug is inherently hard to reproduce
on demand) -- worth specifically watching for during your next game.

## New Feature: "First Down on First Down"

You pointed out a real gap: the existing "Earned First Down" cue only fires when the down number
changes (2nd/3rd/4th down converting to a new 1st down). A big gain on 1st & 10 that nets ANOTHER
fresh 1st & 10 -- the down number never changes -- was invisible. Down/distance alone can't tell
that case apart from ordinary mid-drive stillness.

Your idea: use the play clock box (the small ":30"/":13" box next to the down/distance ribbon,
which shows "--" during the live play and dead-ball overlays, then resumes counting for the next
snap) as an unambiguous play-boundary. Calibrated the crop from 5 screenshots you provided
(confirmed identical position across a live snap, a FLAG overlay, and a FIELD GOAL recap screen).

Built it as a new evaluator: records Down/YardsToGo the instant the play clock stops counting
(right before the snap), compares against Down/YardsToGo the instant it starts counting again
(the next snap). If Down was 1 both times but YardsToGo jumped back up, that's a first down earned
on 1st down. New assignable card: **"Offense: First Down on First Down"** (shows as "Big Gain -
Fresh 1st Down" in the UI). Not yet live-fire tested.

## Cleanup: Removed "Offense"/"Defense"/"Other" From Displayed Event Names

You said seeing those words in event names was confusing. Every place an event name is shown to a
user (assignment cards, the event log, toast messages) already ran through a "friendly name"
lookup that's supposed to strip that -- but the lookup tables had gaps: 5 keys were missing
entirely (including the brand-new First Down on First Down card, so it would've shown its raw
prefixed key), and the two Penalty cards were mapping to literally just the word "Offense" or
"Defense" with nothing else. Filled in the gaps in both the JS-side map (used by the UI) and the
C#-side map (used by the event log/toasts) so every real event key now has a plain-English label
with no side-prefix.

## Cleanup: Big Game Setting, One Home Instead of Two

The Big Game toggle lived in two places -- a full panel in the Adjust sidebar (enabled checkbox +
a "banner" checkbox that, on inspection, was never actually independent of the enabled checkbox
anyway -- both always mirrored the same flag) and a pill on the Matchup screen. You asked to keep
just the Matchup screen version. Removed the sidebar panel entirely; the matchup pill still saves
immediately on change, same as before.

## New Feature: Marketplace "+ Upload" Button

You said there was no way to upload to the marketplace from its own homepage -- true: the only
real upload entry point was a "+ Upload" tile buried inside a team's already-open album grid, even
though the homepage's own instructions text told you to go do that. Added a "+ Upload" button
right in The Bandroom's header that jumps straight to your active team's album and opens the
song-upload flow immediately. If no team is active yet, it prompts you to pick one first instead
of silently failing.

## Released Tonight: v1.1.2 and v1.1.3

- **v1.1.2**: https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.2 -- everything above
  except the marketplace upload button, plus everything still-unreleased from Session 61 (Team
  Profiles publishing/browsing, the marketplace-download cache/.webm fixes, Pregame Ready
  calibration, Pregame Tunnel).
- **v1.1.3**: https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.3 -- adds the
  marketplace "+ Upload" button on top of v1.1.2.

Both are live and public. Existing installs will delta-update automatically on next launch.

## What To Test Live

1. **Frozen-frame recovery** -- if events ever go silent mid-game again, check the log for
   "frame has appeared frozen for over 10s -- forcing detection back on" and let me know if you
   see it (confirms the fix is the right one) or if silence still happens without that message
   (means the real cause is something else).
2. **First Down on First Down** -- get a long gain on 1st & 10 and confirm the new card fires.
3. **Event labels** -- spot-check the assignment screen and event log for any remaining bare
   "Offense"/"Defense"/"Other" text.
4. **Big Game** -- confirm the Matchup screen pill still saves and the sidebar panel is gone.
5. **Marketplace Upload button** -- open The Bandroom, hit + Upload, confirm it jumps to your
   active team's Sound Bank and opens the song picker.

## Known Gaps / Not Touched Tonight

- The frozen-frame safety valve is a best-guess fix -- the original bug was never caught live with
  logs open, so it's not 100% confirmed as the actual root cause yet.
- First Down on First Down hasn't fired in a real live game yet.
- The play clock region's crop is calibrated from screenshots only, same "not yet live-fire
  tested" caveat as every other newly-calibrated region this week.

That's everything for tonight!
