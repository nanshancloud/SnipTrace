using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SnipTrace.App.Infrastructure.Encoding;

/// <summary>Media Foundation SinkWriter 封装：H.264 / MP4，NV12 输入。</summary>
public sealed class Mp4Encoder : IDisposable
{
    private const int MFVersion = 0x00020070;
    private const int MfVideoInterlaceProgressive = 2;
    private const int H264ProfileHigh = 100;

    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatNv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MfMtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MfMtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MfMtPixelAspectRatio = new("c6376a1e-8d0c-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MfMtMpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    private static readonly Guid MfMtAllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
    private static readonly Guid MfReadWriteEnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MfSinkWriterDisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

    private static readonly object StartupGate = new();
    private static bool _mfStarted;
    private static bool? _available;
    private static string? _unavailableReason;

    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly string _path;
    private readonly byte[] _nv12;

    private IMFSinkWriter? _writer;
    private int _streamIndex;
    private bool _finalized;
    private bool _disposed;

    public Mp4Encoder(string path, int width, int height, int fps, int bitRate)
    {
        _path = path;
        _fps = Math.Max(1, fps);

        var even = SnipTrace.Domain.MediaDimensions.H264EvenSize(width, height);
        _width = even.Width;
        _height = even.Height;
        _nv12 = new byte[_width * _height * 3 / 2];

        EnsureMediaFoundation();
        CreateSinkWriter(Math.Max(500_000, bitRate));
    }

    public int EncodedWidth => _width;

    public int EncodedHeight => _height;

    /// <summary>启动探测：尝试真实创建一次编码管线，失败时给出可操作原因。</summary>
    public static (bool Available, string? Reason) ProbeAvailability(string? directory = null)
    {
        lock (StartupGate)
        {
            if (_available.HasValue) return (_available.Value, _unavailableReason);

            string targetDirectory = string.IsNullOrWhiteSpace(directory) ? Path.GetTempPath() : directory;
            try
            {
                Directory.CreateDirectory(targetDirectory);
            }
            catch (Exception)
            {
            }

            string temp = Path.Combine(targetDirectory, $"snipTrace_probe_{Guid.NewGuid():N}.mp4");
            try
            {
                using (var encoder = new Mp4Encoder(temp, 320, 240, 30, 1_500_000))
                using (var frame = new Bitmap(320, 240, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.Black);
                    encoder.WriteFrame(frame, 0);
                    encoder.Finish();
                }

                _available = File.Exists(temp);
                _unavailableReason = _available.Value ? null : "无法生成 MP4 文件。";
            }
            catch (Exception ex)
            {
                _available = false;
                _unavailableReason = ex.Message;
            }
            finally
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch (Exception)
                {
                }
            }

            return (_available.Value, _unavailableReason);
        }
    }

