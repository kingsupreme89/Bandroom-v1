# Bandroom Handoff — Session 44 (2026-08-11)

Separate thread from Session 42/43 (Band Director overlay follow-ups / Mac port) — this session
started from a live-game bug report ("triggers don't work now") and spent the whole session on the
event-firing engine (`src/Bandroom.Core/Helpers/*.cs`, `GameWatcher.cs`, `ConfigStore.cs`'s event
registry). Full detail lives in `docs/Bandroom_Punchlist_2026-08-11_Session41.md` (misnamed
"Session41" — created before session numbers were confirmed against the other threads; treat it as
this session's real log, not Session 41's). This doc is the short version.

## What happened, in order

1. **"3089"** (rebuild + relaunch, WebView2 cache cleared) run twice this session — standard
   workflow, no code changes.

2. **Owner brain-dump of live-game bugs**, captured verbatim into the punch-list doc: down/distance
   misfires (2nd-long never fired, 4th-long fired twice, 2nd/3rd-short falsely triggered Safety),
   plus a pile of UI/UX asks (Band Room navigation, arrow glow, Clipper save destination, whistle
   playing in preview, event-card copy-assignment, start-of-sound delay) that are **still
   untouched** — this session only worked the firing-logic half.

3. **Root-caused and fixed the down/distance + Safety bugs**: `OffenseDownHelper` was classifying
   short-vs-long off `YardsToGo` on the same tick `Down` changed, but those are independent OCR
   reads that don't always land the same tick (its neighbor `DefenseHelper` already had a buffered
   fix for this exact race; `OffenseDownHelper` never got it). Gave it the same buffer. Separately,
   `SafetyHelper` (and every other score-delta evaluator) had zero debounce on the score OCR read —
   a single misread frame could phantom a +2 delta and fire Safety; added a confirm-twice commit.

