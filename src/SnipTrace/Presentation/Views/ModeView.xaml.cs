using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Views;

public enum RecordingMode
{
    Gif,
    Video
}

public partial class ModeView : UserControl
{
    private int _selectedFps;
    private int _selectedEdge;

    public ModeView()
    {
        InitializeComponent();
    }

    public RecordingMode Mode { get; private set; } = RecordingMode.Gif;

    public event Action<RecordingMode>? StartRequested;

    public void Configure(RecordingMode mode, AppSettings settings)
    {
        Mode = mode;
        bool gif = mode == RecordingMode.Gif;

        TitleText.Text = gif ? L.T("GIF 录制") : L.T("屏幕录制");
        SubtitleText.Text = gif
            ? L.T("选区录制循环动图，中位切分量化与 LZW 流式编码，边编码边写盘。")
            : L.T("单显示器选区录制 H.264 MP4，暂停时间不计入成片，失败自动清理临时文件。");
        GifOnlyPanel.Visibility = gif ? Visibility.Visible : Visibility.Collapsed;

        _selectedFps = gif ? settings.Gif.Fps : settings.Video.Fps;
        _selectedEdge = settings.Gif.MaxEdge;
        MaxDurationBox.Text = settings.Gif.MaxDurationSeconds.ToString();
        MaxFramesBox.Text = settings.Gif.MaxFrames.ToString();
        DitherCheck.IsChecked = settings.Gif.HighQualityDither;
        CursorCheck.IsChecked = gif ? false : settings.Video.IncludeCursor;
        CursorCheck.Visibility = gif ? Visibility.Collapsed : Visibility.Visible;

        HintText.Text = gif
            ? L.T("GIF 帧延迟以 10 ms 为单位，界面显示近似帧率。")
            : L.T("H.264 要求偶数宽高，奇数尺寸会从右侧或底部裁去 1 像素。");

        RulesText.Text = gif
            ? "· 连续相同帧自动合并并累计延迟，减小体积\n· 时长与帧数上限均可调整，默认 120 秒 / 2400 帧\n· 录制边框位于画面外侧，不会进入成片\n· 停止后完成封装并提供预览、另存为与打开文件夹"
            : "· 仅录制画面，不含音频\n· 默认 30 FPS，时间戳基于有效录制时间\n· 录制中显示器分辨率或排列变化会自动停止并封装\n· 取消录制后临时文件立即删除";

        BuildFpsChips();
        BuildEdgeChips();
    }

    /// <summary>把界面参数写回设置对象，非法值给出提示。</summary>
    public bool TryApply(AppSettings settings, out string? error)
    {
        error = null;
        if (Mode == RecordingMode.Gif)
        {
            settings.Gif.Fps = _selectedFps;
            settings.Gif.MaxEdge = _selectedEdge;
            settings.Gif.HighQualityDither = DitherCheck.IsChecked == true;
            if (int.TryParse(MaxDurationBox.Text.Trim(), out int seconds)) settings.Gif.MaxDurationSeconds = seconds;
            if (int.TryParse(MaxFramesBox.Text.Trim(), out int frames)) settings.Gif.MaxFrames = frames;
            error = settings.Gif.Validate();
            settings.Gif.Normalize();
        }
        else
        {
            settings.Video.Fps = _selectedFps;
            settings.Video.IncludeCursor = CursorCheck.IsChecked == true;
            error = settings.Video.Validate();
            settings.Video.Normalize();
        }

        return error is null;
    }

    private void BuildFpsChips()
    {
        FpsPanel.Children.Clear();
        IReadOnlyList<int> values = Mode == RecordingMode.Gif ? GifOptions.AvailableFps : VideoOptions.AvailableFps;
        foreach (int fps in values)
        {
            var chip = new ToggleButton
            {
                Content = $"{fps} FPS",
                Style = (Style)FindResource("ChipToggle"),
                Margin = new Thickness(0, 0, 8, 0),
                Tag = fps,
                IsChecked = fps == _selectedFps
            };
            chip.Click += (sender, _) =>
            {
                _selectedFps = (int)((ToggleButton)sender).Tag;
                RefreshChips(FpsPanel);
            };
            FpsPanel.Children.Add(chip);
        }
    }

    private void BuildEdgeChips()
    {
        EdgePanel.Children.Clear();
        foreach (int edge in GifOptions.AvailableMaxEdges)
        {
            var chip = new ToggleButton
            {
                Content = edge == 0 ? L.T("原始") : $"{edge} px",
                Style = (Style)FindResource("ChipToggle"),
                Margin = new Thickness(0, 0, 8, 0),
                Tag = edge,
                IsChecked = edge == _selectedEdge
            };
            chip.Click += (sender, _) =>
            {
                _selectedEdge = (int)((ToggleButton)sender).Tag;
                RefreshChips(EdgePanel);
            };
            EdgePanel.Children.Add(chip);
        }
    }

    private void RefreshChips(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is ToggleButton chip && chip.Tag is int value)
            {
                bool gifEdge = ReferenceEquals(panel, EdgePanel);
                chip.IsChecked = gifEdge ? value == _selectedEdge : value == _selectedFps;
            }
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke(Mode);
}
