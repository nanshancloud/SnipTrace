namespace SnipTrace.Domain;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

/// <summary>快捷键手势，保存为可读字符串（例如 Ctrl+Shift+G）。</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey, string KeyName)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Space"] = 0x20,
        ["Tab"] = 0x09,
        ["Backspace"] = 0x08,
        ["Delete"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Up"] = 0x26,
        ["Down"] = 0x28,
        ["Left"] = 0x25,
        ["Right"] = 0x27,
        ["PrintScreen"] = 0x2C,
        ["Pause"] = 0x13,
        ["Plus"] = 0xBB,
        ["Minus"] = 0xBD,
        ["Comma"] = 0xBC,
        ["Period"] = 0xBE,
        ["Slash"] = 0xBF,
        ["Semicolon"] = 0xBA,
        ["Quote"] = 0xDE,
        ["Backslash"] = 0xDC,
        ["LeftBracket"] = 0xDB,
        ["RightBracket"] = 0xDD,
        ["Tilde"] = 0xC0,
        ["NumPad0"] = 0x60,
        ["NumPad1"] = 0x61,
        ["NumPad2"] = 0x62,
        ["NumPad3"] = 0x63,
        ["NumPad4"] = 0x64,
        ["NumPad5"] = 0x65,
        ["NumPad6"] = 0x66,
        ["NumPad7"] = 0x67,
        ["NumPad8"] = 0x68,
        ["NumPad9"] = 0x69
    };

    public string ToDisplayString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    public override string ToString() => ToDisplayString();

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        string? keyToken = null;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "win":
                case "windows":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    keyToken = token;
                    break;
            }
        }

        if (keyToken is null) return false;
        if (!TryResolveVirtualKey(keyToken, out int virtualKey, out string canonical)) return false;

        gesture = new HotkeyGesture(modifiers, virtualKey, canonical);
        return true;
    }

    public static HotkeyGesture ParseOrDefault(string? text, string fallback)
    {
        if (TryParse(text, out var gesture)) return gesture;
        return TryParse(fallback, out var parsed) ? parsed : new HotkeyGesture(HotkeyModifiers.None, 0x70, "F1");
    }

    private static bool TryResolveVirtualKey(string token, out int virtualKey, out string canonical)
    {
        virtualKey = 0;
        canonical = token;

        if (token.Length == 1)
        {
            char ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                virtualKey = ch;
                canonical = ch.ToString();
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                virtualKey = ch;
                canonical = ch.ToString();
                return true;
            }
        }

        if (token.Length is >= 2 and <= 3 && (token[0] is 'F' or 'f') && int.TryParse(token.AsSpan(1), out int fNumber) && fNumber is >= 1 and <= 24)
        {
            virtualKey = 0x70 + fNumber - 1;
            canonical = $"F{fNumber}";
            return true;
        }

        foreach (var pair in NamedKeys)
        {
            if (string.Equals(pair.Key, token, StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = pair.Value;
                canonical = pair.Key;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 虚拟键码 → 规范按键名（<see cref="TryResolveVirtualKey"/> 的逆向），
    /// 供设置页「按下按键即录入」使用；不认识的键返回 false。
    /// </summary>
    public static bool TryGetKeyName(int virtualKey, out string keyName)
    {
        keyName = string.Empty;

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            keyName = ((char)virtualKey).ToString();
            return true;
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            keyName = ((char)virtualKey).ToString();
            return true;
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            keyName = $"F{virtualKey - 0x70 + 1}";
            return true;
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            keyName = "NumPad" + (virtualKey - 0x60);
            return true;
        }

        // 别名（Return / Escape）跳过，只回最短的规范名。
        foreach (var pair in NamedKeys)
        {
            if (pair.Value != virtualKey) continue;
            if (pair.Key is "Return" or "Escape") continue;
            keyName = pair.Key;
            return true;
        }

        return false;
    }
}

public sealed class WindowLayout
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 720;
    public bool IsMaximized { get; set; }

    public void Normalize()
    {
        if (!double.IsFinite(Left)) Left = 100;
        if (!double.IsFinite(Top)) Top = 100;
        if (!double.IsFinite(Width)) Width = 1100;
        if (!double.IsFinite(Height)) Height = 720;
        Width = Math.Clamp(Width, 860, 6000);
        Height = Math.Clamp(Height, 560, 4000);
    }
}

