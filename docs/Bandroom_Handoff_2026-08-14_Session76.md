# Bandroom Handoff — August 14, 2026 — Session 76

Same idea as always: what happened, explained plain.

## Released v1.1.7

Ran the full `release.ps1` pipeline (commit + push, patch bump, build, Squirrel pack, tag, publish)
to ship everything below in one release. `git`/`gh` weren't on PATH in the PowerShell session used
to invoke the script (same recurring issue as Session 75) -- had to prepend
`C:\Program Files\Git\mingw64\bin`, `C:\Program Files\Git\bin`, and `C:\Program Files\GitHub CLI` to
`$env:PATH` manually again. Still worth fixing in the PowerShell profile if this keeps coming up.

Result: **v1.1.7 is live** at
https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.7. Existing installs auto-update on
next launch; new users get `BandroomSetup.exe`.

## Removed Bundled NBC/ESPN/FOX Scorebug Visuals (owner: "we cant have that")

Owner flagged that the NBC scorebug skin was still selectable on the matchup/GAMETIME screen and
wanted every ripped-broadcast-network visual gone, ASAP, before release. These were real licensed
broadcast graphics (FOX 2021, FOX 2025, ESPN 2020, ESPN 2013, NBC 2024, NBC 2024 Monochrome) bundled
directly into the shipped binary via `BandAudioHook.csproj`'s `<Content Include="Assets\**\*">` glob
-- not just referenced, actually packaged into every install.

- Deleted `Assets/ScoreboardReader/theme-library/themes/*` (all 6 ripped theme HTML folders) and
  `Assets/ScoreboardReader/theme-library/thumbs/*.png` (their preview thumbnails).
- Emptied `Assets/ScoreboardReader/theme-library/library.json` to `{"schema": ..., "themes": []}` so
  `GetScorebugThemeGalleryFromWeb` (WebMainForm.cs) has nothing bundled to surface -- confirmed this
  path fails soft (empty array, not an error) and the `scorebugthumbs` virtual-host mapping is
  already guarded with `Directory.Exists` so the missing `thumbs/` folder doesn't crash startup.
- Also had to manually clear the stale copies already sitting in `bin/Debug/.../Assets/...` from an
  earlier local build -- MSBuild's incremental `Content` copy doesn't delete output files whose
  source was removed, so a dev build could still show the old skins even after the source deletion.
  Not a release-blocker (`release.ps1` wipes `publish_temp` fresh every time), but worth knowing if
  a local debug run still shows old skins after a similar future removal.
- Left `ScorebugPreset.cs`'s `Espn2013` preset definition alone -- it's just numeric OCR crop
  calibration (no ripped visual assets, no branding), already excluded from `AllPresets`/the
  switcher per a prior owner request, not a licensing concern.

## Fixed: Take the Field Timer Wasn't Connected to READY

Owner report: the pregame "Take the Field" entrance song wasn't firing live. Root cause --
`GameWatcher.cs`'s countdown used to arm ONLY off a separate black-screen brightness detector
(`CheckBlackScreenRunoutTrigger`) that had to catch the pregame loading screen transitioning to
black AFTER OCR had already read "READY". If that black-screen transition was ever missed (wrong
threshold, frame sampled mid-fade, etc.) the timer never started at all.

Fix: the countdown now arms the instant OCR reads "READY" for the first time that game, right in the
per-region OCR loop, no longer waiting on a black-screen transition. The old black-screen arm is
still there as a dormant fallback (guarded by `_blackScreenSince == null` so it can't restart a timer
READY already armed) for the rare case READY itself is never read but a black screen still shows up.
Delay stays owner-adjustable 15-45s (default 15s) via the Audio Timing settings panel.

## Fixed: Browse Team Profiles Was a Dead End

Owner report: clicking a team in "Browse Team Profiles" (e.g. Texas) just showed a toast with the
bio/colors and did nothing else -- no way to actually use what was published.

Team colors aren't user-overridable (fixed roster in `TeamColors.cs`), so the fix scopes to what's
actually actionable: `viewMarketplaceTeamProfile` (app.js) now shows the same info in a `confirm()`
prompt asking whether to apply that team's published logo to your own copy of the team. Confirming
fetches the published PNG client-side, base64-encodes it, and hands it to the existing
`SaveCustomTeamLogo` bridge call -- no new C# needed, reused the same save path the crop tool uses.
Verified live: opened the dialog, clicked Texas, confirm prompt appeared with real data, clicked OK,
completed cleanly with no errors.

