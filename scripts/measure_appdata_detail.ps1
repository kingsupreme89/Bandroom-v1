# Drill into the largest AppData folders to identify what is safe to delete.
$ErrorActionPreference = 'SilentlyContinue'

function Get-DirSizeGB([string]$Path) {
    $s = (Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    return [math]::Round($s / 1GB, 2)
}

$targets = @(
    "$env:APPDATA\Claude",
    "$env:LOCALAPPDATA\Bandroom",
    "$env:LOCALAPPDATA\Google",
    "$env:LOCALAPPDATA\Programs",
    "$env:LOCALAPPDATA\Ollama",
    "$env:LOCALAPPDATA\ms-playwright",
    "$env:LOCALAPPDATA\OpenAI",
    "$env:LOCALAPPDATA\lm-studio-updater",
    "$env:LOCALAPPDATA\cfb27-roster-editor-updater",
    "$env:APPDATA\Code",
    "$env:LOCALAPPDATA\Medal",
    "$env:APPDATA\Medal"
)

foreach ($t in $targets) {
    if (-not (Test-Path -LiteralPath $t)) { continue }
    Write-Output "=== $t ==="
    Get-ChildItem -LiteralPath $t -Directory -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            [PSCustomObject]@{ GB = Get-DirSizeGB $_.FullName; Path = $_.Name }
        } |
        Sort-Object GB -Descending | Select-Object -First 12 |
        ForEach-Object { '{0,8:N2} GB  {1}' -f $_.GB, $_.Path }
    Write-Output ""
}