/// <summary>快捷键模式：三种动作各自独立，或统一由一个快捷键唤起、在工具条里选择动作。</summary>
public static class HotkeyModes
{
    public const string Individual = "Individual";
    public const string Unified = "Unified";

    public static string Normalize(string? value) => Unified.Equals(value, StringComparison.OrdinalIgnoreCase)
        ? Unified
        : Individual;
}

/// <summary>界面主题：深色 / 浅色。</summary>
public static class ThemeModes
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static string Normalize(string? value) => Light.Equals(value, StringComparison.OrdinalIgnoreCase)
        ? Light
        : Dark;
}

/// <summary>托盘图标配色：自动跟随任务栏明暗，或固定品牌彩色 / 浅色。</summary>
public static class TrayIconModes
{
    public const string Auto = "Auto";
    public const string Color = "Color";
    public const string Light = "Light";

    public static string Normalize(string? value)
    {
        if (Color.Equals(value, StringComparison.OrdinalIgnoreCase)) return Color;
        if (Light.Equals(value, StringComparison.OrdinalIgnoreCase)) return Light;
        return Auto;
    }
}

/// <summary>录制 / GIF 中的鼠标高亮参数（QQ 演示模式）。</summary>
public sealed class HighlightOptions
{
    public bool Enabled { get; set; }

    /// <summary>高亮圈颜色，#RRGGBB 或 #AARRGGBB。</summary>
    public string Color { get; set; } = "#FFCC00";

    /// <summary>高亮圈直径（物理像素）。</summary>
    public int Diameter { get; set; } = 48;

    /// <summary>填充不透明度（0–100）。</summary>
    public int OpacityPercent { get; set; } = 35;

    public void Normalize()
    {
        Color = NormalizeColor(Color);
        Diameter = Math.Clamp(Diameter, 16, 200);
        OpacityPercent = Math.Clamp(OpacityPercent, 10, 80);
    }

    public string? Validate()
    {
        if (Diameter is < 16 or > 200) return "鼠标高亮直径必须在 16–200 之间。";
        if (OpacityPercent is < 10 or > 80) return "鼠标高亮不透明度必须在 10–80 之间。";
        return null;
    }

    private static string NormalizeColor(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length is 6 or 8 && text.All(char.IsAsciiHexDigit)) return "#" + text.ToUpperInvariant();
        return "#FFCC00";
    }
}

/// <summary>选区工具条上的一个按钮：Id 见 <see cref="SelectionToolbarItems"/>，Visible=false 即隐藏。</summary>
public sealed class ToolbarItemSetting
{
    public string Id { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;
}

/// <summary>选区工具条按钮 Id 与默认排列（列表顺序就是显示顺序，可增删）。</summary>
public static class SelectionToolbarItems
{
    public const string Ratio = "ratio";
    public const string Select = "select";
    public const string Arrow = "arrow";
    public const string Pen = "pen";
    public const string Eraser = "eraser";
    public const string Rectangle = "rectangle";
    public const string Ellipse = "ellipse";
    public const string Text = "text";
    public const string Number = "number";
    public const string Color = "color";
    public const string Copy = "copy";
    public const string Save = "save";
    public const string Pin = "pin";
    public const string Gif = "gif";
    public const string Video = "video";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string Cancel = "cancel";
    public const string Refine = "refine";

    /// <summary>全部按钮，数组顺序即默认顺序（「复制」默认在最右侧）。</summary>
    public static readonly string[] All =
    {
        Ratio, Select, Arrow, Pen, Eraser, Rectangle, Ellipse, Text, Number, Color,
        Save, Pin, Gif, Video, Undo, Redo, Cancel, Refine, Copy
    };

