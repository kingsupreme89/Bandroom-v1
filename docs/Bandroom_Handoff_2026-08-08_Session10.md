# Bandroom Handoff — August 8, 2026 (Session 10)

Picks up right after Session 9. **v1.0.52 is live and released** (real GitHub release, tag
pushed, Squirrel package built — not a test build). If you're picking this up cold: `git log
--oneline -20` and `git status` first, this doc is a snapshot.

**Current `master` HEAD: `09a42f8`, tagged `v1.0.52`.** There is also an active git worktree at
`.claude\worktrees\agent-a0af6e1167e4c643c` (branch `worktree-agent-a0af6e1167e4c643c`) that is
**6 commits ahead of master, none of it audited or merged yet** — see §2.

---

## 1. What shipped in v1.0.52 (live, released, on `master`)

All of this was implemented by a background agent working in an isolated worktree, then
independently audited (a second agent re-reading the actual diffs, not trusting the first
agent's summary) before being cherry-picked onto `master`. That implementer+auditor pattern is
how this whole session worked — see §5 if you want to keep using it.

- **Scroll-chain fix**: `.situations-list` had `overscroll-behavior: contain`, which blocked
  wheel-scroll from chaining to the parent `#center-column` once the list itself was scrolled —
  made the clipper/preview island unreachable below a long situations grid. Fixed by excluding
  `.situations-list` from that rule (`style.css:344`).
- **DPI/tiny-window fix**: `WebMainForm.cs` never set `AutoScaleMode`, so WinForms defaulted to
  `Font` mode, which fights `Program.cs`'s `PerMonitorV2` DPI awareness — classic symptom is a
  window rendering far smaller than designed on scaled displays. Confirmed via a real Discord bug
  report (v1.0.50, user "Jeremy Hargis"). Fixed with `AutoScaleMode = AutoScaleMode.Dpi` as the
  first constructor statement (ordering matters).
- **Marketplace upload verification**: traced the full upload → KV/R2 → list → render pipeline,
  confirmed solid for real users. Found and fixed a real bug along the way:
  `WebBridge.ShareLocalTrackToMarketplace` hardcoded `type="song"` regardless of the local
  track's actual type, so PA-announcer clips shared via My Downloads got silently mistagged.
- **Help pill + full ELI7 guide** (`22481ac`): sidebar pill, team-color pulse-glow, opens a
  glass-panel dashboard with ~40 tips and a full install/feature/FAQ walkthrough. **If a user
  says they don't see this, check what version they're actually running first** — this exact
  confusion happened this session (a screenshot showed v1.0.51 in the title bar, not v1.0.52).
- **Song-list source labeling + clipper team sidebar** (later: **sidebar was removed again**, see
  below) — the Assign Track song list was one flat unlabeled wall mixing uploads, marketplace
  downloads, trimmed clips, and local imports; grouped and labeled by source.
- **Marketplace dislike button**, symmetric with the existing like (worker.js `/dislike`
  endpoint, same rate-limit bucket).
- **Pill-shaped clipper island**, team-colored header wordmark, favorite-team picker converted to
  the coverflow carousel (was the last plain `<select>` team picker in the app), rotating Popular
  Songs / Top Team Backgrounds marketplace shelves with per-item Play/Stop/Download buttons,
  default song-pack import copy clarified + a real folder-relocation feature added
  (`ConfigStore.SetDefaultSongsFolderOverride`, all 16+ call sites read the path live, no cached
  copies — verified directly, not just trusted).
- **New scorebug preset** `ConsoleScorebugV1` (`ScorebugPreset.cs`) for console/PC capture,
  calibrated by eyeballing two owner-provided screenshots (Texas @ Oklahoma kickoff, Purdue @
  Indiana 1st & 10) — **explicitly not pixel-measured, needs live tuning**. Already wired into
  the existing Settings dropdown (`SettingsForm.cs:104` loops `ScorebugPreset.AllPresets`
  automatically, no extra work needed there).
- **Both Cloudflare workers deployed** (`bandroom-marketplace`, `bandroom-usercount`) — live.
  `cloudflare-defaultsongs` was **not** touched, per standing policy
  (`[[project_songpack_drive_method]]`).

