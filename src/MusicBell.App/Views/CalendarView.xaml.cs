using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MusicBell.App.Dialogs;
using MusicBell.App.Helpers;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

namespace MusicBell.App.Views;

public partial class CalendarView : UserControl
{
    private static readonly Brush Transparent = Brushes.Transparent;
    private static Brush SelectedBg => ThemeService.Brush("SelBg");
    private static Brush TileBg => ThemeService.Brush("TileBg");
    private static Brush TileBorder => ThemeService.Brush("TileBorder");
    private static Brush SelectedBorder => ThemeService.Brush("SelBorder");
    private static Brush TodayBg => ThemeService.Brush("TodayBg");
    private static Brush TodayBorder => ThemeService.Brush("TodayBorder");
    private static Brush ChipSelectedBg => ThemeService.Brush("CellChipSelBg");
    private static Brush ChipNormalBg => ThemeService.Brush("CellChipBg");
    private static Brush TextNormal => ThemeService.Brush("CellText");
    private static Brush TextDim => ThemeService.Brush("CellTextDim");
    private static Brush TextOut => ThemeService.Brush("CellTextOut");

    private readonly ObservableCollection<DayVm> _days = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly NeteaseSearchClient _client = new();
    private CancellationTokenSource? _cts;

    private DateTime _month;
    private DateTime _selected = DateTime.Today;

