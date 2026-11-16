namespace SnipTrace.Domain;

/// <summary>
/// 标注编辑的纯逻辑（命中、滚轮微调、等比缩放），供覆盖层与编辑器复用，也便于单测。
/// </summary>
public static class AnnotationEditing
{
    public const double MinStrokeWidth = 1;
    public const double MaxStrokeWidth = 16;
    public const double MinFontSize = 10;
    public const double MaxTextFontSize = 200;
    public const double MaxBadgeFontSize = 160;

    /// <summary>自上而下（后画的优先）命中标注；命中不到返回 null。</summary>
    public static Annotation? HitTest(AnnotationDocument? document, PointD point, double extraTolerance = 3)
    {
        if (document is null) return null;

        Annotation? found = null;
        foreach (var annotation in document.InRenderOrder)
        {
            if (annotation.HitTest(point, annotation.HitTolerance + extraTolerance)) found = annotation;
        }

        return found;
    }

    /// <summary>
    /// 滚轮微调：矩形 / 椭圆 / 箭头改线宽（1–16），文字 / 序号改字号，
    /// 画笔与高亮按比例缩放。返回是否发生变化。
    /// </summary>
    public static bool AdjustOnWheel(Annotation annotation, bool increase, double factor = 1.1)
    {
        switch (annotation)
        {
            case TextAnnotation text:
                text.FontSize = Math.Clamp(text.FontSize * (increase ? factor : 1 / factor), MinFontSize, MaxTextFontSize);
                return true;

            case NumberBadgeAnnotation badge:
                badge.FontSize = Math.Clamp(badge.FontSize * (increase ? factor : 1 / factor), MinFontSize, MaxBadgeFontSize);
                return true;

            case ShapeAnnotation shape when shape.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Arrow:
                double width = Math.Clamp(shape.Style.Width + (increase ? 1 : -1), MinStrokeWidth, MaxStrokeWidth);
                shape.Style = shape.Style.WithWidth(width);
                return true;

            case ShapeAnnotation shape:
                return Scale(shape, increase ? factor : 1 / factor);

            default:
                return false;
        }
    }

    /// <summary>
    /// 橡皮：沿 from→to 的轨迹擦掉画笔 / 高亮笔画（文本、箭头、序号、图形不参与），
    /// 返回被移除的标注。编辑器与覆盖层共用这一套规则。
    /// </summary>
    public static List<Annotation> EraseStrokes(AnnotationDocument? document, PointD from, PointD to, double radius = 3)
    {
        var removed = new List<Annotation>();
        if (document is null) return removed;

        double length = from.DistanceTo(to);
        int steps = Math.Max(1, (int)(length / 5));

        for (int i = 0; i <= steps; i++)
        {
            var point = new PointD(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps);
            var hits = document.Annotations
                .Where(a => a is ShapeAnnotation shape
                            && shape.Kind is AnnotationKind.Pen or AnnotationKind.Highlight
                            && shape.HitTest(point, radius))
                .ToList();

            foreach (var hit in hits)
            {
                if (document.RemoveWithoutHistory(hit)) removed.Add(hit);
            }
        }

        return removed;
    }

    /// <summary>围绕包围盒中心等比缩放；缩到 6 px 以下就停，避免把笔画缩没了。</summary>
    public static bool Scale(ShapeAnnotation shape, double factor)
    {
        if (shape.Points.Count == 0 || factor <= 0) return false;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var point in shape.Points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        if (minX > maxX) return false;

        bool tooSmall = (maxX - minX) * factor < 6 && (maxY - minY) * factor < 6;
        if (factor < 1 && tooSmall && shape.Points.Count > 1) return false;

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;
        for (int i = 0; i < shape.Points.Count; i++)
        {
            var point = shape.Points[i];
            shape.Points[i] = new PointD(
                centerX + (point.X - centerX) * factor,
                centerY + (point.Y - centerY) * factor);
        }

        return true;
    }
}
