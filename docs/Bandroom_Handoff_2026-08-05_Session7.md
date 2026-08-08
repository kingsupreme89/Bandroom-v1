# Bandroom Handoff — 2026-08-05, Session 7

Source: `D:\Claude\Projects\tools\BandAudioHook` (git, remote `origin` =
https://github.com/kingsupreme89/Bandroom-v1). Continued directly from
Session 6's handoff, same day.

## Shipped this session (v1.0.16 → v1.0.18)

- **v1.0.16** — Restored the "Clear Assignment" button in `AssignTrackForm`
  (`RequestClear` already existed on the form, was just never wired to a
  visible button — a prior session had removed it "per user request," this
  session's user wanted it back).
- **v1.0.17** — Trigger-logic overhaul, this session's main ask:
  - PAT Made, Opening Kickoff, and Downs now route through the same live
    possession-color read (`_possession`, set from `GameWatcher.PossessionChanged`)
    that Touchdown/Turnover already used — added `["pat_good"]` and
    `["kickoff"]` to `WebMainForm.SideAwareEvents`, and gave `OnDownChanged`
    a new `FireTriggerForSide` path. Before this, those three fired from
    whatever team profile happened to be open in the editor, not the real
    in-game side.
  - **Scrapped the 29 manual-numpad-hotkey events** from `ConfigStore.BuildDefault`
    — user said outright these were "only for me to teach you," not a real
    feature. New default profiles now only get the 5 auto-detectable events
    (Touchdown, PAT, Kickoff, Turnover, Tackle for Loss) + Down 1st–4th + Flag.
  - **New: Tackle for Loss detection.** User sent a live gameplay screenshot
    confirming the down/distance ribbon reads as one string ("3rd & 7"),
    which meant no new OCR region/calibration was needed — just widened the
    existing regex. `GameWatcher.CheckForLossOfYards` fires
    `TackleForLossDetected` when distance goes negative; `WebMainForm.OnTackleForLoss`
    attributes it to whichever side is NOT current possession (the defense
    caused it). Marked "not yet confirmed" — hasn't been seen live yet.
  - Situations list now greys out + badges (`WebBridge.ConfirmedTriggers`,
    a hardcoded allowlist) any event that's wired but not confirmed working
    in a real game. Currently only Touchdown and Turnover are marked
    confirmed; move an entry into that set once the user confirms it live.
- **v1.0.18**:
  - **11 Big Ten team logos added** (Illinois, Indiana, Iowa, Michigan,
    Michigan State, Minnesota, Northwestern, Penn State, Purdue, Ohio State,
    Wisconsin, Rutgers) via a new `scripts/slice_logo_sheet.ps1` — sliced
    from a source sheet in the user's Downloads folder
    (`C:\Users\Fresh\Downloads\big10.png`), NOT transparency-keyed like the
    old SEC set. Team logo CSS changed to full-bleed (`object-fit: cover`,
    100% width/height) instead of a small centered monogram, per explicit
    user direction: crop tight/even past the logo's own bounds (negative
    padding in the crop script) so zero card-background sliver survives at
    any edge once stretched. `.team-swatch` also got a real 3D
    beveled-button look (layered box-shadow + gloss `::before` sheen) to
    visually match the source sheets' "physical button" tile style — still
    square with rounded corners, not circular, per explicit correction.
  - **Likely fix for a real, still-unconfirmed bug**: user showed a
    screenshot of the "Choose a Team" picker with badly non-square,
    squashed-flat tiles. Reproduced the CSS in a real Chromium browser
    (`Claude_Browser` tool) with a synthetic 24-team dataset — tiles came
    out perfectly square (86×86, verified via `getBoundingClientRect`).
    Since the CSS itself is provably correct, the leading theory is
    **WebView2's persistent `WebView2Data` profile folder caching stale
    `style.css`/`app.js` across a Squirrel update** (that folder is
    intentionally NOT wiped on update, to preserve cookies/localStorage —
    see `WebMainForm.InitWebViewAsync`). Added
    `core.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", ...)`
    right after `EnsureCoreWebView2Async` to force every request to skip
    cache. **NOT YET CONFIRMED FIXED** — user hadn't re-tested the picker
    screen against v1.0.18 before this handoff was written. If it's still
    janky after this update, the cache theory is wrong and the real cause
    needs a fresh look (possibly get an actual screenshot of computed
    styles from the live app, not just a re-tested browser mockup).
  - Capitalized the "Up to date" button (`text-transform: uppercase`).
  - Every button now flashes accent-glow + presses down on `:active` (new
    global `button:active` rule in `style.css`) — user asked for buttons to
    "light up when clicked."

## Keyword change (important, applies going forward)

**"push premo" is now "ppup"** — same meaning, same pipeline
(`release.ps1`: dotnet publish → Squirrel pack → git tag+push → GitHub
release). Memory file `feedback_push_premo.md` updated to recognize both,
but treat "ppup" as current. This was an explicit user request mid-session,
not a guess.

## Logo import — in progress, NOT done

User is going sheet-by-sheet through images sitting in
`C:\Users\Fresh\Downloads\` (all added ~12:30 AM same day as this session):
`big10.png` (done, 11/15 tiles used — 2 unidentifiable tiles skipped, 1
Purdue duplicate skipped), `mac.png` (**identified but NOT yet sliced/committed**
— all 12 MAC teams: Akron, Ball State, Bowling Green, Buffalo, Central
Michigan, Eastern Michigan, Kent State, Miami OH, Northern Illinois, Ohio,
Toledo, Western Michigan — user flagged Akron's tile as NOT actually
Akron's real logo, wants a proper one; no image-gen tool available this
session, so that one needs a real source image, not an AI-slice), plus
still-unprocessed: `mw.png` (Mountain West), `american.png` /
`american 2 i think.png` (AAC), `sun belt.png` / `sun belt i think'.png`
(Sun Belt), and two `Create_3D_*_logos_*.jpeg` / `Make_3D_logo_button_sheet_*.jpeg`
files whose contents weren't reviewed yet this session.

**Process the user wants, going forward** (established mid-session,
follow this exactly): show one sheet at a time via the `Read` tool, guess
each team by tile position, wait for the user to confirm/correct, only
THEN slice+crop+commit that sheet. Do not batch-guess multiple sheets
before confirmation. After each confirmed sheet, log any teams that don't
match anyone in the 133-team roster (`TeamColors.cs`) so the user can
gather better source art for those later — don't silently drop them.

**Full missing-teams list** (as of this session, before MAC was placed):
117 teams still had no logo file. After MAC lands, subtract those 12.
Regenerate this diff (`TeamLogos\` file list vs. `TeamColors.cs` team
names) before doing more sheets, since it'll be stale by next session.

**Crop technique, locked in this session** (`scripts/slice_logo_sheet.ps1`):
cut each grid cell, then tight-crop to the bounding box of "logo pixels"
(saturation > 25, OR near-black for ink/outlines — this correctly ignores
the metallic gray card's brightness *gradient*, which a naive single-pixel
background-color-distance check did NOT handle, see script comments for
why), then apply **negative padding (currently -3.5%)** so the crop
intentionally cuts slightly INSIDE the detected logo bounds — guarantees
zero card-background sliver survives once CSS stretches it to fill the
tile. This value was tuned live with the user (tried 0%, -1.5%, -6% too
aggressive/broke letterforms, -3.5% was the last confirmed-good value).
If future sheets have very different tile padding/card styles, this
percentage may need re-tuning per sheet — watch for it.

## Marketplace / user-count ticker — still not deployed

No change from Session 6's scoping. User asked about it again this
session; reiterated the recommendation (start with CC-licensed sound packs
or metadata-only, not bundling copyrighted audio) and explained Cloudflare
Worker vs KV vs R2 in plain terms (Worker = the code/"front desk", KV =
small data, R2 = actual file storage, ~S3-equivalent, free tier 10GB
storage / 1M writes / 10M reads / no egress fees). **Still nothing
deployed** — `wrangler login`/`deploy` has never been run. This is the
single biggest lever for unblocking both the ticker AND the marketplace at
once; keep suggesting it.

## Also carried over, still untouched

- Down/Flag OCR regions are calibrated at `FxX=0.65,FxY=0.85,FxW=0.14,FxH=0.09`
  (confirmed still accurate via the user's live screenshot this session).
  Flag region itself (`FxW=0, FxH=0`) is STILL uncalibrated — it can never
  fire until someone screenshots a real penalty banner and fills in its
  fractional crop box, same as the comment in `GameWatcher.cs` has said for
  multiple sessions now.
- `TriggerEffectsTest`, Home/Away volume persistence, click sound effects,
  rest of the ~148-team roster art, Discord version-reset decision — all
  still exactly as previously handed off, no movement this session.
- Discord changelog (v1.0.0 → v1.0.17) was drafted and given to the user
  as plain text this session — not posted anywhere, just handed over for
  them to paste.

## Release process reminder (keyword updated)

`release.ps1` in the project root: bumps patch version from latest git
tag, `dotnet publish`, Squirrel pack (delta+full), git tag+push, `gh
release create`. Takes `-Notes` as a PowerShell here-string. **"ppup"**
(was "push premo") triggers this. Always pass real `-Notes` bullets.

Run via: `powershell -NoProfile -ExecutionPolicy Bypass -File release.ps1 -Notes @'...'@`
(plain `powershell -File release.ps1` fails with an execution-policy error
in this environment — always use `-ExecutionPolicy Bypass`).
