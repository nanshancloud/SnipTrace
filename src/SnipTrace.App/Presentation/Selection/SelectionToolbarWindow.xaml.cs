using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTrace.App.Infrastructure.Platform;
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
                IsChecked = ratio.IsFree
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

    private void BuildActions()
    {
        ActionRow.Children.Add(ActionButton("标注", true, SelectionAction.Edit));
        ActionRow.Children.Add(ActionButton("复制", false, SelectionAction.Copy));
        ActionRow.Children.Add(ActionButton("保存", false, SelectionAction.Save));
        ActionRow.Children.Add(ActionButton("钉图", false, SelectionAction.Pin));
        ActionRow.Children.Add(ActionButton("GIF", false, SelectionAction.Gif));
        ActionRow.Children.Add(ActionButton("录屏", false, SelectionAction.Video));
        ActionRow.Children.Add(ActionButton("取消", false, SelectionAction.Cancel));
    }

    private Button ActionButton(string text, bool primary, SelectionAction action)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"),
            MinHeight = 32,
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 4)
        };
        button.Click += (_, _) =>
        {
            if (action == SelectionAction.Cancel) _overlay?.Cancel();
            else _overlay?.RequestAction(action);
        };
        return button;
    }
}
