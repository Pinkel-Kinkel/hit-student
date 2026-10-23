# Starts the built app in demo mode on a given screen and saves a screenshot of its window.
# Example: tools\screenshot.ps1 -Out home.png -DemoArgs '--demo-view=home'
param([string]$Out, [string[]]$DemoArgs = @(), [int]$Wait = 6, [string]$Exe = (Join-Path $PSScriptRoot '..\dist\HIT Student\HitStudent.exe'))
Add-Type -AssemblyName System.Drawing
if (-not ('ShotNative' -as [type])) {
    Add-Type @'
using System; using System.Runtime.InteropServices;
public static class ShotNative {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }
}
'@
}
$p = Start-Process $Exe -ArgumentList (@('--demo') + $DemoArgs) -PassThru
Start-Sleep -Seconds $Wait
$p.Refresh()
$r = New-Object ShotNative+RECT
[ShotNative]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc(); [ShotNative]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Stop-Process -Id $p.Id -Force
Write-Host "Saved $Out"
