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
        AnnotationEditingTests();
        ToolbarAndCanvasTests();
        BadgeShapeTests();
        RecordingTests();
        SettingsTests();
        OutputTests();
        OutputFileTests();
        ShellLayoutTests();

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

    private static void OutputFileTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "snipTrace_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "temp.mp4");
            File.WriteAllText(source, "data");
            string target = Path.Combine(root, "out", "clip.mp4");

            OutputFile.MoveIntoPlace(source, target);
            Check("落盘后临时文件被移除", !File.Exists(source));
            Check("落盘创建目标目录并写入", File.Exists(target) && new FileInfo(target).Length == 4);

            File.WriteAllText(source, "next");
            OutputFile.MoveIntoPlace(source, target);
            Check("落盘覆盖同名目标", new FileInfo(target).Length == 4 && !File.Exists(source));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (Exception)
            {
            }
        }

        var gif = new GifOptions { MaxFrames = 1, MaxDurationSeconds = 0 };
        Check("GIF 帧数越界被拦截", gif.Validate() is not null);
        gif.Normalize();
        Check("GIF 帧数与时长收敛", gif.MaxFrames == 10 && gif.MaxDurationSeconds == 1,
            $"{gif.MaxFrames}/{gif.MaxDurationSeconds}");
    }

    private static void ShellLayoutTests()
    {
        var def = ShellLayout.Resolve(1100, 720);
        Check("默认窗口为标准档", def.Mode == ShellLayoutMode.Standard, def.Mode.ToString());
        Check("默认窗口三列模式卡", !ShellLayout.UsesSingleColumn(def.Mode));
        Check("默认窗口高度压缩标题区", def.HeightTier == ShellHeightTier.Compact, def.HeightTier.ToString());
        Check("标准档宽导航", def.NavWidth == 220 && def.ContentWidth == 880, def.ToString());

        var expanded = ShellLayout.Resolve(1500, 1000);
        Check("展开档启用侧栏", expanded.Mode == ShellLayoutMode.Expanded
            && ShellLayout.UsesRecentSidebar(expanded.Mode, expanded.HeightTier)
            && expanded.HeightTier == ShellHeightTier.Full, expanded.ToString());

        var compact = ShellLayout.Resolve(1000, 900);
        Check("紧凑档窄导航单列", compact.Mode == ShellLayoutMode.Compact
            && compact.NavWidth == ShellLayout.NavNarrowWidth && ShellLayout.UsesSingleColumn(compact.Mode), compact.ToString());

        var minimal = ShellLayout.Resolve(859, 560);
        Check("极简档无左侧导航", minimal.Mode == ShellLayoutMode.Minimal
            && minimal.NavWidth == 0 && minimal.ContentWidth == 859, minimal.ToString());
        Check("极简档折叠最近记录", minimal.HeightTier == ShellHeightTier.Minimal
            && !ShellLayout.UsesRecentSidebar(minimal.Mode, minimal.HeightTier));

        Check("横向断点边界", ShellLayout.ResolveMode(1280) == ShellLayoutMode.Expanded
            && ShellLayout.ResolveMode(1279.9) == ShellLayoutMode.Standard
            && ShellLayout.ResolveMode(1040) == ShellLayoutMode.Standard
            && ShellLayout.ResolveMode(1039.9) == ShellLayoutMode.Compact
            && ShellLayout.ResolveMode(860) == ShellLayoutMode.Compact);
        Check("高度断点边界", ShellLayout.ResolveHeightTier(760) == ShellHeightTier.Full
            && ShellLayout.ResolveHeightTier(759.9) == ShellHeightTier.Compact
            && ShellLayout.ResolveHeightTier(600) == ShellHeightTier.Compact
            && ShellLayout.ResolveHeightTier(599.9) == ShellHeightTier.Minimal);
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

        Check("比例预设共八档且含原始",
            AspectRatio.Presets.Count == 8 && AspectRatio.Presets.Any(r => r.Label == "原始"),
            AspectRatio.Presets.Count.ToString());

        var original = AspectRatio.ForOriginal(new PixelRect(0, 0, 800, 600));
        Check("原始比例取自选区", Math.Abs(original.Value - 4.0 / 3) < 0.001, original.ToString());
    }

    /// <summary>序号标记形状几何：多边形顶点数、心形闭合采样与克隆保留形状/填充。</summary>
    private static void BadgeShapeTests()
    {
        Check("圆形由渲染层直接绘制", BadgeShapes.Outline(BadgeShape.Circle, 10).Count == 0);
        Check("三角三点", BadgeShapes.Outline(BadgeShape.Triangle, 10).Count == 3);
        Check("方形四点", BadgeShapes.Outline(BadgeShape.Square, 10).Count == 4);
        Check("五边五点", BadgeShapes.Outline(BadgeShape.Pentagon, 10).Count == 5);
        Check("六边六点", BadgeShapes.Outline(BadgeShape.Hexagon, 10).Count == 6);

        var triangle = BadgeShapes.Outline(BadgeShape.Triangle, 10);
        Check("多边形顶点朝上", Math.Abs(triangle[0].X) < 0.001 && triangle[0].Y < 0,
            $"{triangle[0].X:0.##},{triangle[0].Y:0.##}");

        // 正心形 ♥：左右对称、宽度贴合直径、上圆瓣 + 下尖点、顶部凹口浅于瓣顶。
        var heart = BadgeShapes.Outline(BadgeShape.Heart, 10);
        Check("心形闭合采样", heart.Count >= 40, heart.Count.ToString());
        Check("心形宽度对齐直径", Math.Abs(heart.Max(p => Math.Abs(p.X)) - 10) < 0.01,
            heart.Max(p => Math.Abs(p.X)).ToString("0.##"));
        Check("心形左右对称", Math.Abs(heart.Sum(p => p.X)) < 0.5, heart.Sum(p => p.X).ToString("0.##"));

        double top = heart.Min(p => p.Y);
        double bottom = heart.Max(p => p.Y);
        Check("心形上圆下尖", top < -8 && bottom > 8, $"{top:0.##},{bottom:0.##}");

        double notch = heart.Where(p => Math.Abs(p.X) < 0.01).Min(p => p.Y);
        Check("心形凹口浅于瓣顶", notch > top + 1 && notch < 0, $"{notch:0.##} vs {top:0.##}");

        var badge = new NumberBadgeAnnotation { Shape = BadgeShape.Heart, FillMode = BadgeFillMode.Outline };
        var clone = (NumberBadgeAnnotation)badge.Clone();
        Check("克隆保留形状与填充", clone.Shape == BadgeShape.Heart && clone.FillMode == BadgeFillMode.Outline);

        // 多段箭头：折线命中、端头与克隆。
        var arrow = new ShapeAnnotation(AnnotationKind.Arrow,
            new[] { new PointD(10, 10), new PointD(60, 10), new PointD(60, 60) })
        {
            ArrowHead = ArrowHead.Both
        };
        Check("多段箭头命中第二段", arrow.HitTest(new PointD(60, 40), 2));
        Check("多段箭头不命中内部空白", !arrow.HitTest(new PointD(20, 50), 2));
        var arrowClone = (ShapeAnnotation)arrow.Clone();
        Check("克隆保留箭头端头与折线",
            arrowClone.ArrowHead == ArrowHead.Both && arrowClone.Points.Count == 3 && arrowClone.Kind == AnnotationKind.Arrow);

        var straight = new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(0, 0), new PointD(80, 0) });
        Check("默认箭头仅末端有头", straight.ArrowHead == ArrowHead.End);
    }

    /// <summary>选区工具条配置（增删 / 排序）与标注画布尺寸同步。</summary>
    private static void ToolbarAndCanvasTests()
    {
        static int Index(List<ToolbarItemSetting> list, string id) => list.FindIndex(item => item.Id == id);

        var defaults = SelectionToolbarItems.CreateDefault();
        Check("工具条默认按钮数", defaults.Count == SelectionToolbarItems.All.Length, defaults.Count.ToString());
        Check("工具条默认全显示", defaults.All(item => item.Visible));
        Check("工具条默认顺序", defaults.Select(item => item.Id).SequenceEqual(SelectionToolbarItems.All));

        var custom = new List<ToolbarItemSetting>
        {
            new() { Id = SelectionToolbarItems.Refine },
            new() { Id = SelectionToolbarItems.Copy, Visible = false },
            new() { Id = "不存在的按钮" },
            new() { Id = SelectionToolbarItems.Refine },
            new() { Id = SelectionToolbarItems.Select }
        };
        var normalized = SelectionToolbarItems.Normalize(custom);
        Check("工具条去重并忽略未知项",
            normalized.Count == SelectionToolbarItems.All.Length
            && normalized.Count(item => item.Id == SelectionToolbarItems.Refine) == 1
            && normalized.All(item => item.Id != "不存在的按钮")
            && Index(normalized, SelectionToolbarItems.Refine) < Index(normalized, SelectionToolbarItems.Copy)
            && Index(normalized, SelectionToolbarItems.Copy) < Index(normalized, SelectionToolbarItems.Select),
            string.Join(",", normalized.Select(i => i.Id).Take(4)));
        Check("工具条保留隐藏状态", !normalized[Index(normalized, SelectionToolbarItems.Copy)].Visible);
        Check("工具条补齐缺失按钮", normalized.Count == SelectionToolbarItems.All.Length, normalized.Count.ToString());

        // 往返持久化：camelCase JSON 能读回顺序与隐藏状态。
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var readOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var custom2 = new AppSettings
        {
            SelectionToolbar = new List<ToolbarItemSetting>
            {
                new() { Id = SelectionToolbarItems.Refine },
                new() { Id = SelectionToolbarItems.Copy, Visible = false },
                new() { Id = SelectionToolbarItems.Select }
            }
        };
        var saved = System.Text.Json.JsonSerializer.Serialize(custom2, options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(saved, readOptions);
        restored?.Normalize(Path.GetTempPath(), Path.GetTempPath());
        Check("工具条配置往返持久化",
            restored is not null
            && restored.SelectionToolbar.Count == SelectionToolbarItems.All.Length
            && Index(restored.SelectionToolbar, SelectionToolbarItems.Refine) < Index(restored.SelectionToolbar, SelectionToolbarItems.Copy)
            && Index(restored.SelectionToolbar, SelectionToolbarItems.Copy) < Index(restored.SelectionToolbar, SelectionToolbarItems.Select)
            && !restored.SelectionToolbar[Index(restored.SelectionToolbar, SelectionToolbarItems.Copy)].Visible,
            restored is null ? "反序列化为空" : string.Join(",", restored.SelectionToolbar.Select(i => i.Id + (i.Visible ? "+" : "-")).Take(4)));

        // 缺失按钮按默认相对位置插回（而非追加末尾）。
        var partial = new List<ToolbarItemSetting>
        {
            new() { Id = SelectionToolbarItems.Copy },
            new() { Id = SelectionToolbarItems.Select }
        };
        var filled = SelectionToolbarItems.Normalize(partial);
        Check("缺失按钮按默认位置插回",
            filled[0].Id == SelectionToolbarItems.Ratio
            && Index(filled, SelectionToolbarItems.Eraser) > Index(filled, SelectionToolbarItems.Pen)
            && Index(filled, SelectionToolbarItems.Eraser) < Index(filled, SelectionToolbarItems.Copy)
            && filled.Count == SelectionToolbarItems.All.Length,
            string.Join(",", filled.Select(i => i.Id).Take(6)));

        // 橡皮：只擦画笔 / 高亮，文本、箭头、图形不动。
        var eraseDoc = new AnnotationDocument(300, 200);
        var penStroke = eraseDoc.Add(new ShapeAnnotation(AnnotationKind.Pen, new[] { new PointD(20, 50), new PointD(200, 50) }));
        var highlightStroke = eraseDoc.Add(new ShapeAnnotation(AnnotationKind.Highlight, new[] { new PointD(20, 120), new PointD(200, 120) }));
        var keepText = eraseDoc.Add(new TextAnnotation { Origin = new PointD(20, 160), Text = "保留" });
        var keepArrow = eraseDoc.Add(new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(20, 20), new PointD(120, 20) }));
        var keepRect = eraseDoc.Add(new ShapeAnnotation(AnnotationKind.Rectangle, new[] { new PointD(240, 20), new PointD(290, 80) }));

        var erased = AnnotationEditing.EraseStrokes(eraseDoc, new PointD(60, 40), new PointD(60, 130));
        Check("橡皮擦掉画笔与高亮",
            erased.Count == 2 && !eraseDoc.Annotations.Contains(penStroke) && !eraseDoc.Annotations.Contains(highlightStroke),
            erased.Count.ToString());
        Check("橡皮不动文本/箭头/图形",
            eraseDoc.Annotations.Contains(keepText) && eraseDoc.Annotations.Contains(keepArrow) && eraseDoc.Annotations.Contains(keepRect),
            eraseDoc.Annotations.Count.ToString());

        var erasedNothing = AnnotationEditing.EraseStrokes(eraseDoc, new PointD(60, 190), new PointD(90, 190));
        Check("橡皮空拖不改文档", erasedNothing.Count == 0 && eraseDoc.Annotations.Count == 3,
            erasedNothing.Count.ToString());

        var canvas = new AnnotationDocument(200, 120);
        var badge = canvas.Add(new NumberBadgeAnnotation { Number = 1, Center = new PointD(50, 40) });
        canvas.ResizeCanvas(320, 240);
        Check("画布尺寸随之调整", canvas.ImageWidth == 320 && canvas.ImageHeight == 240,
            $"{canvas.ImageWidth}×{canvas.ImageHeight}");
        Check("改画布不动标注坐标", badge.Center.X == 50 && badge.Center.Y == 40);
        canvas.ResizeCanvas(0, -10);
        Check("画布尺寸至少 1", canvas.ImageWidth >= 1 && canvas.ImageHeight >= 1);

        // 拖角改选区：对角固定、越界裁剪、最小尺寸保护。
        var selection = new PixelRect(100, 100, 200, 150);
        var bounds = new PixelRect(0, 0, 500, 400);

        Check("四角命中", RectResize.HitCorner(selection, new PixelPoint(102, 98)) == RectCorner.TopLeft
            && RectResize.HitCorner(selection, new PixelPoint(299, 101)) == RectCorner.TopRight
            && RectResize.HitCorner(selection, new PixelPoint(101, 249)) == RectCorner.BottomLeft
            && RectResize.HitCorner(selection, new PixelPoint(298, 252)) == RectCorner.BottomRight);
        Check("选区中间不命中角", RectResize.HitCorner(selection, new PixelPoint(200, 170)) == RectCorner.None);

        var grown = RectResize.FromCorner(selection, RectCorner.BottomRight, new PixelPoint(380, 330), bounds);
        Check("拖右下角向右下扩大", grown.Left == 100 && grown.Top == 100 && grown.Right == 380 && grown.Bottom == 330,
            $"{grown.Left},{grown.Top},{grown.Right},{grown.Bottom}");

        var shrunk = RectResize.FromCorner(selection, RectCorner.TopLeft, new PixelPoint(150, 130), bounds);
        Check("拖左上角保持右下角不动", shrunk.Right == 300 && shrunk.Bottom == 250 && shrunk.Left == 150 && shrunk.Top == 130,
            $"{shrunk.Left},{shrunk.Top},{shrunk.Right},{shrunk.Bottom}");

        var clamped = RectResize.FromCorner(selection, RectCorner.BottomRight, new PixelPoint(900, 900), bounds);
        Check("拖出屏幕被裁剪", clamped.Right == bounds.Right && clamped.Bottom == bounds.Bottom,
            $"{clamped.Right},{clamped.Bottom}");

        var tooSmall = RectResize.FromCorner(selection, RectCorner.BottomRight, new PixelPoint(103, 104), bounds);
        Check("小于最小尺寸保持原样", tooSmall.Equals(selection), tooSmall.ToString());
    }

    /// <summary>覆盖层「选择 + 滚轮」的纯逻辑：命中优先级、线宽 / 字号微调、缩放下限。</summary>
    private static void AnnotationEditingTests()
    {
        var document = new AnnotationDocument(200, 200);
        var below = document.Add(new ShapeAnnotation(AnnotationKind.Rectangle,
            new[] { new PointD(20, 20), new PointD(100, 100) }));
        var above = document.Add(new ShapeAnnotation(AnnotationKind.Rectangle,
            new[] { new PointD(20, 20), new PointD(100, 100) }));

        Check("后画的标注优先命中", ReferenceEquals(AnnotationEditing.HitTest(document, new PointD(20, 60)), above));
        Check("空白处不命中", AnnotationEditing.HitTest(document, new PointD(60, 60)) is null);
        Check("命中外框时命中内框", ReferenceEquals(AnnotationEditing.HitTest(document, new PointD(100, 60)), above),
            below.Id == above.Id ? "同一对象" : "外框边线");

        var rect = new ShapeAnnotation(AnnotationKind.Rectangle, new[] { new PointD(0, 0), new PointD(50, 50) });
        AnnotationEditing.AdjustOnWheel(rect, true);
        Check("图形滚轮加粗一线", Math.Abs(rect.Style.Width - 4) < 0.001, rect.Style.Width.ToString("0.##"));

        for (int i = 0; i < 40; i++) AnnotationEditing.AdjustOnWheel(rect, true);
        Check("线宽上限 16", Math.Abs(rect.Style.Width - AnnotationEditing.MaxStrokeWidth) < 0.001, rect.Style.Width.ToString("0.##"));

        for (int i = 0; i < 40; i++) AnnotationEditing.AdjustOnWheel(rect, false);
        Check("线宽下限 1", Math.Abs(rect.Style.Width - AnnotationEditing.MinStrokeWidth) < 0.001, rect.Style.Width.ToString("0.##"));

        var arrow = new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(0, 0), new PointD(80, 0) });
        double before = arrow.Style.Width;
        AnnotationEditing.AdjustOnWheel(arrow, true);
        Check("箭头滚轮改线宽而非缩放",
            Math.Abs(arrow.Style.Width - (before + 1)) < 0.001 && Math.Abs(arrow.Points[1].X - 80) < 0.001,
            arrow.Style.Width.ToString("0.##"));

        var text = new TextAnnotation { Text = "abc", FontSize = 20 };
        AnnotationEditing.AdjustOnWheel(text, true);
        Check("文字滚轮改字号", text.FontSize > 20, text.FontSize.ToString("0.##"));

        var pen = new ShapeAnnotation(AnnotationKind.Pen, new[] { new PointD(0, 0), new PointD(100, 0) });
        AnnotationEditing.AdjustOnWheel(pen, false);
        Check("画笔滚轮仍是缩放", pen.Points[1].X < 100, pen.Points[1].X.ToString("0.##"));

        var tiny = new ShapeAnnotation(AnnotationKind.Pen, new[] { new PointD(0, 0), new PointD(5, 0) });
        AnnotationEditing.AdjustOnWheel(tiny, false);
        Check("缩小到 6 px 以下即停止", Math.Abs(tiny.Points[1].X - 5) < 0.001, tiny.Points[1].X.ToString("0.##"));
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
