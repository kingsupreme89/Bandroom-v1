# Normalizes TeamLogos\*.png to tightly-cropped, centered, square canvases.
# The source slices had inconsistent widths (104-316px) at a fixed 177px height with the
# actual logo mark occupying only part of that box -- object-fit:contain then rendered each
# one at a different effective scale, so most logos looked tiny/off-center in the UI tiles.
Add-Type -AssemblyName System.Drawing

$folder = "D:\Claude\Projects\tools\BandAudioHook\TeamLogos"
$files = Get-ChildItem "$folder\*.png"

foreach ($f in $files) {
    $src = [System.Drawing.Bitmap]::FromFile($f.FullName)
    $w = $src.Width
    $h = $src.Height

    $minX = $w; $maxX = -1; $minY = $h; $maxY = -1
    $data = $src.LockBits(
        (New-Object System.Drawing.Rectangle 0, 0, $w, $h),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $bytes = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $src.UnlockBits($data)

    for ($y = 0; $y -lt $h; $y++) {
        $rowOffset = $y * $data.Stride
        for ($x = 0; $x -lt $w; $x++) {
            $alpha = $bytes[$rowOffset + $x * 4 + 3]
            if ($alpha -gt 10) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    if ($maxX -lt 0) {
        Write-Output "$($f.Name): no non-transparent content found, skipping"
        $src.Dispose()
        continue
    }

    $cropW = $maxX - $minX + 1
    $cropH = $maxY - $minY + 1
    $side = [Math]::Ceiling([Math]::Max($cropW, $cropH) * 1.14)  # ~7% margin each side

    $square = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($square)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $destX = [Math]::Floor(($side - $cropW) / 2)
    $destY = [Math]::Floor(($side - $cropH) / 2)
    $srcRect = New-Object System.Drawing.Rectangle $minX, $minY, $cropW, $cropH
    $destRect = New-Object System.Drawing.Rectangle $destX, $destY, $cropW, $cropH
    $g.DrawImage($src, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $src.Dispose()

    $square.Save($f.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
    $square.Dispose()
    Write-Output "$($f.Name): cropped $($cropW)x$($cropH) from $($w)x$($h) -> square $($side)x$($side)"
}
