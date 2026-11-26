using System.Text.Json.Serialization;

namespace MusicBell.Core.Models;

/// <summary>一条排歌（预定在某天某时自动点播）。</summary>
public sealed class SongEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>写进搜索框的文字，一般是「歌手 歌名」。</summary>
    public string Title { get; set; } = "";

    /// <summary>歌手。</summary>
    public string? Artist { get; set; }

    /// <summary>网易云歌曲 id（从联想列表选中时记录，可用于直接播放）。</summary>
    public string? SongId { get; set; }

    /// <summary>预定时刻（日期 + 时间）。</summary>
    public DateTime Time { get; set; } = DateTime.Today.AddHours(8);

    /// <summary>排歌名称 / 备注，到点时一起呼出显示。</summary>
    public string? Label { get; set; }

    /// <summary>点歌人（来自名单，可留空）。</summary>
    public string? Requester { get; set; }

    /// <summary>音乐源：netease / bili。</summary>
    public string Source { get; set; } = SongSources.Netease;

    /// <summary>B 站视频链接（Source=bili 时用）。</summary>
    public string? Url { get; set; }

    /// <summary>封面地址（B 站搜索结果里的图；网易云可留空，按 id 查）。</summary>
    public string? CoverUrl { get; set; }

    /// <summary>歌曲时长（毫秒），用于「放完自动关网易云」。0 = 未知。</summary>
    public int DurationMs { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>重复方式：none / daily / weekly。</summary>
    public string Repeat { get; set; } = RepeatModes.None;

    public DateTime? LastPlayed { get; set; }

    public int PlayCount { get; set; }

    [JsonIgnore]
    public TimeSpan TimeOfDay => Time.TimeOfDay;

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Label) ? Title : Label!;

    /// <summary>该排歌在指定日期是否应该响起。</summary>
    public bool OccursOn(DateTime date)
    {
        var d = date.Date;
        var start = Time.Date;
        if (d < start) return false;
        return Repeat switch
        {
            RepeatModes.Daily => true,
            RepeatModes.Weekly => d.DayOfWeek == start.DayOfWeek,
            _ => d == start
        };
    }

    public DateTime? OccurrenceOn(DateTime date)
        => OccursOn(date) ? date.Date + Time.TimeOfDay : null;

    public SongEntry Clone() => (SongEntry)MemberwiseClone();

    [JsonIgnore]
    public string RepeatText => Repeat switch
    {
        RepeatModes.Daily => "每天",
        RepeatModes.Weekly => "每周" + "日一二三四五六"[(int)Time.DayOfWeek],
        _ => ""
    };
}

public static class SongSources
{
    public const string Netease = "netease";
    public const string Bili = "bili";

    public static string Name(string? source) => source == Bili ? "B 站" : "网易云";
}

public static class PlaybackMethods
{
    public const string Auto = "auto";
    public const string Builtin = "builtin";
    public const string Client = "client";

    public static string Name(string? method) => method switch
    {
        Builtin => "内置播放器",
        Client => "网易云客户端",
        _ => "自动选择"
    };
}

public static class RepeatModes
{
    public const string None = "none";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
}
