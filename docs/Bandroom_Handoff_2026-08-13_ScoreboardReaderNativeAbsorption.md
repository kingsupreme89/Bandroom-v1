# Bandroom Handoff — August 13, 2026 — Scoreboard Reader Native Absorption

Same idea as always: what happened, explained plain.

## The Big Picture

This session picked up where the earlier "Scoreboard Reader Integration" planning session
(`Bandroom_Handoff_2026-08-13_ScoreboardReaderIntegration.md`, same day, earlier) left off —
that one was blocked pending three owner decisions. All three got answered, plus the scope grew
significantly mid-session: instead of just consuming Coffee's Scorebug Overlay App as an external
tool, we're now merging with it. Coffee's RAM reader exe lives inside BANDroom's own build now,
his scorebug skins get their own dedicated space in the app, and the whole thing is wired into a
single "Lock In → pick teams → pick a bug" flow instead of a separate settings journey.

## What Got Built

**Reader data pipeline (`src\Bandroom.Core`)**
- `ScoreboardReaderState.cs` — DTO mirroring the reader's JSON schema.
- `GameStateNormalizer.cs` — maps reader data into BANDroom's `PlaySnapshot`, using the same
  "commit only on a confirmed value" sticky-cache discipline `GameWatcher`'s OCR already uses, so
  a blank/stale read can never fabricate a fake event.
- `ScoreboardJsonReader.cs` — atomic-safe file polling, mirroring the reader's own tmp-file +
  rename write pattern.
- `RamReaderValidator.cs` — a faithful port of Coffee's real validation logic (read straight out
  of his decompiled `main.js`, not guessed): freshness check (≤20s old), correct-game-process
  match, and per-field format/range checks (scores 0-255, clock `M:SS`, down 1-4, etc.) before any
  RAM-reader value is trusted.

**Native bundling — the actual "merge," not just integration**
- `Assets\ScoreboardReader\CollegeFB27RamReader.exe` (+ its sidecar `ram-live-profile.json`) now
  ships inside BANDroom's own build output. BANDroom launches it directly via `Process.Start`
  using the exact command-line contract confirmed from Coffee's source
  (`--service <seedPath> <statusPath> <ownPid>`, hidden window, no stdio piping — it talks
  entirely through JSON files on disk). Coffee's Electron app is no longer something the user
  needs installed or running at all for the RAM-reader path to work.
- A follow-up (in flight as of this doc) is bundling the actual scorebug skin files (the
  theme-library HTML/CSS themes — FOX 2021, FOX 2025, ESPN 2020, NBC 2024, NBC 2024 Monochrome)
  the same way, so Coffee's Corner's gallery works with nothing external installed either.
  Any external theme-library a user or Coffee has locally still shows as extras on top.

**One brain, layered data sources — `GameWatcher.cs`**
Three tiers, in priority order, each one a graceful fallback of the last:
1. **RAM reader** (opt-in, off by default) — most accurate, reads exact values from the game's
   memory. Off by default on purpose: EA's anti-cheat can flag processes reading its memory, and
   that's a risk to the *user's account*, not something bundling/shipping the exe changes. This
   matters for most users, since it's regular local play (online or offline) where anti-cheat is
   active — remote-play/console-streaming users are the one case where RAM mode is moot rather
   than "safe," since there's no local process to read from at all when the game is just a video
   feed.
2. **Reader's own screen/OCR mode** — used whenever RAM isn't on. More capable than BANDroom's
   own OCR since it also carries team identity, colors, and records.
3. **BANDroom's own native OCR** (`GameWatcher`, unchanged, always there) — the last-resort
   fallback if neither of the above is working.
- **RAM/OCR watchdog**: when RAM mode is on and primary, BANDroom's own OCR keeps quietly running
  underneath (it always does — nothing new is captured) and logs a diagnostic note if RAM and OCR
  meaningfully disagree (deduped so a persistently-wrong attach logs once, not every tick). This
  is purely informational — it never overrides the RAM-primary value or touches trigger/event
  behavior. It's there so a wrong-game-attached or gone-stale RAM reader shows up in the activity
  log instead of silently producing wrong triggers with no trace.
- `PlaySnapshot.YardLine` — previously hardcoded to `0` (dead code, disabled every red-zone/
  field-position evaluator) — is now live whenever a reader (RAM or screen) is connected.
- `EventRouter`/`FireEventForSide`/`AudioPlayer`/every existing evaluator in
  `src\Bandroom.Core\Helpers\*.cs` — untouched. They only ever see one `PlaySnapshot`, however it
  got built.

**Streamlined user flow**
No settings journey, no resolution prompt (BANDroom auto-detects the display). The full user
experience is:
1. Press **Lock In** (the existing `#btn-matchup-confirm` GAMETIME button).
2. Pick teams — unchanged, existing flow.
3. Pick a scorebug skin — one inline choice, only asked the first time; remembered after that.

