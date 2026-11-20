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

    /// <summary>马赛克块的采样键：块网格锚定标注坐标，取样时再按图像原点换算到源图。</summary>
    private readonly record struct MosaicCellKey(int Block, int Col, int Row, int OriginX, int OriginY);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, Dictionary<MosaicCellKey, uint>> MosaicColorCache = new();
    private static readonly Dictionary<uint, SolidColorBrush> MosaicBrushCache = new();

    /// <summary>
    /// 马赛克笔刷：沿笔迹把扫到的方块取源图平均色铺满。块网格锚定在**标注坐标**（选区/文档原点），
    /// <paramref name="imageOrigin"/> 只用于把取样矩形换算到源图像素——这样覆盖层跨屏切片与导出/编辑器
    /// 得到完全相同的块边界和颜色。
    /// </summary>
    public static void DrawMosaic(DrawingContext dc, BitmapSource? image, ShapeAnnotation shape, PointD imageOrigin = default)
    {
        if (image is null || shape.Points.Count == 0) return;

        int block = shape.MosaicBlockSize;
        int originX = (int)Math.Round(imageOrigin.X);
        int originY = (int)Math.Round(imageOrigin.Y);

        var cells = CollectMosaicCells(shape.Points, block, originX, originY, image.PixelWidth, image.PixelHeight);
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

        var cache = MosaicColorCache.GetValue(image, _ => new Dictionary<MosaicCellKey, uint>());
        var buffer = new byte[block * block * 4];

        foreach (var (col, row) in cells)
        {
            // 块在标注坐标的范围，跟源图可见范围取交集。
            int left = Math.Max(col * block, originX);
            int top = Math.Max(row * block, originY);
            int right = Math.Min((col + 1) * block, originX + image.PixelWidth);
            int bottom = Math.Min((row + 1) * block, originY + image.PixelHeight);
            int width = right - left;
            int height = bottom - top;
            if (width <= 0 || height <= 0) continue;

            var key = new MosaicCellKey(block, col, row, originX, originY);
            uint argb;
            lock (cache)
            {
                if (!cache.TryGetValue(key, out argb))
                {
                    // 取样矩形换算到源图像素（带偏移的块不一定是整块）。
                    if (!TryAverageColor(source, left - originX, top - originY, width, height, buffer, out var color))
                        continue;
                    argb = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
                    cache[key] = argb;
                }
            }

            dc.DrawRectangle(MosaicBrush(argb), null, new Rect(left, top, width + 0.5, height + 0.5));
        }
    }

    /// <summary>
    /// 笔迹扫过的块集合。块按标注坐标网格对齐，且只选中心距笔迹 ≤ 半块的 cell，
    /// 因此实际绘制足迹 ≤ 1 块，与 Bounds / HitTest 的外扩一致。
    /// </summary>
    private static HashSet<(int Col, int Row)> CollectMosaicCells(
        IReadOnlyList<PointD> points, int block, int originX, int originY, int imageWidth, int imageHeight)
    {
        var cells = new HashSet<(int, int)>();
        double step = Math.Max(2, block / 4.0);

        void Mark(double x, double y)
        {
            // cell 中心 (col+0.5)*block 与点的距离不超过半块。
            int minCol = (int)Math.Ceiling((x - block) / block);
            int maxCol = (int)Math.Floor(x / block);
            int minRow = (int)Math.Ceiling((y - block) / block);
            int maxRow = (int)Math.Floor(y / block);

            for (int row = minRow; row <= maxRow; row++)
            {
                if ((row + 1) * block <= originY || row * block >= originY + imageHeight) continue;
                for (int col = minCol; col <= maxCol; col++)
                {
                    if ((col + 1) * block <= originX || col * block >= originX + imageWidth) continue;
                    cells.Add((col, row));
                }
            }
        }

        Mark(points[0].X, points[0].Y);
        for (int i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var current = points[i];
            double dx = current.X - prev.X;
            double dy = current.Y - prev.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(distance / step));
            for (int s = 1; s <= steps; s++)
            {
                Mark(prev.X + dx * s / steps, prev.Y + dy * s / steps);
            }
        }

        return cells;
    }

    /// <summary>取一个矩形的平均色（按源图像素）。</summary>
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

    /// <summary>按颜色复用冻结画刷，避免每帧每块都新建。</summary>
    private static SolidColorBrush MosaicBrush(uint argb)
    {
        lock (MosaicBrushCache)
        {
            if (!MosaicBrushCache.TryGetValue(argb, out var brush))
            {
                brush = Freeze(new SolidColorBrush(Color.FromArgb(
                    (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)));
                MosaicBrushCache[argb] = brush;
            }

            return brush;
        }
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
