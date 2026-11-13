using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Storage;

public sealed class RecentStore
{
    public const int Capacity = 24;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly PathService _paths;
    private List<RecentItem> _items = new();

    public RecentStore(PathService paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<RecentItem> Items => _items;

    public event Action? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(_paths.RecentFile))
            {
                string json = File.ReadAllText(_paths.RecentFile);
                _items = JsonSerializer.Deserialize<List<RecentItem>>(json, Options) ?? new List<RecentItem>();
            }
        }
        catch (Exception)
        {
            _items = new List<RecentItem>();
        }

        Prune();
    }

    public RecentItem Add(string path, RecentItemKind kind, int width, int height, double durationSeconds, Bitmap? thumbnailSource)
    {
        var item = new RecentItem
        {
            Kind = kind,
            Path = path,
            Width = width,
            Height = height,
            DurationSeconds = durationSeconds,
            CreatedAt = DateTime.Now
        };

        if (thumbnailSource is not null)
        {
            item.ThumbnailPath = SaveThumbnail(thumbnailSource, item.Id);
        }

        _items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, item);
        if (_items.Count > Capacity)
        {
            foreach (var removed in _items.Skip(Capacity)) DeleteThumbnail(removed);
            _items = _items.Take(Capacity).ToList();
        }

        Save();
        Changed?.Invoke();
        return item;
    }

    public void Remove(RecentItem item)
    {
        if (!_items.Remove(item)) return;
        DeleteThumbnail(item);
        Save();
        Changed?.Invoke();
    }

    public void Prune()
    {
        int before = _items.Count;
        _items.RemoveAll(item =>
        {
            bool missing = !File.Exists(item.Path);
            if (missing) DeleteThumbnail(item);
            return missing;
        });

        if (before != _items.Count) Save();
    }

    private string SaveThumbnail(Bitmap source, string id)
    {
        try
        {
            _paths.EnsureDirectories();
            string path = Path.Combine(_paths.ThumbnailDirectory, id + ".png");
            using var thumb = ImageUtil.Downscale(source, 320);
            using var stream = new MemoryStream();
            thumb.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllBytes(path, stream.ToArray());
            return path;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private void DeleteThumbnail(RecentItem item)
    {
        try
        {
            if (!string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath)) File.Delete(item.ThumbnailPath);
        }
        catch (Exception)
        {
        }
    }

    private void Save()
    {
        try
        {
            _paths.EnsureDirectories();
            string json = JsonSerializer.Serialize(_items, Options);
            string temp = _paths.RecentFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _paths.RecentFile, true);
        }
        catch (Exception)
        {
        }
    }
}
