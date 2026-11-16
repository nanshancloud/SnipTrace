using SnipTrace.Domain;

namespace SnipTrace.App.Presentation.Selection;

/// <summary>
/// 选区工具条按钮的展示信息：设置页列表里的名字，以及分组（分组切换处自动插分隔线）。
/// </summary>
public static class SelectionToolbarCatalog
{
    /// <summary>设置页显示的按钮名称。</summary>
    public static string DisplayName(string id) => id switch
    {
        SelectionToolbarItems.Ratio => "比例",
        SelectionToolbarItems.Select => "选择",
        SelectionToolbarItems.Arrow => "箭头",
        SelectionToolbarItems.Pen => "画笔",
        SelectionToolbarItems.Eraser => "橡皮",
        SelectionToolbarItems.Rectangle => "矩形",
        SelectionToolbarItems.Ellipse => "椭圆",
        SelectionToolbarItems.Text => "文字",
        SelectionToolbarItems.Number => "序号",
        SelectionToolbarItems.Color => "颜色",
        SelectionToolbarItems.Copy => "复制",
        SelectionToolbarItems.Save => "保存",
        SelectionToolbarItems.Pin => "钉图",
        SelectionToolbarItems.Gif => "GIF 录制",
        SelectionToolbarItems.Video => "录屏",
        SelectionToolbarItems.Cancel => "取消",
        SelectionToolbarItems.Refine => "精标",
        _ => id
    };

    /// <summary>同组按钮之间不插分隔线。</summary>
    public static string GroupOf(string id) => id switch
    {
        SelectionToolbarItems.Ratio => "ratio",
        SelectionToolbarItems.Copy or SelectionToolbarItems.Save or SelectionToolbarItems.Pin => "action",
        SelectionToolbarItems.Gif or SelectionToolbarItems.Video => "record",
        SelectionToolbarItems.Cancel or SelectionToolbarItems.Refine => "decision",
        _ => "tool"
    };
}
