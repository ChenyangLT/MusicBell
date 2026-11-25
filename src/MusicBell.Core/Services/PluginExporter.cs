using System.IO.Compression;

namespace MusicBell.Core.Services;

/// <summary>把随程序附带的 ClassIsland 插件文件释放给用户。</summary>
public static class PluginExporter
{
    public sealed record ExportResult(bool Success, string Message, string? Folder, string? Package);

    public static ExportResult Export(string targetDir)
    {
        try
        {
            var src = AppPaths.BundledPluginDir;
            if (!Directory.Exists(src))
                return new ExportResult(false, "程序目录里没有 plugin 文件夹，请重新下载完整版本", null, null);

            var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
                return new ExportResult(false, "plugin 文件夹是空的", null, null);

            var folder = Path.Combine(targetDir, "MusicBell.ClassIsland");
            Directory.CreateDirectory(folder);
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(src, f);
                var dest = Path.Combine(folder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }

            // 同时产出 .cipx（ClassIsland 的插件包就是 zip）
            var cipx = Path.Combine(targetDir, "MusicBell.ClassIsland.cipx");
            if (File.Exists(cipx)) File.Delete(cipx);
            ZipFile.CreateFromDirectory(folder, cipx, CompressionLevel.Optimal, false);

            AppLog.Info($"插件已释放到 {folder}");
            return new ExportResult(true, "插件文件已释放", folder, cipx);
        }
        catch (Exception ex)
        {
            AppLog.Error("释放插件失败", ex);
            return new ExportResult(false, "释放失败：" + ex.Message, null, null);
        }
    }

    public static string? BundledPluginVersion()
    {
        try
        {
            var manifest = Path.Combine(AppPaths.BundledPluginDir, "manifest.yml");
            if (!File.Exists(manifest)) return null;
            foreach (var line in File.ReadLines(manifest))
            {
                if (line.TrimStart().StartsWith("version:", StringComparison.OrdinalIgnoreCase))
                    return line.Split(':', 2)[1].Trim();
            }
        }
        catch { }
        return null;
    }
}
