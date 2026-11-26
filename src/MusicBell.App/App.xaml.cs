using System.Windows;
using System.Windows.Threading;
using MusicBell.App.Services;
using MusicBell.Core.Models;
using MusicBell.Core.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MusicBell.App;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public StorageService Storage { get; private set; } = null!;
    public SongPlayerService Player { get; private set; } = null!;

    /// <summary>内置播放器（网易云直链 + 歌词）。</summary>
    public PlayerService BuiltinPlayer { get; private set; } = null!;
    public SongScheduler Scheduler { get; private set; } = null!;
    public SingleInstance Single { get; private set; } = null!;
    public MainWindow? Shell { get; private set; }
    public bool StartedSilent { get; private set; }

    private Forms.NotifyIcon? _tray;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args ?? Array.Empty<string>();
        StartedSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += OnUnhandled;

        Single = new SingleInstance();
        if (!Single.TryAcquire())
        {
            var payload = NormalizePayload(args);
            if (string.IsNullOrWhiteSpace(payload)) payload = "--show";
            SingleInstance.SendToExisting(payload);
            Shutdown(0);
            return;
        }

        Storage = StorageService.Load();
        ThemeService.Apply(Storage.Config.Theme);
        Player = new SongPlayerService(Storage);
        BuiltinPlayer = new PlayerService { Volume = Math.Clamp(Storage.Config.PlayerVolume, 0, 1) };
        Player.BuiltinPlayRequested += OnBuiltinPlayRequested;
        Scheduler = new SongScheduler(Storage);
        Scheduler.Due += OnDue;
        Scheduler.Start();
        StartClassWatcher();
        _ = PrefetchCoversAsync();

        Single.StartServer();
        Single.CommandReceived += cmd => Dispatcher.BeginInvoke(new Action(() => HandleCommand(cmd)));

        if (string.IsNullOrWhiteSpace(Storage.Config.NetEasePath))
        {
            var guess = NetEaseLocator.GuessPath();
            if (!string.IsNullOrWhiteSpace(guess))
            {
                Storage.Config.NetEasePath = guess;
                Storage.SaveConfig();
            }
        }

        if (Storage.Config.EnableProtocol && !ProtocolService.IsRegisteredForCurrentExe()) ProtocolService.Register();
        // 每次启动都顺手对一遍：换过目录、被任务管理器禁用过，都在这里修回来
        var autoStart = AutoStartService.Ensure(Storage.Config.AutoStart);
        if (!autoStart.Ok) AppLog.Warn("开机自启没弄好：" + autoStart.Message);

        CreateTray();

        Shell = new MainWindow();
        if (!StartedSilent)
        {
            Shell.ShowInteractive();
        }

        var cmd = NormalizePayload(args);
        if (!string.IsNullOrWhiteSpace(cmd))
            Dispatcher.BeginInvoke(new Action(() => HandleCommand(cmd)), DispatcherPriority.ApplicationIdle);

        AppLog.Info("启动完成（静默=" + StartedSilent + "）");

        if (Storage.Config.CheckUpdateOnStart) _ = CheckUpdateAsync();
    }

    public static string VersionString()
    {
        var v = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return v.Revision > 0 ? v.ToString(3) : v.ToString(2);
    }

    // ---------- 更新 ----------
    private async Task CheckUpdateAsync()
    {
        try
        {
            var info = await UpdateChecker.CheckAsync(Storage.Config.GitHubRepo ?? "ChenyangLT/MusicBell", VersionString())
                .ConfigureAwait(false);
            if (info is { HasUpdate: true })
                await Dispatcher.BeginInvoke(new Action(() => NotifyUpdate(info))).Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("更新检查失败：" + ex.Message);
        }
    }

    public void NotifyUpdate(UpdateInfo info)
    {
        AppLog.Info($"发现新版本 {info.LatestVersion}（当前 {info.CurrentVersion}）→ {info.Url}");
        ToastWindow.Show("发现新版本 " + info.LatestVersion, "当前 " + info.CurrentVersion + "，可以到 GitHub 下载新版",
            info.Url, ToastKind.Info, 9000);
    }

    // ---------- 主题 ----------
    public void ApplyTheme(string preference)
    {
        Storage.Config.Theme = preference;
        Storage.SaveConfig();
        ThemeService.Apply(preference);
    }

    // ---------- 课表联动 ----------
    private DispatcherTimer? _classTimer;
    private string? _closedClassKey;

    /// <summary>把排歌的封面慢慢缓存到本地，界面一打开就有图。</summary>
    private static async Task PrefetchCoversAsync()
    {
        try
        {
            var ids = App.Current.Storage.Songs
                .Select(s => long.TryParse(s.SongId, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .Take(60)
                .ToList();
            foreach (var id in ids)
            {
                if (Helpers.CoverService.TryCached(id) != null) continue;
                await Helpers.CoverService.EnsureAsync(id).ConfigureAwait(false);
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("封面预取失败：" + ex.Message);
        }
    }

    private void StartClassWatcher()
    {
        _classTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _classTimer.Tick += (_, _) => CheckClass();
        _classTimer.Start();
        Dispatcher.BeginInvoke(new Action(CheckClass));
    }

    /// <summary>当前时刻是否正在上课（按 ClassIsland 课表算）。</summary>
    public bool IsInClassNow()
    {
        try
        {
            var dir = ClassIslandSchedule.LocateDataDir(Storage.Config.ClassIslandDir);
            if (string.IsNullOrWhiteSpace(dir)) return false;
            var snap = ClassIslandSchedule.Get(dir!, DateTime.Today);
            return snap?.IsInClass(DateTime.Now) == true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("课表判断失败：" + ex.Message);
            return false;
        }
    }

    private void CheckClass()
    {
        try
        {
            var cfg = Storage.Config;
            var dir = ClassIslandSchedule.LocateDataDir(cfg.ClassIslandDir);
            if (string.IsNullOrWhiteSpace(dir)) return;
            var snap = ClassIslandSchedule.Get(dir!, DateTime.Today);
            if (snap is null) return;

            var now = DateTime.Now;
            var lead = Math.Clamp(cfg.CloseLeadSeconds, 0, 1800);
            var inClass = snap.IsInClass(now);
            var current = snap.CurrentClass(now);
            var next = snap.NextClass(now);
            var start = current?.Start ?? next?.Start;
            if (start is null) return;

            var key = start.Value.ToString("yyyyMMddHHmm");
            if (key == _closedClassKey) return;
            var untilStart = (start.Value - now).TotalSeconds;
            // 课中（含提前量已过）或正好落在「上课前 N 秒」窗口里才动手
            var due = inClass ? untilStart <= lead : untilStart <= lead && untilStart > -5;
            if (!due) return;
            _closedClassKey = key;
            if (!cfg.CloseOnClass) return;
            CloseForClass();
        }
        catch (Exception ex)
        {
            AppLog.Warn("课表检查失败：" + ex.Message);
        }
    }

    /// <summary>上课了：按设置关掉正在放的东西（内置播放器 / 网易云 / B 站那一页）。</summary>
    public void CloseForClass()
    {
        var cfg = Storage.Config;
        var parts = new List<string>();
        if (cfg.StopBuiltinOnClass && BuiltinPlayer.HasTrack)
        {
            BuiltinPlayer.Stop();
            parts.Add("内置播放器");
        }
        if (cfg.CloseClientOnClass)
        {
            parts.Add("网易云");
            _ = Task.Run(() => NetEaseAutomation.CloseNetEase(msg => AppLog.Info(msg)));
        }
        if (cfg.CloseBiliTabOnClass)
        {
            parts.Add("B 站标签页");
            _ = Task.Run(() => BrowserTabs.CloseAll(Storage.DataDir, msg => AppLog.Info(msg)));
        }
        if (parts.Count == 0) return;
        AppLog.Info("到上课时间（提前 " + cfg.CloseLeadSeconds + " 秒），关闭：" + string.Join("、", parts));
        if (cfg.ShowNotification)
            ToastWindow.Show("上课了", "已关闭 " + string.Join("、", parts),
                cfg.CloseLeadSeconds > 0 ? "提前 " + cfg.CloseLeadSeconds + " 秒关的" : "整点关闭", ToastKind.Info);
    }

    // ---------- 内置播放器 ----------
    private void OnBuiltinPlayRequested(PlayerTrack track) =>
        Dispatcher.BeginInvoke(new Action(async () => await PlayBuiltinAsync(track)));

    private async Task PlayBuiltinAsync(PlayerTrack track)
    {
        try
        {
            var t = track;
            if (t.SongId > 0)
            {
                try
                {
                    var details = await new NeteaseSearchClient().DetailsAsync(new[] { t.SongId }).ConfigureAwait(true);
                    var d = details.FirstOrDefault();
                    if (d is not null)
                        t = t with
                        {
                            Title = string.IsNullOrWhiteSpace(d.Name) ? t.Title : d.Name,
                            Artist = d.Artist,
                            CoverUrl = string.IsNullOrWhiteSpace(d.CoverUrl) ? t.CoverUrl : d.CoverUrl
                        };
                }
                catch { }
            }

            var ok = await BuiltinPlayer.PlayAsync(t, Storage.Config.MusicCookie, m => AppLog.Info(m)).ConfigureAwait(true);
            if (!ok)
                ToastWindow.Show("内置播放器没放起来", BuiltinPlayer.LastError ?? t.Title,
                    "设置 → 播放器 里可以改成走客户端", ToastKind.Error);
        }
        catch (Exception ex)
        {
            AppLog.Warn("内置播放器异常：" + ex.Message);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("界面异常", e.Exception);
        e.Handled = true;
        try { ToastWindow.Show("出了点小问题", e.Exception.Message, "已记录到日志", ToastKind.Error); } catch { }
    }

    private static string NormalizePayload(string[]? args)
    {
        if (args is null) return "";
        foreach (var a in args)
        {
            if (a.StartsWith("musicbell://", StringComparison.OrdinalIgnoreCase)) return a;
            if (a.Equals("--play-next", StringComparison.OrdinalIgnoreCase)) return a;
            if (a.StartsWith("--play", StringComparison.OrdinalIgnoreCase)) return a;
            if (a.StartsWith("--show", StringComparison.OrdinalIgnoreCase)) return a;
            foreach (var flag in new[] { "--stop", "--pause", "--close", "--open-native" })
                if (a.Equals(flag, StringComparison.OrdinalIgnoreCase)) return a;
        }
        return "";
    }

    public void HandleCommand(string? raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return;

        if (raw.StartsWith("musicbell://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(raw);
                if (uri.Host.Equals("play", StringComparison.OrdinalIgnoreCase))
                {
                    var dict = ParseQuery(uri.Query);
                    dict.TryGetValue("q", out var q);
                    dict.TryGetValue("label", out var label);
                    dict.TryGetValue("id", out var id);
                    if (!string.IsNullOrWhiteSpace(q) || !string.IsNullOrWhiteSpace(id))
                        PlayExternal(q ?? "", label, string.IsNullOrWhiteSpace(id) ? null : id);
                }
                else if (uri.Host.Equals("next", StringComparison.OrdinalIgnoreCase))
                {
                    PlayNextNow();
                }
                else if (uri.Host.Equals("show", StringComparison.OrdinalIgnoreCase))
                {
                    ShowMain();
                }
                else if (uri.Host.Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    StopBuiltin();
                }
                else if (uri.Host.Equals("pause", StringComparison.OrdinalIgnoreCase))
                {
                    TogglePause();
                }
                else if (uri.Host.Equals("close", StringComparison.OrdinalIgnoreCase))
                {
                    CloseMusic();
                }
                else if (uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase))
                {
                    OpenNativeClient();
                }
            }
            catch (Exception ex) { AppLog.Warn("解析协议失败：" + ex.Message); }
            return;
        }

        if (raw.StartsWith("--show", StringComparison.OrdinalIgnoreCase)) { ShowMain(); return; }
        if (raw.Equals("--play-next", StringComparison.OrdinalIgnoreCase)) { PlayNextNow(); return; }
        if (raw.Equals("--stop", StringComparison.OrdinalIgnoreCase)) { StopBuiltin(); return; }
        if (raw.Equals("--pause", StringComparison.OrdinalIgnoreCase)) { TogglePause(); return; }
        if (raw.Equals("--close", StringComparison.OrdinalIgnoreCase)) { CloseMusic(); return; }
        if (raw.Equals("--open-native", StringComparison.OrdinalIgnoreCase)) { OpenNativeClient(); return; }
        if (raw.StartsWith("--play ", StringComparison.OrdinalIgnoreCase))
        {
            PlayExternal(raw.Substring(7).Trim().Trim('"'), null);
            return;
        }
        if (raw.StartsWith("--play=", StringComparison.OrdinalIgnoreCase))
        {
            PlayExternal(raw.Substring(7).Trim().Trim('"'), null);
            return;
        }
        if (raw.StartsWith("--play", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw.Length > 6 ? raw.Substring(6).Trim().Trim('"') : "";
            if (rest.Length > 0) PlayExternal(rest, null);
            return;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        query = query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            var k = Uri.UnescapeDataString(kv[0]);
            var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
            if (k.Length > 0) dict[k] = v;
        }
        return dict;
    }

    private void OnDue(SongEntry entry, DateTime at)
    {
        AppLog.Info($"到点：{entry.Display}（{at:yyyy-MM-dd HH:mm}）");
        if (Storage.Config.BlockDuringClass && IsInClassNow())
        {
            AppLog.Info("正在上课，按设置跳过这次排歌");
            if (Storage.Config.ShowNotification)
                ToastWindow.Show("上课中，先不点歌", entry.Display, "已按设置跳过", ToastKind.Info);
            return;
        }
        var title = string.IsNullOrWhiteSpace(entry.Label) ? "该放歌了" : entry.Label!;
        var sub = entry.Title + (string.IsNullOrWhiteSpace(entry.Artist) ? "" : " · " + entry.Artist);
        if (Storage.Config.ShowNotification)
            ToastWindow.Show(title, sub, "正在打开网易云…", ToastKind.Playing);

        _ = Task.Run(async () =>
        {
            var result = await Player.PlayAsync(entry).ConfigureAwait(false);
            AppLog.Info($"排歌结果：{result.Success} / {result.Message}");
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!result.Success)
                    ToastWindow.Show("没能自动播放", result.Message, "可在设置里调整网易云路径", ToastKind.Error);
                Storage.SaveSongs();
                Shell?.RefreshAll();
            }));
        });
    }

    public async void PlayExternal(string query, string? label, string? songId = null)
    {
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(songId)) return;
        var shown = string.IsNullOrWhiteSpace(query) ? "网易云歌曲 " + songId : query;
        ToastWindow.Show(string.IsNullOrWhiteSpace(label) ? "正在点歌" : label!, shown, "来自 ClassIsland / 外部调用", ToastKind.Playing);
        var result = await Task.Run(() => Player.PlayAsync(query, songId)).ConfigureAwait(true);
        AppLog.Info($"外部点歌：{query} → {result.Success} / {result.Message} / {result.Elapsed.TotalMilliseconds:F0}ms");
        if (!result.Success)
            ToastWindow.Show("没能自动播放", result.Message, "可在设置里调整网易云路径", ToastKind.Error);
        else
            ToastWindow.Show("已开始搜索播放", query, $"耗时 {result.Elapsed.TotalMilliseconds:F0} ms", ToastKind.Success);
    }

    /// <summary>界面里手动播放某条排歌（网易云 / B 站都走这里）。</summary>
    public void PlayEntry(SongEntry entry)
    {
        if (entry is null) return;
        if (Storage.Config.ShowNotification)
            ToastWindow.Show("正在点歌", entry.Display, SongSources.Name(entry.Source), ToastKind.Playing);
        _ = Task.Run(async () =>
        {
            var result = await Player.PlayAsync(entry).ConfigureAwait(false);
            AppLog.Info($"手动点歌：{entry.Display} → {result.Success} / {result.Message}");
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!result.Success)
                    ToastWindow.Show("没能自动播放", result.Message, "可在设置里调整播放方式", ToastKind.Error);
                else if (Storage.Config.ShowNotification)
                    ToastWindow.Show("已开始播放", entry.Display, result.Message, ToastKind.Success);
                Storage.SaveSongs();
                Shell?.RefreshAll();
            }));
        });
    }

    public void PlayNextNow()
    {
        var next = Storage.NextOccurrence(DateTime.Now, 30);
        if (next is null)
        {
            ToastWindow.Show("还没有排歌", "先到日历里安排一首吧", "", ToastKind.Info);
            return;
        }
        _ = Task.Run(async () =>
        {
            var r = await Player.PlayAsync(next.Value.Entry).ConfigureAwait(false);
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                ToastWindow.Show(r.Success ? "已播放" : "播放失败", next.Value.Entry.Title, r.Message, r.Success ? ToastKind.Success : ToastKind.Error);
                Shell?.RefreshAll();
            }));
        });
    }

    /// <summary>停掉内置播放器（客户端那边不动）。</summary>
    public void StopBuiltin()
    {
        if (!BuiltinPlayer.HasTrack)
        {
            ToastWindow.Show("内置播放器没在放歌", "现在没有可以停的", "", ToastKind.Info);
            return;
        }
        BuiltinPlayer.Stop();
        AppLog.Info("外部调用：停止内置播放器");
        ToastWindow.Show("已停止播放", BuiltinPlayer.Track?.Title ?? "", "", ToastKind.Success);
    }

    /// <summary>播放 / 暂停切换。</summary>
    public void TogglePause()
    {
        if (!BuiltinPlayer.HasTrack)
        {
            ToastWindow.Show("内置播放器没在放歌", "先点一首歌吧", "", ToastKind.Info);
            return;
        }
        BuiltinPlayer.TogglePause();
        AppLog.Info($"外部调用：{(BuiltinPlayer.IsPlaying ? "继续" : "暂停")}播放");
        ToastWindow.Show(BuiltinPlayer.IsPlaying ? "继续播放" : "已暂停", BuiltinPlayer.Track?.Title ?? "", "", ToastKind.Info);
    }

    /// <summary>关掉正在放音乐的客户端：内置播放器停掉 + 结束 cloudmusic.exe。</summary>
    public void CloseMusic()
    {
        if (BuiltinPlayer.HasTrack) BuiltinPlayer.Stop();
        _ = Task.Run(() =>
        {
            var closed = NetEaseAutomation.CloseNetEase(m => AppLog.Info("外部调用：" + m));
            Dispatcher.BeginInvoke(new Action(() =>
                ToastWindow.Show(closed ? "已关闭网易云音乐" : "没有找到网易云进程",
                    closed ? "上课 / 下课时用得上" : "可能本来就没开", "", closed ? ToastKind.Success : ToastKind.Info)));
        });
    }

    /// <summary>把网易云客户端叫到前台（不搜索，只是打开）。</summary>
    public void OpenNativeClient()
    {
        var exe = Storage.Config.NetEasePath ?? Storage.Config.NetEasePathResolved ?? NetEaseLocator.GuessPath() ?? "";
        _ = Task.Run(async () =>
        {
            var hwnd = await NetEaseAutomation.EnsureWindowAsync(exe, 25000, m => AppLog.Info("外部调用：" + m));
            await Dispatcher.BeginInvoke(new Action(() =>
                ToastWindow.Show(hwnd != IntPtr.Zero ? "已打开网易云音乐" : "没能打开网易云",
                    hwnd != IntPtr.Zero ? "窗口已在前台" : "可在设置里指定 cloudmusic.exe 的位置",
                    "", hwnd != IntPtr.Zero ? ToastKind.Success : ToastKind.Error)));
        });
    }

    public void ShowMain()
    {
        if (Shell is null) return;
        if (!Shell.IsVisible) Shell.Show();
        if (Shell.WindowState == WindowState.Minimized) Shell.WindowState = WindowState.Normal;
        Shell.ShowInteractive();
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        try { Scheduler.Stop(); } catch { }
        try
        {
            if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        }
        catch { }
        AppLog.Info("退出");
        Shutdown(0);
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon { Text = "MusicBell 点歌铃" };
        try
        {
            var res = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (res?.Stream is not null) _tray.Icon = new Drawing.Icon(res.Stream);
            else _tray.Icon = Drawing.SystemIcons.Application;
        }
        catch (Exception ex)
        {
            AppLog.Warn("托盘图标加载失败：" + ex.Message);
            _tray.Icon = Drawing.SystemIcons.Application;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开主界面", null, (_, _) => ShowMain());
        menu.Items.Add("立即播放下一首排歌", null, (_, _) => PlayNextNow());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var autoItem = new Forms.ToolStripMenuItem("开机自动启动") { CheckOnClick = true, Checked = Storage.Config.AutoStart };
        autoItem.Click += (_, _) =>
        {
            Storage.Config.AutoStart = autoItem.Checked;
            AutoStartService.SetEnabled(autoItem.Checked);
            Storage.SaveConfig();
            Shell?.SyncSettingsFromConfig();
        };
        menu.Items.Add(autoItem);

        var silentItem = new Forms.ToolStripMenuItem("静默驻留（不显示主界面）") { CheckOnClick = true, Checked = StartedSilent };
        silentItem.Click += (_, _) => StartedSilent = silentItem.Checked;
        menu.Items.Add(silentItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 MusicBell", null, (_, _) => ExitApp());

        _tray.ContextMenuStrip = menu;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowMain();
    }
}