    public CalendarView()
    {
        InitializeComponent();
        var today = DateTime.Today;
        _month = new DateTime(today.Year, today.Month, 1);
        DayGrid.ItemsSource = _days;
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RunSearchAsync();
        };
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
        MonthText.Text = $"{_month:yyyy年M月}";
        BuildDays();
        UpdateSelectedDay();
    }

    public void FocusQuickAdd()
    {
        _selected = DateTime.Today;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        Reload();
        SearchBox.Focus();
    }

    private void BuildDays()
    {
        var storage = App.Current.Storage;
        var offset = ((int)_month.DayOfWeek + 6) % 7; // 周一开头
        var start = _month.AddDays(-offset);

        _days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var inMonth = date.Month == _month.Month && date.Year == _month.Year;
            var isSelected = date.Date == _selected.Date;
            var isToday = date.Date == DateTime.Today;
            var songs = storage.SongsOn(date);

            var chips = songs.Take(2)
                .Select(s => new ChipVm
                {
                    Text = s.Display,
                    Tip = $"{s.Time:HH:mm} {s.Display}",
                    Background = isSelected ? ChipSelectedBg : ChipNormalBg,
                    Fg = isSelected ? OnSelectedBrush : TextNormal,
                    Cover = CoverService.ToBrush(CoverService.TryCached(IdOf(s))),
                    CoverVis = CoverService.TryCached(IdOf(s)) is null ? Visibility.Collapsed : Visibility.Visible
                })
                .ToList();

            _days.Add(new DayVm
            {
                Date = date,
                Fg = !inMonth ? TextOut : isSelected ? OnSelectedBrush : TextNormal,
                Bg = isSelected ? SelectedBg : isToday ? TodayBg : inMonth ? TileBg : Transparent,
                BorderBrush = isSelected ? SelectedBorder : isToday ? TodayBorder : inMonth ? TileBorder : Transparent,
                TodayVis = isToday ? Visibility.Visible : Visibility.Collapsed,
                Chips = chips,
                MoreText = songs.Count > 2 ? $"+{songs.Count - 2} 首" : ""
            });
        }
    }

    private void UpdateSelectedDay()
    {
        var songs = App.Current.Storage.SongsOn(_selected);
        SelDateText.Text = _selected.ToString("M月d日 dddd", new CultureInfo("zh-CN"));
        SelCountText.Text = songs.Count == 0 ? "这天还没有排歌" : $"共 {songs.Count} 首 · 到点自动播";

        var rows = songs.Select(MakeRow).ToList();
        DaySongList.ItemsSource = rows;
        EmptyState.Visibility = songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = FillRowCoversAsync(rows);
    }

    private async System.Threading.Tasks.Task FillRowCoversAsync(List<SongRowVm> rows)
    {
        foreach (var row in rows)
        {
            if (row.CoverVis == Visibility.Visible) continue;
            var id = IdOf(row.Entry);
            if (id <= 0) continue;
            var src = await CoverService.EnsureAsync(id).ConfigureAwait(true);
            if (src is null) continue;
            row.SetCover(src);
        }
        DaySongList.Items.Refresh();
    }

    /// <summary>选中格上的字色：深色主题用白，浅色主题用正文色。</summary>
    private static Brush OnSelectedBrush => ThemeService.IsLight ? ThemeService.Brush("Text") : Brushes.White;

    private static long IdOf(SongEntry song) => long.TryParse(song.SongId, out var id) ? id : 0;

    private static SongRowVm MakeRow(SongEntry e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Requester)) parts.Add("点歌 " + e.Requester);
        if (!string.IsNullOrWhiteSpace(e.Artist)) parts.Add(e.Artist!);
        if (!string.IsNullOrWhiteSpace(e.Label)) parts.Add(e.Label!);
        if (!string.IsNullOrWhiteSpace(e.RepeatText)) parts.Add(e.RepeatText);
        if (e.PlayCount > 0) parts.Add($"已播 {e.PlayCount} 次");

        return new SongRowVm
        {
            Entry = e,
            Cover = CoverService.ToBrush(CoverService.TryCached(IdOf(e))),
            CoverVis = CoverService.TryCached(IdOf(e)) is null ? Visibility.Collapsed : Visibility.Visible,
            CoverFaintVis = CoverService.TryCached(IdOf(e)) is null ? Visibility.Visible : Visibility.Collapsed,
            SubLine = string.Join(" · ", parts),
            TimeBg = e.Enabled ? ChipSelectedBg : ChipNormalBg,
            TimeFg = e.Enabled ? ThemeService.Brush("ChipTextOn") : TextDim
        };
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        Reload();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        Reload();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _selected = DateTime.Today;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        Reload();
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DayVm vm) return;
        _selected = vm.Date;
        if (vm.Date.Month != _month.Month || vm.Date.Year != _month.Year)
            _month = new DateTime(vm.Date.Year, vm.Date.Month, 1);
        Reload();
        // 点日期 → 直接能输入歌名
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void BigAdd_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void AddOnDay_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _debounce.Stop();
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            HideSuggest();
            return;
        }
        _debounce.Start();
    }

    private async System.Threading.Tasks.Task RunSearchAsync()
    {
        var keyword = SearchBox.Text.Trim();
        if (keyword.Length == 0)
        {
            HideSuggest();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var list = await _client.SuggestAsync(keyword, 8, _cts.Token).ConfigureAwait(true);
        if (!string.Equals(keyword, SearchBox.Text.Trim(), StringComparison.Ordinal)) return;

        if (list.Count == 0)
        {
            HideSuggest();
            return;
        }

        var vms = list.Select(s => new SuggestionVm
        {
            Song = s,
            Cover = CoverService.ToBrush(CoverService.TryCached(s.Id)),
            CoverVis = CoverService.TryCached(s.Id) is null ? Visibility.Collapsed : Visibility.Visible,
            CoverFaintVis = CoverService.TryCached(s.Id) is null ? Visibility.Visible : Visibility.Collapsed
        }).ToList();

        SuggestList.ItemsSource = vms;
        SuggestPanel.Visibility = Visibility.Visible;
        _ = FillSuggestCoversAsync(vms, list);
    }

    private async System.Threading.Tasks.Task FillSuggestCoversAsync(List<SuggestionVm> vms, IReadOnlyList<NeteaseSong> songs)
    {
        var target = vms.FirstOrDefault(v => v.CoverVis != Visibility.Visible);
        if (target is null) return;
        var src = await CoverService.GetAsync(target.Song.Id, target.Song.CoverUrl).ConfigureAwait(true);
        if (src is null || SuggestList.ItemsSource is not List<SuggestionVm> current) return;
        foreach (var vm in current.Where(v => v.CoverVis != Visibility.Visible).ToList())
        {
            var one = await CoverService.GetAsync(vm.Song.Id, vm.Song.CoverUrl).ConfigureAwait(true);
            vm.SetCover(one);
        }
        SuggestList.Items.Refresh();
    }

    private void HideSuggest()
    {
        SuggestPanel.Visibility = Visibility.Collapsed;
        SuggestList.ItemsSource = null;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideSuggest();
            SearchBox.Clear();
            return;
        }

        if (e.Key != Key.Enter) return;

        SuggestionVm? pick = null;
        if (SuggestList.Items.Count > 0) pick = SuggestList.Items[0] as SuggestionVm;

        if (pick is not null) OpenEditor(pick.Song.Query, pick.Song.Id.ToString(), pick.Song.Artist);
        else if (!string.IsNullOrWhiteSpace(SearchBox.Text)) OpenEditor(SearchBox.Text.Trim(), null, null);
        e.Handled = true;
    }

    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SuggestionVm pick) return;
        OpenEditor(pick.Song.Query, pick.Song.Id.ToString(), pick.Song.Artist);
    }

    private void OpenEditor(string query, string? songId, string? artist)
    {
        HideSuggest();

        TimeSpan defaultTime;
        var cfg = App.Current.Storage.Config;
        if (!TimeSpan.TryParseExact(cfg.DefaultTime, @"hh\:mm", CultureInfo.InvariantCulture, out defaultTime) &&
            !TimeSpan.TryParse(cfg.DefaultTime, CultureInfo.InvariantCulture, out defaultTime))
            defaultTime = TimeSpan.FromMinutes(7 * 60 + 50);

        var entry = new SongEntry
        {
            Title = query,
            Artist = string.IsNullOrWhiteSpace(artist) ? null : artist,
            SongId = songId,
            Time = _selected.Date + defaultTime,
            Enabled = true,
            Repeat = RepeatModes.None
        };

        var dialog = new SongEditorWindow(entry, true) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        App.Current.Storage.Add(dialog.Result);
        SearchBox.Clear();
        _selected = dialog.Result.Time.Date;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        Reload();
        App.Current.Shell?.RefreshNext();

        ToastWindow.Show("已经排上了", $"{dialog.Result.Time:MM-dd HH:mm} · {dialog.Result.Title}",
            string.IsNullOrWhiteSpace(dialog.Result.Label) ? "到点会自动放" : dialog.Result.Label!, ToastKind.Success);
    }

    private void SongRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if ((sender as FrameworkElement)?.Tag is not SongRowVm row) return;

        var dialog = new SongEditorWindow(row.Entry, false) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        var storage = App.Current.Storage;
        var index = storage.Songs.IndexOf(row.Entry);
        if (index >= 0)
        {
            storage.Songs[index] = dialog.Result;
            storage.SaveSongs();
        }
        _selected = dialog.Result.Time.Date;
        Reload();
        App.Current.Shell?.RefreshNext();
    }

    private void RowPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SongRowVm row) return;
        App.Current.PlayExternal(row.Entry.Title, row.Entry.Label ?? row.Entry.Title);
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SongRowVm row) return;
        var storage = App.Current.Storage;
        storage.Remove(row.Entry);
        App.Current.Scheduler.Forget(row.Entry);
        Reload();
        App.Current.Shell?.RefreshNext();
        ToastWindow.Show("已删除", row.Entry.Title, "这一条不会再到点播放了", ToastKind.Info);
    }
}

