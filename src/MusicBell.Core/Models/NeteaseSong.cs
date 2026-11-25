namespace MusicBell.Core.Models;

/// <summary>网易云联想结果。</summary>
public sealed class NeteaseSong
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int DurationMs { get; set; }

    /// <summary>封面地址（先用搜索联想拿到 id，再补详情）。</summary>
    public string? CoverUrl { get; set; }

    /// <summary>已缓存的本地封面文件，UI 用。</summary>
    public string? CoverPath { get; set; }

    public string Query => string.IsNullOrWhiteSpace(Artist) ? Name : Artist + " " + Name;

    public string Subtitle => string.IsNullOrWhiteSpace(Album) ? Artist : Artist + " · " + Album;

    public string DurationText => TimeSpan.FromMilliseconds(Math.Max(0, DurationMs)).ToString(@"m\:ss");
}
