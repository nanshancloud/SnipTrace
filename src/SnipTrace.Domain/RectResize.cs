namespace SnipTrace.Domain;

/// <summary>矩形的四个角（拖角调整大小用）。</summary>
public enum RectCorner
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>拖角改选区大小的纯几何：对角固定、越界裁剪、最小尺寸保护。</summary>
public static class RectResize
{
    public const int MinSize = 8;

    /// <summary>命中半径内的角（四角同时命中时按左上 / 右上 / 左下 / 右下顺序取一个）。</summary>
    public static RectCorner HitCorner(PixelRect rect, PixelPoint point, int radius = 10)
    {
        if (rect.IsEmpty) return RectCorner.None;
        if (Near(rect.Left, rect.Top)) return RectCorner.TopLeft;
        if (Near(rect.Right, rect.Top)) return RectCorner.TopRight;
        if (Near(rect.Left, rect.Bottom)) return RectCorner.BottomLeft;
        if (Near(rect.Right, rect.Bottom)) return RectCorner.BottomRight;
        return RectCorner.None;

        bool Near(int x, int y) => Math.Abs(point.X - x) <= radius && Math.Abs(point.Y - y) <= radius;
    }

    /// <summary>把某个角拖到光标处：对角保持不动，结果裁剪到 bounds；小于 minSize 时保持原样。</summary>
    public static PixelRect FromCorner(PixelRect start, RectCorner corner, PixelPoint cursor, PixelRect bounds, int minSize = MinSize)
    {
        if (start.IsEmpty || corner == RectCorner.None) return start;

        int fixedX = corner is RectCorner.TopLeft or RectCorner.BottomLeft ? start.Right : start.Left;
        int fixedY = corner is RectCorner.TopLeft or RectCorner.TopRight ? start.Bottom : start.Top;

        var rect = PixelRect.Intersect(PixelRect.FromCorners(fixedX, fixedY, cursor.X, cursor.Y), bounds);
        if (rect.Width < minSize || rect.Height < minSize) return start;
        return rect;
    }
}
