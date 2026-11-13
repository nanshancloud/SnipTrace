using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SnipTrace.App.Infrastructure.Encoding;

/// <summary>
/// 自研流式 GIF 编码：中位切分量化 + 局部调色板 + LZW 压缩，边编码边写盘。
/// </summary>
public sealed class GifStreamEncoder : IDisposable
{
    private const int MaxColors = 256;

    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _dither;
    private readonly object _gate = new();

    private byte[] _current = Array.Empty<byte>();
    private byte[] _previous = Array.Empty<byte>();
    private bool _hasPrevious;
    private int _pendingDelayMs;
    private bool _headerWritten;
    private bool _finished;

    public GifStreamEncoder(Stream stream, int width, int height, bool dither = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _dither = dither;
        _current = new byte[_width * _height * 4];
        _previous = new byte[_width * _height * 4];
    }

    public int FrameCount { get; private set; }

    public long BytesWritten => _stream.Position;

    /// <summary>追加一帧；与上一帧完全相同时合并延迟。</summary>
    public void AddFrame(Bitmap frame, int delayMilliseconds)
    {
        lock (_gate)
        {
            if (_finished) return;
            EnsureHeader();

            CopyPixels(frame);
            if (_hasPrevious && _current.AsSpan().SequenceEqual(_previous))
            {
                _pendingDelayMs += delayMilliseconds;
                return;
            }

            WriteFrame(_current, delayMilliseconds + _pendingDelayMs);
            _pendingDelayMs = 0;
            (_previous, _current) = (_current, _previous);
            _hasPrevious = true;
            FrameCount++;
            _stream.Flush();
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            if (_finished) return;
            EnsureHeader();
            _stream.WriteByte(0x3B);
            _stream.Flush();
            _finished = true;
        }
    }

    private void EnsureHeader()
    {
        if (_headerWritten) return;
        _headerWritten = true;

        Span<byte> header = stackalloc byte[13];
        header[0] = (byte)'G';
        header[1] = (byte)'I';
        header[2] = (byte)'F';
        header[3] = (byte)'8';
        header[4] = (byte)'9';
        header[5] = (byte)'a';
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)_width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], (ushort)_height);
        header[10] = 0x00; // 无全局调色板
        header[11] = 0x00; // 背景色索引
        header[12] = 0x00; // 像素宽高比
        _stream.Write(header);

        // Netscape 循环块：无限循环。
        _stream.Write(new byte[] { 0x21, 0xFF, 0x0B });
        _stream.Write("NETSCAPE2.0"u8);
        _stream.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
    }

    private void CopyPixels(Bitmap frame)
    {
        var normalized = Normalize(frame);
        bool owned = !ReferenceEquals(normalized, frame);
        try
        {
            var data = normalized.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < _height; y++)
                {
                    IntPtr row = data.Scan0 + y * data.Stride;
                    Marshal.Copy(row, _current, y * _width * 4, _width * 4);
                }
            }
            finally
            {
                normalized.UnlockBits(data);
            }
        }
        finally
        {
            if (owned) normalized.Dispose();
        }
    }

    private Bitmap Normalize(Bitmap frame)
    {
        if (frame.Width == _width && frame.Height == _height && frame.PixelFormat == PixelFormat.Format32bppArgb)
        {
            return frame;
        }

        var copy = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(frame, new Rectangle(0, 0, _width, _height));
        return copy;
    }

    private void WriteFrame(byte[] bgra, int delayMilliseconds)
    {
        ColorQuantizer.Quantize(bgra, _width, _height, _dither, out byte[] palette, out byte[] indices);

        int delayCentiseconds = Math.Max(1, (int)Math.Round(delayMilliseconds / 10.0));
        Span<byte> gce = stackalloc byte[8];
        gce[0] = 0x21;
        gce[1] = 0xF9;
        gce[2] = 0x04;
        gce[3] = 0x04; // 保留处置方式：不处置
        BinaryPrimitives.WriteUInt16LittleEndian(gce[4..], (ushort)Math.Min(delayCentiseconds, 65535));
        gce[6] = 0x00;
        gce[7] = 0x00;
        _stream.Write(gce);

        Span<byte> descriptor = stackalloc byte[10];
        descriptor[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[1..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[3..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[5..], (ushort)_width);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[7..], (ushort)_height);
        descriptor[9] = 0x87; // 局部调色板，256 色
        _stream.Write(descriptor);

        _stream.Write(palette, 0, palette.Length);
        _stream.WriteByte(0x08); // LZW 最小码长
        GifLzw.Compress(indices, 8, _stream);
    }

    public void Dispose()
    {
        if (!_finished)
        {
            try
            {
                Finish();
            }
            catch (Exception)
            {
            }
        }
    }
}

