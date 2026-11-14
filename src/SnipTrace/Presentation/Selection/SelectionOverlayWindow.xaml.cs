using System.Windows;
using System.Windows.Input;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

public partial class SelectionOverlayWindow : Window
{
    private SelectionOverlay? _overlay;

    public SelectionOverlayWindow()
    {
        InitializeComponent();
    }

    public MonitorInfo Monitor { get; private set; } = null!;

    public void Attach(SelectionOverlay overlay, DesktopSnapshot snapshot, MonitorInfo monitor)
    {
        _overlay = overlay;
        Monitor = monitor;

        Mask.Image = snapshot.CreateMonitorSliceSource(monitor);
        Mask.MonitorBounds = monitor.Bounds;
        Mask.Scale = monitor.Scale;

        MouseLeftButtonDown += Window_MouseLeftButtonDown;
        MouseLeftButtonUp += Window_MouseLeftButtonUp;
        MouseMove += Window_MouseMove;
        MouseRightButtonDown += Window_MouseRightButtonDown;
        KeyDown += Window_KeyDown;
    }

    public void UpdateSelection(PixelRect selection) => Mask.Selection = selection;

    public void UpdateHover(PixelRect hover) => Mask.Hover = hover;

    public void UpdateAnnotations(AnnotationDocument? annotations, PixelRect selection)
    {
        Mask.Annotations = annotations;
        Mask.AnnotationArea = selection;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_overlay is null) return;
        Focus();
        CaptureMouse();
        _overlay.OnMouseDown(e.ClickCount >= 2);
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        _overlay?.OnMouseMove();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        _overlay?.OnMouseUp();
        e.Handled = true;
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _overlay?.OnRightClick();
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_overlay is null) return;
        _overlay.OnKeyDown(e.Key, Keyboard.Modifiers);
        e.Handled = true;
    }
}
