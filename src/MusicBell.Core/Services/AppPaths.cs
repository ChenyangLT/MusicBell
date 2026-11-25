namespace MusicBell.Core.Services;

/// <summary>各种路径。</summary>
public static class AppPaths
{
    public const string AppName = "MusicBell";

    /// <summary>默认数据目录：%AppData%\MusicBell</summary>
    public static string DefaultDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    /// <summary>记录「数据目录改到哪了」的指针文件，永远留在默认目录。</summary>
    public static string PointerFile => Path.Combine(DefaultDataDir, "location.json");

    public static string ConfigFile(string dataDir) => Path.Combine(dataDir, "config.json");
    public static string SongsFile(string dataDir) => Path.Combine(dataDir, "songs.json");
    public static string LogDir(string dataDir) => Path.Combine(dataDir, "logs");

    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>随程序发布的 ClassIsland 插件目录。</summary>
    public static string BundledPluginDir => Path.Combine(AppDir, "plugin");

    public static string ExePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return exe;
            return Path.Combine(AppDir, "MusicBell.App.exe");
        }
    }
}
