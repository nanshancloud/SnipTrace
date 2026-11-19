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
    /// <summary>按按钮 Id 生成图标内容。</summary>
    public static FrameworkElement For(string id) => id switch
    {
        SelectionToolbarItems.Ratio => RatioChip(),
        SelectionToolbarItems.Select => Stroke("M3,2 L3,11.4 L5.5,9 L7.2,12.8 L9.1,12 L7.4,8.2 L10.6,8 Z"),
        SelectionToolbarItems.Arrow => Stroke("M2,12 L11,3 M11,3 L7.5,3.5 M11,3 L10.5,6.5"),
        SelectionToolbarItems.Pen => Stroke("M2.5,11.5 L10,4 L12,2 M2.5,11.5 L2,13 L3.5,12.5"),
        SelectionToolbarItems.Eraser => Stroke("M3.6,9.2 L8.2,4.6 L11.8,8.2 L7.2,12.8 Z M2,13.8 L9.4,13.8"),
        SelectionToolbarItems.Rectangle => Stroke("M2.5,3.5 L11.5,3.5 L11.5,10.5 L2.5,10.5 Z"),
        SelectionToolbarItems.Ellipse => Stroke("M7,3 C10.3,3 12,4.6 12,7 C12,9.4 10.3,11 7,11 C3.7,11 2,9.4 2,7 C2,4.6 3.7,3 7,3 Z"),
        SelectionToolbarItems.Text => TextGlyph(),
        SelectionToolbarItems.Number => Badge(),
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
