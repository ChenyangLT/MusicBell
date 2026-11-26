namespace MusicBell.Core.Models;

/// <summary>自动播放流程里的一步。位置一律用「网易云客户区比例」，换分辨率/DPI 自动换算。</summary>
public sealed class PlayStep
{
    /// <summary>click / doubleClick / paste / key / wait / url</summary>
    public string Kind { get; set; } = "click";

    /// <summary>显示名，例如「点搜索框」。</summary>
    public string? Name { get; set; }

    /// <summary>横向比例（0~1）。</summary>
    public double X { get; set; }

    /// <summary>纵向比例（0~1）。</summary>
    public double Y { get; set; }

    /// <summary>粘贴内容 / 按键组合。粘贴支持 {query} {label}。</summary>
    public string? Text { get; set; }

    /// <summary>这一步之后等多久（毫秒）。</summary>
    public int WaitMs { get; set; } = 120;

    /// <summary>额外等待条件：空 / title（等窗口标题变化，即真的切歌了）。</summary>
    public string? WaitFor { get; set; }

    /// <summary>只在某个开关打开时执行：空 / play（自动播放）/ now（打开详情页）。</summary>
    public string? Gate { get; set; }

    public PlayStep Clone() => (PlayStep)MemberwiseClone();

    public string Describe()
    {
        var label = string.IsNullOrWhiteSpace(Name) ? Kind : Name!;
        return Kind switch
        {
            "click" => $"点击 {label} ({X:0.###},{Y:0.###})",
            "doubleClick" => $"双击 {label} ({X:0.###},{Y:0.###})",
            "paste" => $"粘贴 {Text}",
            "key" => $"按键 {Text}",
            "wait" => $"等待 {WaitMs} ms",
            "url" => $"打开链接 {Text}",
            _ => label
        };
    }
}

public static class PlayStepKinds
{
    public const string Click = "click";
    public const string DoubleClick = "doubleClick";
    public const string Paste = "paste";
    public const string Key = "key";
    public const string Wait = "wait";
    public const string Url = "url";
}

public static class DefaultPlaySteps
{
    /// <summary>默认流程：点搜索框 → 清空 → 粘贴歌名 → 回车 → 等结果 → 双击第一首 → 等切歌 → 打开歌词页。</summary>
    public static List<PlayStep> Create(AppConfig? config = null)
    {
        var c = config;
        return new List<PlayStep>
        {
            new() { Kind = PlayStepKinds.Click, Name = "点搜索框", X = c?.SearchBoxX ?? 0.304, Y = c?.SearchBoxY ?? 0.042, WaitMs = 160 },
            new() { Kind = PlayStepKinds.Key, Name = "清空搜索框", Text = "Ctrl+A", WaitMs = 120 },
            new() { Kind = PlayStepKinds.Paste, Name = "粘贴歌名", Text = "{query}", WaitMs = 160 },
            new() { Kind = PlayStepKinds.Key, Name = "回车搜索", Text = "Enter", WaitMs = 2000 },
            new() { Kind = PlayStepKinds.Click, Name = "切到单曲", X = 0.230, Y = 0.180, WaitMs = 900 },
            new() { Kind = PlayStepKinds.DoubleClick, Name = "双击第一首", X = c?.PlayRowX ?? 0.290, Y = 0.308, WaitMs = 600, WaitFor = "title", Gate = "play" },
            new() { Kind = PlayStepKinds.Click, Name = "打开歌词页", X = c?.NowPlayingX ?? 0.070, Y = c?.NowPlayingY ?? 0.940, WaitMs = 400, Gate = "now" }
        };
    }
}
