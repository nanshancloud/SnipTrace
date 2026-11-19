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
    private Button? _dragSource;
    private Border? _dragMarker;
    private Point _dragStartPoint;
    private bool _isToolbarDragging;

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
            CaptureHotkeyInput.Value = settings.CaptureHotkey;
            GifHotkeyInput.Value = settings.GifHotkey;
            VideoHotkeyInput.Value = settings.VideoHotkey;
            FullScreenHotkeyInput.Value = settings.FullScreenHotkey;
            AllScreensHotkeyInput.Value = settings.AllScreensHotkey;
            SetText(TextFontBox, settings.TextFontFamily);
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
            RefreshTextFontSample();
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
                Content = SelectionToolbarIcons.For(item.Id, item.Variant),
                Style = (Style)FindResource("ToolIconButton"),
                Tag = item,
                Opacity = item.Visible ? 1.0 : 0.35,
                ToolTip = $"{name}（{(item.Visible ? "已显示" : "已隐藏")}）\n点击切换显示 / 隐藏 · 按住拖动调整顺序"
            };

            if (SelectionToolbarIcons.IsAutoWidth(item.Id))
            {
                button.Width = double.NaN;
                button.MinWidth = 0;
                button.Padding = new Thickness(8, 0, 8, 0);
            }

            ToolbarPreviewPanel.Children.Add(button);
        }
    }

    /// <summary>点预览条上的按钮：切换这个按钮在真实工具条里是否出现。</summary>
    private void ToggleToolbarVisible(string id)
    {
        if (_loading) return;

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

    /// <summary>按下预览条上的按钮：记下起点，后续移动/抬手都交给预览条自己处理。</summary>
    private void ToolbarPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_loading) return;

        Button? button = FindPreviewButton(e.OriginalSource as DependencyObject);
        if (button?.Tag is not ToolbarItemSetting item) return;

        _draggingItem = item;
        _dragSource = button;
        _dragStartPoint = e.GetPosition(ToolbarPreviewPanel);
        _isToolbarDragging = false;

        // 在面板上捕获鼠标：拖出按钮范围也照样收到 MouseMove / 抬手。
        ToolbarPreviewPanel.CaptureMouse();
        e.Handled = true;
    }

    private void ToolbarPanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingItem is null || _dragSource is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point position = e.GetPosition(ToolbarPreviewPanel);

        if (!_isToolbarDragging)
        {
            if (Math.Abs(position.X - _dragStartPoint.X) < 4 && Math.Abs(position.Y - _dragStartPoint.Y) < 4) return;

            _isToolbarDragging = true;
            _dragSource.Opacity = 0.4;
            Panel.SetZIndex(_dragSource, 1);
            _dragMarker = new Border
            {
                Width = 2,
                Height = 18,
                Margin = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            _dragMarker.SetResourceReference(BackgroundProperty, "AccentBrush");
        }

        _dragSource.RenderTransform = new TranslateTransform(position.X - _dragStartPoint.X, 0);
        UpdateDragMarker(position.X);
    }

    private void ToolbarPanel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingItem is null || _dragSource is null) return;

        var item = _draggingItem;
        bool dragged = _isToolbarDragging;
        double x = e.GetPosition(ToolbarPreviewPanel).X;

        if (ToolbarPreviewPanel.IsMouseCaptured) ToolbarPreviewPanel.ReleaseMouseCapture();
        ResetToolbarDrag();

        if (dragged)
        {
            CommitToolbarDrop(item.Id, x);
        }
        else
        {
            ToggleToolbarVisible(item.Id);
        }
    }

    /// <summary>拖动中的插入指示线：落在哪个按钮中线左侧就插到它前面。</summary>
    private void UpdateDragMarker(double x)
    {
        if (_dragMarker is null) return;

        Button? before = FindButtonAfter(x);

        int index = before is null ? ToolbarPreviewPanel.Children.Count : ToolbarPreviewPanel.Children.IndexOf(before);
        int current = ToolbarPreviewPanel.Children.IndexOf(_dragMarker);
        if (current >= 0)
        {
            if (current == index || index == current + 1) return;
            ToolbarPreviewPanel.Children.RemoveAt(current);
            if (index > current) index--;
        }

        ToolbarPreviewPanel.Children.Insert(Math.Clamp(index, 0, ToolbarPreviewPanel.Children.Count), _dragMarker);
    }

    /// <summary>落点右侧第一个按钮（跳过被拖动的自己与指示线）。</summary>
    private Button? FindButtonAfter(double x)
    {
        foreach (var child in ToolbarPreviewPanel.Children)
        {
            if (child is not Button button || ReferenceEquals(button, _dragSource)) continue;
            if (button.Tag is not ToolbarItemSetting) continue;

            double center = button.TranslatePoint(new Point(button.ActualWidth / 2, 0), ToolbarPreviewPanel).X;
            if (x < center) return button;
        }

        return null;
    }

    /// <summary>按落点算目标序号并写回设置（预览条会按新顺序重画）。</summary>
    private void CommitToolbarDrop(string id, double x)
    {
        var items = _settings.SelectionToolbar;
        if (items is null || items.Count == 0) return;

        int from = items.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (from < 0) return;

        Button? before = FindButtonAfter(x);
        int targetIndex = items.Count;
        if (before?.Tag is ToolbarItemSetting target)
        {
            int index = items.FindIndex(item => string.Equals(item.Id, target.Id, StringComparison.Ordinal));
            if (index >= 0) targetIndex = index;
        }

        int to = Math.Clamp(targetIndex > from ? targetIndex - 1 : targetIndex, 0, items.Count - 1);
        MoveToolbarItemTo(from, to);
    }

    private void ResetToolbarDrag()
    {
        if (_dragSource is not null)
        {
            _dragSource.RenderTransform = Transform.Identity;
            Panel.SetZIndex(_dragSource, 0);
        }

        if (_dragMarker is not null) ToolbarPreviewPanel.Children.Remove(_dragMarker);

        _dragMarker = null;
        _dragSource = null;
        _draggingItem = null;
        _isToolbarDragging = false;
    }

    /// <summary>从事件源往上找预览按钮（图标是 Path/TextBlock，点到的往往不是 Button 本身）。</summary>
    private static Button? FindPreviewButton(DependencyObject? source)
    {
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button button && button.Tag is ToolbarItemSetting) return button;
            if (node is WrapPanel) return null;
        }

        return null;
    }

    private static void SetText(TextBox box, string value)
    {
        if (!string.Equals(box.Text, value, StringComparison.Ordinal)) box.Text = value;
    }

    private void HookEvents()
    {
        foreach (var box in new[] { ScreenshotDirBox, VideoDirBox, TextFontBox, HighlightColorBox, HighlightSizeBox, HighlightOpacityBox })
        {
            box.TextChanged += (_, _) =>
            {
                if (_loading) return;
                if (box == HighlightColorBox) RefreshHighlightSwatch();
                Queue();
            };
        }

        // 快捷键控件自己负责按键录入，这里只接「值变了」。
        foreach (var input in new[] { CaptureHotkeyInput, GifHotkeyInput, VideoHotkeyInput, FullScreenHotkeyInput, AllScreensHotkeyInput })
        {
            input.ValueChanged += (_, _) => Queue();
        }

        // 预览条的拖动换位自己做（不用 OLE 拖放，避免落点判定丢失）。
        ToolbarPreviewPanel.PreviewMouseLeftButtonDown += ToolbarPanel_MouseDown;
        ToolbarPreviewPanel.MouseMove += ToolbarPanel_MouseMove;
        ToolbarPreviewPanel.PreviewMouseLeftButtonUp += ToolbarPanel_MouseUp;

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

        var hotkeys = new (string Name, string Value)[]
        {
            ("区域截图", CaptureHotkeyInput.Value),
            ("GIF 录制", GifHotkeyInput.Value),
            ("屏幕录制", VideoHotkeyInput.Value),
            ("全屏截图", FullScreenHotkeyInput.Value),
            ("所有显示器截图", AllScreensHotkeyInput.Value)
        };

        foreach (var (name, value) in hotkeys)
        {
            if (!HotkeyGesture.TryParse(value.Trim(), out _))
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
            TextFontFamily = TextFontBox.Text.Trim(),
            Highlight = new HighlightOptions
            {
                Enabled = HighlightCheck.IsChecked == true,
                Color = HighlightColorBox.Text.Trim(),
                Diameter = highlightDiameter,
                OpacityPercent = highlightOpacity
            },
            CaptureHotkey = CaptureHotkeyInput.Value.Trim(),
            GifHotkey = GifHotkeyInput.Value.Trim(),
            VideoHotkey = VideoHotkeyInput.Value.Trim(),
            FullScreenHotkey = FullScreenHotkeyInput.Value.Trim(),
            AllScreensHotkey = AllScreensHotkeyInput.Value.Trim(),
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

    /// <summary>字体预览：选中的字体立刻套到示例文字上。</summary>
    private void RefreshTextFontSample()
    {
        string family = TextFontBox.Text.Trim();
        if (string.IsNullOrEmpty(family)) family = AppSettings.DefaultTextFontFamily;
        try
        {
            TextFontSample.FontFamily = new FontFamily(family);
        }
        catch (Exception)
        {
            TextFontSample.FontFamily = (FontFamily)FindResource("AppFont");
        }
    }

    /// <summary>一键把五个快捷键恢复成默认值并立即生效。</summary>
    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        CaptureHotkeyInput.Value = AppSettings.DefaultCaptureHotkey;
        GifHotkeyInput.Value = AppSettings.DefaultGifHotkey;
        VideoHotkeyInput.Value = AppSettings.DefaultVideoHotkey;
        FullScreenHotkeyInput.Value = AppSettings.DefaultFullScreenHotkey;
        AllScreensHotkeyInput.Value = AppSettings.DefaultAllScreensHotkey;

        Apply();
        StatusText.Text = "快捷键已恢复默认";
    }

    /// <summary>打开系统字体对话框挑文本标注字体。</summary>
    private void PickTextFont_Click(object sender, RoutedEventArgs e)
    {
        string current = TextFontBox.Text.Trim();
        if (string.IsNullOrEmpty(current)) current = AppSettings.DefaultTextFontFamily;

        try
        {
            using var dialog = new System.Windows.Forms.FontDialog
            {
                ShowEffects = false,
                FontMustExist = true,
                Font = new System.Drawing.Font(current, 12f)
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            TextFontBox.Text = dialog.Font.FontFamily.Name;
            RefreshTextFontSample();
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开字体对话框失败：" + ex.Message;
        }
    }

    /// <summary>高亮颜色取色：系统取色对话框，选完写回 #RRGGBB 并即时生效。</summary>
    private void PickHighlightColor_Click(object sender, RoutedEventArgs e)
    {
        if (!ColorPicker.TryPick(ParseArgb(HighlightColorBox.Text), out uint picked)) return;

        HighlightColorBox.Text = $"#{(picked & 0xFFFFFF):X6}";
        RefreshHighlightSwatch();
        StatusText.Text = "高亮颜色已更新";
    }

    private static uint ParseArgb(string? text)
    {
        try
        {
            Color color = (Color)ColorConverter.ConvertFromString((text ?? string.Empty).Trim());
            return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        }
        catch (Exception)
        {
            return 0xFFFFCC00;
        }
    }

    /// <summary>统一快捷键模式下 GIF / 录屏键不会注册，直接把这两行藏起来，免得看起来像能用。</summary>
    private void RefreshHotkeyModeUi()
    {
        bool unified = HotkeyUnifiedRadio.IsChecked == true;
        Visibility visibility = unified ? Visibility.Collapsed : Visibility.Visible;

        GifHotkeyInput.IsEnabled = !unified;
        VideoHotkeyInput.IsEnabled = !unified;
        GifHotkeyInput.Visibility = visibility;
        GifHotkeyInputLabel.Visibility = visibility;
        VideoHotkeyInput.Visibility = visibility;
        VideoHotkeyInputLabel.Visibility = visibility;
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
        target.TextFontFamily = source.TextFontFamily;
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
