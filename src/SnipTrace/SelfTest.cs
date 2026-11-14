using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Encoding;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.App.Presentation.Recording;
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
                var badgeDoc = new AnnotationDocument(560, 220);
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

                using (var canvas = new Bitmap(560, 220, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(canvas)) graphics.Clear(Color.FromArgb(24, 32, 44));
                    var badgeImage = AnnotationRenderer.Render(ImageUtil.ToBitmapSource(canvas), badgeDoc);
                    string badgePath = Path.Combine(root, "badge-shapes.png");
                    ImageUtil.SavePng(badgeImage, badgePath);
                    Check("序号形状渲染", File.Exists(badgePath) && new FileInfo(badgePath).Length > 1000, badgePath);
                }
            }
            catch (Exception badgeEx)
            {
                Check("序号形状渲染", false, badgeEx.Message);
            }
        }
        catch (Exception ex)
        {
            Check("自检未抛出异常", false, ex.Message + " @ " + ex.StackTrace?.Split('\n')[0]);
        }

        CheckWindowLayouts(root, Check);
        CheckSettingsLiveApply(Check);

        writer.WriteLine($"失败 {failures}");
        return failures;
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
