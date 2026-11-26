using MusicBell.Core.Services;

var cases = new (long Id, string Title)[]
{
    (3458269805, "烂奇迹"),
    (2657613721, "LIFE"),
    (3330872139, "Colorful Array"),
    (186016, "晴天"),
};

foreach (var (id, title) in cases)
{
    var doc = await NeteaseStreamClient.GetLyricAsync(id, title);
    var first = doc.Lines.Count > 0 ? doc.Lines[0].Text : (doc.PlainLines.Count > 0 ? doc.PlainLines[0] : "-");
    Console.WriteLine($"{title,-16} id={id} lines={doc.Lines.Count,4} plain={doc.PlainLines.Count,3} trans={doc.HasTranslation} src={doc.Source ?? "-"} first={first}");
    var stream = await NeteaseStreamClient.ResolveStreamAsync(id, null);
    Console.WriteLine($"    stream ok={stream.Ok} br={stream.Bitrate} msg={stream.Message}");
    await Task.Delay(300);
}
