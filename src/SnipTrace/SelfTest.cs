using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelFormats = System.Windows.Media.PixelFormats;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Encoding;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.App.Presentation.Recording;
using SnipTrace.App.Presentation.Selection;
using SnipTrace.App.Presentation.Views;
using SnipTrace.Domain;
using Color = System.Drawing.Color;

namespace SnipTrace.App;

/// <summary>
/// 编码管线自检（--selftest）：验证 GIF 可解码、帧去重、循环标记与 MP4 编码可用性。
/// 结果写入 %TEMP%\SnipTraceSelfTest\result.txt。
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        int failures = 0;
        string root = Path.Combine(AppContext.BaseDirectory, "selftest");
        Directory.CreateDirectory(root);
        string logPath = Path.Combine(root, "result.txt");
        using var writer = new StreamWriter(logPath, false) { AutoFlush = true };

        void Check(string name, bool ok, string? detail = null)
        {
            if (!ok) failures++;
            string line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? string.Empty : " -> " + detail)}";
            writer.WriteLine(line);
            Console.WriteLine(line);
        }

        try
        {
            const int width = 64;
            const int height = 40;
            string gifPath = Path.Combine(root, "roundtrip.gif");
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Red);
                    using var blue = new SolidBrush(Color.Blue);
                    graphics.FillRectangle(blue, width / 2, 0, width / 2, height);
                }

                using var stream = new FileStream(gifPath, FileMode.Create, FileAccess.Write);
                using var encoder = new GifStreamEncoder(stream, width, height);
                encoder.AddFrame(bitmap, 100);
                encoder.AddFrame(bitmap, 100);
            }

            using (var decoded = new Bitmap(gifPath))
            {
                Check("GIF 尺寸保持", decoded.Width == width && decoded.Height == height, $"{decoded.Width}×{decoded.Height}");
                var left = decoded.GetPixel(10, 10);
                var right = decoded.GetPixel(50, 10);
                Check("GIF 颜色正确", Near(left, Color.Red) && Near(right, Color.Blue), $"{left} / {right}");
                int frames = decoded.GetFrameCount(FrameDimension.Time);
                Check("GIF 连续重复帧合并", frames == 1, frames.ToString());
            }

            Check("GIF 循环标记", ContainsLoopBlock(gifPath));

            // 大画布 + 多颜色：覆盖 LZW 码长 9→12 位的增长与码表重置路径。
            // 码长时序错位时像素流会被截断，表现为只有顶部一条内容、其余发黑。
            const int bigWidth = 900;
            const int bigHeight = 350;
            string bigGifPath = Path.Combine(root, "large.gif");
            using (var source = new Bitmap(bigWidth, bigHeight, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(source))
                {
                    for (int y = 0; y < bigHeight; y += 10)
                    {
                        using var brush = new SolidBrush(Color.FromArgb(255, (y * 7) % 256, (y * 3) % 256, (y * 11) % 256));
                        graphics.FillRectangle(brush, 0, y, bigWidth, 10);
                    }
                }

                using (var stream = new FileStream(bigGifPath, FileMode.Create, FileAccess.Write))
                {
                    using var encoder = new GifStreamEncoder(stream, bigWidth, bigHeight);
                    encoder.AddFrame(source, 100);
                }

                using var decoded = new Bitmap(bigGifPath);
                var expected = source.GetPixel(10, bigHeight - 5);
                var actual = decoded.GetPixel(10, bigHeight - 5);
                Check("GIF 大画布像素流完整", Near(actual, expected), $"{actual} / 期望 {expected}");
            }

            var (mp4Available, reason) = Mp4Encoder.ProbeAvailability(root);
            Check("MP4 编码器可用", mp4Available, reason);

            if (mp4Available)
            {
                string mp4Path = Path.Combine(root, "roundtrip.mp4");
                using (var bitmap = new Bitmap(320, 240, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.DarkSlateBlue);
                    using var encoder = new Mp4Encoder(mp4Path, 320, 240, 30, 2_000_000);
                    for (int i = 0; i < 10; i++) encoder.WriteFrame(bitmap, i * 333_333L);
                    encoder.Finish();
                }

                var info = new FileInfo(mp4Path);
                Check("MP4 文件生成", info.Exists && info.Length > 1000, info.Exists ? info.Length + " 字节" : "缺失");
            }

            var document = new AnnotationDocument(width, height);
            document.Add(new NumberBadgeAnnotation { Number = 1, Center = new PointD(20, 20), FontSize = 16 });
            using (var source = new Bitmap(gifPath))
            {
                var rendered = AnnotationRenderer.Render(ImageUtil.ToBitmapSource(source), document);
                Check("导出严格 1:1 像素", rendered.PixelWidth == width && rendered.PixelHeight == height,
                    $"{rendered.PixelWidth}×{rendered.PixelHeight}");
            }

            // 序号标记形状冒烟：所有形状 × 填充/边框 渲染成 PNG 供人工验收。
            try
            {
                var shapes = new[]
                {
                    (BadgeShape.Circle, "圆"), (BadgeShape.Triangle, "三角"), (BadgeShape.Square, "方"),
                    (BadgeShape.Pentagon, "五边"), (BadgeShape.Hexagon, "六边"), (BadgeShape.Heart, "心")
                };
                var badgeDoc = new AnnotationDocument(560, 330);
                for (int i = 0; i < shapes.Length; i++)
                {
                    badgeDoc.Add(new NumberBadgeAnnotation
                    {
                        Number = i + 1,
                        Center = new PointD(50 + i * 88, 78),
                        FontSize = 22,
                        Shape = shapes[i].Item1,
                        FillMode = BadgeFillMode.Fill
                    });
                    badgeDoc.Add(new NumberBadgeAnnotation
                    {
                        Number = i + 1,
                        Center = new PointD(50 + i * 88, 165),
                        FontSize = 22,
                        Shape = shapes[i].Item1,
                        FillMode = BadgeFillMode.Outline,
                        Style = new AnnotationStyle(0xFF4C8DFF, 3, 1)
                    });
                }

                // 箭头端头 / 多段 / 直角折线：与编辑器工具条选项一一对应。
                var arrowStyle = new AnnotationStyle(0xFFFF6B5F, 3, 1);
                badgeDoc.Add(new ShapeAnnotation(AnnotationKind.Arrow,
                    new[] { new PointD(40, 225), new PointD(150, 225) }, arrowStyle) { ArrowHead = ArrowHead.End });
                badgeDoc.Add(new ShapeAnnotation(AnnotationKind.Arrow,
                    new[] { new PointD(200, 225), new PointD(310, 225) }, arrowStyle) { ArrowHead = ArrowHead.Both });
                badgeDoc.Add(new ShapeAnnotation(AnnotationKind.Arrow,
                    new[] { new PointD(360, 225), new PointD(470, 225) }, arrowStyle) { ArrowHead = ArrowHead.None });
                badgeDoc.Add(new ShapeAnnotation(AnnotationKind.Arrow,
                    new[] { new PointD(60, 265), new PointD(170, 265), new PointD(170, 300), new PointD(300, 300) }, arrowStyle)
                {
                    ArrowHead = ArrowHead.End,
                    Elbow = true
                });
                badgeDoc.Add(new ShapeAnnotation(AnnotationKind.Arrow,
                    new[] { new PointD(360, 265), new PointD(420, 300), new PointD(470, 265), new PointD(520, 300) }, arrowStyle)
                {
                    ArrowHead = ArrowHead.End
                });

                using (var canvas = new Bitmap(560, 330, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(canvas)) graphics.Clear(Color.FromArgb(24, 32, 44));
                    var badgeImage = AnnotationRenderer.Render(ImageUtil.ToBitmapSource(canvas), badgeDoc);
                    string badgePath = Path.Combine(root, "badge-shapes.png");
                    ImageUtil.SavePng(badgeImage, badgePath);
                    Check("序号与箭头形状渲染", File.Exists(badgePath) && new FileInfo(badgePath).Length > 1000, badgePath);
                }
            }
            catch (Exception badgeEx)
            {
                Check("序号与箭头形状渲染", false, badgeEx.Message);
            }
        }
        catch (Exception ex)
        {
            Check("自检未抛出异常", false, ex.Message + " @ " + ex.StackTrace?.Split('\n')[0]);
        }

        CheckWindowLayouts(root, Check);
        CheckSettingsLiveApply(Check);
        CheckEditorInteractions(Check);
        CheckEditorPanelSync(Check);
        CheckSelectionMaskRender(Check);
        CheckSelectionToolbar(root, Check);

        writer.WriteLine($"失败 {failures}");
        return failures;
    }

    /// <summary>编辑器右侧面板与左侧工具联动；只有「选择」工具才显示调整框。</summary>
    private static void CheckEditorPanelSync(Action<string, bool, string?> check)
    {
        EditorWindow? window = null;
        try
        {
            using var source = new Bitmap(420, 300);
            using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(28, 34, 44));
            window = new EditorWindow(ImageUtil.ToBitmapSource(source), "panel-sync");

            var both = window.ArrowModePanel.Children.OfType<ToggleButton>().FirstOrDefault(chip => Equals(chip.Content, "双向"));
            both?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check("右侧箭头模式自动切工具",
                both is not null && window.Canvas.Tool == EditorTool.Arrow, window.Canvas.Tool.ToString());

            var heart = window.BadgePanel.Children.OfType<ToggleButton>().FirstOrDefault(chip => Equals(chip.Content, "心形"));
            heart?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check("右侧序号形状自动切工具",
                heart is not null && window.Canvas.Tool == EditorTool.Number && window.Canvas.BadgeShape == BadgeShape.Heart,
                window.Canvas.Tool + "/" + window.Canvas.BadgeShape);

            var document = window.Canvas.Document;
            if (document is null) return;

            var badge = new NumberBadgeAnnotation { Number = 1, Center = new PointD(100, 80) };
            document.Add(badge);
            window.Canvas.SelectAnnotation(badge);
            bool whileDrawing = window.Canvas.ShowsSelectionBox;

            window.SelectToolButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            bool whileSelecting = window.Canvas.ShowsSelectionBox;
            check("仅选择工具显示调整框", !whileDrawing && whileSelecting,
                $"drawing={whileDrawing} selecting={whileSelecting}");
        }
        catch (Exception ex)
        {
            check("右侧面板与工具联动", false, ex.Message);
        }
        finally
        {
            try
            {
                window?.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>遮罩渲染回归：选区完全落在别的显示器上时不能抛「宽度和高度必须为非负值」。</summary>
    private static void CheckSelectionMaskRender(Action<string, bool, string?> check)
    {
        try
        {
            var mask = new SelectionMask
            {
                MonitorBounds = new PixelRect(0, 0, 320, 200),
                Scale = 1,
                Selection = new PixelRect(900, 640, 120, 60)
            };
            RenderElement(mask, 320, 200);

            mask.Selection = new PixelRect(120, 60, 150, 90);
            mask.Hover = new PixelRect(-50, -40, 80, 60);
            mask.Scale = 1.25;
            RenderElement(mask, 320, 200);

            mask.Selection = PixelRect.Empty;
            RenderElement(mask, 320, 200);

            check("跨屏选区遮罩不崩溃", true, null);
        }
        catch (Exception ex)
        {
            check("跨屏选区遮罩不崩溃", false, ex.Message);
        }
    }

    private static void RenderElement(FrameworkElement element, int width, int height)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new System.Windows.Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);
    }

    /// <summary>覆盖层工具条：选择按钮存在，复制与精标用文字标签区分，整条工具条可渲染。</summary>
    private static void CheckSelectionToolbar(string root, Action<string, bool, string?> check)
    {
        try
        {
            using var snapshot = DesktopSnapshot.Capture(false);
            var overlay = new SelectionOverlay(snapshot);
            var toolbar = new SelectionToolbarWindow();
            toolbar.Attach(overlay);

            if (toolbar.Content is not FrameworkElement content)
            {
                check("覆盖层工具条渲染", false, "无内容");
                return;
            }

            content.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            content.Arrange(new System.Windows.Rect(new System.Windows.Point(), content.DesiredSize));
            content.UpdateLayout();

            var labels = new List<string>();
            bool hasSelect = false;
            bool hasEraser = false;
            WalkVisuals(content, node =>
            {
                if (node is TextBlock text) labels.Add(text.Text);
                if (node is not ToggleButton toggle) return;
                if (Equals(toggle.Tag, OverlayAnnotateTool.Select)) hasSelect = true;
                if (Equals(toggle.Tag, OverlayAnnotateTool.Eraser)) hasEraser = true;
            });

            check("工具条有选择按钮", hasSelect, null);
            check("工具条有橡皮按钮", hasEraser, null);
            check("复制与精标有文字标签", labels.Contains("复制") && labels.Contains("精标"),
                string.Join("/", labels.Where(t => t is "复制" or "精标")));

            // 增删 + 排序：只留 选择 / 复制 / 精标，三组各一个按钮 + 两条分隔线。
            var custom = SelectionToolbarItems.CreateDefault();
            foreach (var item in custom)
            {
                item.Visible = item.Id is SelectionToolbarItems.Copy or SelectionToolbarItems.Select or SelectionToolbarItems.Refine;
            }

            var customOverlay = new SelectionOverlay(snapshot, custom);
            var customBar = new SelectionToolbarWindow();
            customBar.Attach(customOverlay, custom);
            int customCount = customBar.ButtonRow.Children.Count;
            check("工具条按配置增删排序", customCount == 5, customCount.ToString());
            customOverlay.Dispose();

            int width = (int)Math.Ceiling(content.ActualWidth);
            int height = (int)Math.Ceiling(content.ActualHeight);
            if (width <= 0 || height <= 0)
            {
                check("覆盖层工具条渲染", false, "布局尺寸为 0");
                return;
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            string path = Path.Combine(root, "selection-toolbar.png");
            ImageUtil.SavePng(bitmap, path);
            check("覆盖层工具条渲染", File.Exists(path) && new FileInfo(path).Length > 800, $"{width}×{height}");

            overlay.Dispose();
        }
        catch (Exception ex)
        {
            check("覆盖层工具条渲染", false, ex.Message);
        }
    }

    private static void WalkVisuals(DependencyObject node, Action<DependencyObject> visit)
    {
        visit(node);
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            WalkVisuals(System.Windows.Media.VisualTreeHelper.GetChild(node, i), visit);
        }
    }

    /// <summary>画布交互冒烟：多段箭头、双击结束、直角吸附、橡皮只擦画笔。</summary>
    private static void CheckEditorInteractions(Action<string, bool, string?> check)
    {
        try
        {
            var document = new AnnotationDocument(400, 300);
            var canvas = new AnnotationCanvas
            {
                Document = document,
                Tool = EditorTool.Arrow,
                ArrowHead = ArrowHead.End
            };

            // 三次点击 → 三段折线（起点 + 两个顶点）
            canvas.BeginArrowDraft(new PointD(40, 40));
            canvas.AppendArrowPoint(new PointD(140, 40));
            canvas.AppendArrowPoint(new PointD(140, 120));
            bool drafting = canvas.HasArrowDraft && canvas.ArrowDraftPointCount >= 3;
            canvas.FinishArrowDraft();
            var arrow = document.Annotations.OfType<ShapeAnnotation>().FirstOrDefault(a => a.Kind == AnnotationKind.Arrow);
            check("多段箭头三击成折线", drafting && arrow is not null && arrow.Points.Count == 3,
                arrow is null ? "无箭头" : arrow.Points.Count.ToString());

            // 双击（点回上一顶点）结束草稿
            canvas.BeginArrowDraft(new PointD(200, 40));
            canvas.AppendArrowPoint(new PointD(260, 40));
            canvas.AppendArrowPoint(new PointD(260, 40), 2);
            check("双击结束箭头草稿", !canvas.HasArrowDraft, null);

            // 直角模式：每段横平竖直
            canvas.ArrowElbow = true;
            canvas.BeginArrowDraft(new PointD(40, 160));
            canvas.AppendArrowPoint(new PointD(140, 176));
            canvas.FinishArrowDraft();
            var elbow = document.Annotations.OfType<ShapeAnnotation>().Last(a => a.Kind == AnnotationKind.Arrow);
            check("直角箭头按横竖吸附", Math.Abs(elbow.Points[1].Y - 160) < 0.01 && Math.Abs(elbow.Points[1].X - 140) < 0.01,
                $"{elbow.Points[1].X:0},{elbow.Points[1].Y:0}");
            canvas.ArrowElbow = false;

            // 橡皮：只擦画笔，文本与箭头保留
            var pen = new ShapeAnnotation(AnnotationKind.Pen, new[] { new PointD(300, 200), new PointD(360, 200) });
            var text = new TextAnnotation { Origin = new PointD(40, 250), Text = "保留" };
            document.Add(pen);
            document.Add(text);
            canvas.EraseAt(new PointD(330, 200));
            check("橡皮擦掉画笔笔画", !document.Annotations.Contains(pen), null);
            check("橡皮不动文本与箭头",
                document.Annotations.Contains(text) && document.Annotations.OfType<ShapeAnnotation>().Any(a => a.Kind == AnnotationKind.Arrow), null);
        }
        catch (Exception ex)
        {
            check("画布交互冒烟", false, ex.Message);
        }
    }

    /// <summary>设置页即时生效：只改控件、不点任何按钮，防抖结束设置即写回。</summary>
    private static void CheckSettingsLiveApply(Action<string, bool, string?> check)
    {
        try
        {
            var paths = new Infrastructure.Storage.PathService();
            var settings = AppSettings.CreateDefault();
            settings.Normalize(paths.DefaultScreenshotDirectory, paths.DefaultVideoDirectory);
            var view = new SettingsView(settings, paths);

            string target = Path.Combine(paths.RootDirectory, "shots-smoke");
            view.ScreenshotDirBox.Text = target;

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(700),
                DispatcherPriority.Background,
                (_, _) => frame.Continue = false,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();

            bool ok = string.Equals(settings.ScreenshotDirectory, target, StringComparison.OrdinalIgnoreCase);
            check("设置改动即时生效", ok, ok ? target : "仍为 " + settings.ScreenshotDirectory);

            int rows = view.ToolbarListPanel.Children.Count;
            check("设置页可增删排序工具条按钮", rows == SelectionToolbarItems.All.Length, rows.ToString());

            // 离屏渲染整页（带深色底，方便看清浅色文字），便于人工核对「选区工具条」区块的排版。
            var backdrop = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0B, 0x0F, 0x14)),
                Child = view
            };
            backdrop.Measure(new System.Windows.Size(840, double.PositiveInfinity));
            backdrop.Arrange(new System.Windows.Rect(0, 0, 840, Math.Max(1, backdrop.DesiredSize.Height)));
            backdrop.UpdateLayout();

            int width = (int)Math.Ceiling(backdrop.ActualWidth);
            int height = (int)Math.Ceiling(backdrop.ActualHeight);
            if (width > 0 && height > 0)
            {
                var target2 = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                target2.Render(backdrop);
                string pagePath = Path.Combine(AppContext.BaseDirectory, "selftest", "settings-page.png");
                Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
                ImageUtil.SavePng(target2, pagePath);
                check("设置页渲染", new FileInfo(pagePath).Length > 2000, $"{width}×{height}");
            }
        }
        catch (Exception ex)
        {
            check("设置改动即时生效", false, ex.Message);
        }
    }

    /// <summary>UI 冒烟：离屏构建各浮动窗口并强制一次布局，覆盖 XAML 解析、样式引用与尺寸计算。</summary>
    private static void CheckWindowLayouts(string root, Action<string, bool, string?> check)
    {
        var factories = new (string Name, Func<Window> Create)[]
        {
            ("录制前控制条", () => new RecordSetupBar(RecordingKind.Gif)),
            ("录制中控制条", () => new RecordingControlBar(RecordingKind.Gif, 12)),
            ("录制预览窗口", () =>
            {
                var path = Path.Combine(root, "smoke.mp4");
                return new RecordingResultWindow(null, path, RecordingKind.Video, TimeSpan.FromSeconds(4), 1024);
            }),
            ("钉图窗口", () =>
            {
                using var source = new Bitmap(240, 160);
                return new PinWindow(ImageUtil.ToBitmapSource(source), "smoke");
            })
        };

        foreach (var (name, create) in factories)
        {
            Window? window = null;
            try
            {
                window = create();
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.ShowInTaskbar = false;
                window.ShowActivated = false;
                window.Left = -4000;
                window.Top = -4000;
                window.Show();
                window.UpdateLayout();
                double width = window.ActualWidth;
                double height = window.ActualHeight;
                check(name + " 布局", width > 120 && height > 30, $"{width:0}×{height:0}");
            }
            catch (Exception ex)
            {
                check(name + " 布局", false, ex.Message + " @ " + ex.StackTrace?.Split('\n')[0]);
            }
            finally
            {
                try
                {
                    window?.Close();
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private static bool Near(Color actual, Color expected)
        => Math.Abs(actual.R - expected.R) <= 10 && Math.Abs(actual.G - expected.G) <= 10 && Math.Abs(actual.B - expected.B) <= 10;

    private static bool ContainsLoopBlock(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[] marker = "NETSCAPE2.0"u8.ToArray();
        for (int i = 0; i < bytes.Length - marker.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
            {
                if (bytes[i + j] != marker[j])
                {
                    match = false;
                    break;
                }
            }

            if (match) return true;
        }

        return false;
    }
}
