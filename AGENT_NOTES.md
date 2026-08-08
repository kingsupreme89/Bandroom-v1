# Agent Notes (persistent memory for Claude + Cline)

## Dashboard — fixed 2026-08-07
- `dashboard.html` does `fetch('TASK_BOARD.md')`. Browsers block `fetch()` on `file://` pages, so opening the file directly (double-click, or `file:///C:/Bandroom/dashboard.html`) always shows "Cannot read TASK_BOARD.md — Failed to fetch."
- Fix: always serve it. `serve_dashboard.py` already does this correctly.
  - Start: run `serve_dashboard.py` in VS Code (Run button) or `python C:\Bandroom\serve_dashboard.py` in a terminal.
  - View at: **http://localhost:8765/dashboard.html** — never the `file://` path.
- Cline already has the server running in the background as of this session.

## Known constraint
- Claude's scheduled/background tasks run in an isolated sandbox that does NOT share localhost with this Windows machine. A scheduled check cannot `curl http://localhost:8765` and reach the real server. Health checks instead verify the *files* the pipeline depends on (TASK_BOARD.md freshness, dashboard.html/serve_dashboard.py presence, recent build output) — not the live port.

## Conventions (mirrors TASK_BOARD.md)
- Everything for this project lives in `C:\Bandroom` — not `D:\AGY\Bandroom`.
- Read TASK_BOARD.md before starting any task; mark 🔴 IN PROGRESS / ✅ DONE with timestamp.
