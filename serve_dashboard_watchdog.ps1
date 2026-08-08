# Keeps serve_dashboard.py running: restarts it if it ever exits/crashes.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logFile = Join-Path $scriptDir "dashboard_watchdog.log"

function Log($msg) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $msg" | Out-File -FilePath $logFile -Append -Encoding utf8
}

Log "Watchdog started."

while ($true) {
    $conn = Test-NetConnection -ComputerName localhost -Port 8765 -InformationLevel Quiet -WarningAction SilentlyContinue
    if (-not $conn) {
        Log "Dashboard not reachable on :8765 - starting serve_dashboard.py"
        $scriptPath = Join-Path $scriptDir "serve_dashboard.py"
        Start-Process -FilePath "python" -ArgumentList @($scriptPath) -WorkingDirectory $scriptDir -WindowStyle Hidden
        Start-Sleep -Seconds 3
    }
    Start-Sleep -Seconds 10
}
