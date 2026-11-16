using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Presentation.Services;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

public partial class PinWindow : Window
{
    private readonly BitmapSource _image;
    private readonly string _baseName;
    private readonly double _baseWidth;
    private readonly double _baseHeight;

    /// <summary>相对原始大小的缩放倍率（滚轮调节）。</summary>
    private double _zoom = 1;

    /// <summary>右键唤出的工具条是否常驻（不随鼠标移出隐藏）。</summary>
    private bool _toolbarPinned;

    public PinWindow(BitmapSource image, string baseName)
    {
        InitializeComponent();
        _image = image;
        _baseName = baseName;
        PreviewImage.Source = image;

        var area = SystemParameters.WorkArea;
        double scale = Math.Min(1.0, Math.Min(area.Width * 0.8 / image.PixelWidth, area.Height * 0.8 / image.PixelHeight));
        _baseWidth = Math.Max(80, image.PixelWidth * scale);
        _baseHeight = Math.Max(60, image.PixelHeight * scale);
        ApplyZoom(1);

        MouseEnter += (_, _) => ShowToolbar(true);
        MouseLeave += (_, _) =>
        {
            if (!_toolbarPinned) ShowToolbar(false);
        };

        PreviewMouseWheel += PinWindow_MouseWheel;
        MouseRightButtonUp += (_, e) =>
        {
            // 右键开关工具条（Snipaste 行为：钉图上也用右键唤出操作条）。
            _toolbarPinned = !_toolbarPinned;
            ShowToolbar(_toolbarPinned);
            e.Handled = true;
        };

        KeyDown += PinWindow_KeyDown;
    }

    private static AppServices Services => AppServices.Current!;

    private void ShowToolbar(bool visible) => ToolBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void PinWindow_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_toolbarPinned)
                {
                    _toolbarPinned = false;
                    ShowToolbar(false);
                }
                else
                {
                    Close();
                }

                e.Handled = true;
                break;
            case Key.Delete:
                Close();
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                Copy();
                e.Handled = true;
                break;
            case Key.S when Keyboard.Modifiers == ModifierKeys.Control:
                SaveAs();
                e.Handled = true;
                break;
        }
    }

    /// <summary>滚轮缩放钉图；按住 Alt 滚轮切换半透明显示。</summary>
    private void PinWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            ToggleOpacity();
            return;
        }

        ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1);
    }

    private void ZoomBy(double factor)
    {
        double target = Math.Clamp(_zoom * factor, 0.1, 4.0);
        if (Math.Abs(target - _zoom) < 0.001) return;

        var cursor = Mouse.GetPosition(this);
        double oldWidth = ActualWidth > 0 ? ActualWidth : Width;
        double oldHeight = ActualHeight > 0 ? ActualHeight : Height;
        double relX = oldWidth > 0 ? cursor.X / oldWidth : 0.5;
        double relY = oldHeight > 0 ? cursor.Y / oldHeight : 0.5;

        ApplyZoom(target);
        UpdateLayout();

        // 保持光标下的画面点不动，缩放围绕光标进行。
        Left += (oldWidth - ActualWidth) * relX;
        Top += (oldHeight - ActualHeight) * relY;
    }

    private void ApplyZoom(double zoom)
    {
        _zoom = zoom;
        Width = Math.Max(60, _baseWidth * zoom);
        Height = Math.Max(40, _baseHeight * zoom) + 2;
        if (ZoomText is not null) ZoomText.Text = $"{zoom * 100:0}%";
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ApplyZoom(1);

    private void ToggleOpacity()
    {
        Opacity = Opacity < 1 ? 1 : 0.55;
        if (OpacityMenu is not null)
        {
            OpacityMenu.Header = Opacity < 1 ? "恢复不透明 (Alt+滚轮)" : "半透明显示 (Alt+滚轮)";
        }
    }

    private void ToggleOpacity_Click(object sender, RoutedEventArgs e) => ToggleOpacity();

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Copy();

    private void Save_Click(object sender, RoutedEventArgs e) => SaveAs();

    private void SaveToDirectory_Click(object sender, RoutedEventArgs e) => SaveToDirectory();

    /// <summary>精修：把钉图丢进标注编辑器继续编辑；钉图临时取消置顶，避免压住编辑器。</summary>
    private void Refine_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool wasTopmost = Topmost;
            Topmost = false;
            var editor = new EditorWindow(_image, _baseName);
            editor.Closed += (_, _) => Topmost = wasTopmost;
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn("无法打开精修", ex.Message);
        }
    }

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

    /// <summary>保存：弹出另存为对话框，默认定位到截图目录并预填文件名。</summary>
    private void SaveAs()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存钉图",
                FileName = _baseName,
                DefaultExt = ".png",
                Filter = "PNG 图片 (*.png)|*.png",
                InitialDirectory = Services.ScreenshotDirectory
            };

            if (dialog.ShowDialog(this) != true) return;
            ImageUtil.SavePng(_image, dialog.FileName);
            AddRecent(dialog.FileName);
            Services.Toasts.Info("已保存", dialog.FileName);
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn("保存失败", ex.Message);
        }
    }

    /// <summary>不打断流程：直接存入截图目录（文件名冲突自动追加序号）。</summary>
    private void SaveToDirectory()
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

    private void AddRecent(string path)
    {
        try
        {
            using var thumbnail = ImageUtil.ToBitmap(_image);
            Services.Recent.Add(path, RecentItemKind.Screenshot, _image.PixelWidth, _image.PixelHeight, 0, thumbnail);
        }
        catch (Exception)
        {
        }
    }
}
