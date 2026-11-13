using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SnipTrace.App.Infrastructure.Capture;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

public enum SelectionAction
{
    Edit,
    Copy,
    Save,
    Pin,
    Gif,
    Video,
    Cancel
}

public sealed class SelectionResult
{
    public PixelRect Area { get; init; }

    public SelectionAction Action { get; init; }
}

/// <summary>
/// 多覆盖层选区控制器：所有显示器共享同一份选区状态，坐标始终使用虚拟桌面物理像素。
/// </summary>
public sealed class SelectionOverlay : IDisposable
{
    private readonly DesktopSnapshot _snapshot;
    private readonly List<SelectionOverlayWindow> _windows = new();
    private readonly SelectionToolbarWindow _toolbar = new();
    private readonly TaskCompletionSource<SelectionResult?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PixelRect _virtualBounds;

    private AspectRatio _requestedRatio = AspectRatio.Free;
    private AspectRatio _effectiveRatio = AspectRatio.Free;
    private PixelRect _selection;
    private PixelPoint _anchor;
    private PixelPoint _cursor;
    private PixelPoint _lastMagnifierCursor;
    private DateTime _lastMagnifierUpdate = DateTime.MinValue;
    private bool _dragging;
    private bool _completed;

    public SelectionOverlay(DesktopSnapshot snapshot)
    {
        _snapshot = snapshot;
        _virtualBounds = snapshot.Bounds;
        _cursor = MonitorService.GetCursorPosition();
        _toolbar.Attach(this);
    }

    public PixelRect Selection => _selection;

    public async Task<SelectionResult?> ShowAsync()
    {
        foreach (var monitor in _snapshot.Monitors)
        {
            var window = new SelectionOverlayWindow();
            window.Attach(this, _snapshot, monitor);
            window.SourceInitialized += (_, _) => WindowPlacement.ApplyToolWindowStyle(window);
            _windows.Add(window);
            window.Show();
            WindowPlacement.Place(window, monitor.Bounds);
        }

        _toolbar.Show();
        FocusCursorWindow();
        UpdateViews();
        UpdateToolbar(forceMagnifier: true);

        // 显示器 DPI 生效后再校正一次物理位置，避免混合 DPI 下偏移。
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        foreach (var window in _windows)
        {
            WindowPlacement.Place(window, window.Monitor.Bounds);
        }

        UpdateToolbar(forceMagnifier: true);
        return await _completion.Task;
    }

    public void OnMouseDown(bool doubleClick)
    {
        if (_completed) return;
        _cursor = MonitorService.GetCursorPosition();

        if (doubleClick && !_selection.IsEmpty)
        {
            Complete(SelectionAction.Copy);
            return;
        }

        _anchor = _cursor;
        _dragging = true;
        _selection = PixelRect.Empty;
        UpdateViews();
        UpdateToolbar(forceMagnifier: true);
    }

    public void OnMouseMove()
    {
        if (_completed) return;
        _cursor = MonitorService.GetCursorPosition();

        if (_dragging)
        {
            _selection = _effectiveRatio.IsFree
                ? PixelRect.FromCorners(_anchor.X, _anchor.Y, _cursor.X, _cursor.Y).ClampTo(_virtualBounds)
                : _effectiveRatio.FromDrag(_anchor, _cursor, _virtualBounds);

            if (_selection.Width < 2 || _selection.Height < 2) _selection = PixelRect.Empty;
            UpdateViews();
        }

        UpdateToolbar();
    }

    public void OnMouseUp()
    {
        if (_completed || !_dragging) return;
        _dragging = false;
        _cursor = MonitorService.GetCursorPosition();

        if (_selection.IsEmpty)
        {
            var monitor = MonitorService.FromPoint(_cursor) ?? _snapshot.Monitors[0];
            _selection = monitor.Bounds;
        }

        UpdateViews();
        UpdateToolbar(forceMagnifier: true);
    }

    public void OnRightClick()
    {
        if (_completed) return;
        if (!_selection.IsEmpty)
        {
            _selection = PixelRect.Empty;
            _dragging = false;
            UpdateViews();
            UpdateToolbar(forceMagnifier: true);
            return;
        }

        Cancel();
    }

    public void OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (_completed) return;

        switch (key)
        {
            case Key.Escape:
                Cancel();
                return;
            case Key.Enter:
                if (!_selection.IsEmpty) Complete(SelectionAction.Edit);
                return;
        }

        if (_selection.IsEmpty) return;
        int step = (modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        var moved = _selection;
        switch (key)
        {
            case Key.Left:
                moved = moved.Offset(-step, 0);
                break;
            case Key.Right:
                moved = moved.Offset(step, 0);
                break;
            case Key.Up:
                moved = moved.Offset(0, -step);
                break;
            case Key.Down:
                moved = moved.Offset(0, step);
                break;
            default:
                return;
        }

        _selection = moved.ClampTo(_virtualBounds);
        UpdateViews();
        UpdateToolbar();
    }

