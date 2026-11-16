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
            BuildToolbarList();
            BuildToolbarPreview();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>选区工具条配置：每行一个按钮，勾选控制显示、↑ ↓ 调顺序。</summary>
    private void BuildToolbarList()
    {
        if (ToolbarListPanel is null) return;
        ToolbarListPanel.Children.Clear();

        var items = _settings.SelectionToolbar;
        if (items is null || items.Count == 0) items = _settings.SelectionToolbar = SelectionToolbarItems.CreateDefault();
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox
            {
                Content = SelectionToolbarCatalog.DisplayName(items[i].Id),
                Style = (Style)FindResource("AppCheckBox"),
                IsChecked = items[i].Visible,
                VerticalAlignment = VerticalAlignment.Center
            };
            check.Checked += (_, _) => SetToolbarVisible(index, true);
            check.Unchecked += (_, _) => SetToolbarVisible(index, false);
            Grid.SetColumn(check, 0);
            row.Children.Add(check);

            var up = new Button
            {
                Content = "↑",
                Style = (Style)FindResource("SecondaryButton"),
                Width = 34,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = index > 0,
                ToolTip = "上移"
            };
            up.Click += (_, _) => MoveToolbarItem(index, -1);
            Grid.SetColumn(up, 1);
            row.Children.Add(up);

            var down = new Button
            {
                Content = "↓",
                Style = (Style)FindResource("SecondaryButton"),
                Width = 34,
                Height = 26,
                Padding = new Thickness(0),
                IsEnabled = index < items.Count - 1,
                ToolTip = "下移"
            };
            down.Click += (_, _) => MoveToolbarItem(index, 1);
            Grid.SetColumn(down, 2);
            row.Children.Add(down);

            ToolbarListPanel.Children.Add(row);
        }
    }

    private void SetToolbarVisible(int index, bool visible)
    {
        if (_loading) return;
        var items = _settings.SelectionToolbar;
        if (index < 0 || index >= items.Count) return;
        items[index].Visible = visible;
        SettingsSaved?.Invoke();
        BuildToolbarPreview();
        StatusText.Text = "选区工具条已更新";
    }

    private void MoveToolbarItem(int index, int delta) => MoveToolbarItemTo(index, index + delta);

    /// <summary>把第 from 个按钮移动到 to（列表内索引）：列表、预览条同步刷新。</summary>
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
        BuildToolbarList();
        BuildToolbarPreview();
        StatusText.Text = "选区工具条已更新";
    }

    private void ResetToolbar_Click(object sender, RoutedEventArgs e)
    {
        _settings.SelectionToolbar = SelectionToolbarItems.CreateDefault();
        SettingsSaved?.Invoke();
        BuildToolbarList();
        BuildToolbarPreview();
        StatusText.Text = "选区工具条已恢复默认";
    }

    /// <summary>
    /// 工具条预览：按当前顺序横向排出全部按钮（隐藏项半透明），与真实工具条一样按分组插分隔线。
    /// 按住任意项拖动即可调整顺序。
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
                    Margin = new Thickness(5, 5, 5, 5),
                    Background = (Brush)FindResource("BorderBrush")
                });
            }

            previousGroup = group;

            var chip = new Border
            {
                Tag = item,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(9, 4, 9, 4),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.SizeAll,
                Opacity = item.Visible ? 1.0 : 0.45,
                AllowDrop = true,
                Child = new TextBlock
                {
                    Text = SelectionToolbarCatalog.DisplayName(item.Id),
                    FontSize = 12,
                    Foreground = (Brush)FindResource(item.Visible ? "TextPrimaryBrush" : "TextSecondaryBrush")
                },
                ToolTip = item.Visible
                    ? $"{SelectionToolbarCatalog.DisplayName(item.Id)}：按住拖动调整顺序"
                    : $"{SelectionToolbarCatalog.DisplayName(item.Id)}：当前隐藏（勾选后显示），仍可拖动调整位置"
            };
            ResetPreviewChipLook(chip);

            chip.MouseLeftButtonDown += PreviewChip_MouseDown;
            chip.MouseMove += PreviewChip_MouseMove;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _draggingItem = null;
                if (chip.IsMouseCaptured) chip.ReleaseMouseCapture();
            };
            chip.DragEnter += PreviewChip_DragEnter;
            chip.DragOver += PreviewChip_DragEnter;
            chip.DragLeave += (_, _) => ResetPreviewChipLook(chip);
            chip.Drop += PreviewChip_Drop;
            ToolbarPreviewPanel.Children.Add(chip);
        }
    }

    /// <summary>恢复预览项的正常外观（按当前可见性）。</summary>
    private void ResetPreviewChipLook(Border chip)
    {
        if (chip.Tag is not ToolbarItemSetting item) return;
        chip.BorderThickness = new Thickness(1);
        chip.Background = (Brush)FindResource(item.Visible ? "SurfaceBrush" : "PanelBrush");
        chip.BorderBrush = (Brush)FindResource(item.Visible ? "BorderBrush" : "SurfaceHoverBrush");
    }

    private void PreviewChip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_loading) return;
        if (sender is FrameworkElement element && element.Tag is ToolbarItemSetting item)
        {
            _draggingItem = item;
            _dragStartPoint = e.GetPosition(this);
            // 捕获鼠标，快速拖动时也不会在移出预览项后丢掉 MouseMove。
            element.CaptureMouse();
        }
    }

    private void PreviewChip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingItem is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.X) < 4 && Math.Abs(position.Y - _dragStartPoint.Y) < 4) return;

        var item = _draggingItem;
        _draggingItem = null;
        if (sender is UIElement source)
        {
            if (source.IsMouseCaptured) source.ReleaseMouseCapture();
            DragDrop.DoDragDrop(source, new DataObject(typeof(ToolbarItemSetting), item), DragDropEffects.Move);
        }
    }

    private void PreviewChip_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ToolbarItemSetting)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        if (sender is Border chip)
        {
            chip.BorderThickness = new Thickness(2);
            chip.BorderBrush = (Brush)FindResource("AccentBrush");
        }
    }

    private void PreviewChip_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border chip || chip.Tag is not ToolbarItemSetting target) return;
        if (e.Data.GetData(typeof(ToolbarItemSetting)) is not ToolbarItemSetting dragged) return;

        ResetPreviewChipLook(chip);

        var items = _settings.SelectionToolbar;
        if (items is null) return;

        // 设置写回时列表会被整体替换，按 Id 兜底查找更稳。
        int from = items.IndexOf(dragged);
        if (from < 0) from = items.FindIndex(i => string.Equals(i.Id, dragged.Id, StringComparison.Ordinal));
        int to = items.IndexOf(target);
        if (to < 0) to = items.FindIndex(i => string.Equals(i.Id, target.Id, StringComparison.Ordinal));
        if (from < 0 || to < 0) return;

        // 落在目标右半边就插到它后面，左半边插到它前面。
        if (e.GetPosition(chip).X > chip.ActualWidth / 2) to++;
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
