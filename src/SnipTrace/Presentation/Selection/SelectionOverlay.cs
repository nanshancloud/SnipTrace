using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

public enum SelectionAction
{
    Edit,
    Copy,
    Save,
    Pin,
    Gif,
    Video,
    Cancel
}

public sealed class SelectionResult
{
    public PixelRect Area { get; init; }

    public SelectionAction Action { get; init; }

    /// <summary>覆盖层上直接绘制的标注（选区内物理像素坐标），进编辑器可继续精标。</summary>
    public AnnotationDocument? Annotations { get; init; }
}

/// <summary>覆盖层标注工具（Snipaste 式直接标注模式）；None 表示普通框选模式。</summary>
public enum OverlayAnnotateTool
{
    None,

    /// <summary>图形：矩形 / 椭圆 / 直线，具体种类由记住的子选项决定。</summary>
    Shape,
    Arrow,
    Pen,
    Text,
    Number,

    /// <summary>马赛克：按区域块化采样底图，块大小跟随线宽。</summary>
    Mosaic
}

/// <summary>
/// 多覆盖层选区控制器：所有显示器共享同一份选区状态，坐标始终使用虚拟桌面物理像素。
/// </summary>
public sealed class SelectionOverlay : IDisposable
{
    private readonly DesktopSnapshot _snapshot;
    private readonly List<SelectionOverlayWindow> _windows = new();
    private readonly SelectionToolbarWindow _toolbar = new();
    private readonly SelectionMagnifierWindow _magnifier = new();
    private readonly TaskCompletionSource<SelectionResult?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PixelRect _virtualBounds;

    private AspectRatio _requestedRatio = AspectRatio.Free;
    private AspectRatio _effectiveRatio = AspectRatio.Free;
    private AspectRatio? _shiftRatio;
    private PixelRect _selection;
    private PixelPoint _anchor;
    private PixelPoint _cursor;
    private PixelPoint _lastMagnifierCursor;
    private DateTime _lastMagnifierUpdate = DateTime.MinValue;
    private Color _pixelColor = Colors.Black;
    private PixelRect _toolbarRect = PixelRect.Empty;
    private PixelRect _hoverWindow;
    private PixelRect _pendingWindow;
    private DateTime _lastHoverProbe = DateTime.MinValue;
    private bool _dragging;
    private bool _dragStarted;
    private bool _completed;
    private RectCorner _resizeCorner;
    private PixelRect _resizeStart;

    // 覆盖层标注（Snipaste 式）：选区内直接绘制，进编辑器可继续精标。
    private static readonly uint[] AnnotatePalette = { 0xFFFF6B5F, 0xFFFFCC00, 0xFF59E0BE, 0xFF4C8DFF, 0xFFF2F6FC, 0xFF1F2733 };
    private OverlayAnnotateTool _annotateTool = OverlayAnnotateTool.None;
    private AnnotationDocument? _overlayAnnotations;
    private ShapeAnnotation? _pendingShape;
    private AnnotationTextPopup? _textPopup;
    private uint _annotateColor = AnnotatePalette[0];
    private string _textFontFamily = AppSettings.DefaultTextFontFamily;
    private double _strokeWidth = 3;
    private bool _annotating;
    private string _shapeVariant = SelectionToolbarItems.VariantRectangle;
    private string _arrowVariant = SelectionToolbarItems.VariantArrow;
    private double _mosaicWidth = 12;
    private BadgeShape _badgeShape = BadgeShape.Circle;
    private BadgeFillMode _badgeFillMode = BadgeFillMode.Fill;

    public SelectionOverlay(DesktopSnapshot snapshot, IReadOnlyList<ToolbarItemSetting>? toolbarItems = null,
        string? textFontFamily = null)
    {
        _snapshot = snapshot;
        _virtualBounds = snapshot.Bounds;
        _cursor = MonitorService.GetCursorPosition();
        if (!string.IsNullOrWhiteSpace(textFontFamily)) _textFontFamily = textFontFamily.Trim();

        // 图形 / 箭头记住上次选的子选项；工具条 Attach 前先读好，图标一次画对。
        _shapeVariant = SelectionToolbarItems.NormalizeVariant(SelectionToolbarItems.Shape,
            toolbarItems?.FirstOrDefault(item => item.Id == SelectionToolbarItems.Shape)?.Variant);
        _arrowVariant = SelectionToolbarItems.NormalizeVariant(SelectionToolbarItems.Arrow,
            toolbarItems?.FirstOrDefault(item => item.Id == SelectionToolbarItems.Arrow)?.Variant);

        _toolbar.Attach(this, toolbarItems);
    }

