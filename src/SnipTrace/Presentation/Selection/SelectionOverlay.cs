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
    Select,
    Arrow,
    Pen,
    Rectangle,
    Ellipse,
    Text,
    Number
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
    private readonly SelectionContextMenu _contextMenu = new();
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
    private bool _menuOpen;
    private bool _completed;

    // 覆盖层标注（Snipaste 式）：选区内直接绘制，进编辑器可继续精标。
    private static readonly uint[] AnnotatePalette = { 0xFFFF6B5F, 0xFFFFCC00, 0xFF59E0BE, 0xFF4C8DFF, 0xFFF2F6FC, 0xFF1F2733 };
    private OverlayAnnotateTool _annotateTool = OverlayAnnotateTool.None;
    private AnnotationDocument? _overlayAnnotations;
    private ShapeAnnotation? _pendingShape;
    private AnnotationTextPopup? _textPopup;
    private int _paletteIndex;
    private double _strokeWidth = 3;
    private bool _annotating;
    private Annotation? _selectedAnnotation;
    private Annotation? _dragAnnotation;
    private PointD _dragLast;
    private BadgeShape _badgeShape = BadgeShape.Circle;
    private BadgeFillMode _badgeFillMode = BadgeFillMode.Fill;

    public SelectionOverlay(DesktopSnapshot snapshot)
    {
        _snapshot = snapshot;
        _virtualBounds = snapshot.Bounds;
        _cursor = MonitorService.GetCursorPosition();
        _toolbar.Attach(this);
        _contextMenu.Attach(this);
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
        HideContextMenu();
        _toolbar.ClosePopups();

        if (doubleClick && !_selection.IsEmpty)
        {
            Complete(SelectionAction.Copy);
            return;
        }

        // 标注模式下在选区内按下：开始绘制 / 开始拖动已选标注，不改变选区。
        if (_annotateTool != OverlayAnnotateTool.None && !_selection.IsEmpty)
        {
            if (_selection.Contains(_cursor))
            {
                if (_annotateTool == OverlayAnnotateTool.Select) BeginSelectDrag();
                else BeginAnnotation();
            }

            return;
        }

        // 普通模式重新框选会改变选区，覆盖层标注随之作废。
        _overlayAnnotations = null;
        _selectedAnnotation = null;
        _dragAnnotation = null;
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

        if (_dragAnnotation is not null)
        {
            UpdateAnnotationDrag();
            return;
        }

        if (_annotating)
        {
            UpdatePendingAnnotation();
            return;
        }

        if (_menuOpen)
        {
            // 光标离开菜单即收起；在菜单上移动时不刷新放大镜，保持让位状态。
            var menuBounds = _contextMenu.GetBounds();
            if (menuBounds.IsEmpty || !menuBounds.Inflate(4).Contains(_cursor))
            {
                HideContextMenu();
            }
            else
            {
                return;
            }
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
        }

        UpdateToolbar();
    }

    public void OnMouseUp()
    {
        if (_completed) return;
        if (_dragAnnotation is not null)
        {
            _dragAnnotation = null;
            RefreshAnnotations();
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
    /// 右键（参考 Snipaste）：拖动中中断本次拖拽；无选区退出截图；
    /// 有选区弹出快捷操作菜单。
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

        if (!_selection.IsEmpty)
        {
            ShowContextMenu();
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

    public uint AnnotateColor => AnnotatePalette[_paletteIndex];

    public BadgeShape BadgeShape => _badgeShape;

    public BadgeFillMode BadgeFillMode => _badgeFillMode;

    /// <summary>设置序号标记外形（圆 / 三角 / 方 / 五边 / 六边 / 心形 / 圆角）。</summary>
    public void SetBadgeShape(BadgeShape shape) => _badgeShape = shape;

    /// <summary>设置序号标记绘制方式（填充 / 边框）。</summary>
    public void SetBadgeFillMode(BadgeFillMode mode) => _badgeFillMode = mode;

    /// <summary>切换覆盖层标注工具；选中当前工具再次点击则回到普通框选模式。</summary>
    public void SetAnnotateTool(OverlayAnnotateTool tool)
    {
        if (_annotateTool == tool && tool != OverlayAnnotateTool.None) tool = OverlayAnnotateTool.None;
        if (_annotateTool == OverlayAnnotateTool.Select) _selectedAnnotation = null;
        _annotateTool = tool;
        RefreshAnnotations();
        _toolbar.RefreshAnnotateState(_annotateTool, AnnotatePalette[_paletteIndex]);
    }

    /// <summary>循环切换标注颜色（珊瑚 / 黄 / 绿 / 蓝 / 白 / 深灰）。</summary>
    public void CycleAnnotateColor()
    {
        _paletteIndex = (_paletteIndex + 1) % AnnotatePalette.Length;
        _toolbar.RefreshAnnotateState(_annotateTool, AnnotatePalette[_paletteIndex]);
    }

    private AnnotationStyle CurrentAnnotateStyle() => new(AnnotatePalette[_paletteIndex], _strokeWidth, 1);

    private PointD DocPoint(PixelPoint point) => new(point.X - _selection.Left, point.Y - _selection.Top);

    private void EnsureAnnotations()
    {
        if (_overlayAnnotations is null
            || _overlayAnnotations.ImageWidth != _selection.Width
            || _overlayAnnotations.ImageHeight != _selection.Height)
        {
            _overlayAnnotations = new AnnotationDocument(_selection.Width, _selection.Height);
        }
    }

    /// <summary>「选择」工具：点中图形 / 文字 / 笔迹即选中，并在拖动时整体平移。</summary>
    private void BeginSelectDrag()
    {
        if (_overlayAnnotations is null || _overlayAnnotations.Annotations.Count == 0)
        {
            _selectedAnnotation = null;
            RefreshAnnotations();
            return;
        }

        var point = DocPoint(_cursor);
        _selectedAnnotation = FindAnnotation(point);
        _dragAnnotation = _selectedAnnotation;
        _dragLast = point;
        RefreshAnnotations();
    }

    private void UpdateAnnotationDrag()
    {
        if (_dragAnnotation is null) return;
        var point = DocPoint(_cursor);
        double dx = point.X - _dragLast.X;
        double dy = point.Y - _dragLast.Y;
        if (dx == 0 && dy == 0) return;

        _dragAnnotation.Move(dx, dy);
        _dragLast = point;
        RefreshAnnotations();
    }

    /// <summary>自上而下（后画的优先）命中标注。</summary>
    private Annotation? FindAnnotation(PointD point) => AnnotationEditing.HitTest(_overlayAnnotations, point);

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation is null) return;
        _overlayAnnotations?.Remove(_selectedAnnotation);
        _selectedAnnotation = null;
        _dragAnnotation = null;
        RefreshAnnotations();
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

        var kind = _annotateTool switch
        {
            OverlayAnnotateTool.Arrow => AnnotationKind.Arrow,
            OverlayAnnotateTool.Rectangle => AnnotationKind.Rectangle,
            OverlayAnnotateTool.Ellipse => AnnotationKind.Ellipse,
            _ => AnnotationKind.Pen
        };

        _pendingShape = new ShapeAnnotation(kind, new[] { point, point }, CurrentAnnotateStyle());
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
    /// 滚轮微调最后一个标注（选择工具下优先取选中的 / 光标下的标注）：
    /// 矩形、椭圆、箭头改「线宽」；文字、序号改字号；画笔、高亮仍按 10% 缩放。
    /// </summary>
    public void OnMouseWheel(int delta)
    {
        if (_completed || _selection.IsEmpty) return;
        _cursor = MonitorService.GetCursorPosition();
        if (!_selection.Contains(_cursor)) return;

        var annotations = _overlayAnnotations;
        if (annotations is null || annotations.Annotations.Count == 0) return;

        var target = _annotateTool == OverlayAnnotateTool.Select ? FindAnnotation(DocPoint(_cursor)) : null;
        target ??= _selectedAnnotation;
        if (target is null || !annotations.Annotations.Contains(target)) target = annotations.Annotations[^1];

        // 矩形 / 椭圆 / 箭头改线宽（箭头端头随线宽缩放），文字 / 序号改字号，画笔 / 高亮等比缩放。
        if (!AnnotationEditing.AdjustOnWheel(target, delta > 0)) return;

        // 线宽记住下来，后续新画的图形 / 箭头沿用。
        if (target is ShapeAnnotation { Kind: AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Arrow } shape)
        {
            _strokeWidth = shape.Style.Width;
        }

        RefreshAnnotations();
    }

    private void ShowTextPopup(PointD point)
    {
        if (_textPopup is not null) return;
        _textPopup = new AnnotationTextPopup(point, CurrentAnnotateStyle());
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
        var selected = _selectedAnnotation is not null && _overlayAnnotations is not null
            && _overlayAnnotations.Annotations.Contains(_selectedAnnotation)
                ? _selectedAnnotation
                : null;

        foreach (var window in _windows)
        {
            window.UpdateAnnotations(_overlayAnnotations, _selection, selected);
        }
    }

    private void ShowContextMenu()
    {
        _menuOpen = true;
        _magnifier.SetShown(false);
        _contextMenu.ShowMenu(_cursor);

        // 新建的置顶窗口会把同层窗口挤到覆盖层后面，重新提回置顶层。
        if (_toolbar.IsVisible) WindowPlacement.Raise(_toolbar);
        WindowPlacement.Raise(_contextMenu);
    }

    private void HideContextMenu()
    {
        if (!_menuOpen) return;
        _menuOpen = false;
        _contextMenu.HideMenu();
    }

    public void OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (_completed) return;

        switch (key)
        {
            case Key.Escape:
                if (_menuOpen)
                {
                    HideContextMenu();
                    return;
                }

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
            case Key.C when (modifiers & ModifierKeys.Control) != 0:
                if (!_selection.IsEmpty) Complete(SelectionAction.Copy);
                return;
            case Key.S when (modifiers & ModifierKeys.Control) != 0:
                if (!_selection.IsEmpty) Complete(SelectionAction.Save);
                return;
            case Key.Delete or Key.Back when _selectedAnnotation is not null:
                DeleteSelectedAnnotation();
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

        _selection = moved.ClampTo(_virtualBounds);
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
            _selection = _effectiveRatio.FitCentered(_selection, _virtualBounds);
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

    /// <summary>收起覆盖层上的浮动窗口（工具条 / 菜单 / 放大镜 / 文字输入框），保留选区与标注画面。</summary>
    private void SuspendFloaters()
    {
        try
        {
            HideContextMenu();
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
            HideContextMenu();
            _toolbar.Hide();
            _magnifier.SetShown(false);
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
        if (rect.IsEmpty) return requested.IsFree ? "自由" : requested.Label;
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
