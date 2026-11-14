using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Platform;
using SnipTrace.App.Presentation.Views;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

public partial class SelectionToolbarWindow : Window
{
    private readonly List<(ToggleButton Button, AspectRatio Ratio)> _ratioChips = new();
    private SelectionOverlay? _overlay;

    public SelectionToolbarWindow()
    {
        InitializeComponent();
    }

    public void Attach(SelectionOverlay overlay)
    {
        _overlay = overlay;
        BuildRatioChips();
        BuildActions();
        SourceInitialized += (_, _) => WindowPlacement.ApplyNoActivate(this);
    }

    public void UpdateInfo(PixelRect selection, PixelPoint cursor, string ratioLabel, bool hasGap)
    {
        if (selection.IsEmpty)
        {
            SizeText.Text = "拖动选择区域";
            CoordText.Text = $"光标 {cursor.X}, {cursor.Y}";
            ActionRow.Visibility = Visibility.Collapsed;
            MagnifierImage.Visibility = Visibility.Collapsed;
            GapText.Visibility = Visibility.Collapsed;
            return;
        }

        SizeText.Text = $"{selection.Width} × {selection.Height} · {ratioLabel}";
        CoordText.Text = $"坐标 {selection.Left}, {selection.Top} · 光标 {cursor.X}, {cursor.Y}";
        ActionRow.Visibility = Visibility.Visible;
        MagnifierImage.Visibility = Visibility.Visible;
        GapText.Visibility = hasGap ? Visibility.Visible : Visibility.Collapsed;
        if (hasGap) GapText.Text = "包含未覆盖区域，导出时以黑色填充";
    }

    public void UpdateMagnifier(BitmapSource? image, Color color)
    {
        MagnifierImage.Source = image;
        ColorText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public void RefreshRatioChips(AspectRatio current)
    {
        foreach (var (button, ratio) in _ratioChips)
        {
            button.IsChecked = ratio.Label == current.Label;
        }
    }

    private void BuildRatioChips()
    {
        foreach (var ratio in AspectRatio.Presets)
        {
            var captured = ratio;
            var chip = new ToggleButton
            {
                Content = ratio.Label,
                Style = (Style)FindResource("ChipToggle"),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11,
                Padding = new Thickness(9, 3, 9, 3),
                IsChecked = ratio == AspectRatio.Free
            };
            chip.Click += (_, _) =>
            {
                _overlay?.SetRatio(captured);
                RefreshRatioChips(captured);
            };
            _ratioChips.Add((chip, ratio));
            RatioRow.Children.Add(chip);
        }
    }

    /// <summary>
    /// 图标式操作条：输出动作、录制动作、取消/完成分组，中间用细分隔线断开。
    /// 只保留图标，功能靠 ToolTip 说明（与参考设计一致）。
    /// </summary>
    private void BuildActions()
    {
        AddAction("\uE8C8", "复制到剪贴板（Ctrl+C）", SelectionAction.Copy);
        AddAction("\uE74E", "保存为 PNG（Ctrl+S）", SelectionAction.Save);
        AddAction("\uE840", "钉图置顶", SelectionAction.Pin);
        AddSeparator();
        AddAction("\uE91B", "录制 GIF（Ctrl+Shift+G）", SelectionAction.Gif);
        AddAction("\uE714", "录制 MP4（Ctrl+Shift+R）", SelectionAction.Video);
        AddSeparator();
        AddAction("\uE711", "取消（Esc）", SelectionAction.Cancel);
        AddAction("\uE73E", "完成并标注（Enter）", SelectionAction.Edit, primary: true);
    }

    private void AddAction(string glyph, string tooltip, SelectionAction action, bool primary = false)
    {
        var button = new Button
        {
            Content = IconContent.Create(glyph),
            Style = (Style)FindResource(primary ? "ToolIconPrimaryButton" : "ToolIconButton"),
            ToolTip = tooltip
        };
        button.Click += (_, _) =>
        {
            if (action == SelectionAction.Cancel) _overlay?.Cancel();
            else _overlay?.RequestAction(action);
        };
        ActionRow.Children.Add(button);
    }

    private void AddSeparator()
    {
        ActionRow.Children.Add(new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(6, 0, 6, 0),
            Background = (Brush)FindResource("BorderBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
    }
}
