using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SnipTrace.App.Presentation.Services;
using SnipTrace.App.Presentation.ViewModels;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

public partial class ShellWindow : Window
{
    private static readonly (string Key, string Label, string Glyph)[] NavDefinitions =
    {
        ("home", "截图", "▣"),
        ("gif", "GIF", "◉"),
        ("video", "录屏", "▶"),
        ("recent", "最近", "☰"),
        ("settings", "设置", "⚙")
    };

    private readonly List<(RadioButton Button, TextBlock Label)> _navButtons = new();
    private HomeView? _home;
    private ModeView? _gifPage;
    private ModeView? _videoPage;
    private RecentView? _recentPage;
    private SettingsView? _settingsPage;
    private string _currentKey = "home";
    private bool _restoringLayout;

    public ShellWindow()
    {
        InitializeComponent();
        BuildNavigation();
        ApplyStoredLayout();
        Navigate("home");
        UpdateStatusBar();

        // 句柄创建后按所在显示器的工作区再收敛一次，避免首次显示就顶出屏幕。
        SourceInitialized += (_, _) => EnsureWithinWorkArea();

        Loaded += (_, _) =>
        {
            EnsureWithinWorkArea();
            UpdateResponsiveLayout();
            RefreshRecent();
        };

        SizeChanged += (_, _) => UpdateResponsiveLayout();

        // 显示器分辨率、缩放或任务栏变化后重新收敛。
        DpiChanged += (_, _) => EnsureWithinWorkArea();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(EnsureWithinWorkArea));
    }

    public bool AllowClose { get; set; }

    private static AppServices Services => AppServices.Current!;

    public void ShowPage(string key)
    {
        Navigate(key);
        ShowAndActivate();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        EnsureWithinWorkArea();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>截图前隐藏主窗口，避免冻结帧里出现自身。</summary>
    public void HideForCapture()
    {
        if (IsVisible && WindowState != WindowState.Minimized) Hide();
    }

    public void RefreshRecent()
    {
        var items = Services.Recent.Items.Select(RecentItemView.FromItem).ToList();
        _home?.SetRecent(items);
        _recentPage?.SetItems(items);
    }

    public void RefreshSettings()
    {
        _settingsPage?.LoadFrom(Services.Settings);
        UpdateStatusBar();
    }

    public void UpdateStatusBar()
    {
        var settings = Services.Settings;
        ShortcutHintText.Text = $"{settings.CaptureHotkey} 截图 · {settings.GifHotkey} GIF · {settings.VideoHotkey} 录屏";
        OutputDirectoryText.Text = "输出目录：" + settings.ScreenshotDirectory;
        SetTrayState("托盘常驻");
    }

    /// <summary>标题栏状态与底部状态栏的托盘状态保持同步。</summary>
    public void SetTrayState(string text)
    {
        TrayStateText.Text = text;
        TrayHintText.Text = "托盘：" + text;
    }

    private void BuildNavigation()
    {
        foreach (var (key, label, glyph) in NavDefinitions)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("NavItem"),
                GroupName = "nav",
                Tag = key,
                Content = BuildNavContent(label, glyph, out var labelBlock)
            };
            button.Checked += NavItem_Checked;
            NavList.Children.Add(button);
            _navButtons.Add((button, labelBlock));

            var topButton = new RadioButton
            {
                Style = (Style)FindResource("NavItem"),
                GroupName = "topnav",
                Tag = key,
                Margin = new Thickness(0, 0, 4, 0),
                Content = BuildNavContent(label, glyph, out _)
            };
            topButton.Checked += NavItem_Checked;
            TopNavList.Children.Add(topButton);
        }
    }

    private static StackPanel BuildNavContent(string label, string glyph, out TextBlock labelBlock)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            Width = 18,
            TextAlignment = TextAlignment.Center,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(labelBlock);
        return panel;
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key }) Navigate(key);
    }

    private void Navigate(string key)
    {
        _currentKey = key;
        PageHost.Content = GetPage(key);
        SyncNavSelection(key);
        UpdateResponsiveLayout();
    }

    private void SyncNavSelection(string key)
    {
        foreach (var button in NavList.Children.OfType<RadioButton>().Concat(TopNavList.Children.OfType<RadioButton>()))
        {
            if (button.Tag as string == key && button.IsChecked != true) button.IsChecked = true;
        }
    }

    private FrameworkElement GetPage(string key)
    {
        switch (key)
        {
            case "gif":
                return _gifPage ??= CreateModePage(RecordingMode.Gif);
            case "video":
                return _videoPage ??= CreateModePage(RecordingMode.Video);
            case "recent":
                return _recentPage ??= CreateRecentPage();
            case "settings":
                return _settingsPage ??= CreateSettingsPage();
            default:
                return _home ??= CreateHomePage();
        }
    }

    private HomeView CreateHomePage()
    {
        var home = new HomeView();
        home.ScreenshotRequested += () => _ = Services.Capture.CaptureAsync(CaptureIntent.Screenshot);
        home.FullScreenRequested += () => _ = Services.Capture.CaptureFullScreenAsync(false);
        home.AllScreensRequested += () => _ = Services.Capture.CaptureFullScreenAsync(true);
        home.GifRequested += () => _ = Services.Capture.CaptureAsync(CaptureIntent.Gif);
        home.VideoRequested += () => _ = Services.Capture.CaptureAsync(CaptureIntent.Video);
        home.RecentActivated += OpenRecentItem;
        return home;
    }

    private ModeView CreateModePage(RecordingMode mode)
    {
        var page = new ModeView();
        page.Configure(mode, Services.Settings);
        page.StartRequested += requested =>
        {
            if (page.TryApply(Services.Settings, out string? error))
            {
                Services.NotifySettingsChanged();
                _ = Services.Capture.CaptureAsync(requested == RecordingMode.Gif ? CaptureIntent.Gif : CaptureIntent.Video);
            }
            else
            {
                Services.Toasts.Warn("参数不合法", error ?? "请检查录制参数。");
            }
        };
        return page;
    }

    private RecentView CreateRecentPage()
    {
        var page = new RecentView();
        page.ActionRequested += (item, action) => HandleRecentAction(item, action);
        page.SetItems(Services.Recent.Items.Select(RecentItemView.FromItem).ToList());
        return page;
    }

    private SettingsView CreateSettingsPage()
    {
        var page = new SettingsView(Services.Settings, Services.Paths);
        page.SettingsSaved += () =>
        {
            Services.NotifySettingsChanged();
            RefreshSettings();
            RefreshRecent();
        };
        return page;
    }

    private void HandleRecentAction(RecentItemView item, RecentAction action)
    {
        switch (action)
        {
            case RecentAction.Open:
                TryOpenFile(item.Item.Path);
                break;
            case RecentAction.Locate:
                TryLocateFile(item.Item.Path);
                break;
            case RecentAction.Delete:
                Services.Recent.Remove(item.Item);
                RefreshRecent();
                break;
        }
    }

    private void OpenRecentItem(RecentItemView item) => TryOpenFile(item.Item.Path);

    private void TryOpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Services.Toasts.Warn("文件不存在", path);
                Services.Recent.Prune();
                RefreshRecent();
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn("无法打开文件", ex.Message);
        }
    }

    private void TryLocateFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn("无法定位文件", ex.Message);
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (!IsLoaded && ActualWidth <= 0) return;

        ShellLayoutMetrics metrics = ShellLayout.Resolve(ActualWidth, ActualHeight);
        bool minimal = metrics.Mode == ShellLayoutMode.Minimal;

        NavColumn.Width = new GridLength(metrics.NavWidth);
        NavBorder.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        TopNavBar.Visibility = minimal ? Visibility.Visible : Visibility.Collapsed;

        bool wideNav = metrics.Mode is ShellLayoutMode.Expanded or ShellLayoutMode.Standard;
        foreach (var (_, label) in _navButtons)
        {
            label.Visibility = wideNav ? Visibility.Visible : Visibility.Collapsed;
        }

        PageHost.Margin = minimal ? new Thickness(16, 18, 16, 12) : new Thickness(24, 22, 24, 16);

        _home?.ApplyLayout(
            ShellLayout.UsesSingleColumn(metrics.Mode),
            metrics.HeightTier,
            ShellLayout.UsesRecentSidebar(metrics.Mode, metrics.HeightTier));
    }

    private void ApplyStoredLayout()
    {
        var area = GetWorkArea();
        var saved = Services.Settings.MainWindow;

        if (saved is not null && saved.Width > 0 && saved.Height > 0)
        {
            _restoringLayout = true;
            Width = Math.Clamp(saved.Width, MinWidth, Math.Max(MinWidth, area.Width));
            Height = Math.Clamp(saved.Height, MinHeight, Math.Max(MinHeight, area.Height));
            Left = Math.Clamp(saved.Left, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Clamp(saved.Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            if (saved.IsMaximized) WindowState = WindowState.Maximized;
            _restoringLayout = false;
            return;
        }

        Width = Math.Clamp(area.Width * 0.84, MinWidth, 1400);
        Height = Math.Clamp(area.Height * 0.82, MinHeight, 960);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    /// <summary>把窗口尺寸与位置收敛到所在显示器的工作区内，保证内容不被屏幕边缘裁切。</summary>
    private void EnsureWithinWorkArea()
    {
        if (WindowState != WindowState.Normal) return;

        Rect area = GetWorkArea();
        if (area.Width <= 0 || area.Height <= 0) return;

        double width = Math.Min(double.IsNaN(Width) ? ActualWidth : Width, area.Width);
        double height = Math.Min(double.IsNaN(Height) ? ActualHeight : Height, area.Height);
        double left = double.IsNaN(Left) ? area.Left : Left;
        double top = double.IsNaN(Top) ? area.Top : Top;

        // 工作区比最小尺寸还小时，优先保证不出屏。
        if (width < MinWidth) MinWidth = width;
        if (height < MinHeight) MinHeight = height;

        _restoringLayout = true;
        try
        {
            Width = width;
            Height = height;
            Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width));
            Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height));
        }
        finally
        {
            _restoringLayout = false;
        }
    }

    /// <summary>窗口所在显示器的工作区，单位与 WPF 的 Width/Left 一致（DIP）。</summary>
    private Rect GetWorkArea()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
                double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
                return new Rect(
                    info.WorkArea.Left / scaleX,
                    info.WorkArea.Top / scaleY,
                    (info.WorkArea.Right - info.WorkArea.Left) / scaleX,
                    (info.WorkArea.Bottom - info.WorkArea.Top) / scaleY);
            }
        }

        return SystemParameters.WorkArea;
    }

    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void SaveLayout()
    {
        if (_restoringLayout) return;
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        Services.Settings.MainWindow = new WindowLayout
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = WindowState == WindowState.Maximized
        };
        Services.SaveSettings();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            SaveLayout();
            Hide();
            Services.Toasts.Info("已最小化到托盘", "双击托盘图标可重新打开主窗口。");
            return;
        }

        SaveLayout();
        base.OnClosing(e);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
