using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>单台显示器的遮罩层：显示冻结帧切片、55% 遮罩与 1 px 选区边框。</summary>
public sealed class SelectionMask : FrameworkElement
{
    private static readonly Brush MaskBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)));
    private static readonly Pen BorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F)), 1));
    private static readonly Brush HandleFill = Brushes.White;

    private BitmapSource? _image;
    private PixelRect _monitorBounds;
    private PixelRect _selection;
    private double _scale = 1;

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

    public double Scale
    {
        get => _scale;
        set
        {
            _scale = Math.Max(0.1, value);
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
            return;
        }

        var local = ToLocal(_selection);
        var selectionRect = new Rect(local.X, local.Y, local.Width, local.Height);
        var geometry = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(selectionRect));
        dc.DrawGeometry(MaskBrush, null, geometry);
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
