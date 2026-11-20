using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Interop;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Capture;

/// <summary>
/// 虚拟桌面冻结帧：所有显示器共享同一张位图，坐标为虚拟桌面物理坐标。
/// </summary>
public sealed class DesktopSnapshot : IDisposable
{
    private DesktopSnapshot(PixelRect bounds, IReadOnlyList<MonitorInfo> monitors, Bitmap image, int failedMonitors)
    {
        Bounds = bounds;
        Monitors = monitors;
        Image = image;
        FailedMonitors = failedMonitors;
    }

    public PixelRect Bounds { get; }

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public Bitmap Image { get; }

    public int FailedMonitors { get; }

    public static DesktopSnapshot Capture(bool includeCursor)
    {
        var monitors = MonitorService.GetMonitors();
        var bounds = MonitorService.GetVirtualBounds(monitors);
        if (bounds.IsEmpty) bounds = new PixelRect(0, 0, 1, 1);

        var composite = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        int failed = 0;

        using (var graphics = Graphics.FromImage(composite))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

            foreach (var monitor in monitors)
            {
                using var shot = ScreenGrabber.CaptureMonitor(monitor);
                if (shot is null)
                {
                    failed++;
                    continue;
                }

                graphics.DrawImageUnscaled(shot, monitor.Bounds.Left - bounds.Left, monitor.Bounds.Top - bounds.Top);
            }

            if (includeCursor) ScreenGrabber.DrawCursor(graphics, bounds, 0, 0);
        }

        return new DesktopSnapshot(bounds, monitors, composite, failed);
    }

    /// <summary>按虚拟桌面坐标裁剪，超出部分填充黑色。</summary>
    public Bitmap Crop(PixelRect area)
    {
        var result = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Black);
        var overlap = PixelRect.Intersect(area, Bounds);
        if (!overlap.IsEmpty)
        {
            graphics.DrawImage(Image,
                new Rectangle(overlap.Left - area.Left, overlap.Top - area.Top, overlap.Width, overlap.Height),
                new Rectangle(overlap.Left - Bounds.Left, overlap.Top - Bounds.Top, overlap.Width, overlap.Height),
                GraphicsUnit.Pixel);
        }

        return result;
    }

    public BitmapSource CreateMonitorSliceSource(MonitorInfo monitor)
    {
        var area = PixelRect.Intersect(monitor.Bounds, Bounds);
        if (area.IsEmpty) return ImageUtil.EmptyBitmapSource(monitor.Bounds.Width, monitor.Bounds.Height);
        using var slice = Crop(area);
        return ImageUtil.ToBitmapSource(slice);
    }

    public void Dispose() => Image.Dispose();
}

public static class ScreenGrabber
{
    /// <summary>按显示器设备名捕获整屏，避免混合 DPI 下坐标被系统缩放。</summary>
    public static Bitmap? CaptureMonitor(MonitorInfo monitor)
    {
        var bounds = monitor.Bounds;
        if (bounds.IsEmpty) return null;

        IntPtr sourceDc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;

        try
        {
            sourceDc = NativeMethods.CreateDC(null, monitor.DeviceName, null, IntPtr.Zero);
            if (sourceDc == IntPtr.Zero) return null;

            memoryDc = NativeMethods.CreateCompatibleDC(sourceDc);
            bitmapHandle = NativeMethods.CreateCompatibleBitmap(sourceDc, bounds.Width, bounds.Height);
            if (memoryDc == IntPtr.Zero || bitmapHandle == IntPtr.Zero) return null;

            previous = NativeMethods.SelectObject(memoryDc, bitmapHandle);
            bool ok = NativeMethods.BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, sourceDc, 0, 0,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            if (!ok) return null;

            NativeMethods.SelectObject(memoryDc, previous);
            previous = IntPtr.Zero;
            using var raw = Image.FromHbitmap(bitmapHandle);
            var copy = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(copy))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(raw, 0, 0);
            }

