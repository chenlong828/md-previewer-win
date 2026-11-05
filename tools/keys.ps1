# Send text / key chords to the WebView2 child window of the running app (acceptance helper).
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/keys.ps1 -TargetPid 1234 -Text "hello"
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/keys.ps1 -TargetPid 1234 -Chord "ctrl+s"
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [string]$Text,
    [string]$Chord
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Keys {
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr param);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    public delegate bool EnumProc(IntPtr h, IntPtr p);
}
'@

$proc = Get-Process -Id $TargetPid -ErrorAction Stop
$main = $proc.MainWindowHandle
if ($main -eq [IntPtr]::Zero) { throw "main window not found" }
[Keys]::SetForegroundWindow($main) | Out-Null
Start-Sleep -Milliseconds 300

$script:webview = [IntPtr]::Zero
$cb = [Keys+EnumProc]{
    param($h, $p)
    $sb = New-Object System.Text.StringBuilder 256
    [Keys]::GetClassName($h, $sb, 256) | Out-Null
    if ($sb.ToString() -like 'Chrome_WidgetWin*') { $script:webview = $h; return $false }
    return $true
}
[Keys]::EnumChildWindows($main, $cb, [IntPtr]::Zero) | Out-Null
if ($script:webview -eq [IntPtr]::Zero) { throw "webview child window not found" }

if ($Text) {
    foreach ($ch in $Text.ToCharArray()) {
        [Keys]::PostMessage($script:webview, 0x0102, [IntPtr][int]$ch, [IntPtr]::Zero) | Out-Null   # WM_CHAR
        Start-Sleep -Milliseconds 15
    }
}

if ($Chord) {
    $parts = $Chord.Split('+')
    $key = $parts[-1].ToLower()
    $vk = switch ($key) {
        's' { 0x53 }; 'o' { 0x4F }; 'n' { 0x4E }; 'w' { 0x57 }; 'f' { 0x46 }
        'z' { 0x5A }; 'y' { 0x59 }; 'a' { 0x41 }; 'c' { 0x43 }; 'v' { 0x56 }
        '/' { 0xBF }; '\' { 0xDC }; '=' { 0xBB }; '+' { 0xBB }; '-' { 0xBD }; '0' { 0x30 }
        default { 0 }
    }
    if ($vk -eq 0) { throw "unsupported key '$key'" }

    $mods = @{ 'ctrl' = 0x11; 'shift' = 0x10; 'alt' = 0x12 }
    foreach ($part in $parts[0..($parts.Length - 2)]) {
        if ($part -and $mods.ContainsKey($part.ToLower())) {
            [Keys]::keybd_event([byte]$mods[$part.ToLower()], 0, 0, [UIntPtr]::Zero)
        }
    }
    [Keys]::PostMessage($script:webview, 0x0100, [IntPtr]$vk, [IntPtr]::Zero) | Out-Null   # WM_KEYDOWN
    Start-Sleep -Milliseconds 40
    [Keys]::PostMessage($script:webview, 0x0101, [IntPtr]$vk, [IntPtr]::Zero) | Out-Null   # WM_KEYUP
    foreach ($part in $parts[0..($parts.Length - 2)]) {
        if ($part -and $mods.ContainsKey($part.ToLower())) {
            [Keys]::keybd_event([byte]$mods[$part.ToLower()], 0, 2, [UIntPtr]::Zero)        # KEYEVENTF_KEYUP
        }
    }
}

"sent to pid $TargetPid (webview hwnd $($script:webview))"
