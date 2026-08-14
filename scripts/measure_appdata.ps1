# Breakdown of the user's AppData (Local + Roaming), largest first.
$ErrorActionPreference = 'SilentlyContinue'

function Get-DirSizeGB([string]$Path) {
    $s = (Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    return [math]::Round($s / 1GB, 2)
}

foreach ($root in @('Local', 'LocalLow', 'Roaming')) {
    $base = Join-Path $env:APPDATA "..\$root"
    if (-not (Test-Path -LiteralPath $base)) { continue }
    $normalized = [System.IO.Path]::GetFullPath($base)
    Write-Output "=== AppData\$root (largest first) ==="
    Get-ChildItem -LiteralPath $normalized -Directory -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            [PSCustomObject]@{ GB = Get-DirSizeGB $_.FullName; Path = $_.FullName }
        } |
        Sort-Object GB -Descending | Select-Object -First 15 |
        ForEach-Object { '{0,8:N2} GB  {1}' -f $_.GB, $_.Path }
    Write-Output ""
}