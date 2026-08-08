# Bandroom Session Handoff — 2026-08-07

Source of truth for live task state during this build push: `C:\Bandroom\TASK_BOARD.md`. Read that
file first — it's updated by Cline (in VS Code) as it works, and by the orchestrator (Claude, via
this Cowork session) when checking in. This doc is a point-in-time summary for whoever picks up
next in Claude Code; TASK_BOARD.md is the live version.

## Two-agent setup on this project

- **Cline** (VS Code extension) is doing the actual engine/Mac coding work.
- **Claude / orchestrator** (this session) reads and edits `TASK_BOARD.md` to track status, fixes
  the dashboard, writes handoffs, and runs a 10-minute file-based health check
  (`bandroom-health-check` scheduled task) — it does **not** have a live network path to the user's
  machine, so it can't ping `localhost:8765` or watch Cline work in real time. It only knows what's
  written to the board or told to it directly.
- **Known trap**: there are two copies of this project on disk — `D:\AGY\Bandroom` (stale, do not
  use) and `C:\Bandroom` (correct, live one). VS Code's file explorer has been observed rooted at
  `D:\AGY\Bandroom` even while individual tabs are opened from `C:\Bandroom` — don't trust the
  explorer tree alone to confirm where a file lives; check the editor breadcrumb (drive letter) or
  use an absolute path.

## Windows app — done

- Engine (`Bandroom.Core.dll`, net10.0, 16 evaluators, 30 EventKeys): building clean.
- `Bandroom.dll` (Windows app): 0 errors, engine fully wired — `EventsDetected` →
  `WebMainForm.OnEngineEventsDetected` → `FireEventForSide`.
- GameWatcher.cs usings/csproj refs fixed, stale `src\**` excluded from compilation, stale
  obj/bin cleared and rebuilt.

## Dashboard — fixed, now live

- Root cause of the original "Cannot read TASK_BOARD.md — Failed to fetch" error: `dashboard.html`
  does `fetch('TASK_BOARD.md')`, and browsers block `fetch()` on `file://` pages. Opening the file
  directly (double-click or `file:///C:/Bandroom/dashboard.html`) will always show that error.
- Fix: serve it. `serve_dashboard.py` (already written, correct) serves both files on port 8765.
- Correct URL: **http://localhost:8765/dashboard.html** — never the `file://` path.
- Confirmed running by the user as of this session. It had died once mid-session (server process
  closed) and needed a manual restart — if the dashboard shows connection-refused again, restart
  with `python C:\Bandroom\serve_dashboard.py` (VS Code Run button or terminal).
- See `C:\Bandroom\AGENT_NOTES.md` for the same context, written for any agent (Claude or Cline)
  picking this up cold.

## Mac app — in progress, not clean yet

Per TASK_BOARD.md as of ~17:13 and Cline's live session (later than the board reflects):
- `Bandroom.Mac.csproj` updated (Exe, content includes wwwroot/Assets/TeamLogos/TeamBackgrounds/Fonts) — done.
- `MainWindow.axaml` + `MainWindow.axaml.cs` with full-screen WebView + `MacWebBridge` stub — done,
  then further simplified (removed WebView from XAML per a later Cline edit visible in the diff
  view — worth double-checking this didn't regress the WebView wiring).
- **Build verification is not clean**: last observed `dotnet build` on `Bandroom.Mac.csproj` came
  back with **1 error**, after an earlier attempt showed "Command timed out" during NuGet restore
  (Avalonia packages). TASK_BOARD.md still shows this task as "🔴 IN PROGRESS" rather than reflecting
  the error — treat the board as lagging behind actual state here, verify directly before trusting it.
- Still fully TODO: port AudioPlayer → AVFoundation, GameWatcher → Vision OCR, KeyboardHook →
  macOS hotkeys (Carbon/CGEvent), and full feature parity (ConfigStore/TeamColors/profiles/updater).

## Immediate next step for whoever picks this up

1. Get the actual current build error text for `Bandroom.Mac.csproj` (the board doesn't have it).
2. Fix it, confirm 0 errors, then update TASK_BOARD.md task #8 to DONE with a timestamp — don't
   leave it stale.
3. Continue down the Mac task list (#9–12) in order.
