# Generic slicer for the new "3D tile" logo sheets (Downloads folder). Each cell is first cut
# out, then TIGHT-cropped to the bounding box of the actual logo artwork (discarding the flat
# card background around it) -- so once CSS stretches it to fill the button
# (object-fit: cover, see style.css), the logo itself reads big and fills the whole tile
# instead of sitting small in the middle with a lot of dead card border showing. This is what
# makes every team look uniform regardless of how much padding its source tile had.
#
# Usage: pass -Src, -Cols, -Rows, and -NamesJoined (row-major, "|"-joined, empty segment skips).
param(
    [Parameter(Mandatory)] [string]$Src,
    [Parameter(Mandatory)] [int]$Cols,
    [Parameter(Mandatory)] [int]$Rows,
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

# The metallic/silver card backgrounds are low-saturation gray with a brightness GRADIENT
# (lighter top-left highlight to darker edges) -- a single-pixel background color-distance
# check flags most of that gradient as "logo". Saturation is what actually separates a colorful
# logo mark from the neutral card, regardless of the gradient's brightness at that point.
function IsLogoPixel([System.Drawing.Color]$p) {
    $maxc = [math]::Max($p.R, [math]::Max($p.G, $p.B))
    $minc = [math]::Min($p.R, [math]::Min($p.G, $p.B))
    $sat = $maxc - $minc
    # Catches saturated color (the usual case) AND near-black ink (some logos use pure black
    # outlines/text, which is low-saturation but very dark -- the metallic card never gets
    # that dark even in its deepest shadow).
    return ($sat -gt 25) -or ($maxc -lt 60)
}

# Crops to the bounding box of "logo" pixels (see IsLogoPixel), then pads slightly.
function TightCropToLogo([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $minX = $w; $minY = $h; $maxX = 0; $maxY = 0
    $found = $false
    for ($y = 0; $y -lt $h; $y += 2) {
        for ($x = 0; $x -lt $w; $x += 2) {
            if (IsLogoPixel $bmp.GetPixel($x, $y)) {
                $found = $true
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if (-not $found) { return $bmp }
    # Negative pad -- crop slightly INSIDE the detected logo bounds, not around them, so zero
    # card-background sliver survives at any edge. A little of the logo's own outer edge gets
    # cut off, which is fine/expected: CSS stretches this to fill the button completely, so the
    # priority is "no edge visible," not "not one pixel of the mark clipped."
    $padX = -[math]::Floor($w * 0.035); $padY = -[math]::Floor($h * 0.035)
    $minX = [math]::Max(0, $minX - $padX); $minY = [math]::Max(0, $minY - $padY)
    $maxX = [math]::Min($w - 1, $maxX + $padX); $maxY = [math]::Min($h - 1, $maxY + $padY)
    $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
    $rect = New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch
    return $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

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
        $tight = TightCropToLogo $cell

        $path = Join-Path $OutDir "$name.png"
        $tight.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "  $name.png  ($($tight.Width)x$($tight.Height))"
        $cell.Dispose(); $tight.Dispose()
    }
}
$sheet.Dispose()
Write-Host "Done."