    public static bool IsKnown(string? id) => id is not null && Array.IndexOf(All, id) >= 0;

    public static List<ToolbarItemSetting> CreateDefault()
        => All.Select(id => new ToolbarItemSetting { Id = id, Visible = true }).ToList();

    public static List<ToolbarItemSetting> Clone(List<ToolbarItemSetting>? items)
        => items is null
            ? CreateDefault()
            : items.Select(item => new ToolbarItemSetting { Id = item.Id, Visible = item.Visible }).ToList();

    /// <summary>去掉未知 / 重复项并补齐缺失按钮，避免手改配置把工具条弄坏。</summary>
    public static List<ToolbarItemSetting> Normalize(List<ToolbarItemSetting>? items)
    {
        var result = new List<ToolbarItemSetting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (items is not null)
        {
            foreach (var item in items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Id)) continue;
                string id = item.Id.Trim();
                if (!IsKnown(id) || !seen.Add(id)) continue;
                result.Add(new ToolbarItemSetting { Id = id, Visible = item.Visible });
            }
        }

        // 缺失的按钮（含版本升级后新增的）按默认相对位置插回去，而不是一律追加到末尾。
        foreach (string id in All)
        {
            if (seen.Contains(id)) continue;
            int defaultIndex = Array.IndexOf(All, id);
            int insertAt = result.Count;
            for (int i = 0; i < result.Count; i++)
            {
                if (Array.IndexOf(All, result[i].Id) > defaultIndex)
                {
                    insertAt = i;
                    break;
                }
            }

            result.Insert(insertAt, new ToolbarItemSetting { Id = id, Visible = true });
            seen.Add(id);
        }

        return result;
    }
}

public sealed class AppSettings
{
    public const string DefaultCaptureHotkey = "F1";
    public const string DefaultGifHotkey = "Ctrl+Shift+G";
    public const string DefaultVideoHotkey = "Ctrl+Shift+R";
    public const string DefaultFullScreenHotkey = "Ctrl+Shift+F";
    public const string DefaultAllScreensHotkey = "Ctrl+Alt+F";

    public const string DefaultTextFontFamily = "Microsoft YaHei UI";

    public string ScreenshotDirectory { get; set; } = string.Empty;

    public string VideoDirectory { get; set; } = string.Empty;

    public bool IncludeCursorInCapture { get; set; }

    public bool CopyAfterCapture { get; set; } = true;

    /// <summary>截图完成后自动把 PNG 存到截图目录（保存按钮的对话框除外）。</summary>
    public bool AutoSaveAfterCapture { get; set; }

    /// <summary>快捷键注册失败时是否弹提示。</summary>
    public bool WarnHotkeyFailure { get; set; } = true;

    /// <summary>HotkeyModes.Individual / Unified。</summary>
    public string HotkeyMode { get; set; } = HotkeyModes.Individual;

    /// <summary>ThemeModes.Dark / Light；新装默认浅色（白色外观）。</summary>
    public string Theme { get; set; } = ThemeModes.Light;

    /// <summary>文本标注（文本工具）使用的字体，空值回退默认。</summary>
    public string TextFontFamily { get; set; } = DefaultTextFontFamily;

    /// <summary>序号标记改样式（形状 / 填充）后是否按阅读顺序重新计数。</summary>
    public bool RenumberBadgesOnStyleChange { get; set; }

    /// <summary>选区工具条按钮的顺序与显示状态。</summary>
    public List<ToolbarItemSetting> SelectionToolbar { get; set; } = SelectionToolbarItems.CreateDefault();

    public HighlightOptions Highlight { get; set; } = new();

    public bool RunAtStartup { get; set; }

    /// <summary>托盘图标配色，见 <see cref="TrayIconModes"/>。</summary>
    public string TrayIconMode { get; set; } = TrayIconModes.Auto;

