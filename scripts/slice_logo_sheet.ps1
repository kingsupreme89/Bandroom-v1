# Generic slicer for the new "3D tile" logo sheets (Downloads folder) -- unlike the old SEC/
# Big Ten sheets, these tiles are meant to be used FULL-BLEED (CSS now does object-fit: cover
# on .team-logo-img, see style.css), so no transparency-keying or auto-crop is needed here.
# Just cut each grid cell out with a small margin trim and save.
#
# Usage: pass -Src, -Cols, -Rows, and -Names (row-major, $null skips a cell).
param(
    [Parameter(Mandatory)] [string]$Src,
    [Parameter(Mandatory)] [int]$Cols,
    [Parameter(Mandatory)] [int]$Rows,
    # row-major flat list, length Cols*Rows, joined with "|", empty segment = skip that cell
    [Parameter(Mandatory)] [string]$NamesJoined,
    [string]$OutDir = 'D:\Claude\Projects\tools\BandAudioHook\TeamLogos'
)
Add-Type -AssemblyName System.Drawing

$Names = $NamesJoined -split '\|'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$sheet = [System.Drawing.Bitmap]::FromFile($Src)
$cellW = [math]::Floor($sheet.Width / $Cols)
$cellH = [math]::Floor($sheet.Height / $Rows)
$marginX = [math]::Floor($cellW * 0.04)
$marginY = [math]::Floor($cellH * 0.04)

for ($r = 0; $r -lt $Rows; $r++) {
    for ($c = 0; $c -lt $Cols; $c++) {
        $i = $r * $Cols + $c
        $name = $Names[$i]
        if ($null -eq $name -or $name -eq '') { continue }

        $x = $c * $cellW + $marginX
        $y = $r * $cellH + $marginY
        $w = $cellW - (2 * $marginX)
        $h = $cellH - (2 * $marginY)
        $rect = New-Object System.Drawing.Rectangle $x, $y, $w, $h
        $cell = $sheet.Clone($rect, $sheet.PixelFormat)

        $path = Join-Path $OutDir "$name.png"
        $cell.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "  $name.png  ($($cell.Width)x$($cell.Height))"
        $cell.Dispose()
    }
}
$sheet.Dispose()
Write-Host "Done."
