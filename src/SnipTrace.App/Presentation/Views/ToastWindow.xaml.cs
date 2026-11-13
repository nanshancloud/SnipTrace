using System;
using System.Windows;
using System.Windows.Threading;

namespace SnipTrace.App.Presentation.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;

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

    public void ShowMessage(string title, string body, bool warning)
    {
        TitleText.Text = title;
        BodyText.Text = body;
        BodyText.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
        AccentBar.Background = (System.Windows.Media.Brush)FindResource(warning ? "DangerBrush" : "AccentBrush");

        if (!IsVisible) Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 8, area.Right - ActualWidth - 12);
        Top = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 12);

        _timer.Interval = TimeSpan.FromSeconds(warning ? 6 : 3.2);
        _timer.Stop();
        _timer.Start();
    }
}