internal static class GifLzw
{
    /// <summary>GIF LZW 编码，输出 255 字节子块并以 0x00 结束。</summary>
    public static void Compress(byte[] indices, int minCodeSize, Stream output)
    {
        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int codeSize = minCodeSize + 1;
        int nextCode = endCode + 1;

        var dictionary = new Dictionary<int, int>(4096);
        using var bits = new MemoryStream(Math.Max(1024, indices.Length / 2));

        int bitBuffer = 0;
        int bitCount = 0;

        void Emit(int code)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                bits.WriteByte((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        Emit(clearCode);

        if (indices.Length > 0)
        {
            int prefix = indices[0];
            for (int i = 1; i < indices.Length; i++)
            {
                int suffix = indices[i];
                int key = (prefix << 8) | suffix;
                if (dictionary.TryGetValue(key, out int existing))
                {
                    prefix = existing;
                    continue;
                }

                Emit(prefix);
                if (nextCode < 4096)
                {
                    dictionary[key] = nextCode++;
                    if (nextCode > (1 << codeSize) - 1 && codeSize < 12) codeSize++;
                }
                else
                {
                    Emit(clearCode);
                    dictionary.Clear();
                    codeSize = minCodeSize + 1;
                    nextCode = endCode + 1;
                }

                prefix = suffix;
            }

            Emit(prefix);
        }

        Emit(endCode);
        if (bitCount > 0) bits.WriteByte((byte)(bitBuffer & 0xFF));

        byte[] buffer = bits.GetBuffer();
        int length = (int)bits.Length;
        int offset = 0;
        while (offset < length)
        {
            int size = Math.Min(255, length - offset);
            output.WriteByte((byte)size);
            output.Write(buffer, offset, size);
            offset += size;
        }

        output.WriteByte(0x00);
    }
}

internal static class ColorQuantizer
{
    private const int MaxColors = 256;

    private static readonly int[,] BayerMatrix =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 }
    };

    private readonly struct ColorEntry
    {
        public ColorEntry(byte r, byte g, byte b, int count)
        {
            R = r;
            G = g;
            B = b;
            Count = count;
        }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public int Count { get; }
    }

    private sealed class Box
    {
        public int Start;
        public int Count;
        public long Weight;
        public int RMin, RMax, GMin, GMax, BMin, BMax;

        public int LongestSide => Math.Max(RMax - RMin, Math.Max(GMax - GMin, BMax - BMin));

        public void Measure(ColorEntry[] colors)
        {
            RMin = GMin = BMin = 255;
            RMax = GMax = BMax = 0;
            Weight = 0;
            for (int i = Start; i < Start + Count; i++)
            {
                var c = colors[i];
                if (c.R < RMin) RMin = c.R;
                if (c.R > RMax) RMax = c.R;
                if (c.G < GMin) GMin = c.G;
                if (c.G > GMax) GMax = c.G;
                if (c.B < BMin) BMin = c.B;
                if (c.B > BMax) BMax = c.B;
                Weight += c.Count;
            }
        }
    }

    public static void Quantize(byte[] bgra, int width, int height, bool dither, out byte[] palette, out byte[] indices)
    {
        int pixelCount = width * height;
        indices = new byte[pixelCount];

        var histogram = BuildHistogram(bgra, pixelCount, out int stride);
        var entries = new ColorEntry[histogram.Count];
        int entryIndex = 0;
        foreach (var pair in histogram) entries[entryIndex++] = new ColorEntry((byte)(pair.Key >> 16), (byte)(pair.Key >> 8), (byte)pair.Key, pair.Value);

        if (entries.Length <= MaxColors)
        {
            palette = new byte[MaxColors * 3];
            for (int i = 0; i < entries.Length; i++)
            {
                palette[i * 3] = entries[i].R;
                palette[i * 3 + 1] = entries[i].G;
                palette[i * 3 + 2] = entries[i].B;
            }

            var lookup = new Dictionary<int, byte>(entries.Length);
            for (int i = 0; i < entries.Length; i++) lookup[(entries[i].R << 16) | (entries[i].G << 8) | entries[i].B] = (byte)i;

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * stride;
                int key = (bgra[offset + 2] << 16) | (bgra[offset + 1] << 8) | bgra[offset];
                indices[i] = lookup.TryGetValue(key, out byte index) ? index : (byte)0;
            }

            return;
        }

