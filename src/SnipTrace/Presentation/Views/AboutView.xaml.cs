using System;
using System.Windows;
using System.Windows.Controls;
using SnipTrace.App.Presentation.Services;

namespace SnipTrace.App.Presentation.Views;

/// <summary>关于页：版本信息、项目地址与作者，与设置页平级出现在导航里。</summary>
public partial class AboutView : UserControl
{
    private const string RepositoryUrl = "https://github.com/nanshancloud/SnipTrace";

    public AboutView()
    {
        InitializeComponent();
        VersionText.Text = "版本 " + AppVersion;
    }

    /// <summary>当前程序集版本（csproj 的 Version 字段）。</summary>
    private static string AppVersion
    {
        get
        {
            var version = typeof(AboutView).Assembly.GetName().Version;
            return version is null
                ? "1.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private void RepositoryLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RepositoryUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppServices.Current?.Toasts.Warn("无法打开浏览器", ex.Message);
        }
    }
}
