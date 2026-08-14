# Builds Windows + Mac dev builds and drops them in D:\DevBuilds for testers.
# Usage: powershell -File scripts\push_devbuild.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$stamp = Get-Date -Format "yyyy-MM-dd_HHmm"

Write-Host "== Publishing Windows build ==" -ForegroundColor Cyan
dotnet publish BandAudioHook.csproj -c Release -r win-x64 --self-contained true -o "D:\DevBuilds\Windows\$stamp"
Copy-Item "D:\DevBuilds\Windows\$stamp" "D:\DevBuilds\Windows\latest" -Recurse -Force

Write-Host "== Publishing Mac build (arm64) ==" -ForegroundColor Cyan
dotnet publish src\Bandroom.Mac\Bandroom.Mac.csproj -c Release -r osx-arm64 --self-contained true -o "D:\DevBuilds\Mac\$stamp-arm64"
Copy-Item "D:\DevBuilds\Mac\$stamp-arm64" "D:\DevBuilds\Mac\latest-arm64" -Recurse -Force

Write-Host "== Publishing Mac build (intel x64) ==" -ForegroundColor Cyan
dotnet publish src\Bandroom.Mac\Bandroom.Mac.csproj -c Release -r osx-x64 --self-contained true -o "D:\DevBuilds\Mac\$stamp-x64"
Copy-Item "D:\DevBuilds\Mac\$stamp-x64" "D:\DevBuilds\Mac\latest-x64" -Recurse -Force

Write-Host ""
Write-Host "Done. Dated builds + a 'latest' copy live under D:\DevBuilds\Windows and D:\DevBuilds\Mac." -ForegroundColor Green
Write-Host "Zip and hand out the 'latest' folder to testers, or share D:\DevBuilds directly if they have LAN/drive access."
