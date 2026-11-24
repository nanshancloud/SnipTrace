using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.App.Presentation.Services;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

public partial class PinWindow : Window
{
    private readonly BitmapSource _image;
    private readonly string _baseName;
    private readonly AnnotationDocument _document;
    private readonly double _baseWidth;
    private readonly double _baseHeight;

    /// <summary>相对原始大小的缩放倍率（滚轮调节）。</summary>
    private double _zoom = 1;

    /// <summary>右键唤出的工具条是否常驻（不随鼠标移出隐藏）。</summary>
    private bool _toolbarPinned;

    /// <summary>ApplyZoom 改窗口尺寸期间抑制 SizeChanged → ZoomToFit 反向改写画布缩放。</summary>
    private bool _applyingZoom;

    private TextAnnotation? _editingText;

    public PinWindow(BitmapSource image, string baseName, AnnotationDocument? annotations = null,
        string? textFontFamily = null)
    {
        InitializeComponent();
        _image = image;
        _baseName = baseName;

        // 钉图保留标注文档：鼠标悬浮到标注上即可选中，拖动移动、四角调整；
        // 复制 / 保存时再烘焙进图片，钉图期间不丢可编辑性。
        _document = new AnnotationDocument(image.PixelWidth, image.PixelHeight);
        if (annotations is not null)
        {
            foreach (var annotation in annotations.Annotations) _document.Add(annotation.Clone());
        }

        Canvas.Image = image;
        Canvas.Document = _document;
        Canvas.Tool = EditorTool.Select;
        Canvas.HoverSelect = true;
        Canvas.HoverHitTolerance = 4;
        Canvas.ShowCheckerboard = false;
        if (!string.IsNullOrWhiteSpace(textFontFamily)) Canvas.TextFontFamily = textFontFamily.Trim();

        var area = SystemParameters.WorkArea;
        double scale = Math.Min(1.0, Math.Min(area.Width * 0.8 / image.PixelWidth, area.Height * 0.8 / image.PixelHeight));
        _baseWidth = Math.Max(80, image.PixelWidth * scale);
        _baseHeight = Math.Max(60, image.PixelHeight * scale);
        ApplyZoom(1);

        // 窗口尺寸变化后让画布按窗口大小重新贴合，标注跟着一起缩放。
        Loaded += (_, _) => Canvas.ZoomToFit(0);
        SizeChanged += (_, _) =>
        {
            if (!_applyingZoom) Canvas.ZoomToFit(0);
        };

        MouseEnter += (_, _) => ShowToolbar(true);
        MouseLeave += (_, _) =>
        {
            if (!_toolbarPinned) ShowToolbar(false);
        };

        Canvas.TextEditRequested += (annotation, _) => BeginTextEdit(annotation);
        PreviewMouseLeftButtonDown += PinWindow_PreviewMouseLeftButtonDown;
        PreviewMouseWheel += PinWindow_MouseWheel;
        MouseRightButtonUp += (_, e) =>
        {
            // 右键开关工具条（Snipaste 行为：钉图上也用右键唤出操作条）。
            _toolbarPinned = !_toolbarPinned;
            ShowToolbar(_toolbarPinned);
            e.Handled = true;
        };

        // PreviewKeyDown：焦点在工具条按钮 / 弹出层时也能收到快捷键，不依赖冒泡时机。
        PreviewKeyDown += PinWindow_KeyDown;
    }

    private static AppServices Services => AppServices.Current!;

    private void ShowToolbar(bool visible) => ToolBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void PinWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // 文本编辑中：回车 / Esc 交给 TextEditorBox，别被这里的快捷键抢走。
        if (_editingText is not null) return;

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
            case Key.Delete or Key.Back:
                // 与编辑器一致：删选中标注；没有选中时什么都不做，关闭只用 Esc / 关闭按钮。
                if (Canvas.Selection.Count > 0)
                {
                    Canvas.DeleteSelection();
                    e.Handled = true;
                }
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
        _applyingZoom = true;
        try
        {
            _zoom = zoom;
            Width = Math.Max(60, _baseWidth * zoom);
            Height = Math.Max(40, _baseHeight * zoom) + 2;
            if (ZoomText is not null) ZoomText.Text = $"{zoom * 100:0}%";
        }
        finally
        {
            _applyingZoom = false;
        }
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ApplyZoom(1);

