using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Recording;

/// <summary>录制边框：位于选区外侧 3 px，不进入录制画面。</summary>
public sealed class RecordingFrame : IDisposable
{
    private const int Thickness = 3;

    private static readonly Brush RecordingBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5F)));
    private static readonly Brush PausedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xB0, 0x2E)));

    private readonly List<Window> _windows = new();

    public void Show(PixelRect area)
    {
        AddStrip(new PixelRect(area.Left - Thickness, area.Top - Thickness, area.Width + Thickness * 2, Thickness));
        AddStrip(new PixelRect(area.Left - Thickness, area.Bottom, area.Width + Thickness * 2, Thickness));
        AddStrip(new PixelRect(area.Left - Thickness, area.Top, Thickness, area.Height));
        AddStrip(new PixelRect(area.Right, area.Top, Thickness, area.Height));
    }

    public void SetPaused(bool paused)
    {
        var brush = paused ? PausedBrush : RecordingBrush;
        foreach (var window in _windows) window.Background = brush;
    }

    private void AddStrip(PixelRect bounds)
    {
        try
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = RecordingBrush,
                AllowsTransparency = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            window.SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(window);
            window.Show();
            WindowPlacement.Place(window, bounds);
            _windows.Add(window);
        }
        catch (Exception)
        {
        }
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }

    public void Dispose()
    {
        foreach (var window in _windows)
        {
            try
            {
                window.Close();
            }
            catch (Exception)
            {
            }
        }

        _windows.Clear();
    }
}
