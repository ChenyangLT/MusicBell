using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MusicBell.App.Helpers;
using MusicBell.Core.Services;

namespace MusicBell.App.Views;

public partial class AboutView : UserControl
{
    private const string Example = "musicbell://play?q=%E5%91%A8%E6%9D%B0%E4%BC%A6%20%E6%99%B4%E5%A4%A9&label=%E8%AF%BE%E5%89%8D%E4%B8%80%E9%A6%96&src=classisland";

    public AboutView()
    {
        InitializeComponent();
        VersionText.Text = "版本 v" + (typeof(AboutView).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        ProtocolText.Text = Example;
        Loaded += async (_, _) => await LoadAuthorAsync();
    }

    private async System.Threading.Tasks.Task LoadAuthorAsync()
    {
        try
        {
            var profile = await AuthorProfile.GetAsync(App.Current.Storage.DataDir).ConfigureAwait(true);
            if (profile is null) return;
            AuthorName.Text = string.IsNullOrWhiteSpace(profile.Name) ? "Chen_yang_LT" : profile.Name;
            AuthorSub.Text = "B 站 " + (string.IsNullOrWhiteSpace(profile.Name) ? "Chen_yang_LT" : profile.Name)
                             + " · 头像来自 B 站主页";
            var brush = CoverService.FromFile(profile.AvatarPath);
            if (brush is not null)
            {
                AvatarBox.Background = brush;
                AvatarGlyph.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("作者信息加载失败：" + ex.Message);
        }
    }

    private void Github_Click(object sender, RoutedEventArgs e) => OpenUrl(AuthorProfile.GithubUrl);

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ToastWindow.Show("打不开链接", ex.Message, "", ToastKind.Error); }
    }

    private void GoSettings_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main) main.GoToSettings();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Example);
            ToastWindow.Show("已复制", "粘到 ClassIsland 的行动参数里就行", "", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("复制失败", ex.Message, "", ToastKind.Error);
        }
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenFolder(App.Current.Storage.DataDir);

    private void OpenLog_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogDir(App.Current.Storage.DataDir));

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
