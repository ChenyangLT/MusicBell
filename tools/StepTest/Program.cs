using System.Drawing;
using System.Drawing.Imaging;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var cfg = new AppConfig();
var mode = args.Length > 0 ? args[0] : "shot";
var keyword = args.Length > 1 ? args[1] : "周杰伦 晴天";
var outPath = args.Length > 2 ? args[2] : Path.Combine(Environment.CurrentDirectory, "_work", "step_shot.png");

void Log(string m) => Console.WriteLine("  " + m);

if (mode == "shot")
{
    var hwnd = await NetEaseAutomation.EnsureWindowAsync(cfg.NetEasePathResolved, 20000, Log);
    Console.WriteLine("hwnd=" + hwnd);
    if (hwnd == IntPtr.Zero) return 1;
    NetEaseAutomation.BringToFront(hwnd);
    await Task.Delay(400);
    var ok = NetEaseAutomation.SearchInBox(hwnd, keyword, cfg.SearchBoxX, cfg.SearchBoxY, 0, 0, cfg.KeyDelayMs, true, Log);
    Console.WriteLine("search=" + ok);
    await Task.Delay(2800);
    Shot(hwnd, outPath);
    Console.WriteLine("shot=" + outPath);
    return 0;
}

if (mode == "dbl")
{
    var rx = double.Parse(args[1]);
    var ry = double.Parse(args[2]);
    if (args.Length > 3) outPath = args[3];
    var hwnd = await NetEaseAutomation.EnsureWindowAsync(cfg.NetEasePathResolved, 20000, Log);
    if (hwnd == IntPtr.Zero) return 1;
    NetEaseAutomation.BringToFront(hwnd);
    await Task.Delay(350);
    var before = MusicBell.Core.Interop.Win32.Title(hwnd);
    Console.WriteLine("title before = " + before);
    var pt = PlayStepEngine.ResolvePoint(hwnd, new PlayStep { X = rx, Y = ry }, new PlayStepEngine.Options());
    NetEaseAutomation.DoubleClick(pt.X, pt.Y);
    Console.WriteLine("double clicked " + pt.X + "," + pt.Y);
    await Task.Delay(2000);
    Console.WriteLine("title after  = " + MusicBell.Core.Interop.Win32.Title(hwnd));
    Shot(hwnd, outPath);
    Console.WriteLine("shot=" + outPath);
    return 0;
}

if (mode == "try")
{
    // try <keyword> <out> [rowY]
    var rowY = args.Length > 3 ? double.Parse(args[3]) : 0.308;
    var hwnd = await NetEaseAutomation.EnsureWindowAsync(cfg.NetEasePathResolved, 20000, Log);
    if (hwnd == IntPtr.Zero) return 1;
    NetEaseAutomation.BringToFront(hwnd);
    await Task.Delay(300);
    NetEaseAutomation.SearchInBox(hwnd, keyword, cfg.SearchBoxX, cfg.SearchBoxY, 0, 0, cfg.KeyDelayMs, true, Log);
    await Task.Delay(2600);
    var tab = PlayStepEngine.ResolvePoint(hwnd, new PlayStep { X = 0.230, Y = 0.180 }, new PlayStepEngine.Options());
    NetEaseAutomation.Click(tab.X, tab.Y);
    Console.WriteLine("clicked 单曲 tab");
    await Task.Delay(1100);
    var before = MusicBell.Core.Interop.Win32.Title(hwnd);
    Console.WriteLine("title before = " + before);
    var pt = PlayStepEngine.ResolvePoint(hwnd, new PlayStep { X = 0.290, Y = rowY }, new PlayStepEngine.Options());
    NetEaseAutomation.DoubleClick(pt.X, pt.Y);
    Console.WriteLine("double clicked " + pt.X + "," + pt.Y);
    for (var i = 0; i < 12; i++)
    {
        await Task.Delay(300);
        var t = MusicBell.Core.Interop.Win32.Title(hwnd);
        if (!string.Equals(t, before, StringComparison.Ordinal)) { Console.WriteLine("title after  = " + t); break; }
        if (i == 11) Console.WriteLine("title unchanged: " + t);
    }
    await Task.Delay(900);
    Shot(hwnd, outPath);
    Console.WriteLine("shot=" + outPath);
    return 0;
}

if (mode == "click")
{
    var rx = double.Parse(args[1]);
    var ry = double.Parse(args[2]);
    if (args.Length > 3) outPath = args[3];
    var hwnd = await NetEaseAutomation.EnsureWindowAsync(cfg.NetEasePathResolved, 20000, Log);
    if (hwnd == IntPtr.Zero) return 1;
    NetEaseAutomation.BringToFront(hwnd);
    await Task.Delay(300);
    var pt = PlayStepEngine.ResolvePoint(hwnd, new PlayStep { X = rx, Y = ry }, new PlayStepEngine.Options());
    NetEaseAutomation.Click(pt.X, pt.Y);
    Console.WriteLine($"clicked {pt.X},{pt.Y}");
    await Task.Delay(1200);
    Shot(NetEaseAutomation.FindMainWindow(), outPath);
    Console.WriteLine("shot=" + outPath);
    return 0;
}

if (mode == "steps")
{
    var steps = DefaultPlaySteps.Create(cfg);
    var hwnd = await NetEaseAutomation.EnsureWindowAsync(cfg.NetEasePathResolved, 20000, Log);
    if (hwnd == IntPtr.Zero) { Console.WriteLine("no window"); return 1; }
    var options = new PlayStepEngine.Options
    {
        Query = keyword,
        Label = keyword,
        KeyDelayMs = cfg.KeyDelayMs,
        RestoreClipboard = true,
        AutoPlayFirst = true,
        OpenNowPlaying = args.Contains("--now"),
        BringToFront = true,
        Log = Log
    };
    var result = await PlayStepEngine.RunAsync(steps, options);
    Console.WriteLine($"success={result.Success} method={result.Method} msg={result.Message} elapsed={result.Elapsed.TotalMilliseconds:F0}ms");
    await Task.Delay(1200);
    Shot(NetEaseAutomation.FindMainWindow(), outPath);
    Console.WriteLine("shot=" + outPath);
    return result.Success ? 0 : 2;
}

Console.WriteLine("用法: StepTest shot|steps [关键词] [截图路径] [--now]");
return 1;

static void Shot(IntPtr hwnd, string path)
{
    if (hwnd == IntPtr.Zero) return;
    MusicBell.Core.Interop.Win32.GetWindowRect(hwnd, out var r);
    var w = Math.Max(1, r.Right - r.Left);
    var h = Math.Max(1, r.Bottom - r.Top);
    using var bmp = new Bitmap(w, h);
    using var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    bmp.Save(path, ImageFormat.Png);
}
