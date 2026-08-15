# Bandroom Handoff — August 14, 2026 — Session 79

Same idea as always: what happened, explained plain.

## Fixed: RAM-Staleness Fallback Was Too Slow

Owner report, live, during a game: "its triggering all when the game starts but starts to not
trigger once 1st down comes. no 2nd down song etc." Traced to Session 78's brand-new RAM-staleness
fallback (`GameWatcher.cs`) -- confirmed via `ocr_debug.log` that the fallback WAS firing correctly
(RAM stuck on down=1, OCR settled on down=2, code was falling back to OCR every tick from
10:17:25 on), so the bug wasn't broken logic, it was the thresholds: `RamFieldStaleThreshold` (12s)
+ `OcrFieldCorroborationWindow` (3s) meant up to ~15s of lag before a stuck down/distance/possession
value corrected itself -- long enough to miss a song on a fast-paced drive. Owner confirmed after
the fix: "most events are triggering properly though" -- this was specifically about lag on some
downs, not a full outage.

Fix: tightened both constants -- `RamFieldStaleThreshold` 12s -> 5s, `OcrFieldCorroborationWindow`
3s -> 1.5s. Worst-case correction lag drops from ~15s to ~6.5s. Both checks (RAM-frozen AND
OCR-settled) stay in place unchanged -- a single noisy OCR misread still can't force a false
switch, it just resolves faster now that RAM is genuinely stuck. Rebuilt and relaunched live
mid-session; not yet separately reconfirmed by the owner during a full game with the tighter
timing (the change was made and shipped between two live windows).

## New: "Use Generic Profile" Option at GAMETIME

Owner request: "im playing as montana wehich i set but the other team i didnt. we need to be able
to allow my opponent to use the generic profile." Clarified with the owner that per-event
auto-fallback to the Generic profile already existed silently (`ResolveEntryForEvent`,
`WebMainForm.cs`) -- what was actually wanted was an explicit, visible control at the GAMETIME
moment rather than relying on implicit fallback.

- Reused the existing Session 11 "no songs assigned yet" GAMETIME prompt
  (`showDefaultProfilePrompt` in `app.js`, `#default-profile-prompt-overlay` in `index.html`) --
  it already interrupts GAMETIME when a team (almost always the opponent, since the owner sets up
  their own team) has nothing assigned, offering "Use Starter Profile" or "I'll Assign Songs
  Myself." Added a third button, **"Use Generic Profile & Continue"**, between those two.
- New `WebMainForm.ApplyGenericProfileForTeamFromWeb` (Windows) fills only the team's empty event
  slots from `ConfigStore.GetGenericProfile()` -- the same shared fallback pack
  `ResolveEntryForEvent` already reaches for per-event -- never overwrites anything the team
  already has. Mirrored on Mac (`MainWindow.axaml.cs`) for parity since `app.js`/`index.html` are
  shared between platforms. Wired through both `WebBridge.cs` and `MacWebBridge.cs`
  (`ApplyGenericProfileForTeam`).
- `showDefaultProfilePrompt`'s resolved value changed from a bool to a string (`"starter"` /
  `"generic"` / `false`) so `confirmMatchup` can branch on which pack to apply and report the
  right toast/label ("Filled N songs from the Generic profile" vs "...Default Song Pack").

## Release

Shipped as **v1.1.8** via `release.ps1` ("ppup"). Both fixes above are live:
https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.8. `git`/`gh` weren't on the
PowerShell tool's PATH by default this session (only Bash's) -- had to prepend
`C:\Program Files\Git\cmd` and `...\mingw64\bin` to `$env:Path` before `release.ps1` would run;
worth checking if that's a one-off shell-init quirk or worth fixing at the profile level if it
recurs.

## Build & Test Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors, both times this
  session (once for the threshold tightening, once implicitly via `release.ps1`'s Release build).
- No new automated tests added -- both changes were either a constant tweak (RAM staleness
  thresholds) or a thin UI/bridge passthrough (Generic Profile button) mirroring existing,
  already-tested patterns (`ApplyDefaultProfileForTeamFromWeb`).
- Not separately re-confirmed live: the tightened RAM-staleness timing (owner reported the
  original 15s-lag symptom, fix shipped, not yet watched through a full game since); the new
  Generic Profile button (implemented and built, not yet clicked live by the owner).

## Git

Committed and pushed as part of the v1.1.8 release (commit `ff5bb8d`, "Tighten RAM-staleness
fallback timing; add Generic Profile option for opponent teams at GAMETIME"). Also picked up and
committed in the same release: three previously-uncommitted handoff docs (Sessions 76-78) and
`scripts/push_devbuild.ps1` that were sitting untracked from earlier sessions.

## Options Discussed, Not Started

- Session 78's open items (untouched again this session): `]`/`[` hotkey collision risk (no
  modifier requested yet), Mac audio engine, Sparkle auto-update, icon-crop batch pass -- see
  that handoff for detail.
- Whether the `git`/`gh`-not-on-PATH issue in the PowerShell tool is a recurring environment quirk
  worth a permanent fix, or was a one-off for this session.
