using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

namespace MusicBell.App.Dialogs;

/// <summary>点歌流程编辑器：把「点哪里 / 粘贴什么 / 等多久」拆成可增删排序的步骤卡片。</summary>
public partial class PlayStepsWindow : Window
{
    private readonly List<PlayStep> _steps = new();

    public PlayStepsWindow()
    {
        InitializeComponent();
        var cfg = App.Current.Storage.Config;
        var source = cfg.ClientSteps is { Count: > 0 } ? cfg.ClientSteps : DefaultPlaySteps.Create(cfg);
        foreach (var step in source) _steps.Add(step.Clone());
        Reload();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseWith(false);
        };
    }

    private void Reload()
    {
        var rows = new List<StepRowVm>();
        for (var i = 0; i < _steps.Count; i++) rows.Add(new StepRowVm(i + 1, _steps[i]));
        StepList.ItemsSource = rows;
        CountText.Text = _steps.Count + " 步";
        EmptyText.Visibility = _steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Swap(List<PlayStep> list, int a, int b)
    {
        (list[a], list[b]) = (list[b], list[a]);
    }

    private int IndexOf(PlayStep step) => _steps.FindIndex(s => ReferenceEquals(s, step));

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var kind = (sender as FrameworkElement)?.Tag as string;
        var cfg = App.Current.Storage.Config;
        var step = kind switch
        {
            PlayStepKinds.DoubleClick => new PlayStep
            {
                Kind = PlayStepKinds.DoubleClick, Name = "双击第一首", X = cfg.PlayRowX, Y = cfg.PlayRowY,
                WaitMs = 600, WaitFor = "title", Gate = "play"
            },
            PlayStepKinds.Paste => new PlayStep
            {
                Kind = PlayStepKinds.Paste, Name = "粘贴歌名", Text = "{query}", WaitMs = 200
            },
            PlayStepKinds.Key => new PlayStep
            {
                Kind = PlayStepKinds.Key, Name = "回车搜索", Text = "Enter", WaitMs = 300
            },
            PlayStepKinds.Wait => new PlayStep
            {
                Kind = PlayStepKinds.Wait, Name = "等一会", WaitMs = 800
            },
            PlayStepKinds.Url => new PlayStep
            {
                Kind = PlayStepKinds.Url, Name = "打开链接", Text = "orpheus://search?keyword={query}", WaitMs = 400
            },
            _ => new PlayStep
            {
                Kind = PlayStepKinds.Click, Name = "点一下", X = cfg.SearchBoxX, Y = cfg.SearchBoxY, WaitMs = 300
            }
        };
        _steps.Add(step);
        Reload();
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not StepRowVm row) return;
        var i = IndexOf(row.Step);
        if (i <= 0) return;
        Swap(_steps, i, i - 1);
        Reload();
    }

    private void Down_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not StepRowVm row) return;
        var i = IndexOf(row.Step);
        if (i < 0 || i >= _steps.Count - 1) return;
        Swap(_steps, i, i + 1);
        Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not StepRowVm row) return;
        _steps.Remove(row.Step);
        Reload();
    }

    private async void Pick_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not StepRowVm row) return;

        PlayStep step = row.Step;
        var hwnd = NetEaseAutomation.FindMainWindow();
        if (hwnd == IntPtr.Zero)
        {
            MessageBox.Show(this, "还没找到网易云窗口，先把网易云打开再来拾取。", "拾取位置",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NetEaseAutomation.BringToFront(hwnd);
        await Task.Delay(400);

        // 注意：模态窗不能用 Hide()/Show()，那会把 ShowDialog 的对话框状态清掉，
        // 之后的 DialogResult 就会抛 InvalidOperationException。改成把窗口变透明。
        Opacity = 0;
        await Task.Delay(160);
        var picker = new PlayStepPickerWindow();
        var ok = picker.ShowDialog();
        Opacity = 1;
        Activate();

        if (ok == true && picker.Picked)
        {
            step.X = picker.PickedX;
            step.Y = picker.PickedY;
            if (string.IsNullOrWhiteSpace(step.Name) || step.Name == "点一下" || step.Name == "双击一下")
            {
                // 留个记号，方便一眼看出是哪一步
                step.Name = step.Kind == PlayStepKinds.DoubleClick ? "双击第一首" : "点击";
            }
            Reload();
        }
    }

    private void EditText_Changed(object sender, TextChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not StepRowVm row) return;
        var text = (sender as TextBox)?.Text ?? "";
        if (row.Step.Kind == PlayStepKinds.Wait)
        {
            if (int.TryParse(text.Trim(), out var ms)) row.Step.WaitMs = Math.Clamp(ms, 0, 60000);
        }
        else
        {
            row.Step.Text = text;
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.Current.Storage.Config;
        _steps.Clear();
        _steps.AddRange(DefaultPlaySteps.Create(cfg));
        Reload();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.Current.Storage.Config;
        cfg.ClientSteps = _steps.Select(s => s.Clone()).ToList();
        App.Current.Storage.SaveConfig();
        AppLog.Info("点歌流程已保存：" + cfg.ClientSteps.Count + " 步");
        CloseWith(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWith(false);

    /// <summary>关掉窗口。对话框状态万一丢了（比如中途 Show 过），DialogResult 会抛异常，这里兜住，保证窗口一定能关。</summary>
    private void CloseWith(bool ok)
    {
        try { DialogResult = ok; }
        catch (InvalidOperationException) { /* 不是对话框状态，直接关就行 */ }
        Close();
    }
}

/// <summary>列表里的一行。</summary>
public sealed class StepRowVm
{
    public StepRowVm(int index, PlayStep step)
    {
        Index = index;
        Step = step;
    }

    public int Index { get; }

    public PlayStep Step { get; }

    public string Title => string.IsNullOrWhiteSpace(Step.Name) ? Step.Describe() : Step.Name!;

    public string Detail
    {
        get
        {
            var extra = Step.Gate switch
            {
                "play" => " · 仅自动播放时",
                "now" => " · 仅打开详情页时",
                _ => ""
            };
            if (Step.WaitFor == "title") extra += " · 等标题变化";

            return Step.Kind switch
            {
                PlayStepKinds.Click or PlayStepKinds.DoubleClick =>
                    $"位置 {Step.X:0.###}, {Step.Y:0.###}（相对窗口）· 之后等 {Step.WaitMs} ms{extra}",
                PlayStepKinds.Paste => $"粘贴「{Step.Text}」· 之后等 {Step.WaitMs} ms{extra}",
                PlayStepKinds.Key => $"按键 {Step.Text} · 之后等 {Step.WaitMs} ms{extra}",
                PlayStepKinds.Wait => "只等一下，不做别的",
                PlayStepKinds.Url => $"打开 {Step.Text}，之后等 {Step.WaitMs} ms",
                _ => Step.Describe()
            };
        }
    }

    public Visibility PickVis =>
        Step.Kind is PlayStepKinds.Click or PlayStepKinds.DoubleClick ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EditVis =>
        Step.Kind is PlayStepKinds.Paste or PlayStepKinds.Key or PlayStepKinds.Url or PlayStepKinds.Wait
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string EditText => Step.Kind == PlayStepKinds.Wait ? Step.WaitMs.ToString() : Step.Text ?? "";
}
