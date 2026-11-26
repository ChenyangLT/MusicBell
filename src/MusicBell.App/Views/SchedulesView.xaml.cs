using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MusicBell.App.Dialogs;
using MusicBell.App.Helpers;
using MusicBell.Core.Models;

namespace MusicBell.App.Views;

public partial class SchedulesView : UserControl
{
    private static Brush EnabledBg => ThemeService.Brush("ChipOnBg");
    private static Brush EnabledFg => ThemeService.Brush("ChipTextOn");
    private static Brush DisabledBg => ThemeService.Brush("ChipOffBg");
    private static Brush DisabledFg => ThemeService.Brush("ChipOffFg");

    private string _filter = "all";

    public SchedulesView()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
        ThemeService.Changed += _ => Dispatcher.BeginInvoke(new Action(Reload));
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public void Reload()
    {
        var songs = App.Current.Storage.Songs.ToList();
        var now = DateTime.Now;

        IEnumerable<SongEntry> view = _filter switch
        {
            "today" => songs.Where(s => s.OccursOn(now.Date)),
            "week" => songs.Where(s =>
            {
                for (var d = 0; d < 7; d++)
                    if (s.OccursOn(now.Date.AddDays(d))) return true;
                return false;
            }),
            "repeat" => songs.Where(s => s.Repeat != RepeatModes.None),
            "disabled" => songs.Where(s => !s.Enabled),
            _ => songs
        };

        var rows = view
            .Select(s => new { Song = s, At = NextAt(s, now) })
            .OrderBy(x => x.At.HasValue ? 0 : 1)
            .ThenBy(x => x.At ?? DateTime.MaxValue)
            .Select(x => MakeRow(x.Song, x.At))
            .ToList();

        SongList.ItemsSource = rows;
        _ = FillCoversAsync(rows);
        CountText.Text = songs.Count + " 首";
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _filter switch
        {
            "today" => "今天没有排歌",
            "week" => "接下来 7 天都很安静",
            "repeat" => "还没有重复的排歌",
            "disabled" => "没有被停用的排歌",
            _ => "这里还没有排歌"
        };
    }

    private static DateTime? NextAt(SongEntry s, DateTime now)
    {
        if (!s.Enabled) return null;
        if (s.Repeat == RepeatModes.Daily || s.Repeat == RepeatModes.Weekly)
        {
            for (var d = 0; d < 8; d++)
            {
                var day = now.Date.AddDays(d);
                if (!s.OccursOn(day)) continue;
                var at = day + s.Time.TimeOfDay;
                if (at >= now) return at;
            }
            return null;
        }
        return s.Time >= now ? s.Time : null;
    }

    private static ScheduleRowVm MakeRow(SongEntry s, DateTime? at)
    {
        var parts = new List<string>();
        if (s.Source == SongSources.Bili) parts.Add("B 站");
        if (!string.IsNullOrWhiteSpace(s.Requester)) parts.Add("点歌 " + s.Requester);
        if (!string.IsNullOrWhiteSpace(s.Artist)) parts.Add(s.Artist!);
        if (!string.IsNullOrWhiteSpace(s.RepeatText)) parts.Add(s.RepeatText);
        if (at.HasValue) parts.Add(Relative(at.Value));
        else if (!s.Enabled) parts.Add("已停用，不会自动播放");
        else parts.Add("时间已经过去");

        var row = new ScheduleRowVm
        {
            Entry = s,
            TimeText = s.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
            DateText = at.HasValue ? at.Value.ToString("MM-dd", CultureInfo.InvariantCulture) : "—",
            Title = s.Title,
            SubLine = string.Join(" · ", parts),
            TimeBg = s.Enabled ? EnabledBg : DisabledBg,
            TimeFg = s.Enabled ? EnabledFg : DisabledFg,
            Enabled = s.Enabled,
            StateText = s.Enabled ? "启用" : "停用",
            LabelText = s.Label ?? "",
            LabelVis = string.IsNullOrWhiteSpace(s.Label) ? Visibility.Collapsed : Visibility.Visible,
            Cover = CoverBrush(s),
            CoverVis = CoverBrush(s) is null ? Visibility.Collapsed : Visibility.Visible,
            CoverFaintVis = CoverBrush(s) is null ? Visibility.Visible : Visibility.Collapsed
        };
        return row;
    }

