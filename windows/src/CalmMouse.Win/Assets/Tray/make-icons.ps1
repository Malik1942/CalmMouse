# Draws the tray icons into this folder: a plain mouse glyph in three states, each for a dark
# and a light taskbar, at 16, 20, 24, 28, 32, 40 and 48 px (100, 125, 150, 175, 200, 250 and 300 %
# scaling).
# Each size is drawn on its own pixel grid, so edges land on whole pixels and stay crisp.
#
#   normal   filled mouse, white on a dark taskbar, near-black on a light one
#   paused   the outline only, dimmed (as the Mac's menu bar icon when paused)
#   problem  filled mouse in orange (the Mac tints it orange when something is wrong)
#
# Run it from Windows PowerShell 5.1 or PowerShell 7 on Windows (it uses System.Drawing):
#   powershell -ExecutionPolicy Bypass -File windows/src/CalmMouse.Win/Assets/Tray/make-icons.ps1
# The .ico files are checked in; run this again only to change the drawing.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Per size: body left edge and top edge (the body is centred, so right = size - left and
# bottom = size - top), the wheel's width and its top and bottom, and the outline's pen width.
$metrics = @(
    @{ Size = 16; X = 4;  Y = 1; WheelW = 2; WheelTop = 3;  WheelBottom = 7;  Pen = 1 },
    @{ Size = 20; X = 5;  Y = 1; WheelW = 2; WheelTop = 4;  WheelBottom = 9;  Pen = 1 },
    @{ Size = 24; X = 6;  Y = 2; WheelW = 2; WheelTop = 5;  WheelBottom = 10; Pen = 2 },
    @{ Size = 28; X = 7;  Y = 2; WheelW = 2; WheelTop = 5;  WheelBottom = 11; Pen = 2 },
    @{ Size = 32; X = 8;  Y = 2; WheelW = 4; WheelTop = 6;  WheelBottom = 13; Pen = 2 },
    @{ Size = 40; X = 10; Y = 3; WheelW = 4; WheelTop = 8;  WheelBottom = 16; Pen = 3 },
    @{ Size = 48; X = 12; Y = 3; WheelW = 4; WheelTop = 9;  WheelBottom = 19; Pen = 3 }
)

# "on-dark" icons go on a dark taskbar (SystemUsesLightTheme = 0), "on-light" on a light one.
$themes = @{
    'on-dark'  = @{ Ink = [System.Drawing.Color]::FromArgb(255, 255, 255, 255); Problem = [System.Drawing.Color]::FromArgb(255, 0xFF, 0x8C, 0x00) }
    'on-light' = @{ Ink = [System.Drawing.Color]::FromArgb(255, 0x1A, 0x1A, 0x1A); Problem = [System.Drawing.Color]::FromArgb(255, 0xCA, 0x50, 0x10) }
}

function New-Capsule([single]$x, [single]$y, [single]$w, [single]$h) {
    # A rounded rectangle whose corners are half its width: a mouse seen from above.
    $r = $w / 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $w, $w, 180, 180)
    $path.AddLine($x + $w, $y + $r, $x + $w, $y + $h - $r)
    $path.AddArc($x, $y + $h - $w, $w, $w, 0, 180)
    $path.CloseFigure()
    return $path
}

function Draw-Glyph($m, [string]$state, $theme) {
    $s = $m.Size
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.Clear([System.Drawing.Color]::Transparent)

    $x = $m.X; $y = $m.Y; $w = $s - 2 * $m.X; $h = $s - 2 * $m.Y
    $wheelX = ($s - $m.WheelW) / 2
    $wheelH = $m.WheelBottom - $m.WheelTop

    switch ($state) {
        'paused' {
            $ink = [System.Drawing.Color]::FromArgb(150, $theme.Ink.R, $theme.Ink.G, $theme.Ink.B)
            $half = $m.Pen / 2
            $outline = New-Capsule ($x + $half) ($y + $half) ($w - $m.Pen) ($h - $m.Pen)
            $pen = New-Object System.Drawing.Pen($ink, $m.Pen)
            $g.DrawPath($pen, $outline)
            $brush = New-Object System.Drawing.SolidBrush($ink)
            $g.FillPath($brush, (New-Capsule $wheelX $m.WheelTop $m.WheelW $wheelH))
        }
        default {
            $ink = if ($state -eq 'problem') { $theme.Problem } else { $theme.Ink }
            $brush = New-Object System.Drawing.SolidBrush($ink)
            $g.FillPath($brush, (New-Capsule $x $y $w $h))
            # The wheel is cut out of the body.
            $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $clear = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)
            $g.FillPath($clear, (New-Capsule $wheelX $m.WheelTop $m.WheelW $wheelH))
        }
    }
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    # One ICO frame as a classic DIB: BITMAPINFOHEADER, 32-bit BGRA rows bottom-up (straight
    # alpha), then a 1-bit AND mask (all zero: the alpha channel says what's transparent).
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int32]0)
    $maskRow = [int]([math]::Ceiling($s / 32.0) * 4)
    $bw.Write([int32]($s * $s * 4 + $maskRow * $s))
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0)
    for ($row = $s - 1; $row -ge 0; $row--) {
        for ($col = 0; $col -lt $s; $col++) {
            $c = $bmp.GetPixel($col, $row)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    return ,$ms.ToArray()
}

function Write-Ico([string]$path, $frames) {
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $bw.Write([byte]$f.Size); $bw.Write([byte]$f.Size); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int32]$f.Bytes.Length); $bw.Write([int32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $bw.Write([byte[]]$f.Bytes) }
    $bw.Dispose()
}

foreach ($themeName in $themes.Keys) {
    foreach ($state in 'normal', 'paused', 'problem') {
        $frames = foreach ($m in $metrics) {
            $bmp = Draw-Glyph $m $state $themes[$themeName]
            @{ Size = $m.Size; Bytes = [byte[]](Get-DibBytes $bmp) }
            $bmp.Dispose()
        }
        $out = Join-Path $PSScriptRoot "$state-$themeName.ico"
        Write-Ico $out $frames
        Write-Host "wrote $out"
    }
}
