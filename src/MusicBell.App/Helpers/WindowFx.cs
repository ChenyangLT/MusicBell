using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MusicBell.App.Helpers;

/// <summary>窗口外观：Win11 圆角、深色/浅色边框。</summary>
public static class WindowFx
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>圆角 + 边线颜色随主题。失败就直接忽略（Win10 上没有这些属性）。</summary>
    public static void Apply(Window window, bool light)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));

            // 边线：深色用近黑，浅色用浅灰，避免出现一条突兀的白线
            var border = light ? 0xE3E7F0 : 0x1C2129;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch
        {
            // 忽略
        }
    }
}
