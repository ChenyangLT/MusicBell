using System.Text.Json.Serialization;

namespace MusicBell.Core.Models;

/// <summary>软件设置。</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    /// <summary>开机自动启动。</summary>
    public bool AutoStart { get; set; }

    /// <summary>启动后静默驻留托盘（不弹出主界面）。</summary>
    public bool SilentStart { get; set; } = true;

    /// <summary>到点时弹出提醒气泡。</summary>
    public bool ShowNotification { get; set; } = true;

    /// <summary>播放前把网易云切到前台。</summary>
    public bool BringNetEaseToFront { get; set; } = true;

    /// <summary>操作完成后还原剪贴板。</summary>
    public bool RestoreClipboard { get; set; } = true;

    /// <summary>网易云主程序路径，空则自动探测。</summary>
    public string? NetEasePath { get; set; }

    /// <summary>搜索框横向位置（占客户区宽度的比例）。</summary>
    public double SearchBoxX { get; set; } = 0.304;

    /// <summary>搜索框纵向位置（占客户区高度的比例）。</summary>
    public double SearchBoxY { get; set; } = 0.042;

    /// <summary>点击位置微调像素。</summary>
    public int ClickOffsetX { get; set; }

    public int ClickOffsetY { get; set; }

    /// <summary>按键之间的等待毫秒。</summary>
    public int KeyDelayMs { get; set; } = 140;

    /// <summary>true = 打开搜索框输入歌名搜索；false = 直接用 orpheus 协议播放。</summary>
    public bool PlayViaSearch { get; set; } = true;

    /// <summary>插件文件释放目录（上次使用）。</summary>
    public string? PluginExportDir { get; set; }

    /// <summary>允许 ClassIsland 行动调用（协议 musicbell://）。</summary>
    public bool EnableProtocol { get; set; } = true;

    /// <summary>提前多少秒开始播放。</summary>
    public int AdvanceSeconds { get; set; }

    /// <summary>一天里排歌默认时刻（用于新建时预填）。</summary>
    public string DefaultTime { get; set; } = "07:50";

    /// <summary>窗口关闭时最小化到托盘而不是退出。</summary>
    public bool MinimizeToTray { get; set; } = true;

    // ------- 外观 -------
    /// <summary>主题：dark / light / system。</summary>
    public string Theme { get; set; } = "dark";

    // ------- 播放行为 -------
    /// <summary>搜索完自动双击第一首播放。</summary>
    public bool AutoPlayFirst { get; set; } = true;

    /// <summary>播放后打开歌曲详情（歌词）页。</summary>
    public bool OpenNowPlaying { get; set; } = true;

    /// <summary>一首歌放完后关闭网易云。</summary>
    public bool CloseNetEaseAfterPlay { get; set; }

    /// <summary>上课期间不点歌（即使排歌时间冲突）。</summary>
    public bool BlockDuringClass { get; set; }

    /// <summary>上课铃响时自动关掉网易云。</summary>
    public bool KillNetEaseOnClass { get; set; }

    /// <summary>搜索结果第一条标题的位置（占客户区比例）。</summary>
    public double PlayRowX { get; set; } = 0.297;

    public double PlayRowY { get; set; } = 0.458;

    /// <summary>底部播放条封面位置（占客户区比例），点它进详情页。</summary>
    public double NowPlayingX { get; set; } = 0.070;

    public double NowPlayingY { get; set; } = 0.940;

    // ------- 课表联动 -------
    /// <summary>ClassIsland 程序目录（空则自动探测桌面上的 ClassIsland 文件夹）。</summary>
    public string? ClassIslandDir { get; set; }

    // ------- 更新与作者 -------
    /// <summary>GitHub 仓库（owner/repo），用于检测更新。</summary>
    public string GitHubRepo { get; set; } = "ChenyangLT/MusicBell";

    /// <summary>启动后检查一次更新。</summary>
    public bool CheckUpdateOnStart { get; set; } = true;

    [JsonIgnore]
    public string NetEasePathResolved => string.IsNullOrWhiteSpace(NetEasePath) ? NetEaseLocator.GuessPath() : NetEasePath!;
}
