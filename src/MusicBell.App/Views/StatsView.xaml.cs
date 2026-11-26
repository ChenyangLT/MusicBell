using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

namespace MusicBell.App.Views;

public partial class StatsView : UserControl
{
    public StatsView()
    {
        InitializeComponent();
        ThemeService.Changed += _ => Dispatcher.BeginInvoke(new Action(Reload));
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        try
        {
            var storage = App.Current.Storage;
            var songs = storage.Songs;
            var records = storage.History.Records;

            StatSongs.Text = songs.Count.ToString();
            StatSongsSub.Text = $"其中启用 {songs.Count(s => s.Enabled)} 首";
            StatPlays.Text = songs.Sum(s => s.PlayCount).ToString();
            var most = songs.OrderByDescending(s => s.PlayCount).FirstOrDefault();
            StatPlaysSub.Text = most is { PlayCount: > 0 } ? "播放最多：" + Shorten(most.Display, 12) : "还没有放过歌";

            var byPeople = songs
                .Where(s => !string.IsNullOrWhiteSpace(s.Requester))
                .GroupBy(s => s.Requester!.Trim())
                .OrderByDescending(g => g.Count())
                .ToList();
            StatPeople.Text = byPeople.Count.ToString();
            StatPeopleSub.Text = $"名单里 {storage.People.People.Count} 人";

            StatRecords.Text = records.Count.ToString();
            var last = records.OrderByDescending(r => r.At).FirstOrDefault();
            StatRecordsSub.Text = last is null ? "最近一次 —" : "最近 " + last.At.ToString("MM-dd HH:mm");

            BuildRank(byPeople);
            BuildHours(songs);
            BuildRepeat(songs);
            BuildRecent(records);
        }
        catch (Exception ex)
        {
            HeadText.Text = "统计数据读不出来：" + ex.Message;
        }
    }

    private void BuildRank(List<IGrouping<string, SongEntry>> groups)
    {
        RankPanel.Children.Clear();
        if (groups.Count == 0)
        {
            RankPanel.Children.Add(Hint("还没有署名的点歌人。点歌时在「点歌人」里写个名字就会出现。"));
            return;
        }
        var max = groups.Max(g => g.Count());
        var rank = 0;
        foreach (var g in groups.Take(8))
        {
            rank++;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var no = new TextBlock
            {
                Text = rank.ToString(),
                FontSize = 11.5,
                Foreground = ThemeService.Brush(rank <= 3 ? "Accent" : "TextFaint"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(no, 0);
            row.Children.Add(no);

            var name = new TextBlock
            {
                Text = Shorten(g.Key, 6),
                FontSize = 12.5,
                Foreground = ThemeService.Brush("Text"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = g.Key
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            var barHost = new Grid { Height = 16, Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, g.Count()), GridUnitType.Star) });
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, max - g.Count() + 0.001), GridUnitType.Star) });
            var bar = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = ThemeService.Brush("AccentGrad"),
                MinWidth = 6
            };
            Grid.SetColumn(bar, 0);
            barHost.Children.Add(bar);
            Grid.SetColumn(barHost, 2);
            row.Children.Add(barHost);

            var count = new TextBlock
            {
                Text = g.Count() + " 首",
                FontSize = 11.5,
                Foreground = ThemeService.Brush("TextDim"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(count, 3);
            row.Children.Add(count);

            RankPanel.Children.Add(row);
        }
    }

    private void BuildHours(List<SongEntry> songs)
    {
        HourPanel.Children.Clear();
        HourPanel.ColumnDefinitions.Clear();
        var counts = new int[24];
        foreach (var s in songs) counts[s.Time.Hour]++;
        var max = Math.Max(1, counts.Max());
        for (var h = 0; h < 24; h++)
        {
            HourPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new Grid { Margin = new Thickness(1, 0, 1, 0) };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });

            var bar = new Border
            {
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Background = counts[h] == 0
                    ? ThemeService.Brush("TrackBg")
                    : ThemeService.Brush("AccentGrad"),
                Height = counts[h] == 0 ? 3 : Math.Max(6, 132.0 * counts[h] / max),
                VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = $"{h}:00 有 {counts[h]} 首"
            };
            Grid.SetRow(bar, 0);
            cell.Children.Add(bar);

            if (h % 3 == 0)
            {
                var label = new TextBlock
                {
                    Text = h.ToString("00"),
                    FontSize = 9.5,
                    Foreground = ThemeService.Brush("TextFaint"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                Grid.SetRow(label, 1);
                cell.Children.Add(label);
            }
            Grid.SetColumn(cell, h);
            HourPanel.Children.Add(cell);
        }
    }

    private void BuildRepeat(List<SongEntry> songs)
    {
        RepeatPanel.Children.Clear();
        var none = songs.Count(s => s.Repeat == RepeatModes.None);
        var daily = songs.Count(s => s.Repeat == RepeatModes.Daily);
        var weekly = songs.Count(s => s.Repeat == RepeatModes.Weekly);
        var total = Math.Max(1, songs.Count);
        AddRepeatRow("只放一次", none, total);
        AddRepeatRow("每天", daily, total);
        AddRepeatRow("每周", weekly, total);
        if (songs.Count == 0) RepeatPanel.Children.Add(Hint("还没有排歌。"));
    }

    private void AddRepeatRow(string label, int count, int total)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

        var name = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = ThemeService.Brush("TextDim"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        var host = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.4, count), GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.4, total - count), GridUnitType.Star) });
        var bar = new Border { CornerRadius = new CornerRadius(6), Background = ThemeService.Brush("AccentGrad"), MinWidth = count > 0 ? 8 : 0 };
        Grid.SetColumn(bar, 0);
        host.Children.Add(bar);
        Grid.SetColumn(host, 1);
        row.Children.Add(host);

        var pct = new TextBlock
        {
            Text = $"{count} 首",
            FontSize = 11,
            Foreground = ThemeService.Brush("TextFaint"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(pct, 2);
        row.Children.Add(pct);

        RepeatPanel.Children.Add(row);
    }

    private void BuildRecent(List<PlayRecord> records)
    {
        RecentPanel.Children.Clear();
        if (records.Count == 0)
        {
            RecentPanel.Children.Add(Hint("还没有播放记录。到点放歌、手动播放都会记在这里。"));
            return;
        }
        foreach (var r in records.OrderByDescending(r => r.At).Take(10))
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var time = new TextBlock
            {
                Text = r.At.ToString("MM-dd HH:mm"),
                FontSize = 11,
                Foreground = ThemeService.Brush("TextFaint"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(time, 0);
            row.Children.Add(time);

            var title = new TextBlock
            {
                Text = Shorten(r.Title, 26),
                FontSize = 12,
                Foreground = ThemeService.Brush("Text"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = r.Title
            };
            Grid.SetColumn(title, 1);
            row.Children.Add(title);

            var tag = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Background = ThemeService.Brush(r.Success ? "AccentSoft" : "ChipBg"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = r.Success ? SongSources.Name(r.Source) : "失败",
                    FontSize = 10.5,
                    Foreground = ThemeService.Brush(r.Success ? "ChipTextOn" : "TextFaint")
                }
            };
            Grid.SetColumn(tag, 2);
            row.Children.Add(tag);

            RecentPanel.Children.Add(row);
        }
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 6),
        Foreground = ThemeService.Brush("TextFaint")
    };

    private static string Shorten(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..Math.Max(1, max - 1)] + "…";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
}
