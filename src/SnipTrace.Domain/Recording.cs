namespace SnipTrace.Domain;

public enum RecordingKind
{
    Gif,
    Video
}

public enum RecordingState
{
    Idle,
    CountingDown,
    Recording,
    Paused,
    Stopping,
    Completed,
    Cancelled,
    Faulted
}

/// <summary>
/// 录制会话状态机：暂停时间不计入成片时间轴。
/// </summary>
public sealed class RecordingSession
{
    private readonly Func<TimeSpan> _clock;
    private TimeSpan _recorded;
    private TimeSpan _resumedAt;
    private TimeSpan _pausedAt;

    public RecordingSession(RecordingKind kind, PixelRect area, int fps, Func<TimeSpan>? clock = null)
    {
        Kind = kind;
        Area = area;
        Fps = Math.Max(1, fps);
        _clock = clock ?? (() => TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond));
        _resumedAt = _clock();
    }

    public RecordingKind Kind { get; }

    public PixelRect Area { get; }

    public int Fps { get; }

    public RecordingState State { get; private set; } = RecordingState.Idle;

    public int FrameCount { get; private set; }

    public long BytesWritten { get; set; }

    public string? OutputPath { get; private set; }

    public string? FaultMessage { get; private set; }

    public TimeSpan PausedTotal { get; private set; }

    /// <summary>有效录制时长，不含暂停。</summary>
    public TimeSpan Elapsed
    {
        get
        {
            if (State == RecordingState.Recording)
            {
                var delta = _clock() - _resumedAt;
                return delta > TimeSpan.Zero ? _recorded + delta : _recorded;
            }

            return _recorded;
        }
    }

    public bool IsActive => State is RecordingState.CountingDown or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping;

    public void StartCountdown()
    {
        EnsureTransition(RecordingState.CountingDown, RecordingState.Idle);
        State = RecordingState.CountingDown;
    }

    public void BeginRecording()
    {
        EnsureTransition(RecordingState.Recording, RecordingState.CountingDown, RecordingState.Idle);
        _recorded = TimeSpan.Zero;
        _resumedAt = _clock();
        State = RecordingState.Recording;
    }

    public void Pause()
    {
        EnsureTransition(RecordingState.Paused, RecordingState.Recording);
        var now = _clock();
        var delta = now - _resumedAt;
        if (delta > TimeSpan.Zero) _recorded += delta;
        _pausedAt = now;
        State = RecordingState.Paused;
    }

    public void Resume()
    {
        EnsureTransition(RecordingState.Recording, RecordingState.Paused);
        var now = _clock();
        PausedTotal += now - _pausedAt;
        _resumedAt = now;
        State = RecordingState.Recording;
    }

    public void Stop()
    {
        EnsureTransition(RecordingState.Stopping, RecordingState.Recording, RecordingState.Paused);
        var now = _clock();
        if (State == RecordingState.Recording)
        {
            var delta = now - _resumedAt;
            if (delta > TimeSpan.Zero) _recorded += delta;
        }
        else
        {
            PausedTotal += now - _pausedAt;
        }

        State = RecordingState.Stopping;
    }

    public void Cancel()
    {
        if (State is RecordingState.Completed or RecordingState.Cancelled) return;
        State = RecordingState.Cancelled;
    }

    public void Complete(string outputPath)
    {
        EnsureTransition(RecordingState.Completed, RecordingState.Stopping);
        OutputPath = outputPath;
        State = RecordingState.Completed;
    }

    public void Fail(string message)
    {
        if (State is RecordingState.Completed or RecordingState.Cancelled) return;
        FaultMessage = message;
        State = RecordingState.Faulted;
    }

    public bool RegisterFrame()
    {
        if (State != RecordingState.Recording) return false;
        FrameCount++;
        return true;
    }

    private void EnsureTransition(RecordingState target, params RecordingState[] allowed)
    {
        if (!allowed.Contains(State))
        {
            throw new InvalidOperationException($"录制状态无法从 {State} 切换到 {target}。");
        }
    }
}

public sealed class GifOptions
{
    public static readonly int[] AvailableFps = { 5, 10, 12, 20, 25 };
    public static readonly int[] AvailableMaxEdges = { 0, 1280, 1920 };

    public int Fps { get; set; } = 12;

    public int MaxDurationSeconds { get; set; } = 120;

    public int MaxFrames { get; set; } = 2400;

    /// <summary>0 表示保持原始边长。</summary>
    public int MaxEdge { get; set; } = 1920;

    public bool HighQualityDither { get; set; }

    /// <summary>GIF 延迟以 10 ms 为最小单位。</summary>
    public int FrameDelayMilliseconds => Math.Max(20, (int)Math.Round(1000.0 / Math.Max(1, Fps) / 10) * 10);

    public void Normalize()
    {
        if (!AvailableFps.Contains(Fps)) Fps = 12;
        MaxDurationSeconds = Math.Clamp(MaxDurationSeconds, 1, 600);
        MaxFrames = Math.Clamp(MaxFrames, 10, 20000);
        if (!AvailableMaxEdges.Contains(MaxEdge)) MaxEdge = 1920;
    }

    public string? Validate()
    {
        if (!AvailableFps.Contains(Fps)) return "GIF 帧率必须是 5、10、12、20 或 25。";
        if (MaxDurationSeconds is < 1 or > 600) return "GIF 最长时长必须在 1–600 秒之间。";
        if (MaxFrames is < 10 or > 20000) return "GIF 最大帧数必须在 10–20000 之间。";
        if (!AvailableMaxEdges.Contains(MaxEdge)) return "GIF 最大边长必须是原始、1920 或 1280。";
        return null;
    }
}

public sealed class VideoOptions
{
    public static readonly int[] AvailableFps = { 24, 30, 60 };

    public int Fps { get; set; } = 30;

    public bool IncludeCursor { get; set; } = true;

    /// <summary>0 表示按分辨率自动估算。</summary>
    public int BitRate { get; set; }

    public void Normalize()
    {
        if (!AvailableFps.Contains(Fps)) Fps = 30;
        BitRate = Math.Clamp(BitRate, 0, 100_000_000);
    }

    public string? Validate()
    {
        if (!AvailableFps.Contains(Fps)) return "录制帧率必须是 24、30 或 60。";
        if (BitRate is < 0 or > 100_000_000) return "码率超出允许范围。";
        return null;
    }

    public int ResolveBitRate(int width, int height) => BitRate > 0 ? BitRate : MediaDimensions.EstimateBitRate(width, height);
}

public readonly record struct RecordingProgress(
    RecordingKind Kind,
    RecordingState State,
    TimeSpan Elapsed,
    int Frames,
    long Bytes,
    string? Message)
{
    public string ElapsedText => $"{Elapsed.Minutes:00}:{Elapsed.Seconds:00}";

    public string SizeText => Bytes >= 1024 * 1024
        ? $"{Bytes / 1024.0 / 1024.0:0.0} MB"
        : $"{Math.Max(0, Bytes) / 1024.0:0} KB";
}
