# Bring a window to the top so screenshots capture it instead of whatever is in front.
#   powershell -File tools\focus.ps1 -TargetPid 1234
# Uses SetWindowPos TOPMOST then back to NOTOPMOST (no lasting z-order change).
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [switch]$KeepTopmost
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FocusWin {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@

$proc = Get-Process -Id $TargetPid -ErrorAction Stop
$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Error "no main window for pid $TargetPid"; exit 1 }

if ([FocusWin]::IsIconic($h)) { [FocusWin]::ShowWindow($h, 9) | Out-Null }   # SW_RESTORE
[FocusWin]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0003) | Out-Null   # HWND_TOPMOST, NOMOVE|NOSIZE
[FocusWin]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
if (-not $KeepTopmost) {
    [FocusWin]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x0003) | Out-Null  # HWND_NOTOPMOST
}
Write-Output "focused pid=$TargetPid hwnd=$h"
