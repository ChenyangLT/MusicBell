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

/// <summary>网易云封面：本地缓存优先，没缓存就去下载，结果做内存缓存。</summary>
public static class CoverService
{
    private static readonly Dictionary<long, ImageSource?> Memory = new();
    private static readonly SemaphoreSlim Gate = new(4, 4);

    private static string DataDir => App.Current.Storage.DataDir;

    /// <summary>同步取已经缓存到磁盘的封面（不联网）。</summary>
    public static ImageSource? TryCached(long id)
    {
        if (id <= 0) return null;
        lock (Memory)
        {
            if (Memory.TryGetValue(id, out var hit)) return hit;
        }
        var file = Path.Combine(CoverCache.Dir(DataDir), id + ".jpg");
        var image = Load(file);
        lock (Memory) Memory[id] = image;
        return image;
    }

    /// <summary>取封面：本地没有就下载（url 必需）。</summary>
    public static async Task<ImageSource?> GetAsync(long id, string? url, CancellationToken ct = default)
    {
        if (id <= 0) return null;
        var cached = TryCached(id);
        if (cached != null) return cached;
        if (string.IsNullOrWhiteSpace(url)) return null;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var file = await CoverCache.EnsureAsync(DataDir, id, url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(file)) return null;
            var image = await Application.Current.Dispatcher.InvokeAsync(() => Load(file));
            lock (Memory) Memory[id] = image;
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