    public void SetRatio(AspectRatio ratio)
    {
        _requestedRatio = ratio;
        _effectiveRatio = ratio.Label == AspectRatio.Original.Label && !_selection.IsEmpty
            ? AspectRatio.ForOriginal(_selection)
            : ratio == AspectRatio.Original ? AspectRatio.Free : ratio;

        if (!_selection.IsEmpty && !_effectiveRatio.IsFree)
        {
            _selection = _effectiveRatio.FitCentered(_selection, _virtualBounds);
        }

        _toolbar.RefreshRatioChips(ratio);
        UpdateViews();
        UpdateToolbar();
    }

    public void RequestAction(SelectionAction action)
    {
        if (_completed) return;
        if (action == SelectionAction.Cancel)
        {
            Cancel();
            return;
        }

        if (_selection.IsEmpty) return;
        Complete(action);
    }

    public void Cancel()
    {
        if (_completed) return;
        _completed = true;
        CloseAll();
        _completion.TrySetResult(null);
    }

    private void Complete(SelectionAction action)
    {
        if (_completed) return;
        _completed = true;
        var result = new SelectionResult { Area = _selection, Action = action };
        CloseAll();
        _completion.TrySetResult(result);
    }

    private void CloseAll()
    {
        try
        {
            _toolbar.Hide();
            foreach (var window in _windows) window.Close();
            _windows.Clear();
        }
        catch (Exception)
        {
        }
    }

    private void UpdateViews()
    {
        foreach (var window in _windows) window.UpdateSelection(_selection);
    }

    private void UpdateToolbar(bool forceMagnifier = false)
    {
        bool hasGap = HasUncoveredArea();
        _toolbar.UpdateInfo(_selection, _cursor, DescribeRatio(_selection, _requestedRatio), hasGap);

        if (forceMagnifier || _cursor != _lastMagnifierCursor || (DateTime.UtcNow - _lastMagnifierUpdate).TotalMilliseconds > 80)
        {
            _lastMagnifierCursor = _cursor;
            _lastMagnifierUpdate = DateTime.UtcNow;
            try
            {
                var magnifier = ImageUtil.CreateMagnifier(_snapshot, _cursor, 12, 6);
                var pixel = ScreenGrabber.GetPixelColor(_snapshot, _cursor);
                _toolbar.UpdateMagnifier(magnifier, System.Windows.Media.Color.FromRgb(pixel.R, pixel.G, pixel.B));
            }
            catch (Exception)
            {
            }
        }

        PlaceToolbar();
    }

    private void PlaceToolbar()
    {
        if (!_toolbar.IsVisible) return;
        _toolbar.UpdateLayout();

        var monitor = MonitorService.FromPoint(_cursor) ?? _snapshot.Monitors[0];
        var work = monitor.WorkArea;
        var (width, height) = WindowPlacement.GetPixelSize(_toolbar);
        if (width <= 0 || height <= 0)
        {
            double scale = monitor.Scale;
            width = (int)(_toolbar.Width * scale);
            height = (int)(140 * scale);
        }

        int x;
        int y;
        if (_selection.IsEmpty)
        {
            x = _cursor.X + 18;
            y = _cursor.Y + 22;
        }
        else
        {
            x = _selection.Left;
            y = _selection.Bottom + 8;
            if (y + height > work.Bottom - 4) y = _selection.Top - height - 8;
            if (y < work.Top + 4) y = Math.Min(work.Bottom - height - 4, _selection.Bottom - height - 8);
        }

        x = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
        y = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
        WindowPlacement.MoveTo(_toolbar, x, y);
    }

    private bool HasUncoveredArea()
    {
        if (_selection.IsEmpty) return false;
        long covered = 0;
        foreach (var monitor in _snapshot.Monitors)
        {
            covered += PixelRect.Intersect(monitor.Bounds, _selection).Area;
        }

        return covered < _selection.Area;
    }

    private void FocusCursorWindow()
    {
        var cursor = MonitorService.GetCursorPosition();
        foreach (var window in _windows)
        {
            if (!window.Monitor.Bounds.Contains(cursor)) continue;
            window.Activate();
            window.Focus();
            return;
        }

        if (_windows.Count > 0)
        {
            _windows[0].Activate();
            _windows[0].Focus();
        }
    }

    private static string DescribeRatio(PixelRect rect, AspectRatio requested)
    {
        if (rect.IsEmpty) return requested.IsFree ? "自由" : requested.Label;
        if (!requested.IsFree && requested.Label != AspectRatio.Original.Label) return requested.Label;

        int gcd = Gcd(rect.Width, rect.Height);
        if (gcd > 0)
        {
            int w = rect.Width / gcd;
            int h = rect.Height / gcd;
            if (w <= 64 && h <= 64) return $"{w}:{h}";
        }

        return $"{rect.AspectRatio:0.00}:1";
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return Math.Max(1, Math.Abs(a));
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _completed = true;
            CloseAll();
        }
    }
}