    public PixelRect Selection => _selection;

    public async Task<SelectionResult?> ShowAsync()
    {
        foreach (var monitor in _snapshot.Monitors)
        {
            var window = new SelectionOverlayWindow();
            window.Attach(this, _snapshot, monitor);
            window.SourceInitialized += (_, _) => WindowPlacement.ApplyToolWindowStyle(window);
            _windows.Add(window);
            window.Show();
            WindowPlacement.Place(window, monitor.Bounds);
        }

        _toolbar.SetShown(false);
        FocusCursorWindow();
        UpdateViews();
        UpdateToolbar(forceMagnifier: true);

        // 显示器 DPI 生效后再校正一次物理位置，避免混合 DPI 下偏移。
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        foreach (var window in _windows)
        {
            WindowPlacement.Place(window, window.Monitor.Bounds);
        }

        UpdateToolbar(forceMagnifier: true);
        return await _completion.Task;
    }

    public void OnMouseDown(bool doubleClick)
    {
        if (_completed) return;
        _cursor = MonitorService.GetCursorPosition();
        _toolbar.ClosePopups();

        if (doubleClick && !_selection.IsEmpty)
        {
            Complete(SelectionAction.Copy);
            return;
        }

        // 普通模式下的选区四角：拖角继续调整选区大小（已画标注随选区一起对齐）。
        if (!_selection.IsEmpty
            && _annotateTool == OverlayAnnotateTool.None
            && TryBeginResize())
        {
            return;
        }

        // 标注模式下在选区内按下：开始绘制，不改变选区。
        if (_annotateTool != OverlayAnnotateTool.None && !_selection.IsEmpty)
        {
            if (_selection.Contains(_cursor)) BeginAnnotation();
            return;
        }

        // 普通模式重新框选会改变选区，覆盖层标注随之作废。
        _overlayAnnotations = null;
        RefreshAnnotations();

        _anchor = _cursor;
        _dragging = true;
        _dragStarted = false;
        _shiftRatio = null;

        // Snipaste 式吸附：按下时若悬停在窗口上，先整选该窗口；
        // 只有拖动超过阈值才转为自由框选。
        if (!_hoverWindow.IsEmpty)
        {
            _pendingWindow = _hoverWindow;
            _selection = _pendingWindow;
        }
        else
        {
            _pendingWindow = PixelRect.Empty;
            _selection = PixelRect.Empty;
        }

        UpdateViews();
        UpdateToolbar(forceMagnifier: true);
    }

    public void OnMouseMove()
    {
        if (_completed) return;
        _cursor = MonitorService.GetCursorPosition();

        if (_resizeCorner != RectCorner.None)
        {
            ApplyResize();
            return;
        }

        if (_annotating)
        {
            UpdatePendingAnnotation();
            return;
        }

        if (_dragging)
        {
            if (!_dragStarted && !_pendingWindow.IsEmpty)
            {
                // 窗口吸附的等待阶段：拖动超过阈值才转入自由框选。
                if (Math.Abs(_cursor.X - _anchor.X) < 5 && Math.Abs(_cursor.Y - _anchor.Y) < 5)
                {
                    UpdateToolbar();
                    return;
                }

                _dragStarted = true;
                _pendingWindow = PixelRect.Empty;
            }

            AspectRatio dragRatio = ResolveDragRatio();
            _selection = dragRatio.IsFree
                ? PixelRect.FromCorners(_anchor.X, _anchor.Y, _cursor.X, _cursor.Y).ClampTo(_virtualBounds)
                : dragRatio.FromDrag(_anchor, _cursor, _virtualBounds);

            if (_selection.Width < 2 || _selection.Height < 2) _selection = PixelRect.Empty;
            UpdateViews();
        }
        else
        {
            UpdateHoverWindow();
            UpdateResizeCursor();
        }

        UpdateToolbar();
    }

    public void OnMouseUp()
    {
        if (_completed) return;
        if (_resizeCorner != RectCorner.None)
        {
            _resizeCorner = RectCorner.None;
            _cursor = MonitorService.GetCursorPosition();
            UpdateViews();
            UpdateResizeCursor();
            UpdateToolbar(forceMagnifier: true);
            return;
        }

        if (_annotating)
        {
            CommitPendingAnnotation();
            return;
        }

        if (!_dragging) return;
        _dragging = false;
        _shiftRatio = null;
        _pendingWindow = PixelRect.Empty;
        _cursor = MonitorService.GetCursorPosition();

        if (_selection.IsEmpty)
        {
            var monitor = MonitorService.FromPoint(_cursor) ?? _snapshot.Monitors[0];
            _selection = monitor.Bounds;
        }

        UpdateViews();
        UpdateToolbar(forceMagnifier: true);
    }

