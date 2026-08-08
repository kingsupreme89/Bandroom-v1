# Bandroom v1.0.46 Session Handoff

Source: `D:\Claude\Projects\tools\BandAudioHook` (git, remote `origin` =
https://github.com/kingsupreme89/Bandroom-v1). This supersedes the v1.0.45 handoff for current
state. Read this file, not the older one, at the start of the next session.

**Current shipped version: v1.0.46.** Committed and pushed at `main` @ `77f1484`, release published
at https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.46. `git status` clean as of
writing this.

## What changed this session

Three things, all built, audited, and shipped:

1. **Fixed the assign-screen team-switch bug.** Switching between Away/Home while the situations
   panel was open used to show stale data — it looked like both teams shared the same assignments,
   and assign/cancel felt unreliable because the visible panel wasn't reliably tied to live backend
   state. **The actual root cause**: the backend was already correct the whole time — each team's
   song assignments were always saved and loaded independently. The bug was purely in the screen:
   nothing told the already-open panel to refresh when you switched teams, so it kept showing
   whatever it had last fetched. Fixed by having the team-switch action re-open/re-fetch whatever
   category panel was already showing (`wwwroot/app.js`).

2. **Simplified event triggers to home-team-only, on purpose, ahead of the next bigger push.**
   Down-change and situation events (touchdown, turnover, PAT, kickoff) now only fire for the home
   team. This is a deliberate, easily-reversible simplification — the away-team code wasn't
   deleted, just switched off behind one flag (`HomeOnlyEventsForNow` in `WebMainForm.cs`), with the
   old away-aware line left commented directly above each new gate so re-enabling it later is a
   one-line change, not a rebuild. Tackle-for-loss (TFL) was deliberately left firing for both
   teams, since that logic is already confirmed reliable in live play — only the newer, mostly-
   unconfirmed triggers were pulled back to home-only.

3. **Renamed the "not yet confirmed" badge to "Coming Soon."** Any trigger event not yet verified
   live in a real game (still 26 of 33 — see below) now shows a clearer "Coming Soon" badge instead
   of the old, more uncertain-sounding wording.

Two things from earlier in the session, already covered by the v1.0.45 handoff, still true and
unchanged: the marketplace rating system, latency fix, batch logo tool, local song pipeline,
cross-device logo sync, admin override, and the Discord feed panel (still needs your manual
`wrangler deploy` + bot setup before it does anything live).

## What did NOT happen this session (on purpose)

- **No AI commentary engine was built.** You asked for cost research first, got a full tiered
  write-up (`Bandroom_AI_Commentary_Research.md`), then explicitly said "don't do commentary" before
  any implementation landed — a background agent had just started writing the actual engine files
  when it was stopped, and the couple of files it had already touched were reverted back to clean.
  Nothing commentary-related is in v1.0.46. The research doc still stands if you want to revisit it
  later.
- **No patent was filed.** A plain-English pitch/legalities document was written
  (`Bandroom_GameWatcher_Patent_Pitch.md`) with a prominent "not legal advice" disclaimer — that's a
  discussion document for you to bring to an actual patent attorney, not a filing, and nothing legal
  was submitted anywhere on your behalf.

## Multi-pass audit results (this session's version of the "5-deep audit" precedent)

A dedicated 5-pass audit ran on this session's diff before shipping (team-switch fix, home-only
gating, badge relabel, plus an independent re-check of the marketplace). **Result: clean, nothing
needed fixing.** Specifically verified:
- The team-switch fix has no staleness path — there's exactly one place that opens the panel and one
  that closes it (no Escape-key or backdrop-click shortcut bypasses the fix).
- The home-only gate reads `_possession` at the correct point in the same detection tick — no
  stale-read race — and the commented-out away-side code is truly inert, not reachable any other way.
- The badge relabel doesn't collide with any other code still referencing the old label text or a
  renamed class.
- The marketplace's admin-token check, input sanitization on every field that reaches `innerHTML`,
  and the `?sort=` parameter all independently re-checked clean — an unrecognized sort value falls
  back to newest-first silently rather than erroring.
- Build: `dotnet build` clean, 0 warnings introduced. `node --check` clean on both touched JS files.

**One thing flagged, not fixed — a product decision, not a bug**: there's currently no visual
indicator in the app that away-team events are intentionally paused right now. When the away team is
on offense, its events just silently don't fire. To a user who doesn't know about this session's
change, that could read as "broken" rather than "temporarily simplified on purpose." Worth a small
toast or badge if you want it to read clearly as intentional — flagged rather than added unasked,
since it's a UX call, not a defect.

## Release-process note (repeat of the fix applied last session, now working cleanly)

Same PowerShell quirk as v1.0.45's release hit again this time: `release.ps1`'s tag-push step
reports a `NativeCommandError` even though the push actually succeeds (PowerShell wraps a native
command's stderr as a terminating error under `$ErrorActionPreference = "Stop"`, even at real exit
code 0). **This time it didn't matter**, because the actual code was committed and pushed to `main`
*before* running `release.ps1` — so even though the script again stopped short of step 5 (`gh
release create`), the tag it left behind correctly pointed at the real commit. The GitHub release
was then published manually with the already-built Squirrel assets from `squirrel_releases/`, same
as last time. **Keep doing it in this order going forward**: `git add`/`commit`/`push` first, then
`release.ps1`, then manually run `gh release create <tag> <files in squirrel_releases/> --repo
kingsupreme89/Bandroom-v1 --title "Bandroom <tag>" --notes "..."` if the script stops short again.

