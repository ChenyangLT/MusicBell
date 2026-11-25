using System.Globalization;
using System.Text.Json;

namespace MusicBell.Core.Services;

/// <summary>课表里的一段时间（上课 / 课间 / 其他）。</summary>
public sealed class LessonSegment
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public int TimeType { get; init; }
    public string BreakName { get; init; } = "";
    public string? Subject { get; init; }
    public string? Teacher { get; init; }
    public int LessonIndex { get; init; }

    public bool IsClass => TimeType == 0;

    public string Name => IsClass
        ? (string.IsNullOrWhiteSpace(Subject) ? "上课" : Subject!)
        : (string.IsNullOrWhiteSpace(BreakName) ? "课间" : BreakName);

    public TimeSpan Duration => End - Start;

    /// <summary>0..1 进度。</summary>
    public double ProgressAt(DateTime now)
    {
        var total = (End - Start).TotalSeconds;
        if (total <= 0) return 0;
        return Math.Clamp((now - Start).TotalSeconds / total, 0, 1);
    }
}

/// <summary>某一天的完整课表。</summary>
public sealed class ScheduleSnapshot
{
    public string ProfileName { get; init; } = "";
    public string PlanName { get; init; } = "";
    public DateTime Day { get; init; }
    public int WeekDiv { get; init; }
    public int WeekTotal { get; init; }
    public IReadOnlyList<LessonSegment> Segments { get; init; } = Array.Empty<LessonSegment>();

    public LessonSegment? Current(DateTime now)
        => Segments.FirstOrDefault(s => now >= s.Start && now < s.End);

    public LessonSegment? CurrentClass(DateTime now)
        => Segments.FirstOrDefault(s => s.IsClass && now >= s.Start && now < s.End);

    public bool IsInClass(DateTime now) => CurrentClass(now) is not null;

    public LessonSegment? NextClass(DateTime now)
        => Segments.FirstOrDefault(s => s.IsClass && s.Start > now);

    public string WeekText => WeekTotal > 1
        ? (WeekDiv == 1 ? "单周" : WeekDiv == 2 ? "双周" : "第" + WeekDiv + "周")
        : "";
}

