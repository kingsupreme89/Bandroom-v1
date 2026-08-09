# Bandroom Session 19 Handoff — August 9, 2026 (late evening)

## What was requested

A long mixed-bag session: rebuild verification of the previous session's uncommitted theme batch,
Remote Play testing (PS5 + Xbox), team background placement (75 conference-organized images),
public/shared team logos (new feature), Auto-Assign conference-pack option, a 40-level bug audit
cross-check against another agent's report, and — the task this handoff precedes — a real layout
redesign of the Sound Bank / My Downloads / "The Bandroom" hub screens to match a reference image's
structure (sidebar, hero cards, pill filters, table-row track list) using Bandroom's own data and
glass/team-color theme.

## What changed this session

**Build verification**: rebuilt `BandAudioHook.csproj` clean (0 errors/warnings) including all of
the previous session's uncommitted theme/feature batch (team-glow CSS var, font scaling, LIVE
status fix, LOCK IN rename fix, matchup logo eager-load fix, load-by-abbreviation, second-instance
prompt). Verified the second-instance mutex prompt actually blocks correctly by launching two
copies of the SAME dev build (the earlier "it didn't show" observation was because one running
copy was the old pre-installed Squirrel build that predates the mutex code entirely — not a bug).

**Remote Play (console testing)**:
- PS5 Remote Play: confirmed working end-to-end — `GameWatcher.cs`'s `GameProcessNames` already
  includes `"RemotePlay"` (Sony's client), `ScorebugPreset.ConsoleScorebugV1` ("Console/Remote Play
  v1") is selectable via the gear-icon Settings dialog (native WinForms `SettingsForm.cs`, not part
  of the web UI). Crops are fraction-based (`FxX`/`FxY`/`FxW`/`FxH`), so any window size works as
  long as the aspect ratio stays 16:9 — no literal 1080p requirement.
- Xbox Remote Play: **added, uncommitted**. `GameWatcher.cs`'s `FindGameWindow()` now also checks
  for an `ApplicationFrameHost.exe`-hosted window with "Xbox" in its title (`FindXboxAppWindow()`)
  — the Xbox app is UWP/MSIX, so its actual top-level window isn't owned by `Xbox.exe` the way a
  normal Win32 app's window is owned by its own process; `Process.MainWindowHandle` on `Xbox.exe`
  itself is reliably zero. Matched by window title instead. **Caveat, not yet tested live**: if
  any OTHER UWP app hosted by `ApplicationFrameHost` also has "Xbox" in its title while running
  (unlikely but possible), this could grab the wrong window. Needs a real Xbox Remote Play session
  to confirm.

