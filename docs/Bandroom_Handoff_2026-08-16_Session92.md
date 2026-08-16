# Bandroom Handoff — August 16, 2026 — Session 92

Same idea as always: what happened, explained plain.

## Found: The Real Repo Had Silently Moved to D:\Bandroom

Mid-session, `C:\Bandroom` (where this session had been working) lost everything except `src/`
and `.venv` — no `.csproj`, no `.cs` files, no `wwwroot`, no `.git`. Turned out this wasn't data
loss: another tool active in the same VS Code window (Cline, alongside a DeepSeek terminal agent)
had consolidated everything into `D:\Bandroom`, which is the actual git repo — `C:\Bandroom` had
never been one. Every fix made against the `C:\` copy earlier in the session had to be manually
re-applied to `D:\Bandroom` before it could ship. Everything below is in the real repo now.
**Worth confirming going forward: is `C:\Bandroom` still in use for anything, or should it be
deleted to stop this happening again?**

## Fixed: Penalty Events Sometimes Crediting the Wrong Side

Owner report: "the penalty it's showing was on me and not my opponent, it's like it's reading it
backwards, who has the ball."

Two separate bugs stacked here:
1. `EventActivityLog.cs`'s friendly-name table statically mapped `"Penalty: Offense"` →
   `"Penalty - Your Team"` and `"Penalty: Defense"` → `"Penalty - Opponent"` — but those EventKeys
   describe which side of the ball committed the penalty, nothing to do with home/away. The actual
   routed side is appended separately as `(Home)`/`(Away)`, so the two could straight-up
   contradict each other in the log. Relabeled to neutral `"Penalty - Offense"`/`"Penalty -
   Defense"` so the side tag is the only thing claiming a side.
2. The real classification bug: `GameWatcher.cs` decided penalty offense/defense off the raw,
   uncorrected OCR possession sample (`_lastPossession`), a completely separate pipeline from the
   RAM-primary, fallback-corrected value (`readerPossessionAway`) every other evaluator uses for
   routing — the exact class of bug the 2026-08-15 possession-routing fix addressed elsewhere, just
   never applied here. A penalty's FLAG overlay darkens the possession ribbon, making OCR
   possession sampling unusually likely to be wrong at exactly that moment. Now reads the same
   corrected value as everything else.

## Fixed: Song Sometimes Still Fired After Quitting to the Menu

`GameWatcher`'s tick loop only checked for cancellation at the top of the `while` loop and inside
a few `Task.Delay` calls — a tick already mid-flight when Stop Watching fired could still run to
completion and fire an audible clip after the user had already backed out. Added a cancellation
check immediately before the actual fire.

## Fixed: FBS Mode / HBCU Mode Team-List Bugs (Several, All Related)

Chain of owner reports, fixed across a few passes:
- **FBS Mode showed HBCU schools mixed into the main team list.** The roster filter
  (`hbcuFilteredTeams()`) used to just return the FULL unfiltered roster whenever HBCU Mode was
  off, instead of excluding HBCU schools the way "FBS Mode" implies. Fixed to properly exclude
  them.
- That fix then broke **the Market/Sound Bank team picker**, which is supposed to reach every
  team regardless of mode (same rule as Set Matchup — an HBCU band can play, or browse the Sound
  Bank of, any opponent). Added a `skipHbcuFilter` option and used it there.
- **The Team Pot panel could stay visible after leaving HBCU Mode.** Its hidden-flag write was
  gated behind "is the Situations panel currently open," meant only to skip an unnecessary
  re-render — but that same gate also skipped the (cheap, always-safe) hidden-flag write itself.
  Made the hidden-flag write unconditional.

## Fixed: Florida A&M Songs Invisible in the Market

Root cause: 2 real FAMU songs (`FAMU - Coming To America`, `FAMU Drumline DuckMouth`) were sitting
in the marketplace mistagged under the school name `"Florida AM"` (missing the `&`) — leftover
from before a prior session's ampersand-stripping bugfix, which only protected *new* uploads, not
existing records. Also found the marketplace worker was running stale code — the `&`-preserving
fix and a new school-name search match had never actually been deployed live. Deployed the worker,
then PATCHed both songs' `school` field to `"Florida A&M"` (owner confirmed before the live-data
write). Verified live: Florida A&M's album now shows both songs, and searching "famu" finds them.

## Added: Market Pagination + Search

