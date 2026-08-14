# Bandroom Handoff — August 12, 2026 — Session 64

Continuation of tonight's Session 61-63 work. Same idea as always: what happened, explained plain.

## Fixed: Penalty Trigger Never Fired

You sent 3 real screenshots from a live Montana St @ Montana game showing an actual
"ENCROACHMENT - 5 YDS / Against Montana" penalty overlay. Turned out the "penaltyagainst" OCR
crop had been sitting wrong since August 7th -- it was estimated from a different-looking overlay
layout than what CFB27 actually shows. The real overlay is a two-card layout (an Accept/Decline
choice card on the left, the penalty detail card with "Against <Team>" on the right) sitting
noticeably higher on screen than the old guess. Recalibrated the crop to the confirmed position.
The actual routing logic (which side's crowd hears the cue, based on who BENEFITS from the
penalty -- not just raw home/away) was already correct and didn't need to change.

## New Feature: Black-Screen-Timed Pregame Runout

You said the chevron tunnel-walk marker fires too late, and there's no team-neutral timed
entrance consistent across every team to use instead. Your idea: time it off when the screen goes
black after the Ready screen, since that's consistent no matter the matchup.

Built it: GameWatcher now tracks average frame brightness every tick (piggybacked on the existing
frozen-frame pixel sample, no extra scan needed). Once the Ready screen has been seen and the
screen goes black (and no real quarter has been read yet, so a random mid-game black flash can't
hijack it), it arms a wall-clock timer -- tracked independently of the normal tick/evaluator
pipeline so it keeps counting even through a black loading screen that would otherwise get the
frozen-frame detector to suspend everything else. Started at 10 seconds, you tested it live and
confirmed the timing needed to be 13 seconds -- updated and shipped.

This fires "Other: Pregame Take the Field" directly, sharing a guard with the existing chevron/
quarter-down/kickoff fallback signals for that same card, so whichever signal trips first wins --
this is a 4th path to the same event, not a competing one.

## Cleanup: Accidental Cloudflare Deploy

While deploying the download-counter fix, `wrangler deploy` picked up the wrong config file (an
untracked `wrangler.jsonc` at the repo root instead of the usercount worker's own config) and
published your app's static web files to an unrelated project, `androom.bandroom.workers.dev`.
Caught it, redeployed the real usercount worker correctly, then tore down the accidental one and
removed the stray config file per your call.

## Released Tonight: v1.1.2 through v1.1.5

- **v1.1.2**: frozen-frame safety valve, First Down on First Down card, event-label prefix
  cleanup, Big Game sidebar removal.
- **v1.1.3**: marketplace "+ Upload" homepage button.
- **v1.1.4**: per-event Stadium PA speaker toggle.
- **v1.1.5**: penalty crop fix, black-screen-timed pregame runout trigger (13s).

All four are live and public: https://github.com/kingsupreme89/Bandroom-v1/releases

## What To Test Live

1. **Penalty** -- confirm it fires correctly on the next real flag, for both sides.
2. **Black-screen pregame runout** -- you already confirmed 13s is accurate in one live test;
   worth confirming again across a couple more games/matchups to make sure it holds.
3. **Download counter** -- should stay accurate as new downloads come in.

## Known Gaps / Not Touched Tonight

- Penalty recalibration is from one matchup's screenshots (Montana St @ Montana) -- if the overlay
  position varies by penalty type or team, may need widening later, same caveat every other
  freshly-calibrated region in this project carries until proven across more games.
- The "First Down on First Down" card was added in v1.1.2 -- still waiting to hear whether it's
  showing up correctly in the assign screen after a full app restart.

That's everything for tonight!
