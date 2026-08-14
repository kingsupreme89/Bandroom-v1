# Measure sizes of common safe-to-clean locations on C: drive.
$paths = [ordered]@{
    'User Temp'      = $env:TEMP
    'Windows Temp'   = 'C:\Windows\Temp'
    'WinUpdate Dwnld'= 'C:\Windows\SoftwareDistribution\Download'
    'DeliveryOpt'    = 'C:\Windows\SoftwareDistribution\DeliveryOptimization'
    'WER Reports'    = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\WER'
    'NuGet Cache'    = Join-Path $env:USERPROFILE '.nuget\packages'
    'npm Cache'      = Join-Path $env:LOCALAPPDATA 'npm-cache'
    'pip Cache'      = Join-Path $env:LOCALAPPDATA 'pip\cache'
    'Recycle Bin'    = 'C:\$Recycle.Bin'
    'Windows.old'    = 'C:\Windows.old'
}

foreach ($k in $paths.Keys) {
    $p = $paths[$k]
    if (Test-Path -LiteralPath $p) {
        $s = (Get-ChildItem -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        '{0,-16} {1,10:N1} MB   {2}' -f $k, ($s / 1MB), $p
    }
    else {
        '{0,-16} {1,10}   {2} (missing)' -f $k, '-', $p
    }
}