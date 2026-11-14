using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SnipTrace.App.Infrastructure.Interop;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>系统托盘常驻图标，提供全部功能的备用入口。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _normalIcon;
    private readonly Icon _recordingIcon;
    private ToolStripMenuItem? _statusItem;

    public TrayService()
    {
        _normalIcon = CreateIcon(Color.FromArgb(0xFF, 0x6B, 0x5F), false);
        _recordingIcon = CreateIcon(Color.FromArgb(0xF2, 0xB0, 0x2E), true);

        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateItem("区域截图", () => ScreenshotRequested?.Invoke()));
        menu.Items.Add(CreateItem("全屏截图（当前显示器）", () => FullScreenRequested?.Invoke()));
        menu.Items.Add(CreateItem("全屏截图（所有显示器）", () => AllScreensRequested?.Invoke()));
        menu.Items.Add(CreateItem("GIF 录制", () => GifRequested?.Invoke()));
        menu.Items.Add(CreateItem("屏幕录制", () => VideoRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateItem("主窗口", () => OpenRequested?.Invoke()));
        menu.Items.Add(CreateItem("设置", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        _statusItem = new ToolStripMenuItem("就绪") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateItem("退出", () => ExitRequested?.Invoke()));

        _notifyIcon = new NotifyIcon
        {
            Icon = _normalIcon,
            Text = "屏迹 SnipTrace",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public event Action? ScreenshotRequested;

    /// <summary>当前鼠标所在显示器全屏截图。</summary>
    public event Action? FullScreenRequested;

    /// <summary>所有显示器（虚拟桌面并集）全屏截图。</summary>
    public event Action? AllScreensRequested;

    public event Action? GifRequested;

    public event Action? VideoRequested;

    public event Action? OpenRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public void SetRecordingState(bool recording, string? text = null)
    {
        _notifyIcon.Icon = recording ? _recordingIcon : _normalIcon;
        _notifyIcon.Text = Truncate(recording ? text ?? "屏迹 SnipTrace · 正在录制" : "屏迹 SnipTrace");
        if (_statusItem is not null) _statusItem.Text = recording ? "正在录制" : "就绪";
    }

    public void ShowMessage(string title, string text, bool warning = false)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        catch (Exception)
        {
        }
    }

    private static string Truncate(string text) => text.Length <= 63 ? text : text[..63];

    private static ToolStripMenuItem CreateItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>托盘图标与 exe / 窗口图标保持同一图形：圆角方块 + 白色取景框 + 圆点。</summary>
    private static Icon CreateIcon(Color accent, bool recording)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var accentBrush = new SolidBrush(accent);
            using var rounded = RoundedRect(new RectangleF(2, 2, 28, 28), 8f);
            graphics.FillPath(accentBrush, rounded);

            using var whitePen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawRectangle(whitePen, 9f, 11f, 14f, 10f);
            graphics.DrawLine(whitePen, 12f, 8f, 20f, 8f);

            using var dotBrush = new SolidBrush(Color.White);
            if (recording)
            {
                graphics.FillEllipse(dotBrush, 14f, 14f, 4f, 4f);
            }
            else
            {
                graphics.FillEllipse(dotBrush, 13f, 13f, 6f, 6f);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _normalIcon.Dispose();
        _recordingIcon.Dispose();
    }
}
