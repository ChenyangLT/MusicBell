using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicBell.Core.Services;

/// <summary>B 站视频搜索（供点歌页选源用）。</summary>
public sealed class BiliSearchClient
{
    private const string Ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Ua);
        c.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://search.bilibili.com/");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "buvid3=" + Guid.NewGuid().ToString("N").ToUpper() + "infoc; b_nut=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return c;
    }

    public sealed record BiliVideo(string Bvid, string Title, string Author, string CoverUrl, int DurationSec, string DurationText, string Url, long Play)
    {
        public string Query => Author + " " + Title;
        public string Subtitle => Author + (Play > 0 ? " · " + FormatPlay(Play) : "");
    }

    private static string FormatPlay(long play)
        => play >= 10000 ? (play / 10000.0).ToString("0.#") + " 万播放" : play + " 播放";

    public static string WatchUrl(string bvid) => "https://www.bilibili.com/video/" + bvid;

    /// <summary>按关键词搜 B 站视频。</summary>
    public async Task<IReadOnlyList<BiliVideo>> SearchAsync(string keyword, int limit = 8, CancellationToken ct = default)
    {
        keyword = (keyword ?? "").Trim();
        if (keyword.Length == 0) return Array.Empty<BiliVideo>();
        try
        {
            var url = "https://api.bilibili.com/x/web-interface/search/type?search_type=video&page=1&page_size=20&keyword=" + Uri.EscapeDataString(keyword);
            var body = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0) return Array.Empty<BiliVideo>();
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return Array.Empty<BiliVideo>();
            if (!data.TryGetProperty("result", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<BiliVideo>();

            var list = new List<BiliVideo>();
            foreach (var item in arr.EnumerateArray())
            {
                var bvid = GetString(item, "bvid");
                if (string.IsNullOrWhiteSpace(bvid)) continue;
                var title = Clean(GetString(item, "title"));
                if (title.Length == 0) continue;
                var author = Clean(GetString(item, "author"));
                var pic = GetString(item, "pic");
                if (pic.StartsWith("//")) pic = "https:" + pic;
                var dur = GetString(item, "duration");
                var sec = ParseDuration(dur);
                long play = item.TryGetProperty("play", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;
                list.Add(new BiliVideo(bvid!, title, author, pic, sec, string.IsNullOrWhiteSpace(dur) ? "" : dur, WatchUrl(bvid!), play));
                if (list.Count >= limit) break;
            }
            return list;
        }
        catch (Exception ex)
        {
            AppLog.Warn("B 站搜索失败：" + ex.Message);
            return Array.Empty<BiliVideo>();
        }
    }

    private static string GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Clean(string s)
        => WebUtility.HtmlDecode(Tag.Replace(s ?? "", "")).Trim();

    private static int ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var parts = text.Split(':');
        var sec = 0;
        foreach (var p in parts)
            sec = sec * 60 + (int.TryParse(p, out var n) ? n : 0);
        return sec;
    }

    public static string SearchUrl(string keyword)
        => "https://search.bilibili.com/all?keyword=" + Uri.EscapeDataString(keyword ?? "");
}
