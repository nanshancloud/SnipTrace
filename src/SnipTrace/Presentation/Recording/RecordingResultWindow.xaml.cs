using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Encoding;
using SnipTrace.App.Presentation.Services;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Recording;

/// <summary>录制完成预览窗口（参考 QQ 制作MP4）：标题栏 + 播放器 + 操作按钮。</summary>
public partial class RecordingResultWindow : Window
{
    private static readonly Brush LoopOnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F));
    private static readonly Brush LoopOffBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x97, 0xA8));

    private readonly string _path;
    private readonly List<BitmapSource> _frames = new();
    private readonly List<TimeSpan> _delays = new();
    private readonly DispatcherTimer _uiTimer;
    private DispatcherTimer? _gifTimer;
    private readonly bool _isVideo;

    private int _frameIndex;
    private TimeSpan _gifElapsed;
    private TimeSpan _totalDuration;
    private bool _loop = true;
    private bool _isPlaying;
    private bool _scrubbing;
    private bool _syncingSlider;
    private bool _converting;
    private CancellationTokenSource? _conversionCancellation;

    public RecordingResultWindow(BitmapSource? preview, string path, RecordingKind kind, TimeSpan duration, long bytes)
    {
        InitializeComponent();
        _path = path;
        _isVideo = kind != RecordingKind.Gif;
        _totalDuration = duration;
        TitleText.Text = _isVideo ? L.T("制作MP4") : L.T("制作GIF");
        ConvertButton.Visibility = _isVideo ? Visibility.Visible : Visibility.Collapsed;

        string sizeText = bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0} KB";
        InfoText.Text = $"{Path.GetFileName(path)} · {sizeText} · {path}";

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _uiTimer.Tick += (_, _) => SyncPlaybackUi();
        _uiTimer.Start();

        if (_isVideo) StartVideoPlayback();
        else StartGifPlayback(preview);

        // 录制控制条刚关闭，主动抢一次前台，避免预览窗口被其它窗口遮住。
        Loaded += (_, _) =>
        {
            Activate();
            Topmost = true;
            Topmost = false;
        };
        Closed += (_, _) =>
        {
            _uiTimer.Stop();
            _gifTimer?.Stop();
        };
    }

    private void StartVideoPlayback()
    {
        VideoPreview.Visibility = Visibility.Visible;
        VideoPreview.Source = new Uri(_path);
        VideoPreview.Volume = 1;
        VideoPreview.Play();
        _isPlaying = true;
        SetPlayPauseGlyph("\uE769");
    }

    private void StartGifPlayback(BitmapSource? fallback)
    {
        try
        {
            var decoder = new GifBitmapDecoder(new Uri(_path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                PreviewImage.Source = fallback;
                return;
            }

            foreach (BitmapFrame frame in decoder.Frames)
            {
                _frames.Add(frame);
                _delays.Add(ReadFrameDelay(frame));
                _totalDuration += _delays[^1];
            }

            // 单帧 GIF（像素完全一致时编码器会合并）也要显示首帧。
            PreviewImage.Source = _frames[0];
            if (_frames.Count > 1)
            {
                _isPlaying = true;
                SetPlayPauseGlyph("\uE769");
                _gifTimer = new DispatcherTimer { Interval = _delays[0] };
                _gifTimer.Tick += (_, _) => AdvanceGifFrame();
                _gifTimer.Start();
            }
        }
        catch (Exception)
        {
            PreviewImage.Source = fallback;
        }
    }

    /// <summary>每 200ms 把播放进度同步到进度条与时间文本。</summary>
    private void SyncPlaybackUi()
    {
        if (_scrubbing) return;

        TimeSpan current;
        if (_isVideo)
        {
            current = VideoPreview.Position;
            if (!_syncingSlider)
            {
                _syncingSlider = true;
                PlayerSlider.Maximum = Math.Max(0.1, _totalDuration.TotalSeconds);
                PlayerSlider.Value = current.TotalSeconds;
                _syncingSlider = false;
            }
        }
        else
        {
            current = _gifElapsed;
            if (!_syncingSlider && _frames.Count > 1)
            {
                _syncingSlider = true;
                PlayerSlider.Maximum = Math.Max(0.1, _totalDuration.TotalMilliseconds);
                PlayerSlider.Value = current.TotalMilliseconds;
                _syncingSlider = false;
            }
        }

        TimeText.Text = $"{Format(current)} / {Format(_totalDuration)}";
    }

    private static string Format(TimeSpan time) => $"{time.Minutes:00}:{time.Seconds:00}";

    private static TimeSpan ReadFrameDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata)
            {
                object? delay = metadata.GetQuery("/grctlext/Delay");
                if (delay is ushort centiseconds && centiseconds > 0)
                {
                    return TimeSpan.FromMilliseconds(centiseconds * 10);
                }
            }
        }
        catch (Exception)
        {
        }

        return TimeSpan.FromMilliseconds(100);
    }

    /// <summary>GIF 逐帧推进：专用计时器按帧延迟切换，暂停时停止。</summary>
    private void AdvanceGifFrame()
    {
        if (_frames.Count <= 1 || !_isPlaying) return;

        _gifElapsed += _delays[_frameIndex];
        if (_gifElapsed >= _totalDuration)
        {
            if (_loop)
            {
                _gifElapsed = TimeSpan.Zero;
                _frameIndex = 0;
            }
            else
            {
                _gifElapsed = _totalDuration;
                _isPlaying = false;
                SetPlayPauseGlyph("\uE768");
                _gifTimer?.Stop();
                return;
            }
        }
        else
        {
            _frameIndex = (_frameIndex + 1) % _frames.Count;
        }

        PreviewImage.Source = _frames[_frameIndex];
        if (_gifTimer is not null) _gifTimer.Interval = _delays[_frameIndex];
    }

    private void SetPlayPauseGlyph(string glyph) => PlayPauseGlyph.Text = glyph;

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isVideo)
        {
            if (_isPlaying)
            {
                VideoPreview.Pause();
                _isPlaying = false;
                SetPlayPauseGlyph("\uE768");
            }
            else
            {
                VideoPreview.Play();
                _isPlaying = true;
                SetPlayPauseGlyph("\uE769");
            }

            return;
        }

        if (_frames.Count <= 1) return;
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            _gifTimer?.Start();
            SetPlayPauseGlyph("\uE769");
        }
        else
        {
            _gifTimer?.Stop();
            SetPlayPauseGlyph("\uE768");
        }
    }

    private void Loop_Click(object sender, RoutedEventArgs e)
    {
        _loop = !_loop;
        LoopGlyph.Foreground = _loop ? LoopOnBrush : LoopOffBrush;
        LoopButton.ToolTip = _loop ? L.T("循环播放：开") : L.T("循环播放：关");
    }

    private void PlayerSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSlider || _scrubbing) return;

        if (_isVideo)
        {
            if (VideoPreview is null) return;
            _scrubbing = true;
            try
            {
                VideoPreview.Position = TimeSpan.FromSeconds(PlayerSlider.Value);
            }
            finally
            {
                _scrubbing = false;
            }

            return;
        }

        // GIF：按时间定位到最近一帧。
        if (_frames.Count == 0) return;
        double target = PlayerSlider.Value;
        TimeSpan elapsed = TimeSpan.Zero;
        for (int i = 0; i < _frames.Count; i++)
        {
            if (elapsed + _delays[i] > TimeSpan.FromMilliseconds(target) && i > 0) break;
            elapsed += _delays[i];
            _frameIndex = i;
        }

        _gifElapsed = elapsed;
        PreviewImage.Source = _frames[_frameIndex];
    }

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.NaturalDuration.HasTimeSpan)
        {
            _totalDuration = VideoPreview.NaturalDuration.TimeSpan;
        }
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_loop)
        {
            VideoPreview.Position = TimeSpan.Zero;
            VideoPreview.Play();
        }
        else
        {
            _isPlaying = false;
            SetPlayPauseGlyph("\uE768");
        }
    }

    private void VideoPreview_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        VideoPreview.Visibility = Visibility.Collapsed;
        PlayPauseButton.Visibility = Visibility.Collapsed;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn(L.T("无法打开文件"), ex.Message);
        }
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn(L.T("无法打开文件夹"), ex.Message);
        }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string extension = Path.GetExtension(_path);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Path.GetFileName(_path),
                DefaultExt = extension,
                InitialDirectory = Path.GetDirectoryName(_path),
                Filter = extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                    ? L.T("GIF 动图 (*.gif)|*.gif")
                    : L.T("MP4 视频 (*.mp4)|*.mp4")
            };

            if (dialog.ShowDialog(this) != true) return;
            if (string.Equals(dialog.FileName, _path, StringComparison.OrdinalIgnoreCase)) return;

            File.Copy(_path, dialog.FileName, true);
            AppServices.Current?.Toasts.Info(L.T("已另存为"), Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn(L.T("另存为失败"), ex.Message);
        }
    }

    /// <summary>MP4 → GIF：按设置的 GIF 参数转换，转换期间禁用操作按钮。</summary>
    private async void ConvertToGif_Click(object sender, RoutedEventArgs e)
    {
        if (!_isVideo || _converting) return;

        var services = AppServices.Current;
        if (services is null) return;

        try
        {
            string directory = Path.GetDirectoryName(_path) ?? services.ScreenshotDirectory;
            string baseName = Path.GetFileNameWithoutExtension(_path);
            string target = Path.Combine(directory, baseName + ".gif");
            if (string.Equals(target, _path, StringComparison.OrdinalIgnoreCase)) return;

            _converting = true;
            _conversionCancellation = new CancellationTokenSource();
            SetActionsEnabled(false);
            VideoPreview.Pause();
            _isPlaying = false;
            SetPlayPauseGlyph("\uE768");

            InfoText.Text = L.T("正在转换为 GIF… 0%");
            int lastProgress = 0;
            await Mp4ToGifConverter.ConvertAsync(
                _path,
                target,
                services.Settings.Gif,
                percent =>
                {
                    if (percent == lastProgress) return;
                    lastProgress = percent;
                    Dispatcher.BeginInvoke(() => InfoText.Text = $"正在转换为 GIF… {percent}%");
                },
                _conversionCancellation.Token);

            try
            {
                using var thumbnail = ImageUtil.ToBitmap(new BitmapImage(new Uri(target)));
                services.Recent.Add(target, RecentItemKind.Gif, 0, 0, 0, thumbnail);
            }
            catch (Exception)
            {
            }

            InfoText.Text = L.T("已转换为 GIF：") + Path.GetFileName(target);
            services.Toasts.Info(L.T("已转换为 GIF"), target);
        }
        catch (OperationCanceledException)
        {
            InfoText.Text = L.T("已取消转换");
        }
        catch (Exception ex)
        {
            InfoText.Text = L.T("转换失败：") + ex.Message;
            services.Toasts.Warn(L.T("MP4 转 GIF 失败"), ex.Message);
        }
        finally
        {
            _converting = false;
            _conversionCancellation?.Dispose();
            _conversionCancellation = null;
            SetActionsEnabled(true);
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        ConvertButton.IsEnabled = enabled;
        PlayPauseButton.IsEnabled = enabled;
        PlayerSlider.IsEnabled = enabled;
        foreach (var child in ((WrapPanel)ConvertButton.Parent).Children)
        {
            if (child is Button button) button.IsEnabled = enabled;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var files = new System.Collections.Specialized.StringCollection { _path };
            Clipboard.SetFileDropList(files);
            AppServices.Current?.Toasts.Info(L.T("已复制文件"), Path.GetFileName(_path));
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn(L.T("无法复制文件"), ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
