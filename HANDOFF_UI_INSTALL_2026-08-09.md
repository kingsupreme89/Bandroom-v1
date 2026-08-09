# Bandroom UI Redesign — Install/Continuation Handoff — August 9, 2026

## Status vs. the Aug 8 handoff

The Aug 8 doc (`HANDOFF_UI_REDESIGN_2026-08-08.md`) listed ~75 remaining items. Re-checking
against the current code, almost all of the "Next Priority Order" list turned out to already be
implemented (by the time this session started):

| Item | Status |
|------|--------|
| macOS traffic lights + glass depth | ✅ Done (`.window-controls`/`.wc-btn` in style.css:315, layered blur on `#header-bar`/`.side-panel`) |
| Search debounce | ✅ Done (`setupSearchDebounce()`, app.js:5900) |
| Lazy loading for logos | ✅ Done this session — marketplace thumbnails already had `loading="lazy"`; team-picker swatches (`fillTeamSwatch()`, app.js:373-381) were the one gap, now fixed |
| Command palette (Ctrl+K) | ✅ Done (`openCommandPalette()`, app.js:5356; keybinding app.js:5385) |
| Kill-feed / HUD overlay | ✅ Done (`#hud-overlay`, style.css:424) |
| Streamer mode | ✅ Done |
| Skeleton loading screens | ✅ Done |
| Resume last session | ✅ Done |
| Accessibility (focus-visible, aria-labels) | ✅ Done |
| macOS vibrancy material layering | ❌ Not done — cosmetic, low priority |
| Sheet-style dialogs sliding from header | ❌ Not done — cosmetic, low priority |

Everything else on the original 75-item list (Dynasty features, Tips system, Profile dashboard
expansion, etc.) is still genuinely not started — that list was long and only the top of the
priority order got worked.

## What changed this session

1. **`wwwroot/app.js`** — `fillTeamSwatch()` (app.js:377): logo `<img>` now has
   `loading="lazy" decoding="async"`. Shared by every team grid/picker in the app, so this was a
   one-line fix covering all of them.
2. **`ConfigStore.cs`** — new `MigrateLegacyDownEvents()` (called from `EnsureAllEvents()`,
   ConfigStore.cs:1063), fixing a real bug: pre-engine profiles stored down-cue songs under bare
   `"1st/2nd/3rd/4th Down"` slots (Trigger `down:1st`..`down:4th`). The current UI only shows the
   new canonical `"Offense: Nth Down"` slots, so those old slots were invisible in the UI but still
   silently fired at runtime via a fallback in `WebMainForm.cs:537-543` — a song could appear
   "Unassigned" on screen and still play. This migration promotes any leftover legacy audio into
   the canonical slot (only if that slot is empty, so nothing gets clobbered) and clears the legacy
   slot so it can't fire invisibly again.
   - **Caught in self-audit before shipping:** the first version of this migration iterated
     `entries` with `foreach` while calling `entries.Add()` inside the loop when no canonical slot
     existed yet. Confirmed against the real `Tennessee.json` on disk that this exact branch would
     run (it has zero canonical `Offense: Nth Down` rows), which would have thrown
     `InvalidOperationException` and crashed profile load. Fixed by iterating a `.ToList()`
     snapshot instead. Rebuilt clean afterward.

## How to rebuild & test

```
cd /d c:\Bandroom
dotnet build BandAudioHook.csproj
# Then launch Bandroom.exe from bin\Debug\net10.0-windows10.0.19041.0\
```

The app reads `wwwroot\` files fresh on every launch — no deployment needed for JS/CSS/HTML
changes. C# changes (`ConfigStore.cs`, `WebMainForm.cs`, `GameWatcher.cs`, etc.) need a rebuild.

**To verify the down-song migration on first launch after this update:** open Tennessee (or any
team with pre-existing down-cue songs) in the Adjust/Events panel — the songs that used to show as
"Unassigned" under "Offense: Second/Third/Fourth Down" should now show the actual assigned file.

## What's actually left (real, not stale)

1. macOS vibrancy material layering — cosmetic polish, skipped this session
2. Sheet-style dialogs sliding down from header — cosmetic polish, skipped this session
3. Everything under Dynasty Features, Tips System, and Profile Dashboard expansion in the Aug 8
   doc — untouched, still a large body of work
4. The Situations panel's "3rd Down" tiles are ambiguous in the UI — Offense and Defense versions
   of the same down both display as just "3rd Down" with no O/D label, which is confusing even
   though the underlying EventKeys (`"Offense: Third Down"` vs `"Defense: Third Down"`) are
   correctly distinct at runtime. Worth a small UI label fix, not done this session.
