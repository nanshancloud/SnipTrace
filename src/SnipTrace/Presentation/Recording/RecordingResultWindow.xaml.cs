using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnipTrace.App.Presentation.Services;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Recording;

public partial class RecordingResultWindow : Window
{
    private readonly string _path;
    private readonly List<BitmapSource> _frames = new();
    private readonly List<TimeSpan> _delays = new();
    private DispatcherTimer? _gifTimer;
    private int _frameIndex;

    public RecordingResultWindow(BitmapSource? preview, string path, RecordingKind kind, TimeSpan duration, long bytes)
    {
        InitializeComponent();
        _path = path;
        PreviewImage.Source = preview;
        TitleText.Text = kind == RecordingKind.Gif ? "GIF 录制完成" : "屏幕录制完成";

        string sizeText = bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0} KB";
        InfoText.Text = $"{Path.GetFileName(path)}\n时长 {duration.Minutes:00}:{duration.Seconds:00} · 体积 {sizeText}\n{path}";

        if (kind == RecordingKind.Gif) StartGifPlayback(preview);
        else StartVideoPlayback();

        // 录制控制条刚关闭，主动抢一次前台，避免预览窗口被其它窗口遮住。
        Loaded += (_, _) =>
        {
            Activate();
            Topmost = true;
            Topmost = false;
        };
        Closed += (_, _) => _gifTimer?.Stop();
    }

    /// <summary>GIF 逐帧播放；解码失败时退回静态首帧。</summary>
    private void StartGifPlayback(BitmapSource? fallback)
    {
        try
        {
            var decoder = new GifBitmapDecoder(new Uri(_path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return;

            foreach (BitmapFrame frame in decoder.Frames)
            {
                _frames.Add(frame);
                _delays.Add(ReadFrameDelay(frame));
            }

            // 单帧 GIF（像素完全一致时编码器会合并）也要显示首帧。
            PreviewImage.Source = _frames[0];
            if (_frames.Count == 1) return;

            _gifTimer = new DispatcherTimer { Interval = _delays[0] };
            _gifTimer.Tick += (_, _) => AdvanceFrame();
            _gifTimer.Start();
        }
        catch (Exception)
        {
            PreviewImage.Source = fallback;
        }
    }

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

    private void AdvanceFrame()
    {
        if (_gifTimer is null || _frames.Count <= 1) return;

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        PreviewImage.Source = _frames[_frameIndex];
        _gifTimer.Interval = _delays[_frameIndex];
    }

    private void StartVideoPlayback()
    {
        PreviewImage.Visibility = Visibility.Collapsed;
        VideoPreview.Visibility = Visibility.Visible;
        PlayPauseButton.Visibility = Visibility.Visible;
        VideoPreview.Source = new Uri(_path);
        VideoPreview.Volume = 0;
        VideoPreview.Play();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Visibility != Visibility.Visible) return;

        if (VideoPreview.CanPause)
        {
            VideoPreview.Pause();
            SetPlayPauseGlyph("\uE768");
        }
        else
        {
            VideoPreview.Play();
            SetPlayPauseGlyph("\uE769");
        }
    }

    private void SetPlayPauseGlyph(string glyph) => PlayPauseGlyph.Text = glyph;

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Play();
    }

    private void VideoPreview_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        VideoPreview.Visibility = Visibility.Collapsed;
        PlayPauseButton.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Visible;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn("无法打开文件", ex.Message);
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
            AppServices.Current?.Toasts.Warn("无法打开文件夹", ex.Message);
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
                    ? "GIF 动图 (*.gif)|*.gif"
                    : "MP4 视频 (*.mp4)|*.mp4"
            };

            if (dialog.ShowDialog(this) != true) return;
            if (string.Equals(dialog.FileName, _path, StringComparison.OrdinalIgnoreCase)) return;

            File.Copy(_path, dialog.FileName, true);
            AppServices.Current?.Toasts.Info("已另存为", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn("另存为失败", ex.Message);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var files = new System.Collections.Specialized.StringCollection { _path };
            Clipboard.SetFileDropList(files);
            AppServices.Current?.Toasts.Info("已复制文件", Path.GetFileName(_path));
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn("无法复制文件", ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
