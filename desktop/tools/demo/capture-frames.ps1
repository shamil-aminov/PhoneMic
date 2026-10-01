param([int]$ProcessId, [string]$OutDir, [double]$Seconds = 7.5)
# Grabs a window over and over with PrintWindow, naming each frame by where it
# falls in DemoVoice's 6-second loop (wall clock, UTC), for syncing with the phone.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[W2]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Force $OutDir | Out-Null
$h = (Get-Process -Id $ProcessId).MainWindowHandle
$r = New-Object W2+RECT
[W2]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$epoch = [DateTime]::new(1970, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
$end = [DateTime]::UtcNow.AddSeconds($Seconds)
$i = 0
while ([DateTime]::UtcNow -lt $end) {
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    $t0 = ([DateTime]::UtcNow - $epoch).TotalSeconds
    [W2]::PrintWindow($h, $dc, 2) | Out-Null
    $t1 = ([DateTime]::UtcNow - $epoch).TotalSeconds
    $g.ReleaseHdc($dc); $g.Dispose()
    $t = (($t0 + $t1) / 2) % 6
    $name = [string]::Format([cultureinfo]::InvariantCulture, "{0:D4}_{1:F3}.png", $i, $t)
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $i++
}
"$i frames, ${w}x${ht}"
