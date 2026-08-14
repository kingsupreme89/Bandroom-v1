# Measure the largest top-level directories on C: and inside the user profile.
$ErrorActionPreference = 'SilentlyContinue'

function Get-DirSizeGB([string]$Path) {
    $s = (Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    return [math]::Round($s / 1GB, 2)
}

Write-Output "=== Top-level C:\ directories (largest first) ==="
Get-ChildItem -LiteralPath 'C:\' -Directory -Force -ErrorAction SilentlyContinue |
    ForEach-Object {
        [PSCustomObject]@{ GB = Get-DirSizeGB $_.FullName; Path = $_.FullName }
    } |
    Sort-Object GB -Descending | Select-Object -First 20 |
    ForEach-Object { '{0,8:N2} GB  {1}' -f $_.GB, $_.Path }

Write-Output ""
Write-Output "=== User profile top-level directories (largest first) ==="
Get-ChildItem -LiteralPath $env:USERPROFILE -Directory -Force -ErrorAction SilentlyContinue |
    ForEach-Object {
        [PSCustomObject]@{ GB = Get-DirSizeGB $_.FullName; Path = $_.FullName }
    } |
    Sort-Object GB -Descending | Select-Object -First 20 |
    ForEach-Object { '{0,8:N2} GB  {1}' -f $_.GB, $_.Path }