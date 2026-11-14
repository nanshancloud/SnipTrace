using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>工具条「比例」按钮的弹出芯片层：选择一个比例后自动收起。</summary>
public partial class SelectionRatioWindow : Window
{
    private readonly List<(ToggleButton Button, AspectRatio Ratio)> _chips = new();
    private SelectionOverlay? _overlay;

    public SelectionRatioWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void Attach(SelectionOverlay overlay)
    {
        _overlay = overlay;
        foreach (var ratio in AspectRatio.Presets)
        {
            var captured = ratio;
            var chip = new ToggleButton
            {
                Content = ratio.Label,
                Style = (Style)FindResource("ChipToggle"),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11,
                Padding = new Thickness(9, 3, 9, 3),
                IsChecked = ratio == AspectRatio.Free
            };
            chip.Click += (_, _) =>
            {
                _overlay?.SetRatio(captured);
                RefreshRatio(captured);
                HidePopup();
            };
            _chips.Add((chip, ratio));
            ChipPanel.Children.Add(chip);
        }
    }

    /// <summary>显示在工具条「比例」按钮上方，空间不足时翻到下方。</summary>
    public void ShowAbove(PixelRect anchor)
    {
        if (anchor.IsEmpty) return;

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

    public void RefreshRatio(AspectRatio current)
    {
        foreach (var (button, ratio) in _chips)
        {
            button.IsChecked = ratio.Label == current.Label;
        }
    }
}
