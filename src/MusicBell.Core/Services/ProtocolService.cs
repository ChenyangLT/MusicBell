using Microsoft.Win32;

namespace MusicBell.Core.Services;

/// <summary>注册 musicbell:// 协议，供 ClassIsland 行动调用。</summary>
public static class ProtocolService
{
    public const string Scheme = "musicbell";

    public static bool Register()
    {
        try
        {
            var exe = AppPaths.ExePath;
            using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme);
            if (root is null) return false;
            root.SetValue(null, "URL:MusicBell 点歌铃");
            root.SetValue("URL Protocol", "");
            using var icon = root.CreateSubKey("DefaultIcon");
            icon?.SetValue(null, "\"" + exe + "\",0");
            using var cmd = root.CreateSubKey(@"shell\open\command");
            cmd?.SetValue(null, "\"" + exe + "\" \"%1\"");
            AppLog.Info("已注册协议 musicbell://");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("注册协议失败", ex);
            return false;
        }
    }

    public static string BuildPlayUrl(string query, string? songId = null, string? label = null, string source = "classisland")
    {
        var q = "play?q=" + Uri.EscapeDataString(query);
        if (!string.IsNullOrWhiteSpace(songId)) q += "&id=" + Uri.EscapeDataString(songId);
        if (!string.IsNullOrWhiteSpace(label)) q += "&label=" + Uri.EscapeDataString(label!);
        q += "&src=" + Uri.EscapeDataString(source);
        return Scheme + "://" + q;
    }

    public static bool IsEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + Scheme); return k is not null; }
        catch { return false; }
    }

    /// <summary>协议是否已经指向当前这个 exe（换了目录、换了版本后要重注册）。</summary>
    public static bool IsRegisteredForCurrentExe()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + Scheme + @"\shell\open\command");
            var cmd = k?.GetValue(null) as string;
            return !string.IsNullOrEmpty(cmd) &&
                   cmd.Contains(AppPaths.ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
