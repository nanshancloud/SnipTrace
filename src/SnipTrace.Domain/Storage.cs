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
