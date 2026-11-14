using System;
using System.IO;
using System.Text.Json;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Storage;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly PathService _paths;

    public SettingsStore(PathService paths)
    {
        _paths = paths;
    }

    public string? LastLoadWarning { get; private set; }

    public AppSettings Load()
    {
        LastLoadWarning = null;
        _paths.EnsureDirectories();

        AppSettings settings;
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                settings = AppSettings.CreateDefault();
            }
            else
            {
                string json = File.ReadAllText(_paths.SettingsFile);
                settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.CreateDefault();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastLoadWarning = $"设置文件无法读取，已恢复默认值：{ex.Message}";
            TryBackupCorruptedFile();
            settings = AppSettings.CreateDefault();
        }

        settings.Normalize(_paths.DefaultScreenshotDirectory, _paths.DefaultVideoDirectory);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            _paths.EnsureDirectories();
            settings.Normalize(_paths.DefaultScreenshotDirectory, _paths.DefaultVideoDirectory);
            string json = JsonSerializer.Serialize(settings, Options);
            string temp = _paths.SettingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _paths.SettingsFile, true);
        }
        catch (Exception)
        {
            // 保存失败不能打断用户操作。
        }
    }

    private void TryBackupCorruptedFile()
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile)) return;
            string backup = _paths.SettingsFile + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(_paths.SettingsFile, backup, true);
        }
        catch (Exception)
        {
        }
    }
}
