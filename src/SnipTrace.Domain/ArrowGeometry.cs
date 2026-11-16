using System;
using System.Collections.Generic;

namespace SnipTrace.Domain;

/// <summary>
/// 箭头端头几何：端头长度随线宽缩放（且不超过线段长度），三角形按末端方向生成。
/// 渲染层只取这里的顶点，规则只有一份，可直接单测。
/// </summary>
public static class ArrowGeometry
{
    /// <summary>端头长度 = clamp(线宽 × 4.2, 8, 44)，细线不至于过大、粗线也不会缩成小疙瘩。</summary>
    public const double HeadLengthFactor = 4.2;

    public const double MinHeadLength = 8;

    public const double MaxHeadLength = 44;

    /// <summary>端头底边半宽 = 端头长度 × 0.55。</summary>
    public const double HeadWidthRatio = 0.55;

    /// <summary>线段短于此长度就不画端头（避免退化三角形）。</summary>
    public const double MinSegmentLength = 0.5;

    /// <summary>端头长度：按线宽计算并钳制到 8–44，再受线段长度约束。</summary>
    public static double HeadLength(double strokeWidth, double segmentLength = double.PositiveInfinity)
    {
        double length = Math.Clamp(strokeWidth * HeadLengthFactor, MinHeadLength, MaxHeadLength);
        if (!double.IsFinite(segmentLength)) return length;

        return Math.Min(length, Math.Max(0, segmentLength));
    }

    /// <summary>首 / 尾哪些端需要端头（None / Start / End / Both）。</summary>
    public static (bool Start, bool End) Ends(ArrowHead head)
        => (head is ArrowHead.Start or ArrowHead.Both, head is ArrowHead.End or ArrowHead.Both);

    /// <summary>
    /// 端头三角形的三个顶点：尖点在前，随后是底边两侧（绕线段轴对称）。
    /// 线段过短或退化为点时返回空集合，调用方直接跳过绘制。
    /// </summary>
    public static IReadOnlyList<PointD> HeadTriangle(PointD from, PointD tip, double strokeWidth)
    {
        double dx = tip.X - from.X;
        double dy = tip.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (!(length >= MinSegmentLength)) return Array.Empty<PointD>();

        double headLength = HeadLength(strokeWidth, length);
        if (!(headLength > 0.01)) return Array.Empty<PointD>();

        double ux = dx / length;
        double uy = dy / length;
        double halfWidth = headLength * HeadWidthRatio;
        double baseX = tip.X - ux * headLength;
        double baseY = tip.Y - uy * headLength;

        // 法向量 (-uy, ux)：底边两点分别落在线段两侧，保证端头正对走向、左右对称。
        return new[]
        {
            tip,
            new PointD(baseX - uy * halfWidth, baseY + ux * halfWidth),
            new PointD(baseX + uy * halfWidth, baseY - ux * halfWidth)
        };
    }
}
