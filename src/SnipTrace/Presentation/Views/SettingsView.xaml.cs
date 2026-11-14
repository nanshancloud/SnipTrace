using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Infrastructure.Storage;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings;
    private readonly PathService _paths;

    public SettingsView(AppSettings settings, PathService paths)
    {
        InitializeComponent();
        _settings = settings;
        _paths = paths;
        LoadFrom(settings);
    }

    public event Action? SettingsSaved;

    public void LoadFrom(AppSettings settings)
    {
        ScreenshotDirBox.Text = settings.ScreenshotDirectory;
        VideoDirBox.Text = settings.VideoDirectory;
        CaptureHotkeyBox.Text = settings.CaptureHotkey;
        GifHotkeyBox.Text = settings.GifHotkey;
        VideoHotkeyBox.Text = settings.VideoHotkey;
        FullScreenHotkeyBox.Text = settings.FullScreenHotkey;
        AllScreensHotkeyBox.Text = settings.AllScreensHotkey;
        CursorInCaptureCheck.IsChecked = settings.IncludeCursorInCapture;
        CopyAfterCaptureCheck.IsChecked = settings.CopyAfterCapture;
        StartupCheck.IsChecked = settings.RunAtStartup;
        StatusText.Text = string.Empty;
    }

    private void BrowseScreenshot_Click(object sender, RoutedEventArgs e) => Browse(ScreenshotDirBox);

    private void BrowseVideo_Click(object sender, RoutedEventArgs e) => Browse(VideoDirBox);

    private void Browse(TextBox box)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择输出目录",
            InitialDirectory = box.Text
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            box.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? error = null;
        var hotkeys = new (string Name, TextBox Box)[]
        {
            ("区域截图", CaptureHotkeyBox),
            ("GIF 录制", GifHotkeyBox),
            ("屏幕录制", VideoHotkeyBox),
            ("全屏截图", FullScreenHotkeyBox),
            ("所有显示器截图", AllScreensHotkeyBox)
        };

        foreach (var (name, box) in hotkeys)
        {
            if (!HotkeyGesture.TryParse(box.Text.Trim(), out var gesture))
            {
                error = $"{name} 的快捷键格式不正确，例如 Ctrl+Shift+G。";
                break;
            }

            box.Text = gesture.ToDisplayString();
        }

        if (error is null)
        {
            var updated = new AppSettings
            {
                ScreenshotDirectory = ScreenshotDirBox.Text.Trim(),
                VideoDirectory = VideoDirBox.Text.Trim(),
                IncludeCursorInCapture = CursorInCaptureCheck.IsChecked == true,
                CopyAfterCapture = CopyAfterCaptureCheck.IsChecked == true,
                RunAtStartup = StartupCheck.IsChecked == true,
                CaptureHotkey = CaptureHotkeyBox.Text.Trim(),
                GifHotkey = GifHotkeyBox.Text.Trim(),
                VideoHotkey = VideoHotkeyBox.Text.Trim(),
                FullScreenHotkey = FullScreenHotkeyBox.Text.Trim(),
                AllScreensHotkey = AllScreensHotkeyBox.Text.Trim(),
                Gif = _settings.Gif,
                Video = _settings.Video,
                MainWindow = _settings.MainWindow
            };

            updated.Normalize(_paths.DefaultScreenshotDirectory, _paths.DefaultVideoDirectory);
            string? conflict = updated.FindHotkeyConflict();
            if (conflict is not null)
            {
                error = "快捷键冲突：" + conflict;
            }
            else
            {
                CopySettings(updated, _settings);
                StartupService.Apply(_settings.RunAtStartup);
                SettingsSaved?.Invoke();
                LoadFrom(_settings);
                StatusText.Text = "已保存";
                return;
            }
        }

        StatusText.Text = error;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.CreateDefault();
        defaults.Normalize(_paths.DefaultScreenshotDirectory, _paths.DefaultVideoDirectory);
        CopySettings(defaults, _settings);
        StartupService.Apply(_settings.RunAtStartup);
        SettingsSaved?.Invoke();
        LoadFrom(_settings);
        StatusText.Text = "已恢复默认值";
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.ScreenshotDirectory = source.ScreenshotDirectory;
        target.VideoDirectory = source.VideoDirectory;
        target.IncludeCursorInCapture = source.IncludeCursorInCapture;
        target.CopyAfterCapture = source.CopyAfterCapture;
        target.RunAtStartup = source.RunAtStartup;
        target.CaptureHotkey = source.CaptureHotkey;
        target.GifHotkey = source.GifHotkey;
        target.VideoHotkey = source.VideoHotkey;
        target.FullScreenHotkey = source.FullScreenHotkey;
        target.AllScreensHotkey = source.AllScreensHotkey;
        target.Gif = source.Gif;
        target.Video = source.Video;
    }
}
