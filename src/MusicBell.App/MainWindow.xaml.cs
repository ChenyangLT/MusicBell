using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MusicBell.App.Helpers;
using MusicBell.App.Views;
using MusicBell.Core.Services;

namespace MusicBell.App;

public partial class MainWindow : Window
{
    private readonly CalendarView _calendar;
    private readonly SchedulesView _schedules;
    private readonly PeopleView _people;
    private readonly SettingsView _settings;
    private readonly AboutView _about;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();

        _calendar = new CalendarView();
        _schedules = new SchedulesView();
        _people = new PeopleView();
        _settings = new SettingsView();
        _about = new AboutView();

        Host.Content = _calendar;
        VersionText.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        SourceInitialized += (_, _) => WindowFx.Apply(this, ThemeService.IsLight);
        ThemeService.Changed += _ =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                WindowFx.Apply(this, ThemeService.IsLight);
                ThemeBtn.Content = ThemeService.IsLight ? "\uE708" : "\uE706";
            }));
        };
        ThemeBtn.Content = ThemeService.IsLight ? "\uE708" : "\uE706";

        _clock.Tick += (_, _) =>
        {
            UpdateNext();
            UpdateClass();
        };
        _clock.Start();
        UpdateNext();
        UpdateClass();
    }

    public void ShowInteractive()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void RefreshAll()
    {
        _calendar.Reload();
        _schedules.Reload();
        _people.Reload();
        _settings.SyncFromConfig();
        UpdateNext();
        UpdateClass();
    }

    public void SyncSettingsFromConfig() => _settings.SyncFromConfig();

    public void RefreshNext() => UpdateNext();

    public void GoToSettings() => NavSettings.IsChecked = true;

    public void GoToCalendar() => NavCalendar.IsChecked = true;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.Current.Storage.Config.MinimizeToTray) return;
        e.Cancel = true;
        Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        ToastWindow.Show("已缩到托盘", "到点会自动放歌", "双击托盘图标可以打开", ToastKind.Info);
    }

    private void UpdateNext()
    {
        var app = App.Current;
        var next = app.Storage.NextOccurrence(DateTime.Now, 60);
        if (next is null)
        {
            NextTitle.Text = "—";
            NextTime.Text = "还没有安排";
            NextTime.Foreground = ThemeService.Brush("TextDim");
            return;
        }
        var entry = next.Value.Entry;
        NextTitle.Text = string.IsNullOrWhiteSpace(entry.Label) ? entry.Title : entry.Label!;
        var at = next.Value.At;
        var span = at - DateTime.Now;
        var when = span.TotalMinutes < 1 ? "马上" :
            span.TotalHours < 24 ? $"{span.Hours} 小时 {span.Minutes} 分后" :
            $"{span.Days} 天 {span.Hours} 小时后";
        NextTime.Text = $"{at:MM-dd HH:mm} · {when}";
        NextTime.Foreground = span.TotalMinutes <= 60
            ? ThemeService.Brush("Accent")
            : ThemeService.Brush("TextDim");
    }

    /// <summary>左下角课程卡片：当前时间段 + 下节课 + 进度。</summary>
    private void UpdateClass()
    {
        try
        {
            ClassClock.Text = DateTime.Now.ToString("HH:mm");
            var app = App.Current;
            var dir = ClassIslandSchedule.LocateDataDir(app.Storage.Config.ClassIslandDir);
            if (string.IsNullOrWhiteSpace(dir))
            {
                ClassTag.Text = "课表";
                ClassTitle.Text = "没找到 ClassIsland 课表";
                ClassSub.Text = "设置 → 课表联动 里指定目录";
                ClassBar.Value = 0;
                return;
            }
            var snap = ClassIslandSchedule.Get(dir!, DateTime.Today);
            if (snap is null || snap.Segments.Count == 0)
            {
                ClassTag.Text = "课表";
                ClassTitle.Text = "今天没有课表";
                ClassSub.Text = snap is null ? "课表文件读不出来" : "今天没有安排课程";
                ClassBar.Value = 0;
                return;
            }

            var now = DateTime.Now;
            var current = snap.Current(now);
            var currentClass = snap.CurrentClass(now);
            var next = snap.NextClass(now);
            var week = snap.WeekText;
            ClassTag.Text = (string.IsNullOrEmpty(week) ? "" : week + " · ") +
                            (currentClass is not null ? "第" + currentClass.LessonIndex + "节" : "课表");

            if (current is null)
            {
                ClassTag.Text = string.IsNullOrEmpty(week) ? "课表" : week;
                ClassTitle.Text = next is not null ? "下节 " + next.Name : "今天课上完了";
                ClassSub.Text = next is not null ? next.Start.ToString("HH:mm") + " 开始" : "好好休息";
                ClassBar.Value = 0;
                return;
            }

            if (currentClass is not null)
            {
                ClassTag.Text = "上课中 · 第" + currentClass.LessonIndex + "节";
                ClassTitle.Text = currentClass.Name;
                ClassSub.Text = next is not null
                    ? currentClass.End.ToString("HH:mm") + " 下课 · 下节 " + next.Name
                    : currentClass.End.ToString("HH:mm") + " 下课 · 今天没课了";
            }
            else
            {
                ClassTag.Text = string.IsNullOrWhiteSpace(current.BreakName) ? "课间" : current.BreakName;
                ClassTitle.Text = next is not null ? "下节 " + next.Name : "今天课上完了";
                ClassSub.Text = next is not null
                    ? next.Start.ToString("HH:mm") + " 开始 · " + Countdown(next.Start - now)
                    : "好好休息";
            }
            if (currentClass is not null)
            {
                ClassBar.Value = current.ProgressAt(now);
            }
            else if (next is not null)
            {
                var total = (next.Start - current.End).TotalSeconds;
                var passed = (now - current.End).TotalSeconds;
                ClassBar.Value = total <= 0 ? 0 : Math.Clamp(passed / total, 0, 1);
            }
            else
            {
                ClassBar.Value = 0;
            }
        }
        catch (Exception ex)
        {
            ClassTitle.Text = "课表读不出来";
            ClassSub.Text = ex.Message;
        }
    }

    private static string Countdown(TimeSpan span)
    {
        if (span.TotalSeconds <= 0) return "马上";
        if (span.TotalMinutes < 1) return "还有 " + Math.Ceiling(span.TotalSeconds) + " 秒";
        if (span.TotalHours < 1) return "还有 " + Math.Ceiling(span.TotalMinutes) + " 分钟";
        return "还有 " + (int)span.TotalHours + " 小时 " + span.Minutes + " 分";
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Host is null) return;
        var tag = (sender as FrameworkElement)?.Tag as string ?? "calendar";
        Host.Content = tag switch
        {
            "schedules" => _schedules,
            "people" => _people,
            "settings" => _settings,
            "about" => _about,
            _ => _calendar
        };
        switch (tag)
        {
            case "schedules": _schedules.Reload(); break;
            case "people": _people.Reload(); break;
            case "settings": _settings.SyncFromConfig(); break;
            case "calendar": _calendar.Reload(); break;
        }
        e.Handled = true;
    }

    private void QuickAdd_Click(object sender, RoutedEventArgs e)
    {
        NavCalendar.IsChecked = true;
        _calendar.FocusQuickAdd();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var next = ThemeService.IsLight ? ThemeService.Dark : ThemeService.Light;
        App.Current.ApplyTheme(next);
    }

    private void Author_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(AuthorProfile.GithubUrl) { UseShellExecute = true }); } catch { }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMax();
            return;
        }
        try { DragMove(); } catch { }
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) => ToggleMax();

    private void ToggleMax() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (App.Current.Storage.Config.MinimizeToTray)
        {
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                ToastWindow.Show("已缩到托盘", "到点会自动放歌", "双击托盘图标可以打开", ToastKind.Info);
            }
            return;
        }
        App.Current.ExitApp();
    }
}
