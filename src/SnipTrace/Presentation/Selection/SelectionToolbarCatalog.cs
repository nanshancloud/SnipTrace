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
        SelectionToolbarItems.Ratio => L.T("比例"),
        SelectionToolbarItems.Shape => L.T("图形"),
        SelectionToolbarItems.Arrow => L.T("箭头"),
        SelectionToolbarItems.Pen => L.T("画笔"),
        SelectionToolbarItems.Text => L.T("文字"),
        SelectionToolbarItems.Number => L.T("序号"),
        SelectionToolbarItems.Mosaic => L.T("马赛克"),
        SelectionToolbarItems.Color => L.T("颜色"),
        SelectionToolbarItems.Copy => L.T("复制"),
        SelectionToolbarItems.Save => L.T("保存"),
        SelectionToolbarItems.Pin => L.T("钉图"),
        SelectionToolbarItems.Gif => L.T("GIF 录制"),
        SelectionToolbarItems.Video => L.T("录屏"),
        SelectionToolbarItems.Undo => L.T("回退"),
        SelectionToolbarItems.Redo => L.T("前进"),
        SelectionToolbarItems.Cancel => L.T("取消"),
        SelectionToolbarItems.Refine => L.T("精标"),
        _ => id
    };

    /// <summary>子选项显示名（图形 / 箭头按钮的弹出层芯片）。</summary>
    public static string VariantName(string id, string variant) => (id, variant) switch
    {
        (SelectionToolbarItems.Shape, SelectionToolbarItems.VariantEllipse) => L.T("椭圆"),
        (SelectionToolbarItems.Shape, SelectionToolbarItems.VariantLine) => L.T("直线"),
        (SelectionToolbarItems.Shape, _) => L.T("矩形"),
        (SelectionToolbarItems.Arrow, SelectionToolbarItems.VariantLine) => L.T("直线"),
        _ => L.T("箭头")
    };

    /// <summary>同组按钮之间不插分隔线。</summary>
    public static string GroupOf(string id) => id switch
    {
        SelectionToolbarItems.Ratio => "ratio",
        SelectionToolbarItems.Copy or SelectionToolbarItems.Save or SelectionToolbarItems.Pin => "action",
        SelectionToolbarItems.Gif or SelectionToolbarItems.Video => "record",
        SelectionToolbarItems.Undo or SelectionToolbarItems.Redo => "history",
        SelectionToolbarItems.Cancel or SelectionToolbarItems.Refine => "decision",
        _ => "tool"
    };
}
