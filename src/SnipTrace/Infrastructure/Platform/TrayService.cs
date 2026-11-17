using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;
using SnipTrace.App.Infrastructure.Interop;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>系统托盘常驻图标，提供全部功能的备用入口。</summary>
public sealed class TrayService : IDisposable
{
    private static readonly Color AccentColor = Color.FromArgb(0xFF, 0x6B, 0x5F);
    private static readonly Color RecordingColor = Color.FromArgb(0xF2, 0xB0, 0x2E);

    /// <summary>深色任务栏上用的浅色块，浅色任务栏上用的深色块。</summary>
    private static readonly Color LightBlock = Color.FromArgb(0xE9, 0xEE, 0xF4);
    private static readonly Color DarkBlock = Color.FromArgb(0x2B, 0x34, 0x40);
    private static readonly Color DarkGlyph = Color.FromArgb(0x16, 0x20, 0x2A);

    private readonly NotifyIcon _notifyIcon;
    private readonly Dictionary<string, Icon> _icons = new();
    private readonly ToolStripMenuItem? _statusItem;

    private string _mode = TrayIconModes.Auto;
    private bool _recording;

    public TrayService()
    {
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
            Icon = ResolveIcon(),
            Text = "屏迹 SnipTrace",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };

        // 任务栏浅 / 深色切换时，自动模式要跟着换图标。
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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

    /// <summary>应用托盘图标配色设置（自动 / 彩色 / 浅色）。</summary>
    public void ApplySettings(string? mode)
    {
        _mode = TrayIconModes.Normalize(mode);
        RefreshIcon();
    }

    public void SetRecordingState(bool recording, string? text = null)
    {
        _recording = recording;
        _notifyIcon.Icon = ResolveIcon();
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

    /// <summary>当前设置下应该显示的图标（录制中固定用琥珀色）。</summary>
    internal Icon ResolveIcon()
    {
        if (_recording) return GetIcon("recording", RecordingColor, Color.White, true);

        return _mode switch
        {
            TrayIconModes.Color => GetIcon("color", AccentColor, Color.White, false),
            TrayIconModes.Light => GetIcon("light", LightBlock, DarkGlyph, false),
            _ => TaskbarUsesLightTheme()
                ? GetIcon("dark", DarkBlock, Color.White, false)
                : GetIcon("light", LightBlock, DarkGlyph, false)
        };
    }

    /// <summary>任务栏是否为浅色主题；读不到时按深色任务栏处理。</summary>
    internal static bool TaskbarUsesLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RefreshIcon()
    {
        try
        {
            _notifyIcon.Icon = ResolveIcon();
        }
        catch (Exception)
        {
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != TrayIconModes.Auto) return;
        if (e.Category != UserPreferenceCategory.General) return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(RefreshIcon));
    }

    private Icon GetIcon(string key, Color block, Color glyph, bool recording)
    {
        if (_icons.TryGetValue(key, out Icon? cached)) return cached;

        Icon icon = CreateIcon(block, glyph, recording);
        _icons[key] = icon;
        return icon;
    }

    private static string Truncate(string text) => text.Length <= 63 ? text : text[..63];

    private static ToolStripMenuItem CreateItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>托盘图标与 exe / 窗口图标保持同一图形：圆角方块 + 取景框 + 圆点，方块与线条配色可变。</summary>
    private static Icon CreateIcon(Color block, Color glyph, bool recording)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var blockBrush = new SolidBrush(block);
            using var rounded = RoundedRect(new RectangleF(2, 2, 28, 28), 8f);
            graphics.FillPath(blockBrush, rounded);

            using var glyphPen = new Pen(glyph, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawRectangle(glyphPen, 9f, 11f, 14f, 10f);
            graphics.DrawLine(glyphPen, 12f, 8f, 20f, 8f);

            using var dotBrush = new SolidBrush(glyph);
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
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        foreach (Icon icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
