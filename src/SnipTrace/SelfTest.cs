using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Encoding;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.Domain;

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
        }
        catch (Exception ex)
        {
            Check("自检未抛出异常", false, ex.Message + " @ " + ex.StackTrace?.Split('\n')[0]);
        }

        writer.WriteLine($"失败 {failures}");
        return failures;
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
