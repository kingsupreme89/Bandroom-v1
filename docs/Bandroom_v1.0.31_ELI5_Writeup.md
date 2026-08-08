# What We Built Since v1.0.30 — Plain-English Edition

This explains, in normal words (no jargon left unexplained), everything that got added to Bandroom
since the last version, why it exists, and how it actually works under the hood.

## The big picture

Bandroom does two jobs:
1. **Watches your game and plays hype sounds** when something happens (touchdown, turnover, etc.)
2. **The Bandroom marketplace** — a shared library where people upload songs and background
   images for their team, and everyone else can grab them

This update mostly built out job #2 (the marketplace) into something people can actually use
together — upload, browse, delete your own stuff, report bad stuff, like your favorites — and
fixed two real bugs in job #1.

## "OCR" — what it means and how Bandroom uses it

**OCR = Optical Character Recognition.** It's software that looks at a picture and reads the text
in it, the same way you'd read a street sign in a photo. Bandroom doesn't know what's happening in
your football game by hooking into the game itself — it can't, that's not allowed and not
possible for most games. Instead, it takes a tiny screenshot of a small strip of your screen
(the scoreboard area) many times per second, and OCR reads whatever text is sitting there right
now — the down and distance ("1st & 10"), the score, whose ball it is.

Think of it like this: Bandroom isn't "inside" the game, it's a person sitting next to you
squinting at the score bug and going "oh, it says 1st down now, let's play a sound." That's
literally what's happening, just automated and much faster than a human could do it.

### Why the "pause/unpause" bug happened, in plain terms

When you pause the game, the pause menu covers the scoreboard, so OCR reads nothing there —
blank. Bandroom's old logic said "if the text goes blank, forget what it used to say, so next
time it shows up it counts as new." That's usually right — it lets the same event (like another
touchdown later) trigger the sound again after the scoreboard clears between plays.

But a pause is different: when you unpause, the *exact same* text reappears ("Touchdown" from
right before you paused). Since Bandroom had "forgotten" it, it saw that text as brand new and
played the touchdown sound again — even though nothing new actually happened.

**The fix**: instead of forgetting on "blank," Bandroom now only forgets on "the down number
actually changed" (like going from 1st down to 2nd down). A pause never changes the down number,
so it can't cause a repeat anymore. A real new score still works fine, because there's always a
real down change (a kickoff, a new drive) somewhere between any two real scoring plays.

### A second, smaller bug found while fixing that one

If your game window is very small (like minimized), the math for "how big a strip of screen do I
screenshot" could round down to zero pixels wide or tall. Trying to take a screenshot of literally
nothing crashes that specific check, over and over, many times a second. Fixed by saying "never
take a screenshot smaller than 1 pixel," which stops the crash without changing anything about
how it behaves at normal window sizes.

## The marketplace — what got built and what it means

### The Bandroom hub
A landing page, like a store's front window. It shows a horizontally-scrolling strip of the
newest things anyone uploaded for any team — click one, jump straight into that team's page. If
nothing's uploaded yet anywhere, it just says so instead of showing a bunch of fake empty boxes.

### Sound Bank and Trophy Room
Two "rooms" per team: Sound Bank is uploaded songs, Trophy Room is uploaded background images.
Both now show real uploaded content in a grid, with one "+ Upload" tile at the end.

### Compression — why files get shrunk before uploading
"Compression" means making a file smaller without ruining it, similar to how a JPEG photo isn't
as big as the raw camera file but still looks basically the same. We compress:
- **Images**: resized so the longest side is at most 1600 pixels, saved as a JPEG. Keeps the
  marketplace from filling up with giant multi-megabyte photos that all look the same size on
  screen anyway.
- **Songs**: re-encoded to a smaller audio format (Opus, inside a WebM container — just names for
  audio file types) at a quality level that still sounds good for a short hype clip, while taking
  up much less space than the original recording.

### The volume-normalizing "limiter" (TrimmerForm / "the clipper")
Different songs are recorded at wildly different volumes — one clip might be whisper quiet, the
next one blasts your speakers. **Normalization** measures how loud a clip *actually sounds*
(not just its loudest peak — those aren't the same thing) and turns the whole clip up or down so
every saved clip lands at roughly the same loudness. Then a **limiter** gently squashes down any
moment that would still end up too loud, so you never get an ear-splitting spike even if the song
has one unusually loud second in it. This runs automatically now whenever you trim and save a
song in the local Trimmer tool.

### My Uploads, Delete, Report, Likes — what "no accounts" means and why it matters
Bandroom's marketplace has **no login system** — anyone can upload without making an account.
That's simple for users but creates a real question: if you upload the wrong file, how do *you*
delete it, without a login to prove it's yours?

**The answer**: when you upload something, the server hands your browser a secret one-time code
(an "owner token") — like a claim ticket. Your browser remembers it. Only someone holding that
exact code can delete that specific upload. Nobody else — including us — can see or guess it from
browsing the marketplace normally. This was checked specifically to make sure the code never
leaks out anywhere a stranger could grab it.

**Report** lets anyone flag something inappropriate — right now it just adds to a counter the
team behind Bandroom can check later; there's no automatic removal yet (see "what's left" below).

**Likes** let people mark favorites; a "Top Contributing Teams" leaderboard on the hub shows
which schools have uploaded the most.

### Rate limiting
A simple safety valve: if the same internet connection tries to upload more than 10 things in 10
minutes, the server temporarily says no. Stops one person (or a bug, or a bot) from flooding the
marketplace.

### Set as Team Background
Click an image in Trophy Room, and it downloads that image and sets it as your own local
background for that team — same idea as the wallpaper on your computer, but for Bandroom's team
theming.

## What's still genuinely unverified — and why that's different from "broken"

Two pieces of this work were written carefully and pass every automated check we have (the code
compiles, there are no syntax errors), but have never actually been run against a real game or a
real audio file, because the tools available couldn't do that:
1. **The volume normalizer** — needs someone to actually trim a real song and listen to it.
2. **The pause/unpause fix** — needs someone to actually pause and unpause mid-game and confirm
   the sound doesn't repeat.

"Passes every check" is not the same as "confirmed working" — think of it like a recipe that's
correctly written out but nobody's actually cooked and tasted it yet. That's the single most
important thing for a human to do next.

## How we plan to tackle what's left, knowing what we now know

1. **Live-test the two unverified pieces first** (audio normalization, pause/unpause) — these are
   already-written code with real risk, not blank-slate work, so they go before anything new.
2. **Calibration-only items next** (flag/penalty detection, full-screen scoring banner, kickoff
   confirmation, tackle-for-loss, more scorebug skins) — these can't be built further by an agent
   at all; they need a screenshot from an actual live game with that specific thing happening on
   screen, then the OCR region gets tuned to match. Bring screenshots when these come up.
3. **Everything else on the roadmap list** (waveform previews, trending section, admin/moderation
   view, etc.) is normal build work — no live testing blocker, can be picked up in priority order
   whenever there's time, same as this session did with the marketplace features.
4. **Deploys are a two-step reminder**: the local code and the *live* Cloudflare worker are two
   separate things — a worker code change only takes effect after someone runs `wrangler deploy`.
   This session did that deploy directly; future sessions should keep verifying the live URL
   actually reflects what's in the code, not just assume it does.
