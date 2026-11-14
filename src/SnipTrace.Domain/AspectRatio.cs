namespace SnipTrace.Domain;

/// <summary>选区比例锁定。Free 表示自由比例。</summary>
public readonly record struct AspectRatio(int Width, int Height, string Label)
{
    public static readonly AspectRatio Free = new(0, 0, "自由");
    public static readonly AspectRatio Original = new(0, 0, "原始");
    public static readonly AspectRatio R16x9 = new(16, 9, "16:9");
    public static readonly AspectRatio R16x10 = new(16, 10, "16:10");
    public static readonly AspectRatio R4x3 = new(4, 3, "4:3");
    public static readonly AspectRatio R3x2 = new(3, 2, "3:2");
    public static readonly AspectRatio R1x1 = new(1, 1, "1:1");
    public static readonly AspectRatio R9x16 = new(9, 16, "9:16");

    public bool IsFree => Width <= 0 || Height <= 0;

    public double Value => IsFree ? 0 : (double)Width / Height;

    public static IReadOnlyList<AspectRatio> Presets { get; } = new[] { Free, Original, R16x9, R16x10, R4x3, R3x2, R1x1, R9x16 };

    public static AspectRatio ForOriginal(PixelRect rect)
        => rect.IsEmpty ? Original : new AspectRatio(rect.Width, rect.Height, "原始");

    /// <summary>以矩形中心为基准套用比例，并限制在边界内。</summary>
    public PixelRect FitCentered(PixelRect rect, PixelRect bounds)
    {
        if (IsFree || rect.IsEmpty) return rect;

        double ratio = Value;
        int width = rect.Width;
        int height = (int)Math.Round(width / ratio);
        if (height > rect.Height)
        {
            height = rect.Height;
            width = (int)Math.Round(height * ratio);
        }

        width = Math.Max(2, width);
        height = Math.Max(2, height);

        int x = rect.Center.X - width / 2;
        int y = rect.Center.Y - height / 2;
        return new PixelRect(x, y, width, height).ClampTo(bounds);
    }

    /// <summary>拖拽创建时按锚点（起点）生成符合比例的矩形。</summary>
    public PixelRect FromDrag(PixelPoint anchor, PixelPoint current, PixelRect bounds)
    {
        int rawWidth = Math.Abs(current.X - anchor.X);
        int rawHeight = Math.Abs(current.Y - anchor.Y);
        if (IsFree)
        {
            return PixelRect.FromCorners(anchor.X, anchor.Y, current.X, current.Y).ClampTo(bounds);
        }

        double ratio = Value;
        int width = Math.Max(rawWidth, (int)Math.Round(rawHeight * ratio));
        int height = Math.Max(2, (int)Math.Round(width / ratio));
        if (height < rawHeight)
        {
            height = rawHeight;
            width = (int)Math.Round(height * ratio);
        }

        int x = current.X >= anchor.X ? anchor.X : anchor.X - width;
        int y = current.Y >= anchor.Y ? anchor.Y : anchor.Y - height;
        return new PixelRect(x, y, Math.Max(2, width), Math.Max(2, height)).ClampTo(bounds);
    }
}
