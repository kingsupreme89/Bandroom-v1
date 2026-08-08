# Roadmap B — 75 Suggestions (not requested — ideas based on the project as it stands)

Everything below is a suggestion I'm generating from what I now know about Bandroom: a Windows app
that OCR-watches CFB 25/26, fires band-hype audio cues on 33 real trigger events, has Google
sign-in, a marketplace, profile sync, a Discord feed, an in-progress Mac port, and a researched
(not yet built) AI commentary feature. None of this is confirmed work — pick what's worth doing
and it becomes Roadmap A material. Grouped by area, numbered straight through 1-75.

## OCR & Detection Robustness (1-8)

1. Finish calibrating the `flag` region (penalty banner) — currently dead code, no coordinates set. One real screenshot of a penalty banner would unlock the whole Penalties category in the UI.
2. Finish calibrating the `banner` region (full-screen TOUCHDOWN/FIELD GOAL/SAFETY ribbon) — same situation, one screenshot away from working.
3. Add confidence thresholds to OCR reads so a garbled frame doesn't fire a wrong trigger — right now a bad OCR read has no confidence gate mentioned anywhere.
4. Move the hardcoded fractional region coordinates (`FxX/FxY/FxW/FxH`) into an external JSON config, as the integration plan itself recommends, so a new broadcast skin/UI update doesn't require a rebuild to fix.
5. Add a per-broadcast-skin calibration profile (CFB 25 vs. CFB 26 vs. future years may shift the HUD) so one hardcoded set of regions doesn't silently break on a game update.
6. Build the missing OCR regions the integration plan flagged as gaps: yard line, home/away score, game clock, away timeouts remaining — right now `PlaySnapshot` has 18 fields but several have no OCR source at all.
7. Add a "OCR self-test" mode: feed a saved reference screenshot through the pipeline and report which regions parsed correctly, so calibration drift is detectable without a live game.
8. Log OCR misses/failures (not just successes) somewhere inspectable, so "why didn't my song play" has an actual answer instead of guesswork.

## Audio Engine & Playback (9-14)

9. Crossfade or dip-under between overlapping cues (e.g., a touchdown cue and a hype cue firing close together) instead of hard-cutting one over the other.
10. Per-event volume curves, not just per-side volume — a big touchdown cue probably shouldn't play at the same level as a routine first-down cue.
11. A "test fire" button per trigger in the assignment UI, so users can preview a song against a trigger without needing to be mid-game.
12. Audio ducking against system/game audio while a cue plays (there's already an `AudioDuckingController.cs` — worth confirming it's actually wired to *all* 33 events, not a subset).
13. Support for short video/GIF overlays alongside audio cues for streamers, not just sound.
14. A queue/priority system so two triggers firing in the same OCR tick don't both try to play over each other unpredictably.

## Trigger & Event System Expansion (15-21)

15. Expand `IRuleEvaluator` coverage to the full list in the integration plan (`DownChangeEvaluator`, `TouchdownEvaluator`, `ThirdDownStopEvaluator`, etc.) — several are spec'd but not yet confirmed implemented.
16. Add a "close game" / "clutch time" trigger (score within one score, under 2 minutes) — this is a natural, high-emotion moment the current 33 events don't explicitly cover.
17. Add a "blowout" trigger (one team up by 3+ scores) for a completely different band mood (mercy/celebration vs. tension).
18. Add a "comeback" trigger — team was down big, now within one score — genuinely one of the most exciting moments in football and currently unaddressed.
19. Add a "rivalry game" auto-detect (using the two selected teams) instead of relying on a manual `BigGame` flag with no OCR source.
20. Let users create fully custom triggers off arbitrary OCR text matches, not just the 33 built-in events — turns this from "curated list" into an extensible platform.
21. Add a cooldown/rate-limit setting per trigger (not just the global 2-second region cooldown) so chatty triggers like "Offense: Second Down" don't get old fast.

## Mac App Parity & Platform Features (22-29)

