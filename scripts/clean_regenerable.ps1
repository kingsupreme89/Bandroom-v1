# Delete regenerable caches across the system.
$ErrorActionPreference = 'SilentlyContinue'

function Clear-Contents([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$before = (Get-PSDrive C).Free

# --- System junk ---
Clear-Contents $env:TEMP
Clear-Contents 'C:\Windows\Temp'
Stop-Service -Name wuauserv, bits -Force -ErrorAction SilentlyContinue
Clear-Contents 'C:\Windows\SoftwareDistribution\Download'
Start-Service -Name wuauserv, bits -ErrorAction SilentlyContinue
try { Clear-RecycleBin -Force -ErrorAction SilentlyContinue } catch { }
Clear-Contents (Join-Path $env:LOCALAPPDATA 'npm-cache')
Clear-Contents (Join-Path $env:USERPROFILE '.nuget\packages')
Clear-Contents (Join-Path $env:LOCALAPPDATA 'pip\cache')

# --- Claude Desktop VM + caches (regenerate on next launch) ---
Clear-Contents (Join-Path $env:APPDATA 'Claude\vm_bundles')
Clear-Contents (Join-Path $env:APPDATA 'Claude\Cache')
Clear-Contents (Join-Path $env:APPDATA 'Claude\Code Cache')
Clear-Contents (Join-Path $env:APPDATA 'Claude\GPUCache')
Clear-Contents (Join-Path $env:APPDATA 'Claude\logs')

# --- VS Code caches (keep User settings) ---
foreach ($sub in @('CachedExtensionVSIXs','Cache','logs','WebStorage','GPUCache','Crashpad')) {
    Clear-Contents (Join-Path $env:APPDATA "Code\$sub")
}

# --- Ollama updater leftovers ---
Clear-Contents (Join-Path $env:LOCALAPPDATA 'Ollama\updates_v2')

# --- Playwright downloaded browsers ---
Clear-Contents (Join-Path $env:LOCALAPPDATA 'ms-playwright')

# --- Chrome caches (keep bookmarks/passwords) ---
$chromeUserData = Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data'
if (Test-Path -LiteralPath $chromeUserData) {
    Clear-Contents (Join-Path $chromeUserData 'Cache')
    Clear-Contents (Join-Path $chromeUserData 'Crashpad')
    Get-ChildItem -LiteralPath $chromeUserData -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        Clear-Contents (Join-Path $_.FullName 'Cache')
        Clear-Contents (Join-Path $_.FullName 'Code Cache')
        Clear-Contents (Join-Path $_.FullName 'GPUCache')
        Clear-Contents (Join-Path $_.FullName 'Service Worker\CacheStorage')
    }
}

# --- Medal caches (app version folders left alone) ---
Clear-Contents (Join-Path $env:APPDATA 'Medal\Cache')
Clear-Contents (Join-Path $env:APPDATA 'Medal\Partitions')

# --- Old Bandroom Squirrel app versions (current is app-1.1.5) ---
Clear-Contents (Join-Path $env:LOCALAPPDATA 'Bandroom\app-1.1.4')

$after = (Get-PSDrive C).Free
'Freed GB: ' + [math]::Round(($after - $before)/1GB, 2)
'Free now GB: ' + [math]::Round($after/1GB, 2)