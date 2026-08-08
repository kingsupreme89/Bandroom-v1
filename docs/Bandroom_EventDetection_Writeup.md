# Bandroom event-state detection — how it works today, and the gap

## The pipeline, end to end

Every 400ms, while the game window is focused, four things happen in sequence:

```
screen capture (crop box)
        |
        v
   OCR (Windows.Media.Ocr)
        |
        v
  regex match against the OCR'd text
        |
        v
  NormalizeMatch() -- collapses OCR noise into a stable key
        |
        v
  edge-trigger + cooldown check
        |
        v
  RegionChanged("region name", "value") event fires
        |
        v
  WebMainForm builds a trigger key ("region:value")
        |
        v
  look up that trigger key in the user's config
        |
        v
  play the assigned audio file (if one is assigned)
```

All of this lives in two files:
- [`GameWatcher.cs`](tools/BandAudioHook/GameWatcher.cs) — capture, OCR, normalize, edge-trigger, cooldown.
- [`WebMainForm.cs`](tools/BandAudioHook/WebMainForm.cs) (`OnRegionChanged`, `OnDownChanged`) — turns a region+value into a trigger key and plays the sound.
- [`ConfigStore.cs`](tools/BandAudioHook/ConfigStore.cs) (`BuildDefault`) — the master list of all 33 assignable events, and which ones are wired to an auto-detected trigger vs. a manual hotkey.

## Step 1: Regions — what patch of screen gets read

`GameWatcher._regions` is a fixed list of "watched regions." Each one is a crop box (as *fractions* of the game window, so it survives window resizing) plus a regex of what to look for inside it:

| Region | Crop box | Looking for | Status |
|---|---|---|---|
| `down` | bottom-right scorebug (65%,85%,14%,9%) | `1st / 2nd / 3rd / 4th` | calibrated, live-tested |
| `situation` | **same box as `down`** | `KICKOFF`, `PAT GOOD`, `TOUCHDOWN`, `INTERCEPTED`, `FUMBLE`, `TURNOVER` | calibrated, live-tested |
| `banner` | full-screen scoring ribbon | `TOUCHDOWN`, `FIELD GOAL`, `SAFETY` | **not calibrated** (0x0 box, skipped entirely) |
| `flag` | penalty banner | `FLAG`, `PENALTY` | **not calibrated** (0x0 box, skipped entirely) |

`down` and `situation` share one crop box on purpose — the scorebug's rightmost segment is a single spot that cycles between showing the down count and showing situational banner text (different background color per state, same physical location). That was confirmed from 4 live gameplay screenshots this project, not guessed.

Only regions with a non-zero box get scanned each tick (`region.Calibrated`). `flag` and `banner` are wired up in code but effectively dormant until someone fills in real coordinates.

## Step 2: OCR + regex

Each tick, the region's crop box is screenshotted, run through Windows' built-in OCR engine, and the raw text is regex-matched. If nothing matches, `currentValue` is `null` — that's "no active state," not "state cleared." When the box goes blank (banner disappears), the code deliberately resets `region.Last = null` so the *same* text can re-trigger later (e.g. a second kickoff after halftime should still fire, even though it's the same word as the first one).

## Step 3: Normalizing the OCR text into a stable key

OCR is noisy — spacing varies, garbage characters slip in occasionally. `NormalizeMatch()` collapses whitespace, lowercases, and then folds a few *different* words into the *same* trigger key where the game treats them as one concept:

```csharp
"intercepted" or "fumble" or "turnover" => "turnover",
"field goal" => "fieldgoal",
```

So `situation:turnover` fires whether the OCR actually read "INTERCEPTED", "FUMBLE", or "TURNOVER" — they're functionally the same trigger.

## Step 4: Edge-triggering + cooldown

A region only fires when its value *changes* to something new (`currentValue != region.Last`) — this is what makes it event-like instead of level-like; the "kickoff" sound doesn't loop for as long as the word KICKOFF is on screen. On top of that there's a 2-second cooldown per region (`GameWatcher.Cooldown`) so a flickery OCR read (text vanishes for one frame then reappears) doesn't double-fire the same event.

## Step 5: Region+value → trigger key → sound

`WebMainForm.OnRegionChanged` decides how to build the trigger key:

```csharp
static readonly HashSet<string> ValueKeyedRegions = new(...) { "situation", "banner" };
string triggerKey = ValueKeyedRegions.Contains(region) ? $"{region}:{value}" : $"{region}:on";
```

- For `situation`/`banner`, the *value itself* is part of the key (`situation:kickoff`, `situation:touchdown`) — because these regions can be one of several different things.
- For `flag`, there's only one possible state, so the key is always `flag:on` — it doesn't matter what text matched, only that the region fired at all.

