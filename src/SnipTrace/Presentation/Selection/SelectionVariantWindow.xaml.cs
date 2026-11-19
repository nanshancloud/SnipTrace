using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 工具条「图形 / 箭头」按钮的弹出芯片层：图形可选矩形 / 椭圆 / 直线，
/// 箭头可选箭头 / 直线。选中后保持展开，方便连续切换；点击覆盖层时收起。
/// </summary>
public partial class SelectionVariantWindow : Window
{
    private static readonly (string Id, string Variant)[] Variants =
    {
        (SelectionToolbarItems.Shape, SelectionToolbarItems.VariantRectangle),
        (SelectionToolbarItems.Shape, SelectionToolbarItems.VariantEllipse),
        (SelectionToolbarItems.Shape, SelectionToolbarItems.VariantLine),
        (SelectionToolbarItems.Arrow, SelectionToolbarItems.VariantArrow),
        (SelectionToolbarItems.Arrow, SelectionToolbarItems.VariantLine)
    };

    private readonly List<(ToggleButton Button, string Variant)> _chips = new();
    private SelectionOverlay? _overlay;
    private string _id = string.Empty;

    public SelectionVariantWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void Attach(SelectionOverlay overlay) => _overlay = overlay;

    /// <summary>按按钮 Id 重建芯片：图形三个、箭头两个；<paramref name="current"/> 为当前子选项。</summary>
    public void ShowAbove(PixelRect anchor, string id, string current)
    {
        if (anchor.IsEmpty) return;

        _id = id;
        BuildChips();
        Refresh(id, current);

        Show();
        UpdateLayout();

        var (width, height) = WindowPlacement.GetPixelSize(this);
        if (width <= 0 || height <= 0) return;

        var monitor = MonitorService.FromPoint(new PixelPoint(anchor.Left + anchor.Width / 2, anchor.Top)) ?? MonitorService.Primary;
        if (monitor is null) return;
        var work = monitor.WorkArea;

        int x = anchor.Right - width / 2 - anchor.Width / 2;
        int y = anchor.Top - height - 6;
        if (y < work.Top + 4) y = anchor.Bottom + 6;
        x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
        y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));

        WindowPlacement.MoveTo(this, x, y);
    }

    public void HidePopup()
    {
        if (IsVisible) Hide();
    }

    /// <summary>同步芯片选中态（按钮图标切换后由工具条调用）。</summary>
    public void Refresh(string id, string variant)
    {
        if (!string.Equals(id, _id, StringComparison.Ordinal)) return;
        foreach (var (button, chip) in _chips) button.IsChecked = chip == variant;
    }

    private void BuildChips()
    {
        ChipPanel.Children.Clear();
        _chips.Clear();

        foreach (var (id, variant) in Variants)
        {
            if (!string.Equals(id, _id, StringComparison.Ordinal)) continue;

            var chip = new ToggleButton
            {
                Content = SelectionToolbarCatalog.VariantName(id, variant),
                Style = (Style)FindResource("ChipToggle"),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11,
                Padding = new Thickness(9, 3, 9, 3)
            };
            string capturedVariant = variant;
            chip.Click += (_, _) => _overlay?.SetAnnotateVariant(_id, capturedVariant);
            _chips.Add((chip, variant));
            ChipPanel.Children.Add(chip);
        }
    }
}
