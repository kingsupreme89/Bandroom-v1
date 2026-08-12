# Bandroom Handoff — August 12, 2026 — Session 56

Hey! Here's what got done this time, explained simple, like I'm telling a 10-year-old what happened in the workshop today.

## What We Fixed

**1. The team pictures look shinier now.**
Every little team logo picture in the app (the ones you click to pick a team) now has rounder corners and a little shiny highlight on it, kind of like the old glass-look icons on the first iPhones. Before, they were flatter and had sharper corners.

**2. The scrolling list of teams stopped skipping teams.**
On the "Start a Game" screen, there's a skinny scrolling list of team logos next to the big picture. Before, if you scrolled kind of fast, it would sometimes suddenly jump all the way to the top or bottom of the list — like the list got yanked out of your hand. That made it FEEL like teams were disappearing or getting skipped. We found the actual mistake in the code (it was checking the wrong thing to decide when to "wrap around" from Z back to A) and fixed it. Scrolling is smoother now too — it eases into place instead of jerking.

**3. "Not all the teams were there" — turned out to be two things, both fixed.**
Part of it was the same jumpy-scrolling bug above. But we also found the list of teams wasn't in true A-to-Z order — all the big college teams were sorted right, but then ALL the small (FCS) schools were just stuck on the end as their own group instead of mixed in alphabetically. So scrolling past "Rice" would suddenly jump to a totally different letter instead of continuing through the alphabet like it should. Fixed — now everything sorts together, true A to Z.

**4. The "Game Settings" button was actually broken, not just confusing.**
You click it, and nothing happens — you were right, that WAS a real bug, not just you missing something. There's a little pop-up panel that's supposed to hide until you click that button. Because of one tiny styling mistake, that panel was ALWAYS showing (or at least, was never actually able to hide), so clicking the button to "toggle" it open or closed didn't visually do anything — it was already stuck in the same state. Fixed it so the button now properly shows/hides the panel.

**5. The search box at the top was cut off and looked broken.**
The "Search teams..." box was getting squished up against the top of the screen, half-hidden. Moved it down so it's not fighting the header bar for space anymore.

**6. Team names on the "Start a Game" screen now have a thin white outline.**
Makes the big team name (like "MICHIGAN") pop a little more against the background photo.

**7. The little team icons in that scrolling side list are bigger now.**
Not huge — just a bit wider and bigger so they're easier to see and click, like you asked.

**8. Added a smarter way to catch the "team runs onto the field" moment.**
You sent a picture of a little white arrow/chevron shape that shows up on the pregame screen (next to the bowl game logo) every time a team is about to run out for kickoff. Turns out we ALREADY had a song trigger for that exact moment (it plays whatever "take the field" song you picked for that team) — but it used to only guess that moment had happened by watching the score/quarter numbers, which meant it could only catch it AFTER the first play of the game already started. Now it can ALSO catch that white arrow shape appearing, which happens sooner (during the actual team walkout), so the song has a better chance of starting right when they run out instead of after. Both ways of detecting it share one "don't play it twice" switch, so you won't get the song playing twice by accident.

**Heads up on #8:** I only had one screenshot to go by (from that Rose Bowl game), so the exact "where on the screen to look" spot is a best guess, not something I tested against a real live game. If it doesn't seem to catch the moment reliably, it's an easy tweak — just needs a live game to fine-tune the exact spot, and the old score/quarter-based way still works as a backup either way, so nothing breaks if the new way needs adjusting.

## What Got Looked At But Not Changed
Nothing this session — everything reported got fixed or wired in.

## 25 Ideas For What To Do Next

1. Watch a real live game and see if the new "team runs on the field" arrow-detector actually catches the moment — tweak its screen-spot if it's off.
2. Do the same "find the exact screen-spot" calibration for the OTHER scorebug styles (console/remote play), since right now the new arrow-detector only works for the PC scorebug style.
3. Double check the reordered team list (true A-to-Z now) didn't accidentally change the order anywhere else in the app that maybe LIKED the old grouping.
4. Try the new bigger icon-scroll sizing on a small window/screen to make sure it doesn't feel cramped.
5. Look at whether the "Game Settings" popup panel should close automatically after you flip a switch inside it, or stay open until you close it yourself.
6. Consider adding the same shiny-icon look treatment check to any place in the app that might use a DIFFERENT (older) style of team picture instead of the shared one.
7. See if the white-outline team name treatment should also show up on other screens that show a big team name (like the Band Room viewer).
8. Maybe add a quick visual test/checklist for "does this pop-up panel actually hide when its hidden flag is set" so this exact kind of bug (Game Settings pill) doesn't sneak into other new panels later.
9. Ask if the search box should also get pushed down a little more or less once you've actually seen it live — the fix was a solid guess but not eyeball-confirmed against your real screen.
10. Consider giving the new "team takes the field" chevron-arrow detection its own small settings toggle, in case you ever want to turn it off and only use the old score/quarter-based detection.
11. Look into whether there's a way to auto-verify screen-spot calibrations (like the new chevron one) against a batch of saved screenshots instead of eyeballing one at a time.
12. Maybe do a design pass on the Mac version of the app, since a couple of these fixes (like the chevron detector) were Windows-only this time.
13. Revisit the "Big Game" toggle inside Game Settings and make sure it's easy to find/understand now that the panel actually opens.
14. Consider a small "confirm" flash or highlight when a setting inside Game Settings gets changed, so it's obvious it saved.
15. Do a pass checking every place a search box sits at the top of a screen for the same "getting cut off" problem, in case it's not just this one spot.
16. Maybe add hover text (a tooltip) explaining what each toggle inside Game Settings actually does, for a first-time user.
17. Look at whether the wider icon-scroll size should also apply anywhere else icons show up in a tight vertical strip.
18. Think about whether "Pregame Ready" (the earlier READY screen) and "Take the Field" (now catching the walkout) together cover the WHOLE pregame sequence, or if there's a third moment worth its own song trigger.
19. Consider running a real full game start-to-finish as a test, now that three separate pregame-area fixes landed together this session, to make sure they all play nicely together.
20. Maybe give the reflective/shiny icon look its own on/off setting for people who prefer the flatter old look.
21. Look at whether the smoother/eased scrolling feels right on a trackpad vs. a mouse wheel — they can feel different.
22. Consider adding small dividers or letter labels (like "A", "B", "C"...) in the scrolling team list now that it's properly alphabetical, to make jumping to a team faster.
23. See if the coverflow (big center logo) and the side scroll list should visually highlight the SAME team more clearly when they're in sync.
24. Maybe do a pass on all the "eyeballed, needs live tuning" screen-spot calibrations across the whole app and make a punch-list of which ones still need a real screenshot to confirm.
25. Consider writing down (maybe in a settings file) which ScorebugPreset each screenshot/calibration came from, so future tweaks know exactly which style they're adjusting.

That's everything for this session!
