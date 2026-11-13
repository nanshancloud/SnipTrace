using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnipTrace.App.Presentation.Services;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Recording;

public partial class RecordingResultWindow : Window
{
    private readonly string _path;

    public RecordingResultWindow(BitmapSource? preview, string path, RecordingKind kind, TimeSpan duration, long bytes)
    {
        InitializeComponent();
        _path = path;
        PreviewImage.Source = preview;
        TitleText.Text = kind == RecordingKind.Gif ? "GIF 录制完成" : "屏幕录制完成";

        string sizeText = bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0} KB";
        InfoText.Text = $"{Path.GetFileName(path)}\n时长 {duration.Minutes:00}:{duration.Seconds:00} · 体积 {sizeText}\n{path}";
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
