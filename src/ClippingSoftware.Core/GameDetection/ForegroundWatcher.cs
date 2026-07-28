using System.ComponentModel;
using System.Diagnostics;
using ClippingSoftware.Shared.Interop;

namespace ClippingSoftware.Core.GameDetection;

/// <summary>
/// Watches for foreground-window changes using SetWinEventHook(EVENT_SYSTEM_FOREGROUND) on a dedicated
/// message-pump thread, with a ~1s poll-loop fallback (belt-and-suspenders, per the architecture plan) in
/// case the hook misses an event or fails to install (e.g. under certain security/session contexts).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // Rooted for the lifetime of the watcher so the GC never collects the delegate while native code
    // still holds a pointer to it (a classic SetWinEventHook pitfall).
    private readonly Win32Window.WinEventDelegate _winEventDelegate;

    private readonly object _lock = new();
    private Thread? _hookThread;
    private volatile uint _hookThreadId;
    private Timer? _pollTimer;
    private IntPtr _lastHwnd;
    private bool _started;
    private bool _disposed;

    public event Action<IntPtr, string>? ForegroundChanged;

    public ForegroundWatcher()
    {
        _winEventDelegate = OnWinEvent;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;

            _hookThread = new Thread(HookThreadProc)
            {
                IsBackground = true,
                Name = "ForegroundWatcher-WinEventHook",
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();

            _pollTimer = new Timer(_ => PollForegroundWindow(), null, PollInterval, PollInterval);
        }

        // Report whatever is already focused immediately, rather than waiting for the first change/poll tick.
        PollForegroundWindow();
    }

    private void HookThreadProc()
    {
        _hookThreadId = Win32Window.GetCurrentThreadId();

        var hook = Win32Window.SetWinEventHook(
            Win32Window.EVENT_SYSTEM_FOREGROUND,
            Win32Window.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            Win32Window.WINEVENT_OUTOFCONTEXT | Win32Window.WINEVENT_SKIPOWNPROCESS);

        try
        {
            // WINEVENT_OUTOFCONTEXT delivers callbacks via this thread's message queue, so it needs a pump.
            while (Win32Window.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Win32Window.TranslateMessage(ref msg);
                Win32Window.DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (hook != IntPtr.Zero)
            {
                Win32Window.UnhookWinEvent(hook);
            }
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType != Win32Window.EVENT_SYSTEM_FOREGROUND || hwnd == IntPtr.Zero)
        {
            return;
        }

        HandleForegroundCandidate(hwnd);
    }

    private void PollForegroundWindow()
    {
        var hwnd = Win32Window.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            HandleForegroundCandidate(hwnd);
        }
    }

    private void HandleForegroundCandidate(IntPtr hwnd)
    {
        lock (_lock)
        {
            if (hwnd == _lastHwnd)
            {
                return;
            }
        }

        var processName = TryResolveProcessName(hwnd);
        if (processName is null)
        {
            // Elevated/protected process (common for anti-cheat-guarded games) or the process already
            // exited by the time we looked it up - swallow rather than crash the watcher.
            return;
        }

        lock (_lock)
        {
            if (hwnd == _lastHwnd)
            {
                return;
            }

            _lastHwnd = hwnd;
        }

        ForegroundChanged?.Invoke(hwnd, processName);
    }

    private static string? TryResolveProcessName(IntPtr hwnd)
    {
        try
        {
            Win32Window.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                return null;
            }

            using var process = Process.GetProcessById((int)pid);

            // MainModule.FileName throws Win32Exception (access denied) for elevated/protected processes.
            var fullPath = process.MainModule?.FileName;
            return string.IsNullOrEmpty(fullPath)
                ? process.ProcessName + ".exe"
                : Path.GetFileName(fullPath);
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process exited between GetWindowThreadProcessId and GetProcessById.
            return null;
        }
        catch (ArgumentException)
        {
            // No process with that id (already exited).
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _pollTimer?.Dispose();

        if (_hookThreadId != 0)
        {
            Win32Window.PostThreadMessage(_hookThreadId, Win32Window.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _hookThread?.Join(TimeSpan.FromSeconds(2));
    }
}
