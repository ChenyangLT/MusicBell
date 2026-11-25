using System.Runtime.InteropServices;

namespace MusicBell.Core.Interop;

/// <summary>剪贴板读写（Win32 原生，任意线程可用）。</summary>
public static class ClipboardHelper
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);

    public static string? TryGetText()
    {
        for (var i = 0; i < 6; i++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(45); continue; }
            try
            {
                var handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                var ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }
        return null;
    }

    public static bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(50); continue; }
            try
            {
                EmptyClipboard();
                var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                if (hGlobal == IntPtr.Zero) return false;
                var target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero) { GlobalFree(hGlobal); return false; }
                try { Marshal.Copy(bytes, 0, target, bytes.Length); }
                finally { GlobalUnlock(hGlobal); }

                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    continue;
                }
                return true;
            }
            finally { CloseClipboard(); }
        }
        return false;
    }
}
