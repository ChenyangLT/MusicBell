using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>内置播放器用的直链 / 歌词接口（网易云）。</summary>
public static class NeteaseStreamClient
{
    private const string Ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Ua);
        c.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "os=pc; appver=8.9.70");
        return c;
    }

    public sealed record StreamInfo(bool Ok, string? Url, int Bitrate, int Size, string Message);

    /// <summary>拿播放直链：先走 enhance 接口（音质高），失败退回外链 mp3。</summary>
    public static async Task<StreamInfo> ResolveStreamAsync(long id, string? cookie = null, CancellationToken ct = default)
    {
        if (id <= 0) return new StreamInfo(false, null, 0, 0, "没有歌曲 id");
        try
        {
            var url = $"https://music.163.com/api/song/enhance/player/url?ids=[{id}]&br=320000";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(cookie))
                req.Headers.TryAddWithoutValidation("Cookie", cookie!.Trim());
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var item = data[0];
                var link = item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                var br = item.TryGetProperty("br", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 0;
                var size = item.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
                if (!string.IsNullOrWhiteSpace(link))
                    return new StreamInfo(true, link, br, size, br > 0 ? $"{br / 1000} kbps" : "直链就绪");
                var fee = item.TryGetProperty("fee", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : -1;
                return new StreamInfo(false, null, br, size, fee == 1 ? "这首歌要会员才能在线播放" : "这首拿不到播放直链");
            }
            return new StreamInfo(false, null, 0, 0, "接口没有返回直链");
        }
        catch (Exception ex)
        {
            AppLog.Warn("取播放直链失败：" + ex.Message);
            return new StreamInfo(false, null, 0, 0, "取直链失败：" + ex.Message);
        }
    }

    public sealed record LrcLine(TimeSpan Time, string Text);

    public sealed class LrcDoc
    {
        public string Raw { get; init; } = "";
        public List<LrcLine> Lines { get; init; } = new();
        public bool HasTranslation => Lines.Any(l => l.Text.Contains("  ·  "));

        public int IndexAt(TimeSpan position)
        {
            if (Lines.Count == 0) return -1;
            var idx = -1;
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].Time <= position) idx = i;
                else break;
            }
            return idx;
        }
    }

    private static readonly Regex LrcTag = new(@"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    public static LrcDoc ParseLrc(string? lrc, string? translation = null)
    {
        var doc = new LrcDoc { Raw = lrc ?? "" };
        if (string.IsNullOrWhiteSpace(lrc)) return doc;
        var trans = new Dictionary<TimeSpan, string>();
        if (!string.IsNullOrWhiteSpace(translation))
        {
            foreach (var (t, text) in ParseLines(translation!))
                if (!trans.ContainsKey(t)) trans[t] = text;
        }
        foreach (var (time, text) in ParseLines(lrc!))
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var line = text;
            if (trans.TryGetValue(time, out var tr) && !string.IsNullOrWhiteSpace(tr) && tr != text)
                line = text + "  ·  " + tr;
            doc.Lines.Add(new LrcLine(time, line));
        }
        return doc;
    }

    private static IEnumerable<(TimeSpan, string)> ParseLines(string lrc)
    {
        foreach (var raw in lrc.Replace("\r", "").Split('\n'))
        {
            var matches = LrcTag.Matches(raw);
            if (matches.Count == 0) continue;
            var text = LrcTag.Replace(raw, "").Trim();
            foreach (Match m in matches)
            {
                var min = int.Parse(m.Groups[1].Value);
                var sec = int.Parse(m.Groups[2].Value);
                var frac = m.Groups[3].Success ? m.Groups[3].Value : "0";
                var ms = frac.Length switch
                {
                    1 => int.Parse(frac) * 100,
                    2 => int.Parse(frac) * 10,
                    _ => int.Parse(frac.PadRight(3, '0')[..3])
                };
                yield return (new TimeSpan(0, 0, min, sec, ms), text);
            }
        }
    }

    /// <summary>取歌词（含翻译）。</summary>
    public static async Task<LrcDoc?> GetLyricAsync(long id, CancellationToken ct = default)
    {
        if (id <= 0) return null;
        try
        {
            var url = $"https://music.163.com/api/song/lyric?id={id}&lv=1&kv=1&tv=-1";
            var body = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var lrc = root.TryGetProperty("lrc", out var l) && l.TryGetProperty("lyric", out var ly) ? ly.GetString() : null;
            var tlrc = root.TryGetProperty("tlyric", out var t) && t.TryGetProperty("lyric", out var ty) ? ty.GetString() : null;
            var parsed = ParseLrc(lrc, tlrc);
            return parsed.Lines.Count == 0 && string.IsNullOrWhiteSpace(parsed.Raw) ? null : parsed;
        }
        catch (Exception ex)
        {
            AppLog.Warn("取歌词失败：" + ex.Message);
            return null;
        }
    }
}