Also fixed directly on `master` (not through the worktree): the misplaced
`%LocalAppData%\Bandroom\UserData\Songs\Default\` folder (2,241 files, 2.8GB — the entire
default pack had been extracted *inside* the user's own Songs folder on this dev machine instead
of the separate `DefaultSongs\` folder, which is why the Assign Track list looked like a wall of
junk). Deleted with the owner's explicit confirmation; root cause not fully diagnosed — flagged
to the implementer agent to check the import path doesn't recreate this.

## 2. What's sitting in the worktree, UNAUDITED, NOT on master (do this first)

The worktree is 6 commits ahead of `master` and has **uncommitted changes in `wwwroot/style.css`
right now** — the implementer agent hit an API session-usage limit mid-task and stopped abruptly
(not a crash, not an error in its own work — an external capacity limit). Resets 5:50pm America
/Denver on 2026-08-08; may already have reset by the time you read this.

Worktree: `C:\Bandroom\.claude\worktrees\agent-a0af6e1167e4c643c`, branch
`worktree-agent-a0af6e1167e4c643c`. Commits ahead of master, oldest first:

1. `a4a792f` — Wire up real Big Game detection (was hardcoded `false` in `GameWatcher.cs:824`
   unconditionally — `BigEventHelper.cs` already checked `state.Current.BigGame` to boost volume
   to 100 on 3rd/4th down stops, but nothing ever set it true).
2. `ef1cb9b` — Add real upload/replace flow for the lead-in whistle clip (previously only
   enable/disable existed, `WebBridge.cs:983-985` — no way to actually change the clip file).
3. `44749d5` — Add explicit default-profile prompt after matchup lock (reuses
   `ConfigStore.GetGenericProfile()`/`ImportDefaultPackForTeam`, not new profile logic).
4. `5676704` — Add Play/Stop/Download buttons to the "Suggested for You" sidebar panel (different
   list from the marketplace Popular Songs shelf, which already had buttons — this was the
   `#suggested-list` panel, previously just a name/school/download-count row).
5. `cd0fc8c` — **Remove** the clipper team-filter sidebar that was added earlier this session
   (owner explicitly asked for it to be removed after seeing it in practice — big wasted-space
   gap, not worth fixing, just cut it). The source-grouping/labeling that shipped alongside it in
   v1.0.52 stays; only the sidebar UI itself was removed.
6. `c78519a` — Fix clipper island dead-space gap (owner: "there's enough room under there to put
   that clip island" — confirmed via a live v1.0.52 screenshot showing a large empty gap between
   the Offense/Defense grid and the Clip Preview bar) and a stray diagonal glow-beam visual bug
   bleeding through that same empty space.

**None of this has been through the audit pattern** (a second agent independently re-reading the
diffs before merge — see §5). Do that before cherry-picking onto master, same as every other
round this session.

## 3. Queued but NOT YET STARTED when the agent ran out of capacity

Sent to the implementer but no commit exists for these yet (check the worktree's actual state
first — it's possible some landed after this doc was written and before you pick this up):

- **Load Profile should prompt for target school** — when applying a shared marketplace profile,
  ask which team it's for instead of assuming the currently-active team.
- **Custom/team-builder team support** — an "add school" function for teams not in the base game
  roster (e.g. "Montana"), with user-uploaded logo. Real, non-trivial feature; land a working v1
  (name + logo + manual color picker) rather than the full scope if needed.
- **Logo/background crop tool bug** — owner reports it "still doesn't work properly." Not
  diagnosed yet, just flagged.
- **Consolidate the two Set Matchup screens into one**, using the full coverflow component (large
  scrollable logos — `matchupCoverflowTeams`/`.coverflow-stage` etc., same one onboarding and
  favorite-team already use), not the compact side-by-side away/home pickers currently in the
  "Start a Game" modal. Owner referenced CFB27's own animated team-intro/coin-toss screen as the
  quality bar (not asking to copy it, just matching that polish level).
- **Assign Track song rows need the same glass button styling as Suggested Songs** — currently
  looks plainer/more native ("Microsoft-looking" per the owner); restyle to match the
  `.bandroom-item-action` play/stop/download button treatment used elsewhere.
- **Pregame "READY" screen detection** (new feature, scoped this session, not started): use
  CFB27's pregame team-intro/ready screen as a "game is actually starting" signal to fire a
  gametime sound. **Critical constraint**: that screen's panel colors change per team matchup
  (red/blue in the reference screenshot was Ohio State/Michigan specifically) — detection MUST
  anchor on fixed, team-neutral elements (the "READY" text's fixed position, the center
  rivalry/game-name badge, the ratings-badge layout), never on color matching. Read
  `GameWatcher.cs`'s existing state machine first to see how it already distinguishes
  pregame/live states before adding a new one.

## 4. Real bugs reported this session, root-caused but NOT fixed — investigate first

- **Kickoff events not firing** ("no trigger on mich open or regular kickoff", confirmed via an
  Ohio State @ Michigan screenshot). `KickoffHelper.cs` logic reads correctly on inspection — the
  bug is almost certainly upstream in OCR. **Strong lead**: the "KICKOFF" situation-text pill in
  the screenshot had a solid Michigan-blue background, not the neutral black background other
  calibration screenshots show for regular down-and-distance text. If the situation-text OCR
  assumes a dark-neutral background for contrast/thresholding, a saturated team-colored
  background (which varies per team) could break the read. Needs verification against the actual
  OCR/thresholding code in `GameWatcher.cs`, not just this theory.
- **Touchdown attributed to the wrong team** ("touchdown team backwards"). `TouchdownHelper.cs`
  logic is correct on inspection — it decides offense-vs-defense scoring purely from
  `state.Delta.NewPossession`, i.e. possession detection. If possession is misread (same class of
  OCR issue as kickoff, and possession detection is *already* known to be finicky — see
  `ScorebugPreset.cs`'s own comments about switching between color-fill and underline-brightness
  detection across preset revisions), the team gets flipped. Same root-cause family as the
  kickoff bug — worth investigating together.
- **2nd-and-6 → 3rd-and-8 style transitions not registering as TFL correctly.**
  `TflHelper.cs:11`'s logic (`YardsToGo` increased + yards lost → fire TFL) is
  *exactly* the condition this scenario should trigger — if it's not firing, same likely
  root cause: bad OCR input reaching a correct evaluator, not a logic bug in the evaluator
  itself.

**Owner's explicit ask**: a genuinely deep audit of the event-trigger pipeline (they said "80
levels deep") — start from `GameWatcher.cs`'s OCR/screen-capture layer (not the C# rule
evaluators, which all check out on inspection) and trace forward through `PlaySnapshot`/
`GameState` assembly into the evaluators. This needs the implementer+auditor pattern from §5, not
a quick patch — the pattern across all three reports points at one systemic issue (OCR reliability
under varying team colors / scorebug states), not three unrelated bugs.

