using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SnipTrace.App.Infrastructure.Encoding;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Presentation.Services;
using SnipTrace.App.Presentation.Views;

namespace SnipTrace.App;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private AppServices? _services;
    private ShellWindow? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(arg => string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            int selfTestFailures = SelfTest.Run();
            Shutdown(selfTestFailures);
            return;
        }

        // 直接预览某个录制产物（排查/验收用）：--preview <文件路径>
        string? previewPath = ReadArgument(e.Args, "--preview");
        if (previewPath is not null && System.IO.File.Exists(previewPath))
        {
            var kind = System.IO.Path.GetExtension(previewPath)
                .Equals(".gif", StringComparison.OrdinalIgnoreCase)
                ? SnipTrace.Domain.RecordingKind.Gif
                : SnipTrace.Domain.RecordingKind.Video;

            var preview = new SnipTrace.App.Presentation.Recording.RecordingResultWindow(
                null, previewPath, kind, TimeSpan.Zero, new System.IO.FileInfo(previewPath).Length);
            MainWindow = preview;
            preview.Show();
            return;
        }

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.Acquire())
        {
            _singleInstance.SignalExistingInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _singleInstance.ActivationRequested += () => Dispatcher.BeginInvoke(new Action(() => _shell?.ShowAndActivate()));

        _services = new AppServices();
        ThemeManager.Apply(_services.Settings.Theme);
        _shell = new ShellWindow();
        _services.Shell = _shell;
        MainWindow = _shell;
        _shell.Show();

        // 供自动化截图/排查使用：--page settings 直接打开指定页面。
        string? startPage = ReadArgument(e.Args, "--page");
        if (startPage is not null) _shell.ShowPage(startPage);

        WireTray();
        DispatcherUnhandledException += OnUnhandledException;

        var failures = _services.ApplyHotkeys();
        if (failures.Count > 0 && _services.Settings.WarnHotkeyFailure)
        {
            _services.Toasts.Warn("快捷键注册失败", string.Join("；", failures) + "。可在设置中重新录入。");
        }

        if (_services.SettingsStore.LastLoadWarning is not null)
        {
            _services.Toasts.Warn("设置已恢复默认", _services.SettingsStore.LastLoadWarning);
        }

        Task.Run(ProbeMp4Encoder);
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }

        return null;
    }

    private void ProbeMp4Encoder()
    {
        var (available, reason) = Mp4Encoder.ProbeAvailability(_services?.Paths.TempDirectory);
        if (available) return;

        Dispatcher.BeginInvoke(new Action(() =>
            _services?.Toasts.Warn("MP4 录制不可用", reason ?? "未找到可用的 H.264 编码器。")));
    }

    private void WireTray()
    {
        if (_services is null) return;
        var tray = _services.Tray;
        var services = _services;

        tray.OpenRequested += () => Dispatcher.BeginInvoke(new Action(() => _shell?.ShowAndActivate()));
        tray.SettingsRequested += () => Dispatcher.BeginInvoke(new Action(() => _shell?.ShowPage("settings")));
        tray.ScreenshotRequested += () => Dispatcher.BeginInvoke(new Action(() => _ = services.Capture.CaptureAsync(CaptureIntent.Screenshot)));
        tray.FullScreenRequested += () => Dispatcher.BeginInvoke(new Action(() => _ = services.Capture.CaptureFullScreenAsync(false)));
        tray.AllScreensRequested += () => Dispatcher.BeginInvoke(new Action(() => _ = services.Capture.CaptureFullScreenAsync(true)));
        tray.GifRequested += () => Dispatcher.BeginInvoke(new Action(() => _ = services.Capture.CaptureAsync(CaptureIntent.Gif)));
        tray.VideoRequested += () => Dispatcher.BeginInvoke(new Action(() => _ = services.Capture.CaptureAsync(CaptureIntent.Video)));
        tray.ExitRequested += () => Dispatcher.BeginInvoke(new Action(ShutdownApplication));
    }

    private void ShutdownApplication()
    {
        if (_shell is not null)
        {
            _shell.AllowClose = true;
            _shell.Close();
        }

        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string directory = _services?.Paths.RootDirectory ?? System.IO.Path.GetTempPath();
            System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "error.log"),
                $"[{DateTime.Now:HH:mm:ss}] {e.Exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }

        _services?.Toasts.Warn("出现未处理的错误", e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