        var boxes = new List<Box>(MaxColors) { new() { Start = 0, Count = entries.Length } };
        boxes[0].Measure(entries);

        while (boxes.Count < MaxColors)
        {
            int best = -1;
            long bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Count < 2 || box.LongestSide == 0) continue;
                long score = (long)box.LongestSide * box.Weight;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0) break;
            Split(boxes[best], boxes, entries);
        }

        palette = new byte[MaxColors * 3];
        for (int i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            long r = 0, g = 0, b = 0, weight = 0;
            for (int j = box.Start; j < box.Start + box.Count; j++)
            {
                var c = entries[j];
                r += (long)c.R * c.Count;
                g += (long)c.G * c.Count;
                b += (long)c.B * c.Count;
                weight += c.Count;
            }

            if (weight <= 0) weight = 1;
            palette[i * 3] = (byte)(r / weight);
            palette[i * 3 + 1] = (byte)(g / weight);
            palette[i * 3 + 2] = (byte)(b / weight);
        }

        MapPixels(bgra, indices, width, height, stride, palette, boxes.Count, dither);
    }

    private static Dictionary<int, int> BuildHistogram(byte[] bgra, int pixelCount, out int stride)
    {
        stride = 4;
        int step = pixelCount > 1_500_000 ? 3 : pixelCount > 400_000 ? 2 : 1;
        var histogram = new Dictionary<int, int>(Math.Min(pixelCount, 4096));
        for (int i = 0; i < pixelCount; i += step)
        {
            int offset = i * 4;
            int key = (bgra[offset + 2] << 16) | (bgra[offset + 1] << 8) | bgra[offset];
            histogram.TryGetValue(key, out int count);
            histogram[key] = count + 1;
        }

        return histogram;
    }

    private static void Split(Box box, List<Box> boxes, ColorEntry[] colors)
    {
        int channel = 0;
        int range = box.RMax - box.RMin;
        if (box.GMax - box.GMin > range)
        {
            channel = 1;
            range = box.GMax - box.GMin;
        }

        if (box.BMax - box.BMin > range) channel = 2;

        Array.Sort(colors, box.Start, box.Count, Comparer<ColorEntry>.Create((a, b) => channel switch
        {
            0 => a.R.CompareTo(b.R),
            1 => a.G.CompareTo(b.G),
            _ => a.B.CompareTo(b.B)
        }));

        long half = box.Weight / 2;
        long cumulative = 0;
        int split = box.Start + 1;
        for (int i = box.Start; i < box.Start + box.Count - 1; i++)
        {
            cumulative += colors[i].Count;
            if (cumulative >= half)
            {
                split = i + 1;
                break;
            }

            split = i + 2;
        }

        split = Math.Clamp(split, box.Start + 1, box.Start + box.Count - 1);
        var second = new Box { Start = split, Count = box.Start + box.Count - split };
        box.Count = split - box.Start;

        box.Measure(colors);
        second.Measure(colors);
        boxes.Add(second);
    }

    private static void MapPixels(byte[] bgra, byte[] indices, int width, int height, int stride, byte[] palette, int colorCount, bool dither)
    {
        var cache = new short[32768];
        Array.Fill(cache, (short)-1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * stride;
                int b = bgra[offset];
                int g = bgra[offset + 1];
                int r = bgra[offset + 2];

                if (dither)
                {
                    int adjustment = ((BayerMatrix[y & 3, x & 3] - 8) * 4) / 8 * 3;
                    r = Math.Clamp(r + adjustment, 0, 255);
                    g = Math.Clamp(g + adjustment, 0, 255);
                    b = Math.Clamp(b + adjustment, 0, 255);
                }

                int cacheKey = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                short cached = cache[cacheKey];
                byte index;
                if (cached >= 0)
                {
                    index = (byte)cached;
                }
                else
                {
                    index = FindNearest(r, g, b, palette, colorCount);
                    cache[cacheKey] = index;
                }

                indices[y * width + x] = index;
            }
        }
    }

    private static byte FindNearest(int r, int g, int b, byte[] palette, int colorCount)
    {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < colorCount; i++)
        {
            int dr = r - palette[i * 3];
            int dg = g - palette[i * 3 + 1];
            int db = b - palette[i * 3 + 2];
            int distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
                if (distance == 0) break;
            }
        }

        return (byte)best;
    }
}
