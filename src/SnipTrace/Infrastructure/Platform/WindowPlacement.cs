using System;
using System.Windows;
using System.Windows.Interop;
using SnipTrace.App.Infrastructure.Interop;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>按物理像素定位窗口，避免混合 DPI 下 WPF 虚拟坐标与系统坐标冲突。</summary>
public static class WindowPlacement
{
    public static void Place(Window window, PixelRect bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        double scale = GetScale(handle);
        window.Width = Math.Max(1, bounds.Width / scale);
        window.Height = Math.Max(1, bounds.Height / scale);

        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>不改变位置与尺寸，仅把窗口提回置顶层（新弹出窗口会把同层窗口压到覆盖层后面）。</summary>
    public static void Raise(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public static void MoveTo(Window window, int x, int y)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public static double GetScale(IntPtr handle)
    {
        try
        {
            uint dpi = NativeMethods.GetDpiForWindow(handle);
            return dpi >= 96 ? dpi / 96.0 : 1.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    public static (int Width, int Height) GetPixelSize(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var rect)) return (rect.Width, rect.Height);
        return ((int)window.ActualWidth, (int)window.ActualHeight);
    }

    public static PixelRect GetPixelBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var rect))
        {
            return PixelRect.FromLtrb(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        return new PixelRect(0, 0, (int)window.ActualWidth, (int)window.ActualHeight);
    }

    public static void ApplyToolWindowStyle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);
    }

    public static void ApplyNoActivate(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
