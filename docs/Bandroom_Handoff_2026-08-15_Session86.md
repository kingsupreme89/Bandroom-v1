# Bandroom Handoff — August 15, 2026 — Session 86

Same idea as always: what happened, explained plain.

## Fixed: "Newest Uploads" Silently Sorting By Popularity Instead

Owner report: "we need to see the newest always... had to go to the team to find this." The hub's
"🆕 Newest Uploads" hero card and the "Newest" dropdown option both set `_hubSort = "newest"`, but
`renderPopularSongsShelf`'s sort logic only had explicit branches for `"views"`/`"downloads"`/
`"likes"` -- `"newest"` fell through to the *default* branch, which ranks by downloads+likes
combined. So clicking "Newest Uploads" showed the same popularity ranking as the default view,
with no way to actually see what was just uploaded without opening a specific team's page.

Fixed (`wwwroot/app.js`): added an explicit `"newest"` branch that sorts by `uploadedAt`
descending. Also made the section label above the list dynamically read "Newest Uploads"/"Most
Downloaded"/"Most Liked"/"Most Viewed" instead of always saying "Popular Songs", so it's obvious
which ranking is active.

## Fixed: Truncated Uploader Text Cut Off The One Useful Part

Each item's "Uploaded by X · 3h ago" line is capped at 140px with an ellipsis -- but the format put
the uploader name first, so on any reasonably-long name the ellipsis landed *before* the relative
time ever rendered. That's the one piece of info that tells you what's newest inside a team's own
page, and it was the part getting cut. Flipped the order to `"3h ago · X"` (`wwwroot/app.js`) so
the time always shows; full text moved to the row's `title` tooltip.

## Fixed: Hub Scroll Stuck -- Had To Drag The Scrollbar

Owner report: "scroll isnt working in market i have to use bar." Root cause: `#bandroom-popular-
shelf` and `#bandroom-backgrounds-shelf` (the hub's Popular Songs / Top Team Backgrounds lists)
both carry `.bandroom-album-grid` for its row styling -- which also gives them their own
independent `overflow-y: auto` AND `overscroll-behavior: contain`. That class combo is correct for
the team-album page (where it's the *only* scroll region in a flex column) but wrong here, where
these two lists sit nested inside `.bandroom-main`, which is the container actually meant to
scroll. Hovering over the list tried to scroll the tiny inner box first, hit its own boundary, and
`overscroll-behavior: contain` stopped that from ever chaining up to `.bandroom-main` -- so the
mouse wheel did nothing and only dragging the real scrollbar worked.

Fixed (`wwwroot/style.css`): scoped override `.bandroom-main .bandroom-album-grid` resets `flex`,
`overflow`, `overscroll-behavior`, and `min-height` back to normal flow so `.bandroom-main` is the
only scroll container in the hub.

## Added: Full HBCU Abbreviation Coverage In Every Team Search Box

Owner report: typing "FAMU" didn't find Florida A&M anywhere in the Market, and separately asked
for more real-world variants (e.g. Texas Southern also as "TXSU"). Root cause: `TEAM_ABBREVIATIONS`
(`wwwroot/app.js`) -- the lookup every team-search box in the app shares -- had zero HBCU entries,
mirroring the exact same gap Session 85 found and fixed in `scripts/team_registry.json` for song
importing, just in this separate list. The Market's own search box (`filterBandroomTeams`) also
didn't consult that shared lookup at all -- it only substring-matched the full team name.

Fixed:
- Added all 19 HBCU schools (SWAC + MEAC) to `TEAM_ABBREVIATIONS`, with extra real-world variants
  beyond the single code each already had in `team_registry.json` (e.g. Texas Southern now matches
  `TSU`/`TXSU`/`TXSO`, Prairie View A&M matches `PVAMU`/`PV`/`PVU`, Grambling matches
  `GSU`/`GRAM`/`GRAMBLING`).
- Mirrored the same expanded set into `scripts/team_registry.json`'s per-team `abbreviations`
  arrays and `alias_index` (same file the song importer's `IntakeEngine`/`intake_engine.py` both
  read, so no separate Python change needed).
- Fixed `filterBandroomTeams` (`wwwroot/app.js`) to actually use the shared `teamMatchesQuery`
  abbreviation-aware matcher instead of a bare full-name substring check.

## Added: Rename In My Downloads

Owner ask: "let us be able to edit song title in the market if we added them or in my downloads,
whichever is easiest." Marketplace rename for your own uploads already existed (pencil button next
to Report/Delete, owner-token-gated). My Downloads had no equivalent -- picked as the easier path
since it's entirely local, no server auth needed.

Added: `ConfigStore.RenameMarketplaceDownload`/`RenameLocalTrack` (local-only, doesn't touch the
file on disk or the original marketplace listing), a `RenameMyDownload` bridge method on both
`WebBridge.cs` and `MacWebBridge.cs` (same either-manifest lookup pattern as the existing
`RemoveMyDownload`), and a pencil button next to Remove on every My Downloads card
(`wwwroot/app.js`).

## Removed: Edit Colors Button From The Team Grid

Owner request, live, after seeing the "Edit Colors -- Jackson State" popover stuck-open bug from
Session 85's fix screenshotted again: remove the button entirely rather than just re-verify the
fix. Confirmed with the owner this meant hiding the entry point, not deleting the underlying
color-override feature (still used for CFB27 possession-detection color matching) -- so
`openTeamColorEditor`/`closeTeamColorEditor` and the popover markup stay intact, just unreachable.
Removed the pencil-button creation in `renderTeamGrid` (`wwwroot/app.js`) -- confirmed it was the
only place that button got created.

## Clarified: "Import Your Own Song" Is Local-Only

Investigated an owner report: 3 songs uploaded for Bethune-Cookman weren't showing in the Market,
while a Jackson State song from someone else was visible instead. Pulled the live `/list` API
directly (`bandroom-marketplace.bandroom.workers.dev`) to check: Bethune-Cookman genuinely had 0
songs server-side; the visible Jackson State songs were real, uploaded by someone else, and the
owner had simply downloaded one -- not a mistagging bug. Confirmed with the owner that their 3
songs *did* show up in My Downloads, which meant they'd used "Import Your Own Song" (local-only
trim/save pipeline) without ever hitting the separate, explicit "Share to Marketplace" step --
not a bug, just a two-step flow that isn't obvious from the button's own wording.

