# Draws the app icons into src/PhoneMic/Assets. Run from Windows PowerShell:
#   powershell -ExecutionPolicy Bypass -File tools/make-icons.ps1
# The results are committed, so this only needs rerunning after changing the design.

Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\PhoneMic\Assets"
New-Item -ItemType Directory -Force $out | Out-Null

function Draw-Mic([int]$size, [System.Drawing.Color]$bg, [System.Drawing.Color]$fg) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    $g.FillEllipse((New-Object System.Drawing.SolidBrush $bg), 4, 4, 248, 248)

    # Capsule
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 56
    $path.AddArc(100, 48, $r, $r, 180, 180)
    $path.AddArc(100, 104, $r, $r, 0, 180)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $fg), $path)

    # Cradle, stem and base
    $pen = New-Object System.Drawing.Pen $fg, 18
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, 68, 64, 120, 120, 0, 180)
    $g.DrawLine($pen, 128, 184, 128, 208)
    $g.DrawLine($pen, 98, 208, 158, 208)

    $g.Dispose()
    return $bmp
}

function Save-Ico([string]$path, [System.Drawing.Color]$bg, [System.Drawing.Color]$fg) {
    $sizes = 16, 20, 24, 32, 48, 64, 256
    $pngs = foreach ($size in $sizes) {
        $bmp = Draw-Mic $size $bg $fg
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

$live = [System.Drawing.Color]::FromArgb(255, 0x3D, 0xDC, 0x97)
$dark = [System.Drawing.Color]::FromArgb(255, 0x0E, 0x11, 0x13)
$idle = [System.Drawing.Color]::FromArgb(255, 0x5A, 0x64, 0x6A)
$white = [System.Drawing.Color]::FromArgb(255, 0xE8, 0xEC, 0xEE)

Save-Ico (Join-Path $out "app.ico") $live $dark
Save-Ico (Join-Path $out "idle.ico") $idle $white
(Draw-Mic 256 $live $dark).Save((Join-Path $out "app.png"), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Icons written to $out"
