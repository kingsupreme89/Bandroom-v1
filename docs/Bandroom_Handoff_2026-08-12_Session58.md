# Bandroom Handoff — August 12, 2026 — Session 58

Hey! Here's what got done this time, explained simple, like I'm telling a 10-year-old what happened in the workshop today.

## What We Fixed

**1. The matchup screen's icon-scroll list stopped lagging.**
This took a few rounds to nail down. Turned out to be a "Mac Dock" style hover effect (icons puff up bigger as your mouse gets near them) that a previous session added -- it was checking the exact position of all ~190 team icons every single time the mouse moved, which is a LOT of extra work happening constantly. Ripped that effect out entirely. Also reverted the custom "fast scroll with wraparound" behavior back to plain regular scrolling, since that same previous session's fix for "scrolling skips teams" was itself causing a different kind of skip (one scroll wheel click could jump past several teams at once). Plain scrolling never skips, so that's gone now too.

**2. Found out the "true A-to-Z" team sorting was never actually turned on.**
A past session's notes claimed every team (all the big schools plus all the small ones) got mixed into one true alphabetical list. Turns out that never actually got wired up in the code -- the list was just showing teams in whatever raw order they're stored in (big schools grouped by conference, then ALL the small schools tacked on as one block at the end). That's why "Rice" looked like it was in a weird spot. Fixed for real this time, and it now updates the header/side-list/team-picker everywhere at once so they can't drift out of sync with each other again.

**3. Fixed a timing bug where the picked team's icon landed near the top of the list instead of centered.**
The code that centers your currently-picked team in the scrolling list was running a tiny bit too early, before the icons had finished getting their real size set. Now it waits for that to finish first, so centering lands where it's supposed to.

**4. Search boxes can now find a team by nickname/abbreviation, not just full name.**
Typing "OSU," "UGA," "LSU," etc. now finds the right school, same abbreviations the song-sorting tool already knows about, so the two stay in sync.

**5. Fixed the search box sitting too close to the top of the matchup screen.**
A real spacing fix from earlier this session had gotten accidentally undone along the way (see "found while investigating the lag" below) -- put it back.

**6. Found out the worldwide download counter wasn't broken, it was pointed at a private repo.**
The GitHub project the counter reads from had been switched to private at some point, so the counter's request to GitHub was silently failing and showing 0. Nothing was actually lost -- the real number is 404 real installer downloads (not counting the auto-updater's own background traffic, which would make it look inflated at 9,477+). Left the repo private for now since the owner's mid-testing; flip it public whenever the next real release goes out and the counter will pick the real number back up immediately.

**7. Found and fixed why the pregame "team runs onto the field" song wasn't firing.**
There are two ways Bandroom tries to catch that moment (a visual marker on screen, or watching the down-and-quarter counters reset) -- in a real game, apparently NEITHER one caught it. Added a third, simpler backup: if neither of those catches it, Bandroom now also fires it the moment kickoff itself is detected (which we know reliably works, confirmed from the owner's own event log). Worst case it's a little late (right at kickoff instead of during the actual walk-out), but it will now always fire instead of possibly never firing.

**8. Swapped the "alternate whistle" button for a whistle-speed toggle.**
That per-event button used to let you pick a totally different whistle sound file just for one situation. Owner didn't want that anymore -- now the same button cycles the whistle's PLAYBACK SPEED for that one event instead (1x → 1.15x → 1.3x → 1.5x → back to 1x), same global whistle sound every time, just faster or slower. Removed all the old file-picking machinery behind the scenes since it's not needed anymore.

**9. Fixed the "fade out" volume setting not surviving a restart.**
Found two separate bugs stacked on each other: (a) the quick slider/knob version of this setting was never being saved to disk at all, and (b) even the OTHER, "properly saved" version (from the full Settings panel) was write-only -- nothing ever loaded it back in when the app restarted. Both fixed. Whichever way you adjust the fade-out setting now, it sticks across a full close-and-reopen.

**10. Fixed two teams' songs being scrambled together in the default song pack.**
Michigan's songs were entirely missing (never got their own folder -- they'd all been silently dumped into Miami's folder this whole time, because "UM" was being read as "Miami" even for files that were actually Michigan's). Same exact mistake, second case: Mississippi State's songs were dumped into a wrongly-made "Michigan State" folder under the SEC listing. Both root-caused to the same kind of mistake in the song-sorting script (one abbreviation code meaning two different things depending on which conference folder it's in, but the script wasn't told to check that). Fixed the script AND manually re-sorted the already-existing files back into the correct team folders.

**11. Montana's glowing team-color border was silver instead of burgundy.**
The glow always auto-picks whichever of a team's two colors is lighter -- Montana's second color was silver, so silver always won. Changed it to a lighter shade of the actual burgundy instead.

**12. Wrote the first section of a new, much more in-depth Handbook document** (separate from the short in-app Help Guide) covering how the "add your own school" feature (TeamBuilder) really works, especially the part where a custom school doesn't get automatic score/play detection because it doesn't exist in the video game -- you have to set your real matchup using an actual in-game school, and keep your custom school as the "active" one just for looks/sound.

