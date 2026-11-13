using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnipTrace.App.Presentation.Services;

namespace SnipTrace.App.Presentation.Views;

public partial class PinWindow : Window
{
    private readonly BitmapSource _image;
    private readonly string _baseName;

    public PinWindow(BitmapSource image, string baseName)
    {
        InitializeComponent();
        _image = image;
        _baseName = baseName;
        PreviewImage.Source = image;

        var area = SystemParameters.WorkArea;
        double scale = Math.Min(1.0, Math.Min(area.Width * 0.8 / image.PixelWidth, area.Height * 0.8 / image.PixelHeight));
        Width = Math.Max(80, image.PixelWidth * scale);
        Height = Math.Max(60, image.PixelHeight * scale) + 2;

        MouseEnter += (_, _) => ToolBar.Opacity = 1;
        MouseLeave += (_, _) => ToolBar.Opacity = 0;
        KeyDown += PinWindow_KeyDown;
    }

    private static AppServices Services => AppServices.Current!;

    private void PinWindow_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Delete:
                Close();
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                Copy();
                e.Handled = true;
                break;
            case Key.S when Keyboard.Modifiers == ModifierKeys.Control:
                Save();
                e.Handled = true;
                break;
        }
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Copy();

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Copy()
    {
        if (SnipTrace.App.Infrastructure.Platform.ClipboardService.SetImage(_image))
        {
            Services.Toasts.Info("已复制到剪贴板");
        }
        else
        {
            Services.Toasts.Warn("复制失败", "剪贴板可能被其他程序占用，请稍后重试。");
        }
    }

    private void Save()
    {
        try
        {
            string path = Services.SaveScreenshot(_image, _baseName, out _);
            Services.Toasts.Info("已保存", path);
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn("保存失败", ex.Message);
        }
    }
}
