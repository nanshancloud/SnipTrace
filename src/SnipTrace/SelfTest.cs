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
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.App.Presentation.Recording;
using SnipTrace.App.Presentation.Selection;
using SnipTrace.App.Presentation.Views;
using SnipTrace.Domain;
using Color = System.Drawing.Color;

namespace SnipTrace.App;

/// <summary>
/// 编码管线自检（--selftest）：验证 GIF 可解码、帧去重、循环标记与 MP4 编码可用性。
/// 结果与临时文件写入 exe 旁的 selftest 目录；渲染截图默认写入仓库 docs\images（不入库）。
/// </summary>
internal static class SelfTest
{
    /// <summary>自检截图的输出目录：仓库内运行时为 docs\images，否则退回 exe 旁的 selftest。</summary>
    private static readonly string ShotRoot = ResolveShotRoot();

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
                    string badgePath = Path.Combine(ShotRoot, "badge-shapes.png");
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
        CheckAboutPage(ShotRoot, Check);
        CheckUpdateService(Check);
        CheckTrayOptions(Check);
        CheckEditorInteractions(Check);
        CheckEditorPanelSync(Check);
        CheckEditorUndoRedo(Check);
        CheckHighlightRender(Check);
        CheckArrowHeadRender(Check);
        CheckSelectionMaskRender(Check);
        CheckSelectionToolbar(ShotRoot, Check);

