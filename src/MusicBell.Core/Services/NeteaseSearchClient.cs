using System.Net;
using System.Text.Json;
using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>网易云搜索联想（公用 web 接口，无需登录），以及按 id 补封面/时长。</summary>
public sealed class NeteaseSearchClient
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "appver=2.0.2; os=pc; osver=10; deviceId=MusicBell");
        return client;
    }

    public async Task<IReadOnlyList<NeteaseSong>> SuggestAsync(string keyword, int limit = 8, CancellationToken ct = default)
    {
        keyword = keyword?.Trim() ?? "";
        if (keyword.Length == 0) return Array.Empty<NeteaseSong>();

        var url = "https://music.163.com/api/search/get/web?csrf_token=&s=" + Uri.EscapeDataString(keyword)
                  + "&type=1&offset=0&total=true&limit=" + Math.Clamp(limit, 1, 20);
        try
        {
            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return Array.Empty<NeteaseSong>();
            if (!result.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
                return Array.Empty<NeteaseSong>();

            var list = new List<NeteaseSong>();
            foreach (var item in songs.EnumerateArray())
            {
                var song = new NeteaseSong
                {
                    Id = item.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0,
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    DurationMs = item.TryGetProperty("duration", out var d) && d.TryGetInt32(out var dv) ? dv : 0
                };
                if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                    song.Artist = string.Join(" / ", artists.EnumerateArray()
                        .Select(a => a.TryGetProperty("name", out var an) ? an.GetString() : null)
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                if (item.TryGetProperty("album", out var album))
                {
                    if (album.TryGetProperty("name", out var al)) song.Album = al.GetString() ?? "";
                    if (album.TryGetProperty("picUrl", out var pic)) song.CoverUrl = pic.GetString();
                }
                if (song.Id > 0 && !string.IsNullOrWhiteSpace(song.Name)) list.Add(song);
            }
            return list;
        }
        catch (OperationCanceledException) { return Array.Empty<NeteaseSong>(); }
        catch (Exception ex)
        {
            AppLog.Warn("网易云联想失败：" + ex.Message);
            return Array.Empty<NeteaseSong>();
        }
    }

    /// <summary>按歌曲 id 批量取详情（封面 / 时长 / 专辑）。一次最多 20 首。</summary>
    public async Task<IReadOnlyList<NeteaseSong>> DetailsAsync(IReadOnlyList<long> ids, CancellationToken ct = default)
    {
        if (ids is null || ids.Count == 0) return Array.Empty<NeteaseSong>();
        var use = ids.Where(i => i > 0).Distinct().Take(20).ToList();
        if (use.Count == 0) return Array.Empty<NeteaseSong>();

        var url = "https://music.163.com/api/song/detail?ids=[" + string.Join(",", use) + "]";
        try
        {
            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
                return Array.Empty<NeteaseSong>();

            var list = new List<NeteaseSong>();
            foreach (var item in songs.EnumerateArray())
            {
                var song = new NeteaseSong
                {
                    Id = item.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0,
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    DurationMs = item.TryGetProperty("duration", out var d) && d.TryGetInt32(out var dv) ? dv : 0
                };
                if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                    song.Artist = string.Join(" / ", artists.EnumerateArray()
                        .Select(a => a.TryGetProperty("name", out var an) ? an.GetString() : null)
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                if (item.TryGetProperty("album", out var album))
                {
                    if (album.TryGetProperty("name", out var al)) song.Album = al.GetString() ?? "";
                    if (album.TryGetProperty("picUrl", out var pic)) song.CoverUrl = pic.GetString();
                }
                if (song.Id > 0) list.Add(song);
            }
            return list;
        }
        catch (OperationCanceledException) { return Array.Empty<NeteaseSong>(); }
        catch (Exception ex)
        {
            AppLog.Warn("网易云详情失败：" + ex.Message);
            return Array.Empty<NeteaseSong>();
        }
    }

    /// <summary>取单首封面地址。</summary>
    public static async Task<string?> GetCoverUrlAsync(long id, CancellationToken ct = default)
    {
        var detail = await new NeteaseSearchClient().DetailsAsync(new[] { id }, ct).ConfigureAwait(false);
        return detail.FirstOrDefault()?.CoverUrl;
    }

    /// <summary>给联想结果补封面（就地写入 CoverUrl / DurationMs）。</summary>
    public async Task FillCoversAsync(IReadOnlyList<NeteaseSong> songs, CancellationToken ct = default)
    {
        if (songs is null || songs.Count == 0) return;
        var detail = await DetailsAsync(songs.Select(s => s.Id).ToList(), ct).ConfigureAwait(false);
        if (detail.Count == 0) return;
        var map = detail.ToDictionary(d => d.Id);
        foreach (var s in songs)
        {
            if (!map.TryGetValue(s.Id, out var d)) continue;
            s.CoverUrl ??= d.CoverUrl;
            if (s.DurationMs <= 0) s.DurationMs = d.DurationMs;
            if (string.IsNullOrWhiteSpace(s.Album)) s.Album = d.Album;
        }
    }
}
