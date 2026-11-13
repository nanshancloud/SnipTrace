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
        ["Tilde"] = 0xC0
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

public sealed class AppSettings
{
    public const string DefaultCaptureHotkey = "F1";
    public const string DefaultGifHotkey = "Ctrl+Shift+G";
    public const string DefaultVideoHotkey = "Ctrl+Shift+R";
    public const string DefaultFullScreenHotkey = "Ctrl+Shift+F";
    public const string DefaultAllScreensHotkey = "Ctrl+Alt+F";

    public string ScreenshotDirectory { get; set; } = string.Empty;

    public string VideoDirectory { get; set; } = string.Empty;

    public bool IncludeCursorInCapture { get; set; }

    public bool CopyAfterCapture { get; set; } = true;

    public bool RunAtStartup { get; set; }

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

        CaptureHotkey = NormalizeHotkey(CaptureHotkey, DefaultCaptureHotkey);
        GifHotkey = NormalizeHotkey(GifHotkey, DefaultGifHotkey);
        VideoHotkey = NormalizeHotkey(VideoHotkey, DefaultVideoHotkey);
        FullScreenHotkey = NormalizeHotkey(FullScreenHotkey, DefaultFullScreenHotkey);
        AllScreensHotkey = NormalizeHotkey(AllScreensHotkey, DefaultAllScreensHotkey);

        MainWindow?.Normalize();
    }

    /// <summary>返回快捷键重复项的可读提示，无冲突时为 null。</summary>
    public string? FindHotkeyConflict()
    {
        var gestures = new (string Name, string Text)[]
        {
            ("区域截图", CaptureHotkey),
            ("GIF 录制", GifHotkey),
            ("屏幕录制", VideoHotkey),
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
