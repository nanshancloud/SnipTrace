using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Editing;

/// <summary>标注绘制：画布与导出共用同一套几何，保证所见即所得。</summary>
public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, Annotation annotation)
    {
        if (annotation is TextAnnotation { Text.Length: 0 }) return;

        // 马赛克要读源图像素，必须走 DrawMosaic；直接调 Draw 时什么也不画。
        if (annotation is ShapeAnnotation { Kind: AnnotationKind.Mosaic }) return;

        // 高亮按荧光笔处理：始终半透明，底下文字 / 线条仍然看得见。
        var color = ToColor(annotation.Style.Argb, AnnotationEditing.RenderOpacity(annotation));
        var brush = Freeze(new SolidColorBrush(color));
        var pen = new Pen(brush, annotation.Style.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        switch (annotation)
        {
            case ShapeAnnotation shape:
                DrawShape(dc, shape, pen, brush);
                break;
            case TextAnnotation text:
                DrawText(dc, text, brush);
                break;
            case NumberBadgeAnnotation badge:
                DrawBadge(dc, badge, brush);
                break;
        }
    }

    public static void DrawDocument(DrawingContext dc, BitmapSource? image, AnnotationDocument document)
    {
        if (image is not null)
        {
            dc.DrawImage(image, new Rect(0, 0, document.ImageWidth, document.ImageHeight));
        }

        foreach (var annotation in document.InRenderOrder)
        {
            // 马赛克要读源图像素，单独走一条绘制路径。
            if (annotation is ShapeAnnotation { Kind: AnnotationKind.Mosaic } mosaic) DrawMosaic(dc, image, mosaic);
            else Draw(dc, annotation);
        }
    }

    /// <summary>
    /// 马赛克笔刷：沿笔迹把扫到的方块（按图像像素网格对齐）取源图平均色铺满。
    /// <paramref name="imageOrigin"/> 是图像左上角在标注坐标中的位置：导出与编辑器为 (0,0)，
    /// 覆盖层则为本显示器切片相对选区的偏移，保证两条渲染链路结果一致。
    /// </summary>
    public static void DrawMosaic(DrawingContext dc, BitmapSource? image, ShapeAnnotation shape, PointD imageOrigin = default)
    {
        if (image is null || shape.Points.Count == 0) return;

        int block = shape.MosaicBlockSize;
        int originX = (int)Math.Round(imageOrigin.X);
        int originY = (int)Math.Round(imageOrigin.Y);

        var cells = CollectMosaicCells(shape.Points, originX, originY, block, image.PixelWidth, image.PixelHeight);
        if (cells.Count == 0) return;

        BitmapSource source;
        try
        {
            source = BgraSource(image);
        }
        catch (Exception)
        {
            return;
        }

        // 每个块单独取样：笔迹可能很长很细，按块取比整块外接矩形更省。
        var buffer = new byte[block * block * 4];
        foreach (var (col, row) in cells)
        {
            int left = Math.Max(col * block, 0);
            int top = Math.Max(row * block, 0);
            int right = Math.Min((col + 1) * block, image.PixelWidth);
            int bottom = Math.Min((row + 1) * block, image.PixelHeight);
            int width = right - left;
            int height = bottom - top;
            if (width <= 0 || height <= 0) continue;

            if (!TryAverageColor(source, left, top, width, height, buffer, out var color)) continue;

            // 块坐标是图像像素，加回图像原点才是标注坐标。
            // 多铺 0.5 px，减少非整数缩放下块之间的细缝。
            var brush = Freeze(new SolidColorBrush(color));
            dc.DrawRectangle(brush, null, new Rect(left + originX, top + originY, width + 0.5, height + 0.5));
        }
    }

    /// <summary>
    /// 笔迹扫过的块集合：沿折线按小块步进取样，块按图像像素网格对齐（拖动过程中块边界稳定不抖）。
    /// </summary>
    private static HashSet<(int Col, int Row)> CollectMosaicCells(
        IReadOnlyList<PointD> points, int originX, int originY, int block, int imageWidth, int imageHeight)
    {
        var cells = new HashSet<(int, int)>();
        double radius = block / 2.0;
        double step = Math.Max(2, block / 4.0);

        void Mark(double x, double y)
        {
            int minCol = (int)Math.Floor((x - radius) / block);
            int maxCol = (int)Math.Floor((x + radius) / block);
            int minRow = (int)Math.Floor((y - radius) / block);
            int maxRow = (int)Math.Floor((y + radius) / block);

            for (int row = minRow; row <= maxRow; row++)
            {
                if ((row + 1) * block <= 0 || row * block >= imageHeight) continue;
                for (int col = minCol; col <= maxCol; col++)
                {
                    if ((col + 1) * block <= 0 || col * block >= imageWidth) continue;
                    cells.Add((col, row));
                }
            }
        }

        var prev = new PointD(points[0].X - originX, points[0].Y - originY);
        Mark(prev.X, prev.Y);
        for (int i = 1; i < points.Count; i++)
        {
            var current = new PointD(points[i].X - originX, points[i].Y - originY);
            double dx = current.X - prev.X;
            double dy = current.Y - prev.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(distance / step));
            for (int s = 1; s <= steps; s++)
            {
                Mark(prev.X + dx * s / steps, prev.Y + dy * s / steps);
            }

            prev = current;
        }

        return cells;
    }

    /// <summary>取一个块的平均色（只算图像内交集部分）。</summary>
    private static bool TryAverageColor(
        BitmapSource source, int x, int y, int width, int height, byte[] buffer, out Color color)
    {
        color = default;
        int stride = width * 4;
        try
        {
            source.CopyPixels(new Int32Rect(x, y, width, height), buffer, stride, 0);
        }
        catch (Exception)
        {
            return false;
        }

        long sumB = 0, sumG = 0, sumR = 0;
        for (int row = 0; row < height; row++)
        {
            int offset = row * stride;
            for (int col = 0; col < width; col++)
            {
                sumB += buffer[offset];
                sumG += buffer[offset + 1];
                sumR += buffer[offset + 2];
                offset += 4;
            }
        }

        int count = width * height;
        color = Color.FromRgb((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
        return true;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, BitmapSource> BgraCache = new();

    /// <summary>Bgra32 / Bgr32 直接读；其它格式按图缓存一份转换结果，避免每帧重复转换。</summary>
    private static BitmapSource BgraSource(BitmapSource image)
    {
        if (image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Bgr32) return image;
        return BgraCache.GetValue(image, key =>
        {
            var converted = new FormatConvertedBitmap(key, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        });
    }

    public static BitmapSource Render(BitmapSource image, AnnotationDocument document)
    {
        double width = Math.Max(1, document.ImageWidth);
        double height = Math.Max(1, document.ImageHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, height));
            foreach (var annotation in document.InRenderOrder)
            {
                if (annotation is ShapeAnnotation { Kind: AnnotationKind.Mosaic } mosaic) DrawMosaic(dc, image, mosaic);
                else Draw(dc, annotation);
            }
        }

        var target = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static void DrawShape(DrawingContext dc, ShapeAnnotation shape, Pen pen, Brush brush)
    {
        var points = shape.Points;
        if (points.Count == 0) return;

        switch (shape.Kind)
        {
            case AnnotationKind.Rectangle:
            {
                var rect = RectFromPoints(points[0], points[^1]);
                if (rect.Width > 0 && rect.Height > 0) dc.DrawRectangle(null, pen, rect);
                break;
            }
            case AnnotationKind.Ellipse:
            {
                var rect = RectFromPoints(points[0], points[^1]);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                }

                break;
            }
            case AnnotationKind.Arrow:
                DrawArrow(dc, shape, pen, brush);
                break;
            default:
            {
                if (points.Count == 1)
                {
                    double radius = Math.Max(1.5, shape.Style.Width / 2);
                    dc.DrawEllipse(brush, null, new Point(points[0].X, points[0].Y), radius, radius);
                    break;
                }

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(points[0].X, points[0].Y), false, false);
                    for (int i = 1; i < points.Count; i++) ctx.LineTo(new Point(points[i].X, points[i].Y), true, true);
                }

                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
                break;
            }
        }
    }

    /// <summary>箭头：线身支持任意多段，端头按 <see cref="ShapeAnnotation.ArrowHead"/> 画在首 / 尾。</summary>
    private static void DrawArrow(DrawingContext dc, ShapeAnnotation shape, Pen pen, Brush brush)
    {
        var points = shape.Points;
        if (points.Count < 2) return;

        if (points.Count == 2)
        {
            dc.DrawLine(pen, ToPoint(points[0]), ToPoint(points[1]));
        }
        else
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(ToPoint(points[0]), false, false);
                for (int i = 1; i < points.Count; i++) ctx.LineTo(ToPoint(points[i]), true, true);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        var (startHead, endHead) = ArrowGeometry.Ends(shape.ArrowHead);

        if (endHead && shape.HeadSegment(true) is { } endSegment)
        {
            DrawArrowHead(dc, endSegment.From, endSegment.Tip, shape.Style.Width, brush);
        }

        if (startHead && shape.HeadSegment(false) is { } startSegment)
        {
            DrawArrowHead(dc, startSegment.From, startSegment.Tip, shape.Style.Width, brush);
        }
    }

    /// <summary>端头三角形由 <see cref="ArrowGeometry"/> 统一计算，这里只负责描到画布上。</summary>
    private static void DrawArrowHead(DrawingContext dc, PointD from, PointD tip, double strokeWidth, Brush brush)
    {
        var triangle = ArrowGeometry.HeadTriangle(from, tip, strokeWidth);
        if (triangle.Count < 3) return;

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(ToPoint(triangle[0]), true, true);
            ctx.LineTo(ToPoint(triangle[1]), true, true);
            ctx.LineTo(ToPoint(triangle[2]), true, true);
        }

        head.Freeze();
        dc.DrawGeometry(brush, null, head);
    }

    private static Point ToPoint(PointD point) => new(point.X, point.Y);

    private static void DrawText(DrawingContext dc, TextAnnotation text, Brush brush)
    {
        var typeface = new Typeface(new FontFamily(text.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            text.FontSize,
            brush,
            1.0);

        dc.DrawText(formatted, new Point(text.Origin.X, text.Origin.Y));
    }

    private static void DrawBadge(DrawingContext dc, NumberBadgeAnnotation badge, Brush brush)
    {
        double radius = badge.Diameter / 2;
        var center = new Point(badge.Center.X, badge.Center.Y);
        bool outline = badge.FillMode == BadgeFillMode.Outline;
        var typeface = new Typeface(new FontFamily("Segoe UI Variable, Microsoft YaHei UI, Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        // 填充模式：形状用标注色、数字白色；边框模式：形状描边、数字与描边同色。
        var fillBrush = outline ? null : brush;
        var strokePen = outline ? Freeze(new Pen(brush, Math.Max(1.5, badge.FontSize * 0.12))) : null;
        var textBrush = outline ? brush : Brushes.White;
        var label = new FormattedText(
            badge.Number.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            badge.FontSize,
            textBrush,
            1.0);
        var textOrigin = new Point(center.X - label.Width / 2, center.Y - label.Height / 2);

        switch (badge.Shape)
        {
            case BadgeShape.RoundedRect:
            {
                double width = Math.Max(badge.Diameter, label.Width + badge.Diameter * 0.7);
                double height = badge.Diameter;
                var rect = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
                if (outline) dc.DrawRoundedRectangle(null, strokePen, rect, height * 0.28, height * 0.28);
                else dc.DrawRoundedRectangle(brush, null, rect, height * 0.28, height * 0.28);
                dc.DrawText(label, textOrigin);
                break;
            }
            case BadgeShape.Circle:
                if (outline) dc.DrawEllipse(null, strokePen, center, radius, radius);
                else dc.DrawEllipse(brush, null, center, radius, radius);
                dc.DrawText(label, textOrigin);
                break;
            default:
            {
                // 三角 / 方 / 五边 / 六边 / 心形：闭合多边形，与 BadgeShapes 顶点一致。
                var vertices = BadgeShapes.Outline(badge.Shape, radius);
                if (vertices.Count < 3)
                {
                    dc.DrawEllipse(brush, null, center, radius, radius);
                    dc.DrawText(label, textOrigin);
                    break;
                }

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(center.X + vertices[0].X, center.Y + vertices[0].Y), true, true);
                    for (int i = 1; i < vertices.Count; i++)
                    {
                        ctx.LineTo(new Point(center.X + vertices[i].X, center.Y + vertices[i].Y), true, true);
                    }
                }

                geometry.Freeze();
                dc.DrawGeometry(fillBrush, strokePen, geometry);
                dc.DrawText(label, textOrigin);
                break;
            }
        }
    }

    private static Rect RectFromPoints(PointD a, PointD b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    public static Color ToColor(uint argb, double opacity)
    {
        byte a = (byte)(argb >> 24);
        byte r = (byte)(argb >> 16);
        byte g = (byte)(argb >> 8);
        byte b = (byte)argb;
        byte alpha = (byte)Math.Clamp(a * Math.Clamp(opacity, 0, 1), 0, 255);
        return Color.FromArgb(alpha, r, g, b);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
