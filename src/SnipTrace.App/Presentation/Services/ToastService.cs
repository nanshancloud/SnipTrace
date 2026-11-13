using System;
using System.Windows;
using SnipTrace.App.Presentation.Views;

namespace SnipTrace.App.Presentation.Services;

/// <summary>统一的操作反馈入口，避免弹出阻塞式对话框。</summary>
public sealed class ToastService
{
    private ToastWindow? _window;

    public void Info(string title, string message = "") => Show(title, message, false);

    public void Warn(string title, string message = "") => Show(title, message, true);

    private void Show(string title, string message, bool warning)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;

        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _window ??= new ToastWindow();
                _window.ShowMessage(title, message, warning);
            }
            catch (Exception)
            {
            }
        }));
    }

    public void Close()
    {
        try
        {
            _window?.Close();
            _window = null;
        }
        catch (Exception)
        {
        }
    }
}
