using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Infrastructure.Storage;
using SnipTrace.App.Presentation.Selection;
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

    /// <summary>预览条上按下但还没开始拖动的按钮，以及按下时的屏幕位置。</summary>
    private ToolbarItemSetting? _draggingItem;
    private Point _dragStartPoint;

    /// <summary>拖动结束后紧跟的那次 Click 不应该切换显示状态。</summary>
    private bool _suppressToolbarClick;

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
            CheckUpdateCheck.IsChecked = settings.CheckUpdatesOnStartup;
            HighlightCheck.IsChecked = settings.Highlight.Enabled;

            // 换过安装位置（或旧版本写的是不带 --startup 的命令）时，顺手把启动项修到当前路径。
            if (settings.RunAtStartup && !StartupService.MatchesCurrentExecutable())
            {
                StartupService.Apply(true);
            }

            if (settings.HotkeyMode == HotkeyModes.Unified) HotkeyUnifiedRadio.IsChecked = true;
            else HotkeyIndividualRadio.IsChecked = true;

            if (settings.Theme == ThemeModes.Light) ThemeLightRadio.IsChecked = true;
            else ThemeDarkRadio.IsChecked = true;

            if (settings.TrayIconMode == TrayIconModes.Color) TrayColorRadio.IsChecked = true;
            else if (settings.TrayIconMode == TrayIconModes.Light) TrayLightRadio.IsChecked = true;
            else TrayAutoRadio.IsChecked = true;
            TrayHintCheck.IsChecked = !settings.SuppressTrayHint;

            RefreshHighlightSwatch();
            RefreshHotkeyModeUi();
            BuildToolbarPreview();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// 选区工具条配置：直接画成真机的单行图标工具条（隐藏项变暗），
    /// 点按钮切换显示、按住拖动调整顺序，跨分组自动插分隔线。
    /// </summary>
    private void BuildToolbarPreview()
    {
        if (ToolbarPreviewPanel is null) return;
        ToolbarPreviewPanel.Children.Clear();

        var items = _settings.SelectionToolbar;
        if (items is null || items.Count == 0) items = _settings.SelectionToolbar = SelectionToolbarItems.CreateDefault();

        string? previousGroup = null;
        foreach (var item in items)
        {
            string group = SelectionToolbarCatalog.GroupOf(item.Id);
            if (previousGroup is not null && !string.Equals(previousGroup, group, StringComparison.Ordinal))
            {
                ToolbarPreviewPanel.Children.Add(new Border
                {
                    Width = 1,
                    Height = 18,
                    Margin = new Thickness(4, 4, 5, 4),
                    Background = (Brush)FindResource("BorderBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            previousGroup = group;

            string name = SelectionToolbarCatalog.DisplayName(item.Id);
            var button = new Button
            {
                Content = SelectionToolbarIcons.For(item.Id),
                Style = (Style)FindResource("ToolIconButton"),
                Tag = item,
                AllowDrop = true,
                Opacity = item.Visible ? 1.0 : 0.35,
                ToolTip = $"{name}：{(item.Visible ? "已显示，点击隐藏" : "已隐藏，点击显示")}；按住拖动调整顺序"
            };

            if (SelectionToolbarIcons.IsAutoWidth(item.Id))
            {
                button.Width = double.NaN;
                button.MinWidth = 0;
                button.Padding = new Thickness(8, 0, 8, 0);
            }

            button.Click += (_, _) => ToggleToolbarVisible(item.Id);
            button.PreviewMouseLeftButtonDown += PreviewItem_MouseDown;
            button.MouseMove += PreviewItem_MouseMove;
            button.PreviewMouseLeftButtonUp += (_, _) => _draggingItem = null;
            button.DragEnter += PreviewItem_DragOver;
            button.DragOver += PreviewItem_DragOver;
            button.Drop += PreviewItem_Drop;

            ToolbarPreviewPanel.Children.Add(button);
        }
    }

    /// <summary>点预览条上的按钮：切换这个按钮在真实工具条里是否出现。</summary>
    private void ToggleToolbarVisible(string id)
    {
        if (_loading || _suppressToolbarClick) return;

        var items = _settings.SelectionToolbar;
        int index = items?.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal)) ?? -1;
        if (items is null || index < 0) return;

        items[index].Visible = !items[index].Visible;
        SettingsSaved?.Invoke();
        BuildToolbarPreview();
        StatusText.Text = items[index].Visible
            ? $"{SelectionToolbarCatalog.DisplayName(id)}：已显示"
            : $"{SelectionToolbarCatalog.DisplayName(id)}：已隐藏";
    }

    private void MoveToolbarItem(int index, int delta) => MoveToolbarItemTo(index, index + delta);

    /// <summary>把第 from 个按钮移动到 to（当前配置内索引）：预览条按新顺序重画。</summary>
    internal void MoveToolbarItemTo(int from, int to)
    {
        if (_loading) return;
        var items = _settings.SelectionToolbar;
        if (items is null || items.Count == 0) return;
        if (from < 0 || from >= items.Count) return;

        to = Math.Clamp(to, 0, items.Count - 1);
        if (to == from) return;

        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);

        SettingsSaved?.Invoke();
        BuildToolbarPreview();
        StatusText.Text = "选区工具条已更新";
    }

    private void ResetToolbar_Click(object sender, RoutedEventArgs e)
    {
        _settings.SelectionToolbar = SelectionToolbarItems.CreateDefault();
        SettingsSaved?.Invoke();
        BuildToolbarPreview();
        StatusText.Text = "选区工具条已恢复默认";
    }

    private void PreviewItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_loading) return;
        if (sender is FrameworkElement element && element.Tag is ToolbarItemSetting item)
        {
            _draggingItem = item;
            _dragStartPoint = e.GetPosition(this);
            // 捕获鼠标，快速拖动时也不会在移出预览按钮后丢掉 MouseMove。
            element.CaptureMouse();
        }
    }

    private void PreviewItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingItem is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.X) < 4 && Math.Abs(position.Y - _dragStartPoint.Y) < 4) return;

        var item = _draggingItem;
        _draggingItem = null;
        _suppressToolbarClick = true;
        if (sender is UIElement source)
        {
            if (source.IsMouseCaptured) source.ReleaseMouseCapture();
            DragDrop.DoDragDrop(source, new DataObject(typeof(ToolbarItemSetting), item), DragDropEffects.Move);
        }

        // 拖动结束后的那次 Click 不要当成「切换显示」。
        Dispatcher.BeginInvoke(new Action(() => _suppressToolbarClick = false), DispatcherPriority.Background);
    }

    private void PreviewItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ToolbarItemSetting)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PreviewItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ToolbarItemSetting target) return;
        if (e.Data.GetData(typeof(ToolbarItemSetting)) is not ToolbarItemSetting dragged) return;

        var items = _settings.SelectionToolbar;
        if (items is null) return;

        // 设置写回时列表会被整体替换，按 Id 兜底查找更稳。
        int from = items.FindIndex(item => string.Equals(item.Id, dragged.Id, StringComparison.Ordinal));
        int to = items.FindIndex(item => string.Equals(item.Id, target.Id, StringComparison.Ordinal));
        if (from < 0 || to < 0) return;

        // 落在目标右半边就插到它后面，左半边插到它前面。
        if (e.GetPosition(element).X > element.ActualWidth / 2) to++;
        if (to > from) to--;
        e.Handled = true;
        MoveToolbarItemTo(from, to);
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

        foreach (var box in GetHotkeyBoxes()) box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;

        foreach (var check in new[] { CursorInCaptureCheck, CopyAfterCaptureCheck, AutoSaveCheck, HotkeyWarnCheck, StartupCheck, CheckUpdateCheck, HighlightCheck, TrayHintCheck })
        {
            check.Checked += OnToggle;
            check.Unchecked += OnToggle;
        }

        HotkeyIndividualRadio.Checked += OnToggle;
        HotkeyUnifiedRadio.Checked += OnToggle;
        ThemeDarkRadio.Checked += OnToggle;
        ThemeLightRadio.Checked += OnToggle;
        TrayAutoRadio.Checked += OnToggle;
        TrayColorRadio.Checked += OnToggle;
        TrayLightRadio.Checked += OnToggle;
    }

    private TextBox[] GetHotkeyBoxes() => new[] { CaptureHotkeyBox, GifHotkeyBox, VideoHotkeyBox, FullScreenHotkeyBox, AllScreensHotkeyBox };

    /// <summary>快捷键输入框：点进去直接按键即可录入（不必手打 Ctrl+Shift+G）。</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_loading || sender is not TextBox box) return;

        // Alt 组合键会以 SystemKey 形式到达。
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Tab 留给焦点切换；Esc 放弃这次录入，回到已生效的值。
        if (key == Key.Tab) return;
        if (key == Key.Escape)
        {
            e.Handled = true;
            _loading = true;
            try
            {
                SetText(box, CurrentHotkeyText(box));
            }
            finally
            {
                _loading = false;
            }

            StatusText.Text = "已取消本次录入";
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            e.Handled = true;
            box.Clear();
            StatusText.Text = "已清除，按下组合键重新录入";
            return;
        }

        // 只按修饰键不算一次录入。
        if (key == Key.None || IsModifierKey(key) || key is Key.ImeProcessed or Key.DeadCharProcessed) return;

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!HotkeyGesture.TryGetKeyName(virtualKey, out string keyName)) return;

        var gesture = new HotkeyGesture(ToHotkeyModifiers(Keyboard.Modifiers), virtualKey, keyName);
        e.Handled = true;
        box.Text = gesture.ToDisplayString();
        box.CaretIndex = box.Text.Length;
        StatusText.Text = $"已录入 {box.Text}，稍候即生效";
    }

    /// <summary>输入框对应动作当前已生效的快捷键（Esc 取消录入时用它回填）。</summary>
    private string CurrentHotkeyText(TextBox box)
    {
        if (box == CaptureHotkeyBox) return _settings.CaptureHotkey;
        if (box == GifHotkeyBox) return _settings.GifHotkey;
        if (box == VideoHotkeyBox) return _settings.VideoHotkey;
        if (box == FullScreenHotkeyBox) return _settings.FullScreenHotkey;
        return _settings.AllScreensHotkey;
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifiers)
    {
        var result = HotkeyModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0) result |= HotkeyModifiers.Control;
        if ((modifiers & ModifierKeys.Shift) != 0) result |= HotkeyModifiers.Shift;
        if ((modifiers & ModifierKeys.Alt) != 0) result |= HotkeyModifiers.Alt;
        if ((modifiers & ModifierKeys.Windows) != 0) result |= HotkeyModifiers.Win;
        return result;
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
            CheckUpdatesOnStartup = CheckUpdateCheck.IsChecked == true,
            HotkeyMode = HotkeyUnifiedRadio.IsChecked == true ? HotkeyModes.Unified : HotkeyModes.Individual,
            Theme = ThemeLightRadio.IsChecked == true ? ThemeModes.Light : ThemeModes.Dark,
            TrayIconMode = TrayColorRadio.IsChecked == true
                ? TrayIconModes.Color
                : TrayLightRadio.IsChecked == true ? TrayIconModes.Light : TrayIconModes.Auto,
            SuppressTrayHint = TrayHintCheck.IsChecked != true,
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
            RenumberBadgesOnStyleChange = _settings.RenumberBadgesOnStyleChange,
            SelectionToolbar = SelectionToolbarItems.Clone(_settings.SelectionToolbar),
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
        target.CheckUpdatesOnStartup = source.CheckUpdatesOnStartup;
        target.TrayIconMode = source.TrayIconMode;
        target.SuppressTrayHint = source.SuppressTrayHint;
        target.HotkeyMode = source.HotkeyMode;
        target.Theme = source.Theme;
        target.Highlight = source.Highlight;
        target.CaptureHotkey = source.CaptureHotkey;
        target.GifHotkey = source.GifHotkey;
        target.VideoHotkey = source.VideoHotkey;
        target.FullScreenHotkey = source.FullScreenHotkey;
        target.AllScreensHotkey = source.AllScreensHotkey;
        target.RenumberBadgesOnStyleChange = source.RenumberBadgesOnStyleChange;
        target.SelectionToolbar = SelectionToolbarItems.Clone(source.SelectionToolbar);
        target.Gif = source.Gif;
        target.Video = source.Video;
        target.MainWindow = source.MainWindow;
    }
}
