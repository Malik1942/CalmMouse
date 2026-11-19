# Builds CalmMouse.ico, the program's own icon (Explorer, Start menu, taskbar, the settings
# window), from the Mac app's icon (docs/images/icon-256.png), at 16, 20, 24, 32, 40, 48, 64,
# 128 and 256 px. The 256 px image is stored as PNG, the rest as plain bitmaps.
#
# Run it from Windows PowerShell 5.1 or PowerShell 7 on Windows (it uses System.Drawing):
#   powershell -ExecutionPolicy Bypass -File windows/src/CalmMouse.Win/Assets/App/make-app-icon.ps1
# The .ico is checked in; run this again only when the Mac icon changes.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here '..\..\..\..\..')
$source = [System.Drawing.Image]::FromFile((Join-Path $repo 'docs\images\icon-256.png'))
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

$images = foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # A BMP-style icon entry: BITMAPINFOHEADER (height doubled for the mask), bottom-up BGRA
        # pixels, then a 1-bit AND mask (all zero: the alpha channel does the work).
        $w = New-Object System.IO.BinaryWriter $ms
        $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
        $w.Write([int]($size * $size * 4)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([math]::Ceiling($size / 32.0) * 4)
        $w.Write((New-Object byte[] ($maskRow * $size)))
        $w.Flush()
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}
$source.Dispose()

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$img.Bytes.Length); $w.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $here 'CalmMouse.ico'), $out.ToArray())
Write-Host "Wrote $(Join-Path $here 'CalmMouse.ico') ($($out.Length) bytes, $($images.Count) sizes)"
