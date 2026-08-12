# Bandroom Handoff — August 12, 2026 — Session 59

Hey! Here's what got done this time, explained simple, like I'm telling a 10-year-old what happened in the workshop today.

## What We Fixed

**1. The matchup screen's mouse-scrolling problem.**
When you scrolled your mouse wheel on the matchup screen, it would get "stuck" and stop working. Turned out one piece of code was listening for scroll clicks over a much BIGGER area than it needed to (the whole side of the screen, not just the little picture-carousel part), so it was eating scroll clicks that should have gone somewhere else. Fixed it to only listen in the small area it's actually supposed to.

**2. The "can't scroll back up to the letter A" problem — tried twice, still not fully solved.**
After scrolling down through the alphabet list of teams, you couldn't scroll back up to see the teams starting with A. First guess (a CSS centering trick) didn't fix it. Second guess (thinking there was a special white line on the game screen that shows who has the ball) turned out to be based on a false alarm — that "white line" I found was actually just a score number, not a real signal, and chasing it made things WORSE for a little while (possession detection stopped working at all). Backed that all the way out. This one is still not fixed — see the "not changed" section below.

**3. Sound Booth's volume knobs weren't actually doing anything when you dragged them.**
Two separate bugs, actually. When you hit "Preview" to test a sound, the Home/Away/PA/Whistle volume knobs were completely ignored — the sound always played at Master volume no matter what, regardless of which knob you were touching. Also, even Master volume changes made WHILE a preview was playing didn't update live — you'd have to stop and restart the preview to hear the difference. Both are fixed now: whichever knob you're on, and dragging it live while something's playing, actually changes what you hear immediately.

**4. Found out why your Home team's volume was silent.**
Turned out your Home volume was saved at 0% — probably left over from testing the knobs above. Not a bug, just a leftover setting. You bumped it back up to match Away's level.

**5. Chased down why the "team runs onto the field" (pregame) song fired late, at kickoff instead of during the actual walkout.**
Found out your saved settings were pointed at a scorebug style that had never been taught to recognize the walkout moment at all, so it always had to fall back to the "fire at kickoff" backup. Copied the recognition settings over from the other scorebug style that already had them (same walkout screen, just needed the same coordinates applied to your style too), since you confirmed that screen looks the same no matter which broadcast style you're using in-game.

**6. False alarm: thought a wrong song was assigned to "Pregame."**
The event log showed your Pregame trigger playing a file literally named "Touchdown Scored." Flagged it as a mistake — but you confirmed that's actually your fight song, reused on purpose for both moments. No bug, no change needed.

**7. Chased the "wrong team credited for a play" problem for a long time — didn't crack it tonight.**
You reported two separate times where the app said "Away" did something (an earned first down, a tackle-for-loss) that was actually "Home." Spent a lot of time trying to find the real visual signal on your scorebug that shows who has the ball, using several of your real screenshots. Every theory I tried (a bright line, a matching color) turned out to be a false alarm when checked carefully pixel-by-pixel. Backed everything out to the safest known-working state rather than ship another guess. Did make the "confirm it 3 times before trusting it" safety check slightly stricter, which should help a little, but the real fix still needs more work — see below.

**8. Ran your full "523" project checkup — found and fixed 20 real bugs.**
This was the big one. Split the whole app into 4 teams (the part you see and click, the behind-the-scenes settings/save-files, the part that watches the game and decides when to play a song, and the part that actually plays sound) with 2 checkers each, then had two of them double-check everyone else's work afterward. Every single bug found was actually fixed, checked again, and given a little "trip wire" so the same kind of mistake can't sneak back in unnoticed. Some of the bigger ones:
   - A defensive touchdown (like an interception returned for a score) could briefly announce the WRONG team scored before correcting itself a second later — now it waits just long enough to get it right the first time.
   - Restarting the app mid-game (which happened a lot tonight during testing) didn't fully "forget" the previous game's info, which could cause weird mixed-up readings on the next game. Now it properly starts fresh every time.
   - If the app ever crashed while saving your settings, it could have corrupted the save file. Now it saves safely (write to a spare copy first, then swap it in) so a crash can never leave you with a broken file.
   - A team or profile name like "CON" would have actually crashed the whole app (a weird old Windows rule about reserved names) — fixed.
   - If the display/rendering part of the app ever crashed, the whole window used to freeze forever with no way to recover except force-closing. Now it tries to recover on its own.
   - The "Preview" button on the song-picker screen would silently do nothing if you clicked it twice within 20 seconds on the same song — now it always plays instantly like it's supposed to.
   - Found (and fixed) a couple of dead, unused, or invisible-when-shown pop-up windows left over from old features.

## What Got Looked At But Not Changed

