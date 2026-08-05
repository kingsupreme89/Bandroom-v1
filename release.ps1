# Bandroom release script — bumps patch version, builds, packs with Squirrel (delta+full),
# and publishes to GitHub. Squirrel handles delta generation automatically.
#
# Pass -Notes with real bullet points for what changed (shows up in the in-app Updates panel
# verbatim -- see ChangelogService.cs). Passed via a temp file to `gh`, not inline on the
# command line -- embedding multi-line/quoted text directly in a native-exe argument list is
# exactly what broke this script once already (PowerShell mangled the quoting, `gh` failed,
# and the script kept going and printed "Done!" anyway since nothing checked its exit code).
param(
    [string]$Notes = "See the full changelog at https://github.com/kingsupreme89/Bandroom-v1/releases"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectDir  = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "BandAudioHook.csproj"
$PublishDir  = Join-Path $ProjectDir "publish_temp"
$ReleaseDir  = Join-Path $ProjectDir "squirrel_releases"

# Squirrel CLI bundled with the NuGet package — no separate install needed.
$SquirrelExe = "C:\Users\$env:USERNAME\.nuget\packages\clowd.squirrel\2.11.1\tools\Squirrel.exe"
if (-not (Test-Path $SquirrelExe)) {
    Write-Host "Squirrel.exe not found at $SquirrelExe" -ForegroundColor Red
    Write-Host "Run: dotnet restore" -ForegroundColor Yellow
    exit 1
}

# ── 1. Determine next version ────────────────────────────────────────────────
$latestTag = git -C $ProjectDir tag --sort=-v:refname | Select-Object -First 1
if (-not $latestTag) { $latestTag = "v1.0.0" }

$parts   = $latestTag.TrimStart("v") -split "\."
$major   = [int]$parts[0]
$minor   = [int]$parts[1]
$patch   = [int]$parts[2] + 1
$version = "$major.$minor.$patch"
$tag     = "v$version"

Write-Host ""
Write-Host "  Releasing $tag  (was $latestTag)" -ForegroundColor Cyan
Write-Host ""

# ── 2. Build ─────────────────────────────────────────────────────────────────
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

Write-Host "  Building..." -ForegroundColor Yellow
dotnet publish $ProjectFile -c Release -r win-x64 --self-contained false -o $PublishDir `
    /p:AssemblyVersion=$version /p:FileVersion=$version
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

Remove-Item (Join-Path $PublishDir "*.pdb") -Force -ErrorAction SilentlyContinue

# ── 3. Pack with Squirrel (generates Setup.exe + full + delta nupkgs + RELEASES) ─
Write-Host "  Packing with Squirrel..." -ForegroundColor Yellow
if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleaseDir | Out-Null

$IconPath = Join-Path $ProjectDir "app.ico"
& $SquirrelExe pack `
    --packId      "Bandroom" `
    --packVersion $version `
    --packDir     $PublishDir `
    --releaseDir  $ReleaseDir `
    --icon        $IconPath `
    --framework   "net10.0-x64" `
    --allowUnaware

if ($LASTEXITCODE -ne 0) { Write-Host "Squirrel pack failed." -ForegroundColor Red; exit 1 }

Remove-Item $PublishDir -Recurse -Force

Write-Host "  Squirrel packages:" -ForegroundColor Green
Get-ChildItem $ReleaseDir -File | ForEach-Object {
    Write-Host "    $($_.Name)  ($([math]::Round($_.Length/1MB,1)) MB)"
}

# ── 4. Tag and push ───────────────────────────────────────────────────────────
Write-Host "  Tagging $tag..." -ForegroundColor Yellow
git -C $ProjectDir tag $tag
git -C $ProjectDir push origin $tag

# ── 5. Publish GitHub release with all Squirrel assets ───────────────────────
Write-Host "  Creating GitHub release..." -ForegroundColor Yellow
$assets = Get-ChildItem $ReleaseDir -File | ForEach-Object { $_.FullName }

$notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "bandroom_release_notes_$tag.txt"
[System.IO.File]::WriteAllText($notesFile, $Notes, [System.Text.UTF8Encoding]::new($false))

gh release create $tag @assets `
    --repo  kingsupreme89/Bandroom-v1 `
    --title "Bandroom $tag" `
    --notes-file $notesFile

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "  gh release create FAILED (exit $LASTEXITCODE) -- the tag was pushed but NO release/assets are live." -ForegroundColor Red
    Write-Host "  Fix the issue above, then re-run: gh release create $tag <files in $ReleaseDir> --repo kingsupreme89/Bandroom-v1 --title `"Bandroom $tag`" --notes-file $notesFile" -ForegroundColor Red
    exit 1
}

Remove-Item $notesFile -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "  Done! $tag is live." -ForegroundColor Green
Write-Host "  First-time users run Setup.exe." -ForegroundColor Green
Write-Host "  Existing users get a delta update automatically on next launch." -ForegroundColor Green
Write-Host ""
