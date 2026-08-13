# Bandroom Handoff — August 12, 2026 — Session 61

Same idea as always: what happened tonight, explained plain.

## New Feature: Team Profiles on the Marketplace

You asked if profiles could be uploaded to the marketplace. Turned out the existing "Share Profile" button was misleadingly named — it only shares a team's *song assignments* (which song plays on which cue), not the team's actual identity (name, colors, bio, logo). There was no way to publish that at all.

Built it for real:
- **"Publish Team Profile"** and **"Browse Team Profiles"** pills, next to the existing song-assignment sharing buttons.
- Publishing sends your active team's name, primary/secondary colors, a short bio you type in, and your logo URL to the marketplace as a new upload type (`teamprofile`), reusing all the existing upload/list/like/download plumbing on the worker.
- Deployed the worker change live.

## Bug Fix: Downloaded Marketplace Songs Not Showing Up

You reported downloading a song from the marketplace didn't make it appear in the "Marketplace Downloads" section of the song picker when assigning.

Two separate bugs stacked on top of each other:
1. **Stale cache.** The song picker caches its library list in memory and only re-fetches on a team switch — same bug class already fixed once before for song-pack imports, just never applied to marketplace downloads. Fixed: downloading now invalidates that cache so the picker re-fetches fresh.
2. **Missing file extension.** Even after fixing #1, songs saved as `.webm` (a format the download service explicitly accepts and saves) were being silently filtered out by four *other*, narrower "which files count as audio" lists scattered around the codebase that never knew about `.webm`. Added it to all four so nothing gets silently dropped again.

## New Event: "Other: Pregame Tunnel"

You wanted a new trigger for the earliest pregame moment — the EA Sports College Football 27 flag/title card that appears before the team run-out — while leaving the existing chevron-based "Pregame Take the Field" detection untouched.

Built and calibrated it from a real screenshot you provided: a new team-neutral OCR region watching for the literal "COLLEGE FOOTBALL" text on that flag screen, wired through a new evaluator, registered as an assignable card. The chevron marker was never touched — it still does exactly what it always did.

## Fixed: "Other: Pregame Ready" Wasn't Firing At All

Turned out this region had never actually been calibrated — it was still a placeholder with all-zero coordinates, meaning it was silently skipped every single tick regardless of what was on screen. You sent four screenshots across different matchups (Akron/Tennessee both ready states, Alabama/Tennessee, Ball State/Texas A&M) — confirmed the READY prompt always sits in the same screen position for whichever side (home or away) readies up, with only the pill's *color* tinting to match that team (gold for Akron, crimson for Alabama, etc.) — never the position. Calibrated a crop spanning both team-name pill locations so it catches READY on either side, or both.

**Second issue on top of that:** even after calibrating it, you reported it still wasn't firing after hitting Back and re-readying. Root cause: this region was set up to behave like a real in-game event (situation/banner/quarter) that intentionally does NOT reset when its text disappears, specifically so a mid-game pause doesn't replay the same touchdown/etc. cue on unpause. But the READY screen is different — hitting Back and re-readying is a real new "ready" moment, not a pause artifact. Pulled it out of that protective group so it now re-arms every time the READY text clears, meaning it'll fire again every time you go back and re-ready, for as many times as you do it.

## Also Fixed: You Were Testing Against a Stale Build

Multiple times tonight, changes weren't showing up when you relaunched Bandroom — turned out you were running the **Debug** build (`bin\Debug\...`), and several of tonight's rebuilds against that config were silently failing because the *previous* running Debug instance had the DLL file-locked, so `dotnet build` errored out without you necessarily noticing amid other output. Rebuilt clean each time by killing the stale process first. Worth remembering: if a fix "isn't working" after a relaunch, check whether the build that produced the running exe actually succeeded.

**Also explained:** the folder your installed app actually auto-updates from is `C:\Users\Fresh\AppData\Local\Bandroom` (Squirrel-managed, versioned `app-x.y.z` subfolders) — separate from your dev `bin\Debug`/`bin\Release` folders, and it only updates when a real GitHub release goes out via `release.ps1`, not from local rebuilds.

## Released Tonight: v1.1.1

Published live and public: https://github.com/kingsupreme89/Bandroom-v1/releases/tag/v1.1.1

Covers the Team Profiles feature and the marketplace-download cache/extension fixes. The Pregame Tunnel event, the Pregame Ready calibration, and the re-fire fix all happened **after** that release and are only in your local Debug build right now — not yet shipped.

## What To Test Live

1. **Pregame Ready** — go to the team-select screen, ready up, confirm it fires; hit Back, ready up again, confirm it fires *again* this time.
2. **Pregame Tunnel** — watch for the EA Sports flag screen at the very start of a game and confirm it fires (this is calibrated but not yet live-confirmed working).
3. **Publish/Browse Team Profile** — publish a team's identity, then browse and confirm it shows up.
4. **Marketplace song downloads** — download a song, confirm it shows up immediately under Marketplace Downloads without needing a team switch.

## Known Gaps / Not Touched Tonight

- Pregame Tunnel's crop is calibrated from one screenshot, not live-fire tested yet.
- The three pregame-adjacent events (Pregame Ready, Pregame Tunnel, Pregame Take the Field) are still three separate assignable cards for what's really one sequence — worth deciding later whether to collapse any of them.
- Tonight's pregame/marketplace fixes haven't been released yet — still sitting in the local Debug build pending live testing.

That's everything for tonight!
