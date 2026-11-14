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
    Text,
    Number
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

    public AnnotationCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        _checkerBrush = CreateCheckerBrush();
    }

    public event EventHandler? ZoomChanged;

    public event EventHandler? SelectionChanged;

    public event Action<TextAnnotation>? TextEditRequested;

    public BitmapSource? Image { get; set; }

    public EditorTool Tool { get; set; } = EditorTool.Select;

    public AnnotationStyle CurrentStyle { get; set; } = AnnotationStyle.Default;

    public double Zoom => _zoom;

    public IReadOnlyList<Annotation> Selection => _selection;

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

    public void ZoomToFit()
    {
        if (Image is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        double scaleX = (ActualWidth - 40) / Image.PixelWidth;
        double scaleY = (ActualHeight - 40) / Image.PixelHeight;
        _zoom = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);
        CenterImage();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
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
        _document.EndEdit();
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    private void Document_Changed(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(_checkerBrush, null, new Rect(RenderSize));
        if (Image is null || _document is null) return;

        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawImage(Image, new Rect(_offset.X, _offset.Y, Image.PixelWidth * _zoom, Image.PixelHeight * _zoom));

        dc.PushTransform(new TranslateTransform(_offset.X, _offset.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        foreach (var annotation in _document.InRenderOrder) AnnotationRenderer.Draw(dc, annotation);
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
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 107, 95)), 1) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        var handleFill = Brushes.White;
        var handlePen = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 107, 95)), 1);
        handlePen.Freeze();

        foreach (var annotation in _selection)
        {
            var screenRect = ToScreenRect(annotation.Bounds);
            dc.DrawRectangle(null, pen, screenRect);

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

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Image is null) return;
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

        if (Tool == EditorTool.Select)
        {
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

            var hit = _document.HitTest(_dragStartImage);
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
                Style = CurrentStyle,
                Text = string.Empty
            };
            TextEditRequested?.Invoke(text);
            e.Handled = true;
            return;
        }

        var kind = Tool switch
        {
            EditorTool.Rectangle => AnnotationKind.Rectangle,
            EditorTool.Ellipse => AnnotationKind.Ellipse,
            EditorTool.Arrow => AnnotationKind.Arrow,
            EditorTool.Highlight => AnnotationKind.Highlight,
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

        switch (_dragMode)
        {
            case DragMode.Create when _creating is not null:
            {
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
                break;
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
            if (bounds.Width < 3 && bounds.Height < 3)
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
        else if (_dragMode is DragMode.Move or DragMode.Resize)
        {
            _document.EndEdit(_interactionChanged);
        }

        _dragMode = DragMode.None;
        _dragTarget = null;
        _creating = null;
        _handleIndex = -1;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void UpdateCursor(Point screen)
    {
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

        if (Tool != EditorTool.Select)
        {
            Cursor = Cursors.Cross;
            return;
        }

        Cursor = _document?.HitTest(new PixelPoint((int)ToImage(screen).X, (int)ToImage(screen).Y)) is not null
            ? Cursors.SizeAll
            : Cursors.Arrow;
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
        Create
    }
}
