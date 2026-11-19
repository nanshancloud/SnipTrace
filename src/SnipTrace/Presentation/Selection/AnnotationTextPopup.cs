using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 覆盖层文字标注的输入弹窗：贴着点击位置出现并自动聚焦，Enter 提交、Esc 取消；
/// 鼠标移出输入框（或失焦）自动保存，微信截图的手感。
/// </summary>
public sealed class AnnotationTextPopup : Window
{
    private readonly TextBox _input = new();
    private bool _closing;
    private bool _settled;

    public AnnotationTextPopup(PointD origin, AnnotationStyle style, string? fontFamily = null)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 320;

        var color = Color.FromArgb(
            (byte)((style.Argb >> 24) & 0xFF),
            (byte)((style.Argb >> 16) & 0xFF),
            (byte)((style.Argb >> 8) & 0xFF),
            (byte)(style.Argb & 0xFF));

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6)
        };
        _input.MinHeight = 32;
        _input.AcceptsReturn = false;
        _input.FontSize = 14;
        if (!string.IsNullOrWhiteSpace(fontFamily)) _input.FontFamily = new FontFamily(fontFamily);
        _input.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
        _input.Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"];
        _input.CaretBrush = (Brush)Application.Current.Resources["AccentBrush"];
        _input.BorderBrush = Brushes.Transparent;
        _input.VerticalContentAlignment = VerticalAlignment.Center;
        border.Child = _input;
        Content = border;

        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    Commit();
                    break;
                case Key.Escape:
                    e.Handled = true;
                    Cancel();
                    break;
            }
        };

        // 鼠标移出输入框即视为输入完成：自动保存（空内容等同取消）。
        bool entered = false;
        border.MouseEnter += (_, _) => entered = true;
        border.MouseLeave += (_, _) =>
        {
            if (entered) Commit();
        };

        // 窗口已经在关闭流程里时不能再调 Close()，否则会抛「窗口关闭期间无法…」。
        Closing += (_, _) => _closing = true;
        Closed += (_, _) => _settled = true;
        LostFocus += (_, _) => Commit();
    }

    /// <summary>提交文字内容（空文本表示取消）。</summary>
    public event Action<string>? Committed;

    /// <summary>显示后调用：把窗口激活并把焦点交给输入框（已 Loaded 也能立即生效）。</summary>
    public void FocusInput()
    {
        void Apply()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) WindowPlacement.ApplyToolWindowStyle(this);
            Activate();
            _input.Focus();
            Keyboard.Focus(_input);
            _input.SelectAll();
        }

        if (IsLoaded) Apply();
        else Loaded += (_, _) => Apply();
    }

    private void Commit()
    {
        if (_settled) return;
        _settled = true;

        string text = _input.Text;
        CloseSafely();
        Committed?.Invoke(text);
    }

    private void Cancel()
    {
        if (_settled) return;
        _settled = true;
        CloseSafely();
    }

    private void CloseSafely()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
