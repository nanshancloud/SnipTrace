using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnipTrace.App.Presentation.ViewModels;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

public partial class HomeView : UserControl
{
    private bool _wideRecent;

    public HomeView()
    {
        InitializeComponent();
        UpdateRecentHint();
    }

    public event Action? ScreenshotRequested;

    /// <summary>当前显示器全屏截图。</summary>
    public event Action? FullScreenRequested;

    /// <summary>所有显示器全屏截图。</summary>
    public event Action? AllScreensRequested;

    public event Action? GifRequested;

    public event Action? VideoRequested;

    public event Action<RecentItemView>? RecentActivated;

    /// <summary>同步首页快捷键徽标（统一快捷键模式下 GIF / 录屏卡片不再显示单独按键）。</summary>
    public void RefreshHotkeys(AppSettings settings)
    {
        bool unified = settings.HotkeyMode == HotkeyModes.Unified;
        ScreenshotKeyText.Text = settings.CaptureHotkey;
        GifKeyBadge.Visibility = unified ? Visibility.Collapsed : Visibility.Visible;
        VideoKeyBadge.Visibility = unified ? Visibility.Collapsed : Visibility.Visible;
        GifKeyText.Text = settings.GifHotkey;
        VideoKeyText.Text = settings.VideoHotkey;
    }

    public void SetRecent(IReadOnlyList<RecentItemView> items)
    {
        RecentList.ItemsSource = items;
        bool empty = items.Count == 0;
        EmptyRecent.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        RecentList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        UpdateRecentHint();
    }

    /// <summary>按内容区尺寸调整：模式卡列数、标题区高度档位、最近记录位置。</summary>
    public void ApplyLayout(bool compactCards, ShellHeightTier heightTier, bool wideRecent)
    {
        CardGrid.Columns = compactCards ? 1 : 3;
        CardGrid.Rows = 0;

        var margin = compactCards ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 12, 0);
        ScreenshotCard.Margin = margin;
        GifCard.Margin = margin;
        VideoCard.Margin = new Thickness(0);

        // 高度档位：≥760 完整，600–759 压缩标题区，<600 再隐藏副标题并折叠最近记录。
        SubtitleText.Visibility = heightTier == ShellHeightTier.Minimal ? Visibility.Collapsed : Visibility.Visible;
        TitleText.FontSize = heightTier switch
        {
            ShellHeightTier.Full => 26,
            ShellHeightTier.Compact => 22,
            _ => 20
        };
        HeaderPanel.Margin = heightTier == ShellHeightTier.Full
            ? new Thickness(0, 0, 0, 20)
            : new Thickness(0, 0, 0, 12);
        RecentPanel.Visibility = heightTier == ShellHeightTier.Minimal ? Visibility.Collapsed : Visibility.Visible;

        if (_wideRecent != wideRecent)
        {
            _wideRecent = wideRecent;
            if (wideRecent)
            {
                DockPanel.SetDock(RecentPanel, Dock.Right);
                RecentPanel.Width = 320;
                RecentPanel.Margin = new Thickness(28, 0, 0, 0);
                RecentList.ItemsPanel = CreateVerticalPanel();
            }
            else
            {
                DockPanel.SetDock(RecentPanel, Dock.Bottom);
                RecentPanel.Width = double.NaN;
                RecentPanel.Margin = new Thickness(0, 24, 0, 0);
                RecentList.ItemsPanel = CreateWrapPanel();
            }
        }
    }

    private static ItemsPanelTemplate CreateVerticalPanel()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
        return new ItemsPanelTemplate(factory);
    }

    private static ItemsPanelTemplate CreateWrapPanel()
    {
        var factory = new FrameworkElementFactory(typeof(WrapPanel));
        return new ItemsPanelTemplate(factory);
    }

    private void UpdateRecentHint()
    {
        RecentHint.Visibility = RecentList.ItemsSource is IEnumerable<RecentItemView> { } ? Visibility.Visible : Visibility.Collapsed;
        if (RecentList.ItemsSource is System.Collections.ICollection collection)
        {
            RecentHint.Visibility = collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ScreenshotCard_Click(object sender, RoutedEventArgs e) => ScreenshotRequested?.Invoke();

    private void FullScreenButton_Click(object sender, RoutedEventArgs e) => FullScreenRequested?.Invoke();

    private void AllScreensButton_Click(object sender, RoutedEventArgs e) => AllScreensRequested?.Invoke();

    private void GifCard_Click(object sender, RoutedEventArgs e) => GifRequested?.Invoke();

    private void VideoCard_Click(object sender, RoutedEventArgs e) => VideoRequested?.Invoke();

    private void RecentItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentItemView item }) RecentActivated?.Invoke(item);
    }
}
