using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>一次版本检查的结果。失败时 <see cref="Error"/> 给出原因，界面据此提示。</summary>
public sealed record UpdateCheckResult
{
    public bool Succeeded { get; init; }

    public bool HasUpdate { get; init; }

    /// <summary>远端最新版本（形如 1.2.0），失败时为空。</summary>
    public string LatestVersion { get; init; } = string.Empty;

    /// <summary>下载 / 发布页地址。</summary>
    public string DownloadUrl { get; init; } = UpdateService.ReleasesPageUrl;

    public string? Error { get; init; }
}

/// <summary>
/// 更新检查：读 GitHub 上的 Releases / Tags，挑出形如 v1.2.3 的版本标签与当前版本比较。
/// 只做提示，不自动下载安装；网络失败或不联网时静默退化。
/// </summary>
public static class UpdateService
{
    public const string LatestReleaseApi = "https://api.github.com/repos/nanshancloud/SnipTrace/releases/latest";
    public const string ReleasesApi = "https://api.github.com/repos/nanshancloud/SnipTrace/releases?per_page=20";
    public const string TagsApi = "https://api.github.com/repos/nanshancloud/SnipTrace/tags?per_page=30";
    public const string ReleasesPageUrl = "https://github.com/nanshancloud/SnipTrace/releases/latest";

    /// <summary>当前程序集版本（csproj 的 Version）。</summary>
    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

    /// <summary>当前版本的可读形式，如 1.0.0。</summary>
    public static string CurrentVersionText => ToText(CurrentVersion);

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SnipTrace/" + CurrentVersionText);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            // 先看 Releases 列表：tag 不一定是纯版本号，顺带尝试 release 标题。
            string? releases = await TryGetStringAsync(http, ReleasesApi, cancellationToken).ConfigureAwait(false);
            if (releases is not null)
            {
                UpdateCheckResult fromReleases = ParseReleaseList(releases, CurrentVersion);
                if (fromReleases.Succeeded) return fromReleases;
            }

            // 再退到 Tags 列表：仓库只打 tag 没建 release 时也能判断版本。
            string? tags = await TryGetStringAsync(http, TagsApi, cancellationToken).ConfigureAwait(false);
            if (tags is not null)
            {
                UpdateCheckResult fromTags = ParseTagList(tags, CurrentVersion);
                if (fromTags.Succeeded) return fromTags;

                return new UpdateCheckResult
                {
                    Succeeded = false,
                    Error = "仓库还没有形如 v1.0.0 的版本标签"
                };
            }

            return new UpdateCheckResult { Succeeded = false, Error = "无法访问 GitHub，请检查网络" };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Succeeded = false, Error = "请求超时" };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { Succeeded = false, Error = ex.Message };
        }
    }

    private static async Task<string?> TryGetStringAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>解析 releases 列表，取第一个能识别出、且最新的版本标签。</summary>
    public static UpdateCheckResult ParseReleaseList(string json, Version current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new UpdateCheckResult { Succeeded = false, Error = "响应格式异常" };
            }

            Version? newest = null;
            string url = ReleasesPageUrl;

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                string? tag = ReadString(item, "tag_name");
                string? name = ReadString(item, "name");
                Version? version = ParseTag(tag) ?? ParseTag(name);
                if (version is null) continue;

                if (newest is null || version > newest)
                {
                    newest = version;
                    url = ReadString(item, "html_url") ?? ReleasesPageUrl;
                }
            }

            return newest is null
                ? new UpdateCheckResult { Succeeded = false, Error = "没有可识别的版本标签" }
                : CreateResult(newest, url, current);
        }
        catch (JsonException ex)
        {
            return new UpdateCheckResult { Succeeded = false, Error = "响应格式异常：" + ex.Message };
        }
    }

    /// <summary>解析 tags 列表（无 release 时的退路）。</summary>
    public static UpdateCheckResult ParseTagList(string json, Version current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new UpdateCheckResult { Succeeded = false, Error = "响应格式异常" };
            }

            Version? newest = null;
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                Version? version = ParseTag(ReadString(item, "name"));
                if (version is null) continue;
                if (newest is null || version > newest) newest = version;
            }

            return newest is null
                ? new UpdateCheckResult { Succeeded = false, Error = "没有可识别的版本标签" }
                : CreateResult(newest, ReleasesPageUrl, current);
        }
        catch (JsonException ex)
        {
            return new UpdateCheckResult { Succeeded = false, Error = "响应格式异常：" + ex.Message };
        }
    }

    /// <summary>解析 GitHub「latest release」单个对象（保留给旧接口与测试使用）。</summary>
    public static UpdateCheckResult ParseLatestRelease(string json, Version current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string? tag = ReadString(root, "tag_name");
            Version? latest = ParseTag(tag) ?? ParseTag(ReadString(root, "name"));
            if (latest is null)
            {
                return new UpdateCheckResult { Succeeded = false, Error = "无法解析版本号：" + (tag ?? "空") };
            }

            return CreateResult(latest, ReadString(root, "html_url") ?? ReleasesPageUrl, current);
        }
        catch (JsonException ex)
        {
            return new UpdateCheckResult { Succeeded = false, Error = "响应格式异常：" + ex.Message };
        }
    }

    /// <summary>把 tag（v1.2.3 / 1.2.3-beta）解析成三段版本号，失败返回 null。</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string text = tag.Trim().TrimStart('v', 'V');
        int suffix = text.IndexOfAny(new[] { '-', '+' });
        if (suffix > 0) text = text[..suffix];

        return Version.TryParse(text, out Version? version) ? Normalize(version) : null;
    }

    /// <summary>远端是否比当前新：只比 Major.Minor.Build，忽略 Revision。</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    private static UpdateCheckResult CreateResult(Version latest, string url, Version current) => new()
    {
        Succeeded = true,
        HasUpdate = IsNewer(latest, current),
        LatestVersion = ToText(latest),
        DownloadUrl = string.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url
    };

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string ToText(Version version) => Normalize(version).ToString(3);
}
