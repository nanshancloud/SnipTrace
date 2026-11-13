namespace SnipTrace.Domain;

/// <summary>物理像素坐标点，可位于负坐标区域（多显示器虚拟桌面）。</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public static PixelPoint operator +(PixelPoint point, PixelPoint other) => new(point.X + other.X, point.Y + other.Y);

    public override string ToString() => $"{X},{Y}";
}

/// <summary>浮点坐标，用于标注几何运算。</summary>
public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD point, PointD other) => new(point.X + other.X, point.Y + other.Y);

    public bool IsFinite => !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Y) && !double.IsInfinity(Y);

    public double DistanceTo(PointD other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>整像素矩形，宽高始终非负，Right/Bottom 为开区间。</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static readonly PixelRect Empty = new(0, 0, 0, 0);

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int Area => IsEmpty ? 0 : Width * Height;
    public double AspectRatio => IsEmpty ? 0 : (double)Width / Height;
    public PixelPoint Center => new(X + Width / 2, Y + Height / 2);

    public static PixelRect FromLtrb(int left, int top, int right, int bottom)
    {
        if (right < left) (left, right) = (right, left);
        if (bottom < top) (top, bottom) = (bottom, top);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect FromCorners(int x1, int y1, int x2, int y2)
        => FromLtrb(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

    public static PixelRect FromPoints(PointD a, PointD b)
        => FromCorners((int)Math.Round(a.X), (int)Math.Round(a.Y), (int)Math.Round(b.X), (int)Math.Round(b.Y));

    public static PixelRect Union(PixelRect a, PixelRect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        return FromLtrb(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    }

    public static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int left = Math.Max(a.Left, b.Left);
        int top = Math.Max(a.Top, b.Top);
        int right = Math.Min(a.Right, b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);
        return right <= left || bottom <= top ? Empty : new PixelRect(left, top, right - left, bottom - top);
    }

    public bool Contains(PixelPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Contains(PixelRect other) => !other.IsEmpty && other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    public bool IntersectsWith(PixelRect other) => !Intersect(this, other).IsEmpty;

    public PixelRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public PixelRect Inflate(int amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    /// <summary>把矩形平移进边界内；若大于边界则收缩到边界尺寸。</summary>
    public PixelRect ClampTo(PixelRect bounds)
    {
        int width = Math.Min(Width, bounds.Width);
        int height = Math.Min(Height, bounds.Height);
        int x = Math.Clamp(X, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        int y = Math.Clamp(Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        return new PixelRect(x, y, width, height);
    }

    public PixelRect Scale(double factor)
        => new((int)Math.Round(X * factor), (int)Math.Round(Y * factor), (int)Math.Round(Width * factor), (int)Math.Round(Height * factor));

    public override string ToString() => $"{Width} × {Height}";
}

/// <summary>媒体输出尺寸与码率约束。</summary>
public static class MediaDimensions
{
    public static int ToEven(int value) => Math.Max(2, value - value % 2);

    /// <summary>H.264 要求偶数宽高：从右/下裁去 1 像素，不拉伸画面。</summary>
    public static (int Width, int Height) H264EvenSize(int width, int height)
        => (ToEven(width), ToEven(height));

    public static int EstimateBitRate(int width, int height)
    {
        long pixels = (long)Math.Max(1, width) * Math.Max(1, height);
        if (pixels >= 3840L * 2160L) return 24_000_000;
        if (pixels >= 2560L * 1440L) return 16_000_000;
        if (pixels >= 1920L * 1080L) return 10_000_000;
        if (pixels >= 1280L * 720L) return 6_000_000;
        return 4_000_000;
    }
}