    /// <summary>悬停在窗口上时探测并高亮窗口范围（Snipaste 式吸附）。</summary>
    private void UpdateHoverWindow()
    {
        if ((DateTime.UtcNow - _lastHoverProbe).TotalMilliseconds < 100) return;
        _lastHoverProbe = DateTime.UtcNow;

        var hover = SelectionWindowProbe.TryFindWindowAt(_cursor, _virtualBounds, out var rect) ? rect : PixelRect.Empty;
        if (hover.Equals(_hoverWindow)) return;

        _hoverWindow = hover;
        UpdateViews();
    }

    /// <summary>光标落在选区四角时开始拖角调整大小（普通模式与「选择」工具下可用）。</summary>
    private bool TryBeginResize()
    {
        var corner = RectResize.HitCorner(_selection, _cursor);
        if (corner == RectCorner.None) return false;

        _resizeCorner = corner;
        _resizeStart = _selection;
        SetCursor(corner is RectCorner.TopLeft or RectCorner.BottomRight ? Cursors.SizeNWSE : Cursors.SizeNESW);
        return true;
    }

    /// <summary>拖角调整选区：对角固定，选区与标注一起对齐；小于 8 px 的中间态不落地。</summary>
    private void ApplyResize()
    {
        var before = _selection;
        var rect = RectResize.FromCorner(_resizeStart, _resizeCorner, _cursor, _virtualBounds);
        if (rect.Equals(before)) return;

        _selection = rect;
        SyncAnnotations(before);
        UpdateViews();
        UpdateToolbar();
    }

    /// <summary>选区尺寸 / 位置变化后，让已画标注继续贴在原来的屏幕位置上。</summary>
    private void SyncAnnotations(PixelRect before)
    {
        if (_overlayAnnotations is null) return;

        int dx = _selection.Left - before.Left;
        int dy = _selection.Top - before.Top;
        if (dx != 0 || dy != 0)
        {
            foreach (var annotation in _overlayAnnotations.Annotations) annotation.Move(-dx, -dy);
        }

        _overlayAnnotations.ResizeCanvas(_selection.Width, _selection.Height);
        RefreshAnnotations();
    }

    /// <summary>光标反馈：四角显示斜向双箭头，其余位置保持十字。</summary>
    private void UpdateResizeCursor()
    {
        var corner = _annotateTool == OverlayAnnotateTool.None
            ? RectResize.HitCorner(_selection, _cursor)
            : RectCorner.None;

        SetCursor(corner switch
        {
            RectCorner.TopLeft or RectCorner.BottomRight => Cursors.SizeNWSE,
            RectCorner.TopRight or RectCorner.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.Cross
        });
    }

    private void SetCursor(Cursor cursor)
    {
        foreach (var window in _windows)
        {
            if (!ReferenceEquals(window.Cursor, cursor)) window.Cursor = cursor;
        }
    }

    /// <summary>
    /// 拖动中按住 Shift 时，以按下 Shift 那一刻的形状比例临时锁定，
    /// 松开后恢复自由比例；已有预设比例时始终以预设为准。
    /// </summary>
    private AspectRatio ResolveDragRatio()
    {
        if (!_effectiveRatio.IsFree) return _effectiveRatio;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            _shiftRatio = null;
            return AspectRatio.Free;
        }

        if (_shiftRatio is null && _selection.Width >= 8 && _selection.Height >= 8)
        {
            _shiftRatio = AspectRatio.ForOriginal(_selection);
        }

