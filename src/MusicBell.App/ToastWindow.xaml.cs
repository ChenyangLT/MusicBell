using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MusicBell.App;

public enum ToastKind { Info, Playing, Success, Error }

public partial class ToastWindow : Window
{
    private static ToastWindow? _instance;
    private static readonly object Gate = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(4200) };

    public ToastWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => { _timer.Stop(); FadeOut(); };
        MouseLeftButtonUp += (_, _) => { HideToast(); App.Current.ShowMain(); };
    }

    public static void Show(string title, string subtitle, string foot = "", ToastKind kind = ToastKind.Info, int ms = 4200)
    {
        var app = Application.Current as App;
        app?.Dispatcher.BeginInvoke(new Action(() => ShowCore(title, subtitle, foot, kind, ms)));
    }

    private static void ShowCore(string title, string subtitle, string foot, ToastKind kind, int ms)
    {
        lock (Gate)
        {
            if (_instance is null)
            {
                _instance = new ToastWindow();
            }
            var w = _instance;
            w.TitleText.Text = title;
            w.SubText.Text = subtitle;
            w.FootText.Text = foot;
            w.FootText.Visibility = string.IsNullOrWhiteSpace(foot) ? Visibility.Collapsed : Visibility.Visible;
            w.IconGlyph.Text = kind switch
            {
                ToastKind.Playing => "\uE768",
                ToastKind.Success => "\uE73E",
                ToastKind.Error => "\uEA39",
                _ => "\uE7BA"
            };
            w.IconGlyph.Foreground = kind switch
            {
                ToastKind.Error => new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A)),
                ToastKind.Success => new SolidColorBrush(Color.FromRgb(0x4F, 0xD1, 0xA5)),
                _ => Brushes.White
            };
            w.ShowToast(ms);
        }
    }

    private void ShowToast(int ms)
    {
        var area = SystemParameters.WorkArea;
        UpdateLayout();
        double height = ActualHeight > 0 ? ActualHeight : 110;
        double targetLeft = area.Right - Width - 6;
        double targetTop = area.Bottom - height - 6;

        if (!IsVisible)
        {
            Left = area.Right - 6;
            Top = targetTop;
            Opacity = 0;
            Show();
            UpdateLayout();
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(LeftProperty, new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        BeginAnimation(TopProperty, new DoubleAnimation(Top, targetTop, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(200)));

        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1200, ms));
        _timer.Start();
    }

    private void FadeOut()
    {
        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        anim.Completed += (_, _) => HideToast();
        BeginAnimation(OpacityProperty, anim);
    }

    private void HideToast()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }
}