            return copy;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, previous);
            if (bitmapHandle != IntPtr.Zero) NativeMethods.DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            if (sourceDc != IntPtr.Zero) NativeMethods.DeleteDC(sourceDc);
        }
    }

    /// <summary>录屏帧抓取：区域必须位于单台显示器内。</summary>
    public static Bitmap? CaptureArea(PixelRect area, MonitorInfo monitor, bool includeCursor)
    {
        if (area.IsEmpty) return null;
        using var full = CaptureMonitor(monitor);
        if (full is null) return null;

        int offsetX = area.Left - monitor.Bounds.Left;
        int offsetY = area.Top - monitor.Bounds.Top;
        var result = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.DrawImage(full, new Rectangle(0, 0, area.Width, area.Height), new Rectangle(offsetX, offsetY, area.Width, area.Height), GraphicsUnit.Pixel);
            if (includeCursor) DrawCursor(graphics, area, 0, 0);
        }

        return result;
    }

    public static void DrawCursor(Graphics graphics, PixelRect area, int offsetX, int offsetY)
    {
        var info = new NativeMethods.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref info) || info.flags != NativeMethods.CURSOR_SHOWING || info.hCursor == IntPtr.Zero) return;

        if (!NativeMethods.GetIconInfo(info.hCursor, out var iconInfo)) return;

        try
        {
            int x = info.ptScreenPos.X - iconInfo.xHotspot - area.Left + offsetX;
            int y = info.ptScreenPos.Y - iconInfo.yHotspot - area.Top + offsetY;
            IntPtr dc = graphics.GetHdc();
            try
            {
                NativeMethods.DrawIconEx(dc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }
        finally
        {
            if (iconInfo.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmMask);
            if (iconInfo.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmColor);
        }
    }

    /// <summary>读取虚拟桌面某点像素，用于放大镜取色。</summary>
    public static Color GetPixelColor(DesktopSnapshot snapshot, PixelPoint point)
    {
        int x = point.X - snapshot.Bounds.Left;
        int y = point.Y - snapshot.Bounds.Top;
        if (x < 0 || y < 0 || x >= snapshot.Image.Width || y >= snapshot.Image.Height) return Color.Black;
        return snapshot.Image.GetPixel(x, y);
    }
}

public static class ImageUtil
{
    public static BitmapSource EmptyBitmapSource(int width, int height)
    {
        var bitmap = BitmapSource.Create(Math.Max(1, width), Math.Max(1, height), 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, new byte[Math.Max(1, width) * Math.Max(1, height) * 4], Math.Max(1, width) * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        IntPtr handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
        }
    }

    public static BitmapSource ToBitmapSource(BitmapSource source)
    {
        if (source.CanFreeze) source.Freeze();
        return source;
    }

    public static Bitmap ToBitmap(BitmapSource source)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    public static void SavePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>等比缩小到最大边长，不放大。</summary>
    public static Bitmap Downscale(Bitmap source, int maxEdge)
    {
        if (maxEdge <= 0 || (source.Width <= maxEdge && source.Height <= maxEdge)) return new Bitmap(source);
        double scale = Math.Min((double)maxEdge / source.Width, (double)maxEdge / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return result;
    }

    /// <summary>生成放大镜位图：最近邻采样，保持像素清晰。</summary>
    public static BitmapSource CreateMagnifier(DesktopSnapshot snapshot, PixelPoint center, int cells, int cellSize)
    {
        int half = cells / 2;
        var area = new PixelRect(center.X - half, center.Y - half, cells, cells);
        using var source = snapshot.Crop(area);
        // ToBitmapSource 只接管 HBitmap，传入的 Bitmap 必须在这里释放；放大镜拖动时按 ~80ms 反复调用。
        using var result = new Bitmap(cells * cellSize, cells * cellSize, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height));
        return ToBitmapSource(result);
    }

    public static BitmapSource ResizeBitmapSource(BitmapSource source, int width, int height)
    {
        var transformed = new System.Windows.Media.Imaging.TransformedBitmap(source,
            new System.Windows.Media.ScaleTransform((double)width / source.PixelWidth, (double)height / source.PixelHeight));
        transformed.Freeze();
        return transformed;
    }

    public static Rect ToRect(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
