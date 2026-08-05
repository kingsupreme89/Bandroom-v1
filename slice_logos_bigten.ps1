# Slicer for the Big Ten logo sheet -- unlike the SEC sheet (logos on plain white), each Big
# Ten cell is its own rounded-square "app icon" style badge (colored/bordered background is
# PART of the design, e.g. Indiana's white card or Ohio State's white card are intentional).
# So the transparency key targets the light-gray PAGE background specifically (~218,218,218),
# not "near white" generally -- otherwise Indiana/Northwestern/Ohio State/Penn State's white
# tile interiors would get wrongly punched transparent.
Add-Type -AssemblyName System.Drawing

$src = 'D:\Claude\Projects\tools\BandAudioHook\TeamBackgrounds\big ten.png'
$outDir = 'D:\Claude\Projects\tools\BandAudioHook\TeamLogos'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# row,col grid. $null = skip (B1G conference logo isn't a team; last cell is blank).
$names = @(
    @('Illinois','Indiana','Iowa','Maryland'),
    @('Michigan','Michigan State','Minnesota','Nebraska'),
    @('Northwestern','Ohio State','Penn State','Purdue'),
    @('Rutgers','Wisconsin',$null,$null)
)

$sheet = [System.Drawing.Bitmap]::FromFile($src)
$cols = 4; $rows = 4
$cellW = [math]::Floor($sheet.Width / $cols)
$cellH = [math]::Floor($sheet.Height / $rows)
$marginX = [math]::Floor($cellW * 0.03)
$marginY = [math]::Floor($cellH * 0.03)

function PageBgToTransparent([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $out = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $maxc = [math]::Max($p.R, [math]::Max($p.G, $p.B))
            $minc = [math]::Min($p.R, [math]::Min($p.G, $p.B))
            $isGrayish = ($maxc - $minc) -lt 8
            if ($isGrayish -and $p.R -ge 195 -and $p.R -le 235) {
                $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, $p.R, $p.G, $p.B))
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
    $pad = 2
    $minX = [math]::Max(0, $minX - $pad); $minY = [math]::Max(0, $minY - $pad)
    $maxX = [math]::Min($w - 1, $maxX + $pad); $maxY = [math]::Min($h - 1, $maxY + $pad)
    $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
    $rect = New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch
    return $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

for ($r = 0; $r -lt $rows; $r++) {
    for ($c = 0; $c -lt $cols; $c++) {
        $name = $names[$r][$c]
        if ($null -eq $name) { continue }

        $x = $c * $cellW + $marginX
        $y = $r * $cellH + $marginY
        $w = $cellW - (2 * $marginX)
        $h = $cellH - (2 * $marginY)
        $rect = New-Object System.Drawing.Rectangle $x, $y, $w, $h
        $cell = $sheet.Clone($rect, $sheet.PixelFormat)

        $transparent = PageBgToTransparent $cell
        $cropped = AutoCrop $transparent

        $path = Join-Path $outDir "$name.png"
        $cropped.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "  $name.png  ($($cropped.Width)x$($cropped.Height))"

        $cell.Dispose(); $transparent.Dispose(); $cropped.Dispose()
    }
}
$sheet.Dispose()
Write-Host "Done."