using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SnipTrace.App.Presentation.Views;

/// <summary>
/// 生成图标字形内容。必须用显式 TextBlock：Theme 里对 TextBlock 的隐式样式
/// 会把 FontFamily 固定为界面字体，字符串内容走 ContentPresenter 时会被覆盖成方块。
/// </summary>
internal static class IconContent
{
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    public static TextBlock Create(string glyph, double fontSize = 13)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });

        return text;
    }
}
