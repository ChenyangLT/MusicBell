using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MusicBell.Core.Services;

/// <summary>
/// 开机自启动。主路径是 HKCU\\...\\Run，但只写一次不够稳：
/// 学校电脑上常见「写进去了却被任务管理器禁用」「值还指着旧路径」「Run 被策略拦住」三种情况，
/// 所以这里统一走 <see cref="Ensure"/>：写、校验、清禁用标记、必要时再用启动文件夹兜底。
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ValueName = "MusicBell";
    private const string ShortcutName = "MusicBell.lnk";

    /// <summary>注册表里应该写的那条命令（路径带引号，换目录后也能一眼看出对不对）。</summary>
    public static string Command => "\"" + AppPaths.ExePath + "\" --silent";

    private static string StartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    private static string ShortcutPath => Path.Combine(StartupFolder, ShortcutName);

    /// <summary>注册项存在、指向当前这份 exe、而且没被系统禁用。</summary>
    public static bool IsEnabled()
    {
        var (state, _) = Inspect();
        return state is AutoStartState.Ok or AutoStartState.FolderOnly;
    }

    /// <summary>当前这一台的实际情况，界面上直接显示，出问题时不用猜。</summary>
    public static (AutoStartState State, string Detail) Inspect()
    {
        try
        {
            var runValue = ReadRunValue();
            var shortcut = File.Exists(ShortcutPath);
            if (runValue is null && !shortcut)
                return (AutoStartState.NotRegistered, "还没有注册开机自启");

            if (runValue is not null)
            {
                var path = ExtractPath(runValue);
                if (!string.Equals(path, AppPaths.ExePath, StringComparison.OrdinalIgnoreCase))
                    return (AutoStartState.StalePath, "注册项还指着别的位置：" + path);
                if (IsBlockedByApprovedList())
                    return (AutoStartState.DisabledBySystem, "注册项被系统的启动禁用列表关掉了（任务管理器里被禁用）");
                return (AutoStartState.Ok, "已注册：" + runValue);
            }

            return (AutoStartState.FolderOnly, "使用启动文件夹：" + ShortcutPath);
        }
        catch (Exception ex)
        {
            return (AutoStartState.Error, "读注册项失败：" + ex.Message);
        }
    }

    /// <summary>打开 / 关闭开机自启。true = 现在确实能随开机启动。</summary>
    public static bool SetEnabled(bool enabled, bool silent = true)
    {
        if (!enabled)
        {
            var ok = RemoveAll();
            AppLog.Info(ok ? "已关闭开机自启" : "关闭开机自启时有点问题，见日志");
            return !IsEnabled();
        }

        var result = Ensure(true);
        AppLog.Info("设置开机自启：" + result.Message);
        return result.Ok;
    }

    /// <summary>
    /// 让实际状态和配置对齐：需要自启就把注册项修好（路径不对重写、被禁用就解除禁用、写不进去就放启动文件夹）。
    /// 不需要就顺手清掉。返回是否成功与一句人话说明。
    /// </summary>
    public static (bool Ok, string Message) Ensure(bool shouldStart)
    {
        try
        {
            if (!shouldStart)
            {
                if (ReadRunValue() is null && !File.Exists(ShortcutPath))
                    return (true, "本来就没有开机自启");
                RemoveAll();
                return (true, "已关闭开机自启");
            }

            var (state, _) = Inspect();
            if (state == AutoStartState.Ok) return (true, "开机自启正常：" + AppPaths.ExePath);
            if (state == AutoStartState.FolderOnly) return (true, "开机自启走启动文件夹，正常");

            // 1. 重写 Run 项并读回来核对
            if (WriteRunValue(Command) && ReadRunValue() is { } check && ExtractPath(check) is { } path
                && string.Equals(path, AppPaths.ExePath, StringComparison.OrdinalIgnoreCase))
            {
                ClearApprovedBlock();
                // 路径已经对了就不要再留一份启动文件夹，免得开机弹两次
                TryDeleteShortcut();
                return (true, "已写入注册表：" + Command);
            }

            // 2. Run 写不进去（策略/权限），退回启动文件夹
            if (CreateShortcut())
            {
                ClearApprovedBlock();
                return (true, "注册表被拦住了，已改用启动文件夹：" + ShortcutPath);
            }

            return (false, "注册表和启动文件夹都没写成功，可能是安全软件挡住了");
        }
        catch (Exception ex)
        {
            AppLog.Error("处理开机自启失败", ex);
            return (false, "处理开机自启失败：" + ex.Message);
        }
    }

    // ---------- 注册表 ----------

    private static string? ReadRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
        catch { return null; }
    }

    private static bool WriteRunValue(string command)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
                if (key is null) return false;
                key.SetValue(ValueName, command, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"写 Run 项失败（第 {attempt + 1} 次）：{ex.Message}");
                Thread.Sleep(150);
            }
        }
        return false;
    }

    /// <summary>Windows 会在 StartupApproved 里记「用户禁用过」，清掉它才会真的自启。</summary>
    private static bool IsBlockedByApprovedList()
    {
        foreach (var (hivePath, name) in new[] { (ApprovedRunKey, ValueName), (ApprovedFolderKey, ShortcutName) })
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(hivePath);
                if (key?.GetValue(name) is byte[] { Length: > 0 } bytes && (bytes[0] & 1) == 1)
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static void ClearApprovedBlock()
    {
        foreach (var (hivePath, name) in new[] { (ApprovedRunKey, ValueName), (ApprovedFolderKey, ShortcutName) })
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(hivePath, true);
                if (key?.GetValue(name) is byte[] { Length: > 0 } bytes && (bytes[0] & 1) == 1)
                {
                    key.DeleteValue(name, false);
                    AppLog.Info("已解除系统的启动禁用标记：" + hivePath);
                }
            }
            catch (Exception ex) { AppLog.Warn("清理启动禁用标记失败：" + ex.Message); }
        }
    }

    // ---------- 启动文件夹 ----------

    private static bool CreateShortcut()
    {
        try
        {
            Directory.CreateDirectory(StartupFolder);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return WriteCmdFallback();
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = AppPaths.ExePath;
            link.Arguments = "--silent";
            link.WorkingDirectory = AppPaths.AppDir;
            link.IconLocation = AppPaths.ExePath + ",0";
            link.Description = "MusicBell 点歌铃";
            link.Save();
            return File.Exists(ShortcutPath);
        }
        catch (Exception ex)
        {
            AppLog.Warn("创建启动文件夹快捷方式失败：" + ex.Message);
            return WriteCmdFallback();
        }
    }

    /// <summary>连 COM 都用不了时最后的兜底：丢一个 cmd 在启动目录里。</summary>
    private static bool WriteCmdFallback()
    {
        try
        {
            Directory.CreateDirectory(StartupFolder);
            var cmd = Path.Combine(StartupFolder, "MusicBell.cmd");
            var body = "@echo off" + "\r\n" + "start \"\" \"" + AppPaths.ExePath + "\" --silent" + "\r\n";
            File.WriteAllText(cmd, body);
            return File.Exists(cmd);
        }
        catch (Exception ex)
        {
            AppLog.Warn("写启动文件夹 cmd 失败：" + ex.Message);
            return false;
        }
    }

    private static void TryDeleteShortcut()
    {
        try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
    }

    private static bool RemoveAll()
    {
        var ok = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key?.GetValue(ValueName) is not null) key.DeleteValue(ValueName, false);
        }
        catch (Exception ex) { AppLog.Warn("删除 Run 项失败：" + ex.Message); ok = false; }

        try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { ok = false; }
        try
        {
            var cmd = Path.Combine(StartupFolder, "MusicBell.cmd");
            if (File.Exists(cmd)) File.Delete(cmd);
        }
        catch { ok = false; }

        foreach (var (hivePath, name) in new[] { (ApprovedRunKey, ValueName), (ApprovedFolderKey, ShortcutName) })
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(hivePath, true);
                key?.DeleteValue(name, false);
            }
            catch { }
        }
        return ok;
    }

    /// <summary>从 "C:\\x\\MusicBell.exe" --silent 里把 exe 路径抠出来。</summary>
    private static string ExtractPath(string commandLine)
    {
        var text = commandLine.Trim();
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = text.IndexOf('"', 1);
            if (end > 1) return text[1..end];
        }
        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }
}

public enum AutoStartState
{
    Ok,
    FolderOnly,
    NotRegistered,
    StalePath,
    DisabledBySystem,
    Error
}
