using System.Windows;
using SnipTrace.App.Infrastructure.Platform;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 工具条的悬浮介绍：独立的不激活置顶窗口，
/// 避免系统 ToolTip 被全屏置顶的选区覆盖层压在下面。
/// </summary>
public partial class SelectionHintWindow : Window
{
    public SelectionHintWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void ShowHint(string text)
    {
        HintText.Text = text;
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
    }

    public void HideHint()
    {
        if (IsVisible) Hide();
    }
}