    public void WriteFrame(Bitmap frame, long timestamp100ns)
    {
        if (_writer is null || _finalized) return;
        FillNv12(frame);

        Check(MFCreateMemoryBuffer(_nv12.Length, out var buffer));
        try
        {
            Check(buffer.Lock(out IntPtr pointer, out _, out _));
            try
            {
                Marshal.Copy(_nv12, 0, pointer, _nv12.Length);
            }
            finally
            {
                buffer.Unlock();
            }

            buffer.SetCurrentLength(_nv12.Length);

            Check(MFCreateSample(out var sample));
            try
            {
                sample.AddBuffer(buffer);
                sample.SetSampleTime(timestamp100ns);
                sample.SetSampleDuration(10_000_000L / _fps);
                Check(_writer.WriteSample(_streamIndex, sample));
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    public void Finish()
    {
        if (_writer is null || _finalized) return;
        Check(_writer.DoFinalize());
        _finalized = true;
        ReleaseWriter();
    }

    private void CreateSinkWriter(int bitRate)
    {
        Check(MFCreateAttributes(out var attributes, 4));
        try
        {
            Guid hardware = MfReadWriteEnableHardwareTransforms;
            attributes.SetUINT32(ref hardware, 1);
            Guid throttle = MfSinkWriterDisableThrottling;
            attributes.SetUINT32(ref throttle, 1);

            Check(MFCreateSinkWriterFromURL(_path, IntPtr.Zero, attributes, out _writer));

            Check(MFCreateMediaType(out var outputType));
            try
            {
                Guid majorKey = MfMtMajorType;
                Guid subtypeKey = MfMtSubtype;
                Guid videoMajor = MfMediaTypeVideo;
                Guid h264 = MfVideoFormatH264;
                outputType.SetGUID(ref majorKey, ref videoMajor);
                outputType.SetGUID(ref subtypeKey, ref h264);
                SetUInt32(outputType, MfMtAvgBitrate, bitRate);
                SetUInt32(outputType, MfMtInterlaceMode, MfVideoInterlaceProgressive);
                SetUInt32(outputType, MfMtMpeg2Profile, H264ProfileHigh);
                SetUInt64(outputType, MfMtFrameSize, ((long)_width << 32) | (uint)_height);
                SetUInt64(outputType, MfMtFrameRate, ((long)_fps << 32) | 1u);
                SetUInt64(outputType, MfMtPixelAspectRatio, (1L << 32) | 1u);
                Check(_writer.AddStream(outputType, out _streamIndex));
            }
            finally
            {
                Marshal.ReleaseComObject(outputType);
            }

            Check(MFCreateMediaType(out var inputType));
            try
            {
                Guid majorKey = MfMtMajorType;
                Guid subtypeKey = MfMtSubtype;
                Guid videoMajor = MfMediaTypeVideo;
                Guid nv12 = MfVideoFormatNv12;
                inputType.SetGUID(ref majorKey, ref videoMajor);
                inputType.SetGUID(ref subtypeKey, ref nv12);
                SetUInt32(inputType, MfMtInterlaceMode, MfVideoInterlaceProgressive);
                SetUInt32(inputType, MfMtAllSamplesIndependent, 1);
                SetUInt64(inputType, MfMtFrameSize, ((long)_width << 32) | (uint)_height);
                SetUInt64(inputType, MfMtFrameRate, ((long)_fps << 32) | 1u);
                SetUInt64(inputType, MfMtPixelAspectRatio, (1L << 32) | 1u);
                Check(_writer.SetInputMediaType(_streamIndex, inputType, null));
            }
            finally
            {
                Marshal.ReleaseComObject(inputType);
            }

            Check(_writer.BeginWriting());
        }
        finally
        {
            Marshal.ReleaseComObject(attributes);
        }
    }

    private unsafe void FillNv12(Bitmap frame)
    {
        Bitmap? converted = null;
        var source = frame;
        if (frame.Width != _width || frame.Height != _height || frame.PixelFormat != PixelFormat.Format32bppArgb)
        {
            converted = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(converted);
            graphics.DrawImage(frame, new Rectangle(0, 0, _width, _height), new Rectangle(0, 0, _width, _height), GraphicsUnit.Pixel);
            source = converted;
        }

        var data = source.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int yIndex = 0;
            for (int y = 0; y < _height; y++)
            {
                byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
                for (int x = 0; x < _width; x++)
                {
                    int b = row[x * 4];
                    int g = row[x * 4 + 1];
                    int r = row[x * 4 + 2];
                    int luma = ((13933 * r + 46871 * g + 4732 * b) >> 16) + 16;
                    _nv12[yIndex++] = (byte)Math.Clamp(luma, 16, 235);
                }
            }

            int uvIndex = _width * _height;
            for (int y = 0; y < _height; y += 2)
            {
                byte* row0 = (byte*)data.Scan0 + (long)y * data.Stride;
                byte* row1 = (byte*)data.Scan0 + (long)Math.Min(y + 1, _height - 1) * data.Stride;
                for (int x = 0; x < _width; x += 2)
                {
                    int x1 = Math.Min(x + 1, _width - 1);
                    int r = (row0[x * 4 + 2] + row0[x1 * 4 + 2] + row1[x * 4 + 2] + row1[x1 * 4 + 2]) / 4;
                    int g = (row0[x * 4 + 1] + row0[x1 * 4 + 1] + row1[x * 4 + 1] + row1[x1 * 4 + 1]) / 4;
                    int b = (row0[x * 4] + row0[x1 * 4] + row1[x * 4] + row1[x1 * 4]) / 4;
                    int u = ((-7511 * r - 25260 * g + 32768 * b) >> 16) + 128;
                    int v = ((32768 * r - 29767 * g - 3001 * b) >> 16) + 128;
                    _nv12[uvIndex++] = (byte)Math.Clamp(u, 16, 240);
                    _nv12[uvIndex++] = (byte)Math.Clamp(v, 16, 240);
                }
            }
        }
        finally
        {
            source.UnlockBits(data);
            converted?.Dispose();
        }
    }

    private static void EnsureMediaFoundation()
    {
        lock (StartupGate)
        {
            if (_mfStarted) return;
            Check(MFStartup(MFVersion, 0));
            _mfStarted = true;
        }
    }

    private void ReleaseWriter()
    {
        if (_writer is null) return;
        Marshal.ReleaseComObject(_writer);
        _writer = null;
    }

    private static void SetUInt32(IMFAttributes attributes, Guid key, int value)
    {
        Guid local = key;
        attributes.SetUINT32(ref local, value);
    }

    private static void SetUInt64(IMFAttributes attributes, Guid key, long value)
    {
        Guid local = key;
        attributes.SetUINT64(ref local, value);
    }

    private static void Check(int hr)
    {
        if (hr < 0) throw new COMException(DescribeHResult(hr), hr);
    }

    private static string DescribeHResult(int hr)
    {
        if (hr == unchecked((int)0xC00D36B4)) return "未找到可用的 H.264 编码器，请安装 Media Feature Pack。";
        if (hr == unchecked((int)0x80070002)) return "无法创建输出文件。";
        return $"Media Foundation 返回错误 0x{hr:X8}。";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_finalized) ReleaseWriter();
    }

    #region Media Foundation interop

    [ComImport]
    [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IMFAttributes other, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int size, out int length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, [Out] byte[] value, int size, out int blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr value, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, byte[] value, int size);
        [PreserveSig] int SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    [ComImport]
    [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int length);
        [PreserveSig] int SetCurrentLength(int length);
        [PreserveSig] int GetMaxLength(out int length);
    }

