using System;
using System.IO;
using System.Windows.Media.Imaging;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.ViewModels;

public sealed class RecentItemView
{
    private RecentItemView(RecentItem item, BitmapSource? thumbnail)
    {
        Item = item;
        Thumbnail = thumbnail;
    }

    public RecentItem Item { get; }

    public BitmapSource? Thumbnail { get; }

    public string Title => Path.GetFileNameWithoutExtension(Item.Path);

    public string Subtitle => Item.Kind switch
    {
        RecentItemKind.Screenshot => $"PNG · {Item.Width} × {Item.Height}",
        RecentItemKind.Gif => $"GIF · {Item.Width} × {Item.Height} · {FormatDuration()}",
        _ => $"MP4 · {Item.Width} × {Item.Height} · {FormatDuration()}"
    };

    public string KindLabel => Item.Kind switch
    {
        RecentItemKind.Screenshot => "截图",
        RecentItemKind.Gif => "GIF",
        _ => "录屏"
    };

    public string TimeLabel => Item.CreatedAt.ToString("MM-dd HH:mm");

    private string FormatDuration()
    {
        var span = TimeSpan.FromSeconds(Item.DurationSeconds);
        return $"{span.Minutes:00}:{span.Seconds:00}";
    }

    public static RecentItemView FromItem(RecentItem item)
    {
        BitmapSource? thumbnail = null;
        try
        {
            if (!string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.UriSource = new Uri(item.ThumbnailPath);
                image.EndInit();
                image.Freeze();
                thumbnail = image;
            }
        }
        catch (Exception)
        {
            thumbnail = null;
        }

        return new RecentItemView(item, thumbnail);
    }
}
