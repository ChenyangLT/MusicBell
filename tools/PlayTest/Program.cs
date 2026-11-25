using System.Text;
using MusicBell.Core.Services;

Console.OutputEncoding = Encoding.UTF8;
var query = args.Length > 0 ? string.Join(' ', args) : "周杰伦 晴天";
var storage = StorageService.Load();
var player = new SongPlayerService(storage);

var t0 = DateTime.Now;
var result = await player.PlayAsync(query, null, m => Console.WriteLine("  · " + m));
Console.WriteLine($"结果：success={result.Success} method={result.Method} msg={result.Message} 耗时={(DateTime.Now - t0).TotalMilliseconds:F0}ms");
return result.Success ? 0 : 1;