Fixed (`wwwroot/index.html`, `wwwroot/app.js`): button relabeled "+ Import Your Own Song (just for
you)" with a tooltip explaining the Share step, and the success toast now says "Imported ... to
your own library" with a pointer to hit Share in My Downloads if you want it public.

## Added: Edit Generic Pack

Owner ask: a way to edit the shared "Generic" pack directly in HBCU mode, not just toggle a team
onto using it. Turned out trivial server-side: `"Generic"` is already just a sentinel team name the
entire pot backend (`ConfigStore.GetHbcuPot`/`AddToHbcuPot`/etc.) treats identically to any real
team (see Session 84's comment on this). The gap was purely that `renderHbcuPot` (`wwwroot/app.js`)
hardcoded `state.activeTeam` everywhere instead of taking a team parameter.

Added: `_hbcuPotViewingGeneric` module flag + `hbcuPotViewTeam()` helper, threaded through
`renderHbcuPot` and every row action (trim/remove/settings/add-song) instead of `state.activeTeam`
directly. New "Edit Generic Pack" button in the Team Pot panel header
(`wwwroot/index.html`) toggles the whole panel over to the Generic pot with a "← Back to My Team's
Pot" way out; the "Use Generic Pack" checkbox hides itself while viewing Generic since it's
meaningless there. Switching active teams elsewhere in the app resets the flag back to `false` so
you can't accidentally keep editing Generic after moving on.

## Added: Automatic Pot Fallback (No More Silent Side)

Owner ask: "if im playing a team that doesnt have a pot it should just pull from mine then theirs
like vs logic we already use." Previously a side with no pot/pack of its own (typically the
non-HBCU opponent) just sat silent all game unless someone remembered to flip the explicit "Use
Generic Pack" checkbox for it by hand.

