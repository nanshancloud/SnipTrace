using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Editing;

public enum EditorTool
{
    Select,
    Rectangle,
    Ellipse,
    Arrow,
    Pen,
    Highlight,

    /// <summary>马赛克笔刷：沿拖动轨迹打码，块大小跟随线宽。</summary>
    Mosaic,
    Text,
    Number,
    Eraser
}

/// <summary>
/// 画布：图片按 Uniform 显示，标注坐标映射回原始像素，缩放只影响显示。
/// </summary>
public sealed class AnnotationCanvas : FrameworkElement
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 4.0;
    private const double HandleSize = 8;

    private readonly List<Annotation> _selection = new();
    private readonly DrawingBrush _checkerBrush;
    private AnnotationDocument? _document;
    private Point _offset;
    private double _zoom = 1;

    private DragMode _dragMode;
    private Annotation? _dragTarget;
    private ShapeAnnotation? _creating;
    private PixelPoint _dragStartImage;
    private PixelRect _dragStartBounds;
    private int _handleIndex = -1;
    private bool _panning;
    private Point _panStartScreen;
    private Point _panStartOffset;
    private bool _marquee;
    private Point _marqueeStart;
    private Point _marqueeEnd;
    private bool _interactionChanged;

    private ShapeAnnotation? _arrowDraft;
    private int _vertexIndex = -1;
    private bool _erasing;
    private bool _eraseChanged;
    private PointD _lastErasePoint;

    /// <summary>新建文本标注使用的字体（来自设置页「标注字体」）。</summary>
    public string TextFontFamily { get; set; } = AppSettings.DefaultTextFontFamily;

    public AnnotationCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        _checkerBrush = CreateCheckerBrush();
    }

    public event EventHandler? ZoomChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>拖动 / 缩放标注过程中的位置变化（浮动样式条靠它跟随选区）。</summary>
    public event EventHandler? ContentMoved;

    /// <summary>请求编辑文本：(标注, 是否为新建)。</summary>
    public event Action<TextAnnotation, bool>? TextEditRequested;

    public BitmapSource? Image { get; set; }

    public EditorTool Tool { get; set; } = EditorTool.Select;

    public AnnotationStyle CurrentStyle { get; set; } = AnnotationStyle.Default;

    /// <summary>新建箭头使用的端头形式（单向 / 双向 / 无头）。</summary>
    public ArrowHead ArrowHead { get; set; } = ArrowHead.End;

    /// <summary>新建箭头是否按正交方向吸附（直角折线）。</summary>
    public bool ArrowElbow { get; set; }

    /// <summary>新建序号使用的形状（右侧「序号形状」选中后跟随）。</summary>
    public BadgeShape BadgeShape { get; set; } = BadgeShape.Circle;

    /// <summary>新建序号使用的填充方式（填充 / 边框）。</summary>
    public BadgeFillMode BadgeFillMode { get; set; } = BadgeFillMode.Fill;

    /// <summary>是否绘制棋盘格底（钉图不需要，直接透出窗口底色）。</summary>
    public bool ShowCheckerboard { get; set; } = true;

    /// <summary>悬浮即选中：钉图里鼠标移到标注上就显示选框与手柄，方便直接拖动 / 调整。</summary>
    public bool HoverSelect { get; set; }

    /// <summary>悬浮命中的额外容差（px），细线 / 箭头也能轻松悬停选中。</summary>
    public double HoverHitTolerance { get; set; } = 2;

    /// <summary>是否存在尚未结束的多段箭头。</summary>
    public bool HasArrowDraft => _arrowDraft is not null;

    public double Zoom => _zoom;

    public IReadOnlyList<Annotation> Selection => _selection;

    /// <summary>是否显示虚线选框与调整手柄：只有「选择」工具且有选中对象时才显示。</summary>
    public bool ShowsSelectionBox => Tool == EditorTool.Select && _selection.Count > 0;

    public AnnotationDocument? Document
    {
        get => _document;
        set
        {
            if (_document is not null) _document.Changed -= Document_Changed;
            _document = value;
            if (_document is not null) _document.Changed += Document_Changed;
            _selection.Clear();
            InvalidateVisual();
        }
    }

    public void ZoomToFit(double margin = 40)
    {
        if (Image is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        double scaleX = Math.Max(1, ActualWidth - margin) / Image.PixelWidth;
        double scaleY = Math.Max(1, ActualHeight - margin) / Image.PixelHeight;
        _zoom = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);
        CenterImage();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>屏幕坐标命中的标注（后画的优先），钉图用它区分「拖窗口」还是「拖标注」。</summary>
    public Annotation? HitTestAnnotation(Point screen)
    {
        if (_document is null || Image is null) return null;
        var image = ToImage(screen);
        return _document.HitTest(
            new PixelPoint((int)Math.Round(image.X), (int)Math.Round(image.Y)), HoverHitTolerance);
    }

    public void SetZoom(double zoom)
    {
        double clamped = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(clamped - _zoom) < 0.0001) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var imagePoint = ToImage(center);
        _zoom = clamped;
        _offset = new Point(center.X - imagePoint.X * _zoom, center.Y - imagePoint.Y * _zoom);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void CenterImage()
    {
        if (Image is null) return;
        _offset = new Point(
            (ActualWidth - Image.PixelWidth * _zoom) / 2,
            (ActualHeight - Image.PixelHeight * _zoom) / 2);
        InvalidateVisual();
    }

    public Point ToScreen(PointD point) => new(point.X * _zoom + _offset.X, point.Y * _zoom + _offset.Y);

    public PointD ToImage(Point screen) => new((screen.X - _offset.X) / _zoom, (screen.Y - _offset.Y) / _zoom);

    public void SelectAnnotation(Annotation? annotation)
    {
        _selection.Clear();
        if (annotation is not null) _selection.Add(annotation);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void DeleteSelection()
    {
        if (_document is null || _selection.Count == 0) return;
        if (_arrowDraft is not null) FinishArrowDraft(false);
        _document.RemoveRange(_selection.ToList());
        _selection.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ApplyToSelection(Action<Annotation> action, bool updateCurrentStyleOnly = false)
    {
        if (_document is null) return;
        if (_selection.Count == 0 || updateCurrentStyleOnly)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _document.BeginEdit();
        foreach (var annotation in _selection) action(annotation);
        _document.MarkChanged();
        _document.EndEdit();
        InvalidateVisual();
    }

    /// <summary>
    /// 撤销 / 重做之后同步画布状态：丢掉已经不在文档里的选中项（否则会留下过期的虚线框），
    /// 并清理未完成的多段箭头草稿。
    /// </summary>
    public void SyncAfterHistory()
    {
        _arrowDraft = null;
        _creating = null;
        _dragTarget = null;
        _dragMode = DragMode.None;

        if (_document is null)
        {
            _selection.Clear();
            InvalidateVisual();
            return;
        }

        int removed = _selection.RemoveAll(item => !_document.Annotations.Contains(item));
        if (removed > 0) SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    private void Document_Changed(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ShowCheckerboard) dc.DrawRectangle(_checkerBrush, null, new Rect(RenderSize));
        if (Image is null || _document is null) return;

        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawImage(Image, new Rect(_offset.X, _offset.Y, Image.PixelWidth * _zoom, Image.PixelHeight * _zoom));

        dc.PushTransform(new TranslateTransform(_offset.X, _offset.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        foreach (var annotation in _document.InRenderOrder)
        {
            // 画布图像与标注同源（原点都在图像左上角），马赛克直接按图像像素取样。
            if (annotation is ShapeAnnotation { Kind: AnnotationKind.Mosaic } mosaic) AnnotationRenderer.DrawMosaic(dc, Image, mosaic);
            else AnnotationRenderer.Draw(dc, annotation);
        }
        dc.Pop();
        dc.Pop();

        DrawSelections(dc);
        if (_marquee)
        {
            var rect = new Rect(_marqueeStart, _marqueeEnd);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 107, 95)), 1) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 255, 107, 95)), pen, rect);
        }

        dc.Pop();
    }

    private void DrawSelections(DrawingContext dc)
    {
        // 只有「选择」工具才画虚线选框与调整手柄；画笔 / 箭头 / 橡皮绘制时不显示调整框。
        if (!ShowsSelectionBox) return;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 107, 95)), 1) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        var handleFill = Brushes.White;
        var handlePen = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 107, 95)), 1);
        handlePen.Freeze();
        var vertexFill = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        vertexFill.Freeze();
        var vertexPen = new Pen(new SolidColorBrush(Color.FromArgb(255, 78, 161, 255)), 1.4);
        vertexPen.Freeze();

        foreach (var annotation in _selection)
        {
            var screenRect = ToScreenRect(annotation.Bounds);
            dc.DrawRectangle(null, pen, screenRect);

            if (annotation is ShapeAnnotation { Kind: AnnotationKind.Arrow } arrow)
            {
                // 箭头顶点手柄：拖任一端点即可重新定向、拖中间顶点可改折线。
                foreach (var vertex in arrow.Points)
                {
                    dc.DrawEllipse(vertexFill, vertexPen, ToScreen(vertex), 4.5, 4.5);
                }
            }

            if (_selection.Count == 1)
            {
                foreach (var point in HandlePoints(screenRect))
                {
                    dc.DrawRectangle(handleFill, handlePen,
                        new Rect(point.X - HandleSize / 2, point.Y - HandleSize / 2, HandleSize, HandleSize));
                }
            }
        }
    }

    private Rect ToScreenRect(PixelRect rect)
        => new(rect.X * _zoom + _offset.X, rect.Y * _zoom + _offset.Y, rect.Width * _zoom, rect.Height * _zoom);

    private static IEnumerable<Point> HandlePoints(Rect rect)
    {
        double cx = rect.Left + rect.Width / 2;
        double cy = rect.Top + rect.Height / 2;
        yield return new Point(rect.Left, rect.Top);
        yield return new Point(cx, rect.Top);
        yield return new Point(rect.Right, rect.Top);
        yield return new Point(rect.Right, cy);
        yield return new Point(rect.Right, rect.Bottom);
        yield return new Point(cx, rect.Bottom);
        yield return new Point(rect.Left, rect.Bottom);
        yield return new Point(rect.Left, cy);
    }

    /// <summary>笔刷工具下滚轮改下一笔笔刷大小：(是否增大)。其余工具滚轮仍用于画布缩放。</summary>
    public event Action<bool>? BrushWheelChanged;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Image is null) return;

        // 画笔 / 高亮 / 马赛克：滚轮只改下一笔笔刷大小，不缩放画布、不动已画笔迹。
        if (Tool is EditorTool.Pen or EditorTool.Highlight or EditorTool.Mosaic)
        {
            BrushWheelChanged?.Invoke(e.Delta > 0);
            e.Handled = true;
            return;
        }

        var screen = e.GetPosition(this);
        var imagePoint = ToImage(screen);
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _offset = new Point(screen.X - imagePoint.X * _zoom, screen.Y - imagePoint.Y * _zoom);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_document is null || Image is null) return;
        Focus();
        var screen = e.GetPosition(this);
        var image = ToImage(screen);
        _dragStartImage = new PixelPoint((int)Math.Round(image.X), (int)Math.Round(image.Y));

        if (Tool == EditorTool.Eraser)
        {
            // 橡皮：按住拖动擦除画笔 / 高亮笔画；文本、箭头、序号与形状不参与。
            _erasing = true;
            _eraseChanged = false;
            _lastErasePoint = image;
            _document.BeginEdit();
            EraseBetween(image, image);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Arrow)
        {
            if (_arrowDraft is not null)
            {
                AddArrowPoint(image, e.ClickCount);
                e.Handled = true;
                return;
            }

            var arrow = BeginArrowDraft(image);
            if (arrow is null)
            {
                e.Handled = true;
                return;
            }

            _creating = arrow;
            _dragMode = DragMode.Create;
            _interactionChanged = false;
            _selection.Clear();
            _selection.Add(arrow);
            CaptureMouse();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Select)
        {
            // 箭头顶点优先：单击选中后再拖顶点即可重新定向。
            int vertex = HitVertexHandle(screen);
            if (vertex >= 0 && _selection.Count == 1 && _selection[0] is ShapeAnnotation arrow)
            {
                _dragTarget = arrow;
                _vertexIndex = vertex;
                _dragMode = DragMode.Reshape;
                _document.BeginEdit();
                _interactionChanged = false;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            int handle = HitHandle(screen);
            if (handle >= 0 && _selection.Count == 1)
            {
                _handleIndex = handle;
                _dragTarget = _selection[0];
                _dragStartBounds = _dragTarget.Bounds;
                _dragMode = DragMode.Resize;
                _document.BeginEdit();
                CaptureMouse();
                _interactionChanged = false;
                e.Handled = true;
                return;
            }

            // 钉图是「悬浮即选中」，按下时用同样的容差命中，细线不会因为差几像素改去框选。
            var hit = _document.HitTest(_dragStartImage, HoverSelect ? HoverHitTolerance : 0);
            if (hit is TextAnnotation textAnnotation && e.ClickCount == 2)
            {
                // 双击文本进入内容修改。
                _selection.Clear();
                _selection.Add(textAnnotation);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                TextEditRequested?.Invoke(textAnnotation, false);
                e.Handled = true;
                return;
            }

            if (hit is not null)
            {
                bool alreadySelected = _selection.Contains(hit);
                if (!alreadySelected)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _selection.Add(hit);
                    else
                    {
                        _selection.Clear();
                        _selection.Add(hit);
                    }

                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }

                _dragMode = DragMode.Move;
                _dragTarget = hit;
                _dragStartBounds = hit.Bounds;
                _document.BeginEdit();
                _interactionChanged = false;
                CaptureMouse();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            _selection.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            _marquee = true;
            _marqueeStart = screen;
            _marqueeEnd = screen;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Number)
        {
            var badge = new NumberBadgeAnnotation
            {
                Number = _document.NextNumber,
                Center = image,
                FontSize = CurrentStyle.StrokeWidth > 6 ? 24 : 16,
                Shape = BadgeShape,
                FillMode = BadgeFillMode,
                Style = CurrentStyle
            };
            _document.Add(badge);
            _selection.Clear();
            _selection.Add(badge);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Tool == EditorTool.Text)
        {
            var text = new TextAnnotation
            {
                Origin = image,
                FontSize = Math.Max(14, CurrentStyle.StrokeWidth * 5),
                FontFamily = TextFontFamily,
                Style = CurrentStyle,
                Text = string.Empty
            };
            TextEditRequested?.Invoke(text, true);
            e.Handled = true;
            return;
        }

        var kind = Tool switch
        {
            EditorTool.Rectangle => AnnotationKind.Rectangle,
            EditorTool.Ellipse => AnnotationKind.Ellipse,
            EditorTool.Arrow => AnnotationKind.Arrow,
            EditorTool.Highlight => AnnotationKind.Highlight,
            EditorTool.Mosaic => AnnotationKind.Mosaic,
            _ => AnnotationKind.Pen
        };

        var style = CurrentStyle;
        if (kind == AnnotationKind.Highlight)
        {
            style = style.WithWidth(Math.Max(12, style.StrokeWidth * 4));
        }

        _creating = new ShapeAnnotation(kind, new[] { image, image }, style);
        _document.BeginEdit();
        _document.InsertWithoutHistory(_creating);
        _selection.Clear();
        _selection.Add(_creating);
        _dragMode = DragMode.Create;
        _interactionChanged = false;
        CaptureMouse();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_document is null) return;
        var screen = e.GetPosition(this);

        if (_panning)
        {
            _offset = new Point(_panStartOffset.X + (screen.X - _panStartScreen.X), _panStartOffset.Y + (screen.Y - _panStartScreen.Y));
            InvalidateVisual();
            return;
        }

        var image = ToImage(screen);
        var imagePoint = new PixelPoint((int)Math.Round(image.X), (int)Math.Round(image.Y));

        if (_erasing)
        {
            EraseBetween(_lastErasePoint, image);
            _lastErasePoint = image;
            return;
        }

        // 多段箭头：光标移动时实时预览下一段。
        if (_arrowDraft is not null && _dragMode == DragMode.None && _arrowDraft.Points.Count >= 2)
        {
            _arrowDraft.Points[^1] = SnapArrowPoint(_arrowDraft.Points[^2], image, _arrowDraft.Elbow);
            InvalidateVisual();
            return;
        }

        switch (_dragMode)
        {
            case DragMode.Create when _creating is not null:
            {
                if (_creating.Kind == AnnotationKind.Arrow)
                {
                    _creating.Points[1] = SnapArrowPoint(_creating.Points[0], image, _creating.Elbow);
                    _interactionChanged = true;
                    InvalidateVisual();
                    break;
                }

                if (_creating.IsPolyline)
                {
                    var last = _creating.Points[^1];
                    if (Math.Abs(last.X - image.X) + Math.Abs(last.Y - image.Y) > 2)
                    {
                        _creating.Points.Add(image);
                        _interactionChanged = true;
                        InvalidateVisual();
                    }
                }
                else
                {
                    _creating.Points[1] = image;
                    _interactionChanged = true;
                    InvalidateVisual();
                }

                break;
            }
            case DragMode.Move when _dragTarget is not null:
            {
                double dx = image.X - _dragStartImage.X;
                double dy = image.Y - _dragStartImage.Y;
                if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) break;
                foreach (var annotation in _selection) annotation.Move(dx, dy);
                _dragStartImage = imagePoint;
                _dragStartBounds = _dragTarget.Bounds;
                _interactionChanged = true;
                InvalidateVisual();
                break;
            }
            case DragMode.Reshape when _dragTarget is ShapeAnnotation shape
                                      && _vertexIndex >= 0 && _vertexIndex < shape.Points.Count:
            {
                var point = image;
                if (shape.Elbow && shape.Points.Count > 1)
                {
                    int neighbor = _vertexIndex == shape.Points.Count - 1 ? _vertexIndex - 1 : _vertexIndex + 1;
                    if (neighbor >= 0 && neighbor != _vertexIndex)
                    {
                        point = SnapArrowPoint(shape.Points[neighbor], image, true);
                    }
                }

                shape.Points[_vertexIndex] = point;
                _interactionChanged = true;
                InvalidateVisual();
                break;
            }
            case DragMode.Resize when _dragTarget is not null:
            {
                var target = ComputeResizedBounds(_dragStartBounds, _handleIndex, imagePoint);
                if (target.Width >= 8 && target.Height >= 8)
                {
                    _dragTarget.Resize(target);
                    _interactionChanged = true;
                    InvalidateVisual();
                }

                break;
            }
            case DragMode.None:
                UpdateCursor(screen);
                if (HoverSelect && Tool == EditorTool.Select) UpdateHoverSelection(imagePoint);
                break;
        }

        // 拖动改变了标注（移动 / 缩放 / 重定向）：告诉文档这一步有变化，撤销栈才会记录。
        if (_interactionChanged && _document is not null)
        {
            _document.MarkChanged();
            ContentMoved?.Invoke(this, EventArgs.Empty);
        }

        if (_marquee)
        {
            _marqueeEnd = screen;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_document is null) return;

        if (_erasing)
        {
            _erasing = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            _document.EndEdit(_eraseChanged);
            _eraseChanged = false;
            return;
        }

        if (_marquee)
        {
            var rect = ToImageRect(new Rect(_marqueeStart, _marqueeEnd));
            _selection.Clear();
            _selection.AddRange(_document.Annotations.Where(a => !PixelRect.Intersect(a.Bounds, rect).IsEmpty && !rect.IsEmpty));
            _marquee = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        if (_dragMode == DragMode.Create && _creating is not null)
        {
            var bounds = _creating.Bounds;

            if (_creating.Kind == AnnotationKind.Arrow)
            {
                // 只是点击：保留草稿，后续单击继续追加段；真正拖动过：落成一条两点箭头。
                if (!_interactionChanged)
                {
                    _dragMode = DragMode.None;
                    _creating = null;
                    if (IsMouseCaptured) ReleaseMouseCapture();
                    InvalidateVisual();
                    return;
                }

                _arrowDraft = null;
                _document.EndEdit();
            }
            else if (bounds.Width < 3 && bounds.Height < 3)
            {
                _document.RemoveWithoutHistory(_creating);
                _selection.Clear();
                _document.EndEdit(false);
            }
            else
            {
                _document.EndEdit();
            }
        }
        else if (_dragMode is DragMode.Move or DragMode.Resize or DragMode.Reshape)
        {
            _document.EndEdit(_interactionChanged);
        }

        _dragMode = DragMode.None;
        _dragTarget = null;
        _creating = null;
        _handleIndex = -1;
        _vertexIndex = -1;
        _interactionChanged = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _panStartScreen = e.GetPosition(this);
            _panStartOffset = _offset;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && _panning)
        {
            _panning = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_arrowDraft is not null)
        {
            FinishArrowDraft();
            e.Handled = true;
            return;
        }

        base.OnMouseRightButtonDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_arrowDraft is not null && e.Key is Key.Enter or Key.Escape)
        {
            FinishArrowDraft(e.Key == Key.Enter);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void UpdateCursor(Point screen)
    {
        // 非选择工具没有选框手柄，不要给出缩放手势的误导。
        if (Tool != EditorTool.Select)
        {
            Cursor = Cursors.Cross;
            return;
        }

        int handle = HitHandle(screen);
        if (handle >= 0)
        {
            Cursor = handle switch
            {
                0 or 4 => Cursors.SizeNWSE,
                2 or 6 => Cursors.SizeNESW,
                1 or 5 => Cursors.SizeNS,
                _ => Cursors.SizeWE
            };
            return;
        }

        Cursor = _document?.HitTest(new PixelPoint((int)ToImage(screen).X, (int)ToImage(screen).Y)) is not null
            ? Cursors.SizeAll
            : Cursors.Arrow;
    }

    /// <summary>
    /// 钉图：鼠标悬浮到标注上即选中，移开后保留选中（方便接着抓四角手柄调整大小）。
    /// </summary>
    private void UpdateHoverSelection(PixelPoint imagePoint)
    {
        if (_document is null || _selection.Count > 1) return;

        var hover = _document.HitTest(imagePoint, HoverHitTolerance);
        if (hover is null) return;
        if (_selection.Count == 1 && ReferenceEquals(_selection[0], hover)) return;
        SelectAnnotation(hover);
    }

    /// <summary>
    /// 屏幕坐标是否落在当前选中标注的手柄上：钉图用它区分「拖窗口」还是「拖手柄调整」。
    /// </summary>
    public bool IsOverSelectionHandle(Point screen)
        => Tool == EditorTool.Select && _selection.Count == 1
           && (HitHandle(screen) >= 0 || HitVertexHandle(screen) >= 0);

    /// <summary>命中箭头顶点手柄（仅单选箭头时有效）。</summary>
    private int HitVertexHandle(Point screen)
    {
        if (_selection.Count != 1 || _selection[0] is not ShapeAnnotation { Kind: AnnotationKind.Arrow } arrow) return -1;

        for (int i = 0; i < arrow.Points.Count; i++)
        {
            var point = ToScreen(arrow.Points[i]);
            if (Math.Abs(point.X - screen.X) <= HandleSize && Math.Abs(point.Y - screen.Y) <= HandleSize) return i;
        }

        return -1;
    }

    /// <summary>开始一条箭头草稿：先两点（起点 + 预览点），后续单击追加段。</summary>
    internal ShapeAnnotation? BeginArrowDraft(PointD image)
    {
        if (_document is null) return null;

        var arrow = new ShapeAnnotation(AnnotationKind.Arrow, new[] { image, image }, CurrentStyle)
        {
            ArrowHead = ArrowHead,
            Elbow = ArrowElbow
        };

        _document.BeginEdit();
        _document.InsertWithoutHistory(arrow);
        _arrowDraft = arrow;
        return arrow;
    }

    /// <summary>自动化 / 自检入口：以图像坐标推进多段箭头，与鼠标点击同一套逻辑。</summary>
    internal void AppendArrowPoint(PointD image, int clickCount = 1) => AddArrowPoint(image, clickCount);

    /// <summary>自动化 / 自检入口：按图像坐标擦除画笔 / 高亮。</summary>
    internal bool EraseAt(PointD image)
    {
        EraseBetween(image, image);
        return _eraseChanged;
    }

    /// <summary>自动化 / 自检入口：当前草稿的顶点数（无草稿时为 -1）。</summary>
    internal int ArrowDraftPointCount => _arrowDraft?.Points.Count ?? -1;

    /// <summary>多段箭头：单击追加一段；双击或点回上一个已确定的顶点结束。</summary>
    private void AddArrowPoint(PointD image, int clickCount)
    {
        if (_document is null || _arrowDraft is null) return;
        var draft = _arrowDraft;

        // 末尾一点是跟随光标的预览，比较对象必须是它前面的已确定顶点。
        bool backToLast = draft.Points.Count >= 2
            && Math.Abs(draft.Points[^2].X - image.X) + Math.Abs(draft.Points[^2].Y - image.Y) < 2.5;

        if (clickCount >= 2 || backToLast)
        {
            FinishArrowDraft();
            return;
        }

        // 当前预览点转正（直角模式按横 / 竖吸附），再追加新的预览点。
        var point = SnapArrowPoint(draft.Points[^2], image, draft.Elbow);
        draft.Points[^1] = point;
        draft.Points.Add(point);
        InvalidateVisual();
    }

    /// <summary>结束多段箭头；commit=false 表示取消（Esc）。</summary>
    public void FinishArrowDraft(bool commit = true)
    {
        var draft = _arrowDraft;
        if (draft is null || _document is null) return;

        _arrowDraft = null;
        _creating = null;
        _dragMode = DragMode.None;
        _vertexIndex = -1;

        while (draft.Points.Count > 2
               && Math.Abs(draft.Points[^1].X - draft.Points[^2].X) + Math.Abs(draft.Points[^1].Y - draft.Points[^2].Y) < 0.5)
        {
            draft.Points.RemoveAt(draft.Points.Count - 1);
        }

        double length = 0;
        for (int i = 1; i < draft.Points.Count; i++)
        {
            length += draft.Points[i].DistanceTo(draft.Points[i - 1]);
        }

        if (!commit || draft.Points.Count < 2 || length < 3)
        {
            _document.RemoveWithoutHistory(draft);
            _selection.Remove(draft);
            _document.EndEdit(false);
        }
        else
        {
            _document.EndEdit();
        }

        if (IsMouseCaptured) ReleaseMouseCapture();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>正交箭头：把光标吸附成相对上一点的横 / 竖段。</summary>
    private static PointD SnapArrowPoint(PointD previous, PointD cursor, bool elbow)
    {
        if (!elbow) return cursor;
        double dx = Math.Abs(cursor.X - previous.X);
        double dy = Math.Abs(cursor.Y - previous.Y);
        return dx >= dy ? new PointD(cursor.X, previous.Y) : new PointD(previous.X, cursor.Y);
    }

    /// <summary>橡皮：沿拖动轨迹擦掉画笔 / 高亮笔画（文本、箭头、序号不参与）。</summary>
    private void EraseBetween(PointD from, PointD to)
    {
        if (_document is null) return;

        var removed = AnnotationEditing.EraseStrokes(_document, from, to);
        if (removed.Count == 0) return;

        foreach (var hit in removed) _selection.Remove(hit);
        _eraseChanged = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private int HitHandle(Point screen)
    {
        if (_selection.Count != 1) return -1;
        var rect = ToScreenRect(_selection[0].Bounds);
        int index = 0;
        foreach (var point in HandlePoints(rect))
        {
            if (Math.Abs(point.X - screen.X) <= HandleSize && Math.Abs(point.Y - screen.Y) <= HandleSize) return index;
            index++;
        }

        return -1;
    }

    private static PixelRect ComputeResizedBounds(PixelRect start, int handle, PixelPoint current)
    {
        int left = start.Left;
        int top = start.Top;
        int right = start.Right;
        int bottom = start.Bottom;

        switch (handle)
        {
            case 0: left = current.X; top = current.Y; break;
            case 1: top = current.Y; break;
            case 2: right = current.X; top = current.Y; break;
            case 3: right = current.X; break;
            case 4: right = current.X; bottom = current.Y; break;
            case 5: bottom = current.Y; break;
            case 6: left = current.X; bottom = current.Y; break;
            case 7: left = current.X; break;
        }

        return PixelRect.FromLtrb(left, top, right, bottom);
    }

    private PixelRect ToImageRect(Rect rect)
    {
        var a = ToImage(rect.TopLeft);
        var b = ToImage(rect.BottomRight);
        return PixelRect.FromPoints(a, b);
    }

    private static DrawingBrush CreateCheckerBrush()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x18, 0x20)), null, new Rect(0, 0, 16, 16));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2A)), null, new Rect(0, 0, 8, 8));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2A)), null, new Rect(8, 8, 8, 8));
        }

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    private enum DragMode
    {
        None,
        Move,
        Resize,
        Create,
        Reshape
    }
}
