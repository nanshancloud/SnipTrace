namespace SnipTrace.Domain;

/// <summary>主窗口壳层的横向布局档位，见设计稿 4.2。</summary>
public enum ShellLayoutMode
{
    /// <summary>≥ 1280：左侧宽导航 + 三列模式卡 + 最近记录侧栏。</summary>
    Expanded,

    /// <summary>1040–1279：左侧宽导航 + 三列模式卡 + 最近记录下移。</summary>
    Standard,

    /// <summary>860–1039：窄导航栏，模式卡改为纵向列表。</summary>
    Compact,

    /// <summary>&lt; 860：顶部标签导航，单列内容。</summary>
    Minimal
}

/// <summary>主窗口内容区的高度档位，见设计稿 4.2「高度自适应」。</summary>
public enum ShellHeightTier
{
    /// <summary>≥ 760：完整标题、副标题与最近记录。</summary>
    Full,

    /// <summary>600–759：压缩标题区。</summary>
    Compact,

    /// <summary>&lt; 600：隐藏副标题，最近记录折叠。</summary>
    Minimal
}

/// <summary>一次布局计算的结果。</summary>
public readonly record struct ShellLayoutMetrics(
    ShellLayoutMode Mode,
    double NavWidth,
    double ContentWidth,
    ShellHeightTier HeightTier);

/// <summary>
/// 主窗口响应式断点。断点以窗口尺寸为基准（设计稿 4.1 的默认窗口 1100×720
/// 对应「标准」档与三列模式卡），内容区尺寸由导航栏宽度推导，供页面内部布局使用。
/// </summary>
public static class ShellLayout
{
    public const double ExpandedThreshold = 1280;
    public const double StandardThreshold = 1040;
    public const double CompactThreshold = 860;

    public const double NavWideWidth = 220;
    public const double NavNarrowWidth = 64;

    public const double FullHeightThreshold = 760;
    public const double CompactHeightThreshold = 600;

    /// <summary>标题栏 44 + 状态栏 30，用于把窗口高度换算成内容区高度。</summary>
    public const double ChromeHeight = 74;

    public static ShellLayoutMode ResolveMode(double windowWidth)
    {
        if (windowWidth >= ExpandedThreshold) return ShellLayoutMode.Expanded;
        if (windowWidth >= StandardThreshold) return ShellLayoutMode.Standard;
        if (windowWidth >= CompactThreshold) return ShellLayoutMode.Compact;
        return ShellLayoutMode.Minimal;
    }

    public static double ResolveNavWidth(ShellLayoutMode mode) => mode switch
    {
        ShellLayoutMode.Expanded or ShellLayoutMode.Standard => NavWideWidth,
        ShellLayoutMode.Compact => NavNarrowWidth,
        _ => 0
    };

    public static ShellHeightTier ResolveHeightTier(double contentHeight)
    {
        if (contentHeight >= FullHeightThreshold) return ShellHeightTier.Full;
        if (contentHeight >= CompactHeightThreshold) return ShellHeightTier.Compact;
        return ShellHeightTier.Minimal;
    }

    public static ShellLayoutMetrics Resolve(double windowWidth, double windowHeight)
    {
        ShellLayoutMode mode = ResolveMode(windowWidth);
        double navWidth = ResolveNavWidth(mode);
        return new ShellLayoutMetrics(
            mode,
            navWidth,
            Math.Max(0, windowWidth - navWidth),
            ResolveHeightTier(windowHeight - ChromeHeight));
    }

    /// <summary>模式卡是否改为纵向单列。</summary>
    public static bool UsesSingleColumn(ShellLayoutMode mode) =>
        mode is ShellLayoutMode.Compact or ShellLayoutMode.Minimal;

    /// <summary>最近记录是否放到右侧栏。</summary>
    public static bool UsesRecentSidebar(ShellLayoutMode mode, ShellHeightTier tier) =>
        mode == ShellLayoutMode.Expanded && tier != ShellHeightTier.Minimal;
}
