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

    private static Icon CreateIcon(Color accent, bool recording)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var accentBrush = new SolidBrush(accent);
            graphics.FillEllipse(accentBrush, 2, 2, 28, 28);
            using var whitePen = new Pen(Color.White, 2.2f);
            graphics.DrawRectangle(whitePen, 9, 10, 14, 11);
            graphics.DrawLine(whitePen, 12, 8, 20, 8);
            if (recording)
            {
                using var dotBrush = new SolidBrush(Color.White);
                graphics.FillEllipse(dotBrush, 14, 14, 4, 4);
            }
            else
            {
                using var dotBrush = new SolidBrush(Color.White);
                graphics.FillEllipse(dotBrush, 13, 13, 6, 6);
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

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _normalIcon.Dispose();
        _recordingIcon.Dispose();
    }
}
