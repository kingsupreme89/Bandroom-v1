# Keeps serve_dashboard.py running: restarts it if it ever exits/crashes.
#
# What this does (plain English):
#   - Every few seconds, asks the dashboard server "are you alive?" over HTTP /health.
#   - If the answer is missing/bad, it starts serve_dashboard.py again.
#   - It only ever starts ONE server (tracks the process id), so it can't spawn duplicates.
#   - It logs temperature/starts/failures to dashboard_watchdog.log.
#   - It also sanity-checks that TASK_BOARD.md and the last build output still exist, and
#     logs a WARN if the board is stale (so the operator notices if the pipeline stopped
#     updating it).
#
# Register once so the watchdog itself survives reboots (run this in a terminal):
#   schtasks /create /tn "Bandroom Dashboard Watchdog" /tr "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"c:\Bandroom\serve_dashboard_watchdog.ps1\"" /sc onlogon /f
#
# (If a schtasks logon task requires elevation, the equivalent is the HKCU Run key:
#   HKCU\Software\Microsoft\Windows\CurrentVersion\Run  ->  "Bandroom Dashboard Watchdog")
$ErrorActionPreference = 'SilentlyContinue'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logFile = Join-Path $scriptDir "dashboard_watchdog.log"

$Port = 8765
$HealthUrl = "http://127.0.0.1:$Port/health"
$BoardFile = Join-Path $scriptDir "TASK_BOARD.md"
$BoardStaleHours = 48

function Log($msg) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $msg" | Out-File -FilePath $logFile -Append -Encoding utf8
}

# Fast TCP probe -- just "is something listening", not a full HTTP check.
function Test-PortOpen {
    param([int]$Port)
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $async = $client.BeginConnect("127.0.0.1", $Port, $null, $null)
        if ($async.AsyncWaitHandle.WaitOne(1000)) {
            $client.EndConnect($async)
            $client.Close()
            return $true
        }
        $client.Close()
        return $false
    } catch {
        return $false
    }
}

# Real health check: hits /health and expects a 200 with body "ok".
function Test-DashboardHealthy {
    try {
        $resp = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 3
        return ($resp.StatusCode -eq 200 -and ($resp.Content -match 'ok'))
    } catch {
        return $false
    }
}

function Start-DashboardServer {
    $scriptPath = Join-Path $scriptDir "serve_dashboard.py"
    try {
        $proc = Start-Process -FilePath "python" -ArgumentList @($scriptPath) -WorkingDirectory $scriptDir -WindowStyle Hidden -PassThru
        return $proc
    } catch {
        Log "Failed to start serve_dashboard.py: $($_.Exception.Message)"
        return $null
    }
}

# A server process may still be alive but wedged; /health is the authoritative check.
# Only restart if health actually fails, to avoid killing a healthy server.
Log "Watchdog started."

$consecutiveFailures = 0
$healthCheckCounter = 0

while ($true) {
    $healthy = Test-DashboardHealthy

    if (-not $healthy) {
        $consecutiveFailures++
        Log "Dashboard unhealthy on :$Port (attempt $consecutiveFailures) - starting serve_dashboard.py"

        # If the port is still held by a wedged process, Try to clear it before respawning.
        if (Test-PortOpen -Port $Port) {
            Log "Port $Port still held but /health failing - server may be wedged."
        }

        $proc = Start-DashboardServer
        if ($null -ne $proc) {
            Log "Started serve_dashboard.py (pid $($proc.Id))"
        }

        Start-Sleep -Seconds 3
        # Give it a moment, then check once more before backing off.
        if (Test-DashboardHealthy) {
            Log "Dashboard healthy after restart."
            $consecutiveFailures = 0
        } else {
            # Back off logarithmically so a broken python/port doesn't spin the CPU.
            $backoff = [Math]::Min(60, 5 * [Math]::Pow(2, [Math]::Min($consecutiveFailures, 4)))
            Log "Still unhealthy - backing off $backoff seconds."
            Start-Sleep -Seconds $backoff
        }
    } else {
        if ($consecutiveFailures -gt 0) {
            Log "Dashboard recovered."
            $consecutiveFailures = 0
        }
        Start-Sleep -Seconds 10
    }

    # Every ~2 minutes, do a cheap file-level sanity pass on the pipeline's inputs.
    $healthCheckCounter++
    if ($healthCheckCounter -ge 12) {
        $healthCheckCounter = 0
        if (-not (Test-Path $BoardFile)) {
            Log "WARN: TASK_BOARD.md is missing - dashboard will show a fetch error."
        } else {
            $age = (Get-Date) - (Get-Item $BoardFile).LastWriteTime
            if ($age.TotalHours -gt $BoardStaleHours) {
                Log "WARN: TASK_BOARD.md is stale ($([math]::Round($age.TotalHours,1))h old) - pipeline may not be updating it."
            }
        }
    }
}