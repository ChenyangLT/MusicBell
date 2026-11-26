using System.Diagnostics;
using System.Text.Json;
using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>把「点歌」变成真的播起来：内置播放器 / 网易云客户端模拟点击 / B 站浏览器。</summary>
public sealed class SongPlayerService
{
    private readonly StorageService _storage;

    public SongPlayerService(StorageService storage) => _storage = storage;

    public AppConfig Config => _storage.Config;

    /// <summary>需要 App 打开内置播放器时触发。</summary>
    public event Action<PlayerTrack>? BuiltinPlayRequested;

    /// <summary>B 站视频地址（给 App 展示用）。</summary>
    public event Action<string>? BrowserOpened;

    /// <summary>按排歌条目播放（带点歌人信息，记进历史）。</summary>
    public async Task<PlayResult> PlayAsync(SongEntry entry, Action<string>? log = null)
    {
        log ??= _ => { };
        var result = entry.Source == SongSources.Bili
            ? await PlayBiliAsync(entry.Url, entry.Title, log).ConfigureAwait(false)
            : await PlayNeteaseCoreAsync(entry.Title, entry.SongId, entry.DurationMs, log).ConfigureAwait(false);
        Record(entry.Display, entry.Source, result, entry.Requester);
        return result;
    }

    /// <summary>按歌名播放（ClassIsland 行动、快速点歌、测试播放都走这里）。</summary>
    public async Task<PlayResult> PlayAsync(string query, string? songId = null, int durationMs = 0, Action<string>? log = null)
    {
        var result = await PlayNeteaseCoreAsync(query, songId, durationMs, log).ConfigureAwait(false);
        Record(query, SongSources.Netease, result, null);
        return result;
    }

    private void Record(string title, string source, PlayResult result, string? requester)
    {
        try
        {
            _storage.History.Add(new PlayRecord
            {
                At = DateTime.Now,
                Title = string.IsNullOrWhiteSpace(title) ? result.Query : title,
                Source = source,
                Method = result.Method.ToString(),
                Success = result.Success,
                Requester = requester
            });
        }
        catch (Exception ex) { AppLog.Warn("写入播放历史失败：" + ex.Message); }
    }

    // ---------------- B 站 ----------------

    private async Task<PlayResult> PlayBiliAsync(string? url, string query, Action<string>? log)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(url))
        {
            var found = await new BiliSearchClient().SearchAsync(query, 1).ConfigureAwait(false);
            if (found.Count == 0) return PlayResult.Fail(query, "B 站没搜到这个视频");
            url = found[0].Url;
        }
        var cfg = _storage.Config;
        var ok = await BrowserTabs.OpenAsync(_storage.DataDir, url!, query,
            cfg.BiliMaximize, cfg.BiliPageFullscreen, log).ConfigureAwait(false);
        if (ok) BrowserOpened?.Invoke(url!);
        sw.Stop();
        return ok
            ? PlayResult.Ok(query, PlayMethod.Browser, "已在浏览器打开 B 站视频") with { Elapsed = sw.Elapsed }
            : PlayResult.Fail(query, "打开浏览器失败") with { Elapsed = sw.Elapsed };
    }

    // ---------------- 网易云 ----------------

    private async Task<PlayResult> PlayNeteaseCoreAsync(string query, string? songId, int durationMs, Action<string>? log)
    {
        var sw = Stopwatch.StartNew();
        query = (query ?? "").Trim();
        if (query.Length == 0 && string.IsNullOrEmpty(songId))
            return PlayResult.Fail(query, "没有歌名");

        log ??= _ => { };
        var method = Config.PlaybackMethod;
        long.TryParse(songId, out var id);
        if (id <= 0 && !string.IsNullOrWhiteSpace(songId) && long.TryParse(songId, out var parsed)) id = parsed;

        // 内置播放器
        if (!string.Equals(method, PlaybackMethods.Client, StringComparison.Ordinal))
        {
            if (id > 0)
            {
                var info = await NeteaseStreamClient.ResolveStreamAsync(id, Config.MusicCookie).ConfigureAwait(false);
                if (info.Ok)
                {
                    BuiltinPlayRequested?.Invoke(new PlayerTrack(id, query, "", query));
                    log("用内置播放器播放：" + query);
                    sw.Stop();
                    return PlayResult.Ok(query, PlayMethod.Builtin, "正在用内置播放器播放") with { Elapsed = sw.Elapsed };
                }
                log("内置播放器拿不到直链：" + info.Message);
                if (string.Equals(method, PlaybackMethods.Builtin, StringComparison.Ordinal))
                {
                    sw.Stop();
                    return PlayResult.Fail(query, info.Message) with { Elapsed = sw.Elapsed };
                }
            }
            else if (string.Equals(method, PlaybackMethods.Builtin, StringComparison.Ordinal))
            {
                return PlayResult.Fail(query, "内置播放器要先在联想列表里选一首具体的歌（缺歌曲 id）");
            }
        }

        // 老路子：直接用协议
        if (!Config.PlayViaSearch)
        {
            var url = !string.IsNullOrEmpty(songId)
                ? "orpheus://song/" + songId
                : "orpheus://search?keyword=" + Uri.EscapeDataString(query);
            var okDirect = NetEaseAutomation.OpenUrl(url, log);
            if (okDirect && Config.CloseNetEaseAfterPlay) ScheduleClose(durationMs);
            sw.Stop();
            return okDirect
                ? PlayResult.Ok(query, PlayMethod.DirectUrl, "已用链接唤起网易云") with { Elapsed = sw.Elapsed }
                : PlayResult.Fail(query, "唤起网易云失败") with { Elapsed = sw.Elapsed };
        }

        // 客户端模拟点击（自定义步骤）
        var exe = Config.NetEasePathResolved;
        var hwnd = await NetEaseAutomation.EnsureWindowAsync(exe, 25000, log).ConfigureAwait(false);
        if (hwnd == IntPtr.Zero)
            return PlayResult.Fail(query, "没找到网易云窗口，也没能启动它");

        var steps = Config.ClientSteps is { Count: > 0 } custom ? custom : DefaultPlaySteps.Create(Config);
        var options = new PlayStepEngine.Options
        {
            Query = query,
            Label = query,
            KeyDelayMs = Math.Clamp(Config.KeyDelayMs, 30, 2000),
            RestoreClipboard = Config.RestoreClipboard,
            AutoPlayFirst = Config.AutoPlayFirst,
            OpenNowPlaying = Config.OpenNowPlaying,
            BringToFront = Config.BringNetEaseToFront,
            ClickOffsetX = Config.ClickOffsetX,
            ClickOffsetY = Config.ClickOffsetY,
            Log = log
        };

        var result = await PlayStepEngine.RunAsync(steps, options).ConfigureAwait(false);
        if (result.Success && Config.CloseNetEaseAfterPlay) ScheduleClose(durationMs);
        return result with { Elapsed = sw.Elapsed };
    }

    /// <summary>一首歌放完后自动关闭网易云。</summary>
    private void ScheduleClose(int durationMs)
    {
        var ms = durationMs > 5000 ? durationMs : 240_000;
        var delay = TimeSpan.FromMilliseconds(ms + 2500);
        AppLog.Info($"将在 {delay.TotalMinutes:F1} 分钟后关闭网易云");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                NetEaseAutomation.CloseNetEase();
            }
            catch (Exception ex) { AppLog.Warn("自动关闭网易云失败：" + ex.Message); }
        });
    }
}
