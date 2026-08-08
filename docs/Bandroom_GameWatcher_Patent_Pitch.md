# Bandroom's GameWatcher Engine — What It Is, The Pitch, and the Patent Question

*A plain-English explainer for Bandroom's owner. Written August 2026.*

---

## ⚠️ READ THIS FIRST: Not Legal Advice

**Nothing in this document is legal advice.** I am not a lawyer, and this is not a substitute for one. Patent law is complicated, fact-specific, and mistakes (like publicly disclosing your invention before filing) can permanently kill your ability to patent something. **Before you spend any money or take any legal action based on this document, talk to a real, licensed patent attorney.** The patent section below is restated with this same warning because it's the part most likely to cause real harm if misread as "official."

---

## Part 1: What We Built, Explained Simply

### The one-sentence version

Bandroom watches your screen while you play College Football 25/26, reads the on-screen scoreboard the way a person would, and automatically plays the right band song the instant something happens in the game — no button presses.

### The analogy

Imagine a stadium sound tech whose entire job is to stare at the scoreboard and hit "play" on the right song the second something happens — touchdown, third down, change of possession. Now imagine that tech never blinks, never gets distracted, and reacts in a quarter of a second every single time. That's what the `GameWatcher` component does, except instead of a person, it's a small piece of software.

### How it actually works, term by term

**Screen capture.** A few times per second, the program takes a tiny "screenshot" — not of your whole screen, but of specific small rectangles where the game's on-screen scoreboard (the "down and distance" strip, the quarter indicator, etc.) normally sits. Think of it like a camera zoomed in on just the scoreboard, not the whole football field.

**OCR (Optical Character Recognition).** OCR is technology that reads text out of an image — the same way your phone can scan a receipt and turn the printed numbers into text it can search. Bandroom uses Windows' own built-in OCR tool to read the words and numbers inside those small scoreboard screenshots (e.g., turning a picture of the text "3rd & 7" into the actual text "3rd & 7" that the program can understand).

**Polling.** "Polling" just means "checking again and again on a timer," like refreshing a webpage every few seconds to see if anything changed. Bandroom currently re-checks the scoreboard roughly every quarter of a second (250 milliseconds). That's the "how often does it look" setting.

**Pattern matching.** Once the OCR has turned a screenshot into plain text, the program checks that text against a list of things it's looking for — is there a "1st," "2nd," "3rd," or "4th" in there? Does the word "TOUCHDOWN" or "FUMBLE" show up? This is done with something programmers call a "regex" (short for regular expression) — think of it as a very precise find-and-replace search, but for patterns of words instead of exact ones.

**Color sampling for possession.** Separately from reading text, the program also looks at the color of the background behind the down-and-distance strip. Each team's scoreboard color is usually tinted to match whichever team currently has the ball. Bandroom averages the color of that little box and compares it to each team's known colors to figure out who has possession — again, several times a second.

**Edge-triggered / event-driven.** This just means the program only fires a sound when something *changes* — not on every single check. If it reads "3rd down" ten times in a row because nothing changed on screen yet, it doesn't play the third-down song ten times; it plays it once, the moment the down actually flips from 2nd to 3rd. "Event-driven" is the general programming term for software that reacts to things happening (events) rather than running on a fixed script.

**Cooldowns.** Because OCR occasionally misreads a flickering or partially-covered piece of text (imagine misreading a receipt because it's a little blurry), Bandroom waits a couple of seconds before it will let the *same* event fire again, so a brief misread doesn't double-fire the same song.

**Audio triggering.** Once an event is confirmed, Bandroom hands off to its audio player, which fades in the right song at the right volume, with a very short pre-roll (starts an instant before the cue, like a DJ cueing up a track) and a controlled fade-out later — again, all without a person clicking anything.

### Being honest about where this actually stands today

Bandroom can technically detect about 33 different assignable band-cue events (things like "Offense: Third Down," "Defense: Touchdown Scored," "Offense: Drive Starter," etc.), all of which are wired up and functional in the software.

However, **only 7 of those have actually been confirmed reliable by watching real, live gameplay**:
- Touchdown
- Turnover (interception/fumble/general turnover)
- PAT (extra point) good
- 1st down
- 2nd down
- 3rd down
- 4th down

The rest are wired into the code and *should* work based on how the screen elements are expected to render, but haven't yet been verified against an actual live game session. Two detection regions (a "penalty flag" banner and a big full-screen "TOUCHDOWN/FIELD GOAL/SAFETY" ribbon) are flat-out not turned on yet — the code exists, but nobody has grabbed a live screenshot to tell the program exactly where on screen those banners appear, so they currently do nothing.

**Bottom line honesty check:** this is a real, working, clever piece of software with a solid confirmed core (downs, touchdowns, turnovers, PATs) — but it is not yet a fully-proven system across all 33 events, and calling it "finished" or "flawless" right now would be overselling it.

---

## Part 2: The Whole Pitch

### The problem this solves

Right now, if a school band or stadium wants to play a "touchdown song" or a "3rd-down chant" the instant something happens on the field, someone — usually a band director, a student manager, or an AV volunteer — has to be watching the game closely and hit a button or cue a track manually, every single time. That's a real job that requires full attention for an entire game, and human reaction time plus distraction means cues are sometimes late, missed, or wrong (playing the wrong side's song).

