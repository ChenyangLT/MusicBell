using System.Diagnostics;
using MusicBell.Core.Interop;
using MusicBell.Core.Models;

namespace MusicBell.Core.Services;

/// <summary>跑自定义点歌流程：每一步都用「客户区比例」换算成真实像素，点击前做命中校验，双击后等标题真的变了再继续。</summary>
public static class PlayStepEngine
{
    public sealed class Options
    {
        public string Query { get; init; } = "";
        public string Label { get; init; } = "";
        public int KeyDelayMs { get; init; } = 140;
        public bool RestoreClipboard { get; init; } = true;
        public bool AutoPlayFirst { get; init; } = true;
        public bool OpenNowPlaying { get; init; } = true;
        public bool BringToFront { get; init; } = true;
        public int ClickOffsetX { get; init; }
        public int ClickOffsetY { get; init; }
        public Action<string>? Log { get; init; }
    }

    public static async Task<PlayResult> RunAsync(IReadOnlyList<PlayStep> steps, Options options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var log = options.Log ?? (_ => { });
        var notes = new List<string>();
        var clipboardBefore = options.RestoreClipboard ? ClipboardHelper.TryGetText() : null;
        var clipboardTouched = false;

        try
        {
            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();
                if (!ShouldRun(step, options)) continue;

                switch (step.Kind)
                {
                    case PlayStepKinds.Click:
                    case PlayStepKinds.DoubleClick:
                    {
                        var hwnd = NetEaseAutomation.FindMainWindow();
                        if (hwnd == IntPtr.Zero)
                            return PlayResult.Fail(options.Query, "没找到网易云窗口");
                        var point = ResolvePoint(hwnd, step, options);
                        if (options.BringToFront) NetEaseAutomation.BringToFront(hwnd);
                        await Task.Delay(Math.Max(120, options.KeyDelayMs)).ConfigureAwait(false);

                        if (!HitsWindow(hwnd, point))
                        {
                            log($"「{step.Describe()}」落点不在网易云窗口上，重新置前再试");
                            NetEaseAutomation.BringToFront(hwnd);
                            await Task.Delay(280).ConfigureAwait(false);
                        }

                        if (step.Kind == PlayStepKinds.Click)
                        {
                            NetEaseAutomation.Click(point.X, point.Y);
                            log(step.Describe());
                        }
                        else
                        {
                            var before = Win32.Title(hwnd);
                            NetEaseAutomation.DoubleClick(point.X, point.Y);
                            log(step.Describe());
                            if (string.Equals(step.WaitFor, "title", StringComparison.OrdinalIgnoreCase))
                            {
                                var changed = await WaitTitleChangeAsync(before, 3600, ct).ConfigureAwait(false);
                                if (changed)
                                {
                                    notes.Add("已双击第一首并开始播放");
                                }
                                else
                                {
                                    // 标题没变通常是因为这首本来就在放：双击等于从头播，等它起来就行，不要重复双击（会变成暂停）
                                    log("标题没变：可能本来就在放这首，等 1.4s 让它起来");
                                    await Task.Delay(1400, ct).ConfigureAwait(false);
                                    notes.Add("已双击第一首");
                                }
                            }
                        }
                        break;
                    }
                    case PlayStepKinds.Paste:
                    {
                        var hwnd = NetEaseAutomation.FindMainWindow();
                        if (hwnd == IntPtr.Zero) return PlayResult.Fail(options.Query, "没找到网易云窗口");
                        if (options.BringToFront) NetEaseAutomation.BringToFront(hwnd);
                        await Task.Delay(Math.Max(120, options.KeyDelayMs)).ConfigureAwait(false);
                        var text = Expand(step.Text, options);
                        if (!ClipboardHelper.TrySetText(text))
                            return PlayResult.Fail(options.Query, "写剪贴板失败");
                        clipboardTouched = true;
                        await Task.Delay(80).ConfigureAwait(false);
                        NetEaseAutomation.KeyCombo(Win32.VK_CONTROL, Win32.VK_V);
                        log("粘贴 " + text);
                        break;
                    }
                    case PlayStepKinds.Key:
                    {
                        var hwnd = NetEaseAutomation.FindMainWindow();
                        if (hwnd == IntPtr.Zero) return PlayResult.Fail(options.Query, "没找到网易云窗口");
                        if (options.BringToFront) NetEaseAutomation.BringToFront(hwnd);
                        await Task.Delay(Math.Max(100, options.KeyDelayMs)).ConfigureAwait(false);
                        SendCombo(step.Text);
                        log("按键 " + step.Text);
                        break;
                    }
                    case PlayStepKinds.Wait:
                        break;
                    case PlayStepKinds.Url:
                        log(step.Describe());
                        NetEaseAutomation.OpenUrl(Expand(step.Text, options), log);
                        break;
                }

                var wait = step.WaitMs;
                if (wait > 0) await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            sw.Stop();
            var message = notes.Count > 0 ? string.Join("，", notes) : "已按自定义流程执行";
            return PlayResult.Ok(options.Query, PlayMethod.SearchBox, message) with { Elapsed = sw.Elapsed };
        }
        catch (OperationCanceledException)
        {
            return PlayResult.Fail(options.Query, "已取消");
        }
        catch (Exception ex)
        {
            AppLog.Error("执行点歌流程失败", ex);
            return PlayResult.Fail(options.Query, "执行流程出错：" + ex.Message);
        }
        finally
        {
            if (options.RestoreClipboard && clipboardTouched && clipboardBefore is not null)
                ClipboardHelper.TrySetText(clipboardBefore);
        }
    }

