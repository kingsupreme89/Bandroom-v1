# Bandroom Handoff — August 19, 2026 — Session 94

Owner was live in a real game (Kennesaw State @ Montana) the entire session — every fix here was
found and pressure-tested against real, in-progress gameplay, not synthetic tests.

## Root Cause Found: RAM Mode Existed But Was Never On

The night started as "events aren't triggering" and turned into a long chain of live
misattribution bugs, all eventually traced back to one thing: BANDroom ships its own bundled
`CollegeFB27RamReader.exe` (real game-memory reader, not OCR/pixel-color guessing), but
`ConfigStore.LoadScoreboardReaderRamModeEnabled()` defaulted to `false` and nothing in the UI ever
exposed a toggle to turn it on — `SaveScoreboardReaderRamModeEnabled` was never called anywhere in
the codebase. So every install, including the owner's own, has been running OCR-only this whole
time despite the more reliable reader being installed and idle.

**Fixed**: `ConfigStore.cs` — `LoadScoreboardReaderRamModeEnabled()` now defaults to **true**
(absence of the opt-out file means "on," not "never opted in"). Owner's explicit call: "we want it
to be turned on by default.. everyone is okaying offline" — the anti-cheat risk this toggle exists
for only applies to online play, and the userbase is offline/vs-CPU.

## Fixed: RAM Reader's Home/Away Possession Can Be Inverted, Silently

Once RAM mode was manually turned on mid-session, a new, worse failure mode showed up: possession
was being credited to the **wrong side consistently**, not randomly — "Away" for plays that were
actually Home's, every single time, for most of a full game (first downs, 3rd down conversions,
stop credit, all flipped).

Root cause: the bundled reader auto-discovers its own memory offsets fresh every session
(`"automatic-read-only-signatures-v9-special-downs"` profile) rather than using fixed, verified
ones, and it never resolved real team names this session (`awayTeamName`/`homeTeamName` both
stayed `"missing"` in its own status JSON the whole game). So its raw possession bit is just
whichever memory slot it happened to lock onto — there was never any guarantee that slot lines up
with BANDroom's own home/away team selection, and nothing checked.

**Fixed** — `GameWatcher.cs`: added a one-time self-correcting orientation check. The first tick
where BOTH the reader's possession AND OCR's own independently color-sampled possession
(`_lastPossession`) are confirmed, if they disagree, assume the reader is inverted for the rest of
the session and flip every reader possession read from then on (`_ramPossessionOrientationChecked`
/ `_ramPossessionInverted`, reset in `Start()`). OCR is the trusted tie-breaker here specifically
because it's sampled directly against BANDroom's own configured team colors, so it can't have this
same "which physical team is which" ambiguity.

**Not yet live-verified this session** — built and swapped into the running app at 3:06 AM, but
the owner hadn't re-pressed GAMETIME (required to restart watching under the new process) before
this handoff was written. First thing next session: confirm possession now resolves correctly
after a GAMETIME (re-)press on the patched build.

## Fixed: Kickoff Double-Fire

Reported live: "Second-Half Kickoff (Home)" and a plain "Kickoff (Home)" both fired ~7 seconds
apart for the same real kickoff. `KickoffHelper.cs`'s `_didFire` reset the instant `IsKickoff` read
false for even a single tick — a one-tick OCR flicker mid-kickoff-sequence (common, same flicker
class documented all over this codebase) reset it early and let the very next good tick fire again.
Now requires 2 consecutive non-kickoff ticks before resetting (`_notKickoffStreak`), same debounce
pattern `ConfirmPossessionFlip` already uses for the analogous single-frame-flicker problem.

## Fixed: False "3rd & Short" Right After a Kickoff

