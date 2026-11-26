using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MusicBell.App.Helpers;
using MusicBell.Core.Models;
using MusicBell.Core.Services;

namespace MusicBell.App.Dialogs;

public partial class SongEditorWindow : Window
{
    private readonly SongEntry _source;
    private readonly DateTime _date;
    private readonly string? _selfId;
    private string _repeat;
    private string _sourceKind = SongSources.Netease;
    private bool _conflictConfirmed;
    private DateTime? _suggestedShift;

    public SongEntry? Result { get; private set; }

    public SongEditorWindow(SongEntry entry, bool isNew)
    {
        InitializeComponent();

        _source = entry.Clone();
        _date = entry.Time.Date;
        _repeat = string.IsNullOrWhiteSpace(_source.Repeat) ? RepeatModes.None : _source.Repeat;

        HeadText.Text = isNew ? "排一首歌" : "改一下这首";
        TitleInput.Text = _source.Title;
        TimeInput.Text = entry.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        LabelInput.Text = _source.Label ?? "";
        RequesterInput.Text = _source.Requester ?? "";
        _selfId = isNew ? null : _source.Id;
        EnabledSwitch.IsChecked = _source.Enabled;

        _sourceKind = _source.Source == SongSources.Bili ? SongSources.Bili : SongSources.Netease;
        if (_sourceKind == SongSources.Bili) SrcBili.IsChecked = true;
        else SrcNet.IsChecked = true;

        DateText.Text = _date.ToString("yyyy年M月d日 dddd", new CultureInfo("zh-CN"));

        RepeatNone.IsChecked = _repeat == RepeatModes.None;
        RepeatDaily.IsChecked = _repeat == RepeatModes.Daily;
        RepeatWeekly.IsChecked = _repeat == RepeatModes.Weekly;

        Loaded += (_, _) =>
        {
            TitleInput.Focus();
            TitleInput.SelectAll();
            _ = LoadCoverAsync();
        };
    }

    private void EditorSrc_Checked(object sender, RoutedEventArgs e)
    {
        if (SrcNet is null || SrcBili is null) return;
        _sourceKind = SrcBili.IsChecked == true ? SongSources.Bili : SongSources.Netease;
        if (HintText is not null)
            HintText.Text = _sourceKind == SongSources.Bili
                ? "B 站视频：到点会在浏览器里打开这一页"
                : "写在网易云搜索框里的内容，建议“歌手 歌名”";
        _ = LoadCoverAsync();
    }

    private async System.Threading.Tasks.Task LoadCoverAsync()
    {
        ImageSource? cached;
        if (_sourceKind == SongSources.Bili)
        {
            cached = CoverService.TryCachedBili(_source.SongId);
            if (cached is null) cached = await CoverService.GetBiliAsync(_source.SongId, _source.CoverUrl).ConfigureAwait(true);
        }
        else
        {
            if (!long.TryParse(_source.SongId, out var id) || id <= 0) return;
            cached = CoverService.TryCached(id);
            if (cached is null) cached = await CoverService.GetAsync(id, _source.CoverUrl).ConfigureAwait(true);
        }
        if (cached is null) return;
        CoverBox.Background = CoverService.ToBrush(cached);
        CoverBox.Visibility = Visibility.Visible;
        CoverGlyph.Visibility = Visibility.Collapsed;
    }

    private void ResetConflict()
    {
        _conflictConfirmed = false;
        _suggestedShift = null;
        ShiftBtn.Visibility = Visibility.Collapsed;
        SaveBtn.Content = "保存并排上";
        ErrText.Visibility = Visibility.Collapsed;
    }

    private void TitleInput_TextChanged(object sender, TextChangedEventArgs e) => ResetConflict();

    private void RequesterInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = RequesterInput.Text.Trim();
        if (name.Length == 0)
        {
            RequesterPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var hits = App.Current.Storage.People.Suggest(name, 6)
            .Where(n => !string.Equals(n, name, StringComparison.Ordinal))
            .ToList();
        if (hits.Count == 0)
        {
            RequesterPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RequesterList.ItemsSource = hits;
        RequesterPanel.Visibility = Visibility.Visible;
    }

    private void RequesterInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequesterPanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (e.Key != Key.Enter) return;
        if (RequesterPanel.Visibility == Visibility.Visible && RequesterList.Items.Count > 0
            && RequesterList.Items[0] is string picked)
        {
            RequesterInput.Text = picked;
            RequesterPanel.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void RequesterPick_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name) return;
        RequesterInput.Text = name;
        RequesterPanel.Visibility = Visibility.Collapsed;
    }

