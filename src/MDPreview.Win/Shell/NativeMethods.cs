using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MDPreview.Win.Shell;

/// <summary>
/// Win11 外观：深色标题栏、圆角，并把标题栏/边框染成与主题一致的颜色。
/// 不用 Mica：正文是整页白纸，系统背景会从标题栏透出壁纸色，观感与 mac 版不一致；
/// Win10 上这些调用直接失败，忽略即可（保留系统默认标题栏）。
/// </summary>
internal static class NativeMethods
{
    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaBorderColor = 34;
    const int DwmwaCaptionColor = 35;
    const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void ApplyWin11Look(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var darkFlag = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkFlag, sizeof(int));

        var rounded = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref rounded, sizeof(int));

        var caption = dark ? 0x00202020 : 0x00F6F6F6; // COLORREF = 0x00BBGGRR
        var text = dark ? 0x00EAEAEA : 0x001A1A1A;
        var border = dark ? 0x002D2D2D : 0x00E3E3E3;
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
    }
}
