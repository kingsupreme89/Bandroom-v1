# Bandroom Handoff — August 12, 2026 — Session 63

Continuation of tonight's Session 61/62 work. Same idea as always: what happened, explained plain.

## New Feature: Per-Event "Stadium PA Speaker" Effect

You asked whether the PA track had a through-the-speakers effect, then clarified you wanted it on
just ONE event, not applied globally, and that it should sound like open stadium speakers, not a
radio/megaphone honk.

Bandroom already had a global Reverb setting (Off/Stadium/Night Game/Prime Time) and a separate
Megaphone EQ preset in the Sound Booth, but both apply to EVERY clip that plays -- no way to turn
it on for just one card. Added a new per-event toggle: a speaker icon on each event card's
transport strip. Off by default; when you turn it on for a specific card, that card's clip gets
the Stadium reverb treatment (open-air tail, not the honky Megaphone EQ) regardless of what the
global Reverb setting is. Works for both live in-game firing and Preview.

## Released Tonight: v1.1.2, v1.1.3, v1.1.4

- **v1.1.2**: frozen-frame safety valve, First Down on First Down card, event-label prefix
  cleanup, Big Game sidebar removal (see Session 62's handoff for details).
- **v1.1.3**: marketplace "+ Upload" homepage button.
- **v1.1.4**: the new per-event Stadium PA speaker toggle.

All three are live and public. Existing installs will delta-update automatically.

## Fixed: GitHub Download Counter Under-Reporting

You said the counter should read "500 something" -- it was showing a lower number. Root cause: the
usercount worker's GitHub API call only fetched page 1 of releases (GitHub defaults to 30 per
page), so every download from your older releases (back around v1.0.9-v1.0.48) was silently
dropped from the total. This fix was already sitting written in worker.js from earlier tonight
(not yet deployed) -- deployed it now. Counter reads **520** live, confirmed via a direct check
against the worker's `/downloads` endpoint.

## Heads Up: Accidental Deploy to an Unrelated Project

While deploying the usercount fix, running `wrangler deploy` from inside the
`cloudflare-usercount` folder picked up a DIFFERENT config file (`wrangler.jsonc` at your repo
root, project name "androom") instead of the one in that folder -- this Wrangler version searches
up the directory tree for config and found that one first. Result: your app's `wwwroot` static
files got deployed to **https://androom.bandroom.workers.dev**, a project I don't have context on
(there's an untracked `wrangler.jsonc` at the repo root, name "androom", serving `wwwroot` as
static assets -- looks like the start of something not yet finished). Caught it immediately,
re-ran the deploy with an explicit `--config` pointing at the right file, and the actual
usercount worker is now correctly live.

Nothing secret got exposed (just your own public web UI files, same code already on GitHub), but
that URL is now live and I don't know if that's wanted. **Still waiting on your call**: leave it,
tear it down, or is this a project you're already working on separately?

## Event Trigger Reference: What Each Signal Actually Needs

You asked what each event needs to fire. Every event ultimately depends on one or more OCR
regions/signals from GameWatcher.cs actually reading correctly off your screen. Grouped by what
they key off of:

**Down/distance-driven** (need the "down" region calibrated + readable): Earned First Down (and
Short/on-1st-down variants), 2nd/3rd/4th Down cards (both sides), 3rd Down Conversion, Tackle for
Loss, 1st Down After Punt/After Punt, After Opening Kick (both sides).

**Situation-banner text** (need the "situation" region to catch the right keyword on screen):
Kickoff (Opening/Second-Half/generic), Turnover Forced, Timeout cards, Field Goal/PAT Made,
No Punt Return.

**Full-screen "banner" region text**: Field Goal attempt (made/missed), Touchdown (offense side --
defense-side touchdowns are detected from the SCOREBOARD jumping +6 while not possessing, not the
banner, specifically because a defensive score's banner is unreliable/brief).

**Score digits** ("awayscore"/"homescore" regions): both Touchdown cards, Safety, PAT/2-point/Field
Goal Made, Victory in Hand, Iced Game cards.

**Possession color-sample** (not OCR text, a pixel-color read on the down/distance ribbon): every
card that has to know which side has the ball -- Offense vs Defense routing for basically
everything above.

**Pregame-specific, three separate signals for three separate moments**:
- **"Other: Pregame Tunnel"** (the flag/title-card graphic) -- needs the "teamrunout" region to OCR
  the literal text "COLLEGE FOOTBALL" during that opening flag screen. Calibrated from a single
  screenshot, never live-fire confirmed.
- **"Other: Pregame Take the Field"** (chevron tunnel-walk) -- has THREE independent fallback
  signals so it's hard to miss entirely: the chevron marker itself, OR quarter/down flipping from
  0 to a real value, OR (latest fallback) the very first kickoff of the game. Whichever trips
  first fires it -- so even if the chevron crop misses, this should still fire by kickoff at the
  latest.
- **"Other: Pregame Ready"** -- needs the "pregameready" region to catch "READY" text on the
  team-select screen.

## Your Report: Pregame Flag Event Set But Nothing Fired

This is almost certainly **"Other: Pregame Tunnel"** (the flag/title-card event) -- it's the
newest, least-tested signal (added and calibrated from one screenshot this same session, flagged
in Session 61's handoff as "not yet live-fire tested"). Unlike "Pregame Take the Field," it has NO
fallback signal -- if the "teamrunout" OCR crop misses the text, or the flag screen doesn't stay up
long enough for a 250ms-interval poll to catch it, it just silently never fires, no error.

To actually fix this I need a screenshot from the exact moment it should have fired (or the OCR
debug log from that session, if you kept it open) -- I calibrated the crop from a single reference
image at 2560x1440 full-window capture, so if your resolution/aspect ratio or the flag's on-screen
position differs even slightly, the region can miss it entirely. Send either of those and I can
recalibrate for real instead of guessing.

## What To Test Live

1. **Stadium PA speaker toggle** -- turn it on for one card, confirm it sounds like an open-air
   speaker effect (not radio/honky) and that other cards are unaffected.
2. **Download counter** -- should now read 520+ (grows as new downloads come in).
3. **androom.bandroom.workers.dev** -- decide what to do with this.
4. **Pregame Tunnel** -- needs a real screenshot/log from a missed fire to diagnose further.

That's everything for tonight!
