using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MusicBell.Core.Models;
using MusicBell.Core.Services;
using Forms = System.Windows.Forms;

namespace MusicBell.App.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncFromConfig();
    }

    public void SyncFromConfig()
    {
        _loading = true;
        try
        {
            var app = App.Current;
            var cfg = app.Storage.Config;

            SwAutoStart.IsChecked = cfg.AutoStart;
            SwSilentStart.IsChecked = cfg.SilentStart;
            SwTray.IsChecked = cfg.MinimizeToTray;
            SwNotify.IsChecked = cfg.ShowNotification;
            DefaultTimeBox.Text = cfg.DefaultTime;

            NetEaseBox.Text = cfg.NetEasePath ?? cfg.NetEasePathResolved ?? "";
            BoxX.Text = cfg.SearchBoxX.ToString("0.###", CultureInfo.InvariantCulture);
            BoxY.Text = cfg.SearchBoxY.ToString("0.###", CultureInfo.InvariantCulture);
            DelayBox.Text = cfg.KeyDelayMs.ToString(CultureInfo.InvariantCulture);
            AdvanceBox.Text = cfg.AdvanceSeconds.ToString(CultureInfo.InvariantCulture);
            SwSearch.IsChecked = cfg.PlayViaSearch;
            SwFront.IsChecked = cfg.BringNetEaseToFront;
            SwClip.IsChecked = cfg.RestoreClipboard;
            SwAutoPlay.IsChecked = cfg.AutoPlayFirst;
            SwNowPlaying.IsChecked = cfg.OpenNowPlaying;
            SwCloseAfter.IsChecked = cfg.CloseNetEaseAfterPlay;

            ThemeDark.IsChecked = cfg.Theme == ThemeService.Dark;
            ThemeLight.IsChecked = cfg.Theme == ThemeService.Light;
            ThemeSystem.IsChecked = cfg.Theme == ThemeService.System;

            SwBlockClass.IsChecked = cfg.BlockDuringClass;
            SwKillClass.IsChecked = cfg.KillNetEaseOnClass;
            ClassStatusText.Text = DescribeClassSchedule(cfg);

            SwUpdate.IsChecked = cfg.CheckUpdateOnStart;
            RepoBox.Text = cfg.GitHubRepo ?? "ChenyangLT/MusicBell";

            DataDirText.Text = app.Storage.DataDir;
            LogPathText.Text = AppPaths.LogDir(app.Storage.DataDir);
            SwProtocol.IsChecked = cfg.EnableProtocol;
            PluginDirBox.Text = cfg.PluginExportDir ?? DefaultPluginDir();

            var version = PluginExporter.BundledPluginVersion();
            PluginVersionText.Text = version is null
                ? "⚠ 没有找到随程序附带的插件文件（plugin 目录）"
                : "附带插件版本：" + version + " · 与 ClassIsland 2.1.x 匹配";
        }
        finally
        {
            _loading = false;
        }
    }

    private static string DefaultPluginDir()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desktop, "MusicBell 插件");
    }

    private void Save() => App.Current.Storage.SaveConfig();

    private static string DescribeClassSchedule(AppConfig cfg)
    {
        var dir = ClassIslandSchedule.LocateDataDir(cfg.ClassIslandDir);
        if (dir is null) return "还找不到 ClassIsland 的课表，点「自动检测」或手动选择";
        var snap = ClassIslandSchedule.Get(dir, DateTime.Today);
        if (snap is null) return "找到了目录，但读不出今天的课表（检查是否为 2.x 的数据目录）";
        var now = DateTime.Now;
        var current = snap.CurrentClass(now);
        if (current is not null) return $"现在正上 {current.Subject}（{current.Start:HH:mm}-{current.End:HH:mm}）· {snap.PlanName}";
        var next = snap.NextClass(now);
        return next is null
            ? $"今天没有后续课程 · {snap.PlanName}"
            : $"现在没课，下节 {next.Subject} {next.Start:HH:mm} · {snap.PlanName}";
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if ((sender as FrameworkElement)?.Tag is not string mode) return;
        App.Current.ApplyTheme(mode);
        App.Current.Storage.Config.Theme = mode;
        Save();
    }

    private void AutoDetectClass_Click(object sender, RoutedEventArgs e)
    {
        var dir = ClassIslandSchedule.LocateDataDir(null);
        if (dir is null)
        {
            ToastWindow.Show("没找到 ClassIsland", "在设置里手动选一下它的安装目录", "", ToastKind.Error);
            return;
        }
        ClassIslandSchedule.Invalidate();
        ToastWindow.Show("找到了课表", dir, "", ToastKind.Success);
        SyncFromConfig();
    }

    private void ChooseClassDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Forms.FolderBrowserDialog { Description = "选择 ClassIsland 的安装目录" };
        if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
        App.Current.Storage.Config.ClassIslandDir = dlg.SelectedPath;
        Save();
        ClassIslandSchedule.Invalidate();
        SyncFromConfig();
        App.Current.Shell?.RefreshAll();
    }

    private void Repo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var text = RepoBox.Text.Trim();
        App.Current.Storage.Config.GitHubRepo = string.IsNullOrWhiteSpace(text) ? "ChenyangLT/MusicBell" : text;
        Save();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.Current.Storage.Config;
        if (!string.IsNullOrWhiteSpace(RepoBox.Text) && RepoBox.Text.Trim() != cfg.GitHubRepo)
        {
            cfg.GitHubRepo = RepoBox.Text.Trim();
            Save();
        }

        UpdateStatusText.Text = "正在检查…";
        var info = await UpdateChecker.CheckAsync(cfg.GitHubRepo ?? "ChenyangLT/MusicBell", App.VersionString());
        if (info is null)
        {
            UpdateStatusText.Text = "检查失败，稍后再试（也可能是网络连不上 GitHub）";
            return;
        }

        UpdateStatusText.Text = info.HasUpdate
            ? $"有新版 {info.LatestVersion}（当前 {info.CurrentVersion}）"
            : $"已是最新版本（{info.CurrentVersion}）";

        if (info.HasUpdate)
            App.Current.NotifyUpdate(info);
    }

    private void Switch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var tag = (sender as FrameworkElement)?.Tag as string;
        var on = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        var cfg = App.Current.Storage.Config;
        if (tag is null) return;

        switch (tag)
        {
            case "AutoStart":
                cfg.AutoStart = on;
                try { AutoStartService.SetEnabled(on); }
                catch (Exception ex) { ToastWindow.Show("自启动设置失败", ex.Message, "", ToastKind.Error); }
                break;
            case "SilentStart": cfg.SilentStart = on; break;
            case "MinimizeToTray": cfg.MinimizeToTray = on; break;
            case "ShowNotification": cfg.ShowNotification = on; break;
            case "PlayViaSearch": cfg.PlayViaSearch = on; break;
            case "BringNetEaseToFront": cfg.BringNetEaseToFront = on; break;
            case "RestoreClipboard": cfg.RestoreClipboard = on; break;
            case "AutoPlayFirst": cfg.AutoPlayFirst = on; break;
            case "OpenNowPlaying": cfg.OpenNowPlaying = on; break;
            case "CloseNetEaseAfterPlay": cfg.CloseNetEaseAfterPlay = on; break;
            case "BlockDuringClass": cfg.BlockDuringClass = on; break;
            case "KillNetEaseOnClass": cfg.KillNetEaseOnClass = on; break;
            case "CheckUpdateOnStart": cfg.CheckUpdateOnStart = on; break;
            case "EnableProtocol":
                cfg.EnableProtocol = on;
                if (on && !ProtocolService.IsEnabled()) ProtocolService.Register();
                break;
        }

        Save();
    }

    private void DefaultTime_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var text = DefaultTimeBox.Text.Trim();
        if (!TimeSpan.TryParseExact(text, @"hh\:mm", CultureInfo.InvariantCulture, out _) &&
            !TimeSpan.TryParseExact(text, @"h\:mm", CultureInfo.InvariantCulture, out _))
        {
            ToastWindow.Show("时间格式不对", "写成 07:50 这样的 24 小时制", "", ToastKind.Error);
            SyncFromConfig();
            return;
        }
        App.Current.Storage.Config.DefaultTime = text;
        Save();
    }

    private void NetEase_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var text = NetEaseBox.Text.Trim();
        App.Current.Storage.Config.NetEasePath = string.IsNullOrWhiteSpace(text) ? null : text;
        Save();
    }

    private void Number_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var tag = (sender as FrameworkElement)?.Tag as string;
        if (tag is null) return;
        var text = ((TextBox)sender).Text.Trim().Replace(',', '.');
        var cfg = App.Current.Storage.Config;

        switch (tag)
        {
            case "SearchBoxX":
            case "SearchBoxY":
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
                {
                    ToastWindow.Show("比例填个数字", "例如 0.304", "", ToastKind.Error);
                    SyncFromConfig();
                    return;
                }
                var clamped = Math.Clamp(ratio, 0, 1);
                if (tag == "SearchBoxX") cfg.SearchBoxX = clamped;
                else cfg.SearchBoxY = clamped;
                break;
            case "KeyDelayMs":
                if (!int.TryParse(text, out var delay)) { SyncFromConfig(); return; }
                cfg.KeyDelayMs = Math.Clamp(delay, 20, 3000);
                break;
            case "AdvanceSeconds":
                if (!int.TryParse(text, out var adv)) { SyncFromConfig(); return; }
                cfg.AdvanceSeconds = Math.Clamp(adv, 0, 600);
                break;
        }

        Save();
        SyncFromConfig();
    }

    private void NetEase_Detect_Click(object sender, RoutedEventArgs e)
    {
        var guess = NetEaseLocator.GuessPath();
        if (string.IsNullOrWhiteSpace(guess))
        {
            ToastWindow.Show("没找到网易云", "手动点「选择」指定 cloudmusic.exe", "", ToastKind.Error);
            return;
        }
        NetEaseBox.Text = guess;
        App.Current.Storage.Config.NetEasePath = guess;
        Save();
        ToastWindow.Show("找到了网易云", guess, "", ToastKind.Success);
    }

    private void NetEase_Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择网易云音乐的主程序",
            Filter = "cloudmusic.exe|cloudmusic.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        NetEaseBox.Text = dialog.FileName;
        App.Current.Storage.Config.NetEasePath = dialog.FileName;
        Save();
    }

    private void TestPlay_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this)!,
                "会用当前设置打开网易云，在搜索框里搜「周杰伦 晴天」。\n要现在试吗？", "测试播放",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        App.Current.PlayExternal("周杰伦 晴天", "测试播放");
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e) => OpenFolder(App.Current.Storage.DataDir);

    private void OpenLog_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogDir(App.Current.Storage.DataDir));

    private void MoveData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选一个新的配置目录（建议空文件夹或已有 MusicBell 数据的目录）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;

        try
        {
            App.Current.Storage.MoveDataDir(dialog.SelectedPath);
            SyncFromConfig();
            ToastWindow.Show("配置已迁移", dialog.SelectedPath, "原目录的文件已复制过去", ToastKind.Success);
            OpenFolder(dialog.SelectedPath);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("迁移失败", ex.Message, "", ToastKind.Error);
        }
    }

    private void ResetData_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this)!,
                $"把数据目录恢复到默认位置？\n{AppPaths.DefaultDataDir}\n\n现有的排歌会被复制过去。",
                "恢复默认目录", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        try
        {
            App.Current.Storage.ResetDataDir();
            SyncFromConfig();
            ToastWindow.Show("已恢复默认目录", App.Current.Storage.DataDir, "", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("恢复失败", ex.Message, "", ToastKind.Error);
        }
    }

    private void RegisterProtocol_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProtocolService.Register();
            ToastWindow.Show("协议已注册", "musicbell:// 现在能叫醒本软件了", "", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("注册失败", ex.Message, "", ToastKind.Error);
        }
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        var url = ProtocolService.BuildPlayUrl("周杰伦 晴天", null, "示例排歌", "classisland");
        try
        {
            Clipboard.SetText(url);
            ToastWindow.Show("已复制", url, "把这个链接填到网易云或别的地方都行", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("复制失败", ex.Message, "", ToastKind.Error);
        }
    }

    private void PluginDir_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var text = PluginDirBox.Text.Trim();
        App.Current.Storage.Config.PluginExportDir = string.IsNullOrWhiteSpace(text) ? null : text;
        Save();
    }

    private void ChoosePluginDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择一个文件夹用来放插件文件",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        PluginDirBox.Text = dialog.SelectedPath;
        App.Current.Storage.Config.PluginExportDir = dialog.SelectedPath;
        Save();
    }

    private void ExportPlugin_Click(object sender, RoutedEventArgs e)
    {
        var target = PluginDirBox.Text.Trim();
        if (target.Length == 0) target = DefaultPluginDir();

        try
        {
            var result = PluginExporter.Export(target);
            if (!result.Success)
            {
                ToastWindow.Show("释放插件失败", result.Message, "", ToastKind.Error);
                return;
            }

            App.Current.Storage.Config.PluginExportDir = target;
            Save();
            ToastWindow.Show("插件已释放", result.Message, "在 ClassIsland 里加载它就行", ToastKind.Success);
            if (result.Folder is not null) OpenFolder(result.Folder);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("释放插件失败", ex.Message, "", ToastKind.Error);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ToastWindow.Show("打不开目录", ex.Message, "", ToastKind.Error);
        }
    }
}
