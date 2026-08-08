# Bandroom Handoff — 2026-08-04, Session 5

Source lives at `D:\Claude\Projects\tools\BandAudioHook` (git repo, remote
`origin` = https://github.com/kingsupreme89/Bandroom-v1). The build output at
`D:\Games\CFB Tools\BANDROOM` is a stale side copy — ignore it.

## What happened this session

Picked up after Session 4's handoff. Two things were flagged as open items;
both got resolved:

1. **Uncommitted work (894 lines) — committed and pushed.** Also discovered
   `bin/`, `obj/`, and a 231MB release zip were tracked in git despite a
   `.gitignore` that excluded them (added after they were already tracked).
   Untracked all of it — took a few passes since new build artifacts kept
   sneaking back in via `git add -A`. If you see `bin/`/`obj/` show up as
   "deleted" in `git status` again, it means something re-tracked them;
   `git rm -r --cached bin obj` fixes it.

2. **Versioning conflict — never resurfaced.** Kept shipping forward from
   v1.0.11 (this session went v1.0.11 → v1.0.12 → v1.0.13). If you still want
   the Discord-announcement reset to v1.0.1, that's a separate decision —
   raise it explicitly next session.

## Shipped this session (v1.0.12, v1.0.13)

**v1.0.12 — Team logos + matchup discoverability**
- Team tiles, the team picker, and the header badge now show real logos
  (`TeamLogos\<name>.png`, served via a new `teamlogo` virtual host in
  `WebMainForm.cs`/`WebBridge.cs`/`TeamLogo.cs`). Only the 16 SEC teams have
  art right now; everyone else still falls back to the initials monogram —
  this is expected, not a bug.
- The 16 source logo PNGs were sliced at inconsistent non-square dimensions
  (104–316px wide, all 177px tall), so `object-fit: contain` rendered them
  squashed/tiny. Wrote `scripts/square_crop_logos.ps1` to crop to actual
  non-transparent content and re-center on a square canvas — **run this
  again** if more team logos get added with the same slicing problem.
- Restyled the "Set Matchup" button (was a barely-visible muted pill next to
  the much more eye-catching header team-badge) and clarified both controls'
  tooltips, since the two were getting confused for each other.

**v1.0.13 — Home/Away song assignment + changelog panel**
- Added a Home/Away toggle bar above the song-assignment list once a matchup
  is set (`#matchup-side-bar` in `index.html`/`app.js`). Each team already
  had its own independent trigger→song profile (that's how home/away audio
  differs — Alabama's Touchdown cue vs Arkansas's Touchdown cue are separate
  assignments), but there was no visible link between "matchup" and "which
  profile am I currently editing," so it looked like only one slot existed.
  This bar shows both team names and jumps straight to editing either one.
- Fixed `loadMatchup()` existing but never being called from `init()` — the
  matchup was saved server-side (`GetGameTeams`/`SetGameTeams`) but silently
  reset to "Set Matchup" in the UI on every relaunch.
- Wired up the "Updates" button (Adjust panel, next to Help). The backend
  (`ChangelogService.cs`, `WebBridge.GetChangelog()`) already pulled real
  GitHub Releases notes but had zero frontend calling it — no button, no
  panel existed at all until now.

## Known state / things to watch

- **The app on the user's screen was showing v1.0.0 at the start of this
  session** — a much older installed build than what's in source (which was
  already at v1.0.11 from prior sessions). Squirrel's auto-updater applies
  one release at a time on the "Up to date" button click / launch check, so
  it took two manual "Up to date" clicks across the session to catch up.
  **If the user reports something "isn't working" that this handoff says is
  fixed, first ask what version the title bar shows** — it's very likely
  just behind on updates, not a regression.
- `computer-use` access to the Bandroom window was denied automatically this
  session (likely because the screen is being driven via Chrome Remote
  Desktop by the user, not the local console) — verification of UI changes
  had to rely on the user's own screenshots + code review, not live
  interaction. Don't assume computer-use will work on this app; ask the user
  to screenshot instead.
- Most of the ~148-team roster still has no logo art (only 16 SEC teams).
  Team logos was explicitly called out as wanted work — natural next step is
  sourcing/cropping art for the rest of SEC + other conferences, using the
  same `square_crop_logos.ps1` pipeline.

## Still queued (carried over, untouched this session)

- **Click sound effects** (asked for in an earlier session, not started).
- **Team logos for the rest of the roster** (~130 teams still on initials).
- **Discord version-reset decision** (v1.0.1 vs. keep going forward) — needs
  the user's explicit call, don't assume either way.

## Release process reminder

`release.ps1` in the project root does the whole pipeline: bumps patch
version from the latest git tag, `dotnet publish`, Squirrel pack (delta +
full), git tag + push, `gh release create` with the packed assets. Takes a
`-Notes` param (heredoc/here-string, not inline args — quoting native `gh`
args broke this before). "push premo" in user shorthand means: commit +
push source, then run this script.