That key is then looked up against the user's `TriggerEntry` list (loaded from their config JSON), and if an audio file is assigned to that trigger, it plays.

## Step 6: Which of the 33 events get wired to what

`ConfigStore.BuildDefault()` holds the full official list of 33 assignable events (Offense/Defense/Other, exactly as the game's own naming). By default, every one of them gets a manual hotkey (`Numpad0`-`Numpad9` across 4 modifier "banks" — Ctrl/Shift/Alt/none — for up to 40 slots).

Four of them are overridden to use an OCR trigger key instead of a hotkey:

```csharp
["Offense: Touchdown Scored"] = "situation:touchdown",
["Offense: PAT Made"]         = "situation:pat_good",
["Other: Opening Kickoff"]    = "situation:kickoff",
["Defense: Turnover Forced"]  = "situation:turnover",
```

Everything else (2nd/3rd/4th down variants, field goals, safeties, drive starters, victory-in-hand, etc.) still needs a manual key press — either because there's no on-screen text pattern for it yet, or because it hasn't been prioritized.

---

## The actual gap: no offense/defense (possession) signal

You're right that there's no "is this happening to *my* team or the *opponent*" identifier anywhere in this pipeline. Here's exactly where that bites:

**The scorebug text is possession-blind.** `TOUCHDOWN` looks identical in OCR whether your offense just scored or the opponent's offense just scored on you (e.g. a defensive/pick-six TD *against* your team). Same for `TURNOVER`/`INTERCEPTED`/`FUMBLE` — the banner text can't tell you whether *your* defense forced it or *your* offense just gave the ball away.

Concretely, right now:
- `situation:touchdown` is hardcoded to fire **`Offense: Touchdown Scored`** — so if the opponent scores against you, this pipeline still fires your "we scored" sound. There is no `Defense: Touchdown Scored` (pick-six) path at all, even though it's in the 33-event list.
- `situation:turnover` is hardcoded to fire **`Defense: Turnover Forced`** — so if *your* offense fumbles or throws a pick, this pipeline still fires your "we forced a turnover" sound. Wrong direction.

This isn't a bug in the code that's there — the code does exactly what it can with the information it has. The information just isn't there yet.

## How to actually fix it

The real fix is adding a **possession/side signal** to the pipeline — one more OCR'd (or non-OCR) region that says "who currently has the ball" or "who just scored," which the situation logic can then cross-reference before deciding Offense vs. Defense.

Two realistic ways to get that signal, roughly in order of effort:

1. **Score-delta comparison (no new calibration needed for the trigger logic itself, but does need two new regions).**
   Add `home_score` and `away_score` OCR regions (both already visible in the scorebug, just not read yet). Every tick, diff each side's score against its last known value. When one side's score jumps, you know definitively *which* side just scored, and by how much (6 = TD, 3 = FG, 2 = safety-against-them or 2pt, 1 = PAT). Combined with knowing which side is "you" (asked once at setup, alongside the existing favorite-team picker from onboarding), this turns `situation:touchdown` into either `Offense: Touchdown Scored` or `Defense: Touchdown Scored` correctly, every time, and gets field goal / safety / 2pt / PAT almost for free as a side effect — those are currently NOT auto-detected at all.

2. **A possession indicator (harder, but solves the turnover case, which score-delta can't).**
   Most CFB scorebugs show a small arrow, dot, or highlighted team name next to whichever team currently has the ball. If that's OCR-able or even just template-matchable (a fixed-position icon, not text), watching that region tells you who had the ball *before* a turnover happened, so `situation:turnover` can correctly resolve to `Defense: Turnover Forced` (you had the ball taken from... wait, opponent had it and lost it to you) vs. an offensive giveaway. This needs one more live screenshot at the moment of a turnover to see if such an indicator exists on this scorebug style at all — not guaranteed, but worth checking next time you're near a turnover in a real game.

Score-delta is the higher-value, lower-effort one to do first — it fixes the TD case (the worse of the two false-positive risks, since it's the loudest/most-celebratory sound) and adds FG/safety/PAT/2pt detection as a bonus, using regions you'd need to calibrate anyway. The possession-indicator approach only matters for turnovers and is a "nice to have" on top.

### What "you" means to the app
None of this works without the app knowing which side is yours. The onboarding wizard already asks for a favorite team on first run (`ConfigStore.IsFirstRun`/`CompleteFirstRun`) — that answer just isn't currently *used* for anything beyond cosmetics. Wiring it into the score-delta logic (compare "did my team's score change or the opponent's") is the missing link, not a new ask of the user.
