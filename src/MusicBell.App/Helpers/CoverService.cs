using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBell.Core.Services;

namespace MusicBell.App.Helpers;

/// <summary>封面：本地缓存优先，没缓存就去下载，结果做内存缓存（网易云按歌曲 id，B 站按 bvid）。</summary>
public static class CoverService
{
    private static readonly Dictionary<string, ImageSource?> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(4, 4);

    private static string DataDir => App.Current.Storage.DataDir;

    private static string BiliFile(string bvid) => "bili_" + bvid + ".jpg";

    /// <summary>同步取已经缓存到磁盘的封面（不联网）。</summary>
    public static ImageSource? TryCached(long id)
        => id <= 0 ? null : TryCachedFile(id + ".jpg");

    /// <summary>B 站视频封面（本地缓存）。</summary>
    public static ImageSource? TryCachedBili(string? bvid)
        => string.IsNullOrWhiteSpace(bvid) ? null : TryCachedFile(BiliFile(bvid!));

    private static ImageSource? TryCachedFile(string fileName)
    {
        lock (Memory)
        {
            if (Memory.TryGetValue(fileName, out var hit)) return hit;
        }
        var image = Load(Path.Combine(CoverCache.Dir(DataDir), fileName));
        lock (Memory) Memory[fileName] = image;
        return image;
    }

    /// <summary>取网易云封面：本地没有就下载（url 必需）。</summary>
    public static async Task<ImageSource?> GetAsync(long id, string? url, CancellationToken ct = default)
    {
        if (id <= 0) return null;
        var cached = TryCached(id);
        if (cached != null) return cached;
        if (string.IsNullOrWhiteSpace(url)) return null;

        var fileName = id + ".jpg";
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = await CoverCache.EnsureUrlAsync(DataDir, fileName, url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(file)) return null;
            var image = await Application.Current.Dispatcher.InvokeAsync(() => Load(file));
            lock (Memory) Memory[fileName] = image;
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>取封面；本地没有就先用歌曲 id 查接口拿图地址再下载。</summary>
    public static async Task<ImageSource?> EnsureAsync(long id, CancellationToken ct = default)
    {
        var cached = TryCached(id);
        if (cached != null || id <= 0) return cached;
        string? url = null;
        try { url = await NeteaseSearchClient.GetCoverUrlAsync(id, ct).ConfigureAwait(false); }
        catch { }
        if (string.IsNullOrWhiteSpace(url)) return null;
        return await GetAsync(id, url, ct).ConfigureAwait(false);
    }

    /// <summary>取 B 站视频封面（url 来自搜索结果）。</summary>
    public static async Task<ImageSource?> GetBiliAsync(string? bvid, string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bvid)) return null;
        var cached = TryCachedBili(bvid);
        if (cached != null || string.IsNullOrWhiteSpace(url)) return cached;

        var fileName = BiliFile(bvid!);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = await CoverCache.EnsureUrlAsync(DataDir, fileName, url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(file)) return null;
            var image = await Application.Current.Dispatcher.InvokeAsync(() => Load(file));
            lock (Memory) Memory[fileName] = image;
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>按排歌条目取封面（网易云 / B 站都能取）。</summary>
    public static async Task<ImageSource?> EnsureForAsync(MusicBell.Core.Models.SongEntry song, CancellationToken ct = default)
    {
        if (song is null) return null;
        if (song.Source == MusicBell.Core.Models.SongSources.Bili)
            return await GetBiliAsync(song.SongId, song.CoverUrl, ct).ConfigureAwait(false);
        if (long.TryParse(song.SongId, out var id) && id > 0)
            return await EnsureAsync(id, ct).ConfigureAwait(false);
        return null;
    }

    public static ImageBrush? ToBrush(ImageSource? source)
    {
        if (source is null) return null;
        var brush = new ImageBrush(source)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>给一个 Uri 直接生成圆角用的画刷（头像之类）。</summary>
    public static ImageBrush? FromFile(string? file)
    {
        var image = Load(file);
        return ToBrush(image);
    }

    private static ImageSource? Load(string? file)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            var info = new FileInfo(file);
            if (info.Length < 512) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
