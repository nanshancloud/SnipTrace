using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 覆盖层文字标注的输入弹窗：贴着点击位置出现，Enter 提交、Esc 取消。
/// </summary>
public sealed class AnnotationTextPopup : Window
{
    private readonly TextBox _input = new();

    public AnnotationTextPopup(PointD origin, AnnotationStyle style)
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
                    Committed?.Invoke(_input.Text);
                    Close();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        };
        LostFocus += (_, _) => { if (IsLoaded) Close(); };
    }

    /// <summary>提交文字内容（空文本表示取消）。</summary>
    public event Action<string>? Committed;

    public void FocusInput()
    {
        SourceInitialized += (_, _) => { };  // SourceInitialized 之后才可安全激活
        Loaded += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) WindowPlacement.ApplyToolWindowStyle(this);
            _input.Focus();
            Keyboard.Focus(_input);
        };
    }
}
