# Draws the app icons into src/PhoneMic/Assets. Run from Windows PowerShell:
#   powershell -ExecutionPolicy Bypass -File tools/make-icons.ps1
# The results are committed, so this only needs rerunning after changing the design.

Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\PhoneMic\Assets"
New-Item -ItemType Directory -Force $out | Out-Null

# The wave from the phone app (android/app/src/main/java/sh/aminov/phonemic/ui/Wave.kt),
# frozen at one moment: a ribbon of lines, each mixing two wave shapes. It is drawn
# twice, wide and faint for the glow, then thin. $fg, $fg2 and $fg3 are the
# gradient's cyan, blue and violet; pass one colour three times for a flat icon.
# The same drawing, as vectors, is the phone's launcher icon in
# android/app/src/main/res/drawable/ic_launcher_foreground.xml.
$Moment = 1.2
$Lines = 18

function Wave-Point([double]$x, [double]$theta) {
    $tau = 2 * [Math]::PI
    $t = $Moment
    $envelope = [Math]::Exp(-[Math]::Pow(($x - 0.5) / 0.25, 2))
    $f = $envelope * (0.62 * [Math]::Sin($x * $tau * 1.7 + $t * 1.8) + 0.38 * [Math]::Sin($x * $tau * 3.3 - $t * 2.5))
    $g = $envelope * (0.55 * [Math]::Sin($x * $tau * 2.4 - $t * 1.4 + 1.3) + 0.45 * [Math]::Sin($x * $tau * 4.6 + $t * 3.1))
    return [Math]::Cos($theta) * $f + [Math]::Sin($theta) * $g
}

function Wave-Pen([System.Drawing.Color[]]$colors, [double]$alpha, [double]$width) {
    $x0 = 256 * 0.04
    $x1 = 256 * 0.96
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF $x0, 0), (New-Object System.Drawing.PointF $x1, 0), $colors[0], $colors[0]
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 7
    # The ends fade out, as they do on the phone screen.
    $stops = @(0, 1, 1, 1, 1, 1, 0)
    $blend.Colors = [System.Drawing.Color[]]@(for ($i = 0; $i -lt 7; $i++) {
        [System.Drawing.Color]::FromArgb([int](255 * $alpha * $stops[$i]), $colors[$i])
    })
    $blend.Positions = [single[]](0, 0.2, 0.35, 0.5, 0.65, 0.8, 1)
    $brush.InterpolationColors = $blend
    $pen = New-Object System.Drawing.Pen $brush, $width
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Draw-Wave([int]$size, [System.Drawing.Color]$bg, [System.Drawing.Color]$fg, [System.Drawing.Color]$fg2, [System.Drawing.Color]$fg3) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    $g.FillEllipse((New-Object System.Drawing.SolidBrush $bg), 4, 4, 248, 248)
    $colors = [System.Drawing.Color[]]($fg, $fg, $fg2, $fg3, $fg2, $fg, $fg)

    # Glow first, then the lines; lines facing the viewer are brighter than the ones seen edge-on.
    foreach ($pass in @(@{ Width = 14; Alpha = 0.05 }, @{ Width = 3.2; Alpha = 0.55 })) {
        for ($i = 0; $i -lt $Lines; $i++) {
            $theta = [Math]::PI * $i / $Lines
            $facing = 0.45 + 0.55 * [Math]::Abs([Math]::Cos($theta))
            $points = for ($j = 0; $j -le 120; $j++) {
                $x = $j / 120.0
                New-Object System.Drawing.PointF ([single](256 * (0.04 + 0.92 * $x))), ([single](128 + 76.8 * (Wave-Point $x $theta)))
            }
            # 1.0, not 1: with an integer PowerShell picks Math.Min(int, int) and rounds the alpha to 0 or 1.
            $pen = Wave-Pen $colors ([Math]::Min(1.0, $pass.Alpha * $facing)) $pass.Width
            $g.DrawLines($pen, [System.Drawing.PointF[]]$points)
            $pen.Dispose()
        }
    }

    $g.Dispose()
    return $bmp
}

function Save-Ico([string]$path, [System.Drawing.Color]$bg, [System.Drawing.Color]$fg, [System.Drawing.Color]$fg2, [System.Drawing.Color]$fg3) {
    $sizes = 16, 20, 24, 32, 48, 64, 256
    $pngs = foreach ($size in $sizes) {
        $bmp = Draw-Wave $size $bg $fg $fg2 $fg3
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Close()
}

$black = [System.Drawing.Color]::FromArgb(255, 0, 0, 0)
$cyan = [System.Drawing.Color]::FromArgb(255, 0x22, 0xD3, 0xEE)
$blue = [System.Drawing.Color]::FromArgb(255, 0x60, 0xA5, 0xFA)
$violet = [System.Drawing.Color]::FromArgb(255, 0xA7, 0x8B, 0xFA)
$grey = [System.Drawing.Color]::FromArgb(255, 0x8B, 0x93, 0xA1)
$ring = [System.Drawing.Color]::FromArgb(255, 0x1B, 0x1E, 0x24)

Save-Ico (Join-Path $out "app.ico") $black $cyan $blue $violet
Save-Ico (Join-Path $out "idle.ico") $ring $grey $grey $grey
(Draw-Wave 256 $black $cyan $blue $violet).Save((Join-Path $out "app.png"), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Icons written to $out"
