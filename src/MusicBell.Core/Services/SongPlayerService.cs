using System.Diagnostics;
using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>把「点歌」变成真的在网易云里播起来。</summary>
public sealed class SongPlayerService
{
    private readonly StorageService _storage;

    public SongPlayerService(StorageService storage) => _storage = storage;

    public AppConfig Config => _storage.Config;

    public async Task<PlayResult> PlayAsync(string query, string? songId = null, int durationMs = 0, Action<string>? log = null)
    {
        var sw = Stopwatch.StartNew();
        query = (query ?? "").Trim();
        if (query.Length == 0 && string.IsNullOrEmpty(songId))
            return PlayResult.Fail(query, "没有歌名");

        log ??= _ => { };

        if (!Config.PlayViaSearch)
        {
            var url = !string.IsNullOrEmpty(songId)
                ? "orpheus://song/" + songId
                : "orpheus://search?keyword=" + Uri.EscapeDataString(query);
            var okDirect = NetEaseAutomation.OpenUrl(url, log);
            if (okDirect && Config.CloseNetEaseAfterPlay) ScheduleClose(durationMs, log);
            return okDirect
                ? PlayResult.Ok(query, PlayMethod.DirectUrl, "已用链接唤起网易云")
                : PlayResult.Fail(query, "唤起网易云失败");
        }

        var exe = Config.NetEasePathResolved;
        var hwnd = await NetEaseAutomation.EnsureWindowAsync(exe, 25000, log).ConfigureAwait(false);
        if (hwnd == IntPtr.Zero)
        {
            var fallback = NetEaseAutomation.OpenUrl("orpheus://search?keyword=" + Uri.EscapeDataString(query), log);
            return fallback
                ? PlayResult.Ok(query, PlayMethod.DirectUrl, "没找到网易云窗口，已改用搜索链接")
                : PlayResult.Fail(query, "没找到网易云窗口，也没能启动它");
        }

        if (Config.BringNetEaseToFront) NetEaseAutomation.BringToFront(hwnd);

        var keyDelay = Math.Clamp(Config.KeyDelayMs, 30, 2000);
        var ok = NetEaseAutomation.SearchInBox(hwnd, query,
            Config.SearchBoxX, Config.SearchBoxY, Config.ClickOffsetX, Config.ClickOffsetY,
            keyDelay, Config.RestoreClipboard, log);
        if (!ok)
        {
            sw.Stop();
            return PlayResult.Fail(query, "输入搜索框失败") with { Elapsed = sw.Elapsed };
        }

        var notes = new List<string> { "已在网易云搜索框输入并搜索" };

        if (Config.AutoPlayFirst || Config.OpenNowPlaying)
        {
            // 等搜索页渲染
            await Task.Delay(Math.Max(1300, keyDelay * 8)).ConfigureAwait(false);
            hwnd = NetEaseAutomation.FindMainWindow();
        }

        if (Config.AutoPlayFirst && hwnd != IntPtr.Zero)
        {
            NetEaseAutomation.PlayFirstResult(hwnd, Config.PlayRowX, Config.PlayRowY, log);
            notes.Add("已双击第一首播放");
            await Task.Delay(1400).ConfigureAwait(false);
        }

        if (Config.OpenNowPlaying && hwnd != IntPtr.Zero)
        {
            NetEaseAutomation.OpenNowPlaying(hwnd, Config.NowPlayingX, Config.NowPlayingY, log);
            notes.Add("已打开歌曲详情");
            await Task.Delay(400).ConfigureAwait(false);
        }

        if (Config.CloseNetEaseAfterPlay) ScheduleClose(durationMs, log);

        sw.Stop();
        return PlayResult.Ok(query, PlayMethod.SearchBox, string.Join("，", notes)) with { Elapsed = sw.Elapsed };
    }

    /// <summary>按排歌条目播放。</summary>
    public Task<PlayResult> PlayAsync(SongEntry entry, Action<string>? log = null)
        => PlayAsync(string.IsNullOrWhiteSpace(entry.Title) ? entry.Display : entry.Title, entry.SongId, entry.DurationMs, log);

    /// <summary>一首歌放完后自动关闭网易云。</summary>
    private void ScheduleClose(int durationMs, Action<string>? log)
    {
        var ms = durationMs > 5000 ? durationMs : 240_000;
        var delay = TimeSpan.FromMilliseconds(ms + 2500);
        AppLog.Info($"将在 {delay.TotalMinutes:F1} 分钟后关闭网易云");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                NetEaseAutomation.CloseNetEase(log);
            }
            catch (Exception ex) { AppLog.Warn("自动关闭网易云失败：" + ex.Message); }
        });
    }
}
