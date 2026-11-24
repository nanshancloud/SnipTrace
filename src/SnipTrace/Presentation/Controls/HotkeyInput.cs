using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Controls;

/// <summary>
/// 快捷键录入控件：点一下进入录入态，直接按组合键即可（Ctrl+Shift+G 这类）；
/// 组合键按「键帽」展示——每个按键各自一个圆角矩形，空值时提示「点击后按键录入」。
/// </summary>
public sealed class HotkeyInput : Border
{
    private readonly TextBox _sink;
    private readonly StackPanel _chips;
    private readonly TextBlock _placeholder;
    private string _value = string.Empty;

    /// <summary>用户录入（含 Backspace 清空）时触发；程序回填不触发。</summary>
    public event EventHandler? ValueChanged;

    public HotkeyInput()
    {
        CornerRadius = new CornerRadius(9);
        BorderThickness = new Thickness(1);
        MinHeight = 36;
        Padding = new Thickness(6, 5, 6, 5);
        Cursor = Cursors.Hand;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        ToolTip = L.T("点击后直接按下组合键即可录入（Backspace 清除，Esc 退出录入）");

        _chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        _placeholder = new TextBlock
        {
            Text = L.T("点击后按键录入"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        };
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        // 透明的 1×1 输入宿主：只负责拿焦点和收键，用户看不到它。
        _sink = new TextBox
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            Focusable = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            CaretBrush = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var host = new Grid();
        host.Children.Add(_chips);
        host.Children.Add(_placeholder);
        host.Children.Add(_sink);
        Child = host;

        _sink.PreviewKeyDown += Sink_PreviewKeyDown;
        _sink.GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "AccentBrush");
        _sink.LostKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "BorderBrush");
        MouseLeftButtonDown += (_, e) =>
        {
            Keyboard.Focus(_sink);
            e.Handled = true;
        };

        UpdateChips();
    }

    /// <summary>自检用：当前展示的键帽数量（组合键每个按键一个圆角矩形）。</summary>
    internal int KeyCapCount => _chips.Children.OfType<Border>().Count();

    /// <summary>快捷键文本（如 <c>Ctrl+Shift+G</c>），直接对接 <see cref="HotkeyGesture"/> 的解析。</summary>
    public string Value
    {
        get => _value;
        set
        {
            string text = value ?? string.Empty;
            if (string.Equals(_value, text, StringComparison.Ordinal)) return;
            _value = text;
            UpdateChips();
        }
    }

    private void Sink_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt 组合键会以 SystemKey 形式到达。
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Tab 留给焦点切换；Esc 退出录入但保留原值。
        if (key == Key.Tab) return;
        if (key == Key.Escape)
        {
            e.Handled = true;
            Keyboard.ClearFocus();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            e.Handled = true;
            Commit(string.Empty);
            return;
        }

        // 只按修饰键不算一次录入。
        if (key == Key.None || IsModifierKey(key) || key is Key.ImeProcessed or Key.DeadCharProcessed) return;

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!HotkeyGesture.TryGetKeyName(virtualKey, out string keyName)) return;

        var gesture = new HotkeyGesture(ToHotkeyModifiers(Keyboard.Modifiers), virtualKey, keyName);
        e.Handled = true;
        Commit(gesture.ToDisplayString());
    }

    private void Commit(string text)
    {
        if (string.Equals(_value, text, StringComparison.Ordinal))
        {
            UpdateChips();
            return;
        }

        _value = text;
        UpdateChips();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateChips()
    {
        _chips.Children.Clear();

        string[] tokens = _value.Split('+', StringSplitOptions.RemoveEmptyEntries);
        bool empty = tokens.Length == 0;
        _chips.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty) return;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (i > 0) _chips.Children.Add(CreatePlus());
            _chips.Children.Add(CreateKeyCap(tokens[i]));
        }
    }

    private static TextBlock CreatePlus()
    {
        var plus = new TextBlock
        {
            Text = "+",
            FontSize = 11,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return plus;
    }

    /// <summary>单个按键：圆角矩形「键帽」。</summary>
    private static Border CreateKeyCap(string token)
    {
        var cap = new Border
        {
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        cap.SetResourceReference(BackgroundProperty, "SurfaceHoverBrush");
        cap.SetResourceReference(BorderBrushProperty, "BorderBrush");

        var text = new TextBlock
        {
            Text = token,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        cap.Child = text;
        return cap;
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifiers)
    {
        var result = HotkeyModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0) result |= HotkeyModifiers.Control;
        if ((modifiers & ModifierKeys.Shift) != 0) result |= HotkeyModifiers.Shift;
        if ((modifiers & ModifierKeys.Alt) != 0) result |= HotkeyModifiers.Alt;
        if ((modifiers & ModifierKeys.Windows) != 0) result |= HotkeyModifiers.Win;
        return result;
    }
}
