using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Encoding;

/// <summary>
/// 录制引擎：捕获线程按帧率抓屏写入有界队列，编码线程边编码边写盘。
/// 队列满时丢弃最旧帧并记录丢帧，UI 线程不参与抓屏与编码。
/// </summary>
public sealed class RecordingEngine : IDisposable
{
    private const int QueueCapacity = 6;

    private readonly RecordingSession _session;
    private readonly MonitorInfo _monitor;
    private readonly string _tempPath;
    private readonly string _targetPath;
    private readonly GifOptions? _gifOptions;
    private readonly VideoOptions? _videoOptions;
    private readonly bool _includeCursor;
    private readonly HighlightOptions? _highlight;
    private readonly PixelRect _gifSize;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private BlockingCollection<Frame>? _queue;
    private Thread? _captureThread;
    private Thread? _encoderThread;
    private volatile bool _stopRequested;
    private int _failed;
    private long _bytes;
    private int _droppedFrames;
    private DateTime _lastProgress = DateTime.MinValue;
    private DateTime _lastDiskCheck = DateTime.MinValue;
    private bool _spaceWarningRaised;
    private Bitmap? _preview;
    private bool _disposed;

    public RecordingEngine(
        RecordingKind kind,
        PixelRect area,
        MonitorInfo monitor,
        string tempPath,
        string targetPath,
        GifOptions? gifOptions,
        VideoOptions? videoOptions,
        bool includeCursor,
        HighlightOptions? highlight = null)
    {
        Kind = kind;
        _monitor = monitor;
        _tempPath = tempPath;
        _targetPath = targetPath;
        _gifOptions = gifOptions;
        _videoOptions = videoOptions;
        _includeCursor = includeCursor;
        _highlight = highlight;

        int fps = kind == RecordingKind.Gif ? gifOptions?.Fps ?? 12 : videoOptions?.Fps ?? 30;
        _session = new RecordingSession(kind, area, fps, () => _clock.Elapsed);
        _gifSize = kind == RecordingKind.Gif ? ComputeGifSize(area, gifOptions?.MaxEdge ?? 0) : area;
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public RecordingKind Kind { get; }

    public RecordingSession Session => _session;

    public PixelRect Area => _session.Area;

    public PixelRect OutputSize => _session.Kind == RecordingKind.Gif ? _gifSize : _session.Area;

    public int DroppedFrames => _droppedFrames;

    public event Action<RecordingProgress>? Progress;

    public event Action<string, TimeSpan>? Completed;

    public event Action<string>? Failed;

    /// <summary>非致命提示（如磁盘空间将尽），录制可以继续。</summary>
    public event Action<string>? Warning;

    public void Start()
    {
        _session.BeginRecording();
        _queue = new BlockingCollection<Frame>(new ConcurrentQueue<Frame>(), QueueCapacity);
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "SnipTrace.Capture" };
        _encoderThread = new Thread(EncodeLoop) { IsBackground = true, Name = "SnipTrace.Encode" };
        _encoderThread.Start();
        _captureThread.Start();
        Report();
    }

    public void Pause()
    {
        if (_session.State == RecordingState.Recording)
        {
            _session.Pause();
            Report();
        }
    }

    public void Resume()
    {
        if (_session.State == RecordingState.Paused)
        {
            _session.Resume();
            Report();
        }
    }

    public void RequestStop()
    {
        lock (_gate)
        {
            if (_stopRequested) return;
            _stopRequested = true;
        }

        if (_session.State is RecordingState.Recording or RecordingState.Paused)
        {
            _session.Stop();
        }

        Report();
    }

