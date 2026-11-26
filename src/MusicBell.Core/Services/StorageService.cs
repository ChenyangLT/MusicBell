using System.Text.Json;
using System.Text.Json.Serialization;
using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>配置与排歌的读写。数据目录可自定义，指针文件常驻默认目录。</summary>
public sealed class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();

    public string DataDir { get; private set; } = AppPaths.DefaultDataDir;
    public AppConfig Config { get; private set; } = new();
    public List<SongEntry> Songs { get; private set; } = new();

    /// <summary>点歌人名单。</summary>
    public PeopleStore People { get; private set; } = null!;

    /// <summary>播放历史（统计页用）。</summary>
    public PlayHistoryStore History { get; private set; } = null!;

    public event EventHandler? SongsChanged;

    public static StorageService Load(string? forcedDir = null)
    {
        var svc = new StorageService();
        var dir = forcedDir ?? ReadPointer();
        svc.DataDir = string.IsNullOrWhiteSpace(dir) ? AppPaths.DefaultDataDir : dir!;
        svc.EnsureDir();
        svc.ReadAll();
        svc.People = PeopleStore.Load(svc.DataDir);
        svc.History = PlayHistoryStore.Load(svc.DataDir);
        AppLog.Init(svc.DataDir);
        AppLog.Info($"数据目录：{svc.DataDir}");
        return svc;
    }

    private static string? ReadPointer()
    {
        try
        {
            if (!File.Exists(AppPaths.PointerFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.PointerFile));
            return doc.RootElement.TryGetProperty("dataDir", out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }

    private static void WritePointer(string dir)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DefaultDataDir);
            var json = JsonSerializer.Serialize(new { dataDir = dir }, JsonOptions);
            File.WriteAllText(AppPaths.PointerFile, json);
        }
        catch (Exception ex) { AppLog.Error("写入数据目录指针失败", ex); }
    }

    private void EnsureDir()
    {
        try { Directory.CreateDirectory(DataDir); } catch { /* 忽略 */ }
    }

    private void ReadAll()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile(DataDir)))
            {
                Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile(DataDir)), JsonOptions) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("读取设置失败，使用默认设置", ex);
            Config = new AppConfig();
        }

        // 旧版本只有「上课铃响关网易云」：迁移成新的总开关 + 子开关。
        if (Config.KillNetEaseOnClass && !Config.CloseOnClass)
        {
            Config.CloseOnClass = true;
            Config.CloseClientOnClass = true;
        }

        try
        {
            if (File.Exists(AppPaths.SongsFile(DataDir)))
            {
                Songs = JsonSerializer.Deserialize<List<SongEntry>>(File.ReadAllText(AppPaths.SongsFile(DataDir)), JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("读取排歌失败", ex);
            Songs = new();
        }

        // 自动补 id、排序
        foreach (var s in Songs)
            if (string.IsNullOrWhiteSpace(s.Id)) s.Id = Guid.NewGuid().ToString("N");
        SortSongs();
    }

    private void SortSongs() => Songs.Sort((a, b) => a.Time.CompareTo(b.Time));

    public void SaveConfig()
    {
        lock (_gate)
        {
            try
            {
                EnsureDir();
                File.WriteAllText(AppPaths.ConfigFile(DataDir), JsonSerializer.Serialize(Config, JsonOptions));
            }
            catch (Exception ex) { AppLog.Error("保存设置失败", ex); }
        }
    }

    public void SaveSongs()
    {
        lock (_gate)
        {
            try
            {
                EnsureDir();
                SortSongs();
                File.WriteAllText(AppPaths.SongsFile(DataDir), JsonSerializer.Serialize(Songs, JsonOptions));
            }
            catch (Exception ex) { AppLog.Error("保存排歌失败", ex); }
        }
        SongsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把数据目录搬到新位置（复制现有文件）。</summary>
    public void MoveDataDir(string newDir)
    {
        if (string.IsNullOrWhiteSpace(newDir)) return;
        newDir = Path.GetFullPath(newDir);
        if (string.Equals(newDir, DataDir, StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(newDir);
        SaveConfig();
        SaveSongs();
        foreach (var name in new[] { "config.json", "songs.json", "people.json", "playlog.json" })
        {
            var src = Path.Combine(DataDir, name);
            if (File.Exists(src)) File.Copy(src, Path.Combine(newDir, name), true);
        }

        DataDir = newDir;
        WritePointer(newDir);
        EnsureDir();
        AppLog.Init(newDir);
        AppLog.Info($"数据目录已切换到：{newDir}");
    }

    /// <summary>恢复默认数据目录（不删旧文件）。</summary>
    public void ResetDataDir()
    {
        try { if (File.Exists(AppPaths.PointerFile)) File.Delete(AppPaths.PointerFile); } catch { }
        DataDir = AppPaths.DefaultDataDir;
        EnsureDir();
        ReadAll();
        People = PeopleStore.Load(DataDir);
        History = PlayHistoryStore.Load(DataDir);
        AppLog.Init(DataDir);
    }

    public IReadOnlyList<SongEntry> SongsOn(DateTime date)
        => Songs.Where(s => s.OccursOn(date)).OrderBy(s => s.Time.TimeOfDay).ToList();

    public void Add(SongEntry entry)
    {
        Songs.Add(entry);
        SaveSongs();
    }

    public void Remove(SongEntry entry)
    {
        Songs.Remove(entry);
        SaveSongs();
    }

    public void Update(SongEntry entry) => SaveSongs();

    public (SongEntry Entry, DateTime At)? NextOccurrence(DateTime from, int days = 14)
    {
        SongEntry? best = null;
        DateTime bestAt = DateTime.MaxValue;
        for (var i = 0; i <= days; i++)
        {
            var day = from.Date.AddDays(i);
            foreach (var s in Songs)
            {
                if (!s.Enabled) continue;
                var occ = s.OccurrenceOn(day);
                if (occ is null || occ.Value < from) continue;
                if (occ.Value < bestAt) { bestAt = occ.Value; best = s; }
            }
        }
        return best is null ? null : (best, bestAt);
    }
}
