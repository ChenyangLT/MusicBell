using System.Text.Json;

namespace MusicBell.Core.Services;

public sealed record PlayRecord
{
    public DateTime At { get; set; } = DateTime.Now;
    public string Title { get; set; } = "";
    public string Source { get; set; } = "netease";
    public string Method { get; set; } = "";
    public bool Success { get; set; }
    public string? Requester { get; set; }
}

/// <summary>播放历史（统计页用）。</summary>
public sealed class PlayHistoryStore
{
    private const int MaxRecords = 1000;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string FilePath { get; private set; } = "";
    public List<PlayRecord> Records { get; private set; } = new();

    public static PlayHistoryStore Load(string dataDir)
    {
        var store = new PlayHistoryStore { FilePath = Path.Combine(dataDir, "playlog.json") };
        try
        {
            if (File.Exists(store.FilePath))
            {
                var text = File.ReadAllText(store.FilePath);
                store.Records = JsonSerializer.Deserialize<List<PlayRecord>>(text, Json) ?? new List<PlayRecord>();
            }
        }
        catch (Exception ex) { AppLog.Warn("读取播放历史失败：" + ex.Message); }
        return store;
    }

    public void Add(PlayRecord record)
    {
        Records.Add(record);
        if (Records.Count > MaxRecords) Records.RemoveRange(0, Records.Count - MaxRecords);
        Save();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Records, Json));
        }
        catch (Exception ex) { AppLog.Warn("保存播放历史失败：" + ex.Message); }
    }
}