    private async System.Threading.Tasks.Task FillCoversAsync(List<ScheduleRowVm> rows)
    {
        var changed = false;
        foreach (var row in rows)
        {
            if (row.CoverVis == Visibility.Visible) continue;
            var src = await CoverService.EnsureForAsync(row.Entry).ConfigureAwait(true);
            if (src is null) continue;
            row.SetCover(src);
            changed = true;
        }
        if (changed) SongList.Items.Refresh();
    }

    private static long IdOf(SongEntry s) => long.TryParse(s.SongId, out var id) ? id : 0;

    private static ImageBrush? CoverBrush(SongEntry s)
        => s.Source == SongSources.Bili
            ? CoverService.ToBrush(CoverService.TryCachedBili(s.SongId))
            : CoverService.ToBrush(CoverService.TryCached(IdOf(s)));

    private static string Relative(DateTime at)
    {
        var span = at - DateTime.Now;
        if (span.TotalMinutes < 1) return "马上";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟后";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时 {span.Minutes} 分后";
        return $"{span.Days} 天 {span.Hours} 小时后";
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag) _filter = tag;
        Reload();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        var entry = new SongEntry
        {
            Title = "",
            Time = now.Date + TimeSpan.FromMinutes(7 * 60 + 50),
            Enabled = true,
            Repeat = RepeatModes.None
        };
        var dialog = new SongEditorWindow(entry, true) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        App.Current.Storage.Add(dialog.Result);
        Reload();
        App.Current.Shell?.RefreshNext();
        ToastWindow.Show("已经排上了", $"{dialog.Result.Time:MM-dd HH:mm} · {dialog.Result.Title}",
            "到点会自动放", ToastKind.Success);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScheduleRowVm row) return;
        row.Entry.Enabled = (sender as CheckBox)?.IsChecked == true;
        App.Current.Storage.SaveSongs();
        Reload();
        App.Current.Shell?.RefreshNext();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScheduleRowVm row) return;
        App.Current.PlayEntry(row.Entry);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScheduleRowVm row) return;
        EditRow(row.Entry);
    }

    private void Row_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if ((sender as FrameworkElement)?.Tag is not ScheduleRowVm row) return;
        EditRow(row.Entry);
    }

    private void EditRow(SongEntry entry)
    {
        var dialog = new SongEditorWindow(entry, false) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        var storage = App.Current.Storage;
        var index = storage.Songs.IndexOf(entry);
        if (index >= 0)
        {
            storage.Songs[index] = dialog.Result;
            storage.SaveSongs();
        }
        Reload();
        App.Current.Shell?.RefreshNext();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScheduleRowVm row) return;
        var result = MessageBox.Show(Window.GetWindow(this)!,
            $"删除「{row.Title}」？\n删除后不会再自动播放。", "确认删除",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        App.Current.Storage.Remove(row.Entry);
        App.Current.Scheduler.Forget(row.Entry);
        Reload();
        App.Current.Shell?.RefreshNext();
    }
}

public sealed class ScheduleRowVm
{
    public ImageBrush? Cover { get; set; }
    public Visibility CoverVis { get; set; } = Visibility.Collapsed;
    public Visibility CoverFaintVis { get; set; } = Visibility.Visible;

    public void SetCover(ImageSource src)
    {
        Cover = CoverService.ToBrush(src);
        CoverVis = Visibility.Visible;
        CoverFaintVis = Visibility.Collapsed;
    }

    public SongEntry Entry { get; init; } = new();
    public string TimeText { get; init; } = "";
    public string DateText { get; init; } = "";
    public string Title { get; init; } = "";
    public string SubLine { get; init; } = "";
    public Brush TimeBg { get; init; } = Brushes.Transparent;
    public Brush TimeFg { get; init; } = Brushes.White;
    public bool Enabled { get; init; }
    public string StateText { get; init; } = "";
    public string LabelText { get; init; } = "";
    public Visibility LabelVis { get; init; } = Visibility.Collapsed;
}
