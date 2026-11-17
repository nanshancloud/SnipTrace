using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Presentation.Services;

namespace SnipTrace.App.Presentation.Views;

/// <summary>关于页：版本信息、检查更新、项目地址与作者，与设置页平级出现在导航里。</summary>
public partial class AboutView : UserControl
{
    private const string RepositoryUrl = "https://github.com/nanshancloud/SnipTrace";

    private string _downloadUrl = UpdateService.ReleasesPageUrl;
    private bool _checking;

    public AboutView()
    {
        InitializeComponent();
        VersionText.Text = "版本 " + UpdateService.CurrentVersionText;
    }

    /// <summary>手动检查更新：启动时的自动检查可在设置里关闭，这里始终可用。</summary>
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_checking) return;

        _checking = true;
        CheckUpdateButton.IsEnabled = false;
        DownloadButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "正在检查…";

        try
        {
            UpdateCheckResult result = await UpdateService.CheckAsync();
            if (!result.Succeeded)
            {
                UpdateStatusText.Text = "检查失败：" + (result.Error ?? "网络不可用");
                return;
            }

            _downloadUrl = result.DownloadUrl;
            if (result.HasUpdate)
            {
                UpdateStatusText.Text = $"发现新版本 v{result.LatestVersion}（当前 v{UpdateService.CurrentVersionText}）";
                DownloadButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = $"已是最新版本（v{UpdateService.CurrentVersionText}）";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "检查失败：" + ex.Message;
        }
        finally
        {
            _checking = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e) => OpenUrl(_downloadUrl);

    private void RepositoryLink_Click(object sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn("无法打开浏览器", ex.Message);
        }
    }
}
