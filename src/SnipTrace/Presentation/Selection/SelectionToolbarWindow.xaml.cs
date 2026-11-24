using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly SelectionBadgeWindow _badgePopup = new();
    private readonly SelectionVariantWindow _variantPopup = new();
    private readonly Dictionary<string, ToggleButton> _variantButtons = new();
    private readonly List<ToggleButton> _annotateButtons = new();
    private List<ToolbarItemSetting> _items = SelectionToolbarItems.CreateDefault();
    private SelectionOverlay? _overlay;
    private ToggleButton? _ratioButton;
    private TextBlock? _ratioLabel;
    private Border? _colorSwatch;
    private Button? _undoButton;
    private Button? _redoButton;

    public SelectionToolbarWindow()
    {
        InitializeComponent();
    }

    public void Attach(SelectionOverlay overlay, IReadOnlyList<ToolbarItemSetting>? items = null)
    {
        _overlay = overlay;
        _items = SelectionToolbarItems.Normalize(items?.ToList());
        _ratioPopup.Attach(overlay);
        _badgePopup.Attach(overlay);
        _variantPopup.Attach(overlay);
        overlay.BrushSizeChanged += OnBrushSizeChanged;
        PreviewMouseWheel += Toolbar_PreviewMouseWheel;
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
        _badgePopup.HidePopup();
        _variantPopup.HidePopup();
        if (_ratioButton is not null) _ratioButton.IsChecked = false;
    }

    /// <summary>按设置里的顺序与显示状态构建工具条；跨分组时自动插分隔线。</summary>
    private void BuildActions()
    {
        ButtonRow.Children.Clear();
        _annotateButtons.Clear();
        _variantButtons.Clear();
        _ratioButton = null;
        _ratioLabel = null;
        _colorSwatch = null;
        _undoButton = null;
        _redoButton = null;

        string? previousGroup = null;
        foreach (var item in _items)
        {
            if (!item.Visible) continue;
            string group = SelectionToolbarCatalog.GroupOf(item.Id);
            if (previousGroup is not null && !string.Equals(previousGroup, group, StringComparison.Ordinal)) AddSeparator();
            previousGroup = group;
            AddItem(item.Id);
        }

        RefreshHistoryState(_overlay?.CanUndoAnnotations == true, _overlay?.CanRedoAnnotations == true);
    }

    /// <summary>自检用：工具条上是否真的建出了回退 / 前进按钮。</summary>
    internal bool HasHistoryButtons => _undoButton is not null && _redoButton is not null;

    /// <summary>回退 / 前进按钮的可用状态跟随标注历史。</summary>
    public void RefreshHistoryState(bool canUndo, bool canRedo)
    {
        if (_undoButton is not null) _undoButton.IsEnabled = canUndo;
        if (_redoButton is not null) _redoButton.IsEnabled = canRedo;
    }

    /// <summary>单个按钮：按 Id 生成对应控件（图标、提示与动作集中在 AddItem 里，便于配置顺序）。</summary>
    private void AddItem(string id)
    {
        switch (id)
        {
            case SelectionToolbarItems.Ratio:
                AddRatioButton();
                break;

            case SelectionToolbarItems.Shape:
                AddVariantToggle(id, OverlayAnnotateTool.Shape);
                break;

            case SelectionToolbarItems.Arrow:
                AddVariantToggle(id, OverlayAnnotateTool.Arrow);
                break;

            case SelectionToolbarItems.Pen:
                AddAnnotateToggle(OverlayAnnotateTool.Pen, SelectionToolbarIcons.For(id),
                    L.T("画笔标注：在选区内拖动手绘；再点一次退出标注模式"));
                break;

            case SelectionToolbarItems.Text:
                AddAnnotateToggle(OverlayAnnotateTool.Text, SelectionToolbarIcons.For(id),
                    L.T("文字标注：在选区内点击后输入，Enter 确认、Esc 取消；滚轮改字号"));
                break;

            case SelectionToolbarItems.Number:
                AddBadgeButton();
                break;

            case SelectionToolbarItems.Mosaic:
                AddAnnotateToggle(OverlayAnnotateTool.Mosaic, SelectionToolbarIcons.For(id),
                    L.T("马赛克笔刷：在选区内按住拖动，沿轨迹打码；滚轮改块大小（8–48 px）"));
                break;

            case SelectionToolbarItems.Color:
                AddColorButton();
                break;

            case SelectionToolbarItems.Copy:
                AddAction("\uE8C8", L.T("复制到剪贴板（Ctrl+C）：连同标注直接复制，不进入编辑器"),
                    SelectionAction.Copy, label: L.T("复制"));
                break;

            case SelectionToolbarItems.Save:
                AddAction("\uE74E", L.T("保存为 PNG（Ctrl+S）"), SelectionAction.Save);
                break;

            case SelectionToolbarItems.Pin:
                AddAction("\uE840", L.T("钉图置顶，截图贴在屏幕上"), SelectionAction.Pin);
                break;

            case SelectionToolbarItems.Gif:
                AddAction("\uE91B", L.T("录制 GIF 动图（Ctrl+Shift+G）"), SelectionAction.Gif);
                break;

            case SelectionToolbarItems.Video:
                AddAction("\uE714", L.T("录制 MP4 视频（Ctrl+Shift+R）"), SelectionAction.Video);
                break;

            case SelectionToolbarItems.Undo:
                AddHistoryButton(undo: true);
                break;

            case SelectionToolbarItems.Redo:
                AddHistoryButton(undo: false);
                break;

            case SelectionToolbarItems.Cancel:
                AddAction("\uE711", L.T("取消本次截图（Esc / 右键）"), SelectionAction.Cancel);
                break;

            case SelectionToolbarItems.Refine:
                AddAction("\uE70F", L.T("精标：完成截图并进入标注编辑器继续调整（Enter）"),
                    SelectionAction.Edit, primary: true, label: L.T("精标"));
                break;
        }
    }

    /// <summary>序号标记：点击选区放置自增序号；弹出层选择形状（圆/三角/方/五边/六边/心形/圆角）与填充方式。</summary>
    private void AddBadgeButton()
    {
        var button = new ToggleButton
        {
            Content = SelectionToolbarIcons.Badge(),
            Style = (Style)FindResource("ToolIconToggle"),
            Tag = OverlayAnnotateTool.Number
        };
        button.Click += (_, _) =>
        {
            if (_overlay is null) return;
            bool activate = _overlay.AnnotateTool != OverlayAnnotateTool.Number;
            _hint.HideHint();
            _ratioPopup.HidePopup();
            if (_ratioButton is not null) _ratioButton.IsChecked = false;
            _overlay.SetAnnotateTool(activate ? OverlayAnnotateTool.Number : OverlayAnnotateTool.None);

            if (activate)
            {
                _badgePopup.Refresh(_overlay.BadgeShape, _overlay.BadgeFillMode);
                WindowPlacement.Raise(this);
                _badgePopup.ShowAbove(GetButtonBounds(button));
            }
            else
            {
                _badgePopup.HidePopup();
            }
        };
        AttachHint(button, L.T("序号标记：点击选区放置自动递增的序号；弹出层可选形状（圆/三角/方/五边/六边/心形/圆角）与 填充/边框"));
        _annotateButtons.Add(button);
        ButtonRow.Children.Add(button);
    }

    /// <summary>回退 / 前进：撤销或恢复覆盖层上的一步标注操作（与 Ctrl+Z / Ctrl+Y 等价）。</summary>
    private void AddHistoryButton(bool undo)
    {
        var button = new Button
        {
            Content = SelectionToolbarIcons.Glyph(undo ? "\uE7A7" : "\uE7A6"),
            Style = (Style)FindResource("ToolIconButton"),
            IsEnabled = undo ? _overlay?.CanUndoAnnotations == true : _overlay?.CanRedoAnnotations == true
        };
        button.Click += (_, _) =>
        {
            ClosePopups();
            if (undo) _overlay?.UndoAnnotations();
            else _overlay?.RedoAnnotations();
        };
        AttachHint(button, undo
            ? L.T("回退：撤销上一步标注（Ctrl+Z）")
            : L.T("前进：恢复刚撤销的标注（Ctrl+Shift+Z / Ctrl+Y）"));

        if (undo) _undoButton = button;
        else _redoButton = button;
        ButtonRow.Children.Add(button);
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

        // 工具条是独立顶层窗口，光标停在按钮上时滚轮不会到覆盖层窗口，这里直接转发。
        if (tool is OverlayAnnotateTool.Pen or OverlayAnnotateTool.Mosaic)
        {
            button.MouseWheel += (_, e) =>
            {
                _overlay?.AdjustBrushSize(tool, e.Delta > 0);
                e.Handled = true;
            };
        }

        AttachHint(button, hint);
        _annotateButtons.Add(button);
        ButtonRow.Children.Add(button);
    }

    /// <summary>图形 / 箭头：点击激活工具并弹出子选项芯片，再点一次退出标注模式。</summary>
    private void AddVariantToggle(string id, OverlayAnnotateTool tool)
    {
        var button = new ToggleButton
        {
            Content = SelectionToolbarIcons.For(id, VariantOf(id)),
            Style = (Style)FindResource("ToolIconToggle"),
            Tag = tool
        };
        button.Click += (_, _) =>
        {
            if (_overlay is null) return;
            bool activate = _overlay.AnnotateTool != tool;
            ClosePopups();
            _overlay.SetAnnotateTool(activate ? tool : OverlayAnnotateTool.None);
            if (!activate) return;

            WindowPlacement.Raise(this);
            _variantPopup.ShowAbove(GetButtonBounds(button), id, VariantOf(id));
        };
        AttachHint(button, id == SelectionToolbarItems.Shape
            ? L.T("图形标注：在选区内拖出矩形 / 椭圆 / 直线（弹出层切换，记住上次选择）；滚轮改线宽")
            : L.T("箭头标注：在选区内拖动绘制箭头 / 直线（弹出层切换，记住上次选择）；滚轮改线宽"));
        _variantButtons[id] = button;
        _annotateButtons.Add(button);
        ButtonRow.Children.Add(button);
    }

    /// <summary>
    /// 窗口级滚轮兜底：工具条是独立顶层窗口，按钮间隙 / 边框处滚轮到不了按钮，
    /// 这里在预览阶段补一手——当前是画笔 / 马赛克工具时改笔头，颜色按钮除外（它自己切预设色）。
    /// </summary>
    private void Toolbar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var tool = _overlay?.AnnotateTool;
        if (tool is not (OverlayAnnotateTool.Pen or OverlayAnnotateTool.Mosaic)) return;

        // 命中工具条内任一按钮时交给按钮自己的 handler，避免重复调整。
        if (e.OriginalSource is DependencyObject source)
        {
            for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is ButtonBase) return;
            }
        }

        _overlay?.AdjustBrushSize(tool.Value, e.Delta > 0);
        e.Handled = true;
    }

    /// <summary>滚轮调整下一笔笔刷大小后在工具条上方显示当前尺寸，给出可见反馈。</summary>
    private void OnBrushSizeChanged(OverlayAnnotateTool tool, double width, bool mosaic)
    {
        int block = Math.Clamp((int)Math.Round(width * 2), ShapeAnnotation.MinMosaicBlock, ShapeAnnotation.MaxMosaicBlock);
        string text = mosaic
            ? $"马赛克笔刷：下一笔块大小 {block} px（线宽 {width:0}）"
            : $"画笔笔刷：下一笔线宽 {width:0} px";

        var anchor = _annotateButtons.FirstOrDefault(b => Equals(b.Tag, tool));
        if (anchor is not null) ShowHint(anchor, text);
        else _hint.ShowHint(text);
    }

    /// <summary>当前记住的子选项（覆盖层为准，未接通时退回默认值）。</summary>
    private string VariantOf(string id) => (id == SelectionToolbarItems.Shape
        ? _overlay?.ShapeVariant
        : _overlay?.ArrowVariant) ?? SelectionToolbarItems.NormalizeVariant(id, null);

    /// <summary>子选项变化后刷新按钮图标与弹出层选中态。</summary>
    public void RefreshVariantState(string shapeVariant, string arrowVariant)
    {
        RefreshVariantButton(SelectionToolbarItems.Shape, shapeVariant);
        RefreshVariantButton(SelectionToolbarItems.Arrow, arrowVariant);
    }

    private void RefreshVariantButton(string id, string variant)
    {
        if (_variantButtons.TryGetValue(id, out var button)) button.Content = SelectionToolbarIcons.For(id, variant);
        _variantPopup.Refresh(id, variant);
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
        // 取色模式：点击开系统取色对话框；滚轮仍可快速切预设色。
        button.Click += (_, _) =>
        {
            uint current = _overlay?.AnnotateColor ?? 0xFFFF6B5F;
            if (ColorPicker.TryPick(current, out uint picked)) _overlay?.SetAnnotateColor(picked);
        };
        button.MouseWheel += (_, e) =>
        {
            _overlay?.CycleAnnotateColor();
            e.Handled = true;
        };
        AttachHint(button, L.T("标注 / 文本颜色：点击打开取色对话框任选颜色；滚轮切换预设色（珊瑚 / 黄 / 绿 / 蓝 / 白 / 深灰）"));
        ButtonRow.Children.Add(button);
    }

    /// <summary>刷新标注工具选中态与颜色样本；离开序号工具时收起形状弹出层。</summary>
    public void RefreshAnnotateState(OverlayAnnotateTool tool, uint argb)
    {
        foreach (var button in _annotateButtons)
        {
            button.IsChecked = (OverlayAnnotateTool)button.Tag! == tool;
        }

        if (tool != OverlayAnnotateTool.Number) _badgePopup.HidePopup();
        if (tool is not (OverlayAnnotateTool.Shape or OverlayAnnotateTool.Arrow)) _variantPopup.HidePopup();

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
        _ratioButton = new ToggleButton
        {
            Content = SelectionToolbarIcons.RatioChip(out _ratioLabel),
            Style = (Style)FindResource("ChipToggle"),
            Margin = new Thickness(0, 0, 3, 0),
            Focusable = false
        };
        _ratioButton.Click += (_, _) => ToggleRatioPopup();
        AttachHint(_ratioButton, L.T("锁定选区比例：自由 / 原始 / 16:9 / 16:10 / 4:3 / 3:2 / 1:1 / 9:16"));
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

    private PixelRect GetRatioButtonBounds() => GetButtonBounds(_ratioButton);

    /// <summary>计算按钮在虚拟桌面中的物理像素矩形（弹出层锚点用）。</summary>
    private PixelRect GetButtonBounds(FrameworkElement? target)
    {
        var window = WindowPlacement.GetPixelBounds(this);
        if (target is null) return window;

        Point offset;
        try
        {
            offset = target.TransformToAncestor(this).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return window;
        }

        double scale = WindowPlacement.GetScale(new WindowInteropHelper(this).Handle);
        int left = window.Left + (int)Math.Round(offset.X * scale);
        int top = window.Top + (int)Math.Round(offset.Y * scale);
        int width = Math.Max(1, (int)Math.Round(target.ActualWidth * scale));
        int height = Math.Max(1, (int)Math.Round(target.ActualHeight * scale));
        return new PixelRect(left, top, width, height);
    }

    private void AddAction(string glyph, string hint, SelectionAction action, bool primary = false, string? label = null)
    {
        var button = new Button
        {
            Content = BuildActionContent(glyph, label),
            Style = (Style)FindResource(primary ? "ToolIconPrimaryButton" : "ToolIconButton")
        };

        // 带文字的按钮（复制 / 精标）宽度自适应，避免固定 30 px 把文字裁掉。
        if (label is not null)
        {
            button.Width = double.NaN;
            button.MinWidth = 0;
            button.Padding = new Thickness(8, 0, 9, 0);
        }

        button.Click += (_, _) =>
        {
            ClosePopups();
            if (action == SelectionAction.Cancel) _overlay?.Cancel();
            else _overlay?.RequestAction(action);
        };
        AttachHint(button, hint);
        ButtonRow.Children.Add(button);
    }

    /// <summary>图标 + 文字（可选）：复制与精标用文字标签区分，避免两个「确认感」图标混淆。</summary>
    private static FrameworkElement BuildActionContent(string glyph, string? label)
    {
        if (label is null) return IconContent.Create(glyph);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(IconContent.Create(glyph, 12));

        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });
        panel.Children.Add(text);
        return panel;
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
