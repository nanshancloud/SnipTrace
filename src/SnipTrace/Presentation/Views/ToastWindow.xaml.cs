using System;
using System.Windows;
using System.Windows.Threading;

namespace SnipTrace.App.Presentation.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private Action? _action;

    public ToastWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
        };
    }

    /// <param name="actionText">可选操作按钮的文案（例如「不再提示」）。</param>
    /// <param name="action">点击操作按钮后执行的回调。</param>
    public void ShowMessage(string title, string body, bool warning, string? actionText = null, Action? action = null)
    {
        TitleText.Text = title;
        BodyText.Text = body;
        BodyText.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
        AccentBar.Background = (System.Windows.Media.Brush)FindResource(warning ? "DangerBrush" : "AccentBrush");

        _action = action;
        if (action is not null && !string.IsNullOrWhiteSpace(actionText))
        {
            ActionButton.Content = actionText;
            ActionRow.Visibility = Visibility.Visible;
        }
        else
        {
            ActionRow.Visibility = Visibility.Collapsed;
            _action = null;
        }

        if (!IsVisible) Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 8, area.Right - ActualWidth - 12);
        Top = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 12);

        // 带操作按钮的提示多留一会儿，给用户点的时间。
        _timer.Interval = TimeSpan.FromSeconds(
            action is not null ? 12 : warning ? 6 : 3.2);
        _timer.Stop();
        _timer.Start();
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        Action? action = _action;
        _action = null;
        ActionRow.Visibility = Visibility.Collapsed;

        _timer.Stop();
        Hide();
        action?.Invoke();
    }
}
