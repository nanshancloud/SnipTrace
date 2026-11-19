using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using SnipTrace.App.Presentation.Views;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 选区工具条按钮的图标：真实工具条与设置页预览共用同一套几何，两边长得一模一样。
/// 图标颜色统一绑定到最近的按钮容器（Button / ToggleButton）的 Foreground，跟随悬停与选中态。
/// </summary>
internal static class SelectionToolbarIcons
{
    private const string RectanglePath = "M2.5,3.5 L11.5,3.5 L11.5,10.5 L2.5,10.5 Z";
    private const string EllipsePath = "M7,3 C10.3,3 12,4.6 12,7 C12,9.4 10.3,11 7,11 C3.7,11 2,9.4 2,7 C2,4.6 3.7,3 7,3 Z";
    private const string LinePath = "M2.5,11.5 L11.5,2.5";
    private const string ArrowPath = "M2,12 L11,3 M11,3 L7.5,3.5 M11,3 L10.5,6.5";

    /// <summary>按按钮 Id 生成图标内容；图形 / 箭头按记住的子选项换几何并加小三角表示可切换。</summary>
    public static FrameworkElement For(string id, string variant = "") => id switch
    {
        SelectionToolbarItems.Ratio => RatioChip(),
        SelectionToolbarItems.Shape => WithVariantChevron(Stroke(variant switch
        {
            SelectionToolbarItems.VariantEllipse => EllipsePath,
            SelectionToolbarItems.VariantLine => LinePath,
            _ => RectanglePath
        })),
        SelectionToolbarItems.Arrow => WithVariantChevron(Stroke(
            variant == SelectionToolbarItems.VariantLine ? LinePath : ArrowPath)),
        SelectionToolbarItems.Pen => Stroke("M2.5,11.5 L10,4 L12,2 M2.5,11.5 L2,13 L3.5,12.5"),
        SelectionToolbarItems.Text => TextGlyph(),
        SelectionToolbarItems.Number => Badge(),
        SelectionToolbarItems.Mosaic => MosaicGlyph(),
        SelectionToolbarItems.Color => Swatch(),
        SelectionToolbarItems.Copy => Glyph("\uE8C8"),
        SelectionToolbarItems.Save => Glyph("\uE74E"),
        SelectionToolbarItems.Pin => Glyph("\uE840"),
        SelectionToolbarItems.Gif => Glyph("\uE91B"),
        SelectionToolbarItems.Video => Glyph("\uE714"),
        SelectionToolbarItems.Undo => Glyph("\uE7A7"),
        SelectionToolbarItems.Redo => Glyph("\uE7A6"),
        SelectionToolbarItems.Cancel => Glyph("\uE711"),
        SelectionToolbarItems.Refine => Glyph("\uE70F"),
        _ => Glyph("\uE783")
    };

    /// <summary>宽度是否需要自适应（比例芯片里带文字，固定 30 px 会裁掉）。</summary>
    public static bool IsAutoWidth(string id) => id is SelectionToolbarItems.Ratio;

    public static FrameworkElement Glyph(string glyph, double fontSize = 13) => IconContent.Create(glyph, fontSize);

    public static FrameworkElement Stroke(string data)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Width = 14,
            Height = 14,
            Stretch = Stretch.None,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        BindForeground(path, Shape.StrokeProperty);
        return path;
    }

    /// <summary>图形 / 箭头按钮右下角的小三角：提示点击后还能切换子选项。</summary>
    private static FrameworkElement WithVariantChevron(FrameworkElement icon)
    {
        var grid = new Grid { Width = 14, Height = 14 };
        grid.Children.Add(icon);

        var chevron = new Path
        {
            Data = Geometry.Parse("M0,0 L3.4,0 L0,3.4 Z"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -1, -1),
            Opacity = 0.7
        };
        BindForeground(chevron, Shape.FillProperty);
        grid.Children.Add(chevron);
        return grid;
    }

    /// <summary>马赛克按钮：3×3 色块，直观表示打码。</summary>
    public static FrameworkElement MosaicGlyph()
    {
        var canvas = new Canvas { Width = 14, Height = 14 };
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var block = new Rectangle
                {
                    Width = 3.6,
                    Height = 3.6,
                    RadiusX = 0.8,
                    RadiusY = 0.8,
                    Opacity = row == 1 && col == 1 ? 0.55 : 1
                };
                BindForeground(block, Shape.FillProperty);
                Canvas.SetLeft(block, 1 + col * 4.2);
                Canvas.SetTop(block, 1 + row * 4.2);
                canvas.Children.Add(block);
            }
        }

        return canvas;
    }

    public static FrameworkElement TextGlyph()
    {
        var text = new TextBlock
        {
            Text = "T",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI")
        };
        BindForeground(text, TextBlock.ForegroundProperty);
        return text;
    }

    /// <summary>序号按钮：圆环里一个「1」，与真实按钮同一几何。</summary>
    public static FrameworkElement Badge()
    {
        var grid = new Grid { Width = 14, Height = 14 };

        var circle = new Ellipse { StrokeThickness = 1.6 };
        BindForeground(circle, Shape.StrokeProperty);
        grid.Children.Add(circle);

        var number = new TextBlock
        {
            Text = "1",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        BindForeground(number, TextBlock.ForegroundProperty);
        grid.Children.Add(number);
        return grid;
    }

    /// <summary>颜色按钮：当前标注色的小方块（预览固定品牌珊瑚色）。</summary>
    public static FrameworkElement Swatch()
    {
        var swatch = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F))
        };

        if (Application.Current?.TryFindResource("BorderBrush") is Brush border) swatch.BorderBrush = border;
        return swatch;
    }

    /// <summary>比例芯片：文字 + 下拉箭头；需要跟随比例的按钮用 out 参数拿到文字块自己更新。</summary>
    public static StackPanel RatioChip() => RatioChip(out _);

    public static StackPanel RatioChip(out TextBlock label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        label = new TextBlock
        {
            Text = "自由",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(label);

        var chevron = new TextBlock
        {
            Text = "\uE70D",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 9,
            Margin = new Thickness(4, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        BindForeground(chevron, TextBlock.ForegroundProperty);
        panel.Children.Add(chevron);
        return panel;
    }

    /// <summary>把前景 / 描边颜色挂到最近的按钮容器（Button 与 ToggleButton 都覆盖）。</summary>
    private static void BindForeground(DependencyObject target, DependencyProperty property)
    {
        BindingOperations.SetBinding(target, property, new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ButtonBase), 1)
        });
    }
}
