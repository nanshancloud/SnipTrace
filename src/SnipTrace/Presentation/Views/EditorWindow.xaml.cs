using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Presentation.Editing;
using SnipTrace.App.Presentation.Services;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

public partial class EditorWindow : Window
{
    private static readonly uint[] Palette =
    {
        0xFFFF6B5F, 0xFFFFD166, 0xFF59E0BE, 0xFF4EA1FF, 0xFFF2F6FC, 0xFF11171F
    };

    private static readonly double[] StrokeWidths = { 1, 2, 3, 5, 8 };
    private static readonly double[] FontSizes = { 12, 16, 20, 28, 40 };
    private static readonly double[] Opacities = { 0.25, 0.5, 0.75, 1.0 };

    private readonly BitmapSource _image;
    private readonly string _baseName;
    private readonly AnnotationDocument _document;
    private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = new();

    private AnnotationStyle _style = AnnotationStyle.Default;
    private TextAnnotation? _editingText;
    private bool _editingNewText;
    private bool _updatingPanels;
    private bool _propertiesOverlay;
    private bool? _topBarIcons;

    public EditorWindow(BitmapSource image, string baseName)
    {
        InitializeComponent();
        _image = image;
        _baseName = baseName;
        _document = new AnnotationDocument(image.PixelWidth, image.PixelHeight);

        Canvas.Image = image;
        Canvas.Document = _document;
        Canvas.CurrentStyle = _style;
        Canvas.ZoomChanged += (_, _) => UpdateStatus();
        Canvas.SelectionChanged += (_, _) => SyncFromSelection();
        Canvas.TextEditRequested += BeginNewText;

        _document.Changed += (_, _) =>
        {
            UpdateStatus();
            SyncUndoButtons();
        };

        FileNameText.Text = baseName + ".png";
        InitializeTools();
        RefreshPanels();
        UpdateStatus();
        ApplyInitialSize();

        Loaded += (_, _) =>
        {
            UpdatePanelVisibility();
            Canvas.ZoomToFit();
            UpdateStatus();
            Canvas.Focus();
        };

        KeyDown += EditorWindow_KeyDown;
        SizeChanged += (_, _) => UpdatePanelVisibility();
    }

    private static AppServices Services => AppServices.Current!;