**The "which team has the ball" detection is still not reliable on your scorebug style.** This was far and away the biggest time sink tonight. We looked at close to a dozen of your real screenshots, tried three different theories for what visual signal tells the app who has the ball, and every single one fell apart under close inspection — mostly because your two teams tonight (Texas A&M and South Carolina) happen to have almost the exact same shade of maroon, which makes color-based guessing especially unreliable. The good news: I did NOT ship any of the broken guesses — everything is back to the same "sometimes right, sometimes wrong" behavior it had before tonight, just with a slightly stronger "make sure 3 times before trusting it" safety check added on top. The real fix needs either (a) a totally different kind of signal than what's been tried so far, or (b) accepting this scorebug style just won't be perfectly reliable and leaning harder on the default game HUD instead (see idea #1 below).

**Whether the app's DEFAULT scorebug style (not your CBS-style overlay) would work better.** That one has an actual arrow icon that points which way the ball is, which is a much clearer signal than anything found on your style tonight. It's already available to pick from Settings today — nobody needs to build anything new for you to try it — but it's only lightly tested itself (based on just 2 old screenshots, never fully double-checked).

## 25 Ideas For What To Do Next

1. **Top priority:** seriously consider switching to (or at least testing) the app's DEFAULT scorebug style instead of the CBS-style overlay, since it has a real arrow icon showing who has the ball instead of a guessed color/brightness trick.
2. If sticking with the CBS-style overlay, the only way forward on "who has the ball" is probably a fresh round of screenshots from a DIFFERENT matchup where the two teams have clearly different colors (not two shades of maroon), to rule out the color-confusion problem found tonight.
3. Do a live-game test of the Sound Booth volume knob fix — drag Home/Away/PA/Whistle while a preview is playing and confirm you actually hear it change smoothly.
4. Double check all your other teams' volume settings (Master/Away/PA/Whistle) are still where you want them, since Home got reset tonight and it's worth a quick full check of the others too.
5. Watch a full live game start-to-finish to confirm the pregame walkout song now fires at the right moment (during the actual walkout) instead of at kickoff.
6. If the pregame walkout still fires late even after tonight's fix, that likely means the walkout recognition needs its own fresh screenshot-based recalibration, same idea as the "who has the ball" problem.
7. Do a full close-and-reopen test of the app after tonight's 20 bug fixes, then play through a normal game start to finish to make sure everything still works together smoothly.
8. Specifically test a defensive touchdown (any pick-six or fumble-return-for-a-score type play) to confirm it now announces the right team immediately, without briefly announcing the wrong one first.
9. Specifically test starting a brand new game right after finishing a previous one (without fully closing the app) to confirm the "forget the old game" fix works as expected.
10. Try naming a team or profile something on purpose like "CON" or "NUL" just to confirm the crash-prevention fix actually holds up (should now just quietly rename it instead of crashing).
11. Consider intentionally corrupting a test settings file (or ask me to simulate it) to confirm the new crash-recovery actually kicks in gracefully instead of just trusting it worked.
12. Do a normal round of testing the "Preview" button on the song-picker screen, clicking it rapidly a few times in a row, to confirm it always plays now.
13. Now that the app can recover from a display-crash instead of freezing forever, it might be worth simulating that once (or just watching for it over the next few sessions) to see the recovery actually happen live.
14. Consider writing down, in one place, exactly which scorebug styles are "trustworthy" for game-event detection today vs. which ones (like CBS-style, right now) are known to be shaky, so it's clear at a glance for future testing.
15. If you get a spare 10 minutes, grab 2-3 screenshots from a game with clearly different-colored teams (like a red team vs. a blue team) — that would be genuinely useful for cracking the possession-detection problem for real.
16. Consider adding a simple in-app note/warning on the CBS-style overlay's settings that says "who has the ball" detection is less reliable on this style, so it's not a surprise mid-game.
17. Go back through tonight's event log from your test games and see if anything else looks off besides the two possession mix-ups already reported, now that the underlying safety-check is a bit stronger.
18. Consider whether the "confirm 3 times before trusting it" change (up from 2 times) feels noticeably slower in practice during a real game, or if it's not even noticeable — worth a quick gut-check.
19. Do a check of the Dynasty Journal (the game-log feature) to make sure games are actually getting logged reliably now after tonight's timing fix.
20. Take a look at the two small leftover/orphaned pop-up pieces found tonight (a sound visualizer and a breadcrumb trail) and decide whether to actually build them for real or clean them out completely.
21. Consider a broader sweep for any other "team A and team B share the same short code" mixups, similar to the Michigan/Miami mixup found in an earlier session — this session's color-confusion bug (Texas A&M vs South Carolina both being "maroon") is the same root idea in a different form.
22. Do a check across ALL your saved team profiles for any other volume field that might be sitting at 0% by accident from earlier testing.
23. Consider a "smoke test" checklist to run after any big session like tonight's — start a game, watch a few plays, confirm sound/volume/logging/possession-routing all look normal — so a future big change is easy to sanity-check quickly.
24. If you plan on other people (your "users") using this app with different scorebug styles, it might be worth eventually collecting a small library of confirmed-good screenshots for each style, so recalibration work like tonight's doesn't have to start from scratch each time.
25. Given how much of tonight went into a problem that ultimately didn't get solved, it might be worth setting a clear time-box for possession-detection work specifically next time, so it doesn't eat the whole session again if it's not going well.

That's everything for this session!
