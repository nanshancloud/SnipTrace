using System;
using System.Collections.Generic;
using SnipTrace.App.Infrastructure.Interop;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Capture;

public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)
{
    public double Scale => Math.Max(1.0, Dpi) / 96.0;

    public PixelRect DipWorkArea => new(
        (int)Math.Round(WorkArea.X / Scale),
        (int)Math.Round(WorkArea.Y / Scale),
        (int)Math.Round(WorkArea.Width / Scale),
        (int)Math.Round(WorkArea.Height / Scale));

    public string DisplayLabel => IsPrimary ? $"显示器 1（主）" : DeviceName;

    public override string ToString() => $"{DeviceName} {Bounds.Width}×{Bounds.Height} @ {Dpi} DPI";
}

public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty
            };

            if (!NativeMethods.GetMonitorInfo(hMonitor, ref info)) return true;

            int dpi = 96;
            try
            {
                if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX >= 96)
                {
                    dpi = (int)dpiX;
                }
            }
            catch (DllNotFoundException)
            {
                dpi = 96;
            }

            var bounds = PixelRect.FromLtrb(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
            var work = PixelRect.FromLtrb(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
            bool primary = (info.dwFlags & 1) == 1;
            monitors.Add(new MonitorInfo(string.IsNullOrEmpty(info.szDevice) ? "\\\\.\\DISPLAY" : info.szDevice, bounds, work, dpi, primary));
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            var (width, height) = NativeMethods.GetVirtualScreenBounds();
            monitors.Add(new MonitorInfo("\\\\.\\DISPLAY1", new PixelRect(0, 0, Math.Max(1, width), Math.Max(1, height)), new PixelRect(0, 0, Math.Max(1, width), Math.Max(1, height)), 96, true));
        }

        return monitors;
    }

    public static PixelRect GetVirtualBounds(IEnumerable<MonitorInfo> monitors)
    {
        var result = PixelRect.Empty;
        foreach (var monitor in monitors) result = PixelRect.Union(result, monitor.Bounds);
        return result;
    }

    public static MonitorInfo? FromPoint(PixelPoint point)
    {
        var all = GetMonitors();
        foreach (var monitor in all)
        {
            if (monitor.Bounds.Contains(point)) return monitor;
        }

        foreach (var monitor in all)
        {
            if (monitor.IsPrimary) return monitor;
        }

        return all.Count > 0 ? all[0] : null;
    }

    public static MonitorInfo? Primary
    {
        get
        {
            var all = GetMonitors();
            foreach (var monitor in all)
            {
                if (monitor.IsPrimary) return monitor;
            }

            return all.Count > 0 ? all[0] : null;
        }
    }

    public static PixelPoint GetCursorPosition()
    {
        NativeMethods.GetCursorPos(out var point);
        return new PixelPoint(point.X, point.Y);
    }
}