4. **Owner asked for a full "dig 50 levels deep" audit** of the whole event/trigger system before
   trusting it further ("this is the last day we're doing this"). Ran 4 parallel research agents,
   each auditing a slice (down/possession cluster, scoring/turnover/kicking cluster, meta-timing +
   full EventKey-registry cross-check, GameWatcher OCR layer + WebMainForm routing), each
   instructed to re-derive everything from current source rather than trust
   `docs/STATE_MACHINE_ANALYSIS_FINAL_2026-08-11.md` — that doc, despite being dated today and
   claiming "all discrepancies fixed," asserted `OffenseDownHelper` already had the buffer fix from
   step 3, which was proven false (it didn't exist until this session). **That doc's claims should
   not be trusted at face value by a future session either**, even though most of them turned out to
   independently check out.

5. **Bugs the audit found and this session fixed** (all build-clean, relaunched, not yet
   live-verified):
   - `DefenseThirdDownShortHelper` had the identical stale-tick bug as step 3's fix, and is
     explicitly designed to fire in lockstep with `OffenseDownHelper` on the same tick — buffered
     it the same way so the pairing stays intact.
   - The score-debounce fix from step 3 could silently drop a real fast second score (e.g.
     touchdown immediately followed by a 2-point conversion) by discarding the unconfirmed
     intermediate reading. Fixed: the outgoing pending value now commits once before starting a new
     confirmation cycle.
   - `_lastKnownQuarter` had the same zero-debounce issue Safety had, just not yet reported live —
     given the same two-tick confirm pattern (renamed the shared method `CommitValueIfConfirmed`).
   - The touchdown-celebration possession-color guard (`SamplePossessionFromWindow`'s caller) only
     checked `flag`/`situation` region activity, not `banner` — added.
   - Stale `ConfigStore.cs` comment claiming `OffenseDownHelper` emits `"Offense: Fourth Down"`
     corrected (it never has; harmless doc drift).

6. **Two findings needed an owner call, both resolved this session**:
   - `TflHelper` was dead code (unreachable condition) with an assignable-but-silent UI card.
     Owner chose "fix it to actually fire" — rewritten to fire a generic "Defense: Tackle for Loss"
     cue on any 2nd/3rd/4th-down loss, intentionally layered alongside (not replacing) the more
     specific down-by-down Loss cues, with the same OCR-race buffering as its siblings.
   - `"Offense/Defense: Drive Starter"` fired every game (any fresh drive not from a kickoff or
     turnover — in practice almost always the first snap after a punt) but had no assignable UI
     card and no legacy-song fallback. Owner confirmed no duplicate "1st down after kickoff" card
     exists, so restored both keys to `ConfigStore.AllEngineEventKeys`.

7. **Owner started sending real gameplay screenshots** (College Football 25/26, CBS broadcast
   skin) mid-session to cross-check the OCR crop assumptions against actual scorebug frames. Three
   things spotted before the session ended, **none yet investigated against the actual code**:
   - A "3rd & inches" frame — distance rendered as the word "inches," not a number. `DistancePattern`
     (`GameWatcher.cs:140`, `@"&\s*(-?\d+)"`) only matches digits — worth checking whether this
     silently fails to parse (YardsToGo stays stale/0) on any short-yardage situation using text
     instead of digits.
   - A touchdown-celebration frame (player in end zone, ref signaling) still showed "1st & 10" in
     the down/distance ribbon, not "TOUCHDOWN" — consistent with the audit's suspicion that
     TOUCHDOWN may only ever render in a separate full-screen `banner`, never in `situation`.
   - A kickoff frame showed "KICKOFF" in what looks like the exact same ribbon position as
     "1st & 10"/"3rd & inches" — raises the question of whether "down" and "situation" are actually
     reading the same crop box in this broadcast skin, not two independent ones as the code assumes.

## Step 7 follow-up, same session: real bug found from the screenshots

Confirmed the shared-crop-box question is **not** a bug — `down`/`flag`/`situation`/`quarter` are
all deliberately the same crop box (`ScorebugPreset`/`GameWatcher.cs` region defs, `FxY=0.83,
FxH=0.14`), disambiguated only by regex pattern. The "KICKOFF" screenshot landing in the same
ribbon as "1st & 10" confirms this design working as intended.

But found a real, previously-unknown bug: `DistancePattern` (`GameWatcher.cs`) only ever matched
digits (`&\s*(-?\d+)`). "3rd & inches" and "1st & Goal" render with **no digit at all** — both
silently failed to match, leaving `YardsToGo` frozen on the previous down's stale value instead of
updating, which could misclassify a genuinely short down as long (or vice versa) in every
down/distance evaluator. Fixed: `DistancePattern` now also matches `inches`/`goal`, normalized to
`"1"` via a new `NormalizeDistanceRaw` helper. Owner's explicit call: treat "Goal" as short for the
hype logic even though real goal-to-go yardage varies (1st & Goal from the 20 is still "Goal").
Build-clean, relaunched, not yet live-verified.

## Not yet confirmed — real next steps

1. **Nothing from steps 3, 5, or 6 has been run against a real/recorded game yet** — same pattern as
   every other recent session, build-clean and logic-traced only. This is explicitly the thing the
   owner most wants verified next.
2. **The screenshot cross-check from step 7 was interrupted mid-analysis** — next session should
   pick up exactly there: read `ScorebugPreset.cs`'s actual crop coordinates for "down" vs
   "situation" against these screenshots, and check `DistancePattern`'s digit-only assumption
   against the "3rd & inches" case specifically. Owner said more screenshots are coming.
3. **None of this session's code changes are committed to git yet.**
4. The UI/UX punch-list items from step 2 (Band Room navigation, arrow glow, Clipper, whistle
   preview, event-card copy-assignment, sound delay) are entirely untouched — deferred in favor of
   the firing-logic work per owner's own prioritization this session.
5. Two risks flagged by the audit but deliberately left unfixed (no clear owner call needed yet,
   just noted): no sanity bound on the buffered-pending baseline (a bad OCR read at the exact
   down-change tick could poison the whole ~750ms window for any of the four buffered evaluators);
   and a residual, much-lower-probability version of the original score-misread bug (two different
   bad misreads landing on consecutive ticks with no reversion between them could still commit
   garbage).
6. A prior handoff (Session27) flagged a pick-six/fumble-return touchdown routing to the wrong
   team. Current `TouchdownHelper.cs` reads correct against the audit — unclear if it was fixed
   elsewhere without updating that doc, or never actually reproduced. Worth a live check specifically
   on a defensive/return touchdown.
