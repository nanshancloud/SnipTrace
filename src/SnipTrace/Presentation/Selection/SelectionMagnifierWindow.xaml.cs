using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 跟随鼠标的像素放大镜：最近邻放大局部像素，中心像素加框，
/// 同时显示选区尺寸（分辨率）、比例、光标坐标与像素颜色。
/// </summary>
public partial class SelectionMagnifierWindow : Window
{
    /// <summary>放大区域边长（像素格数），取奇数使光标所在像素正好居中。</summary>
    public const int Cells = 15;

    /// <summary>每格放大的显示尺寸。</summary>
    public const int CellSize = 8;

    private const int PixelSize = Cells * CellSize;

    public SelectionMagnifierWindow()
    {
        InitializeComponent();
        BuildCrosshair();
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void UpdateZoom(BitmapSource? image) => ZoomImage.Source = image;

    public void UpdateInfo(PixelRect selection, PixelPoint cursor, string ratioLabel, bool hasGap, Color color)
    {
        SizeText.Text = selection.IsEmpty ? L.T("拖动框选") : $"{selection.Width} × {selection.Height}";
        RatioText.Text = $"比例 {ratioLabel}";
        OriginText.Text = selection.IsEmpty ? string.Empty : $"选区 {selection.Left}, {selection.Top}";
        CoordText.Text = $"光标 {cursor.X}, {cursor.Y}";
        GapText.Visibility = hasGap && !selection.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        ColorText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var swatch = new SolidColorBrush(color);
        swatch.Freeze();
        ColorSwatch.Background = swatch;
    }

    /// <summary>只在可见性真正变化时切换，避免每次移动都重排窗口。</summary>
    public void SetShown(bool shown)
    {
        if (shown == IsVisible)
        {
            return;
        }

        if (shown)
        {
            Show();
            UpdateLayout();
        }
        else
        {
            Hide();
        }
    }

    private void BuildCrosshair()
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F));
        var line = new SolidColorBrush(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF));
        accent.Freeze();
        line.Freeze();

        double center = PixelSize / 2.0;
        var halo = new Rectangle
        {
            Width = PixelSize,
            Height = PixelSize,
            Stroke = line,
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        };
        CrosshairLayer.Children.Add(halo);

        var vertical = new Line { X1 = center, Y1 = 0, X2 = center, Y2 = PixelSize, Stroke = line, StrokeThickness = 1 };
        var horizontal = new Line { X1 = 0, Y1 = center, X2 = PixelSize, Y2 = center, Stroke = line, StrokeThickness = 1 };
        CrosshairLayer.Children.Add(vertical);
        CrosshairLayer.Children.Add(horizontal);

        // 光标所在的那一个屏幕像素加高亮框，便于精确对齐。
        var pixel = new Rectangle
        {
            Width = CellSize,
            Height = CellSize,
            Stroke = accent,
            StrokeThickness = 1
        };
        Canvas.SetLeft(pixel, center - CellSize / 2.0);
        Canvas.SetTop(pixel, center - CellSize / 2.0);
        CrosshairLayer.Children.Add(pixel);
    }
}
