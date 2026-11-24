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

    /// <summary>上一次成功保存的副本：覆盖升级 / 被杀进程导致主文件损坏时用它回滚。</summary>
    public string BackupFile => _paths.SettingsFile + ".bak";

    public AppSettings Load()
    {
        LastLoadWarning = null;
        _paths.EnsureDirectories();

        AppSettings settings;
        if (!File.Exists(_paths.SettingsFile))
        {
            settings = AppSettings.CreateDefault();
        }
        else
        {
            settings = ReadFile(_paths.SettingsFile) ?? ReadBackupOrReset();
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

            // 先把当前这份（可用的）设置留成备份，再原子替换主文件；
            // 这样写入中途被打断（例如安装程序升级时结束进程）也不会把用户设置整份清掉。
            TryBackupCurrent();

            File.Move(temp, _paths.SettingsFile, true);
        }
        catch (Exception)
        {
            // 保存失败不能打断用户操作。
        }
    }

    private static AppSettings? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>主文件读不出来时退到备份；备份也没有可用内容才回默认值。</summary>
    private AppSettings ReadBackupOrReset()
    {
        AppSettings? backup = ReadFile(BackupFile);
        if (backup is not null)
        {
            LastLoadWarning = L.T("设置文件损坏，已从最近一次备份恢复");
            TryMoveCorruptedFile();
            return backup;
        }

        LastLoadWarning = L.T("设置文件无法读取，已恢复默认值");
        TryMoveCorruptedFile();
        return AppSettings.CreateDefault();
    }

    private void TryBackupCurrent()
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile)) return;
            File.Copy(_paths.SettingsFile, BackupFile, true);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>把损坏的文件挪到一边留证据，避免下一次保存把它直接覆盖掉。</summary>
    private void TryMoveCorruptedFile()
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
