using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Recording;

/// <summary>录制前的格式与清晰度选择（参考 QQ 录屏）。</summary>
public readonly record struct RecordSetupChoice(RecordingKind Kind, int BitRateMbps);

public partial class RecordSetupBar : Window
{
    private readonly TaskCompletionSource<RecordSetupChoice?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _quality = L.T("标准");
    private int _bitRateMbps;

    public RecordSetupBar(RecordingKind kind)
    {
        InitializeComponent();
        if (kind == RecordingKind.Gif) GifRadio.IsChecked = true;
        SourceInitialized += (_, _) => WindowPlacement.ApplyToolWindowStyle(this);
        Closed += (_, _) => _completion.TrySetResult(null);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _completion.TrySetResult(null);
                Close();
            }
        };
    }

    /// <summary>显示控制条并等待用户选择；返回 null 表示退出录制。</summary>
    public Task<RecordSetupChoice?> RunAsync(PixelRect area, MonitorInfo monitor)
    {
        Show();
        UpdateLayout();

        var (width, height) = WindowPlacement.GetPixelSize(this);
        if (width > 0 && height > 0)
        {
            var work = monitor.WorkArea;
            int x = area.Left + (area.Width - width) / 2;
            int y = area.Bottom + 12;
            if (y + height > work.Bottom - 4) y = area.Top - height - 12;
            x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
            y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
            WindowPlacement.MoveTo(this, x, y);
        }

        Activate();
        return _completion.Task;
    }

    /// <summary>清晰度只作用于 MP4 码率，GIF 档位禁用避免“假开关”。</summary>
    private void Format_Changed(object sender, RoutedEventArgs e)
    {
        // 初始化解析 XAML 时 Checked 会先于后续控件赋值触发。
        if (QualityButton is null || GifRadio is null) return;

        bool gif = GifRadio.IsChecked == true;
        QualityButton.IsEnabled = !gif;
        QualityButton.Content = gif ? L.T("GIF 帧") : $"{_quality} ▾";
    }

    private void Quality_Click(object sender, RoutedEventArgs e)
    {
        QualityPopup.IsOpen = true;
    }

    private void QualityOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button) return;
        _quality = (string)button.Content;
        _bitRateMbps = int.TryParse(button.Tag as string, out int value) ? value : 0;
        QualityButton.Content = $"{_quality} ▾";
        QualityPopup.IsOpen = false;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        bool gif = GifRadio.IsChecked == true;
        _completion.TrySetResult(new RecordSetupChoice(
            gif ? RecordingKind.Gif : RecordingKind.Video,
            gif ? 0 : _bitRateMbps));
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _completion.TrySetResult(null);
        Close();
    }
}
