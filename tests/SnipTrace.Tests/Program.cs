using SnipTrace.Domain;

namespace SnipTrace.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        NumberingTests();
        AnnotationTests();
        RecordingTests();
        SettingsTests();
        OutputTests();

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed}，失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void NumberingTests()
    {
        var doc = new AnnotationDocument(800, 600);
        Badge(doc, 100, 100);
        Badge(doc, 200, 200);
        Badge(doc, 300, 300);
        Check("序号自动递增", doc.NextNumber == 4, $"next={doc.NextNumber}");

        var last = doc.Annotations.OfType<NumberBadgeAnnotation>().Last();
        doc.Remove(last);
        Check("删除末尾编号后复用", doc.NextNumber == 3, $"next={doc.NextNumber}");

        doc.Undo();
        Check("撤销恢复标签", doc.Annotations.Count == 3);
        Check("撤销恢复编号", doc.NextNumber == 4, $"next={doc.NextNumber}");

        doc.Redo();
        Check("重做再次删除", doc.Annotations.Count == 2 && doc.NextNumber == 3);

        var doc2 = new AnnotationDocument(400, 400);
        var lower = Badge(doc2, 300, 300);
        var upper = Badge(doc2, 100, 100);
        doc2.RenumberByReadingOrder();
        Check("按阅读顺序重排", upper.Number == 1 && lower.Number == 2, $"{upper.Number},{lower.Number}");
        Check("重排后下一个编号", doc2.NextNumber == 3);

        doc2.ResetNumbering();
        Check("重置编号从 1 开始", doc2.NextNumber == 1);
        Check("重置不影响已有标签", doc2.Annotations.OfType<NumberBadgeAnnotation>().Count() == 2);
    }

    private static void AnnotationTests()
    {
        var arrow = new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(10, 10), new PointD(110, 10) });
        Check("箭头命中线上点", arrow.HitTest(new PointD(60, 11), 3));
        Check("箭头不命中远处点", !arrow.HitTest(new PointD(60, 60), 3));

        var rect = new ShapeAnnotation(AnnotationKind.Rectangle, new[] { new PointD(20, 20), new PointD(120, 80) });
        Check("矩形边框命中", rect.HitTest(new PointD(20, 50), 3));
        Check("矩形内部不命中", !rect.HitTest(new PointD(70, 50), 3));

        rect.Resize(new PixelRect(0, 0, 200, 120));
        Check("缩放后边界正确", rect.Bounds.Left <= 1 && rect.Bounds.Width >= 199,
            $"{rect.Bounds.Left},{rect.Bounds.Width}");

        int leftBeforeMove = rect.Bounds.Left;
        rect.Move(10, 5);
        Check("移动后左边界偏移", rect.Bounds.Left - leftBeforeMove == 10, rect.Bounds.Left.ToString());

        var badge = new NumberBadgeAnnotation { Number = 1, Center = new PointD(50, 50), FontSize = 16 };
        Check("序号标签命中中心", badge.HitTest(new PointD(50, 50), 0));
        Check("序号标签不命中外部", !badge.HitTest(new PointD(80, 50), 0));

        var text = new TextAnnotation { Text = "步骤一", Origin = new PointD(30, 30), FontSize = 20 };
        Check("文本命中范围", text.HitTest(new PointD(35, 35), 0));

        var doc = new AnnotationDocument(100, 100);
        doc.Add(rect);
        var hit = doc.HitTest(new PixelPoint(rect.Bounds.Left + 10, rect.Bounds.Top + 2));
        Check("文档命中测试", hit is not null, rect.Bounds.ToString());
    }

    private static void RecordingTests()
    {
        var time = TimeSpan.Zero;
        var session = new RecordingSession(RecordingKind.Gif, new PixelRect(0, 0, 100, 100), 12, () => time);
        session.StartCountdown();
        session.BeginRecording();
        time += TimeSpan.FromSeconds(2);
        session.Pause();
        var pausedAt = session.Elapsed;
        time += TimeSpan.FromSeconds(5);
        Check("暂停不推进时间轴", session.Elapsed == pausedAt, session.Elapsed.ToString());

        session.Resume();
        time += TimeSpan.FromSeconds(3);
        Check("继续后累计有效时长", Math.Abs(session.Elapsed.TotalSeconds - 5) < 0.01, session.Elapsed.ToString());
        Check("暂停累计", Math.Abs(session.PausedTotal.TotalSeconds - 5) < 0.01, session.PausedTotal.ToString());

        session.RegisterFrame();
        session.RegisterFrame();
        Check("帧计数", session.FrameCount == 2);

        session.Stop();
        Check("停止状态", session.State == RecordingState.Stopping);
        session.Complete(@"C:\temp\a.gif");
        Check("完成状态", session.State == RecordingState.Completed && session.OutputPath is not null);
        Check("停止后不再推进", session.Elapsed.TotalSeconds >= 5);

        var invalid = new RecordingSession(RecordingKind.Video, new PixelRect(0, 0, 10, 10), 30, () => time);
        bool threw = false;
        try
        {
            invalid.Resume();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Check("非法状态切换抛异常", threw);

        var cancelled = new RecordingSession(RecordingKind.Video, new PixelRect(0, 0, 10, 10), 30, () => time);
        cancelled.StartCountdown();
        cancelled.Cancel();
        Check("取消状态", cancelled.State == RecordingState.Cancelled);
    }

    private static void SettingsTests()
    {
        Check("快捷键解析 Ctrl+Shift+G", HotkeyGesture.TryParse("Ctrl+Shift+G", out var g) && g.VirtualKey == 'G' && g.Modifiers.HasFlag(HotkeyModifiers.Shift));
        Check("快捷键显示标准化", g.ToDisplayString() == "Ctrl+Shift+G", g.ToDisplayString());
        Check("快捷键解析 F1", HotkeyGesture.TryParse("F1", out var f1) && f1.VirtualKey == 0x70);
        Check("非法快捷键被拒绝", !HotkeyGesture.TryParse("Ctrl+", out _));

        var settings = new AppSettings
        {
            ScreenshotDirectory = string.Empty,
            Gif = new GifOptions { Fps = 13, MaxDurationSeconds = 9999, MaxEdge = 500 },
            Video = new VideoOptions { Fps = 31 },
            CaptureHotkey = "随意",
            GifHotkey = "Ctrl+Shift+G"
        };
        settings.Normalize(@"C:\shots", @"C:\videos");
        Check("截图目录回退", settings.ScreenshotDirectory == @"C:\shots");
        Check("GIF 帧率回退", settings.Gif.Fps == 12);
        Check("GIF 时长收敛", settings.Gif.MaxDurationSeconds == 600);
        Check("GIF 边长回退", settings.Gif.MaxEdge == 1920);
        Check("录屏帧率回退", settings.Video.Fps == 30);
        Check("非法快捷键回退", settings.CaptureHotkey == "F1", settings.CaptureHotkey);

        var conflict = new AppSettings { CaptureHotkey = "F1", GifHotkey = "F1" };
        Check("检测快捷键冲突", conflict.FindHotkeyConflict() is not null);

        var gif = new GifOptions { Fps = 12 };
        Check("GIF 延迟按 10ms 对齐", gif.FrameDelayMilliseconds == 80, gif.FrameDelayMilliseconds.ToString());

        var layout = new WindowLayout { Width = 10, Height = 100000, Left = double.NaN };
        layout.Normalize();
        Check("窗口尺寸收敛", layout.Width == 860 && layout.Height == 4000 && !double.IsNaN(layout.Left));
    }

    private static void OutputTests()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\out\shot.png", @"C:\out\shot (2).png" };
        var path = OutputNaming.UniquePath(@"C:\out", "shot", "png", taken.Contains);
        Check("文件名冲突追加序号", path == @"C:\out\shot (3).png", path);

        Check("H.264 奇数宽高裁偶数", MediaDimensions.H264EvenSize(1921, 1081) == (1920, 1080));
        Check("H.264 偶数不变", MediaDimensions.H264EvenSize(1920, 1080) == (1920, 1080));

        var bounds = new PixelRect(-1920, 0, 5760, 2160);
        var drag = AspectRatio.R16x9.FromDrag(new PixelPoint(-100, 100), new PixelPoint(500, 700), bounds);
        Check("比例锁定按 16:9", Math.Abs(drag.AspectRatio - 16.0 / 9) < 0.02, drag.ToString() + " " + drag.AspectRatio);
        Check("比例锁定限制在边界内", drag.Left >= bounds.Left && drag.Right <= bounds.Right);

        var centered = AspectRatio.R1x1.FitCentered(new PixelRect(0, 0, 400, 100), new PixelRect(0, 0, 800, 800));
        Check("切换比例以中心为基准", centered.Width == centered.Height && centered.Center.X == 200, centered.ToString());

        var free = AspectRatio.Free.FromDrag(new PixelPoint(0, 0), new PixelPoint(733, 419), new PixelRect(0, 0, 1000, 1000));
        Check("自由比例原样保留", free.Width == 733 && free.Height == 419, free.ToString());
    }

    private static NumberBadgeAnnotation Badge(AnnotationDocument doc, double x, double y)
        => doc.Add(new NumberBadgeAnnotation
        {
            Number = doc.NextNumber,
            Center = new PointD(x, y),
            FontSize = 16
        });

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}{(detail is null ? string.Empty : " -> " + detail)}");
        }
    }
}