Bandroom's pitch: what if the computer did that job instead, by literally watching the screen the same way a human would, and reacting faster and more consistently?

### Who wants this

- School and college marching bands / pep bands that want automated, in-sync cues during football games (especially for video-game-based simulcasts, watch parties, or esports/game-day events built around College Football 25/26).
- Streamers and content creators who play sports games and want automatic hype-sound reactions without manually hitting a soundboard mid-play.
- Anyone running a game-watch event (bars, dorms, fan events) who wants an automated "crowd energy" soundtrack tied to what's actually happening in the game.

### What's novel — and an honest gut-check on whether it actually is

I did a web search to check whether "watches a game screen and automatically fires sound effects" already exists as a product. Here's what I found, honestly:

- **Voicemod**, a popular streamer soundboard/voice-changer tool, currently only supports *manual* sound triggering (via hotkeys or a Stream Deck). As of my research, it lists automatic sound-triggering as a "coming soon" feature — meaning even a major player in this exact space hasn't shipped OCR/screen-based auto-triggering yet.
- **Streamer.bot** and similar streaming-automation tools do "event-driven" automation, but their events typically come from chat commands, Twitch/YouTube API events, or donation alerts — not from reading pixels off the game screen itself.
- I did not find a mainstream, shipped consumer product that does exactly what Bandroom does: reading a sports video game's on-screen scoreboard via OCR and automatically triggering pre-assigned audio cues in real time.

**I want to be careful here: "I didn't find one in a search" is not the same as "one doesn't exist."** There could be a niche broadcast-industry tool, a stadium AV vendor product, or an obscure hobbyist project doing something similar that just didn't surface in my search. If you want more certainty on this before spending real money on legal fees, a proper prior-art search (explained in Part 3) is the right next step — it's more thorough than a general web search.

**What's genuinely a little different here, as far as I can tell:** using OCR specifically (versus tapping into the game's own data/API, or a screen-capture-based image-matching approach that doesn't read text) to drive an audio-cue system aimed at bands/stadiums rather than streamers, plus the possession-color-sampling trick (reading the *color* behind the down-and-distance text to infer ball possession) — that specific combination is not something I found already on the market.

**What's very much *not* new:** the individual pieces — screen capture, OCR, polling loops, event-driven programming, and playing an audio file when a condition is met — are all extremely common, well-established techniques. Nothing about them individually is novel.

---

## Part 3: Patent Legalities, Explained Honestly

### ⚠️ Reminder: This is not legal advice

Everything below is general education, not a legal opinion about your specific invention. A registered patent attorney or patent agent needs to review your actual code, your actual claims, and the real prior art before you file anything or spend real money.

### What a patent actually protects

A patent protects a specific **invention** — typically a specific process, method, machine, or system, described in enough technical detail that someone skilled in the field could build it from the description. You get, in exchange for that public disclosure, the legal right to stop others from making, using, or selling that specific invention for a limited time (usually 20 years for a utility patent, counted from the filing date of the full application).

### What a patent does NOT protect

- **You cannot patent an abstract idea by itself.** "An app that watches a game and plays music when something happens" is an idea/concept, not a patentable invention on its own — you'd need to patent the *specific technical way* you actually built it.
- **You generally cannot patent an obvious combination of existing, well-known techniques.** If everything about your approach is "take Technique A (screen capture), do Technique B (OCR), and do Technique C (play a sound when a condition is true)" in the ordinary way each is normally used, a patent examiner may reject it as an obvious combination rather than a genuine invention.
- **Software patents face extra scrutiny in the U.S. right now.** Since a 2014 Supreme Court case called *Alice Corp. v. CLS Bank*, the U.S. Patent Office applies a stricter test to software patents specifically: it asks whether the claimed invention is really just an "abstract idea" (like a mental process or a basic organizational concept) dressed up in computer language, or whether it includes something genuinely more — usually a specific technical improvement to how a computer or system works. This is directly relevant here: an examiner could plausibly argue that "read text off a screen and play a sound" is the abstract idea, and everything else is just standard implementation. That's a real risk worth taking seriously, not a hypothetical.

### Provisional vs. full (non-provisional) patent application

