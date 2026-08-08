# Bandroom Handoff — 2026-08-05, Session 6

Source: `D:\Claude\Projects\tools\BandAudioHook` (git, remote `origin` =
https://github.com/kingsupreme89/Bandroom-v1). Ignore the stale build copy at
`D:\Games\CFB Tools\BANDROOM`.

## Shipped this session (v1.0.12 → v1.0.15)

Continued straight from Session 5's handoff. In order:

- **v1.0.12** — team logos wired into tiles/badge (16 SEC teams), fixed
  squashed source art (`scripts/square_crop_logos.ps1`), restyled Set
  Matchup for discoverability.
- **v1.0.13** — Home/Away quick-switch above song assignment; fixed
  `loadMatchup()` never being called from `init()` (matchup silently reset
  on every relaunch); wired up the Updates/changelog button+modal.
- **v1.0.14** — no code changes, just confirms the update pipeline itself
  works (see "Update-pipeline incident" below).
- **v1.0.15** — the big one this session, **GAMETIME flow**:
  - `_homeConfig`/`_awayConfig` (read live by `FireEventForSide` during a
    real game) were loaded once at matchup-confirm time and never
    refreshed. Editing a team's songs *after* setting the matchup — the
    normal workflow — silently had zero effect until the matchup was
    re-set from scratch. Fixed via `RefreshHomeAwayConfigIfNeeded()`,
    called from both save paths (`SaveCurrentTeamProfile`,
    `SaveProfileAsFromWeb`).
  - New workflow: pick matchup → press a large flashing **GAMETIME**
    button (was a small "Set Matchup" pill) → plays a synthesized
    confirmation chime (`PlayGametimeChime`, same NAudio-buffer technique
    as the existing update chime, no audio asset needed) → locks
    home/away routing (`WebMainForm._matchupLocked`) → swaps the backdrop
    for a two-team VS screen (`#backdrop-vs`): each side's own stadium
    photo, big centered logo, name, pulsing team-color underglow, split by
    a center emblem. Structurally modeled on CFB 27's own team-select
    screen (user provided a reference screenshot) but restyled in
    Bandroom's existing glass/pulsing-LED language, not copied.
  - The Home/Away song-editing toggle (from Session 5) still works freely
    after GAMETIME — only *which team is home/away for OCR routing* is
    locked, not which one you're configuring.
  - **Stop Watching is the one unlock signal** — pressing it (going from
    watching/waiting → off) unlocks `_matchupLocked` server-side and
    reverts the VS backdrop client-side. This is intentional per the
    user's spec, not a bug: there's no other "end game" button.
  - Also removed the changelog modal from Session 5 in favor of embedding
    it directly as an always-visible scrollable section in the Adjust
    panel (nobody was clicking the button-triggered version). Capped at 10
    real feature bullets before showing a "see full changelog on GitHub"
    link — that link no longer substitutes for a release's actual notes
    when `release.ps1` runs without a real `-Notes` list (was showing as
    literal note text before). Also deleted a stray duplicate/dead
    changelog CSS block from an earlier draft that was silently
    overriding the real one.

## Update-pipeline incident (important, could recur)

Mid-session the user reported "still pulling v1.0.11" despite v1.0.12-14
being live. Root cause, confirmed by reading the local filesystem directly
(same machine): `%LOCALAPPDATA%\Bandroom\packages\` had a **corrupt/partial
v1.0.13 nupkg** (10.5MB vs the real ~17.5MB) from an interrupted download —
almost certainly a network hiccup over the Chrome Remote Desktop session the
user was on. The local `RELEASES` cache never advanced past v1.0.11 because
the download never completed. Fixed by deleting the stale partial nupkg +
`SquirrelClowdTemp` from the packages folder, which forced a clean
re-download on the next "Up to date" click.

**If this happens again**: check
`%LOCALAPPDATA%\Bandroom\packages\` for a nupkg whose size doesn't match the
real one in `squirrel_releases\` for that version, and check
`%LOCALAPPDATA%\Bandroom\packages\RELEASES` (has a UTF-8 BOM, that's normal/
Squirrel's own output, not a bug) — it should list the version you expect to
be running. Also: **the app on screen was showing v1.0.0 at the start of
Session 5**, several versions behind source. Always ask/check the title bar
version before assuming a shipped fix "isn't working."

## Dead-end audit (user asked "what else is a dead end") — results

Checked every `<button>` in `index.html` against `app.js` wiring (all 22 are
wired, no orphaned buttons) and every `WebBridge` public method against
frontend callers. Two real gaps found, one now partially addressed:

1. **`TriggerEffectsTest`** (`WebMainForm.TriggerEffectsTestFromWeb`) —
   still a complete dead end. Fires whichever cue is assigned to Touchdown
   (or the first available event) as a quick preview. No UI button calls it
   anywhere in the current web rewrite — likely a leftover from a deleted
   WinForms panel. **Not fixed this session** — no natural home for a
   "Test" button was obvious; flag if the user wants it back.
2. **`GetHomeVolume`/`GetAwayVolume`** — never called from `app.js`, so
   those sliders always visually reset to 100% on launch. Deeper issue:
   `AudioPlayer.HomeVolume`/`AwayVolume` (`AudioPlayer.cs:15-16`) are
   **in-memory-only static fields, never persisted to disk at all** — so
   even wiring the `Get` calls wouldn't fix anything real without also
   adding persistence to `ConfigStore` first. **Not fixed this session** —
   same shape as the matchup-persistence fix, but scoped out due to time;
   good next-session pickup if the user cares about it (they didn't ask for
   it directly, just asked "what's dead").

## Also discovered this session (not fixed, just found)

- **`AudioDuckingController.cs` is fully built but never instantiated
  anywhere in the codebase** — confirmed via grep, zero references outside
  its own file. `AudioPlayer.cs` has no ducking logic either. So there is
  currently **no real audio ducking in the shipped app** despite the class
  existing — if the user asks "why doesn't X duck under Y," this is why.
  User was told this directly this session (asked "how does it work now"
  and the honest answer was "it doesn't, it's disconnected").
- **The live user-count ticker was never actually deployed.**
  `UserCountService.cs:17` has `const string Endpoint = ""` (blank) — the
  Cloudflare Worker source exists at `cloudflare-usercount/worker.js`
  (clean, minimal, heartbeat+count via KV) but `wrangler login`/`deploy`
  was never run, so `GetActiveUserCount()` always returns -1 and the ticker
  stays hidden. This came up while scoping the marketplace request (see
  below) — same infra, still not live.

## Next up: community sound-bank marketplace (research done, not built)

User explicitly asked to research this and pause here rather than build.
Findings:

- **The hard problem isn't listing profiles, it's audio.** A saved profile
  (`TriggerEntry` list) just contains local file paths
  (`C:\Users\...\Songs\...`) — sharing the JSON alone is useless to another
  user, it doesn't carry the actual song files.
- Three options, ranked by recommendation:
  1. **Metadata-only marketplace** (which events are assigned, not the
     audio itself) — safe, small, "here's how other Bama fans set theirs
     up" inspiration value, but doesn't solve "give me the actual sounds."
  2. **Public-domain/CC-licensed sound packs only** (generic stadium
     horns/chants, not copyrighted fight songs) — smallest legal exposure,
     matches what a shared "sound bank" should reasonably contain.
  3. **Bundle real audio files with each shared profile** (zip of MP3s +
     manifest) — the only version that actually solves the user's problem,
     but biggest lift (Cloudflare KV free tier has a ~25MB value-size
     limit) and real copyright exposure redistributing fight
     songs/hype tracks between users, even non-commercially.
  - **Recommendation given to the user: start with #1 or #2, not #3.**
- Reuse path: extend the *same* never-deployed Worker+KV setup
  (`cloudflare-usercount/`) with a `/profiles` list + `/publish` endpoint
  alongside the existing `/heartbeat`+`/count` — one `wrangler deploy` run
  would finally activate both the user-count ticker AND the marketplace,
  since neither has shipped yet.
- **Nothing built yet.** This is scoping only, per the user's explicit
  request to pause here. Next session: get the user's decision on which
  option (1/2/3) before writing any Worker or C# code, then actually run
  `wrangler login`/`deploy` for the first time.

## 20 feature suggestions given this session (for reference)

Given as a numbered brainstorm list when asked for "mind blowing" ideas,
grounded in real hooks already in the codebase (OCR detection, home/away
split, reverb engine, Discord community, user-count ticker). Full list is
in the conversation transcript; user reacted specifically to #1 (crowd
noise — asked for the logic, answered above), #2 was expanded into a real
"PA Music tab" spec (ties into finally activating `AudioDuckingController`,
see above), and #4 (rivalry mode) got a concrete graphical direction: swap
the VS-screen's center emblem for a rivalry-specific badge and cross-fade
the underglow colors at the seam when the matchup is a known rivalry pair
— needs a new `RivalryPairs` lookup, not built yet.

## Still queued (carried over from Session 5, still untouched)

- Click sound effects.
- Team logos for the rest of the ~148-team roster (only 16 SEC teams have
  art; `square_crop_logos.ps1` is ready to reuse once more source images
  exist).
- Discord version-reset decision (v1.0.1 vs. keep going forward) — still
  needs the user's explicit call.
- `TriggerEffectsTest` and Home/Away volume persistence (see dead-end audit
  above) — lower priority, user didn't request fixes, just asked what was
  broken.

## Release process reminder

`release.ps1` in the project root: bumps patch version from latest git tag,
`dotnet publish`, Squirrel pack (delta+full), git tag+push, `gh release
create`. Takes `-Notes` as a PowerShell here-string (not inline args). "push
premo" = commit+push source, then run this script. **Always pass real
`-Notes` bullets** — running it without them puts filler link-text where
real notes should be (this bit the changelog panel this session, see
v1.0.12-13 fix above).
