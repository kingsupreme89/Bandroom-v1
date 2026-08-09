# Bandroom Handoff — Session 13 (2026-08-09)

## Shipped this session (commit `2c56e10`, pushed to `origin/master`)

- **50 FCS schools added to the roster** ([TeamColors.cs](../TeamColors.cs):168, `FcsTeams` array) —
  shipped as first-class teams alongside the ~140 FBS schools, same Team picker/Set
  Matchup/OCR color-matching. Verified zero name collisions with the FBS list (4 initial
  picks — James Madison, Sam Houston, Delaware, Jacksonville State — turned out to already
  be FBS and were swapped out).
- **Fixed a real bug**: [ConfigStore.cs](../ConfigStore.cs):248 `ImportDefaultPackForTeam` —
  a v1.0.53 security fix was blanket-replacing `.`/`/`/`\` in team names with `_`, which
  broke the Default Song Pack folder lookup for any team name containing a period. That team
  silently got 0 songs while everyone else worked fine — this is the root cause of the
  "some users' .54 wasn't working" report. Fixed to only strip actually-invalid filename
  characters, with a path-containment check kept as defense-in-depth against traversal.
- **Default Song Pack download/import dialogs** rewritten as plain numbered steps, bigger
  brand-styled header, larger body text (`wwwroot/index.html`, `wwwroot/style.css`).
- **Help & Guide additions** (`wwwroot/app.js`, `HELP_GUIDE_HTML`): sound-not-playing
  troubleshooting checklist, "how to revert to an older version" walkthrough, TeamBuilder
  "Add School" explanation, and a Tips-tab callout for the new FCS schools.
- New changelog: [docs/v1.0.55_discord_changelog.md](v1.0.55_discord_changelog.md).

All verified: `dotnet build BandAudioHook.csproj -c Release` clean (0 errors/warnings),
`node --check wwwroot/app.js` clean, grep-confirmed no dangling references from an abandoned
in-dialog FCS-picker approach that was tried and then reverted in favor of baking the teams
straight into the roster.

## Known, not yet fixed (confirmed root causes from this session's investigation)

1. **Field Goal misfires as "Earned First Down"** — [PlayDelta.cs:20](../src/Bandroom.Core/PlayDelta.cs):
   `wasFirstDown` doesn't check for a possession change, so a made field goal (4th down →
   opponent's 1st down) incorrectly evaluates true. Fix: add a `!newPossession` guard,
   matching the pattern already used by `wasThirdDownStop`/`wasFourthDownStop` on the
   adjacent lines.
2. **Kickoff tied to "Opening Kickoff"** — [KickoffHelper.cs:13-22](../src/Bandroom.Core/Helpers/KickoffHelper.cs):
   fires "Opening Kickoff" for every Q1 kickoff, not just the game's first one. Needs a real
   "is this the game's first kickoff" flag, not just `Quarter == 1`.
3. **Assign PA button not loading + needs its own volume pill** —
   [app.js:2854](../wwwroot/app.js) `openClipperAssign`. Reuse the existing
   `.situation-volume-popover` pattern (used for regular song volume) for PA.
4. **Clip Preview showing but rest of dashboard not rendering/scrolling** — reported by user,
   not yet root-caused. Likely a CSS flex/overflow issue between `#category-bar`/
   `#situations-panel` and `#clipper-island`, all siblings inside `#center-column`.
5. **Settings menu is a separate native WinForms window**
   ([SettingsForm.cs](../SettingsForm.cs)), not a web modal — user wants it merged into the
   profile popup ([index.html:467](../wwwroot/index.html) `#profile-overlay`) with a
   Settings pill and a close X. This is a bigger lift: converting a native dialog into an
   in-page panel, not just adding a button.

## Also confirmed working today (no action needed)

- **Manual event testing exists**: a hidden owner-only "Test hook" panel
  ([index.html:946](../wwwroot/index.html) `#test-hook-panel`) fires any EventKey straight
  through `WebMainForm.FireEventForSide(bypassCooldown: true)`, bypassing OCR entirely. Not
  currently surfaced/documented for normal use — could be turned into a proper "test all
  situations" checklist UI if wanted.
- **Logo save/share already works end-to-end**: `WebBridge.SaveCustomTeamLogo` writes the
  PNG to disk AND stores it in `UserProfile.CustomTeamLogos`, which syncs via
  `ProfileSyncService.PushAsync` — logos travel with profile sync. (Backgrounds are
  explicitly local-only, NOT synced — separate code path.)
- **Coverflow carousel is real, finished code — but only for team pickers** (onboarding,
  favorite-team, Set Matchup), never built for Marketplace/My Downloads/Sound Bank. The
  actual redesign plan for those three surfaces is `docs/Music_Library_UX_Brief_v2.md`
  ("Hybrid Apple Music × Spotify Glassmorphism Music Library" — tiles/shelves/rows, no
  carousel ever specified there). User's mental model of "coverflow everywhere" doesn't match
  what was ever planned for those three surfaces — worth a quick alignment conversation
  before starting UI work so effort doesn't go to the wrong pattern.

## Next step (per user, 2026-08-09): UI design completion

The user's explicit next priority is finishing the **UI design** — almost certainly the
Marketplace/My Downloads/Sound Bank redesign described in `docs/Music_Library_UX_Brief_v2.md`
(marked in-flight/top-priority as of Session 11, not done). Recommend starting the next
session by:
1. Re-reading `Music_Library_UX_Brief_v2.md` in full to confirm current scope/spec.
2. Confirming with the user whether they still want the Apple Music × Spotify tiles/shelves
   pattern from that brief, or whether "coverflow" is now the desired direction for these
   three surfaces (their Session 13 messages suggested they expected coverflow there, which
   doesn't match the brief — needs one clarifying question before building).
3. Checking `docs/HANDOFF_UI_REDESIGN_2026-08-08.md` (untracked file in the repo root) for
   any additional design-completion notes not yet folded into this handoff.
