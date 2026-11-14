namespace SnipTrace.Domain;

public static class OutputNaming
{
    public static string TimestampedBaseName(string prefix, DateTime timestamp)
        => $"{prefix}_{timestamp:yyyyMMdd_HHmmss}";

    /// <summary>文件名冲突时追加序号，从不静默覆盖。</summary>
    public static string UniquePath(string directory, string baseName, string extension, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!extension.StartsWith('.')) extension = "." + extension;
        string candidate = Path.Combine(directory, baseName + extension);
        int index = 2;
        while (exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
            index++;
            if (index > 100000) break;
        }

        return candidate;
    }
}

/// <summary>产物落盘策略：先写临时文件，封装成功后替换到目标路径。</summary>
public static class OutputFile
{
    /// <summary>
    /// 把封装完成的文件移动到目标位置。同卷直接重命名；
    /// 跨卷先复制到目标目录的 .partial 暂存文件，再在目标卷内重命名，
    /// 保证目标路径上不会出现半成品。
    /// </summary>
    public static void MoveIntoPlace(string sourcePath, string targetPath)
    {
        string fullSource = Path.GetFullPath(sourcePath);
        string fullTarget = Path.GetFullPath(targetPath);

        string? targetDirectory = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(targetDirectory)) Directory.CreateDirectory(targetDirectory);

        string sourceRoot = Path.GetPathRoot(fullSource) ?? string.Empty;
        string targetRoot = Path.GetPathRoot(fullTarget) ?? string.Empty;

        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(fullSource, fullTarget, true);
            return;
        }

        string staging = fullTarget + ".partial";
        try
        {
            File.Copy(fullSource, staging, true);
            File.Move(staging, fullTarget, true);
        }
        catch (Exception)
        {
            TryDelete(staging);
            throw;
        }
        finally
        {
            TryDelete(fullSource);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}

public enum RecentItemKind
{
    Screenshot,
    Gif,
    Video
}

public sealed class RecentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RecentItemKind Kind { get; set; }
    public string Path { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
