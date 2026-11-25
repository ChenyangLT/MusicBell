using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MusicBell.App.Helpers;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

namespace MusicBell.App.Views;

public partial class PeopleView : UserControl
{
    private readonly ObservableCollection<PersonVm> _people = new();
    private string? _selected;

    public PeopleView()
    {
        InitializeComponent();
        PeopleList.ItemsSource = _people;
        Loaded += (_, _) => Reload();
        ThemeService.Changed += _ => Dispatcher.BeginInvoke(new Action(Reload));
    }

    public void Reload()
    {
        var storage = App.Current.Storage;
        var songs = storage.Songs;
        _selected ??= _people.Count > 0 ? _people[0].Name : null;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var last = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var song in songs)
        {
            var name = (song.Requester ?? "").Trim();
            if (name.Length == 0) continue;
            counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
            if (!last.TryGetValue(name, out var t) || song.Time > t) last[name] = song.Time;
        }

        var names = new List<(string Name, string Group)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in storage.People.People)
        {
            var name = (person.Name ?? "").Trim();
            if (name.Length == 0 || !seen.Add(name)) continue;
            names.Add((name, person.Group ?? ""));
        }
        foreach (var name in counts.Keys)
        {
            if (!seen.Add(name)) continue;
            names.Add((name, ""));
        }

        names.Sort((a, b) =>
        {
            counts.TryGetValue(a.Name, out var ca);
            counts.TryGetValue(b.Name, out var cb);
            if (ca != cb) return cb.CompareTo(ca);
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
        });

        _people.Clear();
        foreach (var (name, group) in names)
        {
            counts.TryGetValue(name, out var count);
            last.TryGetValue(name, out var at);
            _people.Add(new PersonVm
            {
                Name = name,
                Group = group,
                Sub = string.IsNullOrWhiteSpace(group)
                    ? (at == default ? "还没点过歌" : "最近 " + at.ToString("MM-dd HH:mm"))
                    : group + " · " + (at == default ? "还没点过歌" : at.ToString("MM-dd HH:mm")),
                Initial = name.Length > 0 ? name.Substring(0, 1) : "?",
                CountText = count + " 首",
                AvatarBg = AvatarBg(name),
                AvatarFg = ThemeService.Brush("Accent"),
                BadgeBg = count > 0 ? ThemeService.Brush("AccentSoft") : ThemeService.Brush("ChipBg"),
                BadgeFg = count > 0 ? ThemeService.Brush("Accent") : ThemeService.Brush("TextFaint")
            });
        }

        CountText.Text = _people.Count + " 位";
        EmptyHint.Visibility = _people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_selected is null || _people.All(p => !string.Equals(p.Name, _selected, StringComparison.OrdinalIgnoreCase)))
            _selected = _people.FirstOrDefault()?.Name;

        LoadDetail(_selected);
    }

    private static Brush AvatarBg(string name)
    {
        var palette = new[] { "AccentSoft", "AccentSoft2", "ChipBg" };
        var index = Math.Abs(name.GetHashCode()) % palette.Length;
        return ThemeService.Brush(palette[index]);
    }

    private void LoadDetail(string? name)
    {
        var storage = App.Current.Storage;
        if (string.IsNullOrWhiteSpace(name))
        {
            DetailName.Text = "选一位点歌人";
            DetailStats.Text = "用左边名单挑个人，就能看到他/她点过的歌。";
            DetailGroupChip.Visibility = Visibility.Collapsed;
            RemoveBtn.Visibility = Visibility.Collapsed;
            SongList.ItemsSource = null;
            DetailEmpty.Visibility = Visibility.Collapsed;
            return;
        }

        _selected = name;
        var songs = storage.Songs
            .Where(s => string.Equals((s.Requester ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Time)
            .ToList();

        var person = storage.People.Find(name!);
        DetailName.Text = name;
        if (person is not null && !string.IsNullOrWhiteSpace(person.Group))
        {
            DetailGroup.Text = person.Group;
            DetailGroupChip.Visibility = Visibility.Visible;
        }
        else
        {
            DetailGroupChip.Visibility = Visibility.Collapsed;
        }
        RemoveBtn.Visibility = Visibility.Visible;

        DetailStats.Text = songs.Count == 0
            ? "还没点过歌"
            : "共 " + songs.Count + " 首 · 最近一次 " + songs[0].Time.ToString("yyyy-MM-dd HH:mm");

        var rows = songs.Select(s => new SongVm
        {
            Title = string.IsNullOrWhiteSpace(s.Artist) ? s.Title : s.Artist + " · " + s.Title,
            Sub = string.IsNullOrWhiteSpace(s.Label) ? s.RepeatText : s.Label + " · " + s.RepeatText,
            TimeText = s.Time.ToString("MM-dd HH:mm"),
            LabelText = s.RepeatText,
            Cover = CoverService.ToBrush(CoverService.TryCached(IdOf(s))),
            CoverVis = CoverService.TryCached(IdOf(s)) is null ? Visibility.Collapsed : Visibility.Visible,
            PlaceholderVis = CoverService.TryCached(IdOf(s)) is null ? Visibility.Visible : Visibility.Collapsed
        }).ToList();

        SongList.ItemsSource = rows;
        DetailEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var missing = songs.Where(s => IdOf(s) > 0 && CoverService.TryCached(IdOf(s)) is null)
                           .Select(IdOf).Distinct().Take(8).ToList();
        if (missing.Count > 0) _ = FillCoversAsync(name!, missing);
    }

    private static long IdOf(SongEntry song) => long.TryParse(song.SongId, out var id) ? id : 0;

    private async Task FillCoversAsync(string name, List<long> ids)
    {
        var got = false;
        foreach (var id in ids)
        {
            try
            {
                var url = await NeteaseSearchClient.GetCoverUrlAsync(id).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (await CoverService.GetAsync(id, url).ConfigureAwait(false) is not null) got = true;
            }
            catch
            {
                // 单个失败没关系
            }
        }
        if (!got) return;
        await Dispatcher.BeginInvoke(new Action(() =>
        {
            if (string.Equals(_selected, name, StringComparison.OrdinalIgnoreCase)) LoadDetail(name);
        }));
    }

    private void Person_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PersonVm vm) LoadDetail(vm.Name);
    }

    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Add_Click(sender, e);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = (AddBox.Text ?? "").Trim();
        if (name.Length == 0) return;
        var storage = App.Current.Storage;
        if (storage.People.Find(name) is null) storage.People.Ensure(name);
        storage.People.Save();
        AddBox.Text = "";
        _selected = name;
        Reload();
        ToastWindow.Show("已加入名单", name, "点歌时可以直接从名单里选", ToastKind.Success);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入点歌人名单",
            Filter = "名单文件 (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var rows = ExcelTable.Read(dialog.FileName);
            if (rows.Count == 0)
            {
                ToastWindow.Show("这份表是空的", dialog.SafeFileName, "换一份再试试", ToastKind.Error);
                return;
            }
            var (people, column) = PeopleStore.FromTable(rows);
            if (people.Count == 0)
            {
                ToastWindow.Show("没认出人名", dialog.SafeFileName, "第一列放姓名就行", ToastKind.Error);
                return;
            }
            var added = App.Current.Storage.People.MergeFrom(people);
            App.Current.Storage.People.Save();
            Reload();
            ToastWindow.Show("名单导入完成", "新增 " + added + " 位 · 一共 " + people.Count + " 位", "识别列：" + column, ToastKind.Success);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("导入失败", ex.Message, "支持 .xlsx / .xls / .csv", ToastKind.Error);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var storage = App.Current.Storage;
        if (storage.People.People.Count == 0)
        {
            ToastWindow.Show("名单本来就是空的", "", "", ToastKind.Info);
            return;
        }
        if (MessageBox.Show("清空点歌人名单？排歌记录不会删。", "MusicBell", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        storage.People.ReplaceAll(new List<Person>());
        storage.People.Save();
        _selected = null;
        Reload();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selected)) return;
        var storage = App.Current.Storage;
        if (MessageBox.Show("把「" + _selected + "」从名单里移除？（已排的歌保留）", "MusicBell", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        storage.People.Remove(_selected!);
        storage.People.Save();
        _selected = null;
        Reload();
    }
}

public sealed class PersonVm
{
    public string Name { get; init; } = "";
    public string Group { get; init; } = "";
    public string Sub { get; init; } = "";
    public string Initial { get; init; } = "";
    public string CountText { get; init; } = "";
    public Brush AvatarBg { get; init; } = Brushes.Transparent;
    public Brush AvatarFg { get; init; } = Brushes.White;
    public Brush BadgeBg { get; init; } = Brushes.Transparent;
    public Brush BadgeFg { get; init; } = Brushes.White;
}

public sealed class SongVm
{
    public string Title { get; init; } = "";
    public string Sub { get; init; } = "";
    public string TimeText { get; init; } = "";
    public string LabelText { get; init; } = "";
    public Brush? Cover { get; init; }
    public Visibility CoverVis { get; init; } = Visibility.Collapsed;
    public Visibility PlaceholderVis { get; init; } = Visibility.Visible;
}
