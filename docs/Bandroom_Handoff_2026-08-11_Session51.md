# Bandroom Handoff — Session 51 (2026-08-11)

Continuation of Session 50, live-fire during a real game the owner was watching/testing. Six
things fixed/added from live reports; v1.1 release was discussed but explicitly held pending
further live testing (owner: "almost, we got a couple events so make sure, im testing in game").
Build clean (0 warnings/errors), 59/59 Core tests passing as of the last edit (58 carried over +
1 new).

## 1. Marketplace/sharing questions answered (no code)

Owner asked whether the marketplace is live and whether "someone uploads for everyone" works.
Confirmed both are real and already deployed: `cloudflare/cloudflare-marketplace/worker.js` has a
real (non-placeholder) KV namespace and is wired into `WebBridge.cs`/`MacWebBridge.cs` at the live
production URL. Profile/team-assignment sharing (`VALID_TYPES` includes `"profile"`) and the
Share Profile / Load Profile from Others flow already exist and were explained to the owner
step-by-step; no code changed.

## 2. Track Info rename — local-only, "School - Title"

Owner wanted Track Info edits to rename the actual audio file, but scoped so a retitled
downloaded/shared song never pushes the rename back to whoever uploaded it (same shape as the
existing `CustomTeamLogos` local-override pattern).

- `WebBridge.SaveTrackMetadata`: after saving the `.meta.json` sidecar, computes `"{School} -
  {Title}"` and calls the new rename helper whenever both fields are set and differ from the
  current filename. Returns the new filename in its JSON response.
- `WebMainForm.RenameAssignedTrackFromWeb` (new): moves the audio file + its `.meta.json` sidecar,
  handles filename collisions with a numeric suffix, and never calls the marketplace worker.
- `WebMainForm.RewriteAudioFileReferences` (new): retargets `AudioFile`/`PaAudioFile`/
  `BigGameAudioFile` on the in-memory active/home/away configs AND sweeps every other saved
  profile file on disk (`ConfigStore.ListProfiles`), so a shared file assigned under multiple
  teams doesn't get orphaned by a rename made while editing just one of them.
- `ConfigStore.SanitizeFileName` made `public` (was `static` internal-only) for reuse here.
- `wwwroot/app.js`: Track Info save toast now reports the new filename when a rename happened.

## 3. Assignment-screen blur — was a stale build, not a CSS bug

Owner reported blur/haze around pills + event cards on the non-gameday Assignment screen was
still there despite Session 50's punch list flagging it. Root cause: the WebView2 host serves
`wwwroot` from `AppContext.BaseDirectory` (the **built output** folder), not the source tree
directly (`WebMainForm.cs:311`) — the CSS fix for this (`style.css:3676`, already written earlier
today) was correct in source but the *running* app instance had been launched before that fix was
last rebuilt, so it was serving stale CSS from its own bin output. Killed the stale process,
rebuilt, confirmed source and output `style.css` now match. No CSS changes were needed.

## 4. Event Activity Log — persists across app restarts

Owner exported the event log and got an empty file. Cause: `EventActivityLog` (`EventActivityLog.cs`)
is an in-memory-only ring buffer — killing/relaunching the app (as done for the build above) wipes
it, and exporting immediately after relaunch legitimately produces nothing since no new events had
fired yet.

- `EventActivityLog`: added a static constructor that reloads `event_log_live.txt` (the existing
  best-effort "rewritten on every Record()" file) back into `Entries` at process start. Restored
  entries carry `EventKey=""`/`Side="n/a"` since the live file only stores the already-formatted
  display string, not the structured fields — fine, since both the UI feed and the exporter only
  ever render `ToDisplayString()`. Best-effort: a missing/corrupt file just starts with an empty
  log, same as before this existed.

## 5. Possession-detection margin raised 15 → 25

Traced two live "wrong side fired" reports (a Tackle for Loss and a Fourth Down) back to
`GameWatcher.SamplePossessionByUnderline`'s brightness-based possession read via the raw
`ocr_debug.log`, not a routing/helper bug:
- First flip: `left=63 right=104` (41-point gap) — comfortably over the old 15-point margin, still
  reported wrong. A margin bump alone doesn't fix this one; flagged as a different problem
  (occasional bad read on an otherwise-clear frame) that would need a different mitigation (e.g. a
  manual possession override, discussed but not built this session — owner deferred).
- Second flip: `left=61 right=79` (18-point gap) — barely over the old margin, genuinely
  borderline. This one motivated the fix.
- `GameWatcher.cs`: `minMargin` in `SamplePossessionByUnderline` raised from 15 to 25 luminance
  points. The existing 2-consecutive-tick confirmation (`ConfirmPossessionFlip`) and cooldown were
  left untouched — owner's explicit steer: "most of these have been correct," so don't risk
  slowing down legitimate flips (turnovers/punts) by over-tuning a mostly-working detector.

## 6. 3rd & Short — both sides now always play together, balanced