    public void Cancel()
    {
        _session.Cancel();
        _cts.Cancel();
        try
        {
            _queue?.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CaptureLoop()
    {
        double intervalMs = 1000.0 / Math.Max(1, _session.Fps);
        var clock = Stopwatch.StartNew();
        double nextFrame = 0;

        while (!_cts.IsCancellationRequested)
        {
            if (_stopRequested) break;

            if (_session.State == RecordingState.Recording)
            {
                if (LimitReached())
                {
                    RequestStop();
                    break;
                }

                try
                {
                    long timestamp = _session.Elapsed.Ticks;
                    var bitmap = ScreenGrabber.CaptureArea(_session.Area, _monitor, _includeCursor);
                    if (bitmap is not null)
                    {
                        ApplyHighlight(bitmap);

                        if (_preview is null)
                        {
                            try
                            {
                                _preview = new Bitmap(bitmap);
                            }
                            catch (Exception)
                            {
                                _preview = null;
                            }
                        }

                        var frame = new Frame(bitmap, timestamp);
                        var queue = _queue;
                        if (queue is not null)
                        {
                            if (!queue.TryAdd(frame))
                            {
                                if (queue.TryTake(out var dropped))
                                {
                                    dropped.Dispose();
                                    Interlocked.Increment(ref _droppedFrames);
                                }

                                try
                                {
                                    queue.TryAdd(frame);
                                }
                                catch (InvalidOperationException)
                                {
                                    frame.Dispose();
                                }
                            }

                            _session.RegisterFrame();
                            ReportThrottled();
                        }
                        else
                        {
                            bitmap.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail(ex.Message);
                    break;
                }
            }

            double elapsed = clock.Elapsed.TotalMilliseconds;
            nextFrame += intervalMs;
            double wait = nextFrame - elapsed;
            if (wait > 0)
            {
                Thread.Sleep((int)Math.Min(wait, 100));
            }
            else if (wait < -300)
            {
                nextFrame = elapsed;
            }
        }

        try
        {
            _queue?.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>鼠标高亮（演示模式）：按设置在光标位置画半透明色圈，GIF 与 MP4 通用。</summary>
    private void ApplyHighlight(Bitmap bitmap)
    {
        var options = _highlight;
        if (options is null || !options.Enabled) return;

        try
        {
            var cursor = MonitorService.GetCursorPosition();
            int cx = cursor.X - _session.Area.Left;
            int cy = cursor.Y - _session.Area.Top;
            int d = Math.Min(options.Diameter, Math.Min(bitmap.Width, bitmap.Height));
            if (d < 4) return;
            if (cx + d < 0 || cy + d < 0 || cx - d > bitmap.Width || cy - d > bitmap.Height) return;

            var color = ColorTranslator.FromHtml(options.Color);
            int alpha = (int)Math.Round(255 * options.OpacityPercent / 100.0);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(alpha, color));
            g.FillEllipse(fill, cx - d / 2f, cy - d / 2f, d, d);
            using var ring = new Pen(Color.FromArgb(Math.Min(220, alpha + 90), color), 2f);
            g.DrawEllipse(ring, cx - d / 2f + 1, cy - d / 2f + 1, d - 2, d - 2);
        }
        catch (Exception)
        {
            // 高亮失败不影响录制。
        }
    }

    private void EncodeLoop()
    {
        try
        {
            if (_session.Kind == RecordingKind.Gif)
            {
                EncodeGif();
            }
            else
            {
                EncodeMp4();
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private void EncodeGif()
    {
        var options = _gifOptions ?? new GifOptions();
        using (var stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024))
        {
            using var encoder = new GifStreamEncoder(stream, _gifSize.Width, _gifSize.Height, options.HighQualityDither);
            foreach (var frame in _queue!.GetConsumingEnumerable())
            {
                try
                {
                    if (_cts.IsCancellationRequested) continue;
                    encoder.AddFrame(frame.Bitmap, options.FrameDelayMilliseconds);
                    Interlocked.Exchange(ref _bytes, stream.Position);
                    ReportThrottled();
                }
                finally
                {
                    frame.Dispose();
                }
            }

            if (!_cts.IsCancellationRequested)
            {
                encoder.Finish();
                Interlocked.Exchange(ref _bytes, stream.Position);
            }
        }

        CompleteOutput();
    }

    private void EncodeMp4()
    {
        var options = _videoOptions ?? new VideoOptions();
        int width = _session.Area.Width;
        int height = _session.Area.Height;
        int bitRate = options.ResolveBitRate(width, height);

        using (var encoder = new Mp4Encoder(_tempPath, width, height, _session.Fps, bitRate))
        {
            foreach (var frame in _queue!.GetConsumingEnumerable())
            {
                try
                {
                    if (_cts.IsCancellationRequested) continue;
                    encoder.WriteFrame(frame.Bitmap, frame.Timestamp);
                    ReportThrottled();
                }
                finally
                {
                    frame.Dispose();
                }
            }

            if (!_cts.IsCancellationRequested)
            {
                encoder.Finish();
            }
        }

        Interlocked.Exchange(ref _bytes, new FileInfo(_tempPath).Length);
        CompleteOutput();
    }

    private void CompleteOutput()
    {
        if (_cts.IsCancellationRequested || _session.State == RecordingState.Cancelled)
        {
            DeleteTemp();
            return;
        }

        try
        {
            OutputFile.MoveIntoPlace(_tempPath, _targetPath);
            _session.Complete(_targetPath);
            Report();
            Completed?.Invoke(_targetPath, _session.Elapsed);
        }
        catch (Exception ex)
        {
            Fail(L.T("无法写入目标文件：") + ex.Message);
        }
    }

    /// <summary>低于该值必须停止录制（仍会尝试封装已有内容）。</summary>
    private const long DiskHardFloorBytes = 300L * 1024 * 1024;

    /// <summary>低于该值给出一次预警，录制继续。</summary>
    private const long DiskSoftFloorBytes = 1024L * 1024 * 1024;

    private bool LimitReached()
    {
        if (DateTime.UtcNow - _lastDiskCheck > TimeSpan.FromSeconds(3))
        {
            _lastDiskCheck = DateTime.UtcNow;
            try
            {
                string? root = Path.GetPathRoot(_targetPath);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady && drive.AvailableFreeSpace < DiskHardFloorBytes)
                    {
                        // 空间见底时停止录制并交给正常封装流程，尽量保住已录内容。
                        RaiseWarning(L.T("磁盘剩余空间不足 300 MB，正在停止录制并封装已录制内容。"));
                        return true;
                    }

                    if (!_spaceWarningRaised && drive.IsReady && drive.AvailableFreeSpace < DiskSoftFloorBytes)
                    {
                        _spaceWarningRaised = true;
                        RaiseWarning(L.T("磁盘剩余空间不足 1 GB，请及时清理磁盘或停止录制。"));
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        if (_session.Kind != RecordingKind.Gif) return false;
        var options = _gifOptions ?? new GifOptions();
        if (_session.FrameCount >= options.MaxFrames) return true;
        return _session.Elapsed.TotalSeconds >= options.MaxDurationSeconds;
    }

    /// <summary>取走首帧预览，调用方负责释放。</summary>
    public Bitmap? TakePreview()
    {
        var preview = _preview;
        _preview = null;
        return preview;
    }

    private void ReportThrottled()
    {
        if (DateTime.UtcNow - _lastProgress < TimeSpan.FromMilliseconds(200)) return;
        _lastProgress = DateTime.UtcNow;
        Report();
    }

    private void RaiseWarning(string message)
    {
        try
        {
            Warning?.Invoke(message);
        }
        catch (Exception)
        {
        }
    }

    private void Report()
    {
        Progress?.Invoke(new RecordingProgress(
            _session.Kind,
            _session.State,
            _session.Elapsed,
            _session.FrameCount,
            Interlocked.Read(ref _bytes),
            _session.FaultMessage));
    }

    private void Fail(string message)
    {
        if (Interlocked.Exchange(ref _failed, 1) == 1) return;
        _session.Fail(message);
        try
        {
            _queue?.CompleteAdding();
        }
        catch (Exception)
        {
        }

        DeleteTemp();
        Report();
        Failed?.Invoke(message);
    }

    private void DeleteTemp()
    {
        try
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
        }
        catch (Exception)
        {
        }
    }

    private static PixelRect ComputeGifSize(PixelRect area, int maxEdge)
    {
        if (maxEdge <= 0 || (area.Width <= maxEdge && area.Height <= maxEdge)) return area;
        double scale = Math.Min((double)maxEdge / area.Width, (double)maxEdge / area.Height);
        return new PixelRect(0, 0, Math.Max(1, (int)Math.Round(area.Width * scale)), Math.Max(1, (int)Math.Round(area.Height * scale)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session.State is RecordingState.Recording or RecordingState.Paused) Cancel();
        _cts.Cancel();
        try
        {
            _captureThread?.Join(1500);
            _encoderThread?.Join(3000);
        }
        catch (Exception)
        {
        }

        _queue?.Dispose();
        _cts.Dispose();
    }

    private sealed class Frame : IDisposable
    {
        public Frame(Bitmap bitmap, long timestamp)
        {
            Bitmap = bitmap;
            Timestamp = timestamp;
        }

        public Bitmap Bitmap { get; }

        public long Timestamp { get; }

        public void Dispose() => Bitmap.Dispose();
    }
}
