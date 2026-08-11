# Bandroom Handoff — Session 28 (2026-08-10) — commits pushed live to `master`

Picks up right after Session 27 (`docs/Bandroom_Handoff_2026-08-10_Session27.md`, v1.0.73). This
session opened to find the working tree in a confused state — an in-progress, half-applied revert
of a feature from a session with no handoff of its own — and spent most of its time untangling
that before doing a fresh bug audit. **Important: a separate, concurrent session was also actively
editing files in this same repo throughout this session** (see "Concurrent work" below) — its
changes are described here only because this session had to work around them, not because this
session verified or authored them.

## Starting point: an undocumented, half-finished revert

`git log` showed `master` 3 commits ahead of the last documented state (`v1.0.73`), ending at
`3e5b83e` ("Fix metadata form part to use quoted Content-Disposition after rebase"). One of those
three commits, `13f7851`, added a **Player Profile Dashboard public-sharing feature + Audio
Metadata extension** with no handoff describing it — `GoogleUserId`/`IsPublicProfile` on
`UserProfile`, `WebBridge.TogglePublicProfile`, a public `/profile/:sub` + `/leaderboard/users`
pair on the Cloudflare worker, and real LUFS/marketplace-metadata fields
(`IntegratedLufs`/`TruePeakDbtp`/`PrimaryGameTriggerEvent`/`MarketplaceCategory`/
`RecommendedReverbPreset`/`AcousticFingerprint`) on `AudioTrackMetadata`.

On top of that, the working tree had **uncommitted changes that were reverting that entire
feature** — `ConfigStore`/`WebBridge`/worker.js/etc. all had the feature's code half-stripped back
out, `wwwroot/{app.js,index.html,style.css}` showed `MM` status (staged AND unstaged changes,
i.e. touched twice in different states), and a separate, unrelated, complete piece of work was
mixed into the same unstaged diff: relocating the Sound Booth panel from an inline section in the
Adjust/Mixer side panel into its own full modal overlay (`#sound-booth-overlay`, matching the
Sound Bank / My Downloads shell). Also present: legitimate new `voice_poc/` line-bank content
(banter/hype/special_teams script categories, two intro docs, a render test script) and an
untracked `voice_poc/.env` (left alone all session — likely holds a secret, was never staged or
committed).

No handoff explained any of this — not the profile feature, not why it was being reverted, not the
Sound Booth relocation. Owner confirmed the profile-sharing feature was intentional and should
stay; the revert was a false start from an undocumented prior session.

## What this session actually did (4 commits, all pushed to `origin/master`)

1. **`23f4210`** — Finished the abandoned revert cleanly (build-verified, 0 orphaned references)
   — done under the owner's first instruction, before the second instruction clarified the revert
   itself was the mistake.
2. **`0ff27f8`** — Reverted the revert. Restored `AudioTrackMetadata.cs`, `ConfigStore.cs`,
   `IntakeEngine.cs`, `ProfileSyncService.cs`, `WebBridge.cs`, `WebMainForm.cs`, and
   `cloudflare/cloudflare-marketplace/worker.js` to their exact pre-revert (`3e5b83e`) state —
   confirmed 0 diff. For `wwwroot/app.js`/`index.html`/`style.css`, since those files had picked
   up the Sound Booth relocation in the same working-tree session, did a manual merge: hand
   re-inserted every removed profile-sharing HTML/CSS/JS block (Track Info drawer's
   trigger-event/category/reverb-preset/fingerprint fields + real-LUFS/true-peak display, the
   Public Profile toggle/share/leaderboard section, the `#public-profile-overlay` dialog, all
   supporting JS functions and wireControls listeners) back on top of the kept Sound Booth modal
   work, rather than a blind file-level revert that would have thrown the Sound Booth relocation
   away. Verified no duplicate ids/functions, build clean.
3. **`74d5233`** — Ran a 3-agent parallel bug audit (backend/profile files, audio engine/DSP
   files, frontend) and fixed all 5 real bugs found:
   - **Stored XSS** in `renderLeaderboardTable` (`wwwroot/app.js`) — a marketplace player's
     `entry.name` went into `innerHTML` unescaped, unlike every other user-string site in the
     file. Now wrapped in `sanitizeHTML()`.
   - **`ProfileSyncService.PullAsync`** never actually deserialized `isPublicProfile` from the
     cloud `/profile` response (always defaulted to `false`) — a real cross-device bug: opting in
     on device A silently didn't carry over on a fresh sign-in on device B. Fixed.
   - **`WebBridge.SignInWithGoogle`**'s cloud-profile merge did a bare `LoadUserProfile()` ...
     `SaveUserProfile(merged)` pair instead of using `ConfigStore.MutateUserProfile` — exactly the
     lost-update race `MutateUserProfile`'s own doc comment warns about. Restructured so the whole
     merge runs inside `MutateUserProfile`'s lambda, reading the fresh profile at lock-acquisition
     time instead of a pre-`await` snapshot.
   - **Escape key didn't close `#public-profile-overlay`** — never added to the overlay-closing
     ladder when that HTML was reinserted in commit 2. Added.
   - **Lead-in whistle ignored live ducking** — `AudioFileReader.Volume` for the whistle was set
     once at `BuildLeadInProvider` construction and never touched again, unlike the main clip's
     `audio.Volume` which the poll loop updates every 15–30ms. Now `leadInReader.Volume` is kept in
     sync with the same `liveVolume * duckMul` (and fade) formula every tick. `AudioPlayer.cs`.
