using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Encoding;
using SnipTrace.App.Presentation.Recording;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Services;

/// <summary>录制编排：倒计时、边框、控制条、完成预览与异常清理。</summary>
public sealed class RecordingWorkflow
{
    private readonly AppServices _services;
    private RecordingEngine? _engine;
    private RecordingFrame? _frame;
    private RecordingControlBar? _bar;

    public RecordingWorkflow(AppServices services)
    {
        _services = services;
    }

    public bool IsRecording => _engine is not null;

    public async Task StartAsync(RecordingKind kind, PixelRect area, MonitorInfo monitor)
    {
        if (_engine is not null)
        {
            _services.Toasts.Warn(L.T("已有录制进行中"), L.T("请先停止当前录制。"));
            return;
        }

        // QQ 录屏式：先在控制条上确认格式与清晰度，再倒计时开始。
        RecordSetupChoice? choice;
        try
        {
            var setup = new RecordSetupBar(kind);
            choice = await setup.RunAsync(area, monitor);
        }
        catch (Exception ex)
        {
            _services.Toasts.Warn(L.T("无法打开录制设置"), ex.Message);
            return;
        }

        if (choice is null) return;

        kind = choice.Value.Kind;
        int bitRateMbps = choice.Value.BitRateMbps;

        var countdown = new CountdownWindow();
        string hint = kind == RecordingKind.Gif ? L.T("3 秒后开始 GIF 录制 · Esc 取消") : L.T("3 秒后开始屏幕录制 · Esc 取消");
        if (!await countdown.RunAsync(3, area, hint)) return;

        try
        {
            var settings = _services.Settings;
            string directory = kind == RecordingKind.Gif ? settings.ScreenshotDirectory : settings.VideoDirectory;
            _services.Paths.EnsureDirectory(directory);

            string extension = kind == RecordingKind.Gif ? ".gif" : ".mp4";
            string baseName = OutputNaming.TimestampedBaseName("SnipTrace", DateTime.Now);
            string target = OutputNaming.UniquePath(directory, baseName, extension);
            string temp = Path.Combine(_services.Paths.TempDirectory, baseName + extension);

            var videoOptions = CloneVideo(settings.Video);
            if (bitRateMbps > 0) videoOptions.BitRate = bitRateMbps * 1_000_000;
            var gifOptions = CloneGif(settings.Gif);

            // 高亮开启时 GIF 也带上光标，否则只剩色圈没有箭头会很怪。
            bool includeCursor = (kind == RecordingKind.Video && videoOptions.IncludeCursor) || settings.Highlight.Enabled;
            var engine = new RecordingEngine(kind, area, monitor, temp, target, gifOptions, videoOptions,
                includeCursor, settings.Highlight);
            _engine = engine;

            _frame = new RecordingFrame();
            _frame.Show(area);

            _bar = new RecordingControlBar(kind, engine.Session.Fps);
            _bar.PauseToggleRequested += TogglePause;
            _bar.StopRequested += StopRecording;
            _bar.CancelRequested += CancelRecording;
            _bar.Show();
            _bar.PlaceNear(area, monitor);

            engine.Progress += Engine_Progress;
            engine.Completed += Engine_Completed;
            engine.Failed += Engine_Failed;
            engine.Warning += Engine_Warning;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

            _services.Tray.SetRecordingState(true, $"正在录制 · {area.Width}×{area.Height}");
            _services.Shell?.SetTrayState(L.T("正在录制"));
            engine.Start();
        }
        catch (Exception ex)
        {
            ShutdownUi();
            _services.Toasts.Warn(L.T("无法开始录制"), ex.Message);
        }
    }

