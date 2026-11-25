using System.Diagnostics;
using System.Text;
using MusicBell.Core.Interop;

namespace MusicBell.Core.Services;

/// <summary>找到/拉起网易云窗口，并在搜索框里输入。</summary>
public static class NetEaseAutomation
{
    public const string WindowClass = "OrpheusBrowserHost";
    public const string ProcessName = "cloudmusic";

    /// <summary>枚举所有 OrpheusBrowserHost 窗口，返回面积最大的那个。</summary>
    public static IntPtr FindMainWindow()
    {
        var best = IntPtr.Zero;
        long bestArea = 0;
        try
        {
            Win32.EnumWindows((h, _) =>
            {
                var sb = new StringBuilder(256);
                Win32.GetClassName(h, sb, sb.Capacity);
                if (!string.Equals(sb.ToString(), WindowClass, StringComparison.Ordinal)) return true;
                if (!Win32.IsWindowVisible(h)) return true;
                if (!Win32.GetWindowRect(h, out var r)) return true;
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { AppLog.Warn("枚举窗口失败：" + ex.Message); }
        return best;
    }

    public static bool IsRunning()
        => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>确保网易云在运行并返回主窗口句柄（失败返回 IntPtr.Zero）。</summary>
    public static async Task<IntPtr> EnsureWindowAsync(string exePath, int timeoutMs = 25000, Action<string>? log = null)
    {
        var hwnd = FindMainWindow();
        if (hwnd != IntPtr.Zero)
        {
            log?.Invoke("网易云窗口已就绪");
            return hwnd;
        }

        log?.Invoke("启动网易云：" + exePath);
        try
        {
            if (File.Exists(exePath))
                Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo { FileName = "cloudmusic://", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("启动网易云失败", ex);
            log?.Invoke("启动网易云失败：" + ex.Message);
            return IntPtr.Zero;
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(400).ConfigureAwait(false);
            hwnd = FindMainWindow();
            if (hwnd != IntPtr.Zero)
            {
                // 冷启动时界面还在加载，多等一会儿
                await Task.Delay(1800).ConfigureAwait(false);
                log?.Invoke($"网易云已启动（{sw.ElapsedMilliseconds} ms）");
                return hwnd;
            }
        }

        log?.Invoke("等待网易云窗口超时");
        return IntPtr.Zero;
    }

    /// <summary>把窗口带到前台。</summary>
    public static bool BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
            else Win32.ShowWindow(hwnd, Win32.SW_SHOW);

            var fg = Win32.GetForegroundWindow();
            if (fg == hwnd) return true;

            var cur = Win32.GetCurrentThreadId();
            var fgThread = Win32.GetWindowThreadProcessId(fg, IntPtr.Zero);
            var ok = false;

            if (fgThread != cur) Win32.AttachThreadInput(cur, fgThread, true);
            try
            {
                Win32.BringWindowToTop(hwnd);
                Win32.SetForegroundWindow(hwnd);
                Win32.SetFocus(hwnd);
                ok = Win32.GetForegroundWindow() == hwnd;
                if (!ok)
                {
                    // 前台锁兜底：轻敲一下 Alt 再试
                    Win32.keybd_event(Win32.VK_MENU, 0, 0, IntPtr.Zero);
                    Win32.keybd_event(Win32.VK_MENU, 0, Win32.KEYEVENTF_KEYUP, IntPtr.Zero);
                    Win32.SwitchToThisWindow(hwnd, true);
                    Win32.SetForegroundWindow(hwnd);
                    ok = Win32.GetForegroundWindow() == hwnd;
                }
            }
            finally
            {
                if (fgThread != cur) Win32.AttachThreadInput(cur, fgThread, false);
            }

            Thread.Sleep(120);
            return ok || Win32.GetForegroundWindow() == hwnd;
        }
        catch (Exception ex)
        {
            AppLog.Warn("窗口置前失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>按比例算出客户区里的像素坐标。</summary>
    public static (int X, int Y) SearchBoxPoint(IntPtr hwnd, double ratioX, double ratioY, int offsetX, int offsetY)
    {
        if (!Win32.GetClientRect(hwnd, out var rect)) return (0, 0);
        var pt = new Win32.POINT
        {
            X = (int)Math.Round(rect.Right * ratioX) + offsetX,
            Y = (int)Math.Round(rect.Bottom * ratioY) + offsetY
        };
        Win32.ClientToScreen(hwnd, ref pt);
        return (pt.X, pt.Y);
    }

    public static void Click(int x, int y)
    {
        Win32.SetCursorPos(x, y);
        Thread.Sleep(60);
        Win32.mouse_event(Win32.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(50);
        Win32.mouse_event(Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
    }

    public static void KeyCombo(byte modifier, byte key)
    {
        Win32.keybd_event(modifier, 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        Win32.keybd_event(key, 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        Win32.keybd_event(key, 0, Win32.KEYEVENTF_KEYUP, IntPtr.Zero);
        Win32.keybd_event(modifier, 0, Win32.KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    public static void PressEnter()
    {
        Win32.keybd_event(Win32.VK_RETURN, 0, 0, IntPtr.Zero);
        Thread.Sleep(40);
        Win32.keybd_event(Win32.VK_RETURN, 0, Win32.KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    /// <summary>清空搜索框 → 粘贴关键词 → 回车。</summary>
    public static bool SearchInBox(IntPtr hwnd, string keyword, double ratioX, double ratioY,
        int offsetX, int offsetY, int keyDelayMs, bool restoreClipboard, Action<string>? log = null)
    {
        var (x, y) = SearchBoxPoint(hwnd, ratioX, ratioY, offsetX, offsetY);
        log?.Invoke($"点击搜索框 ({x},{y})");
        Click(x, y);
        Thread.Sleep(Math.Max(120, keyDelayMs));

        KeyCombo(Win32.VK_CONTROL, Win32.VK_A);
        Thread.Sleep(keyDelayMs);

        var oldClip = restoreClipboard ? ClipboardHelper.TryGetText() : null;
        if (!ClipboardHelper.TrySetText(keyword))
        {
            log?.Invoke("写剪贴板失败");
            return false;
        }
        Thread.Sleep(60);

        KeyCombo(Win32.VK_CONTROL, Win32.VK_V);
        Thread.Sleep(keyDelayMs);
        PressEnter();
        log?.Invoke("已输入并回车：" + keyword);

        Thread.Sleep(140);
        if (restoreClipboard && oldClip is not null) ClipboardHelper.TrySetText(oldClip);
        return true;
    }

    public static void DoubleClick(int x, int y)
    {
        Win32.SetCursorPos(x, y);
        Thread.Sleep(60);
        for (var i = 0; i < 2; i++)
        {
            Win32.mouse_event(Win32.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            Thread.Sleep(30);
            Win32.mouse_event(Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            if (i == 0) Thread.Sleep(85);
        }
    }

    /// <summary>按客户区比例算屏幕坐标。</summary>
    public static (int X, int Y) ClientPoint(IntPtr hwnd, double ratioX, double ratioY)
    {
        if (!Win32.GetClientRect(hwnd, out var rect)) return (0, 0);
        var pt = new Win32.POINT
        {
            X = (int)Math.Round(rect.Right * ratioX),
            Y = (int)Math.Round(rect.Bottom * ratioY)
        };
        Win32.ClientToScreen(hwnd, ref pt);
        return (pt.X, pt.Y);
    }

    /// <summary>双击搜索结果第一条，让它真的播起来。</summary>
    public static bool PlayFirstResult(IntPtr hwnd, double ratioX, double ratioY, Action<string>? log = null)
    {
        if (hwnd == IntPtr.Zero) return false;
        var (x, y) = ClientPoint(hwnd, ratioX, ratioY);
        log?.Invoke($"双击第一条结果 ({x},{y})");
        DoubleClick(x, y);
        Thread.Sleep(700);
        return true;
    }

    /// <summary>点底部播放条的封面，进歌曲详情（歌词）页。</summary>
    public static bool OpenNowPlaying(IntPtr hwnd, double ratioX, double ratioY, Action<string>? log = null)
    {
        if (hwnd == IntPtr.Zero) return false;
        var (x, y) = ClientPoint(hwnd, ratioX, ratioY);
        log?.Invoke($"点底部播放条 ({x},{y})");
        Click(x, y);
        Thread.Sleep(500);
        return true;
    }

    /// <summary>关掉网易云（= 结束进程）。</summary>
    public static bool CloseNetEase(Action<string>? log = null)
    {
        try
        {
            var procs = Process.GetProcessesByName(ProcessName);
            if (procs.Length == 0)
            {
                log?.Invoke("网易云本来就没开");
                return true;
            }
            foreach (var p in procs)
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(2500)) p.Kill(true);
                }
                catch { try { p.Kill(true); } catch { } }
            }
            log?.Invoke("已关闭网易云");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("关闭网易云失败：" + ex.Message);
            log?.Invoke("关闭网易云失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>用 orpheus 协议直接打开歌曲或搜索。</summary>
    public static bool OpenUrl(string url, Action<string>? log = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            log?.Invoke("已调用协议：" + url);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("打开协议失败：" + url, ex);
            return false;
        }
    }
}
