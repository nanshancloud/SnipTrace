using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>单台显示器的遮罩层：显示冻结帧切片、55% 遮罩、1 px 选区边框与覆盖层标注。</summary>
public sealed class SelectionMask : FrameworkElement
{
    private static readonly Brush MaskBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)));
    private static readonly Pen BorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F)), 1));
    private static readonly Brush HandleFill = Brushes.White;
    private static readonly Brush HoverFillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x6B, 0x5F)));
    private static readonly Pen HoverPen = Freeze(
        new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F)), 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) });

    private BitmapSource? _image;
    private PixelRect _monitorBounds;
    private PixelRect _selection;
    private PixelRect _hover;
    private double _scale = 1;
    private AnnotationDocument? _annotations;
    private PixelRect _annotationArea;

    public BitmapSource? Image
    {
        get => _image;
        set
        {
            _image = value;
            InvalidateVisual();
        }
    }

    public PixelRect MonitorBounds
    {
        get => _monitorBounds;
        set
        {
            _monitorBounds = value;
            InvalidateVisual();
        }
    }

    public PixelRect Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            InvalidateVisual();
        }
    }

    public PixelRect Hover
    {
        get => _hover;
        set
        {
            if (_hover.Equals(value)) return;
            _hover = value;
            InvalidateVisual();
        }
    }

    public double Scale
    {
        get => _scale;
        set
        {
            _scale = Math.Max(0.1, value);
            InvalidateVisual();
        }
    }

    /// <summary>覆盖层标注文档（选区内物理像素坐标）。</summary>
    public AnnotationDocument? Annotations
    {
        get => _annotations;
        set
        {
            _annotations = value;
            InvalidateVisual();
        }
    }

    /// <summary>标注文档对应的选区（虚拟桌面物理像素坐标）。</summary>
    public PixelRect AnnotationArea
    {
        get => _annotationArea;
        set
        {
            if (_annotationArea.Equals(value)) return;
            _annotationArea = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(RenderSize);
        if (_image is not null)
        {
            dc.DrawImage(_image, full);
        }
        else
        {
            dc.DrawRectangle(Brushes.Black, null, full);
        }

        if (_selection.IsEmpty)
        {
            dc.DrawRectangle(MaskBrush, null, full);

            // Snipaste 式窗口吸附：光标悬停在窗口上时用虚线框提示可整选的范围。
            if (!_hover.IsEmpty)
            {
                var hoverLocal = ToLocal(_hover);
                if (!hoverLocal.IsEmpty)
                {
                    var hoverRect = new Rect(hoverLocal.X, hoverLocal.Y, hoverLocal.Width, hoverLocal.Height);
                    dc.DrawRectangle(HoverFillBrush, null, hoverRect);
                    dc.DrawRectangle(null, HoverPen, hoverRect);
                }
            }

            return;
        }

        var local = ToLocal(_selection);
        var selectionRect = new Rect(local.X, local.Y, local.Width, local.Height);
        var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(selectionRect));
        dc.DrawGeometry(MaskBrush, null, geometry);

        DrawAnnotations(dc, selectionRect);

        dc.DrawRectangle(null, BorderPen, selectionRect);

        double handle = 6;
        foreach (var point in new[]
                 {
                     selectionRect.TopLeft, selectionRect.TopRight,
                     selectionRect.BottomRight, selectionRect.BottomLeft
                 })
        {
            dc.DrawRectangle(HandleFill, BorderPen, new Rect(point.X - handle / 2, point.Y - handle / 2, handle, handle));
        }
    }

    /// <summary>把覆盖层标注（选区内物理像素坐标）映射到本显示器的局部 DIP 坐标绘制。</summary>
    private void DrawAnnotations(DrawingContext dc, Rect selectionRect)
    {
        if (_annotations is null || _annotations.Annotations.Count == 0 || _annotationArea.IsEmpty) return;

        var clipped = PixelRect.Intersect(_annotationArea, _monitorBounds);
        if (clipped.IsEmpty) return;

        var clipLocal = ToLocal(clipped);
        if (clipLocal.IsEmpty) return;

        dc.PushClip(new RectangleGeometry(selectionRect));
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1 / _scale, 1 / _scale));
        group.Children.Add(new TranslateTransform(
            (_annotationArea.Left - _monitorBounds.Left) / _scale,
            (_annotationArea.Top - _monitorBounds.Top) / _scale));
        dc.PushTransform(group);

        foreach (var annotation in _annotations.InRenderOrder)
        {
            AnnotationRenderer.Draw(dc, annotation);
        }

        dc.Pop();
        dc.Pop();
    }

    private Rect ToLocal(PixelRect rect)
    {
        var clipped = PixelRect.Intersect(rect, _monitorBounds);
        if (clipped.IsEmpty) return Rect.Empty;
        return new Rect(
            (clipped.Left - _monitorBounds.Left) / _scale,
            (clipped.Top - _monitorBounds.Top) / _scale,
            clipped.Width / _scale,
            clipped.Height / _scale);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
