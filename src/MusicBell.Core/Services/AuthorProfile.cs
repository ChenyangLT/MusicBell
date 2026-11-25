using System.Net;
using System.Text.Json;

namespace MusicBell.Core.Services;

/// <summary>作者信息（B 站昵称与头像自动获取；链接指向 GitHub）。</summary>
public sealed class AuthorProfile
{
    public const long BiliMid = 3461567773411503;
    public const string BiliUrl = "https://space.bilibili.com/3461567773411503";
    public const string GithubUrl = "https://github.com/ChenyangLT";

    public string Name { get; set; } = "Chen_yang_LT";
    public string AvatarPath { get; set; } = "";
    public string BiliUrlText => BiliUrl;
    public string GithubUrlText => GithubUrl;

    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://space.bilibili.com/");
        return c;
    }

    /// <summary>取作者信息（缓存 7 天；头像下载到数据目录）。</summary>
    public static async Task<AuthorProfile> GetAsync(string dataDir, bool force = false, CancellationToken ct = default)
    {
        var profile = new AuthorProfile();
        var jsonPath = Path.Combine(dataDir, "author.json");
        var avatarPath = Path.Combine(dataDir, "author.jpg");

        try
        {
            if (!force && File.Exists(jsonPath) && File.Exists(avatarPath) &&
                DateTime.Now - File.GetLastWriteTime(jsonPath) < TimeSpan.FromDays(7))
            {
                using var cached = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (cached.RootElement.TryGetProperty("name", out var n)) profile.Name = n.GetString() ?? profile.Name;
                profile.AvatarPath = avatarPath;
                return profile;
            }
        }
        catch { }

        try
        {
            var json = await Http.GetStringAsync(
                "https://api.bilibili.com/x/web-interface/card?mid=" + BiliMid, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("card", out var card))
            {
                if (card.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } nm)
                    profile.Name = nm;
                if (card.TryGetProperty("face", out var face) && face.GetString() is { Length: > 0 } url)
                {
                    if (url.StartsWith("//")) url = "https:" + url;
                    var bytes = await Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                    if (bytes.Length > 500)
                    {
                        await File.WriteAllBytesAsync(avatarPath, bytes, ct).ConfigureAwait(false);
                        profile.AvatarPath = avatarPath;
                    }
                }
            }

            var payload = JsonSerializer.Serialize(new { name = profile.Name, fetchedAt = DateTime.Now });
            await File.WriteAllTextAsync(jsonPath, payload, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(profile.AvatarPath) && File.Exists(avatarPath))
                profile.AvatarPath = avatarPath;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("获取作者信息失败：" + ex.Message);
            if (File.Exists(avatarPath)) profile.AvatarPath = avatarPath;
        }
        return profile;
    }
}
