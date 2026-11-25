using System.Net;

namespace MusicBell.Core.Services;

/// <summary>把网易云封面缓存到 <数据目录>\covers\{id}.jpg，界面直接读本地文件。</summary>
public static class CoverCache
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly object Gate = new();
    private static readonly Dictionary<long, SemaphoreSlim> Locks = new();

    static CoverCache()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
    }

    public static string Dir(string dataDir) => Path.Combine(dataDir, "covers");

    /// <summary>已经在本地就直接返回（不下载）。</summary>
    public static string? Existing(string dataDir, long id)
    {
        try
        {
            var path = Path.Combine(Dir(dataDir), id + ".jpg");
            return File.Exists(path) && new FileInfo(path).Length > 500 ? path : null;
        }
        catch { return null; }
    }

    /// <summary>确保有本地封面；没有就下载（自动补封面地址）。返回本地路径或 null。</summary>
    public static async Task<string?> EnsureAsync(string dataDir, long id, string? remoteUrl = null, CancellationToken ct = default)
    {
        if (id <= 0) return null;
        var existing = Existing(dataDir, id);
        if (existing is not null) return existing;

        SemaphoreSlim gate;
        lock (Gate)
        {
            if (!Locks.TryGetValue(id, out gate!)) Locks[id] = gate = new SemaphoreSlim(1, 1);
        }

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            existing = Existing(dataDir, id);
            if (existing is not null) return existing;

            var url = remoteUrl;
            if (string.IsNullOrWhiteSpace(url))
                url = await NeteaseSearchClient.GetCoverUrlAsync(id, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(url)) return null;

            var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (bytes.Length < 500) return null;

            Directory.CreateDirectory(Dir(dataDir));
            var path = Path.Combine(Dir(dataDir), id + ".jpg");
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, path, true);
            return path;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            AppLog.Warn($"封面缓存失败（{id}）：{ex.Message}");
            return null;
        }
        finally { gate.Release(); }
    }
}