public sealed class DayVm
{
    public DateTime Date { get; init; }
    public string DayNumber => Date.Day.ToString(CultureInfo.InvariantCulture);
    public Brush Fg { get; init; } = Brushes.White;
    public Brush Bg { get; init; } = Brushes.Transparent;
    public Brush BorderBrush { get; init; } = Brushes.Transparent;
    public Visibility TodayVis { get; init; } = Visibility.Collapsed;
    public List<ChipVm> Chips { get; init; } = new();
    public string MoreText { get; init; } = "";
}

public sealed class ChipVm
{
    public string Text { get; init; } = "";
    public string Tip { get; init; } = "";
    public Brush Background { get; init; } = Brushes.Transparent;
    public Brush Fg { get; init; } = Brushes.White;
    public ImageBrush? Cover { get; init; }
    public Visibility CoverVis { get; init; } = Visibility.Collapsed;
}

public sealed class SuggestionVm
{
    public NeteaseSong Song { get; init; } = new();
    public string Name => Song.Name;
    public string Subtitle => Song.Subtitle;
    public string DurationText => Song.DurationText;
    public ImageBrush? Cover { get; set; }
    public Visibility CoverVis { get; set; } = Visibility.Collapsed;
    public Visibility CoverFaintVis { get; set; } = Visibility.Visible;

    public void SetCover(System.Windows.Media.ImageSource? src)
    {
        Cover = CoverService.ToBrush(src);
        CoverVis = src is null ? Visibility.Collapsed : Visibility.Visible;
        CoverFaintVis = src is null ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class SongRowVm
{
    public SongEntry Entry { get; init; } = new();
    public ImageBrush? Cover { get; set; }
    public Visibility CoverVis { get; set; } = Visibility.Collapsed;
    public Visibility CoverFaintVis { get; set; } = Visibility.Visible;

    public void SetCover(System.Windows.Media.ImageSource? src)
    {
        Cover = CoverService.ToBrush(src);
        CoverVis = src is null ? Visibility.Collapsed : Visibility.Visible;
        CoverFaintVis = src is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Time => Entry.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string Title => Entry.Title;
    public string SubLine { get; init; } = "";
    public Brush TimeBg { get; init; } = Brushes.Transparent;
    public Brush TimeFg { get; init; } = Brushes.White;
}
