using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnipTrace.App.Presentation.ViewModels;

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

    public event Action? GifRequested;

    public event Action? VideoRequested;

    public event Action<RecentItemView>? RecentActivated;

    public void SetRecent(IReadOnlyList<RecentItemView> items)
    {
        RecentList.ItemsSource = items;
        bool empty = items.Count == 0;
        EmptyRecent.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        RecentList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        UpdateRecentHint();
    }

    /// <summary>按内容区尺寸调整：模式卡列数、最近记录位置、装饰区域。</summary>
    public void ApplyLayout(bool compactCards, bool lowHeight, bool wideRecent)
    {
        CardGrid.Columns = compactCards ? 1 : 3;
        CardGrid.Rows = 0;

        var margin = compactCards ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 12, 0);
        ScreenshotCard.Margin = margin;
        GifCard.Margin = margin;
        VideoCard.Margin = new Thickness(0);

        SubtitleText.Visibility = lowHeight ? Visibility.Collapsed : Visibility.Visible;
        TitleText.FontSize = lowHeight ? 22 : 26;
        TitleText.Margin = lowHeight ? new Thickness(0) : new Thickness(0);

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

    private void GifCard_Click(object sender, RoutedEventArgs e) => GifRequested?.Invoke();

    private void VideoCard_Click(object sender, RoutedEventArgs e) => VideoRequested?.Invoke();

    private void RecentItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecentItemView item }) RecentActivated?.Invoke(item);
    }
}
