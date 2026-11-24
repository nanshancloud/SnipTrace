using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Encoding;

/// <summary>
/// 把 MP4 转成 GIF：MediaPlayer 拖动取样 + RenderTargetBitmap 抓帧，
/// 复用 GIF 流式编码器（量化 / LZW / 重复帧合并）。在 UI 线程上异步执行。
/// </summary>
public static class Mp4ToGifConverter
{
    /// <param name="progress">0–100 进度。</param>
    public static async Task ConvertAsync(
        string mp4Path,
        string gifPath,
        GifOptions options,
        Action<int>? progress,
        CancellationToken cancellation)
    {
        var player = new MediaPlayer { ScrubbingEnabled = true, Volume = 0 };
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        player.MediaOpened += (_, _) => opened.TrySetResult(true);
        player.MediaFailed += (_, e) => failed.TrySetResult(e.ErrorException?.Message ?? L.T("未知错误"));
        player.Open(new Uri(mp4Path));

        var winner = await Task.WhenAny(opened.Task, failed.Task, Task.Delay(15000, cancellation));
        if (winner == failed.Task) throw new InvalidOperationException(L.T("无法读取 MP4：") + await failed.Task);
        if (winner != opened.Task) throw new InvalidOperationException(L.T("读取 MP4 超时。"));
        if (!player.NaturalDuration.HasTimeSpan || player.NaturalDuration.TimeSpan <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(L.T("无法确定 MP4 时长。"));
        }

        try
        {
            int sourceWidth = player.NaturalVideoWidth;
            int sourceHeight = player.NaturalVideoHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0) throw new InvalidOperationException(L.T("无法读取视频尺寸。"));

            // H.264 宽高可能为奇数，GIF 不受限制但缩放保持偶数更友好；这里仅按最大边长等比缩放。
            int targetWidth = sourceWidth;
            int targetHeight = sourceHeight;
            int maxEdge = options.MaxEdge;
            if (maxEdge > 0 && (sourceWidth > maxEdge || sourceHeight > maxEdge))
            {
                double scale = Math.Min((double)maxEdge / sourceWidth, (double)maxEdge / sourceHeight);
                targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
                targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            }

            var duration = player.NaturalDuration.TimeSpan;
            int delay = options.FrameDelayMilliseconds;
            int frameCount = Math.Max(1, (int)Math.Round(duration.TotalMilliseconds / delay));
            frameCount = Math.Min(frameCount, options.MaxFrames);

            var drawing = new VideoDrawing
            {
                Player = player,
                Rect = new Rect(0, 0, targetWidth, targetHeight)
            };

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawDrawing(drawing);
            }

            var target = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);

            using (var stream = new FileStream(gifPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024))
            {
                using var encoder = new GifStreamEncoder(stream, targetWidth, targetHeight, options.HighQualityDither);
                for (int index = 0; index < frameCount; index++)
                {
                    cancellation.ThrowIfCancellationRequested();

                    player.Position = TimeSpan.FromMilliseconds(index * (double)delay);
                    // ScrubbingEnabled 模式下拖动后需要一点时间解码出目标帧。
                    await Task.Delay(24, cancellation);
                    target.Render(visual);

                    using var frame = ImageUtil.ToBitmap(target);
                    encoder.AddFrame(frame, delay);

                    if (progress is not null && index % 4 == 0)
                    {
                        progress((index + 1) * 100 / frameCount);
                    }
                }

                encoder.Finish();
            }
        }
        finally
        {
            player.Close();
        }
    }
}