    private void Shift_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestedShift is null) return;
        TimeInput.Text = _suggestedShift.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        ResetConflict();
        TimeInput.Focus();
        TimeInput.SelectAll();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string text) TimeInput.Text = text;
        TimeInput.Focus();
        TimeInput.SelectAll();
        ErrText.Visibility = Visibility.Collapsed;
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string mode) _repeat = mode;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleInput.Text.Trim();
        if (title.Length == 0)
        {
            Err("写个歌名或搜索词吧");
            TitleInput.Focus();
            return;
        }

        if (!TryParseTime(TimeInput.Text, out var tod))
        {
            Err("时间是 24 小时制，例如 07:50、12:10");
            TimeInput.Focus();
            TimeInput.SelectAll();
            return;
        }

        var storage = App.Current.Storage;
        var at = _date.Date + tod;
        var conflicts = SongConflicts.Find(storage.Songs, at, _selfId);
        if (conflicts.Count > 0 && !_conflictConfirmed)
        {
            var first = conflicts[0];
            var next = SongConflicts.NextFreeSlot(storage.Songs, at, _selfId);
            _suggestedShift = next;
            _conflictConfirmed = true;
            SaveBtn.Content = "仍然排上";
            ShiftBtn.Content = next is null ? "改个时间" : "顺延到 " + next.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
            ShiftBtn.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
            Err($"{at:HH:mm} 已有「{first.Display}」，再点一次保存就照排");
            return;
        }

        var result = _source.Clone();
        result.Title = title;
        result.Source = _sourceKind;
        if (_sourceKind == SongSources.Bili)
        {
            result.Url = string.IsNullOrWhiteSpace(_source.Url) ? title : _source.Url;
        }
        else
        {
            result.Url = null;
        }
        result.Time = at;
        result.Label = string.IsNullOrWhiteSpace(LabelInput.Text) ? null : LabelInput.Text.Trim();
        result.Repeat = _repeat;
        result.Requester = string.IsNullOrWhiteSpace(RequesterInput.Text) ? null : RequesterInput.Text.Trim();
        result.Enabled = EnabledSwitch.IsChecked != false;

        if (!string.IsNullOrWhiteSpace(result.Requester))
        {
            var people = App.Current.Storage.People;
            people.Ensure(result.Requester!);
            people.Save();
        }

        Result = result;
        DialogResult = true;
        Close();
    }

    private void Err(string message)
    {
        ErrText.Text = message;
        ErrText.Visibility = Visibility.Visible;
    }

    private static bool TryParseTime(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim()
            .Replace('：', ':')
            .Replace("点", ":")
            .Replace("时", ":")
            .Replace("時", ":");

        if (s.EndsWith("分", StringComparison.Ordinal)) s = s[..^1];
        if (s.EndsWith("半", StringComparison.Ordinal))
        {
            s = s[..^1];
            if (s.EndsWith(":", StringComparison.Ordinal)) s = s[..^1];
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h is >= 0 and <= 23)
            {
                value = new TimeSpan(h, 30, 0);
                return true;
            }
            return false;
        }

        var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            var digits = new string(parts[0].Where(char.IsDigit).ToArray());
            if (digits.Length is 3 or 4 && int.TryParse(digits, out var hhmm))
            {
                var hh = hhmm / 100;
                var mm = hhmm % 100;
                if (hh is >= 0 and <= 23 && mm is >= 0 and <= 59)
                {
                    value = new TimeSpan(hh, mm, 0);
                    return true;
                }
                return false;
            }

            if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)
                && hour is >= 0 and <= 23)
            {
                value = new TimeSpan(hour, 0, 0);
                return true;
            }
            return false;
        }

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h2)) return false;
        var m2 = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out m2)) return false;
        if (h2 is < 0 or > 23 || m2 is < 0 or > 59) return false;

        value = new TimeSpan(h2, m2, 0);
        return true;
    }
}
