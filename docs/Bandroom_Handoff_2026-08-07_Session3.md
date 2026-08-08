# Bandroom Handoff — August 7, 2026 (Session 3, ~20:55 MT)

Picks up from Session 2. That doc covered PA Announcer, the 46-event fix, OCR calibration, and
the worldwide download counter. This session covered: shipping a slim v1.0.48 installer, an
in-app default-song-pack downloader, and a deep audit of Cline's concurrent event-firing fixes
that found real bugs Cline's own self-report missed — plus a live correction from the owner on
how possession detection actually works.

## Build status (verified by direct rebuild)
```
Bandroom.Core.dll    → 0 errors, 0 warnings
Bandroom.dll (Win)   → 0 errors, 0 warnings
```

---

## 1. v1.0.48 shipped — installer split from the song pack

**Problem:** the default song pack grew from ~950 files to 2,241 files / 2.8GB this session.
Bundled into the installer via `BandAudioHook.csproj`, that pushed `BandroomSetup.exe` and the
full `.nupkg` past **GitHub Releases' 2GB per-asset hard cap** — `gh release create` failed
outright, no partial/broken release left behind (it auto-cleans on failure).

**Fix:**
- `BandAudioHook.csproj` — `Songs\Default\**` inclusion is now conditional on
  `$(BundleDefaultSongs)` (defaults to `true`, so local dev builds are unaffected). Public
  releases publish with `-p:BundleDefaultSongs=false`.
- New `DefaultSongPackService.cs` — resumable download (HTTP Range) + extract, reporting
  progress via the same `window.dispatchEvent(CustomEvent(...))` pattern the auto-updater
  already used. Lands in `ConfigStore.DownloadedDefaultSongsFolder`
  (`%LocalAppData%\Bandroom\UserData\DefaultSongs`), which Squirrel updates never touch.
- `ConfigStore.DefaultSongsFolder` is now a computed property: prefers a bundled copy if one
  exists (old-style full installs), else the downloaded copy.
- New Cloudflare Worker `cloudflare-defaultsongs` (deployed:
  `https://bandroom-defaultsongs.bandroom.workers.dev`) + R2 bucket `bandroom-default-songs`,
  streams `pack.zip` (2.73GB, uploaded this session) straight from R2 with Range support.
