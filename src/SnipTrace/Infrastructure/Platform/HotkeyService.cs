using System;
using System.Collections.Generic;
using System.Windows.Interop;
using SnipTrace.App.Infrastructure.Interop;
using SnipTrace.Domain;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>基于消息窗口的全局快捷键注册，冲突时报告具体手势。</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _ids = new();
    private int _nextId = 0xA000;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("SnipTraceHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(-3)
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action<string>? Triggered;

    public List<string> Failures { get; } = new();

    public bool Register(string key, HotkeyGesture gesture, out string? error)
    {
        error = null;
        uint modifiers = NativeMethods.MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= NativeMethods.MOD_ALT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Win)) modifiers |= NativeMethods.MOD_WIN;

        int id = ++_nextId;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, (uint)gesture.VirtualKey))
        {
            error = $"{gesture.ToDisplayString()} 已被其他程序占用";
            Failures.Add(error);
            return false;
        }

        _ids[id] = key;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _ids.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _ids.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _ids.TryGetValue(wParam.ToInt32(), out string? key))
        {
            handled = true;
            Triggered?.Invoke(key);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
