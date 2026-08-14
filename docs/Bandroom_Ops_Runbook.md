# BANDroom — Ops / Admin Runbook

Plain-English instructions for running and maintaining the operational side of BANDroom:
the dashboard, the builds, the in-app admin panel, and the Cloudflare workers.

---

## 1. The dashboard (your "is the app alive?" page)

**What it is:** a tiny local web page that shows `TASK_BOARD.md` so anybody on the
team can see the live status without opening the file directly.

- **View it:** http://localhost:8765/dashboard.html
- **Never** double-click `dashboard.html` directly — browsers block the file read and it
  shows "Failed to fetch". Always use the `http://localhost:8765/...` URL.

### Starting it
Run **`python serve_dashboard.py`**, or let the watchdog start it for you
(see "The watchdog" below).

### The watchdog (`serve_dashboard_watchdog.ps1`)
This is the "babysitter" that makes sure the dashboard stays up.

- **What it does:** every few seconds it asks the server "are you alive?" via
  `http://127.0.0.1:8765/health`. If no answer, it starts the server again. It writes
  everything it does to `dashboard_watchdog.log`. It also warns if `TASK_BOARD.md` is
  missing or has not been updated in 48+ hours (so you notice if the pipeline stopped).
- **Register it once** so it survives a reboot. In a terminal, run:
  ```
  schtasks /create /tn "Bandroom Dashboard Watchdog" /tr "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"c:\Bandroom\serve_dashboard_watchdog.ps1\"" /sc onlogon /f
  ```
  If that requires an elevated terminal, the fallback is the HKCU Run key:
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → name "Bandroom Dashboard Watchdog",
  value pointing to the watchdog script.

### Health check
`/health` returns JSON like:
```json
{ "ok": true, "service": "bandroom-dashboard", "taskBoardPresent": true }
```

---

## 2. Building and testing (one command)

Run **`build_all.ps1`** from the repo root. It builds the three projects
(`Bandroom.Core`, the Windows app, `Bandroom.Mac`) and runs the unit tests, then prints
**ALL GREEN** or **SOME STEPS FAILED**.

- Build + test: `powershell -ExecutionPolicy Bypass -File build_all.ps1`
- Build only (skip tests):   add `-SkipTests`

> Tip: run on Windows (the Mac project still builds on Windows; it only needs a Mac to *run*).

---

## 3. In-app admin panel

(Planned) A settings area in the web UI for:
- Secret/API keys (Google, Discord) — where safe to store locally.
- Scoreboard Reader path + on/off toggle + **RAM-vs-screen** safety toggle.
- Marketplace endpoints.

It reads/writes through the existing `WebBridge` (JS ↔ C#) path. See the scoreboard
integration handoff for the RAM/screen safety decision.

---

## 4. Cloudflare workers (marketplace + download counter)

Two workers live here:

| Folder | Purpose | Deploy |
|---|---|---|
| `cloudflare/cloudflare-usercount` | Live user count, all-time **download counter**, Discord relay | `npx wrangler deploy` (from that folder) |
| `cloudflare/cloudflare-marketplace` | Song-pack marketplace (KV + R2) | `npx wrangler deploy` (from that folder) |

**IMPORTANT — the download counter:** it's powered by `cloudflare-usercount/worker.js`
(sums GitHub release asset download counts and caches in KV). Nothing about local git,
profile files, or the scoreboard reader touches it. To keep it working:
- Don't delete or rename the `USERCOUNT` KV binding.
- Don't change `GITHUB_REPO` (`kingsupreme89/Bandroom-v1`) without updating the repo name.
- Secrets (`DISCORD_BOT_TOKEN`) are set with `wrangler secret put ...`, not in files.

Deploy commands require the [Wrangler CLI](https://developers.cloudflare.com/workers/wrangler/)
and being logged in (`npx wrangler login`).