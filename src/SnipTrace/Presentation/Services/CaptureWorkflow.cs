using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.App.Presentation.Selection;
using SnipTrace.App.Presentation.Views;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Services;

public enum CaptureIntent
{
    Screenshot,
    Gif,
    Video
}

/// <summary>截图编排：冻结桌面、跨屏选区、按动作分发到编辑器/剪贴板/文件/钉图/录制。</summary>
public sealed class CaptureWorkflow
{
    private readonly AppServices _services;
    private bool _busy;

    public CaptureWorkflow(AppServices services)
    {
        _services = services;
    }

    public async Task CaptureAsync(CaptureIntent intent = CaptureIntent.Screenshot)
    {
        if (_busy || _services.Recording.IsRecording)
        {
            _services.Toasts.Warn("正在处理其他捕获任务", "请先完成当前截图或录制。");
            return;
        }

        _busy = true;
        try
        {
            _services.Shell?.HideForCapture();
            await Task.Delay(140);

            using var snapshot = DesktopSnapshot.Capture(_services.Settings.IncludeCursorInCapture);
            if (snapshot.FailedMonitors > 0)
            {
                _services.Toasts.Warn("部分显示器捕获失败", "失败区域已填充黑色，可尝试重新截图。");
            }

            using var overlay = new SelectionOverlay(snapshot, _services.Settings.SelectionToolbar,
                _services.Settings.TextFontFamily);
            overlay.VariantChanged += _services.RememberAnnotateVariant;
            var result = await overlay.ShowAsync();
            if (result is null) return;

            await HandleSelectionAsync(snapshot, result, overlay);
        }
        catch (Exception ex)
        {
            _services.Toasts.Warn("截图失败", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>全屏截图：当前显示器或所有显示器并集。</summary>
    public async Task CaptureFullScreenAsync(bool allMonitors)
    {
        if (_busy || _services.Recording.IsRecording)
        {
            _services.Toasts.Warn("正在处理其他捕获任务", "请先完成当前截图或录制。");
            return;
        }

        _busy = true;
        try
        {
            _services.Shell?.HideForCapture();
            await Task.Delay(120);

            using var snapshot = DesktopSnapshot.Capture(_services.Settings.IncludeCursorInCapture);
            PixelRect area = allMonitors
                ? snapshot.Bounds
                : MonitorService.FromPoint(MonitorService.GetCursorPosition())?.Bounds ?? snapshot.Bounds;
            area = PixelRect.Intersect(area, snapshot.Bounds);
            if (area.IsEmpty) return;

            using var cropped = snapshot.Crop(area);
            var image = ImageUtil.ToBitmapSource(cropped);
            if (_services.Settings.CopyAfterCapture) ClipboardService.SetImage(image);
            OpenEditor(image);
        }
        catch (Exception ex)
        {
            _services.Toasts.Warn("全屏截图失败", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task HandleSelectionAsync(DesktopSnapshot snapshot, SelectionResult result, SelectionOverlay overlay)
    {
        if (result.Area.IsEmpty) return;

        if (result.Action is SelectionAction.Gif or SelectionAction.Video)
        {
            var kind = result.Action == SelectionAction.Gif ? RecordingKind.Gif : RecordingKind.Video;
            var monitor = MonitorService.FromPoint(result.Area.Center) ?? snapshot.Monitors[0];
            var area = PixelRect.Intersect(result.Area, monitor.Bounds);
            if (area.IsEmpty)
            {
                _services.Toasts.Warn("录制区域无效", "屏幕录制仅支持单显示器内的选区。");
                return;
            }

            if (area.Area != result.Area.Area)
            {
                _services.Toasts.Info("录屏仅限单显示器", "选区已裁剪到当前显示器范围。");
            }

            await _services.Recording.StartAsync(kind, area, monitor);
            return;
        }

        using var cropped = snapshot.Crop(result.Area);
        var image = ImageUtil.ToBitmapSource(cropped);
        var annotations = result.Annotations;

        // 覆盖层上直接绘制的标注：导出/复制/钉图时烘焙进图片；进编辑器时带文档继续精标。
        BitmapSource? annotatedImage = null;
        if (annotations is { Annotations.Count: > 0 } && result.Action != SelectionAction.Edit)
        {
            annotatedImage = AnnotationRenderer.Render(image, annotations);
        }

        BitmapSource finalImage() => annotatedImage ?? image;

        switch (result.Action)
        {
            case SelectionAction.Copy:
                CopyImage(finalImage());
                AutoSaveIfEnabled(finalImage());
                break;
            case SelectionAction.Save:
                SaveImage(finalImage(), overlay);
                break;
            case SelectionAction.Pin:
                AutoSaveIfEnabled(finalImage());
                // 钉图拿原图 + 标注文档：悬浮即可选中标注继续拖动 / 调整，复制保存时再烘焙。
                new PinWindow(image, NewBaseName(), annotations, _services.Settings.TextFontFamily).Show();
                break;
            default:
                if (_services.Settings.CopyAfterCapture) ClipboardService.SetImage(finalImage());
                AutoSaveIfEnabled(finalImage());
                OpenEditor(image, annotations);
                break;
        }
    }

    /// <summary>「截图后自动保存」开关打开时，把截图静默存入截图目录（保存按钮的显式保存除外）。</summary>
    private void AutoSaveIfEnabled(BitmapSource image)
    {
        if (!_services.Settings.AutoSaveAfterCapture) return;
        try
        {
            string path = _services.SaveScreenshot(image, NewBaseName(), out _);
            _services.Toasts.Info("已自动保存", path);
        }
        catch (Exception ex)
        {
            _services.Toasts.Warn("自动保存失败", ex.Message);
        }
    }

    private void OpenEditor(BitmapSource image, AnnotationDocument? annotations = null)
    {
        var editor = new EditorWindow(image, NewBaseName(), annotations, _services.Settings.TextFontFamily);
        editor.Show();
    }

    private void CopyImage(BitmapSource image)
    {
        if (ClipboardService.SetImage(image)) _services.Toasts.Info("已复制到剪贴板", $"{image.PixelWidth} × {image.PixelHeight}");
        else _services.Toasts.Warn("复制失败", "剪贴板被其他程序占用，请稍后重试。");
    }

    private void SaveImage(BitmapSource image, SelectionOverlay overlay)
    {
        try
        {
            if (_services.Settings.AutoSaveAfterCapture)
            {
                // 自动保存开关打开：与自动保存一致，直接存入截图目录，不打断流程。
                string path = _services.SaveScreenshot(image, NewBaseName(), out _);
                _services.Toasts.Info("已保存", path);
                return;
            }

            // 开关关闭：弹出另存为对话框。截图状态保持不退出（选区与标注继续显示），
            // 取消置顶让对话框压在截图画面之上；对话框结束后由 using 关闭覆盖层。
            overlay.BeginSaveDialog();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存截图",
                FileName = NewBaseName(),
                DefaultExt = ".png",
                Filter = "PNG 图片 (*.png)|*.png",
                InitialDirectory = _services.ScreenshotDirectory
            };

            // 无论保存成功还是取消保存，对话框关闭后都退出截图状态。
            if (dialog.ShowDialog() != true) return;
            ImageUtil.SavePng(image, dialog.FileName);

            try
            {
                using var thumbnail = ImageUtil.ToBitmap(image);
                _services.Recent.Add(dialog.FileName, RecentItemKind.Screenshot, image.PixelWidth, image.PixelHeight, 0, thumbnail);
            }
            catch (Exception)
            {
            }

            _services.Toasts.Info("已保存", dialog.FileName);
        }
        catch (Exception ex)
        {
            _services.Toasts.Warn("保存失败", ex.Message);
        }
    }

    private static string NewBaseName() => OutputNaming.TimestampedBaseName("SnipTrace", DateTime.Now);
}
