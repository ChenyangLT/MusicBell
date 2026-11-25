using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MusicBell.App;

/// <summary>主题（深色 / 浅色 / 跟随系统）。切换时替换合并字典里的色板，DynamicResource 会自动刷新。</summary>
public static class ThemeService
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    /// <summary>当前实际生效的主题：dark 或 light。</summary>
    public static string Current { get; private set; } = Dark;

    /// <summary>用户选择（dark / light / system）。</summary>
    public static string Preference { get; private set; } = Dark;

    public static event Action<string>? Changed;

    public static void Apply(string? preference)
    {
        var pref = (preference ?? Dark).Trim().ToLowerInvariant();
        if (pref != Light && pref != System) pref = Dark;
        Preference = pref;

        var effective = pref == System ? (IsSystemLight() ? Light : Dark) : pref;
        var app = Application.Current;
        var applied = false;
        if (app != null)
        {
            var uri = new Uri("pack://application:,,,/Themes/" + (effective == Light ? "Light" : "Dark") + ".xaml", UriKind.Absolute);
            var dict = new ResourceDictionary { Source = uri };
            var merged = app.Resources.MergedDictionaries;
            var index = -1;
            for (var i = 0; i < merged.Count; i++)
            {
                var source = merged[i].Source?.OriginalString ?? string.Empty;
                if (source.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                    source.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            if (index >= 0) merged[index] = dict;
            else merged.Add(dict);
            applied = true;
        }

        var changed = Current != effective;
        Current = effective;
        if (changed || applied) Changed?.Invoke(effective);
    }

    public static void Refresh() => Apply(Preference);

    public static Brush Brush(string key)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        }
        catch
        {
            // 资源还没加载
        }
        return Brushes.Transparent;
    }

    public static Color Color(string key) => Brush(key) is SolidColorBrush solid ? solid.Color : Colors.Transparent;

    public static bool IsLight => Current == Light;

    private static bool IsSystemLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
