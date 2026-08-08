# Bandroom macOS Setup Summary

## What was done

1. **Installed .NET SDK on macOS** (verified via `dotnet --version`).
2. **Created a cross-platform solution** (`Bandroom.sln`) containing:
   - `src/Bandroom.Core`: a shared class library for the trigger engine.
   - `src/Bandroom.Mac`: an Avalonia-based macOS desktop application.
3. **Implemented the shared trigger engine** in `Bandroom.Core`:
   - `GameState`: holds current and previous play snapshots.
   - `PlaySnapshot`: represents a single play's state (down, yards to go, yard line, scores, etc.).
   - `PlayDelta`: calculates changes between plays (yards gained, possession changes, etc.).
   - `TriggerEvent`: represents a fired trigger (event key, volume, big event flag).
   - `IRuleEvaluator`: interface for rule helpers.
   - `EventRouter`: routes a `GameState` to all rule helpers and collects fired events.
   - Rule helpers (in `Helpers/`):
     - `TflHelper`: detects Tackle For Loss.
     - `OffenseDownHelper`: triggers on offense down (1st, 2nd, 3rd).
     - `TimeoutHelper`: tracks defensive timeouts remaining.
     - `BigEventHelper`: triggers on big plays (long touchdown, 3rd/4th down stops).
     - `DefenseHelper`: triggers on defensive downs (2nd, 3rd down with loss).
4. **Created the Avalonia Mac app** (`Bandroom.Mac`):
   - `MainWindow.axaml`: UI with a button and a text block.
   - `MainWindow.axaml.cs`: wiring the button to run a demo game state through the trigger engine and display results.
5. **Verified the build**:
   - `dotnet build Bandroom.sln` succeeds on macOS.

## Current state

- The solution builds without errors.
- The Mac app runs and shows a demo:
  - Clicking "Run Demo Trigger" processes a hard-coded game state (e.g., 2nd & 5 -> 3rd & 10) and displays the fired trigger (e.g., `OFF_ON_3RD_DOWN @ 70%`).
- The shared engine (`Bandroom.Core`) is ready to accept real game state from any source (OCR, manual input, etc.).

## What is not yet wired

- The real scoreboard/OCR input from the existing Windows game watcher (`GameWatcher.cs`) is not yet connected to the shared engine.
- The Mac app currently uses a hard-coded demo state instead of live game data.

## Next steps

1. **Map the existing OCR output to `GameState`**:
   - Extract the relevant fields from `GameWatcher` (down, yards to go, yard line, scores, quarter, time remaining, possession, etc.).
   - Populate a `GameState` object (both `Previous` and `Current` snapshots).
2. **Integrate with the shared engine**:
   - In the Windows app (`GameWatcher.cs`), after updating the game state, call:
     ```csharp
     var triggers = _router.Route(gameState);
     ```
   - For each `TriggerEvent` in the result, trigger the corresponding audio (using the existing trigger-to-audio mapping).
3. **Port the same integration to the Mac app** (if live screen capture is desired on macOS) or leave it as a demo that can be fed manual state for testing.
4. **Expand the rule helpers** in `Bandroom.Core` to cover all the trigger events listed in the original `triggers.json` (or the user's provided list).

## Files of interest

- `/Users/user/CODING/PROJECTS/BANDROOM/Bandroom.sln`: the solution file.
- `/Users/user/CODING/PROJECTS/BANDROOM/src/Bandroom.Core/`: shared trigger engine.
- `/Users/user/CODING/PROJECTS/BANDROOM/src/Bandroom.Mac/`: Avalonia macOS app.
- `/Users/user/CODING/PROJECTS/BANDROOM/GameWatcher.cs`: existing Windows OCR/game watcher (to be connected).

## How to run the Mac app demo

```bash
cd /Users/user/CODING/PROJECTS/BANDROOM
dotnet run --project src/Bandroom.Mac/Bandroom.Mac.csproj
```

Click the "Run Demo Trigger" button to see the engine in action.

## Notes for 5-year-olds

- We built a robot brain (`Bandroom.Core`) that decides when to play sounds.
- We gave the robot a Mac body (`Bandroom.Mac`) that can press a button to ask the brain: "What sound should we play now?"
- The brain looks at the game state (like what down it is, how many yards to go, etc.) and says which sound to play.
- Next, we'll connect the robot's eyes (the scoreboard reader) to the brain so it can see the game and decide sounds all by itself.
