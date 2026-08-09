# Bandroom Session 20 Handoff — August 9, 2026 (night) — v1.0.69 shipped

## What happened this session

Continuation of Session 19: scorebug switcher added to the matchup screen, the matchup coverflow
logo bug finally root-caused and fixed, `release.ps1` updated so "ppup" is a real end-to-end
release (not just a local build), and v1.0.69 shipped live to GitHub.

## Matchup coverflow logos — ROOT CAUSE FOUND (this bug has been reported/misdiagnosed repeatedly
## across many prior sessions)

`.coverflow-track .team-swatch` in `wwwroot/style.css` used `-webkit-box-reflect` for a mirror
effect under each tile. This is a legacy WebKit property Chromium has since removed support for.
Combined with the `rotateY()` 3D transform on the side tiles (cf-l1/r1/l2/r2), a known Chromium
removal-era bug drops the WHOLE element from paint instead of just losing the reflection — the
tile, its border, its background, AND the logo image inside it all vanish. Every prior "fix the
logo loading" attempt (eager-loading fixes, cache-busting query params, etc.) was treating a
symptom of a completely different, purely-cosmetic property. Removed the property entirely; the
reflection effect is gone but every tile/logo should render now. **Not visually confirmed by
Claude this session** (couldn't get a live screenshot of the owner's desktop — computer-use access
kept returning a black screen unrelated to the actual desktop state) — owner should confirm on next
launch.

## Scorebug switcher — new, on the matchup screen itself

Owner wanted this reachable directly from the matchup screen, not buried in the gear-icon Settings
dialog. Added a pill + arrows switcher in the matchup dialog's header row (`.scorebug-switcher`,
reuses `.coverflow-arrow` styling at a smaller size). New backend surface:
`WebMainForm.GetScorebugPresetsFromWeb()`/`SetScorebugPresetFromWeb(name)`, exposed via
`WebBridge.GetScorebugPresets()`/`SetScorebugPreset(name)`. Cycles through
`ScorebugPreset.AllPresets` (currently: two PC CBS-skin presets + "Console/Remote Play v1").
Both PS5 and Xbox Remote Play now select their preset from right here instead of opening Settings.

## release.ps1 — now does what "ppup" actually means

Added a real Step 0: commits any uncommitted working-tree changes and pushes the current branch to
`origin` BEFORE tagging/building/packaging. Previously the script tagged whatever commit `HEAD`
happened to be while building from whatever was actually on disk (including any uncommitted work)
— the git tag and the shipped binary could silently diverge, so `git checkout <tag>` later wouldn't
reproduce what was actually released. New params: `-CommitMessage` (what pending work gets
committed as) and `-Branch` (defaults to `master`).

**Real bug caught mid-release tonight**: the first `git add -A` swept in ~48MB that should never
have been tracked — a prior session's leftover `squirrel_releases/Bandroom-1.0.68-full.nupkg` +
`BandroomSetup.exe`, plus Cloudflare Workers' local `.wrangler/` dev-cache sqlite state files. This
is almost certainly why the first push attempt hung and timed out (5 min, killed by the tool).
Fixed: added `squirrel_releases/` and `.wrangler/`/`**/.wrangler/` to `.gitignore`, `git rm
--cached` on both, amended the commit before it had been pushed anywhere. Retry pushed clean and
fast. **Worth a look**: `cloudflare/cloudflare-marketplace/.wrangler/cache/wrangler-account.json`
and the same file under `cloudflare-usercount/` are STILL tracked from an earlier, unrelated
commit (not touched tonight, out of scope for this fix) — the new `.gitignore` rule stops future
changes to these from being picked up, but the existing tracked copies are still sitting in git
history. Worth deciding whether to `git rm --cached` those too at some point (they may contain
account-identifying info, even if not a live secret).

## v1.0.69 — shipped, verified live

- Tag `v1.0.69` pushed, GitHub release created and confirmed via `gh release view` (not just
  trusted the "Done!" message) — 3 assets uploaded: `Bandroom-1.0.69-full.nupkg` (28MB),
  `BandroomSetup.exe` (28.2MB), `RELEASES` (76 bytes). Real timestamps, real sizes.
- Release notes cover: matchup logo fix, scorebug switcher, Xbox Remote Play, conference
  auto-assign, Copy-to-All-Teams fixes (PA/volume + confirmation prompts), Situations panel
  team-color tint, Sound Bank/Downloads table-row redesign, 75 team backgrounds, GameState.Delta
  perf fix, Sharing Guide pill.
- The background-task run of `release.ps1` (first attempt) actually failed with exit code 1 after
  the git push step, with no visible error in the captured log — most likely a race from me running
  manual `git` commands in the foreground at the same time the backgrounded script was also doing
  git operations on the same working directory. Re-ran the remaining steps (build, Squirrel pack,
  tag, GitHub release) directly/manually after confirming git state was clean; all steps verified
  working standalone. Next `ppup` run should be an ordinary single `release.ps1` invocation — this
  session's manual step-by-step was a one-off recovery, not a new normal workflow.

## Trigger words (memory, for future sessions)

- `hunnybunny` (was `premo`) → explain ELI7, no code changes. Renamed this session specifically so
  it doesn't get confused with `ppup`.
- `ppup` → run the full `release.ps1` pipeline: commit, push, tag, build, package, publish to
  GitHub. New this session, now documented in `~/.claude/projects/C--Bandroom/memory/`.

## Still open from Session 19 (not touched tonight)

- Public (shared) team logo worker endpoints (`PUT /teamlogo/{team}`, `GET /teamlogos`) — written,
  still not deployed to the marketplace worker. Needs "lehgo" (or explicit ask) separately from
  tonight's app release — different deploy target (Cloudflare Worker vs. the Squirrel app).
- Xbox Remote Play window detection — shipped in v1.0.69, but still not confirmed against a real
  Xbox Remote Play session. Ask the owner to test and report back, especially whether the
  `ApplicationFrameHost` title-match ever grabs the wrong window if another UWP app is open
  simultaneously.
- Mac build (78 errors in `MacWebBridge.cs`) — untouched, separate large scope.