Everything else — launching the reader, connecting, falling back if it's not working — happens
silently with zero UI from Coffee's side ever shown to the user.

**Coffee's Corner** (renamed mid-session from the original "Reader Hub" working name)
A new left-nav modal, same tier and button styling as "The Bandroom" marketplace
(`btn-coffees-corner`, `#coffees-corner-overlay`), built to be visually indistinguishable from any
other BANDroom screen — same `.glass` panel, same team-color `neon-pulse` glow, same pill/button
tokens, same sports-block header font. It's a passive dashboard only: a scorebug-skin gallery to
browse/showcase Coffee's themes, plus a live connection/game-state status readout. It is
deliberately NOT where a user manages reader settings, calibration, or resolution — none of
Coffee's own multi-tab settings UI (Reading profiles, Reading areas, Theme & placement, Green
screen, Settings, Diagnostics) is exposed anywhere in BANDroom. That whole surface was judged way
too complicated for band directors and was cut entirely in favor of the three-step flow above.

## Verified This Session

- `BandAudioHook.csproj`, `Bandroom.Core.Tests`, `Bandroom.Mac.csproj` all build clean, 0
  warnings/0 errors, across every redirect during the session.
- 102 unit tests passing (up from 88 baseline — 14 new `RamReaderValidatorTests` plus the earlier
  pass's normalizer/JSON-reader tests).
- BANDroom launched live on-screen at the end of the session for a first visual look.

## NOT Verified — Needs a Real Live Test

None of this has touched an actual running CFB27 game yet. Specifically open:
1. Whether the bundled exe actually attaches to and correctly reads a real running `CollegeFB27`
   process when launched via the `--service` contract (the contract itself was read from Coffee's
   real source, not guessed, but the exe is closed-source and was never directly observed running).
2. Cold-start attach timing in practice (Coffee's own code implies up to ~30s to scan).
3. Whether BANDroom's triggers/sounds still fire correctly with the reader in the loop, across a
   real sequence of plays (touchdown, timeout, turnover, etc.).
4. Real-world noise level of the RAM/OCR watchdog log — is it usefully quiet, or noisy from
   ordinary OCR imprecision that isn't actually a problem?
5. The bundled scorebug skins' HTML/CSS actually resolving and rendering correctly from the new
   in-app bundled path (follow-up work, may still be finishing as of this doc).

## Known Open Item — Not Solved This Session

**The "invisible scorebug" CFB27 game mod.** Coffee's RAM-reader path (per his own tester notes:
*"Use it only in offline/modded play"*) appears to assume this mod is already present in the
user's game install. This is a separate manual step in the *game's* own files — nothing BANDroom
or the bundled exe touches or installs. Its name, download link, and install instructions were
not available this session. Next step: get that from Coffee/the owner and add it as a real
first-run setup step inside Coffee's Corner (or wherever onboarding happens), rather than leaving
it as an undocumented prerequisite.

## Other Things Explained This Session (No Code Change)

- **Remote play / console streaming** — screen-only mode already works fine for this, since it's
  just window capture regardless of whether the window shows a native game or a Remote
  Play/console-streaming app. RAM mode simply doesn't apply there (no local process to read), which
  is a non-issue rather than a risk for that specific case. Window-selection logic reuses whatever
  BANDroom's own `GameWatcher` already uses to pick a capture window — no new prompt was added.
- **Scorebug skins vs. event triggers** — these are and always will be independent. The skin is
  purely visual (what the on-screen overlay looks like); triggers are driven by the normalized
  game *data*, which is identical no matter which skin is active. Switching skins never affects
  what sounds fire.
- **RAM + OCR "both at once"** — clarified this isn't a blend of two data streams merged together;
  it's a priority chain (RAM, then reader-screen, then BANDroom-OCR) with the newly-added watchdog
  as a silent diagnostic layer underneath whichever tier is currently primary.

## What To Test Live, In Order

1. Launch BANDroom, Lock In, pick teams, pick a scorebug skin — confirm the flow feels like the
   three steps described above with nothing extra popping up.
2. Open Coffee's Corner — confirm the skin gallery renders (bundled themes should show even with
   nothing external installed) and the status panel shows a real connection state.
3. Start a real CFB27 session with RAM mode left OFF (default) — confirm nothing regressed versus
   BANDroom's existing OCR-only behavior from before this session.
4. If/when the invisible-scorebug mod info is available, install it, opt into RAM mode, and repeat
   with RAM mode ON — check Coffee's Corner status goes CONNECTED, compare live values (score,
   down/distance, possession, yard line) against the actual game, and watch the activity log for
   any RAM/OCR watchdog mismatches during a few real plays.

That's everything for tonight.
