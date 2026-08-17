# Bandroom Handoff — August 16, 2026 — Session 93

Same idea as always: what happened, explained plain.

## Fixed: Team Pot Showing in FBS Mode (the real root cause, finally)

Owner report, reproduced repeatedly across most of this session: "still seeing team pot smh" /
"i STILL see a team pot in the fbs mode." Turned out to be three separate, stacked bugs, found in
order as each one was ruled out:

1. **Stale WebView2 cache** — ruled out. `WebView2Data` (the persistent Chromium profile next to
   the exe) can serve stale `app.js`/`style.css` after a rebuild despite the existing DEBUG-only
   `Network.clearBrowserCache` call on launch. Manually wiped the folder before every rebuild this
   session as a sledgehammer version of the same fix — didn't resolve it, so this wasn't the cause.
2. **The situations-list layout not re-rendering live on toggle** — real bug, fixed.
   `refreshHbcuMode()` kept the team grid and the Team Pot panel's own hidden-flag in sync live,
   but never re-ran `openSituations()` for the panel that was already open — so the event-card grid
   (and its `hbcu-event-list` narrow-list class) stayed stuck on whatever layout was showing when
   the panel was first opened, even after toggling. Now re-runs `openSituations()` for the current
   category if the panel's open, same pattern `selectTeam()` already used.
3. **The actual root cause** — `#hbcu-pot-panel { display: flex; }` in `style.css` is an ID
   selector, which always beats the browser's own `[hidden] { display: none }` UA rule regardless
   of source order (ID specificity > attribute-selector specificity). So every time JS correctly
   set `.hidden = true` — which it always was doing, confirmed live via Chrome DevTools Protocol
   (`hiddenAttr: true` while `computedDisplay: "flex"`) — the CSS silently ignored it. Every app.js
   fix earlier in the session for this bug was already correct; the CSS just never respected it.
   Fixed with `#hbcu-pot-panel[hidden] { display: none; }`, the same override every other
   custom-display panel in this file already had — this one was just missed.

Diagnosing this required moving past screenshots entirely: launched with
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` and drove the live page
directly over CDP (`Runtime.evaluate`, `Page.captureScreenshot`) via a small Node script — read
`state.hbcuMode`, clicked the toggle programmatically, and read `getComputedStyle` directly instead
of relying on the owner reading pixel colors off a screenshot. Caught one real regression along the
way: a CDP-driven click landing at the same moment as the owner's own click raced two overlapping
`refreshHbcuMode()` calls, so whichever resolved last won regardless of which click was more
recent — fixed with a monotonic token in `wireHbcuModeToggle()` that discards a superseded click's
result, plus disabling the pill for the duration of its own in-flight request.

**Also fixed as a side effect of #3**: once the pot panel could no longer force itself open, the
grid list (`.situations-list`, non-HBCU) turned out to have no `flex` sizing of its own inside
`.situations-body`'s flex row — it was only ever getting stretched to full width because
`#hbcu-pot-panel`'s `flex: 1` sibling was pulling the row wide. With that pull gone, the grid
collapsed to a cramped single column. Gave `.situations-list` its own explicit `flex: 1; min-width:
0;` (still overridden by the narrow `.hbcu-event-list` variant) so FBS mode's card grid is full-width
regardless of whether Team Pot exists in the DOM at all.

**Also fixed along the way**: the pill's on/off state was only shown via a dot color, which was
genuinely unreadable from a screenshot and made live diagnosis impossible — the pill now also
says "HBCU Mode: On/Off" in plain text.

## Fixed: Volume Popover Rendering Clipped/Invisible (2 spots)

Same bug class the Share/Settings popovers were already fixed for (2026-08-11): they used to be
`position: absolute` nested inside a `.glass`/backdrop-filter card, which traps `z-index` in its
own stacking context and clips the popover instead of floating it. The event-card volume popover
and the Team Pot row's volume popover were both missed in that earlier fix. Migrated both to the
same `openCardPopover`/`closeCardPopover` (reparent to `document.body`, `position: fixed`,
`.slide-open` transition) pattern the other two already use. Also fixed the Clipper song list's
"Share to..." popover the same way — it was still appended to its row instead of `document.body`.

## Fixed: Rename Dialog Opening Behind My Downloads

`#rename-overlay` (and its sibling picker overlays) were `z-index: 10`; `#my-downloads-overlay`
(which it opens on top of) is `z-index: 60`. Bumped to `70`, matching `#team-profiles-overlay`'s
existing "topmost modal" convention rather than inventing a new value.

## Fixed: Square Glow Around Header Pills