    [ComImport]
    [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample : IMFAttributes
    {
        [PreserveSig] new int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] new int GetItemType(ref Guid key, out int type);
        [PreserveSig] new int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] new int Compare(IMFAttributes other, int matchType, out int result);
        [PreserveSig] new int GetUINT32(ref Guid key, out int value);
        [PreserveSig] new int GetUINT64(ref Guid key, out long value);
        [PreserveSig] new int GetDouble(ref Guid key, out double value);
        [PreserveSig] new int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] new int GetStringLength(ref Guid key, out int length);
        [PreserveSig] new int GetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int size, out int length);
        [PreserveSig] new int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] new int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] new int GetBlob(ref Guid key, [Out] byte[] value, int size, out int blobSize);
        [PreserveSig] new int GetAllocatedBlob(ref Guid key, out IntPtr value, out int size);
        [PreserveSig] new int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] new int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] new int DeleteItem(ref Guid key);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32(ref Guid key, int value);
        [PreserveSig] new int SetUINT64(ref Guid key, long value);
        [PreserveSig] new int SetDouble(ref Guid key, double value);
        [PreserveSig] new int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] new int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] new int SetBlob(ref Guid key, byte[] value, int size);
        [PreserveSig] new int SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out int count);
        [PreserveSig] new int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] new int CopyAllItems(IMFAttributes destination);
        [PreserveSig] int GetSampleFlags(out int flags);
        [PreserveSig] int SetSampleFlags(int flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out int count);
        [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
        [PreserveSig] int RemoveBufferByIndex(int index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int length);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
    }

    [ComImport]
    [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType : IMFAttributes
    {
        [PreserveSig] new int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] new int GetItemType(ref Guid key, out int type);
        [PreserveSig] new int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] new int Compare(IMFAttributes other, int matchType, out int result);
        [PreserveSig] new int GetUINT32(ref Guid key, out int value);
        [PreserveSig] new int GetUINT64(ref Guid key, out long value);
        [PreserveSig] new int GetDouble(ref Guid key, out double value);
        [PreserveSig] new int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] new int GetStringLength(ref Guid key, out int length);
        [PreserveSig] new int GetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int size, out int length);
        [PreserveSig] new int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] new int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] new int GetBlob(ref Guid key, [Out] byte[] value, int size, out int blobSize);
        [PreserveSig] new int GetAllocatedBlob(ref Guid key, out IntPtr value, out int size);
        [PreserveSig] new int GetUnknown(ref Guid key, ref Guid riid, out IntPtr value);
        [PreserveSig] new int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] new int DeleteItem(ref Guid key);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32(ref Guid key, int value);
        [PreserveSig] new int SetUINT64(ref Guid key, long value);
        [PreserveSig] new int SetDouble(ref Guid key, double value);
        [PreserveSig] new int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] new int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] new int SetBlob(ref Guid key, byte[] value, int size);
        [PreserveSig] new int SetUnknown(ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out int count);
        [PreserveSig] new int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] new int CopyAllItems(IMFAttributes destination);
        [PreserveSig] int GetMajorType(out Guid majorType);
        [PreserveSig] int IsCompressedFormat(out int compressed);
        [PreserveSig] int IsEqual(IMFMediaType other, out int equal);
        [PreserveSig] int GetRepresentation(out IntPtr representation);
        [PreserveSig] int FreeRepresentation(IntPtr representation);
    }

    [ComImport]
    [Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetMediaType, out int streamIndex);
        [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
        [PreserveSig] int SendStreamTick(int streamIndex, long timestamp);
        [PreserveSig] int PlaceMarker(int streamIndex, IntPtr context);
        [PreserveSig] int NotifyEndOfSegment(int streamIndex);
        [PreserveSig] int Flush(int streamIndex);
        [PreserveSig] int DoFinalize();
        [PreserveSig] int GetServiceForStream(int streamIndex, ref Guid service, ref Guid riid, out IntPtr value);
        [PreserveSig] int GetStatistics(int streamIndex, IntPtr statistics);
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string? outputUrl,
        IntPtr byteStream,
        IMFAttributes? attributes,
        out IMFSinkWriter sinkWriter);

    #endregion
}
