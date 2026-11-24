using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Infrastructure.Storage;
using SnipTrace.App.Presentation.Views;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Services;

/// <summary>应用组合根：设置、最近记录、托盘、快捷键与两套工作流。</summary>
public sealed class AppServices
{
    public AppServices()
    {
        Current = this;

        Paths = new PathService();
        Paths.EnsureDirectories();
        Paths.CleanupTemp();

        SettingsStore = new SettingsStore(Paths);
        Settings = SettingsStore.Load();

        // 首次运行沿用安装器里选择的语言；之后以用户设置为准。
        if (!File.Exists(Path.Combine(Paths.RootDirectory, "settings.json")))
        {
            try
            {
                if (Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\Software\SnipTrace", "Language", null) is string installed
                    && LanguageModes.All.Contains(installed, StringComparer.Ordinal))
                {
                    Settings.Language = installed;
                }
            }
            catch (Exception)
            {
                // 无注册表权限时保持默认语言。
            }
        }

        // 首选输出目录不可写时回退到应用数据目录，保证保存与录制始终可用。
        string screenshotDirectory = Settings.ScreenshotDirectory;
        if (!PathService.IsWritable(screenshotDirectory))
        {
            screenshotDirectory = Path.Combine(Paths.RootDirectory, "Screenshots");
            Paths.EnsureDirectory(screenshotDirectory);
        }

        string videoDirectory = Settings.VideoDirectory;
        if (!PathService.IsWritable(videoDirectory))
        {
            videoDirectory = Path.Combine(Paths.RootDirectory, "Videos");
            Paths.EnsureDirectory(videoDirectory);
        }

        Settings.ScreenshotDirectory = screenshotDirectory;
        Settings.VideoDirectory = videoDirectory;

        Recent = new RecentStore(Paths);
        Recent.Load();

        Toasts = new ToastService();
        Hotkeys = new HotkeyService();
        Tray = new TrayService();
        Tray.ApplySettings(Settings.TrayIconMode);
        Capture = new CaptureWorkflow(this);
        Recording = new RecordingWorkflow(this);

        Hotkeys.Triggered += OnHotkeyTriggered;
    }

    public static AppServices? Current { get; private set; }

    public PathService Paths { get; }

    public SettingsStore SettingsStore { get; }

    public RecentStore Recent { get; }

    public AppSettings Settings { get; private set; }

    public HotkeyService Hotkeys { get; }

    public TrayService Tray { get; }

    public ToastService Toasts { get; }

    public CaptureWorkflow Capture { get; }

    public RecordingWorkflow Recording { get; }

    public ShellWindow? Shell { get; set; }

    public event Action? SettingsChanged;

    public IReadOnlyList<string> ApplyHotkeys()
    {
        Hotkeys.UnregisterAll();
        Hotkeys.Failures.Clear();

        bool unified = Settings.HotkeyMode == HotkeyModes.Unified;
        Register("capture", Settings.CaptureHotkey);
        if (!unified)
        {
            // 统一快捷键模式：只唤起选区，GIF / 录屏在工具条里选择，不再单独注册。
            Register("gif", Settings.GifHotkey);
            Register("video", Settings.VideoHotkey);
        }

        Register("fullscreen", Settings.FullScreenHotkey);
        Register("allscreens", Settings.AllScreensHotkey);
        return Hotkeys.Failures.ToList();
    }

    public void SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>记住选区工具条「图形 / 箭头」上次选的子选项，下次开截图时沿用。</summary>
    public void RememberAnnotateVariant(string id, string variant)
    {
        var item = Settings.SelectionToolbar?.FirstOrDefault(entry => entry.Id == id);
        string normalized = SelectionToolbarItems.NormalizeVariant(id, variant);
        if (item is null || string.Equals(item.Variant, normalized, StringComparison.Ordinal)) return;

        item.Variant = normalized;
        SaveSettings();
    }

    public void NotifySettingsChanged()
    {
        ThemeManager.Apply(Settings.Theme);
        LanguageManager.Apply(Settings.Language);
        Tray.ApplySettings(Settings.TrayIconMode);
        SaveSettings();
        SettingsChanged?.Invoke();
        ApplyHotkeys();
    }

    /// <summary>保存 PNG 并写入最近记录，文件名冲突时追加序号。</summary>
    public string SaveScreenshot(BitmapSource image, string baseName, out string path)
    {
        string directory = Settings.ScreenshotDirectory;
        Paths.EnsureDirectory(directory);
        path = OutputNaming.UniquePath(directory, baseName, ".png");
        ImageUtil.SavePng(image, path);

        try
        {
            using var thumbnail = ImageUtil.ToBitmap(image);
            Recent.Add(path, RecentItemKind.Screenshot, image.PixelWidth, image.PixelHeight, 0, thumbnail);
        }
        catch (Exception)
        {
        }

        return path;
    }

    public string ScreenshotDirectory => Settings.ScreenshotDirectory;

    public string VideoDirectory => Settings.VideoDirectory;

    private void Register(string key, string text)
    {
        if (!HotkeyGesture.TryParse(text, out var gesture)) return;
        Hotkeys.Register(key, gesture, out _);
    }

    private void OnHotkeyTriggered(string key)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(new Action(() =>
        {
            switch (key)
            {
                case "capture":
                    _ = Capture.CaptureAsync(CaptureIntent.Screenshot);
                    break;
                case "gif":
                    _ = Capture.CaptureAsync(CaptureIntent.Gif);
                    break;
                case "video":
                    _ = Capture.CaptureAsync(CaptureIntent.Video);
                    break;
                case "fullscreen":
                    _ = Capture.CaptureFullScreenAsync(false);
                    break;
                case "allscreens":
                    _ = Capture.CaptureFullScreenAsync(true);
                    break;
            }
        }));
    }

    public void Dispose()
    {
        try
        {
            Hotkeys.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            Tray.Dispose();
        }
        catch (Exception)
        {
        }

        Toasts.Close();
        Current = null;
    }
}