| | Provisional Application | Full (Non-Provisional) Application |
|---|---|---|
| **What it is** | A cheaper, faster placeholder filing that establishes an early "priority date" (basically a timestamp proving you got there first) | The real, formal patent application that actually gets examined and can eventually turn into a granted patent |
| **Does it get examined / become a real patent?** | No — it just holds your place in line | Yes — this is what actually goes through the full USPTO review process |
| **Time limit** | You get 12 months to file the full non-provisional application, or the provisional expires and does nothing for you | Takes typically 2–4+ years to be granted, once filed |
| **Rough cost** *(ballpark only — confirm with an attorney)* | Roughly **$2,000–$7,000** in typical attorney/professional fees, plus a small USPTO filing fee (a few hundred dollars) | Roughly **$8,000–$18,000+** in attorney fees to draft and file, and often **$10,000–$25,000+ total** by the time it's fully granted (including responding to the examiner's objections along the way) |

**Say it again: these are rough, general ballpark numbers pulled from current published estimates, not a quote for your specific case.** Costs vary a lot based on complexity, how much back-and-forth the examiner requires, and which attorney/firm you use. Ask any attorney you talk to for their actual estimate for your situation.

### What "prior art" means

"Prior art" means anything that already existed publicly before you filed — other patents, published patent applications, products, articles, even random public demos — that describes something similar to your invention. If a patent examiner (or a competitor challenging your patent later) finds strong enough prior art, it can block your patent from being granted, or invalidate it later even if it was granted.

**A prior-art search is a normal, sensible, low-cost first step** before spending real money on attorney fees — it tells you whether you're likely wasting money before you spend it.

### The honest gut-check: is this realistically patentable?

Being balanced, not just telling you what you want to hear:

**The case for "maybe, with the right claims":** The specific combination here — OCR reading a game's scoreboard text plus separately sampling ribbon *color* to infer ball possession, both feeding an automated audio-cue system aimed at bands/stadiums rather than streamers — is a fairly specific technical pipeline, and I did not find an identical shipped product in my research. A well-drafted patent application that focuses tightly on the *specific technical method* (the particular combination of region-based OCR, color-based possession inference, and the event-gating/cooldown logic that avoids false re-triggers) has a more plausible shot than a broad claim over "watch a screen and play a sound."

**The case for "probably not, or only narrowly":** Every individual building block — screen capture, OCR, polling loops, regex text matching, event-driven triggering, playing an audio file — is a decades-old, extremely well-known technique, used together in the ordinary way each is normally used. Under the post-*Alice* standard described above, a patent examiner has a real, plausible argument that this is "apply well-known computer techniques to a new subject (a football video game's scoreboard)" rather than a genuine technical breakthrough. Software patents built this way are routinely challenged or rejected specifically on abstract-idea grounds.

**My honest read:** this sits in a genuine gray zone. It's not hopeless — the specific combination and the color-based possession trick give a patent attorney something concrete and narrow to potentially work with, which is usually a better sign than "I built an app that does X." But it's also not a slam-dunk, and anyone telling you otherwise without seeing the actual claims and prior art isn't being straight with you. A patent attorney who specializes in software will be able to tell you, after actually reviewing this, whether narrow claims around the specific technical mechanism are worth pursuing.

---

## Part 4: What to Actually Do Next

If you want to pursue this seriously, here's a realistic order of operations:

1. **Do a basic prior-art search yourself first, for free.** [Google Patents](https://patents.google.com/) is a free, publicly searchable database of patents and published patent applications worldwide. Search terms like "screen capture game event audio trigger," "OCR sports scoreboard automated sound," "game state detection audio cue," etc. This costs nothing and might turn up something (or nothing) that changes your thinking before you spend a dollar on attorneys.

2. **Consult a patent attorney — many offer free or low-cost initial consultations.** A lot of patent attorneys and firms offer a free 15–30 minute initial call specifically to hear about your invention and give you a rough read on patentability and cost, before you commit to paying anything. Look for one who specifically handles software patents (not just general IP or trademark work), since the *Alice*/abstract-idea issue described above is a software-specific problem.

3. **If the attorney thinks it's worth pursuing, consider a provisional application as the lower-cost first move.** It's cheaper, buys you 12 months, and locks in a priority date while you decide whether the full application is worth the bigger investment — without committing to the full cost up front.

4. **Have the attorney (not yourself) do a formal, professional prior-art search before drafting anything.** This is more thorough than the free Google Patents search in step 1, and it's the step that actually protects you from spending thousands of dollars on a full application that a competing patent kills later.

5. **Decide on the full (non-provisional) application only after the above.** This is the expensive, multi-year commitment — worth making only once you and your attorney have a real, informed read on the odds.

6. **In parallel, don't publicly disclose too many implementation details before filing anything**, if you're serious about patenting. Public disclosure (blog posts, demos, detailed marketing about *how* it technically works) before filing can, in some cases, count against you as prior art against your own application. Ask your attorney specifically about timing this correctly — the U.S. has a limited grace period, but it's not something to rely on casually.

---

*Document prepared for Bandroom's owner, August 2026, based on a technical read of `GameWatcher.cs`, `WebBridge.cs`, and `AudioPlayer.cs` in the Bandroom source repository, plus general web research on patent costs and existing streaming/audio-automation products (search dates: August 2026).*
