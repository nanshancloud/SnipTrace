using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Presentation.Views;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// Snipaste 式单行图标工具条：比例▾ ｜ 标注工具 ｜ 复制 保存 钉图 ｜ GIF MP4 ｜ 取消 确认。
/// 比例芯片收在弹出层里，尺寸 / 坐标 / 取色由跟随鼠标的放大镜负责。
/// </summary>
public partial class SelectionToolbarWindow : Window
{
    private readonly SelectionHintWindow _hint = new();
    private readonly SelectionRatioWindow _ratioPopup = new();
    private readonly List<ToggleButton> _annotateButtons = new();
    private SelectionOverlay? _overlay;
    private ToggleButton? _ratioButton;
    private TextBlock? _ratioLabel;
    private Border? _colorSwatch;

    public SelectionToolbarWindow()
    {
        InitializeComponent();
    }

    public void Attach(SelectionOverlay overlay)
    {
        _overlay = overlay;
        _ratioPopup.Attach(overlay);
        BuildActions();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) ClosePopups();
        };
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void SetShown(bool shown)
    {
        if (shown)
        {
            if (!IsVisible)
            {
                Show();
                UpdateLayout();
            }

            return;
        }

        if (IsVisible)
        {
            ClosePopups();
            Hide();
        }
    }

    /// <summary>刷新「比例」按钮文案与弹出层芯片选中态。</summary>
    public void RefreshRatio(AspectRatio current)
    {
        if (_ratioLabel is not null) _ratioLabel.Text = current.Label;
        _ratioPopup.RefreshRatio(current);
    }

    public void ClosePopups()
    {
        _hint.HideHint();
        _ratioPopup.HidePopup();
        if (_ratioButton is not null) _ratioButton.IsChecked = false;
    }

    private void BuildActions()
    {
        AddRatioButton();
        AddSeparator();
        AddAnnotateButtons();
        AddSeparator();
        AddAction("\uE8C8", "复制到剪贴板（Ctrl+C）", SelectionAction.Copy);
        AddAction("\uE74E", "保存为 PNG（Ctrl+S）", SelectionAction.Save);
        AddAction("\uE840", "钉图置顶，截图贴在屏幕上", SelectionAction.Pin);
        AddSeparator();
        AddAction("\uE91B", "录制 GIF 动图（Ctrl+Shift+G）", SelectionAction.Gif);
        AddAction("\uE714", "录制 MP4 视频（Ctrl+Shift+R）", SelectionAction.Video);
        AddSeparator();
        AddAction("\uE711", "取消本次截图（Esc / 右键）", SelectionAction.Cancel);
        AddAction("\uE73E", "完成并进入编辑器精标（Enter）", SelectionAction.Edit, primary: true);
    }

    /// <summary>标注工具组：选中任一工具进入覆盖层标注模式，再点一次回到框选模式。</summary>
    private void AddAnnotateButtons()
    {
        AddAnnotateToggle(OverlayAnnotateTool.Arrow, StrokeIcon("M2,12 L11,3 M11,3 L7.5,3.5 M11,3 L10.5,6.5"),
            "箭头标注：在选区内拖动绘制；两种标注模式——覆盖层直标 / 点「完成」进编辑器精标");
        AddAnnotateToggle(OverlayAnnotateTool.Pen, StrokeIcon("M2.5,11.5 L10,4 L12,2 M2.5,11.5 L2,13 L3.5,12.5"),
            "画笔标注：在选区内拖动手绘；再点一次退出标注模式");
        AddAnnotateToggle(OverlayAnnotateTool.Rectangle, StrokeIcon("M2.5,3.5 L11.5,3.5 L11.5,10.5 L2.5,10.5 Z"),
            "矩形标注：在选区内拖出矩形；再点一次退出标注模式");
        AddAnnotateToggle(OverlayAnnotateTool.Ellipse, StrokeIcon("M7,3 C10.3,3 12,4.6 12,7 C12,9.4 10.3,11 7,11 C3.7,11 2,9.4 2,7 C2,4.6 3.7,3 7,3 Z"),
            "椭圆标注：在选区内拖出椭圆；再点一次退出标注模式");
        AddAnnotateToggle(OverlayAnnotateTool.Text, TextIcon(),
            "文字标注：在选区内点击后输入，Enter 确认、Esc 取消");
        AddColorButton();
    }

    private void AddAnnotateToggle(OverlayAnnotateTool tool, FrameworkElement icon, string hint)
    {
        var button = new ToggleButton
        {
            Content = icon,
            Style = (Style)FindResource("ToolIconToggle"),
            Tag = tool
        };
        button.Click += (_, _) =>
        {
            ClosePopups();
            _overlay?.SetAnnotateTool(tool);
        };
        AttachHint(button, hint);
        _annotateButtons.Add(button);
        ButtonRow.Children.Add(button);
    }

    private void AddColorButton()
    {
        _colorSwatch = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F)),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        var button = new Button
        {
            Content = _colorSwatch,
            Style = (Style)FindResource("ToolIconButton")
        };
        button.Click += (_, _) => _overlay?.CycleAnnotateColor();
        AttachHint(button, "标注颜色：点击切换 珊瑚 / 黄 / 绿 / 蓝 / 白 / 深灰");
        ButtonRow.Children.Add(button);
    }

    private static FrameworkElement StrokeIcon(string data)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Width = 14,
            Height = 14,
            Stretch = Stretch.None,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1)
        });
        return path;
    }

    private static FrameworkElement TextIcon()
    {
        var text = new TextBlock
        {
            Text = "T",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI")
        };
        text.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ToggleButton), 1)
        });
        return text;
    }

    /// <summary>刷新标注工具选中态与颜色样本。</summary>
    public void RefreshAnnotateState(OverlayAnnotateTool tool, uint argb)
    {
        foreach (var button in _annotateButtons)
        {
            button.IsChecked = (OverlayAnnotateTool)button.Tag! == tool;
        }

        if (_colorSwatch is not null)
        {
            _colorSwatch.Background = new SolidColorBrush(Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF)));
        }
    }

    private void AddRatioButton()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _ratioLabel = new TextBlock
        {
            Text = "自由",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        var chevron = new TextBlock
        {
            Text = "\uE70D",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 9,
            Margin = new Thickness(4, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        chevron.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ButtonBase), 1)
        });
        panel.Children.Add(_ratioLabel);
        panel.Children.Add(chevron);

        _ratioButton = new ToggleButton
        {
            Content = panel,
            Style = (Style)FindResource("ChipToggle"),
            Margin = new Thickness(0, 0, 3, 0),
            Focusable = false
        };
        _ratioButton.Click += (_, _) => ToggleRatioPopup();
        AttachHint(_ratioButton, "锁定选区比例：自由 / 原始 / 16:9 / 16:10 / 4:3 / 3:2 / 1:1 / 9:16");
        ButtonRow.Children.Add(_ratioButton);
    }

    private void ToggleRatioPopup()
    {
        _hint.HideHint();
        if (_ratioPopup.IsVisible)
        {
            _ratioPopup.HidePopup();
            if (_ratioButton is not null) _ratioButton.IsChecked = false;
            return;
        }

        WindowPlacement.Raise(this);
        _ratioPopup.ShowAbove(GetRatioButtonBounds());
        if (_ratioButton is not null) _ratioButton.IsChecked = _ratioPopup.IsVisible;
    }

    private PixelRect GetRatioButtonBounds()
    {
        var window = WindowPlacement.GetPixelBounds(this);
        if (_ratioButton is null) return window;

        Point offset;
        try
        {
            offset = _ratioButton.TransformToAncestor(this).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return window;
        }

        double scale = WindowPlacement.GetScale(new WindowInteropHelper(this).Handle);
        int left = window.Left + (int)Math.Round(offset.X * scale);
        int top = window.Top + (int)Math.Round(offset.Y * scale);
        int width = Math.Max(1, (int)Math.Round(_ratioButton.ActualWidth * scale));
        int height = Math.Max(1, (int)Math.Round(_ratioButton.ActualHeight * scale));
        return new PixelRect(left, top, width, height);
    }

    private void AddAction(string glyph, string hint, SelectionAction action, bool primary = false)
    {
        var button = new Button
        {
            Content = IconContent.Create(glyph),
            Style = (Style)FindResource(primary ? "ToolIconPrimaryButton" : "ToolIconButton")
        };
        button.Click += (_, _) =>
        {
            ClosePopups();
            if (action == SelectionAction.Cancel) _overlay?.Cancel();
            else _overlay?.RequestAction(action);
        };
        AttachHint(button, hint);
        ButtonRow.Children.Add(button);
    }

    private void AddSeparator()
    {
        ButtonRow.Children.Add(new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(4, 0, 4, 0),
            Background = (Brush)FindResource("BorderBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    /// <summary>系统 ToolTip 会被全屏置顶的覆盖层遮挡，这里改用独立置顶窗口显示介绍。</summary>
    private void AttachHint(FrameworkElement target, string text)
    {
        target.MouseEnter += (_, _) => ShowHint(target, text);
        target.MouseLeave += (_, _) => _hint.HideHint();
        target.PreviewMouseLeftButtonDown += (_, _) => _hint.HideHint();
    }

    private void ShowHint(FrameworkElement target, string text)
    {
        if (_overlay is null) return;

        _hint.ShowHint(text);
        WindowPlacement.Raise(this);

        var (width, height) = WindowPlacement.GetPixelSize(_hint);
        if (width <= 0 || height <= 0) return;

        Point offset;
        try
        {
            offset = target.TransformToAncestor(this).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var window = WindowPlacement.GetPixelBounds(this);
        double scale = WindowPlacement.GetScale(new WindowInteropHelper(this).Handle);
        int centerX = window.Left + (int)Math.Round((offset.X + target.ActualWidth / 2) * scale);
        int top = window.Top + (int)Math.Round(offset.Y * scale);
        int bottom = window.Top + (int)Math.Round((offset.Y + target.ActualHeight) * scale);

        int x = centerX - width / 2;
        int y = top - height - 6;
        var monitor = MonitorService.FromPoint(new PixelPoint(centerX, top));
        if (monitor is not null)
        {
            var work = monitor.WorkArea;
            x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
            if (y < work.Top + 4) y = bottom + 6;
            y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
        }

        WindowPlacement.MoveTo(_hint, x, y);
    }
}