**Team backgrounds — 75 images placed and compressed**: owner dropped 5 conference folders
(ACC/BIG10/BIG12/PAC12/SEC, files named `<Team>_BR_1.png`) into
`UserData\TeamBackgrounds\<CONF>\` — one folder level too deep for `TeamBackdrop.FindImagePath`,
which only looks for `TeamBackgrounds\<TeamName>.ext` flat. Wrote a one-off script: flattened
alpha, resized to 960×536 (matching the existing SEC convention), re-saved as JPEG q82, placed at
`TeamBackgrounds\<Team Name>.jpg` in the repo (which gets merged into `UserData` on next launch via
`ConfigStore`'s existing `MergeFolderIfNeeded`). All 75 filenames matched the roster exactly
(`MSU`→`Mississippi State`, `Mizzou`→`Missouri` mapped manually; everything else matched verbatim).
Also found and fixed a pre-existing dead 1MB `sec.png` sitting loose at the top level (nothing ever
loaded it) — compressed to 101KB, moved into `TeamBackgrounds\_generic\`, which IS wired up as the
fallback pool for the ~130+ teams without a dedicated background.

**Public (shared) team logos — new feature, backend built, NOT DEPLOYED**:
Owner wants a custom logo they save to become the new DEFAULT logo everyone else sees too — but
only for users who haven't already customized that same team's logo themselves (explicit
guardrail). Distinct from the existing private `CustomTeamLogos` cross-device sync (per-account
only, follows just your own devices).
- `cloudflare/cloudflare-marketplace/worker.js`: new `PUT /teamlogo/{team}` (authed, single
  canonical file per team, overwrite-in-place — NOT a voted gallery like `/upload`'s song/image/pa
  types) and `GET /teamlogos` (public, no auth, returns the whole index). **Syntax-checked only,
  not deployed** — needs "lehgo" (or explicit ask) to push live; until then the C# side's calls
  just fail silently (best-effort, by design).
- `PublicTeamLogoSyncService.cs` (new file): `PushAsync` (fire-and-forget on save, requires
  sign-in), `SyncAsync` (startup pull, no sign-in required, skips any team already in the user's
  own `CustomTeamLogos`).
- `WebBridge.SaveCustomTeamLogo` now also fires the public push automatically after a successful
  signed-in save ("automatic on save" per owner's explicit choice, not an opt-in share button).
- `WebMainForm`'s `Load` handler fires `SyncPublicTeamLogosAsync()` on every launch; dispatches
  `bandroom:publiclogosupdated` → `refreshTeamsAfterLogoChange()` in app.js if anything changed.
- New manifest `ConfigStore.PublicTeamLogoSyncManifest` (`public_team_logo_sync.json`) tracks what's
  already been applied so it doesn't re-download/re-write on every launch.

**Auto-Assign — "Load Conference Pack" option added**:
Third choice in the Auto-Assign confirm dialog alongside Overwrite/Guided Assign. Backfills empty
event slots from shared conference-wide files (`Songs\Default\{Conference}\` — files sitting
directly in the conference folder, not any one team's subfolder) automatically, but for events
that already HAVE a song assigned, prompts per-event ("`X` already has a song assigned. Overwrite
it with the conference pack's `Y`?") instead of silently skipping — owner feedback: most users
already have some songs assigned, so a pure backfill-only pass often did nothing visible.
New backend: `ConfigStore.PreviewConferencePackForTeam`/`ApplyConferencePackSelections`,
`WebMainForm`/`WebBridge` wrappers, `runAutoAssignConference()` in app.js. Guided Assign's
candidate library now also includes conference-wide songs (team-specific still wins ties).

**Situations panel card background**: was flat `rgba(16,18,24,0.92)` (looked plain black
regardless of team) — now `color-mix(in srgb, var(--team-primary, #101218) 18%, rgba(16,18,24,0.92))`,
a darkened tint of the active team's primary color, kept subtle enough to stay legible.

**Sharing Guide pill**: added next to the Save pill in the header — opens Help & Guide pre-scrolled
to the existing "Share Profile / Load Profile from Others" explanation (`openProfileShareGuide()`
in app.js) rather than writing new explainer content.

**40-level bug audit cross-check** (against a separate agent's 34-item report, verified every claim
against actual current code rather than trusting the report):
- Confirmed several claims were ALREADY fixed in an earlier session (stale report): dead legacy
  Down-trigger keys (fixed via `LegacyDownEventAlias` in `WebMainForm.cs`), `DuplicateProfile`
  crash, `ui-bot.js` production spam, missing `.gitignore`.
- One claim was flat wrong: "`_possession ?? "home"` misroutes events" — checked the real code,
  it already has an explicit `if (_possession == null) return` guard for side-specific events
  (only side-agnostic `Other:*` events fall back to home, deliberately, with a comment explaining
  why that's safe).
- **Three real bugs found and fixed**:
  1. `GameState.Delta` recomputed `PlayDelta.Calculate()` fresh on every property access — up to 7×
     per scoring play (7 evaluators reading `state.Delta`). Now cached in a backing field
     (`src/Bandroom.Core/GameState.cs`) — safe because `Previous`/`Current` are `init`-only.
  2. `CopyCurrentToAllTeamsFromWeb`'s snapshot only copied `Trigger`/`Event`/`AudioFile` — silently
     dropped `PaAudioFile` and `Volume` on every "Copy to All Teams". Fixed to copy all fields.
  3. Neither "Copy to All Teams" nor "Delete Profile" had any confirmation prompt — one misclick,
     total data loss, no undo. Both now use `confirm()` (same pattern already used for "Reset
     stats") before calling the bridge method.
- Flagged but NOT fixed (real, lower priority, or needs a bigger separate pass): PA-clip audio
  glitch when `interruptPrevious` calls `StopAll()` (kills the PA layer, which then restarts
  mid-clip); no `CanFire()` early-exit gating across all 18 evaluators (pure perf, cheap at 250ms
  tick rate with simple comparisons, not worth the refactor risk in a rushed pass); Mac build's 78
  errors (separate large scope); `AudioDuckingController.cs` fully dead code (needs a product
  decision on when it should trigger, not a blind wire-up).

**Two Sound Bank/My Downloads/Bandroom-hub panel title edits made THEN REVERTED**: initially
misread the ask as "just make these panel titles use the glowing block-header font like every
other panel" — added `id="bandroom-hub-title"`/`id="my-downloads-title"` and a CSS selector
addition for `#bandroom-album-name`. **This was wrong scope and has been fully reverted** (git diff
confirmed clean, rebuilt 0 errors) before this handoff was written. The REAL ask, picked up
immediately after this handoff:

## What's next (not started yet as of this handoff)

Owner wants Sound Bank, My Downloads, and "The Bandroom" hub screens' actual LAYOUT/STRUCTURE
rebuilt to match a reference image (a "Mooshic" music-app screenshot) they pasted twice — NOT its
visual style (flat dark UI, solid orange accents, no blur/glow — that stays out per the standing
`project_bandroom_theme` memory: Bandroom is dark glassmorphism, team-colored glow, `.glass`
panels, pill buttons, always). The reference is for STRUCTURE only:
- A sidebar with sub-categories (reference: `Playlist > Create New / Project / Ambient / Wedding /
  Orchestral / Electronic`)
- A row of hero/category cards near the top (reference: `4 Playlist / 552 Playlist / 21 Playlist...`
  large tiles)
- A horizontal pill filter bar (reference: `All Genre / Sound Effect / Hip Hop / Electronic /
  Ambient`)
- A table-style row list below with real columns (reference: `Title | Genre | Mood | Duration |
  License`, plus per-row share/like/download icons)
- A persistent bottom player bar (reference: album art, track name, transport controls, volume,
  like/download) — Bandroom already has something in this spirit (the Clip Preview bar relocated
  out of its old fixed-position bottom slot per an earlier session's handoff) worth checking before
  building a second one.

Owner explicitly declined to scope this further when asked (dismissed a multi-select clarifying
question), so the implementer should use judgment: apply this structure to Sound Bank first (single
team's songs+backgrounds together, already the most gallery/list-like of the three), then decide
whether My Downloads and The Bandroom hub warrant the exact same structure or a lighter variant,
given they have different real data shapes (My Downloads = flat list of pulled-down items across
all sources; The Bandroom hub = team picker + a popular-songs carousel, not really a single team's
full catalog).

## Build state

`dotnet build BandAudioHook.csproj` — 0 errors, 0 warnings, confirmed clean as of this handoff.
`node -c wwwroot/app.js` clean. `wwwroot/style.css` brace-balanced (784/784). Bandroom.exe launched
successfully post-build (PID 21416), no crash.log written.

## Not deployed / needs action from owner

- Public team logo worker endpoints (`PUT /teamlogo/{team}`, `GET /teamlogos`) — written, not
  deployed. Say "lehgo" to push both workers live.
- Xbox Remote Play detection — uncommitted in the working tree, needs a real Xbox Remote Play
  session to confirm the `ApplicationFrameHost` title-match approach actually finds the right
  window and doesn't false-match another UWP app.
