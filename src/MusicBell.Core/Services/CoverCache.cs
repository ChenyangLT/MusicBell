using System.Net;

namespace MusicBell.Core.Services;

/// <summary>把封面（网易云 / B 站）缓存到 &lt;数据目录&gt;\covers\&lt;文件名&gt;，界面直接读本地文件。</summary>
public static class CoverCache
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    { Timeout = TimeSpan.FromSeconds(12) };

    private static readonly object Gate = new();
    private static readonly Dictionary<string, SemaphoreSlim> Locks = new();

    static CoverCache()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
    }

    public static string Dir(string dataDir) => Path.Combine(dataDir, "covers");

    /// <summary>任意文件名的本地封面（不下载）。</summary>
    public static string? ExistingFile(string dataDir, string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var path = Path.Combine(Dir(dataDir), fileName);
            return File.Exists(path) && new FileInfo(path).Length > 500 ? path : null;
        }
        catch { return null; }
    }

    /// <summary>已经在本地就直接返回（不下载）。</summary>
    public static string? Existing(string dataDir, long id) => ExistingFile(dataDir, id + ".jpg");

    /// <summary>确保有本地封面；没有就下载（自动补网易云封面地址）。返回本地路径或 null。</summary>
    public static Task<string?> EnsureAsync(string dataDir, long id, string? remoteUrl = null, CancellationToken ct = default)
        => id <= 0 ? Task.FromResult<string?>(null) : EnsureUrlAsync(dataDir, id + ".jpg", remoteUrl ?? "", ct);

    /// <summary>按文件名缓存任意图片（B 站封面等）。url 为空时返回 null。</summary>
    public static async Task<string?> EnsureUrlAsync(string dataDir, string fileName, string? remoteUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var existing = ExistingFile(dataDir, fileName);
        if (existing is not null) return existing;

        SemaphoreSlim gate;
        lock (Gate)
        {
            if (!Locks.TryGetValue(fileName, out gate!)) Locks[fileName] = gate = new SemaphoreSlim(1, 1);
        }

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            existing = ExistingFile(dataDir, fileName);
            if (existing is not null) return existing;

            var url = remoteUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                if (!long.TryParse(Path.GetFileNameWithoutExtension(fileName), out var id)) return null;
                url = await NeteaseSearchClient.GetCoverUrlAsync(id, ct).ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(url)) return null;

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (url.Contains("hdslb.com", StringComparison.OrdinalIgnoreCase))
                req.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
            else if (url.Contains("hdslb", StringComparison.OrdinalIgnoreCase) || url.Contains("biliimg", StringComparison.OrdinalIgnoreCase))
                req.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 500) return null;

            Directory.CreateDirectory(Dir(dataDir));
            var path = Path.Combine(Dir(dataDir), fileName);
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, path, true);
            return path;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            AppLog.Warn($"封面缓存失败（{fileName}）：{ex.Message}");
            return null;
        }
        finally { gate.Release(); }
    }
}
