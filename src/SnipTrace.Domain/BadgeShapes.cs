using System;
using System.Collections.Generic;

namespace SnipTrace.Domain;

/// <summary>序号标记的形状几何：渲染层与工具条图标共用同一套顶点，保证所见即所得。</summary>
public static class BadgeShapes
{
    /// <summary>
    /// 返回以原点为中心、内接于半径 radius 圆的形状轮廓顶点（顶点朝上，闭合折线）。
    /// Circle / RoundedRect 不是多边形，由渲染层直接绘制，返回空列表。
    /// </summary>
    public static IReadOnlyList<PointD> Outline(BadgeShape shape, double radius)
    {
        return shape switch
        {
            BadgeShape.Triangle => Regular(3, radius),
            BadgeShape.Square => Square(radius),
            BadgeShape.Pentagon => Regular(5, radius),
            BadgeShape.Hexagon => Regular(6, radius),
            BadgeShape.Heart => Heart(radius),
            _ => Array.Empty<PointD>()
        };
    }

    private static PointD[] Regular(int sides, double radius)
    {
        var points = new PointD[sides];
        for (int i = 0; i < sides; i++)
        {
            double angle = Math.PI * 2 * i / sides - Math.PI / 2;
            points[i] = new PointD(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }

        return points;
    }

    private static PointD[] Square(double radius)
    {
        // 轴对齐正方形：半边长略小于内接值，给描边留余量。
        double half = radius * 0.78;
        return new[]
        {
            new PointD(-half, -half),
            new PointD(half, -half),
            new PointD(half, half),
            new PointD(-half, half)
        };
    }

    private static PointD[] Heart(double radius)
    {
        // 经典心形参数曲线 x = 16sin³t，y = 13cost − 5cos2t − 2cos3t − cos4t，归一化到 ±radius。
        const int samples = 64;
        double maxAbsX = 0, maxY = double.MinValue, minY = double.MaxValue;
        var raw = new (double X, double Y)[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = Math.PI * 2 * i / samples;
            double x = 16 * Math.Pow(Math.Sin(t), 3);
            double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
            raw[i] = (x, y);
            maxAbsX = Math.Max(maxAbsX, Math.Abs(x));
            maxY = Math.Max(maxY, y);
            minY = Math.Min(minY, y);
        }

        double scale = radius / maxAbsX;
        double midY = (maxY + minY) / 2;
        var points = new PointD[samples];
        for (int i = 0; i < samples; i++)
        {
            points[i] = new PointD(raw[i].X * scale, (raw[i].Y - midY) * scale * 1.15);
        }

        return points;
    }
}
