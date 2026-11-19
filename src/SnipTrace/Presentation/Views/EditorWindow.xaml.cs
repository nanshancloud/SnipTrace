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
    private ArrowHead _arrowHead = ArrowHead.End;
    private bool _arrowElbow;
    private BadgeShape _badgeShape = BadgeShape.Circle;
    private BadgeFillMode _badgeFill = BadgeFillMode.Fill;

    public EditorWindow(BitmapSource image, string baseName, AnnotationDocument? initialAnnotations = null,
        string? textFontFamily = null)
    {
        InitializeComponent();
        _image = image;
        _baseName = baseName;
        _document = new AnnotationDocument(image.PixelWidth, image.PixelHeight);
        if (!string.IsNullOrWhiteSpace(textFontFamily)) Canvas.TextFontFamily = textFontFamily.Trim();

        // 覆盖层上已画的标注带进编辑器继续精标。
        if (initialAnnotations is not null)
        {
            foreach (var annotation in initialAnnotations.Annotations)
            {
                _document.Add(annotation.Clone());
            }
        }

        Canvas.Image = image;
        Canvas.Document = _document;
        Canvas.CurrentStyle = _style;
        Canvas.BadgeShape = _badgeShape;
        Canvas.BadgeFillMode = _badgeFill;
        Canvas.ZoomChanged += (_, _) => UpdateStatus();
        Canvas.SelectionChanged += (_, _) => SyncFromSelection();
        Canvas.TextEditRequested += BeginTextEdit;

        _document.Changed += (_, _) =>
        {
            UpdateStatus();
            SyncHistoryButtons();
        };

        FileNameText.Text = baseName + ".png";
        InitializeTools();
        RenumberStyleCheck.IsChecked = AppServices.Current?.Settings.RenumberBadgesOnStyleChange == true;
        RefreshPanels();
        // 打开窗口时就按历史栈给出按钮状态：没有可撤销 / 可重做 / 可清除的标记时直接置灰，
        // 避免刚进编辑器点「重做」没有任何反应被当成按钮坏了。
        SyncHistoryButtons();
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
            (ClearAllButton, "清除全部", "\uE74D"),
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

            // 取色模式：色板末尾的「取色」块，打开系统取色对话框任选标注 / 文本颜色。
            bool customColor = !Palette.Contains(_style.Argb);
            var picker = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(2),
                BorderBrush = customColor ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "取色：打开系统取色对话框，任选标注 / 文本颜色",
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0xFF, 0x6B, 0x5F), 0),
                        new GradientStop(Color.FromRgb(0xFF, 0xCC, 0x00), 0.25),
                        new GradientStop(Color.FromRgb(0x59, 0xE0, 0xBE), 0.5),
                        new GradientStop(Color.FromRgb(0x4C, 0x8D, 0xFF), 0.75),
                        new GradientStop(Color.FromRgb(0xF2, 0xF6, 0xFC), 1)
                    },
                    new Point(0, 0),
                    new Point(1, 1))
            };
            picker.MouseLeftButtonUp += (_, _) =>
            {
                if (ColorPicker.TryPick(_style.Argb, out uint picked)) SetColor(picked);
            };
            ColorPanel.Children.Add(picker);

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

            var selectedArrow = Canvas.Selection.OfType<ShapeAnnotation>().FirstOrDefault(s => s.Kind == AnnotationKind.Arrow);
            var arrowHead = selectedArrow?.ArrowHead ?? _arrowHead;
            bool arrowElbow = selectedArrow?.Elbow ?? _arrowElbow;
            ArrowModePanel.Children.Clear();
            foreach (var (head, elbow, label, hint) in new[]
            {
                (ArrowHead.End, false, "单向", "末端一个箭头"),
                (ArrowHead.Both, false, "双向", "两端都是箭头"),
                (ArrowHead.None, false, "无头", "只画直线，不带箭头"),
                (ArrowHead.End, true, "直角", "每段横平竖直的折线箭头")
            })
            {
                var capturedHead = head;
                bool capturedElbow = elbow;
                var chip = Chip(label, arrowHead == head && arrowElbow == elbow, () => SetArrowMode(capturedHead, capturedElbow));
                chip.ToolTip = hint;
                ArrowModePanel.Children.Add(chip);
            }

            BadgePanel.Children.Clear();
            var selectedBadge = Canvas.Selection.OfType<NumberBadgeAnnotation>().FirstOrDefault();
            var badgeShape = selectedBadge?.Shape ?? _badgeShape;
            var badgeFill = selectedBadge?.FillMode ?? _badgeFill;
            foreach (var (shape, label) in new[]
            {
                (BadgeShape.Circle, "圆形"), (BadgeShape.Triangle, "三角"), (BadgeShape.Square, "方形"),
                (BadgeShape.Pentagon, "五边"), (BadgeShape.Hexagon, "六边"), (BadgeShape.Heart, "心形"),
                (BadgeShape.RoundedRect, "圆角")
            })
            {
                var capturedShape = shape;
                BadgePanel.Children.Add(Chip(label, badgeShape == shape, () => SetBadgeShape(capturedShape)));
            }

            BadgeFillPanel.Children.Clear();
            BadgeFillPanel.Children.Add(Chip("填充", badgeFill == BadgeFillMode.Fill, () => SetBadgeFillMode(BadgeFillMode.Fill)));
            BadgeFillPanel.Children.Add(Chip("边框", badgeFill == BadgeFillMode.Outline, () => SetBadgeFillMode(BadgeFillMode.Outline)));
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

    /// <summary>
    /// 箭头模式：单向 / 双向 / 无头 / 直角折线；已有选中的箭头一并改。
    /// 右侧选完顺手把左侧切到「箭头」，接着就能直接画。
    /// </summary>
    private void SetArrowMode(ArrowHead head, bool elbow)
    {
        _arrowHead = head;
        _arrowElbow = elbow;
        Canvas.ArrowHead = head;
        Canvas.ArrowElbow = elbow;
        Canvas.ApplyToSelection(annotation =>
        {
            if (annotation is ShapeAnnotation { Kind: AnnotationKind.Arrow } arrow)
            {
                arrow.ArrowHead = head;
                arrow.Elbow = elbow;
            }
        });

        if (Canvas.Tool != EditorTool.Arrow) SelectTool(EditorTool.Arrow);
        RefreshPanels();
    }

    /// <summary>序号形状：选中的号标即时改，同时记住给下一个号标；左侧自动切到「序号」。</summary>
    private void SetBadgeShape(BadgeShape shape)
    {
        _badgeShape = shape;
        Canvas.BadgeShape = shape;
        ApplyBadgeStyle(badge => badge.Shape = shape);
        if (Canvas.Tool != EditorTool.Number) SelectTool(EditorTool.Number);
        RefreshPanels();
    }

    /// <summary>填充方式：同上，改选中号标并作为新建号标的默认值。</summary>
    private void SetBadgeFillMode(BadgeFillMode mode)
    {
        _badgeFill = mode;
        Canvas.BadgeFillMode = mode;
        ApplyBadgeStyle(badge => badge.FillMode = mode);
        if (Canvas.Tool != EditorTool.Number) SelectTool(EditorTool.Number);
        RefreshPanels();
    }

    /// <summary>
    /// 改形状 / 填充：一次编辑会话内改完；「改样式后重新计数」打开时同一步里按阅读顺序重排 1..n，
    /// 撤销一次即可回到改样式之前。
    /// </summary>
    private void ApplyBadgeStyle(Action<NumberBadgeAnnotation> apply)
    {
        bool renumber = AppServices.Current?.Settings.RenumberBadgesOnStyleChange == true;
        bool changed = AnnotationEditing.ApplyBadgeStyle(
            _document, Canvas.Selection.OfType<NumberBadgeAnnotation>(), apply, renumber);
        if (!changed) return;

        Canvas.Refresh();
        UpdateStatus();
        RefreshPanels();
    }

    private void RenumberStyle_Click(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;
        if (services is null) return;
        services.Settings.RenumberBadgesOnStyleChange = RenumberStyleCheck.IsChecked == true;
        services.SaveSettings();
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

    /// <summary>新建文本，或双击已有文本进入内容修改；两者都走一次编辑会话以便撤销。</summary>
    private void BeginTextEdit(TextAnnotation annotation, bool isNew)
    {
        if (isNew) annotation.Style = _style;
        _document.BeginEdit();
        if (isNew) _document.InsertWithoutHistory(annotation);

        _editingText = annotation;
        _editingNewText = isNew;
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
            if (_editingNewText) _document.RemoveWithoutHistory(annotation);
            _document.EndEdit(false);
        }
        else
        {
            bool changed = annotation.Text != text;
            annotation.Text = text;
            if (changed) _document.MarkChanged();
            _document.EndEdit(changed);
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
        // 切换工具时结束未完成的多段箭头，避免草稿悬空。
        Canvas.FinishArrowDraft();
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
        if (!_document.CanUndo) return;

        _document.Undo();
        AfterHistoryChanged();
    }

    private void DoRedo()
    {
        CommitText();
        if (!_document.CanRedo) return;

        _document.Redo();
        AfterHistoryChanged();
    }

    /// <summary>撤销 / 重做之后统一收尾：丢掉已不在文档里的选中项、重画面板与按钮状态。</summary>
    private void AfterHistoryChanged()
    {
        Canvas.SyncAfterHistory();
        Canvas.Refresh();
        RefreshPanels();
        UpdateStatus();
        SyncHistoryButtons();
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

        if (e.Handled || TextEditorBox.IsKeyboardFocusWithin)
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
                if (Canvas.HasArrowDraft)
                {
                    Canvas.FinishArrowDraft(false);
                    e.Handled = true;
                    break;
                }

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
            case Key.X:
                SelectTool(EditorTool.Eraser);
                e.Handled = true;
                break;
            case Key.F2 when Canvas.Selection.Count == 1 && Canvas.Selection[0] is TextAnnotation selected:
                BeginTextEdit(selected, false);
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

    /// <summary>
    /// 撤销 / 重做 / 清除全部按钮跟着历史栈与标注数量走：没得操作就没得点，
    /// 避免「点了没反应」被当成坏了。
    /// </summary>
    private void SyncHistoryButtons()
    {
        bool hasAnnotations = _document.Annotations.Count > 0;
        UndoButton.IsEnabled = _document.CanUndo;
        RedoButton.IsEnabled = _document.CanRedo;
        ClearAllButton.IsEnabled = hasAnnotations;
        UndoButton.ToolTip = _document.CanUndo ? "撤销（Ctrl+Z）" : "没有可撤销的操作";
        RedoButton.ToolTip = _document.CanRedo ? "重做（Ctrl+Shift+Z / Ctrl+Y）" : "没有可重做的操作";
        ClearAllButton.ToolTip = hasAnnotations ? "清除全部标记（可撤销）" : "没有可清除的标记";
        UpdateStatus();
    }

    /// <summary>清除全部标记：一次编辑会话，可被撤销回来。</summary>
    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        Canvas.FinishArrowDraft(false);
        if (_document.Annotations.Count == 0) return;

        _document.Clear();
        AfterHistoryChanged();
        AppServices.Current?.Toasts.Info("已清除全部标记", "可撤销（Ctrl+Z）");
    }

    private void UpdateStatus()
    {
        string toolName = Canvas.Tool switch
        {
            EditorTool.Select => "选择",
            EditorTool.Rectangle => "矩形",
            EditorTool.Ellipse => "椭圆",
            EditorTool.Arrow => _arrowElbow ? "箭头（直角）" : "箭头",
            EditorTool.Pen => "画笔",
            EditorTool.Highlight => "高亮",
            EditorTool.Text => "文本",
            EditorTool.Eraser => "橡皮",
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