Fixed (`HbcuPlaybackService.cs`): factored the existing own-pot-then-own-pack lookup into a
`PoolForTeam` helper, then `Refill` now walks a 3-tier chain per side -- an explicit "Use Generic
Pack" toggle still goes straight to Generic (owner's forced override stays absolute), but
otherwise an empty side automatically borrows the OTHER side's pot/pack first (your own team's
songs beat silence), and only falls back to the shared Generic pack if both sides are empty.

## Delivered: 30-Item Marketplace UX Audit

Owner ask: audit the Market page from 3 customer-service-style lenses and list 30 ways to make it
more streamlined. Delivered inline (not a file) across three perspectives -- first-time user,
power user/support rep, and data/consistency auditor -- covering things like: no distinction
between official vs. user-uploaded content, unlabeled like/dislike icons, no bulk actions, no
delete confirmation, no report reason/category, free-text school field prone to typos, and sort
ties broken silently. Owner said yes to acting on item 9 from that list (the Import wording fix
above); the other 29 are still just a list, not yet triaged into real work.

## Released: v1.1.10 ("ppup")

Full release run via `release.ps1` (called directly this time, not via a nested `powershell.exe` --
see Note On Tooling below):
- Commit `4efdb41` on `master` (9 files changed -- everything above, plus Session 85's own handoff
  doc which had been left uncommitted) -- pushed to origin.
- Tagged and released as `v1.1.10` (was `v1.1.9`).
- `BandroomSetup.exe` (46.5 MB) + `Bandroom-1.1.10-full.nupkg` (46.3 MB) + `RELEASES` uploaded to
  https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.10 -- live, not a draft.
- Existing installs get the delta update automatically on next launch; new installs run Setup.exe.

## Note On Tooling

First `ppup` attempt this session ran `release.ps1` via a *nested* `powershell.exe -File ...` call
from inside an already-running PowerShell tool session -- the outer layer re-parsed the multi-line
`-Notes` string and mangled the `-Branch` positional binding, which broke immediately after the
commit step (`git rev-list --count "origin/$Branch..$Branch"` got a garbage `$Branch` value). The
commit itself had already succeeded by that point, so nothing was lost, but the push/build/tag/
publish steps didn't run. Fixed by re-invoking with `& .\release.ps1 ...` (direct dot-invocation,
no nested `powershell.exe`) in the same tool session, which completed cleanly end to end. Worth
remembering for next `ppup`: always call `release.ps1` directly, never through a second
`powershell.exe -File` layer.

## Verification

- `node --check wwwroot/app.js` -- clean syntax after every JS change this session.
- `python -c "import json; json.load(open('scripts/team_registry.json'))"` -- valid JSON after the
  HBCU abbreviation additions.
- `dotnet build BandAudioHook.csproj -c Debug` -- 0 warnings, 0 errors after the `ConfigStore.cs`/
  `WebBridge.cs`/`MacWebBridge.cs`/`HbcuPlaybackService.cs` changes.
- `release.ps1`'s own Release-config `dotnet publish` + Squirrel pack succeeded clean as part of the
  actual v1.1.10 ship.
- NOT independently live-tested this session: the hub scroll fix, the Generic Pack editor toggle,
  and the automatic pot-fallback logic were all verified by code/build only -- none were clicked
  through in the running app or exercised in a real HBCU-mode game. Same "log/build/unit-test only"
  caveat prior sessions have flagged for UI-facing changes that need an actual browser/WebView2
  session to confirm.

## Options Discussed, Not Started

- Live-verify this session's three UI-facing fixes (hub scroll, Newest Uploads sort, Generic Pack
  editor) and the pot-fallback logic in an actual running game.
- Triage the remaining 29 items from the 30-item Market UX audit into real work (only item 9 --
  clarifying the Import Your Own Song wording -- was acted on this session).
- Everything still carried over from Session 85's own "not started" list: Mac's HBCU Team Pot
  bridge support (`MacWebBridge.cs` had zero HBCU methods before this session -- still true; this
  session only added `RenameMyDownload` there, no Team Pot/Generic Pack methods), the live-fire
  verification of Session 85's 4 audit fixes (TD-sequence race, foreground debounce, distance-regex
  widening, `Stop()` reset), Session 82's original CollegeFB27.exe live-game report, `ChevronMarkerFx*`
  recalibration, and Session 81's Coffee scorebug overlay work / RAM reader address-locking
  unreliability.