        return _shiftRatio ?? AspectRatio.Free;
    }

    /// <summary>
    /// 右键：拖动中只中断本次拖拽；其余情况直接取消本次截图（选区界面不再弹菜单，
    /// 右键菜单只保留在钉图窗口里）。
    /// </summary>
    public void OnRightClick()
    {
        if (_completed) return;
        _toolbar.ClosePopups();

        if (_dragging)
        {
            _dragging = false;
            _dragStarted = false;
            _pendingWindow = PixelRect.Empty;
            _shiftRatio = null;
            _selection = PixelRect.Empty;
            UpdateViews();
            UpdateToolbar(forceMagnifier: true);
            return;
        }

        Cancel();
    }

    /// <summary>清除当前选区但保持截图模式（右键菜单「清除选区」）。</summary>
    public void ClearSelection()
    {
        if (_completed) return;
        _selection = PixelRect.Empty;
        _dragging = false;
        _dragStarted = false;
        _pendingWindow = PixelRect.Empty;
        _shiftRatio = null;
        _overlayAnnotations = null;
        RefreshAnnotations();
        UpdateViews();
        UpdateToolbar(forceMagnifier: true);
    }

    public OverlayAnnotateTool AnnotateTool => _annotateTool;

    public uint AnnotateColor => _annotateColor;

    public BadgeShape BadgeShape => _badgeShape;

    public BadgeFillMode BadgeFillMode => _badgeFillMode;

    /// <summary>图形按钮当前记住的子选项（矩形 / 椭圆 / 直线）。</summary>
    public string ShapeVariant => _shapeVariant;

    /// <summary>箭头按钮当前记住的子选项（箭头 / 直线）。</summary>
    public string ArrowVariant => _arrowVariant;

    /// <summary>工具条切换图形 / 箭头子选项时触发（Id, Variant），供上层写回设置。</summary>
    public event Action<string, string>? VariantChanged;

    /// <summary>设置序号标记外形（圆 / 三角 / 方 / 五边 / 六边 / 心形 / 圆角）。</summary>
    public void SetBadgeShape(BadgeShape shape) => _badgeShape = shape;

    /// <summary>设置序号标记绘制方式（填充 / 边框）。</summary>
    public void SetBadgeFillMode(BadgeFillMode mode) => _badgeFillMode = mode;

    /// <summary>切换覆盖层标注工具；选中当前工具再次点击则回到普通框选模式。</summary>
    public void SetAnnotateTool(OverlayAnnotateTool tool)
    {
        if (_annotateTool == tool && tool != OverlayAnnotateTool.None) tool = OverlayAnnotateTool.None;
        _annotateTool = tool;
        RefreshAnnotations();
        _toolbar.RefreshAnnotateState(_annotateTool, _annotateColor);
    }

    /// <summary>切换图形 / 箭头按钮的子选项：只改下一次绘制的种类，当前工具保持不变。</summary>
    public void SetAnnotateVariant(string id, string variant)
    {
        string normalized = SelectionToolbarItems.NormalizeVariant(id, variant);

        if (id == SelectionToolbarItems.Shape)
        {
            if (_shapeVariant == normalized) return;
            _shapeVariant = normalized;
        }
        else if (id == SelectionToolbarItems.Arrow)
        {
            if (_arrowVariant == normalized) return;
            _arrowVariant = normalized;
        }
        else
        {
            return;
        }

        _toolbar.RefreshVariantState(_shapeVariant, _arrowVariant);
        VariantChanged?.Invoke(id, normalized);
    }

    /// <summary>循环切换预设标注色（珊瑚 / 黄 / 绿 / 蓝 / 白 / 深灰），工具条上滚轮触发。</summary>
    public void CycleAnnotateColor()
    {
        int index = Array.IndexOf(AnnotatePalette, _annotateColor);
        SetAnnotateColor(AnnotatePalette[(index + 1 + AnnotatePalette.Length) % AnnotatePalette.Length]);
    }

    /// <summary>取色模式：直接指定标注色（工具条颜色按钮弹系统取色对话框）。</summary>
    public void SetAnnotateColor(uint argb)
    {
        _annotateColor = argb;
        _toolbar.RefreshAnnotateState(_annotateTool, _annotateColor);
    }

    /// <summary>覆盖层标注是否还有可回退的步骤。</summary>
    public bool CanUndoAnnotations => _overlayAnnotations?.CanUndo == true;

    /// <summary>覆盖层标注是否有被撤销、可重新前进的步骤。</summary>
    public bool CanRedoAnnotations => _overlayAnnotations?.CanRedo == true;

    /// <summary>回退一步标注（工具条「回退」按钮与 Ctrl+Z）。</summary>
    public void UndoAnnotations() => ApplyAnnotationHistory(undo: true);

    /// <summary>前进一步标注（工具条「前进」按钮与 Ctrl+Y / Ctrl+Shift+Z）。</summary>
    public void RedoAnnotations() => ApplyAnnotationHistory(undo: false);

    private void ApplyAnnotationHistory(bool undo)
    {
        if (_overlayAnnotations is null) return;
        if (undo ? !_overlayAnnotations.CanUndo : !_overlayAnnotations.CanRedo) return;

        if (undo) _overlayAnnotations.Undo();
        else _overlayAnnotations.Redo();

        RefreshAnnotations();
        UpdateToolbar();
    }

    private AnnotationStyle CurrentAnnotateStyle() => new(_annotateColor, _strokeWidth, 1);

    private PointD DocPoint(PixelPoint point) => new(point.X - _selection.Left, point.Y - _selection.Top);

    /// <summary>标注文档跟随当前选区：没有就新建，已有则只同步画布尺寸（不再丢标注）。</summary>
    private void EnsureAnnotations()
    {
        if (_overlayAnnotations is null)
        {
            _overlayAnnotations = new AnnotationDocument(_selection.Width, _selection.Height);
            return;
        }

        _overlayAnnotations.ResizeCanvas(_selection.Width, _selection.Height);
    }

    private void BeginAnnotation()
    {
        EnsureAnnotations();
        var point = DocPoint(_cursor);

        if (_annotateTool == OverlayAnnotateTool.Text)
        {
            ShowTextPopup(point);
            return;
        }

        if (_annotateTool == OverlayAnnotateTool.Number)
        {
            // 序号标记：点击即放置，编号自动递增，无需拖动。
            _overlayAnnotations!.Add(new NumberBadgeAnnotation
            {
                Number = _overlayAnnotations.NextNumber,
                Center = point,
                FontSize = 16,
                Shape = _badgeShape,
                FillMode = _badgeFillMode,
                Style = CurrentAnnotateStyle()
            });
            RefreshAnnotations();
            return;
        }

        // 图形 / 箭头按记住的子选项决定画什么：直线用无端头的箭头表示。
        var (kind, arrowHead) = _annotateTool switch
        {
            OverlayAnnotateTool.Shape => _shapeVariant switch
            {
                SelectionToolbarItems.VariantEllipse => (AnnotationKind.Ellipse, ArrowHead.End),
                SelectionToolbarItems.VariantLine => (AnnotationKind.Arrow, ArrowHead.None),
                _ => (AnnotationKind.Rectangle, ArrowHead.End)
            },
            OverlayAnnotateTool.Arrow => _arrowVariant == SelectionToolbarItems.VariantLine
                ? (AnnotationKind.Arrow, ArrowHead.None)
                : (AnnotationKind.Arrow, ArrowHead.End),
            OverlayAnnotateTool.Mosaic => (AnnotationKind.Mosaic, ArrowHead.End),
            _ => (AnnotationKind.Pen, ArrowHead.End)
        };

        var style = kind == AnnotationKind.Mosaic
            ? CurrentAnnotateStyle().WithWidth(_mosaicWidth)
            : CurrentAnnotateStyle();

        _pendingShape = new ShapeAnnotation(kind, new[] { point, point }, style) { ArrowHead = arrowHead };
        _annotating = true;
        _overlayAnnotations!.Add(_pendingShape);
        RefreshAnnotations();
    }

    private void UpdatePendingAnnotation()
    {
        if (_pendingShape is null) return;
        var point = DocPoint(_cursor);
        var last = _pendingShape.Points[^1];

        if (_pendingShape.IsPolyline)
        {
            if (point.DistanceTo(last) >= 2) _pendingShape.Points.Add(point);
        }
        else
        {
            _pendingShape.Points[^1] = point;
        }

        RefreshAnnotations();
    }

    private void CommitPendingAnnotation()
    {
        _annotating = false;
        _pendingShape = null;
        RefreshAnnotations();
    }

    /// <summary>
    /// 滚轮：矩形 / 椭圆 / 箭头改已画标注的线宽，文字 / 序号改字号；
    /// 画笔与马赛克是笔刷——只改下一笔的笔刷大小 / 块大小，不触碰已经输出的笔迹。
    /// 光标在选区外（含工具条按钮上）时也能改下一笔笔刷大小，方便连续调整。
    /// </summary>
    public void OnMouseWheel(int delta)
    {
        if (_completed || _selection.IsEmpty) return;
        bool increase = delta > 0;

        // 画笔 / 马赛克工具下：滚轮直接调笔刷大小，不受光标是否在选区内限制。
        if (_annotateTool is OverlayAnnotateTool.Pen or OverlayAnnotateTool.Mosaic)
        {
            AdjustBrushSize(_annotateTool, increase);
            return;
        }

        var annotations = _overlayAnnotations;
        var target = annotations is { Annotations.Count: > 0 } ? annotations.Annotations[^1] : null;

        // 非绘制模式下若最后画的是笔刷笔迹，同样只调笔刷大小，不受光标守卫限制。
        if (target is ShapeAnnotation { Kind: AnnotationKind.Mosaic })
        {
            AdjustBrushSize(OverlayAnnotateTool.Mosaic, increase);
            return;
        }

        if (target is ShapeAnnotation { Kind: AnnotationKind.Pen or AnnotationKind.Highlight })
        {
            AdjustBrushSize(OverlayAnnotateTool.Pen, increase);
            return;
        }

        // 其余标注只在光标位于选区内时微调；没有标注则不处理。
        if (target is null) return;
        _cursor = MonitorService.GetCursorPosition();
        if (!_selection.Contains(_cursor)) return;

        // 矩形 / 椭圆 / 箭头改线宽（箭头端头随线宽缩放），文字 / 序号改字号。
        if (!AnnotationEditing.AdjustOnWheel(target, increase)) return;
        if (target is ShapeAnnotation { Kind: AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Arrow } shape)
        {
            _strokeWidth = shape.Style.Width;
        }

        RefreshAnnotations();
    }

    /// <summary>当前画笔笔刷线宽 / 马赛克块大小对应的线宽（滚轮调整的是它们，不是已画笔迹）。</summary>
    public double CurrentStrokeWidth => _strokeWidth;
    public double CurrentMosaicWidth => _mosaicWidth;

    /// <summary>下一笔笔刷大小变化：(笔头类型, 线宽, 是否马赛克)。工具条据此显示当前尺寸。</summary>
    public event Action<OverlayAnnotateTool, double, bool>? BrushSizeChanged;

    /// <summary>只改下一笔的笔刷大小：画笔用线宽状态，马赛克用独立的块大小状态。</summary>
    public void AdjustBrushSize(OverlayAnnotateTool tool, bool increase)
    {
        if (tool is not (OverlayAnnotateTool.Pen or OverlayAnnotateTool.Mosaic)) return;

        bool mosaic = tool == OverlayAnnotateTool.Mosaic;
        double current = mosaic ? _mosaicWidth : _strokeWidth;
        double updated = AnnotationEditing.AdjustBrushWidth(current, mosaic, increase);
        if (Math.Abs(updated - current) < 0.001) return;

        if (mosaic) _mosaicWidth = updated;
        else _strokeWidth = updated;

        BrushSizeChanged?.Invoke(tool, updated, mosaic);
    }

    private void ShowTextPopup(PointD point)
    {
        if (_textPopup is not null) return;
        _textPopup = new AnnotationTextPopup(point, CurrentAnnotateStyle(), _textFontFamily);
        _textPopup.Committed += text =>
        {
            _textPopup = null;
            if (string.IsNullOrWhiteSpace(text) || _completed) return;
            EnsureAnnotations();
            _overlayAnnotations!.Add(new TextAnnotation
            {
                Text = text,
                Origin = point,
                FontSize = 20,
                FontFamily = _textFontFamily,
                Style = CurrentAnnotateStyle()
            });
            RefreshAnnotations();
        };
        _textPopup.Closed += (_, _) => { if (_textPopup is not null) _textPopup = null; };

        _textPopup.Show();
        WindowPlacement.MoveTo(_textPopup, _cursor.X, _cursor.Y);
        _textPopup.Activate();
        _textPopup.FocusInput();
    }

    private void RefreshAnnotations()
    {
        foreach (var window in _windows)
        {
            window.UpdateAnnotations(_overlayAnnotations, _selection);
        }

        // 回退 / 前进按钮的可用状态跟着标注历史走。
        _toolbar.RefreshHistoryState(CanUndoAnnotations, CanRedoAnnotations);
    }

    public void OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (_completed) return;

        switch (key)
        {
            case Key.Escape:
                if (_annotateTool != OverlayAnnotateTool.None)
                {
                    SetAnnotateTool(OverlayAnnotateTool.None);
                    return;
                }

                Cancel();
                return;
            case Key.Enter:
                if (!_selection.IsEmpty) Complete(SelectionAction.Edit);
                return;
            case Key.Z when (modifiers & ModifierKeys.Control) != 0 && (modifiers & ModifierKeys.Shift) != 0:
            case Key.Y when (modifiers & ModifierKeys.Control) != 0:
                RedoAnnotations();
                return;
            case Key.Z when (modifiers & ModifierKeys.Control) != 0:
                UndoAnnotations();
                return;
            case Key.C when (modifiers & ModifierKeys.Control) != 0:
                if (!_selection.IsEmpty) Complete(SelectionAction.Copy);
                return;
            case Key.S when (modifiers & ModifierKeys.Control) != 0:
                if (!_selection.IsEmpty) Complete(SelectionAction.Save);
                return;
        }

        if (_selection.IsEmpty) return;
        int step = (modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        var moved = _selection;
        switch (key)
        {
            case Key.Left:
                moved = moved.Offset(-step, 0);
                break;
            case Key.Right:
                moved = moved.Offset(step, 0);
                break;
            case Key.Up:
                moved = moved.Offset(0, -step);
                break;
            case Key.Down:
                moved = moved.Offset(0, step);
                break;
            default:
                return;
        }

        var before = _selection;
        _selection = moved.ClampTo(_virtualBounds);
        SyncAnnotations(before);
        UpdateViews();
        UpdateToolbar();
    }

    public void SetRatio(AspectRatio ratio)
    {
        _requestedRatio = ratio;
        _effectiveRatio = ratio.Label == AspectRatio.Original.Label && !_selection.IsEmpty
            ? AspectRatio.ForOriginal(_selection)
            : ratio == AspectRatio.Original ? AspectRatio.Free : ratio;

        if (!_selection.IsEmpty && !_effectiveRatio.IsFree)
        {
            var before = _selection;
            _selection = _effectiveRatio.FitCentered(_selection, _virtualBounds);
            SyncAnnotations(before);
        }

        UpdateViews();
        UpdateToolbar();
    }

    public void RequestAction(SelectionAction action)
    {
        if (_completed) return;
        if (action == SelectionAction.Cancel)
        {
            Cancel();
            return;
        }

        if (_selection.IsEmpty) return;
        Complete(action);
    }

    public void Cancel()
    {
        if (_completed) return;
        _completed = true;
        CloseAll();
        _completion.TrySetResult(null);
    }

    private void Complete(SelectionAction action)
    {
        if (_completed) return;
        _completed = true;
        var result = new SelectionResult { Area = _selection, Action = action, Annotations = _overlayAnnotations };

        if (action == SelectionAction.Save)
        {
            // 保存对话框打开期间保持截图状态：选区、遮罩与标注继续显示，
            // 只收起工具条 / 菜单 / 放大镜；对话框结束（保存或取消）后由
            // CaptureWorkflow 的 using 触发 Dispose 关闭覆盖层。
            SuspendFloaters();
        }
        else
        {
            CloseAll();
        }

        _completion.TrySetResult(result);
    }

    /// <summary>收起覆盖层上的浮动窗口（工具条 / 放大镜 / 文字输入框），保留选区与标注画面。</summary>
    private void SuspendFloaters()
    {
        try
        {
            _toolbar.SetShown(false);
            _magnifier.SetShown(false);
            _textPopup?.Close();
            _textPopup = null;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>另存为对话框即将弹出：取消覆盖层置顶，让对话框压在截图画面之上。</summary>
    public void BeginSaveDialog()
    {
        foreach (var window in _windows)
        {
            window.Topmost = false;
        }
    }

    private void CloseAll()
    {
        try
        {
            // 应用是 OnExplicitShutdown，Hide 会让这两个 Window 永远留在 Application.Windows 里，
            // 每次截图泄漏两个窗口；这里必须 Close。
            _toolbar.Close();
            _magnifier.Close();
            _textPopup?.Close();
            _textPopup = null;
            foreach (var window in _windows) window.Close();
            _windows.Clear();
        }
        catch (Exception)
        {
        }
    }

    private void UpdateViews()
    {
        foreach (var window in _windows)
        {
            window.UpdateSelection(_selection);
            window.UpdateHover(_selection.IsEmpty && !_dragging ? _hoverWindow : PixelRect.Empty);
        }
    }

    private void UpdateToolbar(bool forceMagnifier = false)
    {
        bool hasGap = HasUncoveredArea();
        string ratioLabel = DescribeRatio(_selection, _requestedRatio);
        PixelRect infoRect = !_selection.IsEmpty ? _selection : _hoverWindow;
        _toolbar.RefreshRatio(_requestedRatio);
        _magnifier.UpdateInfo(infoRect, _cursor, ratioLabel, hasGap, _pixelColor);

        // 放大镜只在框选过程中出现，其余时间把屏幕让给截图内容。
        if (_dragging && (forceMagnifier || _cursor != _lastMagnifierCursor || (DateTime.UtcNow - _lastMagnifierUpdate).TotalMilliseconds > 80))
        {
            _lastMagnifierCursor = _cursor;
            _lastMagnifierUpdate = DateTime.UtcNow;
            try
            {
                var zoom = ImageUtil.CreateMagnifier(_snapshot, _cursor, SelectionMagnifierWindow.Cells, SelectionMagnifierWindow.CellSize);
                var pixel = ScreenGrabber.GetPixelColor(_snapshot, _cursor);
                _pixelColor = Color.FromRgb(pixel.R, pixel.G, pixel.B);
                _magnifier.UpdateZoom(zoom);
                _magnifier.UpdateInfo(infoRect, _cursor, ratioLabel, hasGap, _pixelColor);
            }
            catch (Exception)
            {
            }
        }

        PlaceToolbar();
        PlaceMagnifier();
    }

    /// <summary>
    /// 工具条（参考 Snipaste）：贴选区右下角、右缘对齐选区右缘；
    /// 下方放不下时翻到选区上方；无选区时隐藏，等松开鼠标后再出现。
    /// </summary>
    private void PlaceToolbar()
    {
        if (_selection.IsEmpty)
        {
            _toolbar.SetShown(false);
            _toolbarRect = PixelRect.Empty;
            return;
        }

        _toolbar.SetShown(true);
        _toolbar.UpdateLayout();

        var monitor = MonitorService.FromPoint(_selection.Center) ?? _snapshot.Monitors[0];
        var work = monitor.WorkArea;
        var (width, height) = WindowPlacement.GetPixelSize(_toolbar);
        if (width <= 0 || height <= 0) return;

        int x = _selection.Right - width;
        int y = _selection.Bottom + 8;
        if (y + height > work.Bottom - 4) y = _selection.Top - height - 8;
        if (y < work.Top + 4) y = Math.Min(work.Bottom - height - 4, _selection.Bottom - height - 8);

        x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
        y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
        WindowPlacement.MoveTo(_toolbar, x, y);
        _toolbarRect = WindowPlacement.GetPixelBounds(_toolbar);
    }

    /// <summary>
    /// 放大镜贴着光标移动：默认落在光标右下，靠边时依次翻转到左、上，
    /// 并避开工具条；光标落在工具条上时隐藏，避免遮住按钮。
    /// </summary>
    private void PlaceMagnifier()
    {
        // 仅在按住鼠标框选截图范围时显示放大镜。
        if (_completed || !_dragging)
        {
            _magnifier.SetShown(false);
            return;
        }

        if (!_toolbarRect.IsEmpty && _toolbarRect.Inflate(2).Contains(_cursor))
        {
            _magnifier.SetShown(false);
            return;
        }

        _magnifier.SetShown(true);
        WindowPlacement.Raise(_magnifier);
        var (width, height) = WindowPlacement.GetPixelSize(_magnifier);
        if (width <= 0 || height <= 0) return;

        var monitor = MonitorService.FromPoint(_cursor) ?? _snapshot.Monitors[0];
        var work = monitor.WorkArea;
        const int gap = 18;
        var candidates = new[]
        {
            new PixelRect(_cursor.X + gap, _cursor.Y + gap, width, height),
            new PixelRect(_cursor.X - gap - width, _cursor.Y + gap, width, height),
            new PixelRect(_cursor.X + gap, _cursor.Y - gap - height, width, height),
            new PixelRect(_cursor.X - gap - width, _cursor.Y - gap - height, width, height)
        };

        var chosen = candidates[0].ClampTo(work);
        foreach (var candidate in candidates)
        {
            var placed = candidate.ClampTo(work);
            chosen = placed;
            if (_toolbarRect.IsEmpty || !placed.IntersectsWith(_toolbarRect)) break;
        }

        WindowPlacement.MoveTo(_magnifier, chosen.Left, chosen.Top);
    }

    private bool HasUncoveredArea()
    {
        if (_selection.IsEmpty) return false;
        long covered = 0;
        foreach (var monitor in _snapshot.Monitors)
        {
            covered += PixelRect.Intersect(monitor.Bounds, _selection).Area;
        }

        return covered < _selection.Area;
    }

    private void FocusCursorWindow()
    {
        var cursor = MonitorService.GetCursorPosition();
        foreach (var window in _windows)
        {
            if (!window.Monitor.Bounds.Contains(cursor)) continue;
            window.Activate();
            window.Focus();
            return;
        }

        if (_windows.Count > 0)
        {
            _windows[0].Activate();
            _windows[0].Focus();
        }
    }

    private static string DescribeRatio(PixelRect rect, AspectRatio requested)
    {
        if (rect.IsEmpty) return requested.IsFree ? L.T("自由") : requested.Label;
        if (!requested.IsFree && requested.Label != AspectRatio.Original.Label) return requested.Label;

        int gcd = Gcd(rect.Width, rect.Height);
        if (gcd > 0)
        {
            int w = rect.Width / gcd;
            int h = rect.Height / gcd;
            if (w <= 64 && h <= 64) return $"{w}:{h}";
        }

        return $"{rect.AspectRatio:0.00}:1";
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return Math.Max(1, Math.Abs(a));
    }

    public void Dispose()
    {
        // 无论正常完成还是保存对话框中断，都要确保覆盖层窗口关闭。
        _completed = true;
        CloseAll();
    }
}
