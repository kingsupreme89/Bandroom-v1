# Bandroom Handoff — August 12, 2026 — Session 60

Same idea as always: what happened tonight, explained plain. This one's a big session — cracked the "wrong team" problem that's been eating multiple sessions.

## The Big One: "Wrong Team" Bug

You sent screenshots from a Georgia/Florida game (clearly different colors, red vs blue — no confusion like the maroon-on-maroon problem from last time), asking me to trace how the app knows who has the ball.

**What I found:** the down-and-distance box at the bottom of the screen (the "1st & 10" / "3rd & 5" pill) fills in solid with whichever team currently has the ball's color. Georgia has it → red box. Florida has it → blue box. Simple, reliable signal.

Then I went digging in the code history and found something wild: **this exact detection method already existed and was proven to work**, calibrated back on 2026-08-08 with a note calling it "far more reliable than the underline dashes." But when the scorebug preset got upgraded to "v3" on 2026-08-09, that working color-detection got silently dropped — the new version only carried over the less-reliable "underline brightness" method. Nobody noticed because the old preset was deleted at the same time, so there was no way to compare.

**Fixed:** restored the color-matching crop onto the current CBS preset and made it the primary way the app decides who has the ball, with the underline method now just a backup for when the color read isn't confident. This is the actual fix for the "wrong team celebrated," "first down went to the wrong side," and similar reports from the last few sessions.

## What Else Got Fixed Tonight

**1. Missing "3rd Down" card for your own offense.**
The app was actually playing an event called "Offense: Third Down" on 3rd & long — but there was no card anywhere to assign a song to it. It got accidentally hidden from the list back on 2026-08-09, before it was re-activated on 2026-08-11, and nobody un-hid it after. Fixed — it's a real card now.

**2. Added a new card: your own team going for it on 4th down.**
Before tonight, ONLY the defense got a cue on 4th down ("Stopped Them on 4th"). If your own team was the one driving on 4th down (going for it), nothing played for you at all — no card existed. Added "Offense: Fourth Down" as a new card, paired with the existing defense one on the same snap. This plays for whoever's driving, home or away, Big Game or not — not restricted the way some other cues are outside Big Game.

**3. Pausing the game could fire fake events.**
You caught this with a screenshot — pausing mid-game fired two songs that had nothing to do with anything. Turns out there's already a "the screen stopped moving, must be paused" detector, but it waited a full second before trusting that and shutting off song-triggering — plenty of time for one bad read off the pause menu's totally different layout to slip through. Cut that wait time in half. Doesn't fully eliminate the risk but cuts the exposure window significantly.

**4. The Event Log was lying to you about card names.**
This was the root of a lot of your confusion tonight. The Event Log (the thing that shows "Third Down (Home) — no song assigned") and the actual song-assignment screen were pulling names from two completely different places that had drifted apart. The log would say "Third Down," but the real card on the assignment screen was named "3rd & Long" — so you'd go looking for "Third Down" and never find it, even though it existed. Fixed: the log now always uses the exact same name as the card.

**5. Renamed the down/distance cards for clarity.**
No more generic "2nd Down" that doesn't tell you if it's the short or long version. Everything now reads: **2nd & Short, 2nd & Long, 3rd & Short, 3rd & Long, 4th Down, 4th Down After a Loss.** Also dropped the redundant "(Defense)" tags on some cards — the section they're filed under (Offense vs Defense) already tells you that, repeating it in the name was just noise.

**6. A leftover broken test got fixed.**
Not something you'd notice, but a testing check for touchdowns was failing because of a change from an earlier session that never got its test updated to match. Confirmed the actual game behavior was fine (touchdowns still announce correctly, just on a very short buffer to avoid crediting the wrong team) — just the test itself was stale. Fixed the test, all 62 checks pass now.

## Released Tonight: v1.1.0

Published live and public: https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.0

Existing installs will pick this up automatically on next launch (delta update). First-time installs use the Setup.exe from that page.

**Follow-up mid-session: nobody could actually see it.** After publishing, you reported people couldn't access the release. Turned out the release itself was fine (published, not a draft) — the whole GitHub repo (`kingsupreme89/Bandroom-v1`) was set to **Private**, which hides everything in it, releases included, from anyone without explicit access. That's a repo-level setting, separate from whether an individual release is a draft. Flipped the repo to Public (with your go-ahead) — the link above should now be reachable by anyone.

*Worth remembering for next time:* if a future release ever seems "invisible" again, check repo visibility first (`gh repo view <repo> --json visibility`) before assuming it's a draft/asset problem — same symptom, different cause, and this one cost real time to track down.

## What To Test Live

1. **The possession fix is the big one** — watch a full drive or two and confirm the right team gets credited for first downs, third-down stops, etc. This is the fix most likely to actually move the needle on the "wrong team" reports from the last several sessions.
2. Try going for it on 4th down with your own team and confirm the new "Offense: Fourth Down" card plays (you'll need to assign a song to it first — it's a brand new card, starts unassigned).
3. Pause the game mid-play a couple times and confirm nothing fires that shouldn't.
4. Glance at the Event Log during a game and confirm the names it shows now match what you see when you go to assign songs.

## Known Gaps / Not Touched Tonight

- **Mac version is behind.** The Mac app's evaluator list is missing several helpers the Windows app already has (including tonight's new 4th-down one, and a handful from recent sessions). Didn't touch this tonight since it's a bigger separate sync-up, but flagging it so it doesn't surprise anyone later.
- The possession color-match fix is currently only calibrated for the CBS-style scorebug preset ("Kam's CBSv3") — the default CFB27 HUD preset already has its own separate possession method (an arrow icon) that wasn't touched.
- If you ever hit a matchup where both teams share a very similar color again, watch for whether the fallback (underline method) kicks in cleanly, or whether that scenario needs its own dedicated fix later.

That's everything for tonight!
