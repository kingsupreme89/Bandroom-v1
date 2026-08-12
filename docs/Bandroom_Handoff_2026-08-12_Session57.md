# Bandroom Handoff — August 12, 2026 — Session 57

Hey! Here's what got done this time, explained simple, like I'm telling a 10-year-old what happened in the workshop today.

## What We Fixed

**1. Found out logo-sharing between users was silently broken — and fixed it.**
You can save a custom team logo, and it's supposed to also share itself so everyone else using Bandroom sees it too. Turns out that sharing part was completely broken since the day it was built — not a bug in the app, but the online piece (the "worker") that's supposed to receive and hand out logos had never actually been turned on with the code that does that job. The app was quietly trying to share every time you saved a logo, failing every time, and nobody could tell because it fails silently by design (so it never gets in your way). Turned the online piece on properly — checked it with a test call and confirmed it's really working now, not just "looks deployed."
**Heads up:** because it was broken this whole time, nobody's shared logos ever actually landed anywhere. It'll start working going forward, but old saves won't show up for others until you (or they) save/re-save that logo again.

**2. Your Montana team's glowing outline was silver instead of burgundy — fixed.**
The glowing LED-style border around a team always uses whichever of that team's two colors is the brighter one — that's on purpose, so the glow is never a dark, hard-to-see color. Montana's second color happened to be silver, which is brighter than its burgundy, so the glow was picking silver. Changed Montana's second color to a lighter shade of the same burgundy instead, so now the glow reads as burgundy like you wanted.

**3. Confirmed Montana isn't a "custom" team at all — it's fully wired in, same as any big-name school.**
You asked if your Montana team was hooked up the same way a school you build yourself (TeamBuilder) is. Turns out Montana already ships as a real built-in team (one of 50 popular smaller "FCS" schools bundled with the app) — it was never a TeamBuilder team to begin with. That means it gets full automatic detection (touchdowns, downs, possession, all of it) exactly like Michigan or Ohio State would, and it works the same whether you're using the CBS-style scorebug or the CFB27 scorebug — detection doesn't care which team it is, only which scorebug style you're using.

**4. Explained (and wrote down) what a TeamBuilder team can and can't do.**
Started a real, in-depth handbook document (separate from the quick in-app help) and wrote the first full section: how to add your own school with TeamBuilder, and the important catch — a school you type in yourself doesn't exist in the video game, so Bandroom can't automatically detect scores/plays for it. It still gets its own colors and its own song folder, you just have to set your ACTUAL matchup using a real in-game school so the detection engine has something real to watch, while your custom school stays the "look and sound" of the app.

**5. Fixed a mislabeling issue in the song-sorting tool for the Big Ten folder.**
The tool that sorts your song files into the right team folder was reading "UM" as Miami everywhere, even inside your Big Ten folder — which would've dumped Michigan songs into Miami's folder by mistake. Fixed it so "UM" only means Miami outside the Big Ten folder; inside `\B1G\`, it correctly means Michigan. Doesn't touch Miami's own files anywhere else.

**6. Got PDF-making set up for the new handbook.**
Installed the tool (pandoc) needed to turn the handbook's plain document into an actual PDF, since it wasn't on this computer yet. Put it on the D: drive since that's where there was room/permission.

## What Got Looked At But Not Changed

Nothing else this session — everything looked into got fixed or answered.

## 25 Ideas For What To Do Next

1. Re-save (or ask other users to re-save) any custom logos that were made before today, since sharing was broken the whole time and those never actually went out.
2. Keep writing the in-depth handbook — TeamBuilder section is done, but the rest of the app (Clipper, marketplace, Set Matchup, Game Settings, etc.) still needs full write-ups.
3. Actually export the handbook to a real PDF file now that the tool's installed, to make sure the whole pipeline (writing → PDF) works end to end.
4. Decide on a consistent place all future "explainer" docs like the handbook should live, so they don't get scattered.
5. Consider a quick automated check that pings the logo-sharing worker occasionally, so if it ever goes silent again (like it did unnoticed for days) something actually flags it instead of it failing invisibly.
6. Look at whether other online pieces (marketplace uploads, profile sharing, etc.) have ever had the same "code written but never actually turned on" gap.
7. Double check no other FCS built-in team has a silver/light secondary color that's quietly overpowering its real team color in the glow, the way Montana's did.
8. Consider writing a short internal note listing which built-in teams have had a manual color tweak (like Montana's now), so future color-scheme passes don't accidentally overwrite it.
9. Think about whether TeamBuilder should eventually offer a "match this to a real in-game school" shortcut, since that's the actual workflow needed to make a custom team detectable.
10. Look at whether other TeamBuilder-style abbreviation collisions exist (like "UM" for Miami vs. Michigan) across other conference folders, not just Big Ten.
11. Consider re-running the song-sorting tool now that the Michigan/Miami mixup is fixed, to catch any files that got misfiled before the fix.
12. Add a short "how do I know if my logo actually shared?" indicator in the app, so this kind of silent failure is visible to you next time, not just something we have to dig for.
13. Write the marketplace section of the handbook next, since logo-sharing lives there and just got fixed — good time to document it clearly.
14. Consider a "last successfully deployed" note/version check for each Cloudflare worker, so it's obvious at a glance if one's stale.
15. Look at whether any other FCS built-in teams could use the same lighter-tint color pass Montana just got, for teams whose current colors don't glow well.
16. Decide if the handbook should include screenshots (like the one you sent of Montana) or stay text-only for now.
17. Consider a small settings toggle for "which real school is secretly powering my custom team's detection," so it's easy to check/change without digging through Set Matchup.
18. Do a pass through all TeamBuilder teams anyone's created so far and see how many actually have a matching real-school Set Matchup set up correctly.
19. Think about whether pandoc should be added to a documented setup checklist so future computers don't hit the same "missing tool" surprise.
20. Consider writing a short FAQ entry in the handbook specifically for "why doesn't my custom team's touchdown song play automatically" — likely a common question given today's explanation.
21. Look at whether the handbook should link back to (or replace) the shorter in-app Help Guide once it's more complete.
22. Consider a periodic health-check pass across all deployed Cloudflare workers (marketplace, usercount, etc.) to catch any other stale deploys before they're noticed the hard way.
23. Revisit whether "lighter tint of the primary" should become the standard fallback (instead of picking secondary) for any team whose secondary is much lighter than expected, rather than doing it one team at a time.
24. Ask whether other users have also noticed logos "not showing up" for them — worth a heads-up post now that the real cause is known and fixed.
25. Consider adding conference-folder-aware overrides (like the UM fix) as a small documented list in the song-sorting script, in case more ambiguous abbreviations turn up later (e.g., another "OM"/"MSU"-style collision).

That's everything for this session!