`.header-right` (wraps Not Watching/Teams/Save) needs `overflow-x: auto` for its scroll-mask
effect, which forces `overflow-y` to also compute as non-visible — clipping the pills' soft round
`pill-glow-pulse` box-shadow flat at the top/bottom and reading as a hard square halo instead of a
glow. Padded the clipping box vertically with an equal negative margin so layout is unaffected but
the glow has room to fade before the clip edge — same root-cause pattern as the pre-existing
`#header-bar` box-glow fix a few lines up in the same file.

## Fixed: Preview Playback Fading Out Early

Owner: "remove fade for previews, I want to hear the whole song. HBCU shouldn't be fading anyways."
- `AudioPlayer.cs` (Windows) and `AudioPlayer.Mac.cs` both had their fade-out logic skip only when
  the explicit `noFade` flag was set — now also skips whenever `isPreview` is true, so previewing a
  song from the Clipper/Sound Bank always plays it through in full regardless of its in-game fade
  settings. Mac's `afplay`-based hard-stop-at-deadline had the identical bug, fixed the same way.
- Team Pot's ad-hoc playback (`FireAdHocForSide` in `WebMainForm.cs`) now always passes `noFade:
  true`, ignoring any per-song fade settings — pot shuffle is continuous background music, not a
  scripted cue, so it should never fade regardless of what a song's individual settings say.

## Fixed: Lowered Default Master Volume

Default `MasterVolume` in `ConfigStore.AudioSettings.Default` was `72`, but the owner still needed
to manually turn it down to `50` every time. Lowered the default to `50` to match. Only affects a
fresh profile with no saved `audio_settings.json` yet — anyone's already-saved value (including the
owner's own, already `50`) is unaffected.

## Fixed: Imported Songs Not Appearing in Sound Bank

`importLocalSong()` refreshed My Downloads after a successful import but never invalidated
`_clipperAssignLibrary`, the cached song list Sound Bank/Assign Track reads from — same stale-cache
class of bug the song-pack import path was already fixed for. Newly imported songs now show up in
Sound Bank immediately instead of needing an unrelated team-switch to force the cache to clear.

## Added: Batch Add to Team Pot

Owner: "i need to be able to batch add songs to pot." Ctrl/Cmd-click in the song picker now
multi-selects rows when adding to a Team Pot (tracked in `_clipperAssignMultiSelected`, a plain
click still does the old single-select behavior); the action button becomes "Add N to Pot" and
loops `AddToHbcuPot` over every selected song in one action.

## Added: Favorite a Song

Owner: "we need a way to favorite a song in the song list... next to the imported tracks pill." A
★ Favorites filter pill sits next to Imported Files in the song picker; every row gets a star
toggle (persisted in `localStorage`, cross-cuts every source — Sound Bank, marketplace, imports,
trimmed clips all show up together under Favorites). Un-starring while viewing the Favorites pill
drops the row from view immediately. Shared by both the event-assign flow and the Team Pot add
flow.

## Added: Full Event Registry Audit

Owner asked to "verify all events and label them so we know what we have," after asking why two
"First Down" cards exist and whether a Field Goal event exists. Answer for both, now documented in
two places:
- **Why two First Downs (and 2nd/3rd/4th)**: intentional, not a bug. `LegacyDownEventAlias` in
  `WebMainForm.cs` falls back to the old bare `"1st Down"`/etc. card if the modern
  `"Offense: Earned First Down"`/etc. card is empty — un-retiring the legacy cards on 2026-08-15
  was a deliberate fix for teams who'd only ever filled in the old slot. Assign to the modern card;
  leave the legacy one blank.
- **Field Goal**: already exists and is auto-detected (`Offense: Field Goal Made` /
  `Defense: Field Goal Missed by Opponent`, both via `FieldGoalPATHelper.cs`/
  `FieldGoalMissedHelper.cs`).
- Confirmed `Offense: Third Down Short`'s threshold is already exactly what the owner described
  (3rd & 5 or less) — `OffenseDownHelper.cs`'s `isShort` check, corrected to `<= 5` back on
  2026-08-11.
- Found (and documented, not yet fixed): `Other: Pregame Tunnel` already exists as an assignable
  card but has zero detection wired up — no OCR, no manual hotkey, unlike `Other: Pregame Take the
  Field` which has both. It's a fully dead cue right now. Needs a real screenshot of the actual
  in-game tunnel screen/moment before real detection can be built — flagged for the owner to send
  next session.
- Published as a standalone artifact (full table, every event, plain-language notes, no blanks)
  and folded a condensed version into the in-app Help & Guide (`HELP_GUIDE_HTML` in `app.js`) under
  a new "What triggers each situation?" section, right after "Assigning songs," so the same
  reference lives in-app for end users, not just this session's chat.

## Fixed: Download Counter Stuck at 0 (post-`ppup` `lehgo` deploy)

Owner report after the app release: "i cant see how many dl i have anymore on the ticker."
`bandroom-usercount`'s `/downloads` endpoint was returning `{"count":0}` live.

- GitHub's REST `GET /repos/.../releases` list endpoint was reliably returning HTTP 200 with a
  genuinely empty `[]` body for this repo — confirmed both unauthenticated and with a valid token,
  so not a rate-limit/transient issue, the REST list endpoint itself was broken for this repo. Since
  `res.ok` was true, none of the worker's existing error-fallback paths ever caught it, so this got
  computed as `0` and cached as the real download count for up to an hour.
- GitHub's GraphQL API returns the exact same data correctly (confirmed live: 88 releases, real
  per-asset counts) — rewrote `/downloads` to use GraphQL instead of REST pagination. GraphQL
  requires an auth token even for public repos (REST didn't), so added a `GITHUB_TOKEN` secret.
- Also added a standing guard: a `0`/empty result is never trusted over an existing known-good
  cached count, in case this class of upstream flakiness recurs.
- Real gotcha along the way: setting the new secret via `$token | npx wrangler secret put
  GITHUB_TOKEN` in PowerShell silently prepended a UTF-8 BOM to the value (`Bearer <BOM>gho_...`),
  which GitHub rejected as "Bad credentials" (401) with zero indication a BOM was the cause. Traced
  it live via `wrangler tail` + temporary debug logging (removed before the final deploy). Setting
  it from a clean bash-side file instead fixed it. Documented in `SECRETS_CHECKLIST.md` (which is
  gitignored via a broad `*secret*` pattern — stays local-only by design, not committed).
- Environment note: this shell had no working `wrangler`/`npm`/`npx` at session start (`node.exe`
  existed standalone at `D:\node.exe` with no accompanying npm). Reinstalled Node.js LTS properly
  via `winget` so `lehgo` deploys can run directly from here going forward without this detour.

Verified live post-fix: `curl https://bandroom-usercount.bandroom.workers.dev/downloads` →
`{"count":724}`.

