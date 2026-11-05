# 直接抓单个窗口（PrintWindow + PW_RENDERFULLCONTENT），窗口被遮挡也能取到内容
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\grab.ps1 -TargetPid 1234 -Path artifacts\shot.png
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [Parameter(Mandatory = $true)][string]$Path
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GrabWin {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

# 关键：先把本脚本进程设成 per-monitor DPI aware，否则 GetWindowRect 返回被系统缩放
# 虚拟化过的坐标，截图只会拿到窗口左上角一块（250% 缩放下尤其明显）。
try { [GrabWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null } catch { }

$proc = Get-Process -Id $TargetPid -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "no main window for pid $TargetPid"; exit 1 }
if ([GrabWin]::IsIconic($hwnd)) { [GrabWin]::ShowWindow($hwnd, 9) | Out-Null; Start-Sleep -Milliseconds 400 }

$rect = New-Object GrabWin+RECT
[GrabWin]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { Write-Error "bad window rect ${width}x${height}"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap($width, $height)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $gfx.GetHdc()
# PW_RENDERFULLCONTENT = 2：让 DWM 把（含 WebView2 子窗口的）完整内容画进来
$ok = [GrabWin]::PrintWindow($hwnd, $hdc, 2)
$gfx.ReleaseHdc($hdc)
$gfx.Dispose()

$out = Join-Path (Get-Location) $Path
New-Item -ItemType Directory -Force (Split-Path -Parent $out) | Out-Null
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("{0} {1}x{2} printWindow={3}" -f $out, $width, $height, $ok)
