using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Recording;

public partial class CountdownWindow : Window
{
    private TaskCompletionSource<bool>? _completion;
    private DispatcherTimer? _timer;
    private int _remaining;
    private bool _finished;

    public CountdownWindow()
    {
        InitializeComponent();
    }

    public Task<bool> RunAsync(int seconds, PixelRect area, string hint)
    {
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _remaining = Math.Max(1, seconds);
        HintText.Text = hint;
        NumberText.Text = _remaining.ToString();

        Show();
        WindowPlacement.Place(this, area);
        Activate();
        Focus();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0)
            {
                Finish(true);
                return;
            }

            NumberText.Text = _remaining.ToString();
        };
        _timer.Start();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(false);
                e.Handled = true;
            }
        };
        MouseLeftButtonDown += (_, _) => Finish(false);
        return _completion.Task;
    }

    private void Finish(bool started)
    {
        if (_finished) return;
        _finished = true;
        _timer?.Stop();
        _completion?.TrySetResult(started);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _finished = true;
        _timer?.Stop();
        _completion?.TrySetResult(false);
        base.OnClosed(e);
    }
}
