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
/// 工具条「序号」按钮的弹出芯片层：上排选形状（圆 / 三角 / 方 / 五边 / 六边 / 心形 / 圆角），
/// 下排选填充方式（填充 / 边框）。选择后保持展开，方便连续调整；点击覆盖层时收起。
/// </summary>
public partial class SelectionBadgeWindow : Window
{
    private static readonly (BadgeShape Shape, string Label)[] Shapes =
    {
        (BadgeShape.Circle, "圆形"),
        (BadgeShape.Triangle, "三角"),
        (BadgeShape.Square, "方形"),
        (BadgeShape.Pentagon, "五边"),
        (BadgeShape.Hexagon, "六边"),
        (BadgeShape.Heart, "心形"),
        (BadgeShape.RoundedRect, "圆角")
    };

    private readonly List<(ToggleButton Button, BadgeShape Shape)> _shapeChips = new();
    private readonly List<(ToggleButton Button, BadgeFillMode Mode)> _modeChips = new();
    private SelectionOverlay? _overlay;

    public SelectionBadgeWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void Attach(SelectionOverlay overlay)
    {
        _overlay = overlay;

        foreach (var (shape, label) in Shapes)
        {
            var chip = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("ChipToggle"),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11,
                Padding = new Thickness(9, 3, 9, 3)
            };
            var capturedShape = shape;
            chip.Click += (_, _) =>
            {
                _overlay?.SetBadgeShape(capturedShape);
                Refresh(_overlay?.BadgeShape ?? capturedShape, _overlay?.BadgeFillMode ?? BadgeFillMode.Fill);
            };
            _shapeChips.Add((chip, shape));
            ShapePanel.Children.Add(chip);
        }

        foreach (var (mode, label) in new[] { (BadgeFillMode.Fill, "填充"), (BadgeFillMode.Outline, "边框") })
        {
            var chip = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("ChipToggle"),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11,
                Padding = new Thickness(9, 3, 9, 3)
            };
            var capturedMode = mode;
            chip.Click += (_, _) =>
            {
                _overlay?.SetBadgeFillMode(capturedMode);
                Refresh(_overlay?.BadgeShape ?? BadgeShape.Circle, capturedMode);
            };
            _modeChips.Add((chip, mode));
            ModePanel.Children.Add(chip);
        }

        Refresh(_overlay.BadgeShape, _overlay.BadgeFillMode);
    }

    /// <summary>显示在工具条「序号」按钮上方，空间不足时翻到下方。</summary>
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
        int y = anchor.Top - height - 8;
        if (y < work.Top + 4) y = anchor.Bottom + 8;
        x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
        y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));

        WindowPlacement.MoveTo(this, x, y);
    }

    public void HidePopup()
    {
        if (IsVisible) Hide();
    }

    public void Refresh(BadgeShape shape, BadgeFillMode mode)
    {
        foreach (var (button, chipShape) in _shapeChips) button.IsChecked = chipShape == shape;
        foreach (var (button, chipMode) in _modeChips) button.IsChecked = chipMode == mode;
    }
}
