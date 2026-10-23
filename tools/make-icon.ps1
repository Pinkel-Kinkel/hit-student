# Draws the app icon (gradient tile with a graduation cap) and writes assets\app.ico with 16-256 px images.
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\app.ico'))
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $r = [float]($size * 0.22)
    $s = [float]$size
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($s - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2, $s - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $s - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(99, 102, 241)), ([System.Drawing.Color]::FromArgb(236, 72, 153))
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(99, 102, 241), [System.Drawing.Color]::FromArgb(139, 92, 246), [System.Drawing.Color]::FromArgb(236, 72, 153))
    $blend.Positions = @([float]0, [float]0.55, [float]1)
    $brush.InterpolationColors = $blend
    $g.FillPath($brush, $path)

    # Soft highlight
    $hl = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(36, 255, 255, 255))
    $g.FillEllipse($hl, -$s * 0.3, -$s * 0.55, $s * 1.1, $s * 0.95)

    # Graduation cap
    $white = [System.Drawing.Color]::White
    $u = $s / 24
    $cap = @(
        (New-Object System.Drawing.PointF ($u * 12), ($u * 5)),
        (New-Object System.Drawing.PointF ($u * 21.5), ($u * 9.6)),
        (New-Object System.Drawing.PointF ($u * 12), ($u * 14.2)),
        (New-Object System.Drawing.PointF ($u * 2.5), ($u * 9.6))
    )
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $white), $cap)
    $pen = New-Object System.Drawing.Pen $white, ([float]($u * 1.9))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawArc($pen, $u * 6.3, $u * 7.5, $u * 11.4, $u * 9.5, 15, 150)
    $g.DrawLine($pen, $u * 6.4, $u * 11.6, $u * 6.4, $u * 13.6)
    $g.DrawLine($pen, $u * 17.6, $u * 11.6, $u * 17.6, $u * 13.6)
    $g.DrawLine($pen, $u * 21.5, $u * 9.8, $u * 21.5, $u * 15)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(251, 191, 36))), $u * 20.2, $u * 14.4, $u * 2.6, $u * 2.6)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $b = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

# Also a large PNG for the README.
$big = Draw-Icon 512
$big.Save((Join-Path (Split-Path $Out) 'icon-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
Write-Host "Icon: $Out"
