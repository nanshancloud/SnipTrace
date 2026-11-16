namespace SnipTrace.Domain;

public enum AnnotationKind
{
    Rectangle,
    Ellipse,
    Arrow,
    Pen,
    Highlight,
    Text,
    Number
}

/// <summary>序号标记外形：圆 / 圆角胶囊 / 三角 / 方 / 五边形 / 六边形 / 心形。</summary>
public enum BadgeShape
{
    Circle,
    RoundedRect,
    Triangle,
    Square,
    Pentagon,
    Hexagon,
    Heart
}

/// <summary>序号标记绘制方式：填充（白字）或边框（描边 + 同色字）。</summary>
public enum BadgeFillMode
{
    Fill,
    Outline
}

/// <summary>箭头端头：无端头（直线）/ 仅末端 / 仅起点 / 两端。</summary>
public enum ArrowHead
{
    None,
    End,
    Start,
    Both
}

/// <summary>颜色为 0xAARRGGBB，线宽与字号均为原图像素单位。</summary>
public sealed record AnnotationStyle(uint Argb, double StrokeWidth, double Opacity)
{
    public static readonly AnnotationStyle Default = new(0xFFFF6B5F, 3, 1);

    public double Width => Math.Max(1, StrokeWidth);

    public AnnotationStyle WithArgb(uint argb) => this with { Argb = argb };

    public AnnotationStyle WithWidth(double width) => this with { StrokeWidth = Math.Max(1, width) };

    public AnnotationStyle WithOpacity(double opacity) => this with { Opacity = Math.Clamp(opacity, 0.05, 1) };
}

public abstract class Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public abstract AnnotationKind Kind { get; }

    public AnnotationStyle Style { get; set; } = AnnotationStyle.Default;

    public int ZIndex { get; set; }

    public abstract PixelRect Bounds { get; }

    public virtual double HitTolerance => 4 + Style.Width;

    public abstract bool HitTest(PointD point, double tolerance);

    public abstract void Move(double dx, double dy);

    /// <summary>把标注缩放到目标矩形（导出/缩放不受影响，仅改几何）。</summary>
    public abstract void Resize(PixelRect target);

    public abstract Annotation Clone();

    public bool HitTestPoint(PixelPoint point, double extraTolerance = 0)
        => HitTest(new PointD(point.X, point.Y), HitTolerance + extraTolerance);

    protected static double DistanceToSegment(PointD point, PointD a, PointD b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= double.Epsilon) return point.DistanceTo(a);
        double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return point.DistanceTo(new PointD(a.X + t * dx, a.Y + t * dy));
    }
}

/// <summary>矩形、椭圆、箭头（两段起，可多段）与画笔/高亮（折线）。</summary>
public sealed class ShapeAnnotation : Annotation
{
    private readonly AnnotationKind _kind;

    public ShapeAnnotation(AnnotationKind kind, IEnumerable<PointD> points, AnnotationStyle? style = null)
    {
        _kind = kind;
        Points = points.ToList();
        if (Points.Count == 0) Points.Add(new PointD(0, 0));
        if (_kind is not (AnnotationKind.Pen or AnnotationKind.Highlight or AnnotationKind.Arrow) && Points.Count == 1)
        {
            Points.Add(Points[0]);
        }

        Style = style ?? AnnotationStyle.Default;
    }

    public override AnnotationKind Kind => _kind;

    public List<PointD> Points { get; }

    /// <summary>画笔 / 高亮：采样折线，创建时逐点追加。</summary>
    public bool IsPolyline => _kind is AnnotationKind.Pen or AnnotationKind.Highlight;

    /// <summary>箭头端头形式；多段箭头同样适用（起点 / 末端取首尾段方向）。</summary>
    public ArrowHead ArrowHead { get; set; } = ArrowHead.End;

    /// <summary>箭头折线是否按正交（横 / 竖）方向吸附。</summary>
    public bool Elbow { get; set; }

    /// <summary>首点：折线起点（箭头尾端）。</summary>
    public PointD StartPoint => Points[0];

    /// <summary>末点：折线终点（箭头尖所在端）。</summary>
    public PointD EndPoint => Points[^1];

    /// <summary>线段数：两点箭头为 1，多段折线按实际段数。</summary>
    public int SegmentCount => Math.Max(0, Points.Count - 1);

    /// <summary>是否多段折线（三个及以上顶点）。</summary>
    public bool IsMultiSegment => Points.Count > 2;

    /// <summary>
    /// 端头所在的那一段：尾部取最后一段、头部取第一段，用于确定端头朝向；
    /// 顶点不足两个（退化）时返回 null。
    /// </summary>
    public (PointD From, PointD Tip)? HeadSegment(bool atEnd)
    {
        if (Points.Count < 2) return null;
        return atEnd ? (Points[^2], Points[^1]) : (Points[1], Points[0]);
    }