## What Got Looked At But Not Changed

**The pregame walkout visual-marker calibration.** The owner sent 3 real screenshots from an actual Rose Bowl game (the exact same game the original marker calibration was guessed from) specifically to help get the exact screen-position right. Didn't get to actually redo that calibration with the new screenshots this session -- the kickoff fallback (item 7 above) means pregame songs will fire no matter what now, but the VISUAL marker itself (which catches the moment earlier and more accurately, during the actual walk-out instead of at kickoff) is still just the original rough guess. Real screenshots are sitting there ready to use for a proper recalibration pass.

**A screenshot the owner sent about a "ticker" overlapping something.** Looked at it and it appeared to actually be a screenshot of the Claude Code / editor tool itself (code and terminal text visible), not the Bandroom app. Never got a confirmation either way -- worth asking the owner directly next time it comes up, since Bandroom does have its own real scrolling "ticker" bar (bottom of the matchup screen) that's a legitimate thing to check if that IS what was meant.

## 25 Ideas For What To Do Next

1. **Top priority carryover:** use the 3 real Rose Bowl screenshots the owner already sent to properly recalibrate the pregame walkout visual marker (exact pixel position), instead of relying only on the new kickoff-time fallback.
2. Follow up on whether the "ticker overlap" screenshot was actually about Bandroom's own bottom ticker bar on the matchup screen, or something in the coding tool itself.
3. Watch a real live game specifically to confirm the new kickoff-fallback pregame trigger actually fires reliably (it should, but hasn't been tested live yet).
4. Do a full pass through the rest of the FCS/small-school list checking for any OTHER two-different-teams-same-abbreviation mixups like the Michigan/Miami and Michigan State/Mississippi State ones found this session.
5. Consider a quick built-in checker that flags "this team has zero songs in its default folder" so a future misfiling bug like this gets caught immediately instead of discovered by accident.
6. Whenever the next real version ships, remember to flip the GitHub repo back to public so the live download counter starts working again.
7. Keep writing the new in-depth Handbook -- TeamBuilder section is done, everything else (Clipper, marketplace, Set Matchup, Sound Booth, etc.) still needs full write-ups.
8. Actually export the Handbook to a real PDF file (the tool for that is installed and ready, just hasn't been run yet).
9. Do a quick pass listening to the new whistle-speed presets (1.15x/1.3x/1.5x) against a real whistle sound to make sure they still sound like a whistle and not a chipmunk.
10. Consider whether the whistle-speed button needs its own explanation tooltip somewhere obvious, since it quietly replaced a feature (alternate whistle sound) some users may have been using.
11. Double-check nobody had already picked a custom "alternate whistle" sound under the old system -- those old picks are now unused; worth a friendly heads-up if anyone had one set.
12. Confirm the fade-out volume fix actually holds up: change it, fully close Bandroom, reopen, and verify it's still the value you set.
13. Look at whether the OTHER 3 playback-timing settings (pre-roll, fade duration, re-fire cooldown) also needed this same "doesn't survive a restart" fix applied somewhere else, or if this one fix covered all of them (it should have, but worth confirming live).
14. Consider adding the same kind of "backup trigger" safety net (like the new kickoff-based pregame fallback) to any other single-signal-only event that could silently never fire.
15. Do a pass on whether search-by-abbreviation should also cover the 50 FCS/smaller schools, not just the ~130 big schools it currently knows codes for.
16. Look at whether the true-A-to-Z sort fix should also touch any other list in the app that might have quietly had the same "never actually sorted" bug.
17. Consider a "last verified working" note or simple test for event triggers that are known to be fragile (visual-marker-based ones especially), so it's easy to see at a glance which ones need live-game re-checking.
18. Ask if the whistle-speed presets (1.15/1.3/1.5) are the right values, or if the owner wants different/more granular speed options.
19. Look into whether the download counter should show a friendly "temporarily unavailable" message instead of just "0" the next time something like the private-repo issue happens, so it's less alarming.
20. Consider a documented checklist of "things that don't survive a relaunch" to sweep the whole app for, now that this exact bug pattern has shown up more than once (Big Game routing history, now fade-out volume).
21. Revisit whether the matchup screen's search box spacing fix (search box position) looks right on a smaller/different-sized window, since it was a rough headroom guess.
22. Now that hover-effects have caused real performance problems twice on the matchup screen, consider a general rule/checklist for any future hover/animation effect added there (test with the full team list, not just a few).
23. Look at whether Montana's new burgundy glow reads well against ALL the app's background themes, not just the one screenshot it was checked against.
24. Consider writing the Michigan/Miami and Michigan State/Mississippi State mixup fix up as a specific "gotcha" note in the song-sorting script's own documentation, in case a similar ambiguous-abbreviation case comes up for a totally different pair of schools later.
25. Do an end-to-end live-game test now that so many matchup-screen and event-trigger fixes landed together this session, to make sure they all still play nicely together.

That's everything for this session!