- New UI: opt-in prompt + progress overlay (`songpack-prompt-overlay`/`songpack-progress-overlay`
  in `index.html`, wired in `app.js`'s `initDefaultSongPackPrompt()`). Shows once if no pack is
  present on disk (checked via `WebBridge.HasDefaultSongPack()`), skippable, revisitable from
  Settings.
- **Squirrel packaging bug hit and worked around, not root-caused:** `Squirrel.exe pack --icon
  ...` threw `PlaceHolderNotFoundInAppHostException` on the full 2.8GB payload, on retry-succeeded
  on the small slim payload without `--icon`. Clowd.Squirrel is deprecated/unmaintained (last
  release Nov 2023, successor is Velopack, which has the *same* open unresolved GitHub issue
  #121). **Dropping `--icon` from the pack step is the current workaround** — if a future release
  needs a custom Setup.exe icon again, expect this to resurface.
- Released: `https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.0.48` (~22MB installer).
- Repo was **not a git repo locally before this session** — `git init` + connected to the
  existing `kingsupreme89/Bandroom-v1` remote, tags fetched shallow (`--depth 1`) rather than a
  full history pull (multi-GB, timed out at 2min). First commit made this session includes the
  2,241-file song pack (~1.16GB pushed).

**Not yet done:** worldwide download counter (`cloudflare-usercount` `/downloads`) counts
`BandroomSetup.exe` downloads only (filters out Squirrel's own `RELEASES`/`.nupkg` auto-update
noise) — confirmed correct logic, current real count ~139 (not the raw ~2,449 asset-download
total, which is mostly auto-update traffic).

---

## 2. CONFIRMED ROOT CAUSE: why events stopped firing (2 separate bugs, now fixed)

The owner reported nothing played during a live Auburn @ Georgia Tech test despite real songs
being assigned. Traced end-to-end, cross-checked against Cline's own concurrent fixes and against
`docs/Bandroom_Full_Feature_List.txt` (the actual 41-event spec). Two distinct root causes found:

### 2a. Legacy Down triggers orphaned by `_useEngineForEvents` going permanent-true
Cline's fix (`WebMainForm.cs:78`, `_useEngineForEvents = true` unconditionally in the
constructor) was itself correct for what it targeted — but `OnDownChanged`
(`WebMainForm.cs:962`), the *only* code path that ever fired the legacy `Trigger`-keyed
`"1st Down"/"2nd Down"/"3rd Down"/"4th Down"` entries, opens with `if (_useEngineForEvents)
return;`. Since that flag is now permanently true, those four entries — which is exactly where
the owner's real, already-assigned song files lived — became permanently unreachable. Confirmed
this wasn't hypothetical: `ConfigStore.BuildDefault()` still manufactures these same 4 dead slots
(with a pre-filled default 4th-down file) for every profile, so this shipped to every user, not
just this one profile.

**Fixed:** `WebMainForm.cs` — new `LegacyDownEventAlias` dictionary + fallback lookup in
`FireEventForSide`. When the canonical engine event (`"Offense: Earned First Down"` /
`"Offense: Second Down"` / `"Offense: Third Down"`) has no assigned file, it now falls back to
checking the matching legacy `Trigger` (`down:1st`/`down:2nd`/`down:3rd`) on the same side's
profile — so already-assigned files start working again with zero data migration.
**`"4th Down"` was deliberately NOT aliased** — no clean engine equivalent exists (there's no
`"Offense: Fourth Down"` event in the spec; going for it on 4th is only modeled from the
defense's reactive side, `"Defense: Fourth Down"`, a different meaning). This needs an owner
decision, not a guess — see "Open decisions" below.

### 2b. Four spec'd events that were never implemented at all
Cross-referencing `docs/Bandroom_Full_Feature_List.txt` (41-event spec) against every `EventKey`
actually emitted by `src/Bandroom.Core/Helpers/*.cs` found 4 events with a real UI slot
(`ConfigStore.AllEngineEventKeys`) but **no evaluator ever emitted them** — assignable, would
silently never fire regardless of OCR correctness:
- `Offense: Second Down (Midfield)` — still not implemented (see below, blocked)
- `Defense: Second Down (Loss)` — **FIXED**, added to `DefenseHelper.cs`
- `Defense: Second Down (Midfield)` — still not implemented (see below, blocked)
- `Defense: Fourth Down (Loss)` — **FIXED**, added to `BigEventHelper.cs`

### 2c. Bonus bug found while fixing 2b: Midfield variants are structurally broken
`YardLine` is hardcoded to `0` everywhere (never OCR'd — known gap, tracked since Session 2).
`FirstDownHelper.cs`'s existing `"Offense: Earned First Down (Midfield)"` branch checked
`YardLine <= 50`, which is **always true** since YardLine is always 0 — meaning that branch fired
on literally every first down, and the base `"Offense: Earned First Down"` event was dead code,
unreachable, this whole time. **Fixed:** disabled that branch (commented out, not deleted) so the
base event is reachable again. The 2 still-missing spec'd Midfield events were deliberately *not*
implemented with the same broken pattern — they're listed in `ConfigStore.AllEngineEventKeys`
(assignable in UI) with a comment explaining they're blocked on real YardLine data, not silently
missing.

### 2d. Cline's own audit self-report had a confirmed inaccuracy
Cline's concurrent "What's NOT wired" audit claimed the `flag` and `banner` OCR regions "never
had coordinates calibrated." **False** — `GameWatcher.cs:145-153` (`flag`) and `:198-206`
(`banner`) both have real, non-zero crop coordinates from Session 2's live-screenshot
calibration. Not touched/changed this session, just documented here so this claim doesn't get
propagated as fact. (Cline's *other* 4 fixes — HomeOnlyEventsForNow, null-possession dropping
events, engine gated on matchup confirmation, duplicate 1st-down EventKey between
OffenseDownHelper/FirstDownHelper — were all independently verified correct by reading the actual
code, not just trusting the self-report.)

### 2e. Kickoff: separate, simpler issue, not a code bug
Owner also reported the opening kickoff didn't play. Confirmed: `"Other: Opening Kickoff"` in the
Georgia Tech profile has **no file assigned** — the owner assigned songs to
`"Kickoff on Kick (Receiving)"`/`"(Kicking)"` but not the separate `"Opening Kickoff"` event
(the game's very first kickoff is deliberately its own distinct event in `KickoffHelper.cs`, not
lumped in with mid-game kickoffs). Not a firing-pipeline bug — just an unassigned slot the owner
didn't realize was separate. No code change; needs either the owner assigning that slot, or (not
built) a product decision to make `FireEventForSide` fall back across kickoff variants.

---

## 3. v1.0.49 changelog text was written before the fix existed — corrected

Cline's `WHATS_NEW_CHANGELOG` entry for `v1.0.49` (in `wwwroot/app.js`) claimed "Fixed a big
problem where sounds stopped playing... both home and away teams get their cues now" **before**
the actual fix (section 2 above) existed in the code — verified `OnDownChanged` was still
unchanged at the time that text was written. This is exactly the class of thing
`.claude/skills/deep-audit/SKILL.md` exists to catch: a self-report isn't verification. The
underlying claim is now true as of this session's fixes, so the text itself doesn't need
correcting, but **flag this pattern going forward** — changelog entries should describe what's
actually landed and verified, not what's intended/in-flight.

---

## 4. Possession detection: wrong technique, being corrected (in progress, needs live testing)

**Owner correction, directly contradicting existing code assumptions:** possession is signaled by
a **thin underline beneath the team name** (lit for whoever has the ball, dim otherwise) — not a
team-colored fill box the way `GameWatcher.SamplePossession`/`ScorebugPreset.PossessionFx*`
assumed since it was first built. Since every `Offense:`/`Defense:` routing decision in the whole
engine depends on possession being read correctly, this is a plausible root cause for broader
flakiness beyond just the Downs/Kickoff issues above — not confirmed broken in practice, but the
technique was confirmed wrong in principle.

Owner also identified the calibrating screenshots as a **new scorebug revision** ("Kam's CBSv3"),
distinct from the `KamsCbsScorebug` preset calibrated in Session 2 — kept as a separate preset
per this file's existing "swap presets, don't hand-edit" architecture (`ScorebugPreset.AllPresets`
already supports multiple named presets via a Settings dropdown, built Session 2 for exactly this
kind of scorebug-revision churn).

**Built this session:**
- `ScorebugPreset.cs` — new `AwayUnderlineFx*`/`HomeUnderlineFx*` fields (tight crop under each
  team's name) + new `KamsCbsScorebugV3` preset with estimated coordinates from the owner's live
  screenshots (Auburn=away/left, Georgia Tech=home/right, matching this app's existing
  home=user's-team convention).
- `GameWatcher.cs` — new `SamplePossessionByUnderline()` + `SampleCropBrightness()`: reads
  average luminance under each team's name, calls possession for whichever is brighter, using
  the same segment-luminance technique as the existing timeout-dash sampler
  (`SampleTimeoutSegments`). Requires >15 luminance-point margin to call a side — ambiguous
  frames deliberately do nothing rather than guess, matching this file's existing philosophy.
  `SamplePossessionFromWindow` now branches: uses the new underline method if the active preset
  has it calibrated, else falls back to the old color-match method (so `KamsCbsScorebug`,
  the older preset, is untouched/unaffected).

**Build-verified only (0 errors) — NOT live-tested.** The `KamsCbsScorebugV3` crop coordinates
are pure visual estimates from compressed screenshots (no pixel-level zoom tool was available
this session) — treat as a rough starting point, same caveat as every other region calibrated
this way in prior sessions. **First thing to check live:** does the underline for the possessing
team actually read >15 luminance points brighter than the other side at these coordinates? If
the margin is too small or the crop is landing wrong, tighten/reposition
`AwayUnderlineFx*`/`HomeUnderlineFx*` in `ScorebugPreset.KamsCbsScorebugV3`.

**Also worth investigating, not done this session:** the new v3 underline coordinates
(`Y≈0.975`) are close to but not identical to the *old* `AwayTimeoutFx` coordinates (`Y=0.895`,
calibrated Session 2 for the older scorebug version) — worth confirming these are genuinely two
different scorebug revisions and not the same element mis-identified twice. If v3 timeouts also
moved, `AwayTimeoutFx*` likely needs its own v3-specific recalibration too (not attempted this
session — scope was possession only).

---

## 5. Open decisions for next session (not blind-implementable, need the owner)

1. **"4th Down" legacy alias** — no clean engine equivalent exists for the bare legacy
   `"4th Down"` trigger (which fired regardless of offense/defense). Options: (a) add a real
   `"Offense: Fourth Down"` event to the engine (extends the spec), or (b) alias it to
   `"Defense: Fourth Down"` (semantically inverted — plays for the *other* side), or (c) leave it
   dead and have the owner reassign that one file manually. Not decided.
2. **Kickoff variant fallback** — should `FireEventForSide` fall back across kickoff variants
   (Opening → Kick on Kick) so one assigned song covers all kickoffs? Or is keeping them fully
   separate (current behavior) intentional? Not decided.
3. **Possession underline crop tuning** — needs a live session watching the "now: away/home"
   log lines fire correctly (or not) at the actual moment possession changes, not just a static
   screenshot read.
4. **`AwayTimeoutFx*` may need v3 recalibration** — flagged above, not investigated.
5. **Terminology simplification** (owner's request, not started) — names like
   `Offense: Second Down (Midfield)` are internal-jargon-y and, per this session's findings,
   correlate with things quietly going unimplemented. Agreed direction: keep the technical
   `EventKey` as the internal ID (zero risk to already-saved profiles), add a friendlier
   *display* label in the UI layer only (`app.js`'s situation-card rendering). Not started.

## 6. Unrelated, not investigated this session
- Mac build status unknown this session (Session 2 left it broken on ~78 `MacWebBridge.cs`
  errors against missing `MainWindow` methods) — not touched, no new information.
- Cline's other concurrent work this session (No Punt Return detection, "What's New" popup) —
  spot-checked (JS syntax valid, IDs match between HTML/JS/CSS, evaluator wiring looks correct)
  but not deep-audited to the same 20-level depth as the event-firing investigation above.

## Conventions (unchanged)
- Everything lives in `C:\Bandroom`.
- `C:\Bandroom` is now a real git repo (`git init` done this session) connected to
  `kingsupreme89/Bandroom-v1`. Full history was never fully fetched locally (shallow tag fetch
  only) — a `git log`/`git blame` on old history may need a real `git fetch` first.
- Read `TASK_BOARD.md` before starting anything — the `## Cline → Orchestrator` section has this
  session's granular audit log. `.claude/skills/deep-audit/SKILL.md` is the audit checklist used
  throughout this session; keep using it before trusting any "done"/"fixed" self-report.