22. Once the Mac build error is fixed, get a real device/screen-recording smoke test (not just a clean `dotnet build`) before calling any Mac task "done."
23. Match the Windows app's borderless full-screen overlay behavior exactly, since that's core to how this is used (overlaid on the CFB window) — worth an explicit visual side-by-side against Windows.
24. Port `ConfigProfileManager`/`ConfigStore` in a way that shares the *same* profile file format as Windows, so a user's assignments are portable between platforms, not stored twice.
25. Decide and document early whether Mac uses the same Google sign-in flow (`GoogleAuthService.cs`) as Windows, since OAuth redirect handling differs meaningfully on macOS.
26. Vision-framework OCR accuracy will likely differ from the Windows OCR engine — budget an explicit recalibration pass rather than assuming region coordinates transfer 1:1.
27. macOS Gatekeeper/notarization plan — an unsigned Mac app will get a scary warning on first launch; this needs an Apple Developer account and a notarization step before any real user tries it.
28. Global hotkey permissions on macOS require explicit Accessibility permission grants from the user — plan the first-run prompt/explainer now, it's a common source of "why doesn't this work" support tickets.
29. Decide on a Mac auto-update mechanism (Windows uses Squirrel/`appcast.xml`) — Sparkle is the natural Mac equivalent, but it's a separate integration, not a port of the existing one.

## AI Commentary (per the existing research doc) (30-35)

30. Ship the Tier 0 (free, local, phrase-bank + Windows SAPI/OneCore voices) version first as a low-risk MVP — zero cost, zero new dependencies, and the research doc already scoped it.
31. Write the phrase-bank templates for the highest-value events first (touchdown, turnover, third-down stop) rather than trying to cover all 33 events on day one.
32. Add a settings toggle to fall back from cloud commentary (if ever shipped) to the local Tier 0 path automatically on network failure — the research doc already flags this as a real risk, worth building the fallback in from the start rather than retrofitting it.
33. If moving to Tier 2 (cloud), default to Google Cloud TTS specifically — the research doc found it's the only option whose free tier is large enough to cover Bandroom's usage indefinitely, not just a trial year.
34. Keep commentary and band-cue audio on separate volume sliders/mute toggles — some users will want cues but not a talking commentator, or vice versa.
35. Revisit the Coqui XTTS v2 licensing note (CPML, non-commercial only, vendor defunct) before anyone experiments with it locally — flag it clearly in code comments so it's never accidentally shipped.

## Marketplace & Community (36-43)

36. Add marketplace search/filtering by team, not just browsing — `MarketplaceDownloadService.cs` exists, worth confirming discoverability scales past a handful of uploads.
37. Add a reporting/flagging mechanism for marketplace content (copyright, inappropriate audio) before the user base grows past a size you can moderate by hand.
38. Add marketplace ratings/reviews so popular song packs surface above one-off uploads.
39. Add a "featured/staff pick" rotation to give new users a curated starting point instead of an empty-feeling marketplace on day one.
40. Finish and actually deploy the Discord feed panel (`wrangler deploy` + bot setup) — it's built but explicitly noted as not live yet in the existing docs.
41. Add a "share my profile" link/export so a user can hand their exact song assignments to a friend without both going through the marketplace.
42. Consider per-team leaderboards (most popular song per team) surfaced from marketplace download counts, which also doubles as social proof/discovery.
43. Add attribution/credit display for marketplace uploaders on download — a small thing that meaningfully improves goodwill in a community content system.

## Profiles, Sync & Personalization (44-49)

44. Confirm Universal Profile override and per-team profiles are visually distinguishable in the UI at a glance — easy to forget which mode is active mid-game.
45. Add profile versioning/backup so a bad marketplace download or accidental overwrite doesn't destroy a hand-built assignment set with no undo.
46. Make the dashboard server (`serve_dashboard.py`) run as a persistent background/startup service instead of something that has to be manually restarted after it dies — this directly follows from the outage that happened this session.
47. Add cross-device conflict resolution for `ProfileSyncService.cs` — what happens if the same profile is edited on two machines before either syncs?
48. Let users tag/organize their song library (genre, mood, energy level) since 33 trigger slots × however many teams could get unwieldy to manage without some structure.
49. Add a "preview before assign" waveform/scrub view in the assignment UI so picking the right clip doesn't require blind trial and error.

## Monetization & Growth (50-56)

