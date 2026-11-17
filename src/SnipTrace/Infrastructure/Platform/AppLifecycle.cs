using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace SnipTrace.App.Infrastructure.Platform;

/// <summary>单实例：第二次启动唤出已有窗口。</summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\SnipTrace.App.SingleInstance";
    private const string EventName = @"Local\SnipTrace.App.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private Thread? _listener;
    private volatile bool _running = true;

    public bool IsFirstInstance { get; private set; }

    public event Action? ActivationRequested;

    public bool Acquire()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
        if (!createdNew)
        {
            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _listener = new Thread(Listen) { IsBackground = true, Name = "SnipTrace.ActivationListener" };
        _listener.Start();
        return true;
    }

    public void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
            {
                handle.Set();
                handle.Dispose();
            }
        }
        catch (Exception)
        {
        }
    }

    private void Listen()
    {
        while (_running)
        {
            try
            {
                if (_activateEvent?.WaitOne(500) == true) ActivationRequested?.Invoke();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            _activateEvent?.Set();
            _listener?.Join(300);
        }
        catch (Exception)
        {
        }

        _activateEvent?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                if (IsFirstInstance) _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _mutex.Dispose();
        }
    }
}

/// <summary>开机启动：写入当前用户 Run 键，避免写系统级注册表。</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnipTrace";

    /// <summary>开机自启参数：静默启动到托盘，不弹主窗口。</summary>
    public const string StartupArgument = "--startup";

    private static string CommandLine =>
        $"\"{Process.GetCurrentProcess().MainModule?.FileName}\" {StartupArgument}";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return false;
            if (enabled)
            {
                string? processPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(processPath)) return false;
                key.SetValue(ValueName, $"\"{processPath}\" {StartupArgument}");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>启动项是否已经指向当前可执行文件（换过安装位置后需要重写）。</summary>
    public static bool MatchesCurrentExecutable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not string value) return false;
            return string.Equals(value.Trim(), CommandLine.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void EnsureTempDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }
}