Owner reports: "we should be able to find typing famu" / "no way to just see all songs" / no
next/previous paging.
- The Market hub's Popular Songs list used to hard-cap at the top 20 with nothing past that
  reachable. Now paginates the full list (20/page) with Prev/Next and a page counter.
- Added a search box that hits the marketplace's existing (but previously unused in the hub) `q`
  search param — also widened server-side to match school name, not just song title/artist.
- Along the way found `fetchUploadList` (the wrapper the hub actually calls) never forwarded the
  search text to the function that builds the request — every search silently re-ran the same
  unfiltered query until this was fixed.

## Ran: Full State-Machine Audit + Fixed All 6 Findings

Owner asked for a formal state-machine analysis of the whole game-detection engine
(`GameWatcher.cs` + `Bandroom.Core`'s ~24 rule evaluators), read-only first, then "fix them all."
The audit re-verified all 15 previously-closed discrepancies from the Aug 11 `FINAL` doc are
still genuinely fixed, then surfaced 6 new ones:

1. **`Stop()`/`Start()` race** — `Stop()` never waited for the previous tick loop to actually exit
   before `Start()` reset shared state; a fast double-start could run a stale tick against a
   half-reset watcher. Added a generation counter — a stale loop's tick is now a no-op.
2. **`BigEventHelper`/`TurnoverHelper` same-tick stacking** — a late-game 4th-down stop can fire
   both "Defense: Fourth Down Stop" and "Defense: Iced Game by Turnover" together. Turned out this
   is genuinely intentional per `TurnoverHelper`'s own existing comment ("turning it over on downs"
   is explicitly one of the ways a game gets iced) — it just had no cross-reference or test. Added
   both.
3. **Non-HBCU "Other: Kickoff" always fired for both bands**, unlike the HBCU path, which already
   routes it to whichever team just scored (real football: the scoring team kicks off next). Added
   a non-HBCU-scoped `_lastTouchdownSide` tracker and mirrored the HBCU routing, falling back to
   both sides only if the scoring side genuinely isn't known yet.
4. **`CanFire`/`Evaluate` drift risk** — nothing enforced that an evaluator's cheap `CanFire`
   pre-check stays a superset of what `Evaluate` actually checks (this exact class of bug broke
   `TurnoverHelper` once already, 2026-08-11). Added a reflection-based test
   (`EvaluatorInvariantTests.cs`) that checks the invariant across every evaluator in the codebase
   automatically, not just the one that broke before.
5. **`DownDistanceBuffer` timing** — audit worried a shared-buffer refactor silently tightened
   timing for 5 evaluators based on one evaluator's complaint. On inspection: already documented
   as an explicit, owner-confirmed decision applying to all 5, and the existing timeout test would
   already fail if it ever regressed. No code change needed; left a note.
6. **Foreground-window capture could stall indefinitely** if the game window's foreground focus
   kept flickering between two different valid candidate windows (their "wait for 2 consecutive
   ticks on the same candidate" debounce never resolves in that case). Added a 5-second stall
   timeout that forces a retarget instead of waiting forever.

## Shipped

**v1.1.18** — one `ppup` release covering everything above.

## Verification

- `dotnet build BandAudioHook.csproj -c Debug` — 0 warnings, 0 errors, after every C# change.
- `dotnet test src/Bandroom.Core.Tests` — 132/132 passing, including the new
  `EvaluatorInvariantTests` (runs across every `IRuleEvaluator`) and a new test pinning down the
  intentional `BigEventHelper`/`TurnoverHelper` stacking.
- `node --check` on every touched `.js` file.
- Live-verified against the real deployed marketplace worker (curl'd `/list` before and after both
  the worker deploy and the FAMU PATCH).
- The 6 state-machine fixes are code-reviewed and unit-tested but NOT live-game-tested this
  session (owner didn't have CFB27 running) — flag for a follow-up live check, especially #1 and
  #6 (both timing/concurrency issues that are hard to unit test meaningfully).

## Open Items For Next Session

- Confirm whether `C:\Bandroom` should be deleted now that `D:\Bandroom` is confirmed to be the
  real repo, to prevent a repeat of this session's confusion.
- Live-game verification of the 6 state-machine fixes, particularly the `Stop()`/`Start()`
  generation-counter fix and the foreground-stall-timeout fix.
- Consider auditing whether any other AI tool/agent running in the same VS Code session has write
  access to files outside its intended scope, given the C:\ → D:\ consolidation happened without
  this session's involvement.