/// <summary>直接读 ClassIsland 的课表文件算「现在上什么课 / 下节什么课」。ClassIsland 不用开着。</summary>
public static class ClassIslandSchedule
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (long Stamp, DateTime Day, ScheduleSnapshot? Snap)> Cache = new();

    /// <summary>找一个 ClassIsland 数据目录（含 Settings.json 与 Profiles）。</summary>
    public static string? LocateDataDir(string? configured)
    {
        foreach (var cand in Candidates(configured))
        {
            try
            {
                if (!Directory.Exists(cand)) continue;
                var settings = Path.Combine(cand, "Settings.json");
                var profiles = Path.Combine(cand, "Profiles");
                if (File.Exists(settings) && Directory.Exists(profiles)) return Path.GetFullPath(cand);
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var dir = configured!.Trim().Trim('"');
            yield return dir;
            yield return Path.Combine(dir, "data");
        }

        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        foreach (var root in roots.Distinct())
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            string[] subs;
            try { subs = Directory.GetDirectories(root); } catch { continue; }
            foreach (var d in subs)
            {
                var name = Path.GetFileName(d);
                if (name.Contains("classisland", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("ClassIsland", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(d, "data");
                    yield return d;
                }
            }
        }
    }

    /// <summary>取指定日期的课表（带缓存，课表文件变了会自动重读）。</summary>
    public static ScheduleSnapshot? Get(string dataDir, DateTime day)
    {
        var key = dataDir.ToLowerInvariant();
        var stamp = Fingerprint(dataDir);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var hit) && hit.Stamp == stamp && hit.Day == day.Date)
                return hit.Snap;
        }

        ScheduleSnapshot? snap = null;
        try { snap = Build(dataDir, day); }
        catch (Exception ex) { AppLog.Warn("解析课表失败：" + ex.Message); }

        lock (Gate) Cache[key] = (stamp, day.Date, snap);
        return snap;
    }

    public static void Invalidate()
    {
        lock (Gate) Cache.Clear();
    }

    private static long Fingerprint(string dataDir)
    {
        long sum = 0;
        try
        {
            var settings = Path.Combine(dataDir, "Settings.json");
            if (File.Exists(settings)) sum += File.GetLastWriteTimeUtc(settings).Ticks;
            var profile = ProfilePath(dataDir);
            if (profile is not null && File.Exists(profile)) sum += File.GetLastWriteTimeUtc(profile).Ticks;
        }
        catch { }
        return sum;
    }

    private static string? ProfilePath(string dataDir)
    {
        try
        {
            var settingsPath = Path.Combine(dataDir, "Settings.json");
            if (!File.Exists(settingsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var name = Str(doc.RootElement, "SelectedProfile");
            if (string.IsNullOrWhiteSpace(name)) return null;
            var path = Path.Combine(dataDir, "Profiles", name!);
            return File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    private static ScheduleSnapshot? Build(string dataDir, DateTime day)
    {
        var settingsPath = Path.Combine(dataDir, "Settings.json");
        var profilePath = ProfilePath(dataDir);
        if (!File.Exists(settingsPath) || profilePath is null) return null;

        using var settingsDoc = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var settings = settingsDoc.RootElement;
        var singleWeekStart = Date(settings, "SingleWeekStartTime") ?? day.Date;
        var maxCycle = (int)(Num(settings, "MultiWeekRotationMaxCycle") ?? 4);
        if (maxCycle < 2) maxCycle = 2;
        var offsets = IntArray(settings, "MultiWeekRotationOffset");

        using var profileDoc = JsonDocument.Parse(File.ReadAllText(profilePath));
        var profile = profileDoc.RootElement;

        var profileName = Str(profile, "Name") ?? Path.GetFileNameWithoutExtension(profilePath);
        var selectedGroup = Str(profile, "SelectedClassPlanGroupId") ?? "";
        var subjects = Obj(profile, "Subjects");
        var timeLayouts = Obj(profile, "TimeLayouts");
        var classPlans = Obj(profile, "ClassPlans");
        if (classPlans is null || timeLayouts is null) return null;

        var elapsedWeeks = (int)Math.Floor((day.Date - singleWeekStart.Date).TotalDays / 7.0);
        var weekday = (int)day.DayOfWeek;

        JsonElement? chosen = null;
        var chosenName = "";
        var chosenDiv = 0;
        var chosenTotal = 1;
        string? fallbackName = null;

        foreach (var plan in classPlans.Value.EnumerateObject())
        {
            var p = plan.Value;
            if (Bool(p, "IsOverlay") == true) continue;
            if (Bool(p, "IsEnabled") == false) continue;
            var rule = Obj(p, "TimeRule");
            if (rule is null) continue;
            if ((int)(Num(rule.Value, "WeekDay") ?? -1) != weekday) continue;

            var total = (int)(Num(rule.Value, "WeekCountDivTotal") ?? 1);
            if (total < 1) total = 1;
            if (total > maxCycle) continue;

            var div = (int)(Num(rule.Value, "WeekCountDiv") ?? 0);
            if (div == 0) { /* 每周都上 */ }
            else
            {
                var offset = offsets.Count > total ? offsets[total] : 0;
                var pos = (elapsedWeeks + offset) % total;
                if (pos < 0) pos += total;
                if (div != pos + 1) continue;
            }

            var group = Str(p, "AssociatedGroup") ?? "";
            var groupOk = string.IsNullOrWhiteSpace(selectedGroup) || group == selectedGroup;
            if (chosen is null && groupOk)
            {
                chosen = p;
                chosenName = Str(p, "Name") ?? "";
                chosenDiv = div == 0 ? 1 : div;
                chosenTotal = total;
            }
            else if (!groupOk && fallbackName is null)
            {
                fallbackName = Str(p, "Name");
                if (chosen is null)
                {
                    chosen = p;
                    chosenName = fallbackName!;
                    chosenDiv = div == 0 ? 1 : div;
                    chosenTotal = total;
                }
            }
        }

        if (chosen is null) return null;

        var layoutId = Str(chosen.Value, "TimeLayoutId");
        if (string.IsNullOrWhiteSpace(layoutId)) return null;
        if (!Guid.TryParse(layoutId, out var layoutGuid)) return null;

        JsonElement? layout = null;
        foreach (var tl in timeLayouts.Value.EnumerateObject())
        {
            if (!Guid.TryParse(tl.Name, out var g) || g != layoutGuid) continue;
            layout = tl.Value;
            break;
        }
        if (layout is null) return null;

        var layouts = Arr(layout.Value, "Layouts");
        if (layouts is null) return null;

        var segments = new List<LessonSegment>();
        var lessonIndex = 0;
        var classes = Arr(chosen.Value, "Classes");

        foreach (var item in layouts.Value.EnumerateArray())
        {
            var type = (int)(Num(item, "TimeType") ?? 1);
            var startSpan = Time(item, "StartTime");
            var endSpan = Time(item, "EndTime");
            if (startSpan is null || endSpan is null) continue;
            if (endSpan <= startSpan) continue;

            string? subject = null;
            string? teacher = null;
            var idx = lessonIndex;
            if (type == 0)
            {
                lessonIndex++;
                if (classes is not null && idx < classes.Value.GetArrayLength())
                {
                    var cls = classes.Value[idx];
                    if (Bool(cls, "IsEnabled") == false) { subject = null; }
                    else
                    {
                        var sid = Str(cls, "SubjectId");
                        if (Guid.TryParse(sid, out var sg) && subjects is not null)
                        {
                            foreach (var sub in subjects.Value.EnumerateObject())
                            {
                                if (!Guid.TryParse(sub.Name, out var g2) || g2 != sg) continue;
                                subject = Str(sub.Value, "Name");
                                teacher = Str(sub.Value, "TeacherName");
                                break;
                            }
                        }
                    }
                }
            }

            segments.Add(new LessonSegment
            {
                Start = day.Date + startSpan.Value,
                End = day.Date + endSpan.Value,
                TimeType = type,
                BreakName = Str(item, "BreakName") ?? "",
                Subject = subject,
                Teacher = teacher,
                LessonIndex = type == 0 ? idx + 1 : 0
            });
        }

        segments.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new ScheduleSnapshot
        {
            ProfileName = profileName ?? "",
            PlanName = chosenName,
            Day = day.Date,
            WeekDiv = chosenDiv,
            WeekTotal = chosenTotal,
            Segments = segments
        };
    }

    // ---------- JSON 小工具（不区分大小写，兼容 camelCase / PascalCase） ----------
    private static bool NameIs(JsonProperty p, string name)
        => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase);

    private static JsonElement? Find(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject())
            if (NameIs(p, name)) return p.Value;
        return null;
    }

    private static string? Str(JsonElement e, string name)
    {
        var v = Find(e, name);
        return v is { ValueKind: JsonValueKind.String } ? v.Value.GetString() : null;
    }

    private static double? Num(JsonElement e, string name)
    {
        var v = Find(e, name);
        if (v is null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.Number => v.Value.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String => double.TryParse(v.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : null,
            _ => null
        };
    }

    private static bool? Bool(JsonElement e, string name)
    {
        var v = Find(e, name);
        if (v is null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static JsonElement? Obj(JsonElement e, string name)
    {
        var v = Find(e, name);
        return v is { ValueKind: JsonValueKind.Object } ? v : null;
    }

    private static JsonElement? Arr(JsonElement e, string name)
    {
        var v = Find(e, name);
        return v is { ValueKind: JsonValueKind.Array } ? v : null;
    }

    private static DateTime? Date(JsonElement e, string name)
    {
        var s = Str(e, name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static List<int> IntArray(JsonElement e, string name)
    {
        var list = new List<int>();
        var arr = Arr(e, name);
        if (arr is null) return list;
        foreach (var item in arr.Value.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var i) ? i : 0);
        return list;
    }

    private static TimeSpan? Time(JsonElement e, string name)
    {
        var s = Str(e, name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var t)) return t;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt.TimeOfDay;
        return null;
    }
}