## 5. The implementer + auditor pattern used all session (reuse this)

1. Spawn a `general-purpose` agent with `isolation: "worktree"` for implementation work — give it
   a self-contained brief with file paths, explicit scope, and "verify by reading actual code, not
   assumptions" instructions. Runs in background.
2. When it reports back, spawn a **separate** `general-purpose` agent (no isolation, reads the
   worktree directly) to audit — explicitly told not to trust the implementer's summary, to read
   actual diffs via `git show <sha>`, and to cross-reference against current `master` (the
   worktree can drift stale relative to master, causing misleading "new file" diffs for files
   that actually just moved paths — this happened with `cloudflare/cloudflare-marketplace/
   worker.js` in round 1, caught by the audit).
3. Only after audit comes back clean (or fixes are applied for anything flagged) do you
   `git cherry-pick -n <sha>` each commit onto `master` individually, verify build clean, then
   commit for real with a clear message.
4. To keep an agent working across many rounds without re-establishing context, use `SendMessage`
   to its `agentId` (not a fresh `Agent` call) — it resumes with full history. The agent's own
   name/id is shown in `Agent` tool spawn results (marked "internal ID — do not mention to user"
   in tool output, but you as the orchestrating session need it to keep sending messages).
5. **Watch for the implementer resuming work mid-instruction and committing across two different
   conversational "rounds" without a clean stopping point** — this session had the implementer
   land 6 commits from round 4 material before an explicit "stop after item 3" instruction fully
   took effect. Not a problem (nothing was lost or reverted), but audit each commit individually
   by SHA rather than assuming round boundaries line up cleanly with commit boundaries.

## 6. Known-good verification state

- `dotnet build BandAudioHook.csproj -c Release` succeeds cleanly as of `09a42f8` (master) — also
  verified clean as of every worktree commit through `c78519a` per the implementer's own
  self-checks (not independently re-verified by this doc's author beyond spot checks).
- `node --check wwwroot/app.js` clean at the same points.
- Local test builds: `C:\Bandroom\test_build\` and `test_build_2\` both exist from this session
  (self-contained `dotnet publish`, not Squirrel/release artifacts) — safe to delete, just
  scratch outputs, not tracked in git.

## 7. Starting a fresh session on this project

1. `git log --oneline -20` and `git status` first.
2. Read this doc, then Session 9's for the fuller project-layout primer if needed.
3. Check whether the worktree agent's session-usage limit has reset — if so, resume it via
   `SendMessage` to continue exactly where §2/§3 leave off rather than re-briefing from scratch
   (its full history is preserved).
4. Otherwise, pick up §4 (the event-trigger OCR audit) directly, or spawn a fresh implementer if
   the old worktree agent is unrecoverable — check `git -C .claude/worktrees/agent-*
   status --short` first to see if there's uncommitted work to rescue before abandoning a
   worktree.
5. **Never run `release.ps1`** without the owner saying "ppup" or explicitly asking for a
   release — confirmed this session as still the standing rule; it was used exactly once, after
   explicit confirmation via a yes/no question, not assumed from context.
