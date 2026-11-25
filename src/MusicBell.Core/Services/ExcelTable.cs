using System.Text;
using ExcelDataReader;

namespace MusicBell.Core.Services;

/// <summary>读 Excel（.xlsx / .xls）与 .csv 的原始表格。表头不做假设，上层自己认列。</summary>
public static class ExcelTable
{
    private static bool _encRegistered;

    private static void EnsureEncoding()
    {
        if (_encRegistered) return;
        _encRegistered = true;
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
    }

    /// <summary>读第一个工作表，返回逐行字符串。失败抛异常（调用方显示原因）。</summary>
    public static List<string[]> Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".csv") return ReadCsv(path);

        EnsureEncoding();
        var rows = new List<string[]>();
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            while (reader.Read())
            {
                var cells = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    cells[i] = Convert.ToString(reader.GetValue(i))?.Trim() ?? "";
                rows.Add(cells);
            }
            // 只读第一个非空工作表
            if (rows.Any(r => r.Any(c => c.Length > 0))) break;
        } while (reader.NextResult());
        return rows;
    }

    private static List<string[]> ReadCsv(string path)
    {
        EnsureEncoding();
        var rows = new List<string[]>();
        var bytes = File.ReadAllBytes(path);
        var text = Decode(bytes);
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.Length == 0) continue;
            rows.Add(SplitCsvLine(line));
        }
        return rows;
    }

    private static string Decode(byte[] bytes)
    {
        // 有 BOM 按 BOM；无 BOM 先试 UTF-8，失败回退 GB18030
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            var strict = new UTF8Encoding(false, true);
            return strict.GetString(bytes);
        }
        catch
        {
            try { return Encoding.GetEncoding("GB18030").GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }

    private static string[] SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else quoted = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') quoted = true;
                else if (ch == ',' || ch == '\t') { cells.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(ch);
            }
        }
        cells.Add(sb.ToString().Trim());
        return cells.ToArray();
    }
}
