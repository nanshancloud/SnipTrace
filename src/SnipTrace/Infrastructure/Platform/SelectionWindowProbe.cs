using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SnipTrace.App.Infrastructure.Interop;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>
/// 光标下的可见窗口探测（Snipaste 式自动吸附）：
/// 按 Z 序枚举顶层窗口，取第一个包含光标且可交互的窗口，
/// 返回 DWM 扩展边框（不含不可见的阴影边距）。
/// </summary>
public static class SelectionWindowProbe
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "SysListView32",
        "XamlExplorerHostIslandWindow", "Windows.UI.Composition.DesktopWindowContentBridgeInputSite"
    };

    public static bool TryFindWindowAt(PixelPoint point, PixelRect bounds, out PixelRect windowRect)
    {
        windowRect = PixelRect.Empty;
        uint currentPid = GetCurrentProcessIdSafe();
        var found = PixelRect.Empty;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (found.IsEmpty && IsSelectableWindow(hwnd, currentPid, point, bounds, out var rect))
            {
                found = rect;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (found.IsEmpty) return false;
        windowRect = found;
        return true;
    }

    private static bool IsSelectableWindow(IntPtr hwnd, uint currentPid, PixelPoint point, PixelRect bounds, out PixelRect rect)
    {
        rect = PixelRect.Empty;

        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return false;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == currentPid) return false;

        var className = GetWindowClass(hwnd);
        if (className.Length == 0 || IgnoredClasses.Contains(className)) return false;

        // UWP 挂起/被遮蔽的窗口要跳过。
        int cloaked = 0;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, ref cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        var extended = GetExtendedBounds(hwnd);
        if (extended.Width < 60 || extended.Height < 60) return false;
        if (PixelRect.Intersect(extended, bounds).IsEmpty) return false;
        if (!extended.Contains(point)) return false;

        // 空标题 + 极小宽高的不可交互辅助窗口直接忽略。
        if (!HasWindowContent(hwnd, extended)) return false;

        rect = extended;
        return true;
    }

    private static PixelRect GetExtendedBounds(IntPtr hwnd)
    {
        var rect = new NativeMethods.RECT();
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, ref rect,
                Marshal.SizeOf<NativeMethods.RECT>()) != 0)
        {
            NativeMethods.GetWindowRect(hwnd, out rect);
        }

        return new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        var buffer = new char[64];
        int length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static bool HasWindowContent(IntPtr hwnd, PixelRect rect)
    {
        // 与桌面同大的空壳窗口（壁纸宿主等）虽然类名过滤了大部分，这里再按尺寸兜底。
        return rect.Width >= 80 && rect.Height >= 80;
    }

    private static uint GetCurrentProcessIdSafe()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return (uint)process.Id;
    }
}