        writer.WriteLine($"失败 {failures}");
        return failures;
    }

    /// <summary>
    /// 截图目录解析：优先环境变量 SNIPTRACE_SHOTS_DIR，其次从 exe 目录向上找带 SnipTrace.slnx 与 docs 的仓库根，
    /// 命中则用 docs\images；打包安装后没有仓库结构，退回 exe 旁的 selftest。
    /// </summary>
    private static string ResolveShotRoot()
    {
        string? custom = Environment.GetEnvironmentVariable("SNIPTRACE_SHOTS_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            string full = Path.GetFullPath(custom);
            Directory.CreateDirectory(full);
            return full;
        }

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SnipTrace.slnx")) &&
                Directory.Exists(Path.Combine(dir.FullName, "docs")))
            {
                string shots = Path.Combine(dir.FullName, "docs", "images");
                Directory.CreateDirectory(shots);
                return shots;
            }
        }

        string fallback = Path.Combine(AppContext.BaseDirectory, "selftest");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>高亮渲染半透明：同色画笔不透明作对照，保证覆盖层 / 编辑器 / 导出一致。</summary>
    private static void CheckHighlightRender(Action<string, bool, string?> check)
    {
        try
        {
            using var source = new Bitmap(160, 80);
            using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
            var white = ImageUtil.ToBitmapSource(source);

            var highlight = new AnnotationDocument(160, 80);
            highlight.Add(new ShapeAnnotation(AnnotationKind.Highlight, new[] { new PointD(20, 40), new PointD(140, 40) })
            {
                Style = new AnnotationStyle(0xFFFF6B5F, 14, 1)
            });
            var rendered = AnnotationRenderer.Render(white, highlight);

            var sample = new byte[4];
            rendered.CopyPixels(new Int32Rect(80, 40, 1, 1), sample, 4, 0);
            int highlightGreen = sample[1];

            // 同色画笔（不透明）作对照。
            var pen = new AnnotationDocument(160, 80);
            pen.Add(new ShapeAnnotation(AnnotationKind.Pen, new[] { new PointD(20, 40), new PointD(140, 40) })
            {
                Style = new AnnotationStyle(0xFFFF6B5F, 14, 1)
            });
            var penRendered = AnnotationRenderer.Render(white, pen);
            var penSample = new byte[4];
            penRendered.CopyPixels(new Int32Rect(80, 40, 1, 1), penSample, 4, 0);
            int penGreen = penSample[1];

            // 半透明会被白底提亮（G 明显高于纯色的 107），不透明则保持原色。
            check("高亮半透明渲染", highlightGreen > 150 && highlightGreen < 250, $"G={highlightGreen}");
            check("画笔仍不透明", penGreen < 130, $"G={penGreen}");

            // 存一张对比图：同一段文字上，高亮仍能看见文字，同色画笔则盖住文字。
            using var demo = new Bitmap(320, 120);
            using (var graphics = Graphics.FromImage(demo))
            {
                graphics.Clear(Color.White);
                using var font = new Font("Segoe UI", 13);
                using var textBrush = new SolidBrush(Color.FromArgb(40, 50, 60));
                graphics.DrawString("Highlight: text still visible", font, textBrush, 12, 48);
                graphics.DrawString("Pen: text covered", font, textBrush, 12, 88);
            }

            var demoDocument = new AnnotationDocument(320, 120);
            demoDocument.Add(new ShapeAnnotation(AnnotationKind.Highlight,
                new[] { new PointD(8, 58), new PointD(312, 58) }) { Style = new AnnotationStyle(0xFFFF6B5F, 18, 1) });
            demoDocument.Add(new ShapeAnnotation(AnnotationKind.Pen,
                new[] { new PointD(8, 98), new PointD(312, 98) }) { Style = new AnnotationStyle(0xFFFF6B5F, 6, 1) });

            string path = Path.Combine(ShotRoot, "highlight-transparency.png");
            ImageUtil.SavePng(AnnotationRenderer.Render(ImageUtil.ToBitmapSource(demo), demoDocument), path);
            check("高亮对比图输出", File.Exists(path) && new FileInfo(path).Length > 500, "已生成对比图");
        }
        catch (Exception ex)
        {
            check("高亮半透明渲染", false, ex.Message);
        }
    }

    /// <summary>箭头端头随线宽缩放：细线端头窄、粗线端头宽，同一位置取样可区分。</summary>
    private static void CheckArrowHeadRender(Action<string, bool, string?> check)
    {
        try
        {
            using var source = new Bitmap(260, 160);
            using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
            var white = ImageUtil.ToBitmapSource(source);

            var document = new AnnotationDocument(260, 160);
            document.Add(new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(20, 40), new PointD(220, 40) })
            {
                Style = new AnnotationStyle(0xFF1B2430, 1, 1)
            });
            document.Add(new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(20, 120), new PointD(220, 120) })
            {
                Style = new AnnotationStyle(0xFF1B2430, 16, 1)
            });

            var rendered = AnnotationRenderer.Render(white, document);

            var thin = new byte[4];
            var thick = new byte[4];
            rendered.CopyPixels(new Int32Rect(190, 25, 1, 1), thin, 4, 0);   // 细箭头端头之外
            rendered.CopyPixels(new Int32Rect(190, 105, 1, 1), thick, 4, 0); // 粗箭头端头之内

            bool thinIsBlank = thin[0] > 240 && thin[1] > 240 && thin[2] > 240;
            bool thickIsInk = thick[0] < 200 || thick[1] < 200 || thick[2] < 200;
            check("细箭头端头窄", thinIsBlank, $"B={thin[0]},G={thin[1]},R={thin[2]}");
            check("粗箭头端头宽", thickIsInk, $"B={thick[0]},G={thick[1]},R={thick[2]}");

            // 存一张对照图：三种线宽的端头 + 三段折线 + 正心形号标。
            using var demo = new Bitmap(560, 240);
            using (var graphics = Graphics.FromImage(demo)) graphics.Clear(Color.White);
            var demoDocument = new AnnotationDocument(560, 240);
            demoDocument.Add(new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(30, 40), new PointD(240, 40) })
            {
                Style = new AnnotationStyle(0xFF1B2430, 1, 1)
            });
            demoDocument.Add(new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(30, 110), new PointD(240, 110) })
            {
                Style = new AnnotationStyle(0xFF1B2430, 6, 1)
            });
            demoDocument.Add(new ShapeAnnotation(AnnotationKind.Arrow, new[] { new PointD(30, 190), new PointD(240, 190) })
            {
                Style = new AnnotationStyle(0xFF1B2430, 16, 1)
            });
            demoDocument.Add(new ShapeAnnotation(AnnotationKind.Arrow,
                new[] { new PointD(300, 190), new PointD(420, 190), new PointD(420, 60) })
            {
                Style = new AnnotationStyle(0xFFFF6B5F, 5, 1),
                ArrowHead = ArrowHead.Both
            });
            demoDocument.Add(new NumberBadgeAnnotation
            {
                Number = 3,
                Center = new PointD(500, 90),
                FontSize = 26,
                Shape = BadgeShape.Heart,
                FillMode = BadgeFillMode.Outline,
                Style = new AnnotationStyle(0xFFFF6B5F, 16, 1)
            });

            string path = Path.Combine(ShotRoot, "arrow-head-widths.png");
            ImageUtil.SavePng(AnnotationRenderer.Render(ImageUtil.ToBitmapSource(demo), demoDocument), path);
            check("箭头端头对照图输出", File.Exists(path) && new FileInfo(path).Length > 500, "已生成对照图");
        }
        catch (Exception ex)
        {
            check("箭头端头随线宽缩放", false, ex.Message);
        }
    }

    /// <summary>编辑器撤销 / 重做：按钮与快捷键都要能把状态倒回去再拿回来。</summary>
    private static void CheckEditorUndoRedo(Action<string, bool, string?> check)
    {
        EditorWindow? window = null;
        try
        {
            using var source = new Bitmap(420, 300);
            using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(28, 34, 44));
            window = new EditorWindow(ImageUtil.ToBitmapSource(source), "undo-redo");

            var document = window.Canvas.Document;
            if (document is null)
            {
                check("编辑器撤销重做", false, "文档为空");
                return;
            }

            // 刚打开时没有历史：两个按钮都应该是置灰的，而不是亮着点了没反应。
            check("初始无可撤销/重做时按钮置灰",
                !window.UndoButton.IsEnabled && !window.RedoButton.IsEnabled,
                $"undoEnabled={window.UndoButton.IsEnabled} redoEnabled={window.RedoButton.IsEnabled}");

            var rect = document.Add(new ShapeAnnotation(AnnotationKind.Rectangle,
                new[] { new PointD(20, 20), new PointD(120, 80) }));
            window.Canvas.SelectAnnotation(rect);
            window.Canvas.Refresh();
            int afterDraw = document.Annotations.Count;

            window.UndoButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            int afterUndo = document.Annotations.Count;
            bool canRedoAfterUndo = document.CanRedo;
            bool redoEnabledAfterUndo = window.RedoButton.IsEnabled;

            window.RedoButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            int afterRedo = document.Annotations.Count;

            check("撤销按钮生效", afterDraw == 1 && afterUndo == 0, $"{afterDraw}→{afterUndo}");
            check("撤销后可重做", canRedoAfterUndo && redoEnabledAfterUndo, $"可用={redoEnabledAfterUndo}");
            check("重做按钮生效", afterRedo == 1, $"undo={afterUndo} redo={afterRedo}");

            // 文本类编辑（改样式）也要能重做，避免 EndEdit 把 redo 栈清掉。
            var badge = new NumberBadgeAnnotation { Number = 1, Center = new PointD(200, 120) };
            document.Add(badge);
            window.Canvas.SelectAnnotation(badge);
            window.Canvas.ApplyToSelection(annotation =>
            {
                if (annotation is NumberBadgeAnnotation b) b.Shape = BadgeShape.Heart;
            });
            window.UndoButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.RedoButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var restored = document.Annotations.OfType<NumberBadgeAnnotation>().FirstOrDefault();
            check("改样式后可重做", restored is not null && restored.Shape == BadgeShape.Heart,
                restored is null ? "号标丢失" : restored.Shape.ToString());

            // 清除全部：一次清空画布，可撤销回来，清空后按钮自己置灰。
            int beforeClear = document.Annotations.Count;
            bool clearEnabledBefore = window.ClearAllButton.IsEnabled;
            window.ClearAllButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            int afterClear = document.Annotations.Count;
            bool clearEnabledAfter = window.ClearAllButton.IsEnabled;
            window.UndoButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            int afterClearUndo = document.Annotations.Count;
            check("清除全部标记",
                beforeClear > 0 && afterClear == 0 && afterClearUndo == beforeClear
                && clearEnabledBefore && !clearEnabledAfter,
                $"{beforeClear}→{afterClear}→撤销{afterClearUndo} 可用={clearEnabledBefore}→{clearEnabledAfter}");
        }
        catch (Exception ex)
        {
            check("编辑器撤销重做", false, ex.Message);
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
    private static void CheckSelectionToolbar(string shots, Action<string, bool, string?> check)
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
            check("工具条有回退与前进按钮", toolbar.HasHistoryButtons, null);
            check("回退前进初始不可用", !overlay.CanUndoAnnotations && !overlay.CanRedoAnnotations,
                $"undo={overlay.CanUndoAnnotations} redo={overlay.CanRedoAnnotations}");
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
            string path = Path.Combine(shots, "selection-toolbar.png");
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
            view.CheckUpdateCheck.IsChecked = false;
            view.TrayColorRadio.IsChecked = true;
            view.TrayHintCheck.IsChecked = false;

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
            check("启动检查更新开关即时生效", !settings.CheckUpdatesOnStartup,
                settings.CheckUpdatesOnStartup.ToString());
            check("托盘图标与提示开关即时生效",
                settings.TrayIconMode == TrayIconModes.Color && settings.SuppressTrayHint,
                $"{settings.TrayIconMode} suppress={settings.SuppressTrayHint}");

            // 工具条配置区改成真机样式的图标按钮：每个按钮都是可点（显隐）、可拖（排序）的。
            int previewItems = view.ToolbarPreviewPanel.Children.OfType<Button>().Count(b => b.Tag is ToolbarItemSetting);
            check("工具条预览显示全部按钮", previewItems == SelectionToolbarItems.All.Length, previewItems.ToString());
            check("工具条预览含回退与前进",
                view.ToolbarPreviewPanel.Children.OfType<Button>().Count(b =>
                    b.Tag is ToolbarItemSetting item && item.Id is SelectionToolbarItems.Undo or SelectionToolbarItems.Redo) == 2,
                null);

            // 快捷键按键录入：虚拟键码能反查回规范按键名（按键录入 → 文本 → 再解析要能对上）。
            check("快捷键按键名反查",
                HotkeyGesture.TryGetKeyName(0x47, out string g) && g == "G"
                && HotkeyGesture.TryGetKeyName(0x70, out string f1) && f1 == "F1"
                && HotkeyGesture.TryGetKeyName(0x0D, out string enter) && enter == "Enter"
                && HotkeyGesture.TryParse(new HotkeyGesture(HotkeyModifiers.Control, 0x47, g).ToDisplayString(), out var parsed)
                && parsed.VirtualKey == 0x47 && parsed.Modifiers == HotkeyModifiers.Control,
                "G / F1 / Enter");

            // 拖动排序与真实拖动共用 MoveToolbarItemTo：顺序要变、数量不变。
            string firstId = settings.SelectionToolbar[0].Id;
            view.MoveToolbarItemTo(0, 3);
            bool moved = settings.SelectionToolbar[3].Id == firstId
                         && settings.SelectionToolbar.Count == SelectionToolbarItems.All.Length;
            check("工具条预览拖动排序", moved, $"{firstId} → {settings.SelectionToolbar[3].Id}");

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
                string pagePath = Path.Combine(ShotRoot, "settings-page.png");
                ImageUtil.SavePng(target2, pagePath);
                check("设置页渲染", new FileInfo(pagePath).Length > 2000, $"{width}×{height}");
            }
        }
        catch (Exception ex)
        {
            check("设置改动即时生效", false, ex.Message);
        }
    }


    /// <summary>托盘的图标配色归一化，以及提示窗的「不再提示」按钮。</summary>
    private static void CheckTrayOptions(Action<string, bool, string?> check)
    {
        try
        {
            bool normalize = TrayIconModes.Normalize("color") == TrayIconModes.Color
                             && TrayIconModes.Normalize("Light") == TrayIconModes.Light
                             && TrayIconModes.Normalize(null) == TrayIconModes.Auto
                             && TrayIconModes.Normalize("weird") == TrayIconModes.Auto;

            var toast = new ToastWindow();
            bool actionInvoked = false;
            toast.ShowMessage("已最小化到托盘", "双击托盘图标可重新打开主窗口。", false, "不再提示",
                () => actionInvoked = true);

            bool hasAction = toast.ActionRow.Visibility == Visibility.Visible
                             && string.Equals(toast.ActionButton.Content as string, "不再提示", StringComparison.Ordinal);
            toast.ActionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            toast.Close();

            check("托盘图标与提示选项", normalize && hasAction && actionInvoked,
                $"normalize={normalize} action={hasAction} invoked={actionInvoked}");
        }
        catch (Exception ex)
        {
            check("托盘图标与提示选项", false, ex.Message);
        }
    }

    /// <summary>更新检查的纯逻辑：tag 解析、版本比较、GitHub 响应解析（不联网）。</summary>
    private static void CheckUpdateService(Action<string, bool, string?> check)
    {
        try
        {
            var current = new Version(1, 0, 0);
            Version? patch = UpdateService.ParseTag("v1.0.1");
            Version? suffix = UpdateService.ParseTag("1.2.0-beta");
            Version? bare = UpdateService.ParseTag("2.0");

            bool newer = patch is not null && UpdateService.IsNewer(patch, current);
            bool same = patch is not null && !UpdateService.IsNewer(new Version(1, 0, 0), current);
            bool parseOk = patch is not null && patch.ToString(3) == "1.0.1"
                           && suffix is not null && suffix.ToString(3) == "1.2.0"
                           && bare is not null && bare.ToString(3) == "2.0.0";

            UpdateCheckResult parsed = UpdateService.ParseLatestRelease(
                "{\"tag_name\":\"v9.9.9\",\"html_url\":\"https://example.com/release\"}", current);
            UpdateCheckResult bad = UpdateService.ParseLatestRelease("{\"tag_name\":\"nightly\"}", current);

            // 仓库里的 tag 不一定是纯版本号：要跳过无关 tag，取能识别的最新版本。
            UpdateCheckResult fromReleases = UpdateService.ParseReleaseList(
                "[{\"tag_name\":\"SnipTrace\",\"html_url\":\"https://example.com/odd\"}," +
                "{\"tag_name\":\"v1.3.0\",\"html_url\":\"https://example.com/130\"}]", current);
            UpdateCheckResult fromTags = UpdateService.ParseTagList(
                "[{\"name\":\"SnipTrace\"},{\"name\":\"v2.1.0\"}]", current);

            check("更新检查版本解析",
                newer && same && parseOk && parsed.Succeeded && parsed.HasUpdate
                && parsed.LatestVersion == "9.9.9" && parsed.DownloadUrl == "https://example.com/release"
                && !bad.Succeeded
                && fromReleases.HasUpdate && fromReleases.LatestVersion == "1.3.0"
                && fromReleases.DownloadUrl == "https://example.com/130"
                && fromTags.HasUpdate && fromTags.LatestVersion == "2.1.0",
                $"newer={newer} same={same} parse={parseOk} latest={parsed.LatestVersion} bad={bad.Succeeded} " +
                $"list={fromReleases.LatestVersion} tags={fromTags.LatestVersion}");
        }
        catch (Exception ex)
        {
            check("更新检查版本解析", false, ex.Message);
        }
    }

    /// <summary>关于页：版本号来自程序集，并渲染一张整页图便于人工核对排版。</summary>
    private static void CheckAboutPage(string shots, Action<string, bool, string?> check)
    {
        try
        {
            var view = new AboutView();
            check("关于页版本信息", view.VersionText.Text.StartsWith("版本 ", StringComparison.Ordinal),
                view.VersionText.Text);

            var backdrop = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0B, 0x0F, 0x14)),
                Child = view
            };
            backdrop.Measure(new System.Windows.Size(780, double.PositiveInfinity));
            backdrop.Arrange(new System.Windows.Rect(0, 0, 780, Math.Max(1, backdrop.DesiredSize.Height)));
            backdrop.UpdateLayout();

            int width = (int)Math.Ceiling(backdrop.ActualWidth);
            int height = (int)Math.Ceiling(backdrop.ActualHeight);
            if (width <= 0 || height <= 0) return;

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(backdrop);
            string path = Path.Combine(shots, "about-page.png");
            ImageUtil.SavePng(target, path);
            check("关于页渲染", File.Exists(path) && new FileInfo(path).Length > 2000, $"{width}×{height}");
        }
        catch (Exception ex)
        {
            check("关于页渲染", false, ex.Message);
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