    private void ToggleOpacity()
    {
        Opacity = Opacity < 1 ? 1 : 0.55;
        if (OpacityMenu is not null)
        {
            OpacityMenu.Header = Opacity < 1 ? L.T("恢复不透明 (Alt+滚轮)") : L.T("半透明显示 (Alt+滚轮)");
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

    /// <summary>
    /// 按下时先问画布：命中标注就交给它选中 / 拖动 / 调整，其余位置拖整个钉图窗口；
    /// 工具条上的点击不触发窗口拖动。
    /// </summary>
    private void PinWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ToolBar.IsVisible && ToolBar.IsMouseOver) return;

        var point = e.GetPosition(Canvas);
        if (Canvas.HitTestAnnotation(point) is not null || Canvas.IsOverSelectionHandle(point)) return;

        Canvas.SelectAnnotation(null);
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        e.Handled = true;
    }

    /// <summary>导出用：有标注时把标注烘焙进图片，否则直接给原图。</summary>
    private BitmapSource FlatImage()
        => _document.Annotations.Count > 0 ? AnnotationRenderer.Render(_image, _document) : _image;

    private void BeginTextEdit(TextAnnotation annotation)
    {
        _editingText = annotation;
        Canvas.SelectAnnotation(annotation);

        var origin = Canvas.ToScreen(annotation.Origin);
        TextEditorBox.Margin = new Thickness(origin.X + 2, origin.Y + 2, 0, 0);
        TextEditorBox.FontSize = Math.Max(12, annotation.FontSize * Canvas.Zoom);
        TextEditorBox.Width = Math.Max(180, Math.Min(560, annotation.EstimatedWidth * Canvas.Zoom + 24));
        TextEditorBox.MinHeight = Math.Max(38, annotation.EstimatedHeight * Canvas.Zoom);
        TextEditorBox.Text = annotation.Text;
        TextEditorBox.Visibility = Visibility.Visible;
        TextEditorBox.Focus();
        TextEditorBox.SelectAll();
    }

    private void CommitText(bool cancel = false)
    {
        if (_editingText is null) return;
        var annotation = _editingText;
        _editingText = null;
        string text = TextEditorBox.Text;
        TextEditorBox.Visibility = Visibility.Collapsed;

        if (!cancel && annotation.Text != text)
        {
            _document.BeginEdit();
            annotation.Text = text;
            _document.MarkChanged();
            _document.EndEdit(true);
        }
    }

    private void TextEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            CommitText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitText(true);
            e.Handled = true;
        }
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_editingText is not null) CommitText();
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
            // 共享同一份文档：精修里的增删改 / 撤销会实时反映到钉图，复制保存也用最新内容。
            var editor = new EditorWindow(_image, _baseName, _document, Canvas.TextFontFamily, shareDocument: true);
            editor.Closed += (_, _) => Topmost = wasTopmost;
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn(L.T("无法打开精修"), ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Copy()
    {
        if (SnipTrace.App.Infrastructure.Platform.ClipboardService.SetImage(FlatImage()))
        {
            Services.Toasts.Info(L.T("已复制到剪贴板"));
        }
        else
        {
            Services.Toasts.Warn(L.T("复制失败"), L.T("剪贴板可能被其他程序占用，请稍后重试。"));
        }
    }

    /// <summary>保存：弹出另存为对话框，默认定位到截图目录并预填文件名。</summary>
    private void SaveAs()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = L.T("保存钉图"),
                FileName = _baseName,
                DefaultExt = ".png",
                Filter = L.T("PNG 图片 (*.png)|*.png"),
                InitialDirectory = Services.ScreenshotDirectory
            };

            if (dialog.ShowDialog(this) != true) return;
            var flat = FlatImage();
            ImageUtil.SavePng(flat, dialog.FileName);
            AddRecent(dialog.FileName, flat);
            Services.Toasts.Info(L.T("已保存"), dialog.FileName);
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn(L.T("保存失败"), ex.Message);
        }
    }

    /// <summary>不打断流程：直接存入截图目录（文件名冲突自动追加序号）。</summary>
    private void SaveToDirectory()
    {
        try
        {
            string path = Services.SaveScreenshot(FlatImage(), _baseName, out _);
            Services.Toasts.Info(L.T("已保存"), path);
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn(L.T("保存失败"), ex.Message);
        }
    }

    private void AddRecent(string path, BitmapSource image)
    {
        try
        {
            using var thumbnail = ImageUtil.ToBitmap(image);
            Services.Recent.Add(path, RecentItemKind.Screenshot, _image.PixelWidth, _image.PixelHeight, 0, thumbnail);
        }
        catch (Exception)
        {
        }
    }
}