    /// <summary>「已最小化到托盘」提示是否已被永久关闭。</summary>
    public bool SuppressTrayHint { get; set; }

    /// <summary>启动时静默检查 GitHub 新版本；关掉后启动不联网，关于页仍可手动检查。</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    public string CaptureHotkey { get; set; } = DefaultCaptureHotkey;

    public string GifHotkey { get; set; } = DefaultGifHotkey;

    public string VideoHotkey { get; set; } = DefaultVideoHotkey;

    public string FullScreenHotkey { get; set; } = DefaultFullScreenHotkey;

    public string AllScreensHotkey { get; set; } = DefaultAllScreensHotkey;

    public GifOptions Gif { get; set; } = new();

    public VideoOptions Video { get; set; } = new();

    public WindowLayout? MainWindow { get; set; }

    public static AppSettings CreateDefault() => new();

    /// <summary>把非法值收敛到安全默认值，保证界面与录制参数始终可用。</summary>
    public void Normalize(string defaultScreenshotDirectory, string defaultVideoDirectory)
    {
        ScreenshotDirectory = NormalizeDirectory(ScreenshotDirectory, defaultScreenshotDirectory);
        VideoDirectory = NormalizeDirectory(VideoDirectory, defaultVideoDirectory);
        Gif ??= new GifOptions();
        Video ??= new VideoOptions();
        Gif.Normalize();
        Video.Normalize();
        SelectionToolbar = SelectionToolbarItems.Normalize(SelectionToolbar);
        Highlight ??= new HighlightOptions();
        Highlight.Normalize();
        Theme = ThemeModes.Normalize(Theme);
        HotkeyMode = HotkeyModes.Normalize(HotkeyMode);
        TrayIconMode = TrayIconModes.Normalize(TrayIconMode);
        TextFontFamily = string.IsNullOrWhiteSpace(TextFontFamily) ? DefaultTextFontFamily : TextFontFamily.Trim();

        CaptureHotkey = NormalizeHotkey(CaptureHotkey, DefaultCaptureHotkey);
        GifHotkey = NormalizeHotkey(GifHotkey, DefaultGifHotkey);
        VideoHotkey = NormalizeHotkey(VideoHotkey, DefaultVideoHotkey);
        FullScreenHotkey = NormalizeHotkey(FullScreenHotkey, DefaultFullScreenHotkey);
        AllScreensHotkey = NormalizeHotkey(AllScreensHotkey, DefaultAllScreensHotkey);

        MainWindow?.Normalize();
    }

    /// <summary>返回快捷键重复项的可读提示，无冲突时为 null。统一快捷键模式下不参与唤起的 GIF / 录屏键不检查。</summary>
    public string? FindHotkeyConflict()
    {
        bool unified = HotkeyMode == HotkeyModes.Unified;
        var gestures = new (string Name, string Text)[]
        {
            ("区域截图", CaptureHotkey),
            ("GIF 录制", unified ? string.Empty : GifHotkey),
            ("屏幕录制", unified ? string.Empty : VideoHotkey),
            ("全屏截图", FullScreenHotkey),
            ("所有显示器截图", AllScreensHotkey)
        };

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, text) in gestures)
        {
            if (!HotkeyGesture.TryParse(text, out var gesture)) continue;
            string key = gesture.ToDisplayString();
            if (seen.TryGetValue(key, out var other)) return $"{name} 与 {other} 都使用 {key}";
            seen[key] = name;
        }

        return null;
    }

    private static string NormalizeDirectory(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            string full = Path.GetFullPath(value.Trim());
            return string.IsNullOrWhiteSpace(full) ? fallback : full;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static string NormalizeHotkey(string? value, string fallback)
    {
        if (HotkeyGesture.TryParse(value, out var gesture)) return gesture.ToDisplayString();
        return HotkeyGesture.ParseOrDefault(fallback, fallback).ToDisplayString();
    }
}