50. `UserCountService.cs` already exists — turn it into an actual usage dashboard (DAU/MAU, games watched) so growth decisions have real data instead of guesses.
51. Decide explicitly whether the marketplace stays free or gets a creator monetization path (tips, revenue share) before the community grows large enough that changing the model later causes backlash.
52. Consider a "Pro" tier gated on genuinely valuable things (AI commentary cloud tier, expanded custom triggers, cross-device sync) rather than gating core functionality that's already free today.
53. Add a changelog-to-Discord auto-post pipeline (there's already `Bandroom_Discord_Changelog_v1.0.31_to_v1.0.40.md` written by hand) — automate what's currently manual.
54. Track which of the 33 trigger events actually get used in real games (the trigger-event-list doc already flags 26 of 33 as "not yet confirmed live") — instrument this instead of relying on manual confirmation.
55. Consider a referral or "invite your league" mechanic — this app's value is highest when a whole friend group uses it together for the same games.
56. Explore a lightweight patent filing (the pitch doc already exists) for the OCR-driven event-detection method specifically — it's the most technically novel part of the product and currently just a discussion doc with a "not legal advice" disclaimer.

## Reliability, Telemetry & Support (57-63)

57. Wire `CrashLog.cs` to actually upload/report crashes somewhere reviewable, if it doesn't already — a local-only crash log is easy to lose exactly when you need it.
58. Add basic in-app diagnostics export ("copy my log") so support requests come with actual data instead of "it's not working."
59. Add automated smoke tests that run a saved OCR reference clip through the full pipeline (region parse → trigger → audio) on every build, catching a broken calibration before it ships instead of after a user reports it.
60. `VersionGuard.cs` exists — confirm it actually blocks known-incompatible saved profiles/configs from a version mismatch rather than just checking version numbers.
61. Add telemetry (opt-in, clearly disclosed) on which OCR regions fail to parse most often in the wild — this turns "regions sometimes miss" from a guess into a prioritized fix list.
62. Build a health-check equivalent to the file-based one just set up for the dev/orchestrator loop, but user-facing — an in-app "is Bandroom seeing the game correctly right now?" indicator during a live session.
63. Add a rollback path in the Squirrel updater flow (`appcast.xml`) so a bad release can be un-shipped without waiting on users to manually downgrade.

## Dev Process, Testing & CI (64-69)

64. Get `TASK_BOARD.md` timestamp-accurate in practice, not just in convention — it's fallen behind live state at least twice in one session; consider having Cline auto-timestamp on every file save rather than relying on manual discipline.
65. Add a lightweight CI build (GitHub Actions, given the repo's already on GitHub) that runs `dotnet build` on both Windows and Mac projects on every push, so a broken Mac build is caught before a session, not discovered mid-session like this one was.
66. Unit-test the `PlayDelta` calculation logic (`YardsGained`, `NewPossession`, `WasFirstDown`, etc.) directly — it's pure, deterministic logic that's easy to test and currently has no test coverage mentioned anywhere.
67. Unit-test each `IRuleEvaluator` against known `GameState` fixtures — same reasoning, this is the highest-leverage place a regression could silently break a trigger.
68. Consolidate the many dated hand-written session handoff docs (11+ so far) into a single living `PROJECT_STATUS.md` that gets edited, not appended to forever — the handoff pattern is useful but is starting to sprawl.
69. Resolve the `D:\AGY\Bandroom` vs. `C:\Bandroom` duplicate-folder situation permanently (delete or clearly archive the stale one) rather than relying on every future session rediscovering the trap via a note.

## Security & Privacy (70-73)

70. Confirm `google_client_secret.local.txt` and `admin_token.local.txt` are actually gitignored everywhere they exist (both root and `bin\Debug` copies were visible) — a leaked OAuth client secret or admin token is a real, avoidable risk.
71. Document what `obfuscar.xml` is protecting against (anti-cheat concerns for a game-adjacent tool? IP protection?) so future changes to the build don't accidentally disable obfuscation without knowing why it was added.
72. Add scope limits/rate limiting to the marketplace upload/download endpoints before opening it to a larger audience, to prevent abuse (mass upload spam, scraping).
73. Review what the Discord bot integration will have permission to do once deployed — least-privilege the bot token scope now, before it's live, rather than after.

## Documentation & Onboarding (74-75)

74. Turn `Bandroom_User_Guide.md` into an in-app first-run walkthrough (it's currently a standalone doc a user has to find and read separately) — the 8-step "setting up a game" flow is exactly first-run-tutorial material.
75. Write a single canonical architecture doc (the integration plan is close, but it's framed as a to-do list for an agent, not a reference) that a new contributor — human or AI — could read once and understand the whole OCR → engine → audio pipeline without spelunking through 15+ handoff docs.
