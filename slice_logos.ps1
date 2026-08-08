# One-off slicer for the SEC logo sheet (sec.png, 4x4 grid, white cell backgrounds
# with thin black gridlines). Slices each cell, keys white/near-white pixels to
# transparent so the logos sit cleanly on the app's glass panels, then auto-crops
# the transparent margin so each logo is tightly bounded. Not part of the app build --
# run once, delete when done.
Add-Type -AssemblyName System.Drawing

$src = 'D:\Claude\Projects\tools\BandAudioHook\TeamBackgrounds\sec.png'
$outDir = 'D:\Claude\Projects\tools\BandAudioHook\TeamLogos'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$names = @(
    @('Alabama','Arkansas','Auburn','Florida'),
    @('Georgia','Kentucky','LSU','Mississippi State'),
    @('Missouri','Oklahoma','Ole Miss','South Carolina'),
    @('Tennessee','Texas','Texas A&M','Vanderbilt')
)

$sheet = [System.Drawing.Bitmap]::FromFile($src)
$cols = 4; $rows = 4
$cellW = [math]::Floor($sheet.Width / $cols)
$cellH = [math]::Floor($sheet.Height / $rows)
# Trim a margin off each cell to avoid the grid border lines.
$marginX = [math]::Floor($cellW * 0.06)
$marginY = [math]::Floor($cellH * 0.06)

function Whiten-ToTransparent([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $out = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $bmp.GetPixel($x, $y)
            # Distance from white -- near-white background becomes fully/partially transparent,
            # logo-colored pixels stay opaque. Soft threshold avoids a hard jagged cutout edge.
            $maxc = [math]::Max($p.R, [math]::Max($p.G, $p.B))
            $minc = [math]::Min($p.R, [math]::Min($p.G, $p.B))
            $brightness = ($p.R + $p.G + $p.B) / 3.0
            if ($brightness -gt 246 -and ($maxc - $minc) -lt 10) {
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, $p.R, $p.G, $p.B))
            } elseif ($brightness -gt 225 -and ($maxc - $minc) -lt 14) {
                $alpha = [int](255 * (246 - $brightness) / (246 - 225))
                if ($alpha -lt 0) { $alpha = 0 }
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $p.R, $p.G, $p.B))
            } else {
                $out.SetPixel($x, $y, $p)
            }
        }
    }
    return $out
}

function AutoCrop([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $minX = $w; $minY = $h; $maxX = 0; $maxY = 0
    $found = $false
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            if ($bmp.GetPixel($x, $y).A -gt 15) {
                $found = $true
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if (-not $found) { return $bmp }
    $pad = 4
    $minX = [math]::Max(0, $minX - $pad); $minY = [math]::Max(0, $minY - $pad)
    $maxX = [math]::Min($w - 1, $maxX + $pad); $maxY = [math]::Min($h - 1, $maxY + $pad)
    $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
    $rect = New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch
    return $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

for ($r = 0; $r -lt $rows; $r++) {
    for ($c = 0; $c -lt $cols; $c++) {
        $x = $c * $cellW + $marginX
        $y = $r * $cellH + $marginY
        $w = $cellW - (2 * $marginX)
        $h = $cellH - (2 * $marginY)
        $rect = New-Object System.Drawing.Rectangle $x, $y, $w, $h
        $cell = $sheet.Clone($rect, $sheet.PixelFormat)

        $transparent = Whiten-ToTransparent $cell
        $cropped = AutoCrop $transparent

        $name = $names[$r][$c]
        $path = Join-Path $outDir "$name.png"
        $cropped.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "  $name.png  ($($cropped.Width)x$($cropped.Height))"

        $cell.Dispose(); $transparent.Dispose(); $cropped.Dispose()
    }
}
$sheet.Dispose()
Write-Host "Done."