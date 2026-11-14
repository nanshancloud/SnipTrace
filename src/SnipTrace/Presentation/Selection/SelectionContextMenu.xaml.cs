using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 右键快捷操作菜单（参考 Snipaste）：无选区时右键退出截图，
/// 有选区时右键弹出复制 / 保存 / 钉图 / 录制 / 清除选区的菜单。
/// </summary>
public partial class SelectionContextMenu : Window
{
    private SelectionOverlay? _overlay;

    public SelectionContextMenu()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void Attach(SelectionOverlay overlay)
    {
        _overlay = overlay;
        AddItem("复制到剪贴板", "Ctrl+C", () => Request(SelectionAction.Copy));
        AddItem("保存为 PNG", "Ctrl+S", () => Request(SelectionAction.Save));
        AddItem("钉图置顶", null, () => Request(SelectionAction.Pin));
        AddSeparator();
        AddItem("录制 GIF", "Ctrl+Shift+G", () => Request(SelectionAction.Gif));
        AddItem("录制 MP4", "Ctrl+Shift+R", () => Request(SelectionAction.Video));
        AddSeparator();
        AddItem("清除选区", null, () => _overlay?.ClearSelection());
        AddItem("退出截图", "Esc", () => _overlay?.Cancel());
    }

    /// <summary>显示在光标右下，靠边时向左/向上翻转并钳制在工作区内。</summary>
    public void ShowMenu(PixelPoint cursor)
    {
        Show();
        UpdateLayout();

        var (width, height) = WindowPlacement.GetPixelSize(this);
        if (width <= 0 || height <= 0) return;

        var monitor = MonitorService.FromPoint(cursor);
        int x = cursor.X + 6;
        int y = cursor.Y + 6;
        if (monitor is not null)
        {
            var work = monitor.WorkArea;
            if (x + width > work.Right - 4) x = cursor.X - width - 6;
            if (y + height > work.Bottom - 4) y = cursor.Y - height - 6;
            x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
            y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
        }

        WindowPlacement.MoveTo(this, x, y);
    }

    public void HideMenu()
    {
        if (IsVisible) Hide();
    }

    /// <summary>菜单当前占用的屏幕物理矩形；未显示时返回空。</summary>
    public PixelRect GetBounds()
    {
        if (!IsVisible) return PixelRect.Empty;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return PixelRect.Empty;
        return WindowPlacement.GetPixelBounds(this);
    }

    private void Request(SelectionAction action)
    {
        HideMenu();
        _overlay?.RequestAction(action);
    }

    private void AddItem(string text, string? shortcut, Action click)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        if (!string.IsNullOrEmpty(shortcut))
        {
            var keys = new TextBlock
            {
                Text = shortcut,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0)
            };
            Grid.SetColumn(keys, 1);
            grid.Children.Add(keys);
        }

        var item = new Button
        {
            Content = grid,
            Style = (Style)FindResource("SelectionMenuItem"),
            Margin = new Thickness(0, 0, 0, 1)
        };
        item.Click += (_, _) => click();
        MenuPanel.Children.Add(item);
    }

    private void AddSeparator()
    {
        MenuPanel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(6, 4, 6, 5),
            Background = (Brush)FindResource("BorderBrush")
        });
    }
}
