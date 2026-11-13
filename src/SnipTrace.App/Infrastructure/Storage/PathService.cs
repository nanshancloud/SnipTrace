using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SnipTrace.App.Infrastructure.Storage;

public sealed class PathService
{
    public PathService()
    {
        RootDirectory = ResolveRoot(out bool fallback);
        RootWasFallback = fallback;
        SettingsFile = Path.Combine(RootDirectory, "settings.json");
        RecentFile = Path.Combine(RootDirectory, "recent.json");
        TempDirectory = Path.Combine(RootDirectory, "Temp");
        ThumbnailDirectory = Path.Combine(RootDirectory, "Thumbnails");
    }

    public string RootDirectory { get; }

    /// <summary>首选目录不可写时回退到临时目录，界面可据此提示用户。</summary>
    public bool RootWasFallback { get; }

    public string SettingsFile { get; }

    public string RecentFile { get; }

    public string TempDirectory { get; }

    public string ThumbnailDirectory { get; }

    public string DefaultScreenshotDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnipTrace", "Screenshots");

    public string DefaultVideoDirectory
    {
        get
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
            {
                videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            }

            return Path.Combine(videos, "SnipTrace");
        }
    }

    public void EnsureDirectories()
    {
        TryCreate(RootDirectory);
        TryCreate(TempDirectory);
        TryCreate(ThumbnailDirectory);
    }

    public bool EnsureDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        return TryCreate(directory);
    }

    /// <summary>目录存在且可写入（通过写入探测文件判断）。</summary>
    public static bool IsWritable(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        if (!TryCreate(directory)) return false;

        try
        {
            string probe = Path.Combine(directory, $".snipTrace_write_{Guid.NewGuid():N}");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>启动时清理 24 小时前的临时文件。</summary>
    public int CleanupTemp(TimeSpan? olderThan = null)
    {
        var limit = olderThan ?? TimeSpan.FromHours(24);
        int removed = 0;

        try
        {
            if (!Directory.Exists(TempDirectory)) return 0;
            foreach (string file in Directory.EnumerateFiles(TempDirectory))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > limit)
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (Exception)
                {
                    // 文件仍被占用时留待下次清理。
                }
            }
        }
        catch (Exception)
        {
        }

        return removed;
    }

    public IEnumerable<string> EnumerateTempFiles()
    {
        try
        {
            return Directory.Exists(TempDirectory) ? Directory.EnumerateFiles(TempDirectory).ToList() : Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static string ResolveRoot(out bool fallback)
    {
        fallback = false;
        string preferred = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipTrace");
        if (IsWritable(preferred)) return preferred;

        fallback = true;
        string temp = Path.Combine(Path.GetTempPath(), "SnipTrace");
        if (IsWritable(temp)) return temp;

        string local = Path.Combine(AppContext.BaseDirectory, "SnipTraceData");
        TryCreate(local);
        return local;
    }

    private static bool TryCreate(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
