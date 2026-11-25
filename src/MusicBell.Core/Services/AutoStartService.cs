using Microsoft.Win32;

namespace MusicBell.Core.Services;

/// <summary>开机自启动（写 HKCU Run）。</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MusicBell";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Contains("MusicBell", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool SetEnabled(bool enabled, bool silent = true)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;
            if (enabled)
            {
                var cmd = "\"" + AppPaths.ExePath + "\"" + (silent ? " --silent" : "");
                key.SetValue(ValueName, cmd, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("设置开机自启失败", ex);
            return false;
        }
    }
}