## New: Icon-Only Logo Crop Variant

Owner wants the small tile spots (main team-select grid, matchup side-grid, events-page side-bar) to
show a text-free icon crop, while the big screens (Set Matchup coverflow/GAMETIME, team-picker
popup) keep showing the full logo (icon + baked-in team-name text) as-is, unchanged.

Investigated whether this could be scripted automatically first -- tested an adaptive "find the gap
row between icon and text banner" cropper on Alabama/Ohio State/Texas/LSU/Michigan. Results were
mixed enough to rule out a blind batch pass: some logos (Texas, Michigan, LSU) are already bare
marks and need no work; some (Alabama) have the name banner's swoosh physically overlapping the icon
with zero real gap row to cut at; some (Ohio State) have the team name woven into the shield shape
itself, not a separate banner at all. No script can reliably tell these apart, so cropping needed a
human eye -- landed on giving the owner a fast in-app tool instead of trying to fully automate it.

Backend (new, additive, nothing required to already exist):
- `ConfigStore.TeamIconsFolder` = `TeamLogos/Icons/` subfolder.
- `TeamLogo.FindIconImagePath` (same sanitize/match rules as `FindImagePath`, different folder).
- `WebBridge.IconUrl`/`GetTeams()`'s new `iconUrl` field, `SaveCustomTeamLogoIcon` bridge method.
  Local-only for now -- does NOT join the `CustomTeamLogos` cross-device/public sync triangle the
  full logo does, since it's a per-device convenience crop, not a shared roster asset.

Frontend:
- `fillTeamSwatch`/`renderTeamGridInto` gained a `preferIcon` flag: true for the main team-grid,
  matchup side-grid, and events side-bar; false (full logo) everywhere else including the Set
  Matchup coverflow and team-picker popup.
- `openLogoCropTool` gained a `mode` param (`"logo"` or `"icon"`) routing the save call to
  `SaveCustomTeamLogo` vs `SaveCustomTeamLogoIcon`, with matching header/button text.
- The existing hidden batch-import tool (`Ctrl+Alt+Shift+L`, folder picker + auto team-name
  matching + sequential crop queue) got an icon-mode twin: **`Ctrl+Alt+Shift+I`**. Owner can point it
  at the existing `TeamLogos` folder to re-crop the whole roster into icon variants one at a time --
  this is the actual answer to "what's the easiest way to crop 130+ images," reusing the tool that
  already existed rather than building something new.

## Changed: Matchup Screen Duplicate Team-Name Text

Owner wanted the redundant on-screen team-name label removed from the Set Matchup screen, while
keeping the name that's already baked into each logo's own artwork untouched. Set
`.matchup-columns .coverflow-name { display: none; }` in `style.css` -- hides the big-coverflow text
label only; left the Events-page side-bar's "Away: TeamName" / "Home: TeamName" labels alone since
those serve a real functional purpose (labeling which button is home/away) on a different screen.

## Build & Run Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, throughout the session.
- Live-verified in the running app (screenshots): matchup screen with Alabama's transparent logo +
  hidden text label; Browse Team Profiles' new confirm-and-apply flow end to end.
- Release build (`release.ps1`'s own `dotnet publish -c Release`) succeeded as part of shipping
  v1.1.7.

## Git

`e60bac3` committed and pushed to `origin/master` (19 files changed, includes the 6 deleted ripped
theme HTML files + 4 deleted thumbnail PNGs). Tagged and released as `v1.1.7`, live on GitHub.

## Options Discussed, Not Started

- Icon-crop is a manual, one-team-at-a-time process via `Ctrl+Alt+Shift+I` -- owner hasn't started
  running it yet as of this handoff. No automated/AI-assisted cropping path exists or is planned;
  established this session that it isn't reliably scriptable.
- Session 75's open items (Mac audio engine, Sparkle auto-update, possession color-sampling gap on
  Mac) weren't touched this session -- still open, see that handoff for detail.
