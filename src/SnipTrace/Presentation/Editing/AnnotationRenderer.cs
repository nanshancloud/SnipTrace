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

        foreach (var annotation in document.InRenderOrder) Draw(dc, annotation);
    }

    public static BitmapSource Render(BitmapSource image, AnnotationDocument document)
    {
        double width = Math.Max(1, document.ImageWidth);
        double height = Math.Max(1, document.ImageHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, height));
            foreach (var annotation in document.InRenderOrder) Draw(dc, annotation);
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

        if (shape.ArrowHead is ArrowHead.End or ArrowHead.Both)
        {
            DrawArrowHead(dc, points[^2], points[^1], pen, brush);
        }

        if (shape.ArrowHead is ArrowHead.Start or ArrowHead.Both)
        {
            DrawArrowHead(dc, points[1], points[0], pen, brush);
        }
    }

    private static void DrawArrowHead(DrawingContext dc, PointD from, PointD tip, Pen pen, Brush brush)
    {
        double dx = tip.X - from.X;
        double dy = tip.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.5) return;

        // 端头随线宽同步缩放：细线不至于过大，粗线也不会显得像个小疙瘩。
        double headLength = Math.Min(Math.Clamp(pen.Thickness * 4.2, 8, 44), length);
        double headWidth = headLength * 0.55;
        double ux = dx / length;
        double uy = dy / length;
        double normalX = -uy;
        double normalY = ux;

        var end = ToPoint(tip);
        var basePoint = new Point(tip.X - ux * headLength, tip.Y - uy * headLength);
        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(end, true, true);
            ctx.LineTo(new Point(basePoint.X + normalX * headWidth, basePoint.Y + normalY * headWidth), true, true);
            ctx.LineTo(new Point(basePoint.X - normalX * headWidth, basePoint.Y - normalY * headWidth), true, true);
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