4. **`5a20447`** — Owner asked to commit everything and push; this folded in the concurrent
   session's completed-looking work at that moment (see below) since it built clean.

All 4 commits pushed to `origin/master` (`58d8ad6..5a20447`). **Not tagged/released** — no
`release.ps1` run this session, still sitting as unreleased commits on top of `v1.0.73`.

## Concurrent work folded into commit `5a20447`

A separate session was live-editing files in this same working tree throughout. This session did
**not** author, deeply verify, or click-test any of the following — just confirmed it built clean
before including it, same caveat as Session 24 gave the `df38e4b`/`cc19e12` commits it didn't
write:

- **`ScorebugPreset.cs`/`GameWatcher.cs`** — promotes away/home-score and clock crop X/W from
  hardcoded CBS-specific constants to new per-preset fields (`AwayScoreFxX/W`,
  `HomeScoreFxX/W`, `ClockFxX/W`). Replaces `ConsoleScorebugV1` with a new `CollegeFootball27`
  preset, calibrated (per its own doc comment) from 7 owner-supplied live screenshots. Possession/
  timeout crops deliberately left uncalibrated for this preset — the reference screenshots show a
  different arrow-shaped possession indicator than the underline/dash signal `GameWatcher`
  currently knows how to sample, and detecting it needs new logic that doesn't exist yet.
- **`WebMainForm.cs`** — moves `_watching = true` to before `GameWatcher.Start()` instead of
  after, since `Start()` fires `WindowFoundChanged` synchronously partway through and that handler
  reads `_watching` to push the watch-state pill to the web UI; the old ordering made every watch
  start briefly report "off".
- **`wwwroot/style.css`** — adds `.icon-btn[hidden] { display: none; }`, fixing the same
  "author `display` beats UA `[hidden]`" specificity trap Session 27 already fixed once for
  `.clipper-assign-list` — this time on `#btn-unlock-matchup`, the first `.icon-btn` ever toggled
  hidden.

**At session end, that other session was still actively editing** — `GameWatcher.cs` and
`ScorebugPreset.cs` picked up further uncommitted changes even after `5a20447` was pushed. Those
are sitting uncommitted on disk right now; this session deliberately left them alone both times
(did not stage, did not investigate further) since they aren't this session's work to finish or
verify.

## What's genuinely unverified

- **None of this session's own fixes were click-tested live** — no GUI-driving access, same gap
  every recent session has had. Build-clean and logic-traced only.
- **The restored Player Profile Dashboard feature itself** was never verified end-to-end even
  before this session touched it (no handoff ever confirmed it was tested) — worth an actual
  sign-in-on-two-devices test given this session found and fixed a real bug in exactly that path.
- **The concurrent session's CFB27 scorebug preset** is explicitly flagged uncalibrated for
  possession/timeout detection by its own doc comment, and this session did not verify the
  score/clock crop coordinates against real footage.
- **The `_watching` ordering fix and `.icon-btn[hidden]` fix** were not click-tested by this
  session either, since they weren't authored here.

## Starting a fresh session on this

1. **Check on the concurrent scorebug-calibration session** — `GameWatcher.cs`/
   `ScorebugPreset.cs` had new uncommitted edits as of this session's end, on top of what
   `5a20447` already committed. Read the current diff fresh (`git status`/`git diff`) rather than
   trusting this handoff's snapshot, and reconcile with whoever's still running it before assuming
   it's done or safe to touch.
2. **`voice_poc/.env` is still untracked, uncommitted, and not gitignored** — almost certainly
   holds a secret. Left alone per explicit owner instruction each time it came up; still worth a
   `.gitignore` entry at some point so a future `git add -A` (e.g. in `release.ps1`) can't sweep it
   in by accident. `release.ps1`'s step 0 does exactly that per Session 27's own gitignore-trap
   note about `.claude/worktrees/`.
3. **Not released** — 4 commits pushed to `master` since `v1.0.73` with no version bump/tag/
   Squirrel pack. Run `release.ps1` when ready ("ppup" per the owner's usual shorthand from past
   sessions), but consider doing a live click-through of the profile-sharing restore first given
   it's had three separate treatments (add → revert → restore) in one day with no live test
   through any of them.
4. **Verify the Player Profile Dashboard's public-sharing flow live**: toggle public on, sign out,
   sign in on a fresh profile / second device, confirm the toggle state actually carries over
   (this is the exact bug this session fixed in `ProfileSyncService.PullAsync` — worth confirming
   the fix actually works against the real worker, not just that it compiles).
5. Everything still open from Session 27's handoff not touched this session remains open: Mac
   marketplace-sharing multipart fix, trim-preview pill follow-up (owner said it resolved itself,
   watch for recurrence).
