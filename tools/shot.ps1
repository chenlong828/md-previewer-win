# Capture a window (or the whole screen) for debugging.
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/shot.ps1 -TargetPid 1234 -Path artifacts/shot.png
#   ... -Move 200,150 -Size 1400,900      place the window first (pixels, DPI-aware)
#   ... -FullScreen                       capture the primary screen instead
param(
    [string]$Path = "artifacts/shot.png",
    [int]$TargetPid = 0,
    [string]$TitleLike = "MDPreview",
    [int[]]$Move,
    [int[]]$Size,
    [switch]$FullScreen
)

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public static class Win32Shot {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@

[Win32Shot]::SetProcessDPIAware() | Out-Null

$proc = if ($TargetPid -gt 0) {
    Get-Process -Id $TargetPid -ErrorAction Stop
} else {
    Get-Process | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
}
if (-not $proc) { throw "no target window found" }

$handle = $proc.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw "window handle is empty" }

[Win32Shot]::ShowWindow($handle, 9) | Out-Null
if ($Move -and $Size) {
    # SWP_NOZORDER(0x4) | SWP_NOACTIVATE(0x10)
    [Win32Shot]::SetWindowPos($handle, [IntPtr]::Zero, $Move[0], $Move[1], $Size[0], $Size[1], 0x14) | Out-Null
    Start-Sleep -Milliseconds 500
}
[Win32Shot]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 800

$rect = New-Object RECT

if ($FullScreen) {
    Add-Type -AssemblyName System.Windows.Forms
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $rect.Left = $screen.Left; $rect.Top = $screen.Top
    $rect.Right = $screen.Right; $rect.Bottom = $screen.Bottom
} else {
    # DWMWA_EXTENDED_FRAME_BOUNDS(9)：去掉不可见的调整边框/阴影，只取可见窗口
    $dwm = New-Object RECT
    $ok = [Win32Shot]::DwmGetWindowAttribute($handle, 9, [ref]$dwm, [System.Runtime.InteropServices.Marshal]::SizeOf($dwm))
    if ($ok -eq 0 -and ($dwm.Right -gt $dwm.Left)) {
        $rect = $dwm
    } else {
        [Win32Shot]::GetWindowRect($handle, [ref]$rect) | Out-Null
    }
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "invalid capture size $width x $height" }

$full = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
$dir = [System.IO.Path]::GetDirectoryName($full)
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
$bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

$kb = [math]::Round((Get-Item $full).Length / 1kb)
"$full ${width}x${height} at ($($rect.Left),$($rect.Top)) ${kb}KB"