    private void ApplyInitialSize()
    {
        var area = SystemParameters.WorkArea;
        double targetWidth = Math.Min(_image.PixelWidth + 56 + 240 + 24, area.Width * 0.9);
        double targetHeight = Math.Min(_image.PixelHeight + 46 + 30 + 24, area.Height * 0.9);
        Width = Math.Clamp(targetWidth, MinWidth, Math.Max(MinWidth, area.Width * 0.95));
        Height = Math.Clamp(targetHeight, MinHeight, Math.Max(MinHeight, area.Height * 0.95));
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private void UpdatePanelVisibility()
    {
        bool narrow = ActualWidth < 1000;
        PropertiesColumn.Width = new GridLength(narrow ? 0 : 240);
        PropertiesToggle.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;

        if (narrow)
        {
            // 窄屏下属性面板改为覆盖在画布右侧的浮层，由工具栏「属性」按钮控制显隐。
            Grid.SetColumn(PropertiesPanel, 1);
            PropertiesPanel.Width = 240;
            PropertiesPanel.HorizontalAlignment = HorizontalAlignment.Right;
            PropertiesPanel.Visibility = _propertiesOverlay ? Visibility.Visible : Visibility.Collapsed;
            Panel.SetZIndex(PropertiesPanel, 10);
        }
        else
        {
            Grid.SetColumn(PropertiesPanel, 2);
            PropertiesPanel.Width = double.NaN;
            PropertiesPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            PropertiesPanel.Visibility = Visibility.Visible;
            Panel.SetZIndex(PropertiesPanel, 0);
            _propertiesOverlay = false;
            PropertiesToggle.IsChecked = false;
        }

        UpdateTopBarIcons(narrow);
    }

    private void PropertiesToggle_Click(object sender, RoutedEventArgs e)
    {
        _propertiesOverlay = PropertiesToggle.IsChecked == true;
        UpdatePanelVisibility();
    }

    /// <summary>窄屏时顶部按钮只保留图标，靠 ToolTip 说明功能。</summary>
    private void UpdateTopBarIcons(bool narrow)
    {
        if (_topBarIcons == narrow) return;
        _topBarIcons = narrow;

        var actions = new (Button Button, string Text, string Glyph)[]
        {
            (UndoButton, "撤销", "\uE7A7"),
            (RedoButton, "重做", "\uE7A6"),
            (CopyButton, "复制", "\uE8C8"),
            (SaveButton, "保存", "\uE74E"),
            (PinButton, "钉图", "\uE840"),
            (FullscreenButton, "全屏", "\uE740")
        };

        var textFont = (FontFamily)FindResource("AppFont");
        foreach (var (button, text, glyph) in actions)
        {
            button.Content = narrow ? IconContent.Create(glyph, 14) : text;
            button.FontFamily = textFont;
            button.FontSize = 13;
        }
    }

    private void InitializeTools()
    {
        foreach (var button in ToolPanel.Children.OfType<ToggleButton>())
        {
            if (button.Tag is string tag && Enum.TryParse(tag, out EditorTool tool))
            {
                _toolButtons[tool] = button;
            }
        }
    }

    #region 属性面板

    private void RefreshPanels()
    {
        _updatingPanels = true;
        try
        {
            ColorPanel.Children.Clear();
            foreach (uint color in Palette)
            {
                uint captured = color;
                var swatch = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(7),
                    Margin = new Thickness(0, 0, 6, 6),
                    Background = new SolidColorBrush(AnnotationRenderer.ToColor(color, 1)),
                    BorderThickness = new Thickness(2),
                    BorderBrush = _style.Argb == color ? new SolidColorBrush(AnnotationRenderer.ToColor(0xFFF2F6FC, 1)) : Brushes.Transparent,
                    Cursor = Cursors.Hand
                };
                swatch.MouseLeftButtonUp += (_, _) => SetColor(captured);
                ColorPanel.Children.Add(swatch);
            }

            WidthPanel.Children.Clear();
            foreach (double width in StrokeWidths)
            {
                double captured = width;
                WidthPanel.Children.Add(Chip($"{width:0}", Math.Abs(_style.StrokeWidth - width) < 0.01, () => SetWidth(captured)));
            }

            FontSizePanel.Children.Clear();
            double currentFont = Canvas.Selection.FirstOrDefault() switch
            {
                TextAnnotation text => text.FontSize,
                NumberBadgeAnnotation badge => badge.FontSize,
                _ => 20
            };
            foreach (double size in FontSizes)
            {
                double captured = size;
                FontSizePanel.Children.Add(Chip($"{size:0}", Math.Abs(currentFont - size) < 0.01, () => SetFontSize(captured)));
            }

            OpacityPanel.Children.Clear();
            foreach (double opacity in Opacities)
            {
                double captured = opacity;
                OpacityPanel.Children.Add(Chip($"{opacity * 100:0}%", Math.Abs(_style.Opacity - opacity) < 0.01, () => SetOpacity(captured)));
            }

            BadgePanel.Children.Clear();
            var badgeStyle = Canvas.Selection.OfType<NumberBadgeAnnotation>().FirstOrDefault()?.BadgeStyle ?? BadgeStyle.FilledCircle;
            BadgePanel.Children.Add(Chip("实心", badgeStyle == BadgeStyle.FilledCircle, () => SetBadgeStyle(BadgeStyle.FilledCircle)));
            BadgePanel.Children.Add(Chip("描边", badgeStyle == BadgeStyle.OutlineCircle, () => SetBadgeStyle(BadgeStyle.OutlineCircle)));
            BadgePanel.Children.Add(Chip("圆角", badgeStyle == BadgeStyle.RoundedRect, () => SetBadgeStyle(BadgeStyle.RoundedRect)));
        }
        finally
        {
            _updatingPanels = false;
        }
    }

    private ToggleButton Chip(string text, bool isChecked, Action onClick)
    {
        var chip = new ToggleButton
        {
            Content = text,
            Style = (Style)FindResource("ChipToggle"),
            Margin = new Thickness(0, 0, 6, 6),
            IsChecked = isChecked
        };
        chip.Click += (_, _) =>
        {
            if (_updatingPanels) return;
            onClick();
        };
        return chip;
    }

