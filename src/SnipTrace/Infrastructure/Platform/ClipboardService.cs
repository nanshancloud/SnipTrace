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
            byte[] png = ImageUtil.EncodePng(source);
            var data = new DataObject();
            data.SetData("PNG", png);
            data.SetImage(source);

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
