using System.Diagnostics;
using System.Text.Json;
using MusicBell.Core.Interop;

namespace MusicBell.Core.Services;

/// <summary>B 站标签页登记：开的时候记下标题，关闭时按标题找到那一个标签页，只发 Ctrl+W，不动整个浏览器。</summary>
public static class BrowserTabs
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] BrowserNames =
    {
        "msedge", "chrome", "firefox", "brave", "vivaldi", "opera", "browser", "360se", "360chrome", "QQBrowser", "SogouExplorer", "Thorium"
    };

    public sealed class Tab
    {
        public string Url { get; set; } = "";
        public string Hint { get; set; } = "";
        public DateTime OpenedAt { get; set; } = DateTime.Now;
    }

    private static string FilePath(string dataDir) => Path.Combine(dataDir, "bili-tabs.json");

    public static List<Tab> Load(string dataDir)
    {
        try
        {
            var path = FilePath(dataDir);
            if (!File.Exists(path)) return new List<Tab>();
            return JsonSerializer.Deserialize<List<Tab>>(File.ReadAllText(path), Json) ?? new List<Tab>();
        }
        catch { return new List<Tab>(); }
    }

    public static void Save(string dataDir, List<Tab> tabs)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            if (tabs.Count == 0)
            {
                if (File.Exists(FilePath(dataDir))) File.Delete(FilePath(dataDir));
                return;
            }
            File.WriteAllText(FilePath(dataDir), JsonSerializer.Serialize(tabs, Json));
        }
        catch (Exception ex) { AppLog.Warn("保存 B 站标签页记录失败：" + ex.Message); }
    }

    /// <summary>用默认浏览器打开 B 站视频，并记下这一页的标题特征。</summary>
    public static async Task<bool> OpenAsync(string dataDir, string url, string titleHint,
        bool maximize = true, bool pageFullscreen = false, Action<string>? log = null)
    {
        try
        {
            log?.Invoke("在浏览器里打开：" + url);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log?.Invoke("打开浏览器失败：" + ex.Message);
            return false;
        }

        var hint = MakeHint(titleHint, url);
        var found = await WaitForTitleAsync(hint, 12000).ConfigureAwait(false);
        if (maximize || pageFullscreen) await FitBrowserAsync(hint, maximize, pageFullscreen, log).ConfigureAwait(false);
        var tabs = Load(dataDir);
        tabs.RemoveAll(t => string.Equals(t.Hint, hint, StringComparison.Ordinal));
        tabs.Add(new Tab { Url = url, Hint = found ? hint : hint, OpenedAt = DateTime.Now });
        Save(dataDir, tabs);
        log?.Invoke(found ? "已登记 B 站标签页：" + hint : "已经打开，但没读到标签页标题（关闭时按关键词再找）");
        return true;
    }

    private static string MakeHint(string titleHint, string url)
    {
        var hint = (titleHint ?? "").Trim();
        foreach (var bad in new[] { "【", "】", "_哔哩哔哩", "哔哩哔哩_bilibili", "|", "：", ":" })
            hint = hint.Replace(bad, " ");
        hint = string.Join(" ", hint.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (hint.Length > 12) hint = hint[..12];
        if (string.IsNullOrWhiteSpace(hint))
        {
            var bvid = ExtractBvid(url);
            hint = string.IsNullOrEmpty(bvid) ? "bilibili" : bvid;
        }
        return hint.Trim();
    }

    private static string ExtractBvid(string url)
    {
        var idx = url.IndexOf("BV", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var end = idx;
        while (end < url.Length && (char.IsLetterOrDigit(url[end]))) end++;
        return url[idx..end];
    }

    /// <summary>
    /// 把浏览器窗口放到最大；需要时再按一次 F11 让页面全屏。
    /// 这里用的是 ShowWindow / 快捷键这类系统调用，不往网页里乱点，所以不会被页面布局带偏。
    /// </summary>
    private static async Task FitBrowserAsync(string hint, bool maximize, bool fullscreen, Action<string>? log)
    {
        var hwnd = IntPtr.Zero;
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < 6000)
        {
            var windows = FindWindows(hint);
            if (windows.Count > 0) { hwnd = windows[0]; break; }
            await Task.Delay(300).ConfigureAwait(false);
        }
        if (hwnd == IntPtr.Zero)
        {
            log?.Invoke("没找到浏览器窗口，跳过最大化");
            return;
        }

        if (maximize)
        {
            Win32.ShowWindow(hwnd, 3); // SW_MAXIMIZE
            log?.Invoke("浏览器窗口已最大化");
            await Task.Delay(450).ConfigureAwait(false);
        }

        if (!fullscreen) return;

        // F11 只在浏览器自己在前台时才发，免得全屏落到别的程序上。
        Win32.SetForegroundWindow(hwnd);
        await Task.Delay(260).ConfigureAwait(false);
        if (Win32.GetForegroundWindow() != hwnd)
        {
            log?.Invoke("浏览器没抢到前台，这次不按 F11");
            return;
        }
        const byte vkF11 = 0x7A;
        const uint keyUp = 0x0002;
        Win32.keybd_event(vkF11, 0, 0, IntPtr.Zero);
        await Task.Delay(60).ConfigureAwait(false);
        Win32.keybd_event(vkF11, 0, keyUp, IntPtr.Zero);
        log?.Invoke("页面已切到全屏（F11）");
    }

    private static async Task<bool> WaitForTitleAsync(string hint, int timeoutMs)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            if (FindWindows(hint).Count > 0) return true;
            await Task.Delay(350).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>关掉登记过的 B 站标签页（找不到就按 bilibili 关键词找）。</summary>
    public static int CloseAll(string dataDir, Action<string>? log = null)
    {
        var tabs = Load(dataDir);
        var closed = 0;
        foreach (var tab in tabs.ToList())
        {
            if (CloseByHint(tab.Hint, log)) closed++;
            else tabs.Remove(tab);
        }
        // 记录丢了也试着兜一下
        if (closed == 0)
        {
            foreach (var hwnd in FindWindows("哔哩哔哩"))
            {
                if (SendCloseToTab(hwnd)) closed++;
            }
        }
        Save(dataDir, Load(dataDir).Where(t => FindWindows(t.Hint).Count > 0).ToList());
        if (closed > 0) log?.Invoke("已关闭 " + closed + " 个 B 站标签页");
        else log?.Invoke("没有找到正在播放的 B 站标签页");
        return closed;
    }

    public static bool CloseByHint(string hint, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(hint)) return false;
        var windows = FindWindows(hint);
        foreach (var hwnd in windows)
        {
            if (SendCloseToTab(hwnd))
            {
                log?.Invoke("已关闭 B 站标签页：" + hint);
                return true;
            }
        }
        log?.Invoke("没找到标题含「" + hint + "」的浏览器窗口");
        return false;
    }

    private static bool SendCloseToTab(IntPtr hwnd)
    {
        try
        {
            NetEaseAutomation.BringToFront(hwnd);
            Thread.Sleep(320);
            PlayStepEngine.SendCombo("Ctrl+W");
            Thread.Sleep(450);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("关闭标签页失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>枚举浏览器窗口里标题匹配的。</summary>
    public static List<IntPtr> FindWindows(string hint)
    {
        var result = new List<IntPtr>();
        if (string.IsNullOrWhiteSpace(hint)) return result;
        var lowered = hint.ToLowerInvariant();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd)) return true;
            var title = Win32.Title(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (!title.ToLowerInvariant().Contains(lowered)) return true;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return true;
            try
            {
                var name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                if (!BrowserNames.Any(b => name.Contains(b))) return true;
            }
            catch { return true; }
            result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>当前系统里有没有在跑的浏览器（上课关闭时提示用）。</summary>
    public static bool AnyBrowserRunning()
        => BrowserNames.Any(n => Process.GetProcessesByName(n).Length > 0);
}
