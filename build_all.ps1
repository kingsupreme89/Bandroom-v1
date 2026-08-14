# One-command build + test for BANDroom.
#   ./build_all.ps1          -> build everything + run tests
#   ./build_all.ps1 -SkipTests -> build only (no test run)
#
# Plain English: instead of remembering three separate dotnet commands,
# this builds the shared engine, the Windows app, the Mac app, then runs
# the unit tests -- and prints a clear PASS/FAIL summary at the end.

param(
    [switch]$SkipTests
)
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$projects = @(
    @{ Name = "Bandroom.Core"; Path = "$root\src\Bandroom.Core\Bandroom.Core.csproj" },
    @{ Name = "Bandroom (Windows)"; Path = "$root\BandAudioHook.csproj" },
    @{ Name = "Bandroom.Mac"; Path = "$root\src\Bandroom.Mac\Bandroom.Mac.csproj" }
)
$testProject = @{ Name = "Bandroom.Core.Tests"; Path = "$root\src\Bandroom.Core.Tests\Bandroom.Core.Tests.csproj" }

$overallOk = $true

Write-Host "`n=== BANDROOM BUILD ===" -ForegroundColor Cyan

foreach ($p in $projects) {
    Write-Host "`n--- Building $($p.Name) ---" -ForegroundColor Yellow
    dotnet build $p.Path --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: $($p.Name)" -ForegroundColor Red
        $overallOk = $false
    } else {
        Write-Host "OK:   $($p.Name)" -ForegroundColor Green
    }
}

if (-not $SkipTests) {
    Write-Host "`n--- Running tests: $($testProject.Name) ---" -ForegroundColor Yellow
    dotnet test $testProject.Path --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: $($testProject.Name)" -ForegroundColor Red
        $overallOk = $false
    } else {
        Write-Host "OK:   $($testProject.Name)" -ForegroundColor Green
    }
}

Write-Host "`n=== SUMMARY ===" -ForegroundColor Cyan
if ($overallOk) {
    Write-Host "ALL GREEN" -ForegroundColor Green
} else {
    Write-Host "SOME STEPS FAILED - see output above" -ForegroundColor Red
}
exit $(if ($overallOk) { 0 } else { 1 })