    private void Engine_Progress(RecordingProgress progress)
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(new Action(() =>
        {
            _bar?.UpdateProgress(progress);
            if (progress.State == RecordingState.Paused) _bar?.SetPaused(true);
            else if (progress.State == RecordingState.Recording) _bar?.SetPaused(false);
        }));
    }

    private void Engine_Completed(string path, TimeSpan duration)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => HandleCompleted(path, duration)));
    }

    private void Engine_Failed(string message)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => HandleFailed(message)));
    }

    private void Engine_Warning(string message)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _bar?.SetStatus(message);
            _services.Toasts.Warn(L.T("磁盘空间预警"), message);
        }));
    }

    private void HandleCompleted(string path, TimeSpan duration)
    {
        var engine = _engine;
        if (engine is null) return;

        int width = engine.OutputSize.Width;
        int height = engine.OutputSize.Height;
        var kind = engine.Kind;

        Bitmap? preview = engine.TakePreview();
        BitmapSource? source = preview is not null ? ImageUtil.ToBitmapSource(preview) : null;
        long bytes = 0;
        try
        {
            if (File.Exists(path)) bytes = new FileInfo(path).Length;
        }
        catch (Exception)
        {
        }

        ShutdownUi();
        Task.Run(() => engine.Dispose());

        if (preview is not null)
        {
            _services.Recent.Add(path,
                kind == RecordingKind.Gif ? RecentItemKind.Gif : RecentItemKind.Video,
                width, height, duration.TotalSeconds, preview);
            preview.Dispose();
        }

        new RecordingResultWindow(source, path, kind, duration, bytes).Show();
        _services.Toasts.Info(kind == RecordingKind.Gif ? L.T("GIF 已生成") : L.T("录屏已保存"), Path.GetFileName(path));
    }

    private void HandleFailed(string message)
    {
        var engine = _engine;
        ShutdownUi();
        if (engine is not null) Task.Run(() => engine.Dispose());
        _services.Toasts.Warn(L.T("录制失败"), message + L.T(" 临时文件已清理。"));
    }

    private void TogglePause()
    {
        var engine = _engine;
        if (engine is null) return;

        bool wasPaused = engine.Session.State == RecordingState.Paused;
        if (wasPaused) engine.Resume();
        else engine.Pause();

        _bar?.SetPaused(!wasPaused);
        _frame?.SetPaused(!wasPaused);
        _services.Tray.SetRecordingState(true, wasPaused ? L.T("正在录制") : L.T("已暂停"));
        _services.Shell?.SetTrayState(wasPaused ? L.T("正在录制") : L.T("已暂停"));
    }

    private void StopRecording()
    {
        var engine = _engine;
        if (engine is null) return;
        _bar?.SetStatus(L.T("正在完成封装，请稍候…"));
        engine.RequestStop();
    }

    private void CancelRecording()
    {
        var engine = _engine;
        if (engine is null) return;
        engine.Cancel();
        ShutdownUi();
        Task.Run(() => engine.Dispose());
        _services.Toasts.Info(L.T("已取消录制"), L.T("目标文件未生成，临时文件已清理。"));
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_engine is null) return;
            _services.Toasts.Warn(L.T("显示设置已变化"), L.T("录制已自动停止并完成封装。"));
            _bar?.SetStatus(L.T("显示设置变化，正在封装…"));
            _engine.RequestStop();
        }));
    }

    private static VideoOptions CloneVideo(VideoOptions source) => new()
    {
        Fps = source.Fps,
        IncludeCursor = source.IncludeCursor,
        BitRate = source.BitRate
    };

    private static GifOptions CloneGif(GifOptions source) => new()
    {
        Fps = source.Fps,
        MaxDurationSeconds = source.MaxDurationSeconds,
        MaxFrames = source.MaxFrames,
        MaxEdge = source.MaxEdge,
        HighQualityDither = source.HighQualityDither
    };

    /// <summary>关闭录制 UI 并恢复空闲状态。</summary>
    private void ShutdownUi()
    {
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;

        var engine = _engine;
        _engine = null;
        if (engine is not null)
        {
            engine.Progress -= Engine_Progress;
            engine.Completed -= Engine_Completed;
            engine.Failed -= Engine_Failed;
            engine.Warning -= Engine_Warning;
        }

        try
        {
            _frame?.Dispose();
        }
        catch (Exception)
        {
        }

        _frame = null;

        var bar = _bar;
        _bar = null;
        if (bar is not null)
        {
            try
            {
                bar.PauseToggleRequested -= TogglePause;
                bar.StopRequested -= StopRecording;
                bar.CancelRequested -= CancelRecording;
                bar.Close();
            }
            catch (Exception)
            {
            }
        }

        _services.Tray.SetRecordingState(false);
        _services.Shell?.SetTrayState(L.T("托盘常驻"));
    }
}
