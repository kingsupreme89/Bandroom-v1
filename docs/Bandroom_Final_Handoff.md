# Final Handoff

Source: `D:\Claude\Projects\tools\BandAudioHook` (git, remote `origin` =
https://github.com/kingsupreme89/Bandroom-v1). This supersedes all prior handoffs for current
state.

**Current shipped version: v1.0.45.** Source committed and pushed at `main` @ `19a5679`, release
published at https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.45. `git status` is
clean as of writing this. **Google sign-in is genuinely live and confirmed working end-to-end**
(carried over from last handoff, unchanged this session).

**Worker deploys still outstanding** — code for both is committed, but neither has actually been
`wrangler deploy`'d with this session's changes yet:
- `cloudflare-marketplace/worker.js` — ratings endpoints, admin override, `/profile` logo-sync field.
- `cloudflare-usercount/worker.js` — new Discord relay endpoint.
Run `npx.cmd wrangler deploy` from each directory before any of this session's server-side features
actually work live, even though the app itself is already shipped and installable.

## Session recap: v1.0.44 → v1.0.45 (six features, one session)

1. **Marketplace rating system.** `views`/`downloads` added to item metadata (default 0 for
   pre-existing items). New `POST /view/<type>/<id>` and `POST /download/<type>/<id>`, same
   KV-read-modify-write pattern as `/like`. `/list` takes `?sort=newest|views|downloads|likes`. A
   sort dropdown was added to the marketplace hub UI.
2. **Tackle-to-sound latency fix.** The one `GameWatcher.cs` poll delay actually on the
   tackle-detection critical path was tightened 400ms → 250ms (the other three delays are
   recovery-path only and were deliberately left alone — don't tighten those without re-reading why
   they're there). `AudioPlayer.Warmup()` pre-opens a throwaway output device once at startup
   (`Program.cs`) to absorb the one-time Windows audio-driver cold-start cost before the first real
   trigger fires. **Not verified against a live game this session** — reasoned from code path only,
   worth confirming the perceived delay is actually gone next time someone plays a real game.
3. **Batch logo/icon import tool — for the app owner only, not end users.** Hidden entry point:
   `Ctrl+Alt+Shift+L`. Reuses the existing single-team crop canvas/math unchanged, just wraps a
   queue around it (pick a folder → crop each image in sequence → auto-advance). Zoom range widened
   100–400% → 100–900% at the same time (applies to the single-logo path too, not just batch).
4. **End-user local song upload pipeline** — a user can import their own audio file without going
   through the marketplace: name it → `TrimmerForm` auto-opens for clipping → same
   `NormalizeAndLimit` limiter/leveling marketplace tracks get → saved into a new local-tracks
   manifest (`ConfigStore.cs`, mirrors the existing `local_tracks.json`-style pattern) → assignable
   to any trigger like a marketplace download. "My Downloads" gained an explicit, opt-in-only
   "Share to Marketplace" button per locally-created track (never shown for tracks that already came
   from the marketplace) — nothing auto-shares.
5. **Custom team logos now sync across a signed-in user's devices.** New `CustomTeamLogos:
   Dictionary<string, TeamLogoEntry>` field on `ConfigStore.UserProfile` — **this is a "newest
   `UpdatedAtUtc` wins per key" merge (`WebBridge.MergeLatestWins`), not the existing "counter only
   ever goes up" `MergeCounts` max-merge** used for `EventCounts`/`GamesWatchedByTeam`. If you add
   another non-counter dictionary field later, copy `MergeLatestWins`'s pattern, not `MergeCounts`'s.
   Full sync triangle updated: `ConfigStore.UserProfile` ↔ `ProfileSyncService.cs` push/pull ↔
   `worker.js` `/profile` GET/PUT (capped at 50 logo entries server-side — see gap below). Toasts on
   push failure ("saved locally but couldn't sync") and on pulled changes from another device
   ("Logo updated for X, Y"), suppressed on the very first sync via a manifest flag so a fresh device
   doesn't get spammed catching up on pre-existing logos.
   - **Known gap, intentionally not fixed**: `capLogos` caps at the first 50 dict entries by JS
     object key insertion order, not recency — if a user ever exceeds 50 customized team logos, which
     entries "win" on that sync is arbitrary rather than newest-first. Not a real risk at current
     roster size; revisit if that changes.
   - **Known gap, intentionally not fixed**: the disk-write → `CustomTeamLogos` update →
     `team_logo_sync.json` manifest update sequence in `SaveCustomTeamLogo`/`ApplyPulledLogos` is
     three separate un-transacted writes, same best-effort convention as every other manifest in
     `ConfigStore.cs` — a crash mid-sequence could leave a logo written to disk but not yet reflected
     in what gets pushed to the cloud until the next edit. Consistent with existing risk tolerance,
     not a regression.
6. **Admin-only marketplace override, for the app owner only.** `DELETE /item/<type>/<id>` now
   accepts an `X-Admin-Token` header that bypasses the normal per-uploader `ownerToken` check (which
   still fully applies to everyone else — regular users can only ever delete their own uploads, this
   was already true before this session and is unchanged). New admin-exclusive `PATCH
   /item/<type>/<id>` for renaming/re-categorizing any item, no ownerToken fallback. The admin secret
   lives in `admin_token.local.txt` at the **source checkout path**
   (`D:\Claude\Projects\tools\BandAudioHook\admin_token.local.txt`), read only if that exact path
   exists — **deliberately NOT** added to `<Content Include>` in `BandAudioHook.csproj` and
   **deliberately NOT** like the Google client secret pattern. This is intentional, not an
   oversight: unlike the Google secret (safe to ship to every install), this one grants delete/edit
   power over every other user's upload — if it ever ended up inside the packaged installer, any
   user could extract it from the .exe and impersonate the admin. Don't "fix" this into `<Content
   Include>` later thinking it was forgotten.
   - Generated token (already written to `admin_token.local.txt`, needs the matching Cloudflare
     secret set once): `f72b65e60f8a6a772e7abeb52c520b4d8cf9964b9c6ba496dcd060c3e8e69b43` — run
     `npx.cmd wrangler secret put ADMIN_TOKEN` from `cloudflare-marketplace/` and paste it in.

## Also this session (not yet on `main` in the sense of "finished" — feature #6/7, still pending as of last check)

**Live Discord chat feed panel** — read-only, one-way feed of a Discord channel's messages shown in
a new dockable panel (`Discord` pill next to `My Downloads`). Deliberately **polling**
(`GET /discord/messages?after=`, 4.5s client interval, 3s per-isolate server cache), not a live
gateway WebSocket — simpler to keep running than holding a persistent connection open. Built on
`cloudflare-usercount/worker.js` (it already had the isolate-cache pattern this needed; didn't stand
up a separate worker for one endpoint). Needs, before it does anything: a Discord bot created in the
target server with `View Channel` + `Read Message History` only (no send/manage), then
`wrangler secret put DISCORD_BOT_TOKEN` and a `DISCORD_CHANNEL_ID` var/secret, both against
`cloudflare-usercount`, then deploy.

**Important caveat surfaced mid-session, unresolved as of this handoff**: the user mentioned the
target Discord channel may not be on a server they own/administer — just a thread they have member
access to. Adding a bot requires "Manage Server" permission, which the user may not have. If so,
either whoever does have that permission on that server needs to invite the bot on the user's
behalf, or this feature has no working target server yet. **Do not assume this is resolved** — ask
before treating the Discord feed as ready to configure for a specific server.

## Multi-pass bug audit (this session's version of the "5-deep audit" precedent from v1.0.44)

Before shipping v1.0.45, a dedicated audit pass (cross-feature collisions, the sync triangle,
security, counter-vs-newest-wins merge boundaries, resource/perf, build sanity) was run across all
six features, since they were built by six separate background agents with no visibility into each
other's work. **One real bug found and fixed**: the new admin `PATCH /item/<type>/<id>` endpoint set
`name`/`school` from the raw request body, bypassing the `sanitizeSegment()` sanitizer the normal
`/upload` path uses — since `app.js` renders those fields via `innerHTML` in item tiles, this was a
stored-XSS path reachable only by whoever holds the admin token, but still fixed to route through
the same sanitizer as every other write path. Everything else audited clean (sync triangle field
agreement, atomic-merge granularity, admin token exclusion from git and from packaged `Content`,
timing/empty-string bypass check on the token comparison, no DOM id or function-name collisions
across the six features' `app.js` changes).

## KNOWN ISSUE — Google OAuth consent screen is still in "Testing" mode

Unchanged from last handoff. In Testing mode, Google only allows pre-approved test-user emails to
sign in; anyone else gets Google's own "Access blocked" page, which never redirects back to
Bandroom — indistinguishable from a silent timeout from the app's side. **To fix**: Google Cloud
Console → `bandroom-504621` project → APIs & Services → OAuth consent screen → add test user emails,
or publish the app.

## Marketplace/usercount workers — architecture notes (unchanged, still true)

- Both workers are on Cloudflare's free plan, effectively $0/month at this app's scale — worth
  re-checking now that the Discord relay adds outbound calls to a third-party API on top of KV.
- **Do not move the marketplace off Cloudflare onto Google Drive** — Drive access is per-user by
  design, so there's no way for one user's app to browse another user's uploads via Drive.
- The index-per-type pattern in `worker.js` (`readIndexRaw`/`rebuildIndex`/`getIndexIds`) is the
  established pattern for "list without list()" — extend it the same way for any new endpoint that
  needs to enumerate KV records.

## Starting a fresh session on this project

1. Read this file — sole "current state" source.
2. `cd D:\Claude\Projects\tools\BandAudioHook`, `git log --oneline -5`, `git status` — should be
   clean at `19a5679` (v1.0.45).
3. Confirm `google_client_secret.local.txt` AND `admin_token.local.txt` both exist locally before
   building/releasing — both gitignored, so a fresh clone or new machine won't have either. Without
   the Google secret, sign-in fails closed (safe, silent beyond crash.log). Without the admin token,
   admin mode simply doesn't activate (also safe, also silent) — `admin_token.local.txt` is NOT
   packaged into any build, so this is expected on any machine other than the owner's own dev
   checkout.
4. **Deploy both workers before assuming this session's server-side features work** — code is
   committed but `wrangler deploy` was not run for either `cloudflare-marketplace` or
   `cloudflare-usercount` this session. See the deploy list at the top of this file.
5. Still outstanding, not yet delivered:
   - A broader UI-consistency pass ("all sliders, glass effects, transitions, animations... all
     smooth") beyond the fixes already shipped — a full sweep of every panel still hasn't been done.
   - The trigger/event confirmation status noted in `Bandroom_Trigger_Event_List.md` is still stale:
     `WebBridge.cs`'s `ConfirmedTriggers` set shows only 7 of 33 assignable triggers confirmed live in
     a real game. Worth reconciling next time triggers come up.
   - Whether the 250ms tackle-detection poll interval actually fixed the perceived audio delay —
     needs a live game to confirm, wasn't testable this session.
   - Discord feed: blocked on confirming the target server/permissions question above.
6. **Never run `release.ps1` without the user explicitly saying "ppup"** in the live conversation.
7. **Watch out for `release.ps1`'s tag/push step under `$ErrorActionPreference = "Stop"`**: this
   session, `git push origin $tag` actually succeeded but PowerShell's native-command stderr
   wrapping made it look like a terminating error (see this repo's own tool-usage guidance on
   `2>&1` with native exes), so the script aborted right after tagging and never reached step 5
   (`gh release create`) — leaving a pushed tag with NO published release/assets, and in this specific
   case the tag also pointed at the wrong commit because `git add`/`commit` had never been run first.
   **The fix that was applied**: delete the bad tag locally and on the remote
   (`git tag -d <tag>` + `git push origin :refs/tags/<tag>`), commit the actual changes, re-tag on
   the real commit, push the tag, then run `gh release create <tag> <files in squirrel_releases/>
   --repo kingsupreme89/Bandroom-v1 --title "Bandroom <tag>" --notes "..."` manually using the
   already-built assets (no need to rebuild — the Squirrel pack step had already succeeded and
   produced correct binaries from the working tree, only the git/GitHub side was broken). **Going
   forward: run `git add`/`commit`/`push` BEFORE `release.ps1`, not after**, so the tag always lands
   on a commit that actually contains the release's code.
8. When adding new fields to `ConfigStore.UserProfile` in the future: the sync triangle is three
   places that all need to agree — the C# record itself, `ProfileSyncService.cs`'s Push/Pull, and
   `worker.js`'s `/profile` GET/PUT. Missing one silently drops that field from cross-device sync
   without erroring. Also decide up front whether the field is a monotonic counter (`MergeCounts`,
   max-per-key) or a "latest edit wins" field (`MergeLatestWins`, newest-timestamp-per-key,
   introduced this session for `CustomTeamLogos`) — using the wrong merge strategy for the field's
   actual semantics will corrupt data silently rather than error.