## Shipped

**v1.1.19** — one `ppup` release covering everything above.
**Both Cloudflare workers redeployed** via `lehgo` — `bandroom-usercount` (with the GraphQL/download-
counter fix) and `bandroom-marketplace` (no code changes this session, redeployed per the standard
"deploy both" convention).

## Verification

- `dotnet build BandAudioHook.csproj -c Debug` — 0 warnings, 0 errors, after every C# change, many
  rebuild/relaunch cycles this session while chasing the Team Pot bug.
- Live-verified via Chrome DevTools Protocol against the actual running app for the Team Pot fix
  specifically (not just a build check) — read real `state.hbcuMode`/computed CSS, not just
  screenshots, after screenshots proved genuinely inconclusive/contradictory multiple times in a
  row this session.
- Everything else verified by the owner directly in the running dev build across several
  rebuild/relaunch cycles.

## Open Items For Next Session

- `Other: Pregame Tunnel` still has no real detection — need a screenshot of the actual CFB27
  tunnel-entrance screen/moment to wire up OCR or a manual hotkey pair, same pattern as
  `Other: Pregame Take the Field`.
- The native `TrimmerForm` (WinForms) → embedded web `clipper-trim-panel` reroute discussed early
  this session (owner confirmed "reroute to the web trimmer") was paused for the Team Pot
  investigation and never started. `TrimmerForm` is still invoked from `WebMainForm.cs` at three
  call sites (`AssignTrackForm`'s Trim button, the Clipper island's Trim button, a native file-picker
  flow) — none of them route through the modern embedded trimmer yet.
- An unreproduced "Something went wrong rendering part of the UI" toast was reported once, on the
  embedded waveform trimmer, on a pre-session build — never chased down (generic global-error-guard
  toast with no specific error captured). Worth a DevTools console check next time it happens.
- Consider whether `#hbcu-pot-panel[hidden] { display: none; }`-class bugs (ID selector beating the
  UA `[hidden]` rule) exist on any OTHER custom-`display` element in `style.css` that doesn't
  already have the override — this session found one by accident via live CDP inspection, not by
  systematically checking, so there could be others.
- The `GITHUB_TOKEN` secret now backing the download counter is `gh auth token`'s personal OAuth
  token (broad scopes, tied to the owner's own GitHub login) — works fine, but a dedicated
  fine-grained PAT scoped to just this one repo's read access would be cleaner long-term and
  wouldn't break if the personal token is ever rotated/revoked.
- Node.js/wrangler is now properly installed in this dev environment (was previously a bare,
  broken `node.exe` with no npm) — future `lehgo` deploys should work directly without needing to
  reinstall anything.
