using System;
using System.Windows;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Capture;

namespace SnipTrace.App.Infrastructure.Platform;

public static class ClipboardService
{
    /// <summary>同时写入 PNG 与位图格式，兼容微信、Word、浏览器等目标。</summary>
    public static bool SetImage(BitmapSource source)
    {
        try
        {
            BitmapSource image = WithMonitorDpi(source);
            byte[] png = ImageUtil.EncodePng(image);
            var data = new DataObject();
            data.SetData("PNG", png);
            data.SetImage(image);

            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    System.Threading.Thread.Sleep(60);
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 剪贴板内容默认按 96 DPI 解释：在 125% / 150% 缩放的屏幕上粘贴出去会比屏幕上看到的大一圈。
    /// 这里把目标显示器的 DPI 写进图里，粘贴到微信 / Word / 浏览器后的大小才和屏幕一致（相当于把图片尺寸缩小）。
    /// </summary>
    private static BitmapSource WithMonitorDpi(BitmapSource source)
    {
        try
        {
            MonitorInfo? monitor = MonitorService.FromPoint(MonitorService.GetCursorPosition())
                                   ?? MonitorService.Primary;
            int dpi = monitor?.Dpi ?? 96;
            if (dpi <= 96) return source;
            if (Math.Abs(source.DpiX - dpi) < 1 && Math.Abs(source.DpiY - dpi) < 1) return source;

            int stride = (source.Format.BitsPerPixel * source.PixelWidth + 7) / 8;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            BitmapSource rescaled = BitmapSource.Create(
                source.PixelWidth, source.PixelHeight, dpi, dpi,
                source.Format, source.Palette, pixels, stride);
            rescaled.Freeze();
            return rescaled;
        }
        catch (Exception)
        {
            return source;
        }
    }

    public static void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
        }
    }
}
