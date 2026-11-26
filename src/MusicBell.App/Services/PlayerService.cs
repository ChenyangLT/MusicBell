using System;
using System.Windows.Media;
using MusicBell.App.Helpers;
using System.Windows.Threading;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

namespace MusicBell.App.Services;

/// <summary>内置播放器：网易云直链 + 歌词滚动。全部 API 只能在 UI 线程调用。</summary>
public sealed class PlayerService
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(240) };
    private long _token;

    public PlayerService()
    {
        _player.MediaOpened += (_, _) =>
        {
            Duration = _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;
            IsPlaying = true;
            _player.Volume = Math.Clamp(Volume, 0, 1);
            Changed?.Invoke();
        };
        _player.MediaEnded += (_, _) =>
        {
            IsPlaying = false;
            Changed?.Invoke();
        };
        _player.MediaFailed += (_, e) =>
        {
            IsPlaying = false;
            LastError = "播放失败：" + (e.ErrorException?.Message ?? "音频解码不了");
            AppLog.Warn("内置播放器 " + LastError);
            Changed?.Invoke();
        };
        _timer.Tick += (_, _) =>
        {
            if (Track is null) return;
            UpdateLyricIndex();
            Tick?.Invoke();
        };
        _timer.Start();
    }

    public PlayerTrack? Track { get; private set; }
    public ImageSource? Cover { get; private set; }
    public NeteaseStreamClient.LrcDoc? Lyrics { get; private set; }
    public int LyricIndex { get; private set; } = -1;
    public bool IsPlaying { get; private set; }
    public string? LastError { get; private set; }
    public TimeSpan Duration { get; private set; }
    public TimeSpan Position => _player.Position;
    public bool HasTrack => Track is not null;

    private double _volume = 0.8;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _player.Volume = _volume;
        }
    }

    /// <summary>停下来并清空当前曲目（上课前关音乐用）。</summary>
    public void Stop()
    {
        _player.Stop();
        _player.Close();
        IsPlaying = false;
        Duration = TimeSpan.Zero;
        Changed?.Invoke();
    }

    /// <summary>暂停 / 继续。</summary>
    public void TogglePause()
    {
        if (Track is null) return;
        if (IsPlaying)
        {
            _player.Pause();
            IsPlaying = false;
        }
        else
        {
            _player.Play();
            IsPlaying = true;
        }
        Changed?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (Track is null) return;
        try { _player.Position = position; } catch { }
        UpdateLyricIndex();
        Tick?.Invoke();
    }

    public void SeekRatio(double ratio)
    {
        if (Duration <= TimeSpan.Zero) return;
        Seek(TimeSpan.FromSeconds(Math.Clamp(ratio, 0, 1) * Duration.TotalSeconds));
    }

    /// <summary>换一首歌开始放；返回是否成功拿到直链。</summary>
    public async System.Threading.Tasks.Task<bool> PlayAsync(PlayerTrack track, string? cookie = null, Action<string>? log = null)
    {
        var token = ++_token;
        LastError = null;
        Track = track;
        Cover = null;
        Lyrics = null;
        LyricIndex = -1;
        Duration = TimeSpan.Zero;
        IsPlaying = false;
        Changed?.Invoke();
        log?.Invoke($"内置播放器：" + track.Title);

        var info = await NeteaseStreamClient.ResolveStreamAsync(track.SongId, cookie).ConfigureAwait(true);
        if (token != _token) return false;
        if (!info.Ok || string.IsNullOrWhiteSpace(info.Url))
        {
            LastError = info.Message;
            AppLog.Warn($"内置播放器拿不到直链：{track.Title}（{info.Message}）");
            Changed?.Invoke();
            return false;
        }

        try
        {
            _player.Open(new Uri(info.Url!));
            _player.Volume = _volume;
            _player.Play();
            IsPlaying = true;
        }
        catch (Exception ex)
        {
            LastError = "打开音频失败：" + ex.Message;
            AppLog.Warn(LastError!);
            Changed?.Invoke();
            return false;
        }

        log?.Invoke($"内置播放器：{info.Message}，共 {(info.Size > 0 ? info.Size / 1024 / 1024 : 0)} MB");
        Changed?.Invoke();

        _ = LoadExtrasAsync(track, token);
        return true;
    }

    private async System.Threading.Tasks.Task LoadExtrasAsync(PlayerTrack track, long token)
    {
        try
        {
            var cover = CoverService.TryCached(track.SongId);
            if (cover is null)
            {
                cover = !string.IsNullOrWhiteSpace(track.CoverUrl)
                    ? await CoverService.GetAsync(track.SongId, track.CoverUrl, default).ConfigureAwait(true)
                    : await CoverService.EnsureAsync(track.SongId).ConfigureAwait(true);
            }
            if (token != _token) return;
            if (cover is not null)
            {
                Cover = cover;
                Changed?.Invoke();
            }
        }
        catch { }

        try
        {
            var lrc = await NeteaseStreamClient.GetLyricAsync(track.SongId).ConfigureAwait(true);
            if (token != _token) return;
            Lyrics = lrc;
            UpdateLyricIndex();
            Changed?.Invoke();
        }
        catch { }
    }

    private void UpdateLyricIndex()
    {
        if (Lyrics is null || Lyrics.Lines.Count == 0)
        {
            LyricIndex = -1;
            return;
        }
        var idx = Lyrics.IndexAt(_player.Position);
        if (idx != LyricIndex)
        {
            LyricIndex = idx;
            LyricMoved?.Invoke();
        }
    }

    public event Action? Changed;
    public event Action? Tick;
    public event Action? LyricMoved;
}
