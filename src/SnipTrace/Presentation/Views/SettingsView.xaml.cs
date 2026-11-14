using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Infrastructure.Storage;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

/// <summary>
/// 设置页：所有改动即时生效（文本类输入有 350 ms 防抖），不再需要保存按钮。
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings;
    private readonly PathService _paths;
    private readonly DispatcherTimer _debounce;

    /// <summary>正在把设置回填到控件，忽略控件事件。</summary>
    private bool _loading;

    public SettingsView(AppSettings settings, PathService paths)
    {
        InitializeComponent();
        _settings = settings;
        _paths = paths;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Apply();
        };

        LoadFrom(settings);
        HookEvents();
    }

    public event Action? SettingsSaved;

    public void LoadFrom(AppSettings settings)
    {
        _loading = true;
        try
        {
            SetText(ScreenshotDirBox, settings.ScreenshotDirectory);
            SetText(VideoDirBox, settings.VideoDirectory);
            SetText(CaptureHotkeyBox, settings.CaptureHotkey);
            SetText(GifHotkeyBox, settings.GifHotkey);
            SetText(VideoHotkeyBox, settings.VideoHotkey);
            SetText(FullScreenHotkeyBox, settings.FullScreenHotkey);
            SetText(AllScreensHotkeyBox, settings.AllScreensHotkey);
            SetText(HighlightColorBox, settings.Highlight.Color);
            SetText(HighlightSizeBox, settings.Highlight.Diameter.ToString(CultureInfo.InvariantCulture));
            SetText(HighlightOpacityBox, settings.Highlight.OpacityPercent.ToString(CultureInfo.InvariantCulture));

            CursorInCaptureCheck.IsChecked = settings.IncludeCursorInCapture;
            CopyAfterCaptureCheck.IsChecked = settings.CopyAfterCapture;
            AutoSaveCheck.IsChecked = settings.AutoSaveAfterCapture;
            HotkeyWarnCheck.IsChecked = settings.WarnHotkeyFailure;
            StartupCheck.IsChecked = settings.RunAtStartup;
            HighlightCheck.IsChecked = settings.Highlight.Enabled;

            if (settings.HotkeyMode == HotkeyModes.Unified) HotkeyUnifiedRadio.IsChecked = true;
            else HotkeyIndividualRadio.IsChecked = true;

            if (settings.Theme == ThemeModes.Light) ThemeLightRadio.IsChecked = true;
            else ThemeDarkRadio.IsChecked = true;

            RefreshHighlightSwatch();
            RefreshHotkeyModeUi();
        }
        finally
        {
            _loading = false;
        }
    }

    private static void SetText(TextBox box, string value)
    {
        if (!string.Equals(box.Text, value, StringComparison.Ordinal)) box.Text = value;
    }

    private void HookEvents()
    {
        foreach (var box in new[] { ScreenshotDirBox, VideoDirBox, CaptureHotkeyBox, GifHotkeyBox, VideoHotkeyBox, FullScreenHotkeyBox, AllScreensHotkeyBox, HighlightColorBox, HighlightSizeBox, HighlightOpacityBox })
        {
            box.TextChanged += (_, _) =>
            {
                if (_loading) return;
                if (box == HighlightColorBox) RefreshHighlightSwatch();
                Queue();
            };
        }

        foreach (var check in new[] { CursorInCaptureCheck, CopyAfterCaptureCheck, AutoSaveCheck, HotkeyWarnCheck, StartupCheck, HighlightCheck })
        {
            check.Checked += OnToggle;
            check.Unchecked += OnToggle;
        }

        HotkeyIndividualRadio.Checked += OnToggle;
        HotkeyUnifiedRadio.Checked += OnToggle;
        ThemeDarkRadio.Checked += OnToggle;
        ThemeLightRadio.Checked += OnToggle;
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        RefreshHotkeyModeUi();
        Queue();
    }

    private void Queue()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>把控件状态写回设置并立即生效；非法输入只提示、不落盘。</summary>
    private void Apply()
    {
        if (_loading) return;

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
            if (!HotkeyGesture.TryParse(box.Text.Trim(), out _))
            {
                StatusText.Text = $"{name} 快捷键格式不正确，例如 Ctrl+Shift+G";
                return;
            }
        }

        if (!int.TryParse(HighlightSizeBox.Text.Trim(), out int highlightDiameter)) highlightDiameter = 48;
        if (!int.TryParse(HighlightOpacityBox.Text.Trim(), out int highlightOpacity)) highlightOpacity = 35;

        var updated = new AppSettings
        {
            ScreenshotDirectory = ScreenshotDirBox.Text.Trim(),
            VideoDirectory = VideoDirBox.Text.Trim(),
            IncludeCursorInCapture = CursorInCaptureCheck.IsChecked == true,
            CopyAfterCapture = CopyAfterCaptureCheck.IsChecked == true,
            AutoSaveAfterCapture = AutoSaveCheck.IsChecked == true,
            WarnHotkeyFailure = HotkeyWarnCheck.IsChecked == true,
            RunAtStartup = StartupCheck.IsChecked == true,
            HotkeyMode = HotkeyUnifiedRadio.IsChecked == true ? HotkeyModes.Unified : HotkeyModes.Individual,
            Theme = ThemeLightRadio.IsChecked == true ? ThemeModes.Light : ThemeModes.Dark,
            Highlight = new HighlightOptions
            {
                Enabled = HighlightCheck.IsChecked == true,
                Color = HighlightColorBox.Text.Trim(),
                Diameter = highlightDiameter,
                OpacityPercent = highlightOpacity
            },
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
            StatusText.Text = "快捷键冲突：" + conflict;
            return;
        }

        bool startupChanged = updated.RunAtStartup != _settings.RunAtStartup;
        CopySettings(updated, _settings);
        if (startupChanged)
        {
            StartupService.Apply(_settings.RunAtStartup);
            _loading = true;
            StartupCheck.IsChecked = _settings.RunAtStartup;
            _loading = false;
        }

        SettingsSaved?.Invoke();
        StatusText.Text = "已生效";
    }

    private void RefreshHighlightSwatch()
    {
        try
        {
            HighlightSwatch.Color = (Color)ColorConverter.ConvertFromString(HighlightColorBox.Text.Trim());
        }
        catch (FormatException)
        {
        }
    }

    private void RefreshHotkeyModeUi()
    {
        bool unified = HotkeyUnifiedRadio.IsChecked == true;
        GifHotkeyBox.IsEnabled = !unified;
        VideoHotkeyBox.IsEnabled = !unified;
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
        target.AutoSaveAfterCapture = source.AutoSaveAfterCapture;
        target.WarnHotkeyFailure = source.WarnHotkeyFailure;
        target.RunAtStartup = source.RunAtStartup;
        target.HotkeyMode = source.HotkeyMode;
        target.Theme = source.Theme;
        target.Highlight = source.Highlight;
        target.CaptureHotkey = source.CaptureHotkey;
        target.GifHotkey = source.GifHotkey;
        target.VideoHotkey = source.VideoHotkey;
        target.FullScreenHotkey = source.FullScreenHotkey;
        target.AllScreensHotkey = source.AllScreensHotkey;
        target.Gif = source.Gif;
        target.Video = source.Video;
        target.MainWindow = source.MainWindow;
    }
}
