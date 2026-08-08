# Roadmap A — Committed Work (what you've actually told us to do)

This is everything explicitly requested or already in motion, pulled from `TASK_BOARD.md`,
`AGENT_NOTES.md`, the session handoffs, and this conversation. Nothing here is a suggestion — it's
a clean list of stated asks, organized so you can see what's shipped vs. what's left. Live status
always lives in `TASK_BOARD.md`; this is the organized read-through of it.

## 1. Windows engine + integration — ✅ done

1. Fix `GameWatcher.cs` usings + csproj reference (`using Bandroom.Core` + `Helpers`, `ProjectReference` with `DefaultItemExcludes`).
2. Wire `EventsDetected` → `FireEventForSide` (`OnEngineEventsDetected` in `WebMainForm.cs`).
3. Exclude `src\**` from `BandAudioHook` compilation so it doesn't collide with the shared `Bandroom.Core` engine.
4. Clear stale `obj`/`bin` and rebuild Windows — confirmed 0 errors, 5 pre-existing warnings.
5. `Bandroom.Core.dll` (net10.0, 16 evaluators, 30 EventKeys) and `Bandroom.dll` both build clean.

## 2. Orchestrator dashboard — ✅ done, ⚠️ needs a durability fix

6. Build `TASK_BOARD.md` + `dashboard.html` + `serve_dashboard.py` as the shared status board between Cline and the orchestrator.
7. Diagnose and fix the "Cannot read TASK_BOARD.md — Failed to fetch" error — root cause was opening `dashboard.html` via `file://`, which browsers block from calling `fetch()`. Fix is to always serve it over HTTP.
8. Get the server actually running and confirmed reachable at `http://localhost:8765/dashboard.html`.
9. Document the fix and the "two copies of the project" trap (`D:\AGY\Bandroom` vs. `C:\Bandroom`) in `AGENT_NOTES.md` so it isn't rediscovered from scratch next session.
10. **Still open**: the server has already died once mid-session and needed a manual restart. It isn't running as a persistent/auto-restarting service — that's a real gap, not just a one-off. See Roadmap B for the durable fix (item 46).

## 3. Agent coordination / process — ✅ set up

11. Confirm both Cline and the orchestrator are actually reading/writing the same `TASK_BOARD.md` (verified — Cline references it directly in its own chat as "the shared file").
12. Correct stale/inaccurate board entries when caught (task board lagged behind Cline's real-time progress at least twice this session — corrected each time rather than left wrong).
13. Set up a recurring 10-minute health check (`bandroom-health-check` scheduled task) — file-based (TASK_BOARD.md freshness, presence of dashboard files, build output timestamps), since the sandbox this runs in cannot reach the user's Windows `localhost`.
14. Write a session handoff (`docs/Bandroom_Handoff_2026-08-07_Session1.md`) summarizing state for whoever (human or Claude Code) picks this up next.

## 4. macOS port — 🔴 in progress, this is the current front line

15. Stand up `Bandroom.sln` as a cross-platform solution: `src/Bandroom.Core` (shared engine) + `src/Bandroom.Mac` (Avalonia app) — done.
16. Update `Bandroom.Mac.csproj` (Exe output, content includes `wwwroot`, `Assets`, `TeamLogos`, `TeamBackgrounds`, `Fonts`) — done.
17. Build `MainWindow.axaml` + `MainWindow.axaml.cs` with a full-screen WebView + `MacWebBridge` stub — done, then further simplified (WebView pulled from the XAML in a later edit — **needs a direct check that this didn't regress the WebView wiring**, it wasn't confirmed before this doc was written).
18. **Immediate blocker**: last observed `dotnet build` on `Bandroom.Mac.csproj` came back with **1 error**, after an earlier NuGet restore attempt (Avalonia packages) timed out. The board still shows this task as "IN PROGRESS" rather than reflecting the error — get the actual error text and fix it before anything else on the Mac side.
19. Port `AudioPlayer` from NAudio to AVFoundation.
20. Port `GameWatcher` from WinRT `OcrEngine` to macOS Vision framework (+ ScreenCaptureKit for capture).
21. Port `KeyboardHook` from Win32 `RegisterHotKey` to macOS hotkeys (Carbon/CGEvent).
22. Plumb `WebBridge` JS↔C# interop through Avalonia's WebView.
23. Reach full feature parity: port `ConfigStore`, `TeamColors`, profiles, and the Sparkle-style auto-updater equivalent for Mac.

## How to use this document

- This is a snapshot, not the live source — `TASK_BOARD.md` is always more current.
- Items 8, 10, and 17-18 are the parts most likely to have moved since this was written; verify against the board before acting on them.
