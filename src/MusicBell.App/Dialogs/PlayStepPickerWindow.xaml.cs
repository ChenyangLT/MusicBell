using System;
using System.Windows;
using System.Windows.Input;
using MusicBell.Core.Interop;
using MusicBell.Core.Services;

namespace MusicBell.App.Dialogs;

/// <summary>全屏半透明取点窗：用户点哪里，就把它记成网易云客户区的比例坐标。</summary>
public partial class PlayStepPickerWindow : Window
{
    public bool Picked { get; private set; }

    /// <summary>横向比例 0~1。</summary>
    public double PickedX { get; private set; }

    /// <summary>纵向比例 0~1。</summary>
    public double PickedY { get; private set; }

    public PlayStepPickerWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var screen = PointToScreen(e.GetPosition(this));
        var hwnd = NetEaseAutomation.FindMainWindow();
        if (hwnd == IntPtr.Zero)
        {
            Hint.Text = "没找到网易云窗口，先把网易云打开再试 · 按 Esc 退出";
            Hint.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        if (!Win32.GetClientRect(hwnd, out var client))
        {
            DialogResult = false;
            return;
        }

        var pt = new Win32.POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        Win32.ScreenToClient(hwnd, ref pt);

        var w = Math.Max(1, client.Right - client.Left);
        var h = Math.Max(1, client.Bottom - client.Top);
        var rx = Math.Clamp(pt.X / (double)w, 0.0, 1.0);
        var ry = Math.Clamp(pt.Y / (double)h, 0.0, 1.0);

        if (pt.X < 0 || pt.Y < 0 || pt.X > w || pt.Y > h)
        {
            Hint.Text = "那个点在网易云窗口外面，再点一次窗口里的位置 · 按 Esc 取消";
            Hint.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        PickedX = Math.Round(rx, 4);
        PickedY = Math.Round(ry, 4);
        Picked = true;
        DialogResult = true;
        Close();
    }
}