## 20 ways to make Bandroom better from here

Roughly ordered from smallest/cheapest to biggest lift:

1. **Add the "home-only mode is active" indicator** flagged above — cheapest fix on this list, pure
   UX clarity.
2. **Reconcile `Bandroom_Trigger_Event_List.md` against `WebBridge.ConfirmedTriggers`** — the doc is
   still stale (per the original handoff), now doubly worth doing since triggers are actively being
   worked on this session and next.
3. **Confirm more of the 26 unconfirmed triggers live**, one real-game session at a time, moving them
   out of "Coming Soon" — you're already testing in-game now, this is just formalizing what you find.
4. **Re-enable away-team events once home-only triggers are all confirmed** — flip
   `HomeOnlyEventsForNow` back, the away-side code is sitting right there commented out, ready to go.
5. **Deploy both Cloudflare workers** (`cloudflare-marketplace`, `cloudflare-usercount`) — code for
   ratings, admin override, and the Discord relay is committed but not live until `wrangler deploy`
   runs for both.
6. **Set the `ADMIN_TOKEN` Cloudflare secret** (value already generated and sitting in
   `admin_token.local.txt`) so the admin override actually works against the live worker, not just
   locally.
7. **Resolve the Discord feed's server-permission question** — still unresolved whether you have
   "Manage Server" on the target server; the panel is built but has nowhere to point yet.
8. **The broader UI-consistency sweep** carried over from the v1.0.44 handoff — "all sliders, glass
   effects, transitions, animations... all smooth" — still not done as a dedicated pass.
9. **Fix the Google OAuth "Testing mode" limitation** — still blocking anyone who isn't a
   pre-approved test-user email from signing in at all; this has been a known issue for several
   handoffs now and blocks real user growth more than almost anything else on this list.
10. **Add a real prior-art search** (Google Patents, free) before spending money on the patent path,
    per the pitch document's own recommendation.
11. **Loosen the 50-entry cap on `CustomTeamLogos`** if/when more than 50 teams' logos get
    customized by one user — currently caps by insertion order, not recency, which is a real (if
    currently harmless) correctness gap flagged in the v1.0.45 handoff.
12. **Make the custom-logo write sequence atomic** (disk write → profile field → sync manifest are
    three separate un-transacted writes right now) — low priority given it matches every other
    manifest's existing risk tolerance in this codebase, but worth a look if a "half-updated logo"
    bug ever gets reported.
13. **Read Discord's `Retry-After` header** on 429 responses in the relay worker instead of just
    falling back to cache — a real gap flagged when that feature was built.
14. **Add message-history pagination** to the Discord feed — first load only fetches the most
    recent 50 messages, no way to scroll further back.
15. **Consider periodic/background profile pull**, not just at sign-in — cross-device logo sync
    (and any future cross-device field) currently only catches up when you sign in again, not while
    the app is just sitting open on two devices at once.
16. **Batch-logo import tool rough edge**: the Skip button isn't gated on whether the current image
    actually failed to decode — minor, low-traffic, maintainer-only tool, but easy to tidy.
17. **A lightweight settings export/backup** covering everything (profile, custom logos, local
    tracks) as one bundle, not just the existing separate profile-JSON export — useful before any
    future reinstall/migration.
18. **Rate-limit tuning pass** across all the new marketplace endpoints (`/view`, `/download`,
    `/like`, `/report`) now that there are more of them than when the original limits were set —
    worth revisiting whether the numbers still make sense at current usage.
19. **A "what changed" in-app changelog view** tied to `ChangelogService.cs` and the `-Notes` param
    already passed into `release.ps1` — the data's already flowing into releases, just not
    surfaced anywhere inside the app itself yet.
20. **Revisit the AI commentary idea later, deliberately, as its own scoped session** — the
    research doc is done and free/near-free options exist; there's no urgency, but it's a real,
    buildable feature whenever you're ready to actually greenlight it (you weren't this time).

## Starting a fresh session on this project

1. Read this file first — it's the current "start here."
2. `cd D:\Claude\Projects\tools\BandAudioHook`, `git log --oneline -5`, `git status` — should be
   clean at `77f1484` (v1.0.46).
3. Confirm `google_client_secret.local.txt` and `admin_token.local.txt` both still exist locally —
   both gitignored, neither packaged into any build, both required for their respective features to
   work on this specific dev machine.
4. Both Cloudflare workers still need a real `wrangler deploy` for everything built since v1.0.44 to
   actually be live server-side (see item 5/6 in the improvement list above).
5. **Never run `release.ps1` without the user explicitly saying "ppup"** in the live conversation.
6. **Commit BEFORE running `release.ps1`, not after** — this is now confirmed working cleanly two
   releases in a row once done in this order; don't regress to doing it the other way around.
7. When adding new fields to `ConfigStore.UserProfile`: remember the sync triangle (C# record +
   `ProfileSyncService.cs` + `worker.js` `/profile`) and pick the right merge strategy up front —
   `MergeCounts` (max-per-key) for monotonic counters, `MergeLatestWins` (newest-timestamp-per-key)
   for "latest edit wins" fields like `CustomTeamLogos`. Using the wrong one corrupts data silently.
8. `HomeOnlyEventsForNow` in `WebMainForm.cs` is a deliberate, temporary, one-flag simplification —
   don't "fix" it back to dual-sided without checking with the user first; it was an explicit ask,
   not an oversight.
