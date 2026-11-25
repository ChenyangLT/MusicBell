using Microsoft.Win32;

namespace MusicBell.Core.Models;

/// <summary>定位网易云音乐主程序。</summary>
public static class NetEaseLocator
{
    private static readonly string[] Candidates =
    {
        @"D:\Program Files\NetEase\CloudMusic\cloudmusic.exe",
        @"C:\Program Files\NetEase\CloudMusic\cloudmusic.exe",
        @"C:\Program Files (x86)\NetEase\CloudMusic\cloudmusic.exe",
        @"D:\Program Files (x86)\NetEase\CloudMusic\cloudmusic.exe",
        @"C:\Program Files\NetEase\CloudMusic\cloudmusic.exe"
    };

    /// <summary>常识路径 + 注册表 orpheus 协议里登记的命令行。</summary>
    public static string GuessPath()
    {
        foreach (var c in Candidates)
            if (File.Exists(c)) return c;

        foreach (var root in new[] { @"Software\Classes\orpheus\shell\open\command", @"Software\Classes\musicbox\shell\open\command" })
        {
            try
            {
                var cmd = Registry.CurrentUser.OpenSubKey(root)?.GetValue(null) as string
                          ?? Registry.ClassesRoot.OpenSubKey(root.Replace(@"Software\Classes\", ""))?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(cmd)) continue;
                var path = ExtractPath(cmd);
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
            catch { /* 忽略 */ }
        }

        // 兜底：从已运行的进程里拿
        try
        {
            var p = System.Diagnostics.Process.GetProcessesByName("cloudmusic").FirstOrDefault();
            if (p?.MainModule?.FileName is { } file && File.Exists(file)) return file;
        }
        catch { /* 忽略 */ }

        return @"D:\Program Files\NetEase\CloudMusic\cloudmusic.exe";
    }

    private static string ExtractPath(string commandLine)
    {
        commandLine = commandLine.Trim();
        if (commandLine.StartsWith('"'))
        {
            var end = commandLine.IndexOf('"', 1);
            return end > 1 ? commandLine[1..end] : "";
        }
        var space = commandLine.IndexOf(' ');
        return space > 0 ? commandLine[..space] : commandLine;
    }

    public static bool Exists(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}