Owner rule: on 3rd & short (including "3rd & inches"), Offense and Defense should ALWAYS both
play (this pairing already existed by design — `DefenseThirdDownShortHelper`/`OffenseDownHelper`
fire on the same tick, per each file's own doc comment), rebalanced so Defense is the bigger
moment:
- `DefenseThirdDownShortHelper.cs`: `"Defense: Third Down Short"` volume changed from
  `BigGame ? 100 : 70` to flat **100** every time.
- `OffenseDownHelper.cs`: `"Offense: Third Down Short"` volume changed from `BigGame ? 100 : 70`
  to flat **60** every time (other event keys this same helper returns — `Offense: Second Down
  Short`, `Defense: Second Down`, `Defense: Fourth Down` — keep the original `BigGame ? 100 : 70`
  scaling, only the Third Down Short branch was singled out).
- Noted for the owner: if only one side plays after this, the dual-fire logic itself is confirmed
  working by design — a silent side means that side's "Third Down Short" card has no song
  assigned, not a routing bug.

## 7. "Earned First Down" no longer stacks on a 3rd-down conversion

Owner: converting 3rd down specifically should only play `"Offense: 3rd Down Conversion"`
(`ThirdDownConversionHelper`), not also the generic `"Offense: Earned First Down"` — these were
deliberately firing together per `ThirdDownConversionHelper`'s own Session-earlier doc comment,
but the owner wants only the more specific cue now.

- `FirstDownHelper.cs`: added a guard — `if (state.Previous.Down == 3) return null;` — placed
  after the existing `NewPossession` guard, before the 4th-down-ambiguity buffer. 2nd-down
  conversions are unaffected (still fall through to the base event). A 3rd-down conversion now
  fires ONLY `ThirdDownConversionHelper`'s `"Offense: 3rd Down Conversion"`.
- `EvaluatorTests.cs`: `FirstDownHelper_Fires_OnFreshFirstDown` changed from `Previous.Down: 3` to
  `Previous.Down: 2` (the old case is no longer this helper's job); added
  `FirstDownHelper_DoesNotFire_OnThirdDownConversion` asserting `Previous.Down: 3` now returns
  null. 59/59 passing after the update.

## Also observed, not yet actioned

- Around the possession-margin change, the owner saw a burst of `-- skipped: we haven't figured
  out which team has the ball yet` (3rd Down Conversion, Earned First Down, Third Down Short all
  skipped within ~7 seconds). Possibly a side effect of the stricter 25-point margin taking longer
  to lock onto an initial/fresh possession read after a turnover-adjacent stretch — **not yet
  confirmed** as caused by the margin change vs. a coincidental rough patch of frames. Worth
  watching over more live play; if it's a real regression, consider whether the fix should be
  scoped differently (e.g. only widen the margin near a possible turnover, not universally).
- Manual possession override (a quick "flip possession" control for the rare live miss) was
  discussed as the safer alternative to further automatic-detection tuning but explicitly not
  built yet — owner was mid-testing and didn't confirm go-ahead before moving on to other reports.
- The "fullscreen reflective bandroom split matchup screen" ask was scoped down to: the Away/Home
  team-picker screen (`#matchup-overlay` / `.matchup-columns`) already has per-team background
  photos wired in as of earlier today (`renderMatchupCoverflow`, `--team-bg-image` in
  `style.css:4765-4805`, confirmed via `GetTeamBackgroundUrl` + marketplace-upload fallback) and
  already has a reflection effect on the CoverFlow logos (`.team-swatch-reflection`,
  `.matchup-columns .team-swatch-reflection` stronger variant at `style.css:5077`). Owner confirmed
  this is the right screen and wants the per-team uploaded photo (not a generic stock photo) as
  the split fullscreen background — **investigation paused here, no code written yet**, session
  moved on to live-fire reports before this was implemented.

## Build/test status

- `dotnet build BandAudioHook.csproj` — clean, 0 warnings/errors. App was killed/relaunched
  multiple times mid-session for build-lock conflicts (same `AppContext.BaseDirectory`-served-
  wwwroot pattern as every prior session) — always confirmed with the owner first this session
  before killing, since they were actively testing live, then `taskkill /F` on the exact PID.
- `dotnet test src/Bandroom.Core.Tests` — **59/59 passing** (58 carried over + 1 new test for the
  3rd-down-conversion suppression).
- v1.1 release (`release.ps1`) was discussed and explicitly deferred by the owner pending more
  live-game testing — **not run this session**.

## Real next steps

1. Finish the matchup-screen reflective background (item 6 above) — wire `--team-bg-image` (or a
   dedicated new custom property) into a genuinely fullscreen layer behind `.matchup-columns` with
   a mirror/reflection treatment, reusing `GetTeamBackgroundUrl`'s existing per-team photo +
   marketplace-upload fallback that `renderMatchupCoverflow` already fetches.
2. Watch for a repeat of the "-- skipped: we haven't figured out which team has the ball yet"
   burst under the new 25-point margin; if it recurs, decide whether the margin change needs to be
   more targeted.
3. Decide whether to build the manual possession override (discussed, deferred) — becomes more
   valuable if the margin bump alone doesn't fully resolve wrong-side reports.
4. Once the owner is done live-testing and confirms, run `release.ps1` for v1.1 — nothing from
   Sessions 46–51 has been released yet.