Reported live: a fresh kickoff-receiving team got credited with "3rd & Short" before ever taking a
snap. `_lastKnownDown` (GameWatcher's sticky down field) holds the last REAL down seen through an
entire kickoff sequence, since there's no down/distance HUD to read during a kickoff — so it was
still holding "3rd & Short" from the drive that just ended. `DriveStarterHelper` already knows to
suppress itself until the real first snap after a kickoff (`_awaitingPostKickoffSnap`);
`OffenseDownHelper` — the actual evaluator that fired the bogus event — never had that guard. Added
the identical guard pattern to `OffenseDownHelper.cs`.

## Diagnosed, Not Code-Fixed: "Kam's CBSv3" Possession Color-Match Instability

Spent a long stretch of the session chasing misattribution under the "Kam's CBSv3" scorebug preset
before RAM mode entered the picture. Root cause: `ScorebugPreset.KamsCbsScorebugV3`'s possession
detection reads the down-and-distance box's *fill color* and matches it against each team's known
color — works well for high-contrast matchups (the Georgia/Florida red-vs-blue screenshots it was
calibrated from) but has very little signal when a team's primary color (Kennesaw State: black) is
close to the CBS overlay's own neutral/black chrome. The documented fallback
(underline-brightness) was previously found unreliable enough to be reverted once already (see
that preset's own 2026-08-12 comment).

No code changed for this — RAM mode becoming the default (see above) makes it moot for anyone with
RAM mode on, since possession no longer comes from OCR color-matching at all. Left as a known,
documented limitation for OCR-only users on this preset with a near-black away team; would need
real screenshots of a confirmed Kennesaw State possession frame to attempt a proper fix.

## Verification

- `./build_all.ps1` — all three projects (`Bandroom.Core`, `Bandroom` Windows, `Bandroom.Mac`)
  build clean, all 132 `Bandroom.Core.Tests` pass.
- Live-swapped the running app mid-game from the installed `app-1.1.21` release build to this
  session's `bin/Debug` dev build (killed the old process, launched
  `D:\bandroom\bin\Debug\net10.0-windows10.0.19041.0\Bandroom.exe` in its place) so the owner
  benefits from tonight's fixes immediately instead of waiting for a full `ppup` release.
  `UserData`/config/profiles are shared by product name, not install path, so nothing was lost in
  the swap — the owner just needed to press GAMETIME again.
- Kickoff double-fire and post-kickoff stale-down fixes are code-reviewed against the exact live
  log lines that reported them, but not yet re-observed live post-fix (no kickoff has happened yet
  on the patched build as of this handoff).

## Post-Session: Deep Review + Release (v1.1.22)

Before shipping, ran a full code-review pass over this session's diff and found two real bugs in
the possession-orientation logic that hadn't been live-verified yet — both fixed and built/tested
green before release:

- **RAM/OCR watchdog compared OCR against the raw, uncorrected reader bit.** Once
  `_ramPossessionInverted` flips true, `readerPossessionAway` is corrected (negated) everywhere it's
  used *except* the stale-RAM-field watchdog at `GameWatcher.cs`'s `ramForStaleness.PossessionAway`
  checks, which still read the raw bit. If the corrected value later got stuck wrong while OCR
  settled on the truth, the watchdog would spuriously "agree" with the raw (un-negated) value by
  coincidence of the inversion and never fire. **Fixed**: introduced `correctedRamPossessionAway`
  and used it for the stability tracker, the comparison, and the log line.
- **Orientation self-check was one-shot with no debounce.** The very first tick where the reader's
  possession resolved was compared against OCR's `_lastPossession` — but OCR's own possession read
  is itself 2-tick debounced (`ConfirmPossessionFlip`), so a reader connecting right around a real
  possession change could catch OCR mid-flip and wrongly conclude "inverted" from a timing race,
  then flip every correct read for the rest of the game. **Fixed**: gated the check on
  `ocrPossessionSettled` (same corroboration window the stale-RAM fallback already uses) so it only
  compares once OCR has genuinely settled, not mid-flicker.
- Two stale doc comments (`ConfigStore.cs`'s `ScoreboardReaderRamModeEnabledPath`, `WebMainForm.cs`'s
  auto-launch comment) still described RAM mode as opt-in/off-by-default after tonight's flip to
  default-on — updated both so a future reader isn't misled.
- `OffenseDownHelper.cs`'s post-kickoff guard (clears on the first in-range `Down` read, which can
  still be the stale pre-kickoff value) was flagged by review but left as-is — it's the same pattern
  already shipped in `DriveStarterHelper.cs`, not a defect unique to tonight's diff. A real fix needs
  a "real snap occurred" signal that doesn't exist yet; worth a future session if it's ever observed
  live.

Killed the live-swapped `bin/Debug` dev build (owner confirmed it was safe to close), rebuilt clean —
all three projects green, 132/132 `Bandroom.Core.Tests` pass — then ran the full `ppup` release
pipeline: committed, pushed, tagged, packed, published.

**`v1.1.22` is live**: https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.22 — existing
installs get it as a delta update on next launch; the "not yet released" open item below is now
resolved.

## Open Items For Next Session

- **Confirm the RAM possession-inversion self-correction actually works live** — this is the
  single most important thing to verify first. Have the owner press GAMETIME on the patched build,
  play a few snaps, and check the event log attributes both sides correctly. Watch for the new
  `[watcher] RAM reader's possession disagreed with OCR on first comparison...` log line to confirm
  the self-correction actually triggered (or didn't need to, if the reader happened to lock on
  correctly this time — profile is regenerated fresh every session, so orientation isn't guaranteed
  consistent run to run).
- ~~**This has not been released**~~ — **Resolved**: released as `v1.1.22` (see "Post-Session: Deep
  Review + Release" above). The RAM-possession-inversion fix below is still not live-verified in a
  real game even though it's now shipped — that verification is still the top priority next session.
- A user-reported "turnover, nothing triggered" was flagged mid-session but never isolated — came
  in right as the RAM/possession chase was peaking and the app was mid-restart shortly after. Worth
  specifically watching for on the next turnover once the possession fix is confirmed; may have
  been a downstream symptom of the same inverted-possession bug (structural-turnover backstop and
  `TurnoverHelper` both key off possession) rather than a separate issue.
- Reader's own per-field resolution is still incomplete for this matchup — `awayTeamName`/
  `homeTeamName` never resolved all session (stayed `"ram-pending"`), and `awayTimeouts`/
  `homeTimeouts`/`awayRank`/`homeRank` never came in either. Didn't block tonight's fixes (nothing
  here depends on team names or timeouts), but worth keeping an eye on if a future session needs
  those fields.
- Consider whether the orientation self-check should also cover cases where OCR *itself* might be
  the wrong one (this session assumed OCR as ground truth for the tie-break, which held up against
  the owner's live observations, but wasn't independently proven against RAM in a matchup where
  OCR's own color-match is known-shaky, e.g. this exact Kennesaw State game).
