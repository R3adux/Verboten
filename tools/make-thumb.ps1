# Draws the Workshop/mod-browser picture: a German "BETRETEN VERBOTEN" sign (own drawing, no third-party images).
# Usage (Windows PowerShell):  powershell -ExecutionPolicy Bypass -File tools\make-thumb.ps1
# Writes About\thumb.png (Steam Workshop, must stay below 1 MB) and About\Preview.png (in-game mod browser).
param([int]$Size = 1024)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Color([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

# Text fitted to a width, centered on (cx, cy)
function DrawFitted($g, [string]$text, [string]$family, [System.Drawing.FontStyle]$style, [float]$maxWidth, [float]$maxSize, [float]$cx, [float]$cy, $brush) {
    $ff = New-Object System.Drawing.FontFamily($family)
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $size = $maxSize
    while ($size -gt 8) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding   # overlapping glyph contours must not leave holes
        $path.AddString($text, $ff, [int]$style, $size, (New-Object System.Drawing.PointF(0, 0)), $fmt)
        $b = $path.GetBounds()
        if ($b.Width -le $maxWidth) { break }
        $path.Dispose()
        $size -= 2
    }
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($cx - $b.X - $b.Width / 2, $cy - $b.Y - $b.Height / 2)
    $path.Transform($m)
    $g.FillPath($brush, $path)
    $path.Dispose()
}

$S = 1024                                   # drawn at 1024, scaled on save
$bmp = New-Object System.Drawing.Bitmap($S, $S)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.PixelOffsetMode = 'HighQuality'
$g.InterpolationMode = 'HighQualityBicubic'
$g.TextRenderingHint = 'AntiAliasGridFit'

# ---- backdrop: dusk over a red planet
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point(0, $S)), (Color '#0f1218'), (Color '#4a2414'))
$g.FillRectangle($bg, 0, 0, $S, $S)
$rnd = New-Object System.Random(7)
for ($i = 0; $i -lt 90; $i++) {                # a few stars in the upper part
    $x = $rnd.Next(0, $S); $y = $rnd.Next(0, [int]($S * 0.55)); $r = $rnd.NextDouble() * 2.2 + 0.6
    $g.FillEllipse((New-Object System.Drawing.SolidBrush((Color '#ffffff' ([int](60 + $rnd.Next(0, 140)))))), [float]$x, [float]$y, [float]$r, [float]$r)
}

# ---- the sign
$sx = 192; $sy = 32; $sw = 640; $sh = 960
$shadow = RoundRect ($sx + 14) ($sy + 16) $sw $sh 44
$g.FillPath((New-Object System.Drawing.SolidBrush((Color '#000000' 110))), $shadow)
$sign = RoundRect $sx $sy $sw $sh 44
$g.FillPath((New-Object System.Drawing.SolidBrush((Color '#f6f5f0'))), $sign)
$red = Color '#d71f26'
$border = RoundRect ($sx + 26) ($sy + 26) ($sw - 52) ($sh - 52) 28
$g.DrawPath((New-Object System.Drawing.Pen($red, 26)), $border)
foreach ($p in @(@(($sx + 44), ($sy + 44)), @(($sx + $sw - 44), ($sy + 44)), @(($sx + 44), ($sy + $sh - 44)), @(($sx + $sw - 44), ($sy + $sh - 44)))) {
    $g.FillEllipse((New-Object System.Drawing.SolidBrush((Color '#b9b9b9'))), [float]($p[0] - 8), [float]($p[1] - 8), 16, 16)
}

# ---- prohibition sign with a raised hand
$cx = 512; $cy = 332; $R = 238; $ring = 46
$g.FillEllipse((New-Object System.Drawing.SolidBrush($red)), [float]($cx - $R), [float]($cy - $R), [float](2 * $R), [float](2 * $R))
$ri = $R - $ring
$inner = New-Object System.Drawing.Drawing2D.GraphicsPath
$inner.AddEllipse([float]($cx - $ri), [float]($cy - $ri), [float](2 * $ri), [float](2 * $ri))
$g.FillPath((New-Object System.Drawing.SolidBrush((Color '#f6f5f0'))), $inner)
$g.SetClip($inner)
$k = [Math]::Cos([Math]::PI / 4) * $ri
$g.DrawLine((New-Object System.Drawing.Pen($red, $ring)), [float]($cx - $k - 20), [float]($cy - $k - 20), [float]($cx + $k + 20), [float]($cy + $k + 20))

# hand (own drawing): palm, four fingers, thumb, wrist – slightly tilted
$black = Color '#1b1b1b'
$state = $g.Save()
$g.TranslateTransform(526, 362)
$g.ScaleTransform(0.92, 0.92)
$g.RotateTransform(-8)
$hb = New-Object System.Drawing.SolidBrush($black)
$finger = New-Object System.Drawing.Pen($black, 44)
$finger.StartCap = 'Round'; $finger.EndCap = 'Round'
$g.FillRectangle($hb, -58, 90, 112, 200)                     # wrist (cut by the ring)
$g.FillPath($hb, (RoundRect -86 -46 172 170 58))               # palm
$g.DrawLine($finger, -62, -18, -62, -158)                       # index
$g.DrawLine($finger, -16, -18, -16, -186)                       # middle
$g.DrawLine($finger, 30, -18, 30, -172)                         # ring
$little = New-Object System.Drawing.Pen($black, 38)
$little.StartCap = 'Round'; $little.EndCap = 'Round'
$g.DrawLine($little, 72, -4, 76, -118)                          # little finger
$thumb = New-Object System.Drawing.Pen($black, 46)
$thumb.StartCap = 'Round'; $thumb.EndCap = 'Round'
$g.DrawLine($thumb, -74, 46, -142, -34)                         # thumb
$g.Restore($state)
$g.ResetClip()

# ---- text
$ink = New-Object System.Drawing.SolidBrush((Color '#1d1d1b'))
DrawFitted $g 'BETRETEN' 'Bahnschrift' ([System.Drawing.FontStyle]::Bold) 470 190 $cx 655 $ink
DrawFitted $g 'VERBOTEN' 'Bahnschrift' ([System.Drawing.FontStyle]::Bold) 470 190 $cx 805 $ink
DrawFitted $g 'Vertrauen ist gut, Verboten ist besser.' 'Bahnschrift SemiLight' ([System.Drawing.FontStyle]::Italic) 480 40 $cx 905 (New-Object System.Drawing.SolidBrush((Color '#55534f')))

$g.Dispose()

# ---- save
$about = Join-Path (Split-Path -Parent $PSScriptRoot) 'About'
function SaveScaled($src, [int]$size, [string]$file) {
    $out = New-Object System.Drawing.Bitmap($size, $size)
    $gg = [System.Drawing.Graphics]::FromImage($out)
    $gg.InterpolationMode = 'HighQualityBicubic'
    $gg.PixelOffsetMode = 'HighQuality'
    $gg.DrawImage($src, 0, 0, $size, $size)
    $gg.Dispose()
    $out.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
    "{0}: {1} KB" -f $file, [int]((Get-Item $file).Length / 1024)
}
SaveScaled $bmp $Size (Join-Path $about 'thumb.png')
SaveScaled $bmp $Size (Join-Path $about 'Preview.png')
$bmp.Dispose()