    private static bool ShouldRun(PlayStep step, Options options)
    {
        if (string.Equals(step.Gate, "play", StringComparison.OrdinalIgnoreCase) && !options.AutoPlayFirst) return false;
        if (string.Equals(step.Gate, "now", StringComparison.OrdinalIgnoreCase) && !options.OpenNowPlaying) return false;
        return true;
    }

    private static string Expand(string? template, Options options)
        => (template ?? "")
            .Replace("{query}", options.Query)
            .Replace("{label}", string.IsNullOrWhiteSpace(options.Label) ? options.Query : options.Label);

    /// <summary>把比例换算成屏幕像素（每次都用最新的客户区尺寸，所以改分辨率/DPI 也不会错位）。</summary>
    public static Win32.POINT ResolvePoint(IntPtr hwnd, PlayStep step, Options options)
    {
        if (!Win32.GetClientRect(hwnd, out var rect)) return new Win32.POINT();
        var w = Math.Max(1, rect.Right - rect.Left);
        var h = Math.Max(1, rect.Bottom - rect.Top);
        var x = (int)Math.Round(w * Math.Clamp(step.X, 0, 1)) + options.ClickOffsetX;
        var y = (int)Math.Round(h * Math.Clamp(step.Y, 0, 1)) + options.ClickOffsetY;
        var pt = new Win32.POINT { X = x, Y = y };
        Win32.ClientToScreen(hwnd, ref pt);
        return pt;
    }

    /// <summary>命中校验：点下去真的落在网易云窗口上（而不是被别的窗口盖住）。</summary>
    private static bool HitsWindow(IntPtr hwnd, Win32.POINT point)
    {
        var target = Win32.WindowFromPoint(point);
        if (target == IntPtr.Zero) return false;
        var root = Win32.GetAncestor(target, Win32.GA_ROOT);
        return root == hwnd || target == hwnd;
    }

    private static async Task<bool> WaitTitleChangeAsync(string before, int timeoutMs, CancellationToken ct)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(180, ct).ConfigureAwait(false);
            var hwnd = NetEaseAutomation.FindMainWindow();
            if (hwnd == IntPtr.Zero) continue;
            var now = Win32.Title(hwnd);
            if (!string.IsNullOrWhiteSpace(now) && !string.Equals(now, before, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>发送 "Ctrl+A" / "Enter" 这类组合键。</summary>
    public static void SendCombo(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var parts = text!.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mods = new List<byte>();
        byte vk = 0;
        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods.Add(Win32.VK_CONTROL); break;
                case "alt": mods.Add(Win32.VK_MENU); break;
                case "shift": mods.Add(Win32.VK_SHIFT); break;
                case "win": mods.Add(Win32.VK_LWIN); break;
                default: vk = VirtualKey(raw); break;
            }
        }
        if (vk == 0) return;

        foreach (var m in mods) Win32.keybd_event(m, 0, 0, IntPtr.Zero);
        Thread.Sleep(40);
        Win32.keybd_event(vk, 0, 0, IntPtr.Zero);
        Thread.Sleep(40);
        Win32.keybd_event(vk, 0, Win32.KEYEVENTF_KEYUP, IntPtr.Zero);
        for (var i = mods.Count - 1; i >= 0; i--)
        {
            Thread.Sleep(30);
            Win32.keybd_event(mods[i], 0, Win32.KEYEVENTF_KEYUP, IntPtr.Zero);
        }
    }

    private static byte VirtualKey(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "enter": case "return": return 0x0D;
            case "esc": case "escape": return 0x1B;
            case "tab": return 0x09;
            case "space": return 0x20;
            case "backspace": return 0x08;
            case "delete": return 0x2E;
            case "up": return 0x26;
            case "down": return 0x28;
            case "left": return 0x25;
            case "right": return 0x27;
        }
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return (byte)c;
        }
        return 0;
    }
}