    private void SetColor(uint argb)
    {
        _style = _style.WithArgb(argb);
        Canvas.CurrentStyle = _style;
        Canvas.ApplyToSelection(a => a.Style = a.Style.WithArgb(argb));
        RefreshPanels();
    }

    private void SetWidth(double width)
    {
        _style = _style.WithWidth(width);
        Canvas.CurrentStyle = _style;
        Canvas.ApplyToSelection(a => a.Style = a.Style.WithWidth(width));
        RefreshPanels();
    }

    private void SetOpacity(double opacity)
    {
        _style = _style.WithOpacity(opacity);
        Canvas.CurrentStyle = _style;
        Canvas.ApplyToSelection(a => a.Style = a.Style.WithOpacity(opacity));
        RefreshPanels();
    }

    private void SetFontSize(double size)
    {
        Canvas.ApplyToSelection(annotation =>
        {
            switch (annotation)
            {
                case TextAnnotation text:
                    text.FontSize = size;
                    break;
                case NumberBadgeAnnotation badge:
                    badge.FontSize = size;
                    break;
            }
        });

        var selected = Canvas.Selection.FirstOrDefault();
        if (selected is null && Canvas.Tool == EditorTool.Text)
        {
            _style = _style.WithWidth(Math.Max(1, size / 5));
        }

        RefreshPanels();
    }

    private void SetBadgeStyle(BadgeStyle style)
    {
        Canvas.ApplyToSelection(annotation =>
        {
            if (annotation is NumberBadgeAnnotation badge) badge.BadgeStyle = style;
        });
        RefreshPanels();
    }

    private void SyncFromSelection()
    {
        var selected = Canvas.Selection.FirstOrDefault();
        if (selected is not null) _style = selected.Style;
        Canvas.CurrentStyle = _style;
        RefreshPanels();
        UpdateStatus();
    }

    #endregion

    #region 文本编辑

    private void BeginNewText(TextAnnotation annotation)
    {
        annotation.Style = _style;
        _editingText = annotation;
        _editingNewText = true;
        _document.BeginEdit();
        _document.InsertWithoutHistory(annotation);
        Canvas.SelectAnnotation(annotation);
        ShowTextEditor(annotation);
    }

    private void ShowTextEditor(TextAnnotation annotation)
    {
        var origin = Canvas.ToScreen(annotation.Origin);
        TextEditorBox.Margin = new Thickness(origin.X + 2, origin.Y + 2, 0, 0);
        TextEditorBox.FontSize = Math.Max(12, annotation.FontSize * Canvas.Zoom);
        TextEditorBox.Width = Math.Max(180, Math.Min(560, annotation.EstimatedWidth * Canvas.Zoom + 24));
        TextEditorBox.MinHeight = Math.Max(38, annotation.EstimatedHeight * Canvas.Zoom);
        TextEditorBox.Text = annotation.Text;
        TextEditorBox.Visibility = Visibility.Visible;
        TextEditorBox.Focus();
        TextEditorBox.SelectAll();
    }

    private void CommitText(bool cancel = false)
    {
        if (_editingText is null) return;
        var annotation = _editingText;
        _editingText = null;
        string text = TextEditorBox.Text;
        TextEditorBox.Visibility = Visibility.Collapsed;

        if (cancel || (string.IsNullOrWhiteSpace(text) && _editingNewText))
        {
            _document.RemoveWithoutHistory(annotation);
            _document.EndEdit(false);
        }
        else
        {
            annotation.Text = text;
            _document.EndEdit();
        }

        _editingNewText = false;
        Canvas.Focus();
        UpdateStatus();
    }

