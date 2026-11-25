using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>到点检查器：每 10 秒扫一次排歌。</summary>
public sealed class SongScheduler : IDisposable
{
    private readonly StorageService _storage;
    private readonly System.Threading.Timer _timer;
    private readonly HashSet<string> _fired = new();
    private readonly object _gate = new();
    private bool _busy;

    /// <summary>补播窗口：错过这么久内仍然补播。</summary>
    public TimeSpan GraceWindow { get; set; } = TimeSpan.FromMinutes(10);

    public event Action<SongEntry, DateTime>? Due;

    public SongScheduler(StorageService storage)
    {
        _storage = storage;
        _timer = new System.Threading.Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(1000, 10000);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void Forget(SongEntry entry)
    {
        lock (_gate) _fired.RemoveWhere(k => k.StartsWith(entry.Id + "@", StringComparison.Ordinal));
    }

    private void Check()
    {
        if (_busy) return;
        try
        {
            var now = DateTime.Now;
            foreach (var entry in _storage.Songs.Where(e => e.Enabled).ToArray())
            {
                var occ = entry.OccurrenceOn(now.Date);
                if (occ is null) continue;
                var at = occ.Value.AddSeconds(-Math.Max(0, _storage.Config.AdvanceSeconds));
                if (now < at || now - at > GraceWindow) continue;

                var key = entry.Id + "@" + occ.Value.ToString("yyyyMMddHHmm");
                lock (_gate)
                {
                    if (_fired.Contains(key)) continue;
                    _fired.Add(key);
                }

                entry.LastPlayed = now;
                entry.PlayCount++;
                _busy = true;
                Due?.Invoke(entry, occ.Value);
                _busy = false;
            }
        }
        catch (Exception ex)
        {
            _busy = false;
            AppLog.Error("排歌检查出错", ex);
        }
    }

    public void Dispose() => _timer.Dispose();
}
