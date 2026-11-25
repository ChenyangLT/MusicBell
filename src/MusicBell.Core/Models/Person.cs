namespace MusicBell.Core.Models;

/// <summary>一位点歌人（来自 Excel 名单或手动添加）。</summary>
public sealed class Person
{
    public string Name { get; set; } = "";

    /// <summary>分组 / 班级，可空。</summary>
    public string? Group { get; set; }

    public string? Note { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.Now;

    public override string ToString() => Name;
}
