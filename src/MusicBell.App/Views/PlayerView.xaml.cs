using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MusicBell.App.Helpers;
using MusicBell.App.Services;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

namespace MusicBell.App.Views;

public partial class PlayerView : UserControl
{
    private readonly List<TextBlock> _lines = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _dragging;
    private bool _built;
    private PlayerService? _player;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        _refresh.Tick += (_, _) => UpdateStatus();
        ThemeService.Changed += _ => Dispatcher.BeginInvoke(new Action(() => { _built = false; Render(); }));
    }

    private void Attach()
    {
        var app = App.Current;
        if (app is null) return;
        _player = app.BuiltinPlayer;
        _player.Changed += OnChanged;
        _player.Tick += OnTick;
        _player.LyricMoved += OnLyricMoved;
        _player.Volume = app.Storage.Config.PlayerVolume;
        VolumeBar.Value = Math.Clamp(app.Storage.Config.PlayerVolume, 0, 1);
        ModeText.Text = "播放方式：" + PlaybackMethods.Name(app.Storage.Config.PlaybackMethod) + "（设置 → 播放器 里改）";
        _refresh.Start();
        Render();
        UpdateStatus();
    }

    private void Detach()
    {
        _refresh.Stop();
        if (_player is null) return;
        _player.Changed -= OnChanged;
        _player.Tick -= OnTick;
        _player.LyricMoved -= OnLyricMoved;
        _player = null;
    }

    private void OnChanged() => Dispatcher.BeginInvoke(new Action(() => { Render(); UpdateStatus(); }));
    private void OnTick() => Dispatcher.BeginInvoke(new Action(UpdateStatus));
    private void OnLyricMoved() => Dispatcher.BeginInvoke(new Action(ScrollToCurrent));

    public void Reload()
    {
        ModeText.Text = "播放方式：" + PlaybackMethods.Name(App.Current.Storage.Config.PlaybackMethod) + "（设置 → 播放器 里改）";
        Render();
        UpdateStatus();
    }

    private void Render()
    {
        var player = _player;
        if (player is null) return;
        var track = player.Track;
        if (track is null)
        {
            TitleText.Text = "还没有在放歌";
            SubText.Text = "从日历点歌或在下边挑一首，会自动在内置播放器里播";
            CoverBox.Background = ThemeService.Brush("Card");
            CoverGlyph.Visibility = Visibility.Visible;
            PlayGlyph.Text = "\uE768";
            SeekBar.IsEnabled = false;
            BuildLyrics(null);
            return;
        }

        TitleText.Text = track.Title;
        SubText.Text = string.IsNullOrWhiteSpace(track.Artist) ? "内置播放器" : track.Artist;
        CoverBox.Background = CoverService.ToBrush(player.Cover) ?? ThemeService.Brush("Card");
        CoverGlyph.Visibility = player.Cover is null ? Visibility.Visible : Visibility.Collapsed;
        PlayGlyph.Text = player.IsPlaying ? "\uE769" : "\uE768";
        SeekBar.IsEnabled = player.Duration > TimeSpan.Zero;
        if (player.LastError is { Length: > 0 } err)
        {
            ErrorText.Text = err + "（可以在设置里改成「走客户端」）";
            ErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
        BuildLyrics(player.Lyrics);
    }

    private void BuildLyrics(NeteaseStreamClient.LrcDoc? doc)
    {
        LyricPanel.Children.Clear();
        _lines.Clear();
        _built = true;
        if (doc is null || doc.Lines.Count == 0)
        {
            LyricHint.Text = "没有歌词";
            var empty = new TextBlock
            {
                Text = "这首歌还没有歌词",
                FontSize = 13,
                Margin = new Thickness(0, 18, 0, 0),
                Foreground = ThemeService.Brush("TextFaint"),
            };
            LyricPanel.Children.Add(empty);
            return;
        }
        LyricHint.Text = doc.HasTranslation ? "原文 + 翻译" : "逐句滚动";
        foreach (var line in doc.Lines)
        {
            var tb = new TextBlock
            {
                Text = line.Text,
                FontSize = 14,
                LineHeight = 24,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 5),
                Foreground = ThemeService.Brush("TextFaint"),
            };
            LyricPanel.Children.Add(tb);
            _lines.Add(tb);
        }
        ScrollToCurrent();
    }

    private void ScrollToCurrent()
    {
        var player = _player;
        if (player is null) return;
        var idx = player.LyricIndex;
        for (var i = 0; i < _lines.Count; i++)
        {
            var active = i == idx;
            var near = idx >= 0 && Math.Abs(i - idx) == 1;
            _lines[i].Foreground = active
                ? ThemeService.Brush("Accent")
                : near ? ThemeService.Brush("TextDim") : ThemeService.Brush("TextFaint");
            _lines[i].FontSize = active ? 16 : 14;
            _lines[i].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        if (idx < 0 || idx >= _lines.Count) return;
        var target = _lines[idx];
        try
        {
            var y = target.TransformToAncestor(LyricPanel).Transform(new Point(0, 0)).Y;
            var view = LyricScroll.ViewportHeight;
            LyricScroll.ScrollToVerticalOffset(Math.Max(0, y - view / 2 + 20));
        }
        catch { }
    }

    private void UpdateStatus()
    {
        var player = _player;
        if (player is null) return;
        var pos = player.Position;
        var dur = player.Duration;
        PosText.Text = Clock(pos);
        DurText.Text = Clock(dur);
        if (!_dragging && dur > TimeSpan.Zero)
        {
            SeekBar.Value = Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1);
        }
        PlayGlyph.Text = player.IsPlaying ? "\uE769" : "\uE768";
    }

    private static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || _player.Track is null) return;
        _player.TogglePause();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _player?.Stop();

    private void Restart_Click(object sender, RoutedEventArgs e) => _player?.Seek(TimeSpan.Zero);

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player is null || !IsLoaded) return;
        _player.Volume = e.NewValue;
        var cfg = App.Current?.Storage.Config;
        if (cfg is null) return;
        cfg.PlayerVolume = Math.Round(e.NewValue, 2);
    }

    private void SeekBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _dragging = true;
    private void SeekBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _dragging = false;
        _player?.SeekRatio(SeekBar.Value);
    }
}