    private void TextEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            CommitText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitText(true);
            e.Handled = true;
        }
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_editingText is not null) CommitText();
    }

    #endregion

    #region 命令

    private void SelectTool(EditorTool tool)
    {
        Canvas.Tool = tool;
        foreach (var pair in _toolButtons) pair.Value.IsChecked = pair.Key == tool;
        Canvas.Focus();
        UpdateStatus();
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse(tag, out EditorTool tool))
        {
            CommitText();
            SelectTool(tool);
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Canvas.ZoomToFit();

    private void Undo_Click(object sender, RoutedEventArgs e) => DoUndo();

    private void Redo_Click(object sender, RoutedEventArgs e) => DoRedo();

    private void DoUndo()
    {
        CommitText();
        _document.Undo();
        Canvas.Refresh();
        UpdateStatus();
    }

    private void DoRedo()
    {
        CommitText();
        _document.Redo();
        Canvas.Refresh();
        UpdateStatus();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard();

    private void Save_Click(object sender, RoutedEventArgs e) => SaveToDisk();

    private void Pin_Click(object sender, RoutedEventArgs e) => PinImage();

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyToClipboard()
    {
        CommitText();
        var flat = AnnotationRenderer.Render(_image, _document);
        if (ClipboardService.SetImage(flat))
        {
            Services.Toasts.Info("已复制到剪贴板", $"{_document.ImageWidth} × {_document.ImageHeight}");
        }
        else
        {
            Services.Toasts.Warn("复制失败", "剪贴板被其他程序占用，请稍后重试。");
        }
    }

    private void SaveToDisk()
    {
        try
        {
            CommitText();
            var flat = AnnotationRenderer.Render(_image, _document);
            string path = Services.SaveScreenshot(flat, _baseName, out _);
            Services.Toasts.Info("已保存", path);
        }
        catch (Exception ex)
        {
            Services.Toasts.Warn("保存失败", ex.Message);
        }
    }

    private void PinImage()
    {
        CommitText();
        var flat = AnnotationRenderer.Render(_image, _document);
        new PinWindow(flat, _baseName).Show();
    }

    private void ToggleFullscreen()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Renumber_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        _document.RenumberByReadingOrder();
        Canvas.Refresh();
        UpdateStatus();
    }

    private void ResetNumbering_Click(object sender, RoutedEventArgs e)
    {
        _document.ResetNumbering();
        UpdateStatus();
    }

    private void EditorWindow_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (TextEditorBox.IsKeyboardFocusWithin)
        {
            return;
        }

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift:
                case Key.Y:
                    DoRedo();
                    e.Handled = true;
                    return;
                case Key.Z:
                    DoUndo();
                    e.Handled = true;
                    return;
                case Key.C:
                    CopyToClipboard();
                    e.Handled = true;
                    return;
                case Key.S:
                    SaveToDisk();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Escape:
                CommitText();
                Close();
                e.Handled = true;
                break;
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.V:
                SelectTool(EditorTool.Select);
                e.Handled = true;
                break;
            case Key.R:
                SelectTool(EditorTool.Rectangle);
                e.Handled = true;
                break;
            case Key.E:
                SelectTool(EditorTool.Ellipse);
                e.Handled = true;
                break;
            case Key.A:
                SelectTool(EditorTool.Arrow);
                e.Handled = true;
                break;
            case Key.P:
                SelectTool(EditorTool.Pen);
                e.Handled = true;
                break;
            case Key.H:
                SelectTool(EditorTool.Highlight);
                e.Handled = true;
                break;
            case Key.T:
                SelectTool(EditorTool.Text);
                e.Handled = true;
                break;
            case Key.N:
                SelectTool(EditorTool.Number);
                e.Handled = true;
                break;
            case Key.OemPlus:
            case Key.Add:
                Canvas.SetZoom(Canvas.Zoom * 1.1);
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                Canvas.SetZoom(Canvas.Zoom / 1.1);
                e.Handled = true;
                break;
            case Key.D0 when ctrl:
                Canvas.ZoomToFit();
                e.Handled = true;
                break;
        }
    }

    private void SyncUndoButtons()
    {
        // 撤销/重做按钮状态由命令可用性体现，这里仅刷新状态栏。
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string toolName = Canvas.Tool switch
        {
            EditorTool.Select => "选择",
            EditorTool.Rectangle => "矩形",
            EditorTool.Ellipse => "椭圆",
            EditorTool.Arrow => "箭头",
            EditorTool.Pen => "画笔",
            EditorTool.Highlight => "高亮",
            EditorTool.Text => "文本",
            _ => "序号"
        };

        StatusLeft.Text = $"{_document.ImageWidth} × {_document.ImageHeight} · 缩放 {Math.Round(Canvas.Zoom * 100)}% · {toolName}";
        NumberHintText.Text = $"下一个序号：{_document.NextNumber}";
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CommitText();
        base.OnClosing(e);
    }

    #endregion
}
