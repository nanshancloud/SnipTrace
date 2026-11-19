using System;
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
    /// 马赛克：按块大小对源图区域取平均色再逐块填充（滚轮改块大小）。
    /// <paramref name="imageOrigin"/> 是图像左上角在标注坐标中的位置：导出与编辑器为 (0,0)，
    /// 覆盖层则为本显示器切片相对选区的偏移，保证两条渲染链路结果一致。
    /// </summary>
    public static void DrawMosaic(DrawingContext dc, BitmapSource? image, ShapeAnnotation shape, PointD imageOrigin = default)
    {
        if (image is null || shape.Points.Count < 2) return;

        int originX = (int)Math.Round(imageOrigin.X);
        int originY = (int)Math.Round(imageOrigin.Y);

        // 形状矩形换算到图像像素坐标（标注坐标减去图像原点）。
        int left = (int)Math.Round(Math.Min(shape.Points[0].X, shape.Points[1].X)) - originX;
        int top = (int)Math.Round(Math.Min(shape.Points[0].Y, shape.Points[1].Y)) - originY;
        int right = (int)Math.Round(Math.Max(shape.Points[0].X, shape.Points[1].X)) - originX;
        int bottom = (int)Math.Round(Math.Max(shape.Points[0].Y, shape.Points[1].Y)) - originY;

        // 只画落在图像内的部分（覆盖层的显示器切片可能比选区小）。
        int sampleLeft = Math.Max(left, 0);
        int sampleTop = Math.Max(top, 0);
        int sampleRight = Math.Min(right, image.PixelWidth);
        int sampleBottom = Math.Min(bottom, image.PixelHeight);
        int width = sampleRight - sampleLeft;
        int height = sampleBottom - sampleTop;
        if (width <= 1 || height <= 1) return;

        int block = Math.Clamp((int)Math.Round(shape.Style.Width * 2), 8, 48);

        // 绘制坐标 = 图像坐标 + 图像原点。
        int drawLeft = sampleLeft + originX;
        int drawTop = sampleTop + originY;

        try
        {
            var crop = new CroppedBitmap(image, new Int32Rect(sampleLeft, sampleTop, width, height));
            BitmapSource source = crop.Format == PixelFormats.Bgra32
                ? crop
                : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);

            int stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);

            for (int y = 0; y < height; y += block)
            {
                int rows = Math.Min(block, height - y);
                for (int x = 0; x < width; x += block)
                {
                    int cols = Math.Min(block, width - x);
                    long sumB = 0, sumG = 0, sumR = 0;
                    for (int row = 0; row < rows; row++)
                    {
                        int offset = (y + row) * stride + x * 4;
                        for (int col = 0; col < cols; col++)
                        {
                            sumB += pixels[offset];
                            sumG += pixels[offset + 1];
                            sumR += pixels[offset + 2];
                            offset += 4;
                        }
                    }

                    int count = rows * cols;
                    var brush = Freeze(new SolidColorBrush(Color.FromRgb(
                        (byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count))));
                    // 多铺 1 px，避免块之间出现细缝。
                    dc.DrawRectangle(brush, null, new Rect(drawLeft + x, drawTop + y, cols + 1, rows + 1));
                }
            }
        }
        catch (Exception)
        {
            // 源图异常时不画马赛克，别影响其它标注。
        }
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
