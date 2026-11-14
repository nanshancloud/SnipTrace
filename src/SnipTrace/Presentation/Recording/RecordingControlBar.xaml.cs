using System;
using System.Windows;
using System.Windows.Media;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Recording;

public partial class RecordingControlBar : Window
{
    private static readonly Brush RecordingBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F)));
    private static readonly Brush PausedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xB0, 0x2E)));

    private readonly int _fps;
    private bool _paused;

    public RecordingControlBar(RecordingKind kind, int fps)
    {
        InitializeComponent();
        _fps = fps;
        MetaText.Text = $"{fps} FPS · 0 帧";
        DetailText.Text = kind == RecordingKind.Gif
            ? "正在录制 GIF，鼠标移出控制条可继续操作"
            : "正在录制 MP4，边框位于画面外侧";
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public event Action? PauseToggleRequested;

    public event Action? StopRequested;

    public event Action? CancelRequested;

    public void UpdateProgress(RecordingProgress progress)
    {
        TimeText.Text = progress.ElapsedText;
        MetaText.Text = $"{_fps} FPS · {progress.Frames} 帧 · {progress.SizeText}";
    }

    public void SetStatus(string text) => DetailText.Text = text;

    public void SetPaused(bool paused)
    {
        _paused = paused;
        PauseButton.Content = paused ? "继续" : "暂停";
        Dot.Fill = paused ? PausedBrush : RecordingBrush;
        TimeText.Foreground = paused ? PausedBrush : (Brush)FindResource("TextPrimaryBrush");
        DetailText.Text = paused ? "已暂停，暂停时间不计入成片" : "正在录制，边框位于画面外侧";
    }

    public void PlaceNear(PixelRect area, MonitorInfo monitor)
    {
        UpdateLayout();
        var work = monitor.WorkArea;
        var (width, height) = WindowPlacement.GetPixelSize(this);
        if (width <= 0 || height <= 0)
        {
            width = (int)(Width * monitor.Scale);
            height = (int)(60 * monitor.Scale);
        }

        int x = area.Left;
        int y = area.Bottom + 12;
        if (y + height > work.Bottom - 4) y = area.Top - height - 12;
        if (y < work.Top + 4) y = Math.Min(Math.Max(work.Top + 4, work.Bottom - height - 4), area.Bottom - height - 12);

        x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
        y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
        WindowPlacement.MoveTo(this, x, y);
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => PauseToggleRequested?.Invoke();

    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
