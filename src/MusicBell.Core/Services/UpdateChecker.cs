using System.Net;
using System.Text.Json;

namespace MusicBell.Core.Services;

public sealed record UpdateInfo(
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string Url,
    string Notes,
    DateTime? PublishedAt);

/// <summary>查 GitHub Releases 最新版本（纯提示，不自动更新）。</summary>
public static class UpdateChecker
{
    public const string DefaultRepo = "ChenyangLT/MusicBell";

    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MusicBell-Updater");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return c;
    }

    public static async Task<UpdateInfo?> CheckAsync(string repo, string currentVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo)) repo = DefaultRepo;
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{repo.Trim()}/releases/latest", ct)
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            DateTime? published = null;
            if (root.TryGetProperty("published_at", out var pa) && pa.GetString() is { } ps &&
                DateTime.TryParse(ps, out var pd)) published = pd.ToLocalTime();

            var latest = tag.TrimStart('v', 'V');
            var hasNew = IsNewer(latest, currentVersion);
            return new UpdateInfo(hasNew, currentVersion, latest, url, notes, published);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            AppLog.Warn("检查更新失败：" + ex.Message);
            return null;
        }
    }

    public static bool IsNewer(string latest, string current)
    {
        static int[] Parts(string s)
            => s.Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();

        var a = Parts(latest);
        var b = Parts(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}
