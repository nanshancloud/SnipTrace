using System.Drawing;
using System.Drawing.Imaging;
using SnipTrace.App.Infrastructure.Encoding;

string directory = @"D:\Repo\Harness\artifacts";
Directory.CreateDirectory(directory);
string path = Path.Combine(directory, "gifprobe.gif");
const int width = 64;
const int height = 40;

using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
{
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.Red);
        using var blue = new SolidBrush(Color.Blue);
        graphics.FillRectangle(blue, width / 2, 0, width / 2, height);
    }

    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var encoder = new GifStreamEncoder(stream, width, height);
    encoder.AddFrame(bitmap, 100);
    encoder.AddFrame(bitmap, 100);
}

byte[] bytes = File.ReadAllBytes(path);
Console.WriteLine($"file={path} size={bytes.Length}");
Console.WriteLine("header: " + string.Join(' ', bytes.Take(24).Select(b => b.ToString("X2"))));

foreach (string candidate in new[] { path, Path.Combine(Path.GetTempPath(), "SnipTraceSelfTest", "roundtrip.gif") })
{
    try
    {
        using var decoded = new Bitmap(candidate);
        Console.WriteLine($"decoded {candidate} = {decoded.Width}x{decoded.Height} frames={decoded.GetFrameCount(FrameDimension.Time)}");
        Console.WriteLine($"  left={decoded.GetPixel(10, 10)} right={decoded.GetPixel(50, 10)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DECODE FAILED {candidate}: {ex.GetType().Name} {ex.Message}");
    }
}
