using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>排歌时间冲突检测：同一个日期、同一个时刻（可带容差）是否已经有别的排歌。</summary>
public static class SongConflicts
{
    /// <summary>找出会在 at 这一刻与它撞车的排歌（已停用的不算）。</summary>
    public static List<SongEntry> Find(IEnumerable<SongEntry> songs, DateTime at, string? ignoreId, int windowMinutes = 1)
    {
        var hit = new List<SongEntry>();
        foreach (var s in songs)
        {
            if (!s.Enabled) continue;
            if (!string.IsNullOrEmpty(ignoreId) && s.Id == ignoreId) continue;
            if (!s.OccursOn(at.Date)) continue;
            if (Math.Abs((s.Time.TimeOfDay - at.TimeOfDay).TotalMinutes) <= windowMinutes) hit.Add(s);
        }
        return hit.OrderBy(s => s.Time).ToList();
    }

    /// <summary>往后找一个没被占用的时刻（5 分钟一档，最多找 24 小时）。</summary>
    public static DateTime? NextFreeSlot(IEnumerable<SongEntry> songs, DateTime at, string? ignoreId, int stepMinutes = 5, int maxMinutes = 24 * 60)
    {
        var list = songs.ToList();
        for (var m = stepMinutes; m <= maxMinutes; m += stepMinutes)
        {
            var candidate = at.AddMinutes(m);
            if (Find(list, candidate, ignoreId).Count == 0) return candidate;
        }
        return null;
    }
}
