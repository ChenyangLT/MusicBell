using System.Collections.Concurrent;
using System.Text;

namespace MusicBell.Core.Services;

/// <summary>极简文件日志 + 内存最近日志（供界面显示）。</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<string> Recent = new();
    private static string? _dir;

    public static void Init(string dataDir)
    {
        _dir = AppPaths.LogDir(dataDir);
        try { Directory.CreateDirectory(_dir); } catch { /* 忽略 */ }
    }

    public static IReadOnlyList<string> Snapshot() => Recent.ToArray();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : message + " :: " + ex.GetType().Name + ": " + ex.Message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        Recent.Enqueue(line);
        while (Recent.Count > 500 && Recent.TryDequeue(out _)) { }

        if (_dir is null) return;
        try
        {
            lock (Gate)
            {
                var file = Path.Combine(_dir, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* 忽略 */ }
    }
}
