using System.Text.Json;
using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>点歌人名单（people.json）。可以从 Excel 名单导入，输入时做联想。</summary>
public sealed class PeopleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] NameHeaders =
        { "姓名", "名字", "学生姓名", "学生", "点歌人", "点播人", "点歌者", "name", "Name", "学生名" };

    public string FilePath { get; }
    public List<Person> People { get; private set; } = new();

    private PeopleStore(string filePath) => FilePath = filePath;

    public static PeopleStore Load(string dataDir)
    {
        var store = new PeopleStore(Path.Combine(dataDir, "people.json"));
        try
        {
            if (File.Exists(store.FilePath))
                store.People = JsonSerializer.Deserialize<List<Person>>(File.ReadAllText(store.FilePath), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取名单失败：" + ex.Message);
            store.People = new();
        }
        store.People.RemoveAll(p => string.IsNullOrWhiteSpace(p.Name));
        return store;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(People, JsonOptions));
        }
        catch (Exception ex) { AppLog.Error("保存名单失败", ex); }
    }

    public Person? Find(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) return null;
        return People.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>点歌时自动建档（新名字才加）。</summary>
    public Person Ensure(string name)
    {
        name = name?.Trim() ?? "";
        var found = Find(name);
        if (found is not null) return found;
        var p = new Person { Name = name };
        People.Add(p);
        Save();
        return p;
    }

    public void Remove(string name)
    {
        People.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>输入联想：前缀优先，其次包含。</summary>
    public IReadOnlyList<string> Suggest(string text, int limit = 8)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0) return Array.Empty<string>();
        var prefix = new List<string>();
        var contains = new List<string>();
        foreach (var p in People)
        {
            if (p.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase)) prefix.Add(p.Name);
            else if (p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) contains.Add(p.Name);
        }
        return prefix.Concat(contains).Take(limit).ToList();
    }

    /// <summary>整份替换（导入名单）。</summary>
    public void ReplaceAll(IEnumerable<Person> people)
    {
        People = people.Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .ToList();
        Save();
    }

    public int MergeFrom(IEnumerable<Person> people)
    {
        var added = 0;
        var existing = new HashSet<string>(People.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var p in people)
        {
            var name = p.Name?.Trim() ?? "";
            if (name.Length == 0 || !existing.Add(name)) continue;
            People.Add(new Person { Name = name, Group = p.Group });
            added++;
        }
        if (added > 0) Save();
        return added;
    }

    /// <summary>从表格里认名单：优先找「姓名」一类的表头列，找不到就用第一列。</summary>
    public static (List<Person> People, string ColumnLabel) FromTable(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return (new List<Person>(), "无数据");

        // 1. 在前 5 行里找表头
        var headerRow = -1;
        var col = -1;
        var headerText = "";
        for (var r = 0; r < Math.Min(rows.Count, 5) && col < 0; r++)
        {
            var cells = rows[r];
            for (var c = 0; c < cells.Length; c++)
            {
                var cell = cells[c].Trim();
                if (cell.Length == 0) continue;
                if (NameHeaders.Any(h => string.Equals(h, cell, StringComparison.OrdinalIgnoreCase) ||
                                         cell.Contains(h, StringComparison.OrdinalIgnoreCase) && h.Length > 1 && cell.Length <= 8))
                {
                    headerRow = r;
                    col = c;
                    headerText = cell;
                    break;
                }
            }
        }

        var people = new List<Person>();
        if (col < 0)
        {
            // 2. 没有表头：取第一列，跳过看起来像表头的那一行
            col = 0;
            headerText = "第一列";
            var start = 0;
            if (rows[0].Length > 0 && rows[0][0].Length <= 8 && NameHeaders.Contains(rows[0][0])) start = 1;
            for (var r = start; r < rows.Count; r++)
            {
                var name = rows[r].Length > 0 ? rows[r][0].Trim() : "";
                if (name.Length == 0) continue;
                people.Add(new Person { Name = name });
            }
        }
        else
        {
            // 表头右侧常见「班级 / 组别」列，顺便带上
            var groupCol = -1;
            for (var c = 0; c < rows[headerRow].Length; c++)
            {
                var h = rows[headerRow][c].Trim();
                if (h.Length == 0 || c == col) continue;
                if (h.Contains("班") || h.Contains("组") || h.Contains("队")) { groupCol = c; break; }
            }
            for (var r = headerRow + 1; r < rows.Count; r++)
            {
                var row = rows[r];
                var name = col < row.Length ? row[col].Trim() : "";
                if (name.Length == 0) continue;
                var group = groupCol >= 0 && groupCol < row.Length ? row[groupCol].Trim() : null;
                people.Add(new Person { Name = name, Group = string.IsNullOrWhiteSpace(group) ? null : group });
            }
        }

        return (people, headerText);
    }
}
