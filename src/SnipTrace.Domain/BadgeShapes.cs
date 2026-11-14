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

    /// <summary>
    /// 正心形 ♥：左右两个圆瓣 + 下方尖点，用切线把圆瓣与尖点连成闭合轮廓，
    /// 与 ♥ 字形一致（浅凹口、饱满圆瓣、尖而对称的底）。
    /// </summary>
    private static PointD[] Heart(double radius)
    {
        const double lobeOffset = 0.45;   // 瓣心横向偏移（半径比例）
        const double lobeCenterY = -0.42; // 瓣心纵坐标
        const double lobeRadius = 0.55;   // 瓣半径
        const double tipY = 1.0;          // 底部尖点
        const int arcSteps = 26;

        // 从尖点作圆瓣的切线：右瓣切点在圆上的角度 = 圆心→尖点方向 − acos(r/d)。
        double distance = Math.Sqrt(lobeOffset * lobeOffset + (tipY - lobeCenterY) * (tipY - lobeCenterY));
        double toTip = Math.Atan2(tipY - lobeCenterY, -lobeOffset);
        double theta = Math.Acos(Math.Clamp(lobeRadius / distance, -1, 1));
        double tangentAngle = toTip - theta;

        // 顶部凹口：两瓣圆的交点。
        double notchY = lobeCenterY - Math.Sqrt(lobeRadius * lobeRadius - lobeOffset * lobeOffset);
        double notchAngle = Math.Atan2(notchY - lobeCenterY, -lobeOffset);

        var right = new List<PointD>(arcSteps + 3);
        for (int i = 0; i <= arcSteps; i++)
        {
            double angle = notchAngle + (tangentAngle - notchAngle) * i / arcSteps;
            right.Add(new PointD(lobeOffset + Math.Cos(angle) * lobeRadius, lobeCenterY + Math.Sin(angle) * lobeRadius));
        }

        right.Add(new PointD(0, tipY));

        var points = new List<PointD>(right.Count * 2 - 2);
        points.AddRange(right);
        for (int i = right.Count - 2; i >= 1; i--) points.Add(new PointD(-right[i].X, right[i].Y));

        // 归一化：宽度对齐直径，垂直居中。
        double midY = (lobeCenterY - lobeRadius + tipY) / 2;
        var scaled = new PointD[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            scaled[i] = new PointD(points[i].X * radius, (points[i].Y - midY) * radius);
        }

        return scaled;
    }
}