    private PixelRect RawBounds
    {
        get
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var point in Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            if (minX > maxX) return PixelRect.Empty;
            return PixelRect.FromLtrb((int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
        }
    }

    public override PixelRect Bounds
    {
        get
        {
            var raw = RawBounds;
            if (raw.IsEmpty) return raw;
            int pad = (int)Math.Ceiling(Style.Width / 2) + 1;
            return raw.Inflate(pad);
        }
    }

    public override bool HitTest(PointD point, double tolerance)
    {
        double pad = tolerance + Style.Width / 2;

        if (IsPolyline || _kind == AnnotationKind.Arrow)
        {
            if (Points.Count == 1) return point.DistanceTo(Points[0]) <= pad;
            for (int i = 1; i < Points.Count; i++)
            {
                if (DistanceToSegment(point, Points[i - 1], Points[i]) <= pad) return true;
            }

            return false;
        }

        var start = Points[0];

        var raw = RawBounds;
        if (raw.IsEmpty) return point.DistanceTo(start) <= pad;

        switch (_kind)
        {
            case AnnotationKind.Ellipse:
            {
                double rx = raw.Width / 2.0;
                double ry = raw.Height / 2.0;
                if (rx <= 0 || ry <= 0) return point.DistanceTo(start) <= pad;
                double nx = (point.X - (raw.Left + rx)) / rx;
                double ny = (point.Y - (raw.Top + ry)) / ry;
                double distance = Math.Abs(Math.Sqrt(nx * nx + ny * ny) - 1) * Math.Min(rx, ry);
                return distance <= pad;
            }

            default:
            {
                var outer = raw.Inflate((int)Math.Ceiling(pad));
                if (!outer.Contains(new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)))) return false;
                var inner = raw.Inflate(-(int)Math.Ceiling(pad));
                if (inner.IsEmpty) return true;
                var rounded = new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
                return !inner.Contains(rounded);
            }
        }
    }

    public override void Move(double dx, double dy)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = new PointD(Points[i].X + dx, Points[i].Y + dy);
    }

    public override void Resize(PixelRect target)
    {
        var raw = RawBounds;
        if (raw.IsEmpty || target.IsEmpty) return;
        double scaleX = (double)target.Width / raw.Width;
        double scaleY = (double)target.Height / raw.Height;
        for (int i = 0; i < Points.Count; i++)
        {
            double x = target.Left + (Points[i].X - raw.Left) * scaleX;
            double y = target.Top + (Points[i].Y - raw.Top) * scaleY;
            Points[i] = new PointD(x, y);
        }
    }

    public override Annotation Clone()
    {
        var clone = new ShapeAnnotation(_kind, Points, Style)
        {
            ZIndex = ZIndex,
            ArrowHead = ArrowHead,
            Elbow = Elbow
        };
        return clone;
    }
}

public sealed class TextAnnotation : Annotation
{
    public string Text { get; set; } = string.Empty;
    public PointD Origin { get; set; }
    public double FontSize { get; set; } = 20;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";

    public override AnnotationKind Kind => AnnotationKind.Text;

    public IReadOnlyList<string> Lines => Text.Replace("\r\n", "\n").Split('\n');

    public double EstimatedWidth => Math.Max(FontSize, Lines.Max(line => line.Length) * FontSize * 0.62 + FontSize * 0.4);

    public double EstimatedHeight => Lines.Count * FontSize * 1.35 + FontSize * 0.2;

    public override PixelRect Bounds
    {
        get
        {
            int width = (int)Math.Ceiling(EstimatedWidth);
            int height = (int)Math.Ceiling(EstimatedHeight);
            return new PixelRect((int)Math.Floor(Origin.X), (int)Math.Floor(Origin.Y), Math.Max(1, width), Math.Max(1, height));
        }
    }

    public override bool HitTest(PointD point, double tolerance)
    {
        var bounds = Bounds.Inflate((int)Math.Ceiling(tolerance));
        return bounds.Contains(new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)));
    }

    public override void Move(double dx, double dy) => Origin = new PointD(Origin.X + dx, Origin.Y + dy);

    public override void Resize(PixelRect target)
    {
        double scale = Math.Max(1, target.Height) / Math.Max(1.0, EstimatedHeight);
        FontSize = Math.Clamp(FontSize * scale, 12, 240);
        Origin = new PointD(target.Left, target.Top);
    }

    public override Annotation Clone() => new TextAnnotation
    {
        Text = Text,
        Origin = Origin,
        FontSize = FontSize,
        FontFamily = FontFamily,
        Style = Style,
        ZIndex = ZIndex
    };
}

public sealed class NumberBadgeAnnotation : Annotation
{
    public int Number { get; set; } = 1;
    public PointD Center { get; set; }
    public BadgeShape Shape { get; set; } = BadgeShape.Circle;
    public BadgeFillMode FillMode { get; set; } = BadgeFillMode.Fill;

    /// <summary>字号最小 12 px，保证缩略图可读。</summary>
    public double FontSize { get; set; } = 16;

    public double Diameter => Math.Max(24, FontSize * 1.8);

    public override AnnotationKind Kind => AnnotationKind.Number;

    public override double HitTolerance => 0;

    public override PixelRect Bounds
    {
        get
        {
            int size = (int)Math.Ceiling(Diameter);
            return new PixelRect((int)Math.Round(Center.X - size / 2.0), (int)Math.Round(Center.Y - size / 2.0), size, size);
        }
    }

    public override bool HitTest(PointD point, double tolerance)
        => point.DistanceTo(Center) <= Diameter / 2 + tolerance;

    public override void Move(double dx, double dy) => Center = new PointD(Center.X + dx, Center.Y + dy);

    public override void Resize(PixelRect target)
    {
        double scale = Math.Max(0.2, Math.Min(target.Width / Diameter, target.Height / Diameter));
        FontSize = Math.Clamp(FontSize * scale, 12, 240);
        Center = new PointD(target.Left + target.Width / 2.0, target.Top + target.Height / 2.0);
    }

    public override Annotation Clone() => new NumberBadgeAnnotation
    {
        Number = Number,
        Center = Center,
        Shape = Shape,
        FillMode = FillMode,
        FontSize = FontSize,
        Style = Style,
        ZIndex = ZIndex
    };
}
